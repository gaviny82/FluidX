using System.Collections.Immutable;
using FluidX.TextBuffers;
using FluidX.TextBuffers.PersistentPieceTree;

namespace FluidX.TextModels;

/// <summary>
/// A general plain text document model with snapshot-based, per-edit undo/redo history.
/// </summary>
public class TextModel
{
    private readonly ITextBuffer _buffer;
    private readonly TextVersionController _history;

    // Reentrant guards to prevent subscribers from mutating the model during content change events.
    private bool _isMutating;

    #region Document Metadata

    public bool HasBOM
    {
        get;
        set
        {
            EnsureNotMutating();
            field = value;
        }
    }

    public bool MightContainRTL { get; private set; }

    public bool MightContainNonBasicASCII { get; private set; }

    #endregion

    /// <summary>
    /// An immutable read view. All mutations must go through this model.
    /// </summary>
    public IReadOnlyTextBuffer TextBuffer => _buffer.CreateSnapshot();

    /// <summary>
    /// Current document end-of-line state.
    /// </summary>
    /// <remarks>The <see cref="DocumentEndOfLine.Mixed"/> is conservative. It can change to <see cref="DocumentEndOfLine.Unknown"/> if
    /// the document only has one line after a change.
    /// The only way to change from <see cref="DocumentEndOfLine.Mixed"/> to a normalized state is by calling <see cref="NormalizeEOL"/>.
    /// </remarks>
    public DocumentEndOfLine EOL => CurrentRecord.EndOfLine;

    #region Forward Read-Only Version History APIs

    /// <summary>
    /// Version number of the document. Starts from 0 and increases monotonically with each change.
    /// </summary>
    public long VersionId => _history.VersionId;

    /// <summary>
    /// Identifies the current document state. A new record is generated on each change, but undo/redo/jump can return to an earlier record.
    /// </summary>
    public long RecordId => _history.RecordId;

    /// <summary>
    /// The current document state, including a snapshot of the text buffer and the end-of-line state.
    /// </summary>
    public TextRecord CurrentRecord => _history.CurrentRecord;

    /// <summary>
    /// Whether the document can be undone by navigating to the previous record in the retained history.
    /// </summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>
    /// Whether the document can be redone by navigating to the next record in the retained history.
    /// </summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>The latest chronological version, including caller-defined operation metadata.</summary>
    public TextVersion CurrentVersion => _history.CurrentVersion;

    /// <summary>Number of retained chronological versions, including the current version.</summary>
    public int StoredVersionCount => _history.StoredVersionCount;

    /// <summary>Number of retained records preceding the current record.</summary>
    public int HistoryRecordCount => _history.HistoryRecordCount;

    /// <summary>Number of retained records following the current record.</summary>
    public int FutureRecordCount => _history.FutureRecordCount;

    /// <summary>Returns retained versions in chronological order, oldest first.</summary>
    /// <remarks>Versions and records are evicted independently. A version does not retain its referenced records.</remarks>
    public ImmutableArray<TextVersion> GetStoredVersions() => _history.GetStoredVersions();

    /// <summary>Gets a retained version by distance from the latest version; zero selects the current version.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside retained versions.</exception>
    public TextVersion GetStoredVersion(int indexFromCurrent) => _history.GetStoredVersion(indexFromCurrent);

    /// <summary>Returns retained past records, nearest first, excluding the current record.</summary>
    public ImmutableArray<TextRecord> GetHistoryRecords() => _history.GetHistoryRecords();

    /// <summary>Returns retained future records, nearest first, excluding the current record.</summary>
    public ImmutableArray<TextRecord> GetFutureRecords() => _history.GetFutureRecords();

    /// <summary>Gets a retained past record; zero selects the nearest undo record.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside retained past records.</exception>
    public TextRecord GetHistoryRecord(int index) => _history.GetHistoryRecord(index);

    /// <summary>Gets a retained future record; zero selects the nearest redo record.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside retained future records.</exception>
    public TextRecord GetFutureRecord(int index) => _history.GetFutureRecord(index);

    /// <summary>Gets a record by its ID on the retained undo/redo path.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The record is not retained.</exception>
    public TextRecord GetRecord(long recordId) => _history.GetRecord(recordId);

    /// <summary>Returns adjacent transitions in action order between two retained records.</summary>
    /// <remarks>Each transition uses its own source/target coordinates. Equal IDs return an empty array.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Either record is not retained.</exception>
    public ImmutableArray<TextRecordTransition> GetTransitionsBetweenRecords(long fromRecordId, long toRecordId)
        => _history.GetTransitionsBetweenRecords(fromRecordId, toRecordId);

