using System.Text;
using System.Collections.Immutable;
using FluidX.TextBuffers;
using FluidX.TextBuffers.PersistentPieceTree;

namespace FluidX.TextModels;

/// <summary>
/// A plain text document model with snapshot-based, per-edit undo/redo history.
/// </summary>
public class PlainTextModel
{
    private readonly PersistentPieceTreeTextBuffer _buffer;
    private readonly TextVersionController _history;

    // Reentrant guards to prevent subscribers from mutating the model during content change events.
    private bool _isMutating;

    private int[]? _trimAutoWhitespaceLineIndices;

    #region Text Model Options

    /// <summary>
    /// Width of tab stops in columns, used to display \t and alignment.
    /// </summary>
    public int TabSize
    {
        get;
        set
        {
            EnsureNotMutating();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 4;

    /// <summary>
    /// Number of columns in one indentation level for future indent/outdent commands.
    /// </summary>
    public int IndentSize
    {
        get;
        set
        {
            EnsureNotMutating();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 4;

    /// <summary>
    /// Whether future Tab/indent commands should insert spaces instead of literal \t.
    /// </summary>
    public bool InsertSpaces
    {
        get;
        set
        {
            EnsureNotMutating();
            field = value;
        }
    } = true;

    /// <summary>
    /// Whether automatically inserted whitespace is eligible for subsequent cleanup.
    /// </summary>
    public bool TrimAutoWhitespace
    {
        get;
        set
        {
            EnsureNotMutating();
            field = value;
            if (!value) _trimAutoWhitespaceLineIndices = null;
        }
    } = true;

    /// <summary>The end-of-line sequence higher-level editors should insert for Enter.</summary>
    public EndOfLine DefaultEOL
    {
        get => field;
        set
        {
            EnsureNotMutating();
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Invalid enum value.");
            field = value;
        }
    } = EndOfLine.LF;

    #endregion

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

    public PlainTextModel(
        string source,
        EndOfLine defaultEOL)
    {
        HasBOM = source.Length > 0 && source[0] == char.Utf8Bom;
        if (HasBOM)
            source = source[1..];

        // TODO: When creating the ITextBuffer, document EOL, MightContainNonBasicASCII, and MightContainRTL
        // can be computed at the same time. No need to scan the text twice. This requires a unified ITextBuffer
        // creation interface or factory interface that can return the extra information. We accept the two-pass
        // scan for now and will optimize in the future if needed.
        DefaultEOL = defaultEOL;
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

                    // Must insert some lines
                    int spliceLineIndex = startLineIndex + editingLinesCnt;
                    int cnt = insertingLinesCnt - editingLinesCnt;
                    int fromLineIndex = newLineCount - lineCount - cnt + spliceLineIndex + 1;
                    //LineInjectedText[]?[] injectedTexts = [];
                    string[] newLines = new string[cnt];
                    for (int j = 0; j < cnt; j++)
                    {
                        int lineIndex = fromLineIndex + j;
                        newLines[j] = TextBuffer.GetLineContent(lineIndex);

                        //injectedTextInEditedRangeQueue.takeWhile(r => r.lineIndex < lineIndex);
                        //injectedTexts[i] = injectedTextInEditedRangeQueue.takeWhile(r => r.lineIndex === lineIndex);
                    }

                    rawContentChanges.Add(
                        new ModelRawLinesInserted(
                            spliceLineIndex + 1,
                            startLineIndex + insertingLinesCnt,
                            newLines
                        //injectedTexts // Not implemented yet
                        )
                    );
                }

                lineCount += changeLineCountDelta;
            }

            // Fire ContentChanged Event
            OnContentChanged(
                new ModelRawContentChangedEventArgs(
                    rawContentChanges,
                    VersionId,
                    isUndoing,
                    isRedoing
                ),
                new ModelContentChangedEventArgs(
                    contentChanges.Select(c => new ModelContentChange
                    {
                        Range = c.Range,
                        RangeLength = c.RangeLength,
                        RangeOffset = c.RangeOffset,
                        Text = c.Text
                    }).ToList(),
                    _eol,
                    VersionId,
                    isUndoing,
                    isRedoing,
                    false,
                    false,
                    [reason],
                    [contentChanges.Count]
                )
            );
        }

        return computeUndoEdits ? reverseEdits : null;
    }

    private ModelEditOperation[] AppendAutoWhitespaceTrimEdits(
        ModelEditOperation[] editOperations,
        Selection[]? beforeCursorState)
    {
        if (!TrimAutoWhitespace || _trimAutoWhitespaceLineIndices is null)
            return editOperations;

        int[] trimLineIndices = _trimAutoWhitespaceLineIndices;
        _trimAutoWhitespaceLineIndices = null;

        bool editsAreNearCursors = true;
        if (beforeCursorState is not null)
        {
            foreach (var selection in beforeCursorState)
            {
                int selectionStartLine = Math.Min(selection.SelectionStartLineIndex, selection.PositionLineIndex);
                int selectionEndLine = Math.Max(selection.SelectionStartLineIndex, selection.PositionLineIndex);
                bool foundNearbyEdit = editOperations.Any(operation =>
                    operation.Range.StartLineIndex <= selectionEndLine
                    && operation.Range.EndLineIndex >= selectionStartLine);
                if (!foundNearbyEdit)
                {
                    editsAreNearCursors = false;
                    break;
                }
            }
        }

        // If the edits are not near the cursors, we don't trim auto whitespace
        if (!editsAreNearCursors)
            return editOperations;

        List<ModelEditOperation> result = [.. editOperations];
        foreach (int trimLineIndex in trimLineIndices)
        {
            int maxLineColumnIndex = _buffer.GetLineLength(trimLineIndex);
            bool allowTrimLine = true;

            foreach (var operation in editOperations)
            {
                TextRange editRange = operation.Range;
                if (trimLineIndex < editRange.StartLineIndex || trimLineIndex > editRange.EndLineIndex)
                    continue;

                bool insertsLineAfter = editRange.IsEmpty
                    && editRange.StartLineIndex == trimLineIndex
                    && editRange.StartColumnIndex == maxLineColumnIndex
                    && StartsWithLineBreak(operation.Text);
                bool insertsLineBefore = editRange.IsEmpty
                    && editRange.StartLineIndex == trimLineIndex
                    && editRange.StartColumnIndex == 0
                    && EndsWithLineBreak(operation.Text);
                if (insertsLineAfter || insertsLineBefore)
                    continue;

                allowTrimLine = false;
                break;
            }

            if (allowTrimLine)
            {
                result.Add(new ModelEditOperation(
                    new TextRange(trimLineIndex, 0, trimLineIndex, maxLineColumnIndex),
                    ""));
            }
        }

        return result.ToArray();
    }

    private List<AutoWhitespaceEdit> CaptureAutoWhitespaceEdits(ModelEditOperation[] operations)
    {
        List<AutoWhitespaceEdit> result = [];
        if (!TrimAutoWhitespace)
            return result;

        for (int i = 0; i < operations.Length; i++)
        {
            var operation = operations[i];
            if (operation.IsAutowhitespaceEdit && operation.Range.IsEmpty)
            {
                result.Add(new AutoWhitespaceEdit(i, _buffer.GetLineContent(operation.Range.StartLineIndex)));
            }
        }

        return result;
    }

    private int[]? ComputeAutoWhitespaceLineIndices(
        ModelEditOperation[] operations,
        List<AutoWhitespaceEdit> autoWhitespaceEdits,
        ReverseSingleEditOperation[]? reverseOperations)
    {
        if (autoWhitespaceEdits.Count == 0 || reverseOperations is null)
            return null;

        Dictionary<int, ReverseSingleEditOperation> reverseOperationsByIndex =
            reverseOperations.ToDictionary(op => op.SortIndex);
        List<(int LineIndex, string OldContent)> candidates = [];

        foreach (var edit in autoWhitespaceEdits)
        {
            if (!reverseOperationsByIndex.TryGetValue(edit.SortIndex, out var reverseOperation))
                continue;

            for (int lineIndex = reverseOperation.Range.StartLineIndex;
                lineIndex <= reverseOperation.Range.EndLineIndex;
                lineIndex++)
            {
                string oldContent = lineIndex == reverseOperation.Range.StartLineIndex
                    ? edit.OldLineContent
                    : "";
                if (lineIndex == reverseOperation.Range.StartLineIndex && ContainsNonWhitespace(oldContent))
                    continue;
                candidates.Add((lineIndex, oldContent));
            }
        }

        candidates.Sort((a, b) => b.LineIndex - a.LineIndex);
        List<int> result = [];
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (i > 0 && candidates[i - 1].LineIndex == candidate.LineIndex)
                continue;

            string lineContent = _buffer.GetLineContent(candidate.LineIndex);
            if (lineContent.Length == 0
                || lineContent == candidate.OldContent
                || ContainsNonWhitespace(lineContent))
            {
                continue;
            }
            result.Add(candidate.LineIndex);
        }

        return result.Count == 0 ? null : result.ToArray();
    }

