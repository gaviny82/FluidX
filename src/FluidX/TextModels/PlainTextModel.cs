using System.Collections.Immutable;
using FluidX.TextBuffers;
using FluidX.TextBuffers.PersistentPieceTree;

namespace FluidX.TextModels;

/// <summary>
/// A plain text document model with snapshot-based, per-edit undo/redo history.
/// </summary>
public class PlainTextModel
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
    /// Current document end-of-line state.
    /// </summary>
    public DocumentEndOfLine EOL => CurrentRecord.EndOfLine;

    /// <summary>
    /// Whether the document can be undone by navigating to the previous record in the retained history.
    /// </summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>
    /// Whether the document can be redone by navigating to the next record in the retained history.
    /// </summary>
    public bool CanRedo => _history.CanRedo;

    public event EventHandler<TextModelContentChangedEventArgs>? ContentChanged;

    public PlainTextModel(string source)
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

    public void Edit(TextEdit edit)
    {
        PushEditOperations(
            edit.Replacements.Select(replacement => new ModelEditOperation(replacement)).ToArray(),
            null,
            null
        );
    }

    public Selection[]? PushEditOperations(
        ModelEditOperation[] editOperations,
        Selection[]? beforeCursorState,
        Func<ReverseSingleEditOperation[]?, Selection[]?>? cursorStateComputer
        )
    {
        // Ported from microsoft/vscode/src/vs/editor/common/model/editStack.ts

        // Get or create an edit stack element
        SingleModelEditStackElement editStackElement;
        if (_undoRedoStack.GetLastElement() is SingleModelEditStackElement lastElement)
        {
            editStackElement = lastElement;
        }
        else
        {
            editStackElement = new SingleModelEditStackElement(this, beforeCursorState);
            _undoRedoStack.PushElement(editStackElement);
        }

        editOperations = AppendAutoWhitespaceTrimEdits(editOperations, beforeCursorState);

        // Apply the edits to the text buffer
        var inverseEditOperations = ApplyEdits(editOperations, true)!;

        // Compute the resulting cursor state
        var afterCursorState = cursorStateComputer?.Invoke(inverseEditOperations);

        // Coalesce changes by appending to the edit stack element
        var textChanges = inverseEditOperations
            .Select(e => e.TextChange)
            .Index()
            .ToArray();
        textChanges.Sort((a, b) =>
        {
            if (a.Item.OldPosition == b.Item.OldPosition)
                return a.Index - b.Index;
            return a.Item.OldPosition - b.Item.OldPosition;
        });
        editStackElement.Append(
            textChanges.Select(i => i.Item).ToArray(),
            EOL,
            AlternativeVersionId,
            afterCursorState);

        return afterCursorState;
    }


    /// <summary>
    /// Applies model edit operations without adding them to the undostack.
    /// </summary>
    /// <param name="editOperations">The text replacements and their model-level behavior.</param>
    /// <param name="computeUndoEdits"></param>
    /// <returns>Not null when <paramref name="computeUndoEdits"/> is true</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when a range is outside the document or an endpoint lies inside a CRLF sequence.
    /// </exception>
    public ReverseSingleEditOperation[]? ApplyEdits(
        ModelEditOperation[] editOperations,
        bool computeUndoEdits,
        TextModelEditSource? reason = null,
        bool isUndoing = false,
        bool isRedoing = false)
    {
        reason ??= EditSources.CreateApplyEdits();
        foreach (var edit in editOperations)
        {
            foreach (var position in new[] { edit.Range.StartPosition, edit.Range.EndPosition })
            {
                if (position.LineIndex < 0 || position.LineIndex >= TextBuffer.LineCount
                    || position.ColumnIndex < 0 || position.ColumnIndex > TextBuffer.GetLineLength(position.LineIndex))
                    throw new ArgumentException(
                        "Edit range is outside line content or has an endpoint inside CRLF.",
                        nameof(editOperations));
            }
        }
        ModelEditOperation[] operations = ReduceOperations(editOperations);
        var autoWhitespaceEdits = CaptureAutoWhitespaceEdits(operations);

        // Normalize replacement text to the model's EOL before handing it to
        // the buffer. The buffer applies replacements verbatim, so TextModel
        // owns the EOL policy and keeps the buffer in normalized (fast) mode.
        var replacements = operations
            .Select(operation => new TextReplacement(operation.Range, NormalizeTextEOL(operation.Text)))
            .ToArray();

        // TODO: Emit events
        int oldLineCount = TextBuffer.LineCount;
        bool needReverseEdits = computeUndoEdits || autoWhitespaceEdits.Count > 0;
        var prepared = replacements.Select((replacement, index) => new InternalModelContentChange
        {
            SortIndex = index,
            Range = replacement.Range,
            RangeOffset = TextBuffer.GetOffsetAt(replacement.Range.StartPosition),
            RangeLength = TextBuffer.GetTextLengthInRange(replacement.Range),
            Text = replacement.Text
        }).ToArray();
        Array.Sort(prepared, (a, b) =>
        {
            int order = TextRange.CompareRangesUsingEnds(a.Range, b.Range);
            return order != 0 ? order : a.SortIndex.CompareTo(b.SortIndex);
        });
        bool hasTouchingRanges = false;
        for (int i = 1; i < prepared.Length; i++)
        {
            int previousEnd = prepared[i - 1].RangeOffset + prepared[i - 1].RangeLength;
            if (prepared[i].RangeOffset < previousEnd)
                throw new ArgumentException("Overlapping ranges are not allowed.", nameof(editOperations));
            hasTouchingRanges |= prepared[i].RangeOffset == previousEnd;
        }
        TextChange[]? textChanges = needReverseEdits ? new TextChange[prepared.Length] : null;
        if (textChanges is not null)
        {
            int delta = 0;
            for (int i = 0; i < prepared.Length; i++)
            {
                var edit = prepared[i];
                textChanges[i] = new TextChange(edit.RangeOffset, TextBuffer.GetTextInRange(edit.Range),
                    edit.RangeOffset + delta, edit.Text);
                delta += edit.Text.Length - edit.RangeLength;
            }
        }
        TextBuffer.ApplyEdits(prepared.Select(edit => new TextReplacement(edit.Range, edit.Text)).ToArray());
        ReverseSingleEditOperation[]? reverseEdits = null;
        if (textChanges is not null)
        {
            reverseEdits = textChanges.Select((change, index) => new ReverseSingleEditOperation
            {
                SortIndex = prepared[index].SortIndex,
                Range = TextBuffer.GetRangeAt(change.NewPosition, change.NewLength),
                Text = change.OldText,
                TextChange = change
            }).ToArray();
            if (!hasTouchingRanges)
                Array.Sort(reverseEdits, (a, b) => a.SortIndex.CompareTo(b.SortIndex));
        }
        foreach (TextReplacement replacement in replacements)
        {
            if (!MightContainNonBasicASCII && !replacement.Text.IsBasicASCII())
                MightContainNonBasicASCII = true;
            if (!MightContainRTL && !replacement.Text.IsBasicASCII() && replacement.Text.ContainsRTL())
                MightContainRTL = true;
        }
        int newLineCount = TextBuffer.LineCount;

        var contentChanges = prepared.Reverse().Where(edit => !edit.Range.IsEmpty || edit.Text.Length != 0).ToList();
        _trimAutoWhitespaceLineIndices = ComputeAutoWhitespaceLineIndices(operations, autoWhitespaceEdits, reverseEdits);

        if (contentChanges.Count != 0)
        {
            // TODO: Use events or otherwise to decouple this from PlainTextModel.
            // TODO: Update decorations, injected text and compute event args

            // We do a first pass to update decorations
            // because we want to read decorations in the second pass
            // where we will emit content change events
            // and we want to read the final decorations
            for (int i = 0, len = contentChanges.Count; i < len; i++)
            {
                var change = contentChanges[i];
                var operation = operations[change.SortIndex];
                AcceptDecorationReplace(
                    change.RangeOffset,
                    change.RangeLength,
                    change.Text.Length,
                    operation.ForceMoveMarkers);
            }

            IncreaseVersionId();

            List<ModelRawChange> rawContentChanges = [];
            int lineCount = oldLineCount;
            for (int i = 0; i < contentChanges.Count; i++)
            {
                var change = contentChanges[i];
                int eolCount = EOLCounter.CountEOL(change.Text).eolCount;
                //this._onDidChangeDecorations.fire();

                int startLineIndex = change.Range.StartLineIndex;
                int endLineIndex = change.Range.EndLineIndex;

                int deletingLinesCnt = endLineIndex - startLineIndex;
                int insertingLinesCnt = eolCount;
                int editingLinesCnt = Math.Min(deletingLinesCnt, insertingLinesCnt);

                int changeLineCountDelta = (insertingLinesCnt - deletingLinesCnt);

                int currentEditStartLineIndex = newLineCount - lineCount - changeLineCountDelta + startLineIndex;
                int firstEditLineIndex = currentEditStartLineIndex;
                int lastInsertedLineIndex = currentEditStartLineIndex + insertingLinesCnt;

                //var decorationsWithInjectedTextInEditedRange = this._decorationsTree.getInjectedTextInInterval(
                //    this,
                //    TextBuffer.GetOffsetAt(new TextPosition(firstEditLineIndex, 0)),
                //    TextBuffer.GetOffsetAt(new TextPosition(lastInsertedLineIndex, TextBuffer.GetLineMaxColumnIndex(lastInsertedLineIndex))),
                //    0
                //);

                //var injectedTextInEditedRange = LineInjectedText.fromDecorations(decorationsWithInjectedTextInEditedRange);
                //var injectedTextInEditedRangeQueue = new ArrayQueue(injectedTextInEditedRange);

                for (int j = editingLinesCnt; j >= 0; j--)
                {
                    int editLineIndex = startLineIndex + j;
                    int currentEditLineIndex = currentEditStartLineIndex + j;

                    //injectedTextInEditedRangeQueue.takeFromEndWhile(r => r.lineIndex > currentEditLineIndex);
                    //var decorationsInCurrentLine = injectedTextInEditedRangeQueue.takeFromEndWhile(r => r.lineIndex === currentEditLineIndex);

                    rawContentChanges.Add(
                        new ModelRawLineChanged(
                            editLineIndex,
                            TextBuffer.GetLineContent(currentEditLineIndex)
                        //decorationsInCurrentLine // Not implemented yet
                        ));
                }

                if (editingLinesCnt < deletingLinesCnt)
                {
                    // Must delete some lines
                    int spliceStartLineIndex = startLineIndex + editingLinesCnt;
                    rawContentChanges.Add(new ModelRawLinesDeleted(spliceStartLineIndex + 1, endLineIndex));
                }

                if (editingLinesCnt < insertingLinesCnt)
                {
                    //var injectedTextInEditedRangeQueue = new ArrayQueue(injectedTextInEditedRange);

    public void Undo()
                    {
        EnsureNotMutating();
        if (CanUndo)
            Navigate(TextVersionKind.Undo, _history.GetHistoryRecord(0));
                    }

    public void Redo()
    {
        EnsureNotMutating();
        if (CanRedo)
            Navigate(TextVersionKind.Redo, _history.GetFutureRecord(0));
                }

    /// <summary>Jumps to a record retained in this model's edit history.</summary>
    /// <remarks>
    /// To recover an unretained record, call <see cref="ReplaceContent"/>
    /// using the saved <see cref="TextRecord.Snapshot"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The record ID does not exist in the retained history.</exception>
    public void JumpToRecord(long recordId)
                    {
        EnsureNotMutating();
        Navigate(TextVersionKind.Jump, _history.GetRecord(recordId));
        }

    // Preconditions:
    // The record must exist in the retrained edit history.
    // Kind must be either Undo, Redo or Jump
    private void Navigate(TextVersionKind kind, TextRecord target)
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
            _history.CommitNavigation(kind, target.RecordId);
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
    /// This is a no-op if the current <see cref="PlainTextModel.EOL"/> is <see cref="DocumentEndOfLine.Unknown"/>
    /// or matches <paramref name="eol"/>. Edit history and the redo path is unchanged and no events are published.<br/>
    /// Calls <see cref="ITextBuffer.NormalizeEOL"/> and records one whole-document change span,
    /// without computing a diff. Before/after snapshots preserve the exact content for history
    /// consumers that need more precise changes.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The EOL enum value is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// CR normalization is not yet supported, or a mutation is reentrant.
    /// </exception>
    public void NormalizeEOL(EndOfLine eol)
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
                [new(0, before.Snapshot.Length, 0, snapshot.Length)], snapshot, endOfLine);
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
    public void ReplaceContent(IReadOnlyTextBuffer content)
        {
        EnsureNotMutating();
        var before = CurrentRecord;
        _isMutating = true;
        try
    {
            RestoreContent(content);
            var after = _buffer.CreateSnapshot();
            _history.CommitReplacement(after, ClassifyEOL(after));
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

    /// <summary>
    /// Normalizes the EOL form of a replacement string to the model's preferred
    /// EOL. The text buffers apply replacements verbatim, so this keeps the
    /// buffer content in a single-EOL form and its fast read paths engaged.
    /// </summary>
    private string NormalizeTextEOL(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string preferredEOL = _eol;
        var (_, _, _, actualEOL) = EOLCounter.CountEOL(text);
        StringEndOfLine expectedEOL = preferredEOL == "\r\n" ? StringEndOfLine.CRLF : StringEndOfLine.LF;
        if (actualEOL == StringEndOfLine.Unknown || actualEOL == expectedEOL)
            return text;

    #region Internal Helpers

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
    private static bool InsideCrLf(IReadOnlyTextBuffer snapshot, int offset) =>
        offset > 0 && offset < snapshot.Length
        && snapshot.GetChar(offset - 1) == '\r' && snapshot.GetChar(offset) == '\n';

    #endregion

    /// <summary>
    /// Called before invoking the public <see cref="ContentChanged"/> event.
    /// </summary>
    protected virtual void OnContentChanged(TextModelContentChangedEventArgs change) { }

    private void PublishContentChangeEvent(ImmutableArray<TextRecordTransition> transitions,
        TextModelEditSource? reason = null)
    {
        var change = new TextModelContentChangedEventArgs(_history.CurrentVersion, transitions, reason);
        OnContentChanged(change);
        ContentChanged?.Invoke(this, change);
}
}