    #endregion

    public event EventHandler<TextModelContentChangedEventArgs>? ContentChanged;

    public TextModel(string source)
    {
        HasBOM = source.Length > 0 && source[0] == char.Utf8Bom;
        if (HasBOM)
            source = source[1..];

        // TODO: When creating the ITextBuffer, document EOL, MightContainNonBasicASCII, and MightContainRTL
        // can be computed at the same time. No need to scan the text twice. This requires a unified ITextBuffer
        // creation interface or factory interface that can return the extra information. We accept the two-pass
        // scan for now and will optimize in the future if needed.
        MightContainNonBasicASCII = !source.IsBasicASCII();
        MightContainRTL = MightContainNonBasicASCII && source.ContainsRTL();

        _buffer = new PersistentPieceTreeTextBuffer(source);
        _history = new TextVersionController(_buffer.CreateSnapshot(), ClassifyEOL(_buffer));
    }

    /// <summary>
    /// Applies a batch of explicit text replacements and returns each input's old and new span.
    /// Validation failure leaves content, history, and notifications untouched. An empty batch
    /// succeeds without committing, clearing redo path, or publishing an event. A non-empty
    /// successful edit commits a new version and record, and publishes <see cref="ContentChanged"/>,
    /// even when the content is unchanged before and after the edit.
    /// </summary>
    /// <remarks>
    /// 1. Ranges of each <see cref="TextReplacement"/> refer to the same pre-edit snapshot.<br/>
    /// 2. Overlapping replacements and insertions inside a replaced range are rejected.<br/>
    /// 3. Invalid endpoints, overlapping edits, return a failed result before mutation.
    /// 4. Edit precedance:<br/>
    ///    4a. Insertions at a replacement start precede its replacement text.<br/>
    ///    4b. Insertions at a replacement end follow the replacement text.<br/>
    ///    4c. Same-position insertions appear in input order.<br/>
    /// 5. Replacement text is applied verbatim.<br/>
    /// 6. Input range endpoints must not lie between CR and LF in an existing CRLF pair.<br/>
    /// 7. Replacement text may begin with LF or end with CR, forming a new CRLF pair with
    ///    surrounding text or another replacement. Deleting text between CR and LF can also
    ///    form a pair.<br/>
    /// 8. Returned edit spans and committed change spans describe exact UTF-16 extents, whose
    ///    new starts or ends may lie inside a newly formed CRLF pair.<br/>
    /// </remarks>
    public TextEditResult ApplyEdits(ReadOnlySpan<TextReplacement> edits, TextModelOperationMetadata? metadata = null)
    {
        EnsureNotMutating();
        _isMutating = true;
        try
        {
            return ApplyEditsCore(edits, metadata);
        }
        finally
        {
            _isMutating = false;
        }
    }