    private static bool ContainsNonWhitespace(string text)
        => text.Any(ch => ch is not (' ' or '\t'));

    private static bool StartsWithLineBreak(string text)
        => text.StartsWith('\n') || text.StartsWith("\r");

    private static bool EndsWithLineBreak(string text)
        => text.EndsWith('\n') || text.EndsWith("\r");

    private readonly record struct AutoWhitespaceEdit(int SortIndex, string OldLineContent);

    public void Undo()
    {
        if (_undoRedoStack.GetClosestPastElement() is not IUndoRedoElement element)
            return;
        _undoRedoStack.MoveBackward(element);
        element.Undo();
    }

    public void ApplyUndo(TextChange[] changes, EndOfLineSequence eol, long resultingAltVersionId, Selection[]? resultingSelection)
    {
        var edits = changes.Select(change =>
        {
            var rangeStart = TextBuffer.GetPositionAt(change.NewPosition);
            var rangeEnd = TextBuffer.GetPositionAt(change.NewEnd);
            return new ModelEditOperation(
                new TextRange(
                    rangeStart.LineIndex,
                    rangeStart.ColumnIndex,
                    rangeEnd.LineIndex,
                    rangeEnd.ColumnIndex
                ),
                change.OldText);
        }).ToArray();

        // TODO: Emit events

        ApplyEdits(edits, false, null, true, false);
        SetEOL(eol);
        AlternativeVersionId = resultingAltVersionId;
    }