    private TextEditResult ApplyEditsCore(ReadOnlySpan<TextReplacement> edits, TextModelOperationMetadata? metadata)
    {
        TextRecord before = CurrentRecord;
        // An empty input batch is a no-op
        if (edits.IsEmpty)
            return new(before, before, [], null);

        // This checks if line and column indices are within the document. GetLineLength excludes the EOL and also covers checks if the position is inside CRLF.
        static bool IsValidEndpoint(ITextSnapshot snapshot, TextPosition position) =>
            position.LineIndex >= 0 && position.LineIndex < snapshot.LineCount
            && position.ColumnIndex >= 0 && position.ColumnIndex <= snapshot.GetLineLength(position.LineIndex);

        // Capture inputs and validate the complete batch before touching the buffer.
        var prepared = new PreparedEdit[edits.Length];
        for (int i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            if (!IsValidEndpoint(before.Snapshot, edit.Range.StartPosition)
                || !IsValidEndpoint(before.Snapshot, edit.Range.EndPosition))
                return new(before, before, [], "Edit range is outside line content or has an endpoint inside CRLF.");
            prepared[i] = new(i, edit.Range, edit.Text,
                before.Snapshot.GetOffsetAt(edit.Range.StartPosition), before.Snapshot.GetTextLengthInRange(edit.Range));
        }
        Array.Sort(prepared, (a, b) =>
        {
            int order = a.RangeOffset.CompareTo(b.RangeOffset);
            if (order == 0) order = a.RangeLength.CompareTo(b.RangeLength);
            return order != 0 ? order : a.InputIndex.CompareTo(b.InputIndex);
        });
        for (int i = 1; i < prepared.Length; i++)
            if (prepared[i].RangeOffset < prepared[i - 1].RangeOffset + prepared[i - 1].RangeLength)
                return new(before, before, [], "Overlapping edits are not allowed.");

        // Apply validated prepared edits and record change spans
        var spans = ImmutableArray.CreateBuilder<TextChangeSpan>(prepared.Length);
        var inputSpans = ImmutableArray.CreateBuilder<TextChangeSpan>(prepared.Length);
        inputSpans.Count = prepared.Length; // sets count for indexed writes
        int delta = 0;
        for (int i = 0; i < prepared.Length; i++)
        {
            var edit = prepared[i];
            var span = new TextChangeSpan(edit.RangeOffset, edit.RangeLength, edit.RangeOffset + delta, edit.Text.Length);
            spans.Add(span);
            inputSpans[edit.InputIndex] = span;
            delta += edit.Text.Length - edit.RangeLength;
        }
        _buffer.ApplyEdits(prepared.Select(edit => new TextReplacement(edit.Range, edit.Text)).ToArray());

        // Update edit history
        var snapshot = _buffer.CreateSnapshot();
        var historySpans = spans.MoveToImmutable();
        var editSpans = inputSpans.MoveToImmutable();
        _history.CommitEdit(historySpans, snapshot, ClassifyEOLAfterEdit(before, snapshot, prepared, historySpans), metadata);

        // Update document flags
        foreach (var edit in prepared)
            UpdateCharacterFlags(edit.Text);

        // Publish event and return result
        var transition = new TextRecordTransition(before, CurrentRecord, CurrentRecord.ChangeSpans);
        var result = new TextEditResult(before, CurrentRecord, editSpans, null);
        PublishContentChangeEvent([transition]);
        return result;
    }

    private readonly record struct PreparedEdit(
        int InputIndex, TextRange Range, string Text, int RangeOffset, int RangeLength);

    public void Undo(TextModelOperationMetadata? metadata = null)
    {
        EnsureNotMutating();
        if (CanUndo)
            Navigate(TextVersionKind.Undo, _history.GetHistoryRecord(0), metadata);
    }

    public void Redo(TextModelOperationMetadata? metadata = null)
    {
        EnsureNotMutating();
        if (CanRedo)
            Navigate(TextVersionKind.Redo, _history.GetFutureRecord(0), metadata);
    }

    /// <summary>Jumps to a record retained in this model's edit history.</summary>
    /// <remarks>
    /// To recover an unretained record, call <see cref="ReplaceContent"/>
    /// using the saved <see cref="TextRecord.Snapshot"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The record ID does not exist in the retained history.</exception>
    public void JumpToRecord(long recordId, TextModelOperationMetadata? metadata = null)
    {
        EnsureNotMutating();
        Navigate(TextVersionKind.Jump, _history.GetRecord(recordId), metadata);
    }

    // Preconditions:
    // The record must exist in the retrained edit history.
    // Kind must be either Undo, Redo or Jump
    private void Navigate(TextVersionKind kind, TextRecord target, TextModelOperationMetadata? metadata)
    {
        var before = CurrentRecord;
        var currentRecordId = before.RecordId;
        if (target.RecordId == currentRecordId)
            return; // no-op

        _isMutating = true;
        try
        {
            // Both records are retained and distinct, so adjacent transitions are always available.
            var transitions = _history.GetTransitionsBetweenRecords(currentRecordId, target.RecordId);
            // Tries to retore a snapshot if the text buffer supports this.
            // Otherwise, apply a sequence of edits to restore the text buffer to the provided state.
            if (!TryRestoreSnapshot(target.Snapshot))
            {
                foreach (var transition in transitions)
                    _buffer.ApplyEdits(GetTransitionEdits(transition));
            }
            _history.CommitNavigation(kind, target.RecordId, metadata);
            PublishContentChangeEvent(transitions);
        }
        finally
        {
            _isMutating = false;
        }
    }

    // Used for explicit content replacements.
    private void RestoreContent(IReadOnlyTextBuffer content)
    {
        if (content is ITextSnapshot snapshot && TryRestoreSnapshot(snapshot))
            return;
        string text = content.GetTextInRange(content.GetRangeAt(0, content.Length));
        _buffer.ApplyEdits([new(_buffer.GetRangeAt(0, _buffer.Length), text)]);
    }

    /// <summary>Normalize line endings of the whole document as one undoable edit.</summary>
    /// <remarks>
    /// This is a no-op if the current <see cref="TextModel.EOL"/> is <see cref="DocumentEndOfLine.Unknown"/>
    /// or matches <paramref name="eol"/>. Edit history and the redo path is unchanged and no events are published.<br/>
    /// Calls <see cref="ITextBuffer.NormalizeEOL"/> and records one whole-document change span,
    /// without computing a diff. Before/after snapshots preserve the exact content for history
    /// consumers that need more precise changes.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The EOL enum value is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// CR normalization is not yet supported, or a mutation is reentrant.
    /// </exception>
    public void NormalizeEOL(EndOfLine eol, TextModelOperationMetadata? metadata = null)
    {
        var endOfLine = eol switch
        {
            EndOfLine.LF => DocumentEndOfLine.LF,
            EndOfLine.CRLF => DocumentEndOfLine.CRLF,
            // TODO: Support CR normalization once ITextBuffer.NormalizeEOL and its implementations support it.
            EndOfLine.CR => throw new InvalidOperationException("CR normalization is not yet supported by text buffers."),
            _ => throw new ArgumentOutOfRangeException(nameof(eol))
        };

        EnsureNotMutating();
        var before = CurrentRecord;
        if (before.EndOfLine == endOfLine || before.EndOfLine == DocumentEndOfLine.Unknown)
            return;
        _isMutating = true;
        try
        {
            _buffer.NormalizeEOL(eol.AsString());
            var snapshot = _buffer.CreateSnapshot();
            _history.CommitEolNormalization(
                [new(0, before.Snapshot.Length, 0, snapshot.Length)], snapshot, endOfLine, metadata);
            var after = CurrentRecord;
            PublishContentChangeEvent([new(before, after, after.ChangeSpans)]);
        }
        finally
        {
            _isMutating = false;
        }
    }

    /// <summary>
    /// Replaces the entire document with the supplied content.
    /// </summary>
    /// <remarks>
    /// Always commits a new record, even if the content is the same before and after the replacement.
    /// Callers should skip identical content replacement when appropriate.
    /// </remarks>
    public void ReplaceContent(IReadOnlyTextBuffer content, TextModelOperationMetadata? metadata = null)
    {
        EnsureNotMutating();
        var before = CurrentRecord;
        _isMutating = true;
        try
        {
            RestoreContent(content);
            var after = _buffer.CreateSnapshot();
            _history.CommitReplacement(after, ClassifyEOL(after), metadata);
            UpdateCharacterFlags(after.GetTextInRange(after.GetRangeAt(0, after.Length)));
            PublishContentChangeEvent([new(before, CurrentRecord, CurrentRecord.ChangeSpans)]);
        }
        finally
        {
            _isMutating = false;
        }
    }

    /// <summary>
    /// Validates a text position, ensuring it is within the bounds of the document and not in the middle of a surrogate pair unless allowed.
    /// </summary>
    /// <param name="position">The text position to validate.</param>
    /// <param name="allowInSurrogatePairs">Whether to allow positions in the middle of surrogate pairs.</param>
    /// <returns>The nearest valid text position.</returns>
    public TextPosition ValidatePosition(TextPosition position, bool allowInSurrogatePairs = false)
    {
        int lineIndex = position.LineIndex;
        int columnIndex = position.ColumnIndex;
        int lineCount = _buffer.LineCount;

        if (lineIndex < 0)
            return new TextPosition(0, 0);
        if (lineIndex >= lineCount)
            return new TextPosition(lineCount - 1, _buffer.GetLineLength(lineCount - 1));
        if (columnIndex <= 0)
            return new TextPosition(lineIndex, 0);
        int maxColumnIndex = _buffer.GetLineLength(lineIndex);
        if (columnIndex > maxColumnIndex)
            return new TextPosition(lineIndex, maxColumnIndex);

        if (!allowInSurrogatePairs)
        {
            // If the position would end up in the middle of a high-low surrogate pair,
            // we move it to before the pair. At this point, columnIndex > 0 is required.
            char charCodeBefore = _buffer.GetChar(new TextPosition(lineIndex, columnIndex - 1));
            if (char.IsHighSurrogate(charCodeBefore))
                return new TextPosition(lineIndex, columnIndex - 1);
        }

        return position;
    }

    #region Document Read With Optional EOL Normalization