    public void ApplyRedo(TextChange[] changes, EndOfLineSequence eol, long resultingAltVersionId, Selection[]? resultingSelection)
    {
        var edits = changes.Select(change =>
        {
            var rangeStart = TextBuffer.GetPositionAt(change.OldPosition);
            var rangeEnd = TextBuffer.GetPositionAt(change.OldEnd);
            return new ModelEditOperation(
                new TextRange(
                    rangeStart.LineIndex,
                    rangeStart.ColumnIndex,
                    rangeEnd.LineIndex,
                    rangeEnd.ColumnIndex
                ),
                change.NewText);
        }).ToArray();

        // TODO: Emit events

        ApplyEdits(edits, false, null, false, true);
        SetEOL(eol);
        AlternativeVersionId = resultingAltVersionId;
    }

    public void Redo()
    {
        if (_undoRedoStack.GetClosestFutureElement() is not IUndoRedoElement element)
            return;
        _undoRedoStack.MoveForward(element);
        element.Redo();
    }

    public void SetEOL(EndOfLineSequence eol)
    {
        string newEOL = eol switch
        {
            EndOfLineSequence.LF => "\n",
            _ => "\r\n",
        };

        // Set EOL only if different
        if (_eol == newEOL) return;

        var oldFullModelRange = GetFullModelRange();
        int oldModelValueLength = TextBuffer.GetTextLengthInRange(oldFullModelRange);
        int endLineIndex = TextBuffer.LineCount - 1;
        int endColumnIndex = TextBuffer.GetLineLength(endLineIndex);

        // TODO: OnEOLChanging
        string normalizedText = StringExtensions.EndOfLinesRegex.Replace(
            TextBuffer.GetTextInRange(oldFullModelRange), newEOL);
        TextBuffer.ApplyEdits([new TextReplacement(oldFullModelRange, normalizedText)]);
        _eol = newEOL;
        _isEOLNormalized = true;
        IncreaseVersionId();
        // TODO: OnEOLChanged

        OnContentChanged(
            new ModelRawContentChangedEventArgs(
                [new ModelRawEOLChanged()],
                VersionId,
                false,
                false
            ),
            new ModelContentChangedEventArgs(
                Changes: [
                    new ModelContentChange
                    {
                        Range = new TextRange(0, 0, endLineIndex, endColumnIndex),
                        RangeOffset = 0,
                        RangeLength = oldModelValueLength,
                        Text = GetValue()
                    }
                ],
                _eol,
                VersionId: VersionId,
                IsUndoing: false,
                IsRedoing: false,
                IsFlush: false,
                IsEolChange: true,
                DetailedReasons: [EditSources.CreateEOLChange()],
                DetailedReasonsChangeLengths: [1]
            )
        );
    }

    public TextRange GetFullModelRange()
    {
        int lineCount = TextBuffer.LineCount;
        int endColumnIndex = TextBuffer.GetLineLength(lineCount - 1);
        return new(0, 0, lineCount - 1, endColumnIndex);
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

    private static bool IsEOLNormalized(string text, string eol)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (eol != "\r\n" || i + 1 >= text.Length || text[i + 1] != '\n')
                    return false;
                i++;
            }

    #region Helpers

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

    #endregion

    /// <summary>
    /// Allows specialized models to keep model-specific decorations in sync
    /// with text edits. Plain text models do not maintain decorations.
    /// </summary>
    protected virtual void AcceptDecorationReplace(
        int offset,
        int length,
        int textLength,
        bool forceMoveMarkers)
    {
    }

    private void OnContentChanged(
        ModelRawContentChangedEventArgs rawChange,
        ModelContentChangedEventArgs change)
    {
        ContentChanged?.Invoke(new(rawChange, change));
    }

}

public record struct TextModelOptions(
    int TabSize,
    int IndentSize,
    bool InsertSpaces,
    DefaultEndOfLine DefaultEOL,
    bool TrimAutoWhitespace
);

public enum EndOfLineSequence
{
    LF = 0,
    CRLF = 1,
}