    /// <summary>
    /// Gets all text of the document, optionally normalizing line endings to the specified
    /// <paramref name="eol"/> and preserving the UTF8 byte order mark (BOM) if requested.
    /// </summary>
    /// <param name="eol">The end-of-line sequence to use for normalization, or <see langword="null"> to preserve the existing line endings.</param>
    /// <param name="preserveBOM"><see langword="true"> to include the UTF8 byte order mark (BOM) if it exists, <see langword="false"> otherwise.</param>
    public string GetAllText(EndOfLine? eol = null, bool preserveBOM = false)
    {
        var fullRange = _buffer.GetRangeAt(0, _buffer.Length);
        var fullText = GetTextInRange(fullRange, eol);
        if (preserveBOM && HasBOM)
            return $"{char.Utf8Bom}{fullText}";
        return fullText;
    }

    /// <summary>
    /// Gets text in a specified range, optionally normalizing line endings to the specified <paramref name="eol"/>.
    /// </summary>
    /// <param name="range">The range of text to retrieve.</param>
    /// <param name="eol">The end-of-line sequence to use for normalization, or <see langword="null"> to preserve the existing line endings.</param>
    public string GetTextInRange(TextRange range, EndOfLine? eol = null)
    {
        string text = _buffer.GetTextInRange(range);
        return eol is null ? text : StringExtensions.EndOfLinesRegex.Replace(text, eol.Value.AsString());
    }

    /// <summary>
    /// Gets the UTF-16 length of text in a specified range, optionally normalizing line endings to the specified <paramref name="eol"/>.
    /// </summary>
    /// <param name="range">The range of text to retrieve the length.</param>
    /// <param name="eol">The end-of-line sequence to use for normalization, or <see langword="null"> to preserve the existing line endings.</param>
    public int GetTextLengthInRange(TextRange range, EndOfLine? eol = null)
    {
        if (range.IsEmpty)
            return 0;

        if (range.StartLineIndex == range.EndLineIndex)
            return range.EndColumnIndex - range.StartColumnIndex;

        int rawLength = _buffer.GetTextLengthInRange(range);
        if (eol is null) return rawLength;

        // Compensate for requested EOL normalization.
        if (EOL == DocumentEndOfLine.Unknown)
            return rawLength;
        int desiredEOLLength = eol.Value.AsString().Length;
        int storedEOLLength = EOL switch
        {
            DocumentEndOfLine.CR or DocumentEndOfLine.LF => 1,
            DocumentEndOfLine.CRLF => 2,
            _ => -1 // Mixed
        };
        if (storedEOLLength >= 0)
            return rawLength + (desiredEOLLength - storedEOLLength) * (range.EndLineIndex - range.StartLineIndex);

        int eolOffsetCompensation = 0;
        for (int line = range.StartLineIndex; line < range.EndLineIndex; line++)
            eolOffsetCompensation += desiredEOLLength - _buffer.GetLineEOL(line).Length;

        return rawLength + eolOffsetCompensation;
    }

    /// <summary>
    /// Counts the number of Unicode characters in a specified range, optionally normalizing line endings to the specified <paramref name="eol"/>.
    /// </summary>
    /// <param name="range">The range of text to retrieve the character count.</param>
    /// <param name="eol">The end-of-line sequence to use for normalization, or <see langword="null"> to preserve the existing line endings.</param>
    public int GetCharacterCountInRange(TextRange range, EndOfLine? eol = null)
    {
        string text = GetTextInRange(range, eol);
        int count = 0;
        for (int i = 0; i < text.Length; i++, count++)
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
        return count;
    }

    #endregion

    public IReadOnlyList<FindMatch> FindMatchesLineByLine(
        TextRange searchRange, SearchData searchData, bool captureMatches, int limitResultCount)
        => _buffer.FindMatchesLineByLine(searchRange, searchData, captureMatches, limitResultCount);

    #region Internal Helpers

    private static DocumentEndOfLine ClassifyEOLAfterEdit(TextRecord before, ITextSnapshot after,
        PreparedEdit[] edits, ImmutableArray<TextChangeSpan> spans)
    {
        if (after.LineCount == 1) return DocumentEndOfLine.Unknown;
        if (before.EndOfLine == DocumentEndOfLine.Mixed) return DocumentEndOfLine.Mixed;

        // Conservatively retain the previous kind. Only inserted text and final edit
        // boundaries can introduce a different kind.
        var result = before.EndOfLine;
        for (int i = 0; i < edits.Length; i++)
        {
            string text = edits[i].Text;
            var span = spans[i];
            if (text.Length == 0 && InsideCrLf(after, span.NewPosition))
            {
                if (result != DocumentEndOfLine.Unknown && result != DocumentEndOfLine.CRLF)
                    return DocumentEndOfLine.Mixed;
                result = DocumentEndOfLine.CRLF;
            }
            for (int j = 0; j < text.Length; j++)
            {
                DocumentEndOfLine kind;
                if (text[j] == '\r')
                {
                    bool followedByLf = j + 1 < text.Length ? text[j + 1] == '\n'
                        : span.NewEnd < after.Length && after.GetChar(span.NewEnd) == '\n';
                    kind = followedByLf ? DocumentEndOfLine.CRLF : DocumentEndOfLine.CR;
                    if (followedByLf && j + 1 < text.Length) j++;
                }
                else if (text[j] == '\n')
                {
                    bool precededByCr = j == 0 && span.NewPosition > 0
                        && after.GetChar(span.NewPosition - 1) == '\r';
                    kind = precededByCr ? DocumentEndOfLine.CRLF : DocumentEndOfLine.LF;
                }
                else continue;

                if (result != DocumentEndOfLine.Unknown && result != kind)
                    return DocumentEndOfLine.Mixed;
                result = kind;
            }
        }
        return result;
    }

    private static DocumentEndOfLine ClassifyEOL(IReadOnlyTextBuffer buffer)
    {
        DocumentEndOfLine result = DocumentEndOfLine.Unknown;
        for (int line = 0; line < buffer.LineCount - 1; line++)
        {
            var current = buffer.GetLineEOL(line) switch
            {
                "\r" => DocumentEndOfLine.CR,
                "\n" => DocumentEndOfLine.LF,
                _ => DocumentEndOfLine.CRLF
            };
            if (result != DocumentEndOfLine.Unknown && result != current)
                return DocumentEndOfLine.Mixed;
            result = current;
        }
        return result;
    }

    private void UpdateCharacterFlags(string text)
    {
        MightContainNonBasicASCII |= !text.IsBasicASCII();
        MightContainRTL |= MightContainNonBasicASCII && text.ContainsRTL();
    }

    private void EnsureNotMutating()
    {
        if (_isMutating)
            throw new InvalidOperationException("Reentrant model mutations are not supported.");
    }

    private bool TryRestoreSnapshot(ITextSnapshot snapshot) =>
        _buffer is ISnapshotRestorableTextBuffer restorable && restorable.TryRestoreSnapshot(snapshot);

    private static TextReplacement[] GetTransitionEdits(TextRecordTransition transition)
    {
        var source = transition.Source.Snapshot;
        var target = transition.Target.Snapshot;
        var spans = transition.ChangeSpans;
        var edits = new List<TextReplacement>(spans.Length);
        for (int i = 0; i < spans.Length; i++)
        {
            var first = spans[i];
            var last = first;
            // Undo can put endpoints inside a newly formed CRLF. Expand to include the
            // whole pair, merging touching edits so borrowed characters are unchanged.
            int start = first.OldPosition - (InsideCrLf(source, first.OldPosition) ? 1 : 0);
            int end = last.OldEnd + (InsideCrLf(source, last.OldEnd) ? 1 : 0);
            while (i + 1 < spans.Length)
            {
                var next = spans[i + 1];
                int nextStart = next.OldPosition - (InsideCrLf(source, next.OldPosition) ? 1 : 0);
                if (nextStart > end) break;
                last = spans[++i];
                end = last.OldEnd + (InsideCrLf(source, last.OldEnd) ? 1 : 0);
            }
            int newStart = first.NewPosition - (first.OldPosition - start);
            int newEnd = last.NewEnd + (end - last.OldEnd);
            edits.Add(new(source.GetRangeAt(start, end - start),
                target.GetTextInRange(target.GetRangeAt(newStart, newEnd - newStart))));
        }
        return edits.ToArray();
    }

    private static bool InsideCrLf(IReadOnlyTextBuffer snapshot, int offset) =>
        offset > 0 && offset < snapshot.Length
        && snapshot.GetChar(offset - 1) == '\r' && snapshot.GetChar(offset) == '\n';

    #endregion

    /// <summary>
    /// Called before invoking the public <see cref="ContentChanged"/> event.
    /// </summary>
    protected virtual void OnContentChanged(TextModelContentChangedEventArgs change) { }

    private void PublishContentChangeEvent(ImmutableArray<TextRecordTransition> transitions)
    {
        var change = new TextModelContentChangedEventArgs(_history.CurrentVersion, transitions);
        OnContentChanged(change);
        ContentChanged?.Invoke(this, change);
    }
}
