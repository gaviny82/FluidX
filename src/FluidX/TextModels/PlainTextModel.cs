using System.Text;
using FluidX.TextBuffers;
using FluidX.TextBuffers.PieceTree;

namespace FluidX.TextModels;

public class PlainTextModel
{
    private readonly string _bom;
    private string _eol;
    private bool _isEOLNormalized;

    /// <summary>
    /// The underlying text buffer of this text model
    /// </summary>
    public ITextBuffer TextBuffer { get; private set; }

    /// <summary>
    /// Increases monotonically with each change
    /// </summary>
    public long VersionId { get; private set; } = 1;

    /// <summary>
    /// Can be reset to a stored value during undo/redo to revert back to a known baseline
    /// </summary>
    public long AlternativeVersionId { get; private set; } = 1;

    public TextModelOptions Options { get; private set; } = new(
        TabSize: 4,
        IndentSize: 4,
        InsertSpaces: true,
        DefaultEOL: DefaultEndOfLine.LF,
        TrimAutoWhitespace: true
    );

    public event TextModelModelContentChangedEventHandler? ContentChanged;

    public string BOM => _bom;

    public bool MightContainRTL { get; private set; }

    public bool MightContainNonBasicASCII { get; private set; }

    public EndOfLineSequence EOL => _eol switch
    {
        "\n" => EndOfLineSequence.LF,
        _ => EndOfLineSequence.CRLF,
    };

    public bool CanUndo => _undoRedoStack.CanUndo;
    public bool CanRedo => _undoRedoStack.CanRedo;


    private readonly UndoRedoStack _undoRedoStack = new();
    private int[]? _trimAutoWhitespaceLineIndices;

    public PlainTextModel(
        string source,
        DefaultEndOfLine eol)
    {
        if (source.Length > 0 && source[0] == (char)CharCode.UTF8_BOM)
        {
            _bom = ((char)CharCode.UTF8_BOM).ToString();
            source = source[1..];
        }
        else
        {
            _bom = string.Empty;
        }

        _eol = DetermineEOL(source, eol);
        _isEOLNormalized = IsEOLNormalized(source, _eol);
        Options = Options with { DefaultEOL = eol };
        MightContainNonBasicASCII = !source.IsBasicASCII();
        MightContainRTL = MightContainNonBasicASCII && source.ContainsRTL();

        TextBuffer = PieceTreeTextBuffer.Create(source, eol);

    }

    public void Edit(TextEdit edit)
    {
        // Ported from microsoft/vscode/src/vs/editor/common/model/textModel.ts
        // This is a simplified implementation of TextModel.edit=>pushEditOperations=>_pushEditOperations=>_commandManager.pushEditOperation
        // TODO: Emit events for ContentChanged and DecorationsChanged
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
    public ReverseSingleEditOperation[]? ApplyEdits(
        ModelEditOperation[] editOperations,
        bool computeUndoEdits,
        TextModelEditSource? reason = null,
        bool isUndoing = false,
        bool isRedoing = false)
    {
        reason ??= EditSources.CreateApplyEdits();
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
        bool computeBufferUndoEdits = computeUndoEdits || autoWhitespaceEdits.Count > 0;
        var result = TextBuffer.ApplyEdits(replacements, computeBufferUndoEdits);
        foreach (TextReplacement replacement in replacements)
        {
            if (!MightContainNonBasicASCII && !replacement.Text.IsBasicASCII())
                MightContainNonBasicASCII = true;
            if (!MightContainRTL && !replacement.Text.IsBasicASCII() && replacement.Text.ContainsRTL())
                MightContainRTL = true;
        }
        int newLineCount = TextBuffer.LineCount;

        var contentChanges = result.Changes;
        _trimAutoWhitespaceLineIndices = ComputeAutoWhitespaceLineIndices(operations, autoWhitespaceEdits, result.ReverseEdits);

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

        return computeUndoEdits ? result.ReverseEdits : null;
    }

    private ModelEditOperation[] AppendAutoWhitespaceTrimEdits(
        ModelEditOperation[] editOperations,
        Selection[]? beforeCursorState)
    {
        if (!Options.TrimAutoWhitespace || _trimAutoWhitespaceLineIndices is null)
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
            int maxLineColumnIndex = TextBuffer.GetLineLength(trimLineIndex);
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

    public ModelEditOperation[] ReduceOperations(ModelEditOperation[] operations)
    {
        // We know from empirical testing that a thousand edits work fine regardless of their shape.
        if (operations.Length < 1000 || operations.Any(operation => operation.IsTracked))
            return operations;

        ModelEditOperation[] sortedOperations = [.. operations];
        Array.Sort(sortedOperations, (a, b) => TextRange.CompareRangesUsingStarts(a.Range, b.Range));

        for (int i = 0; i < sortedOperations.Length - 1; i++)
        {
            if (sortedOperations[i + 1].Range.StartPosition.IsBefore(sortedOperations[i].Range.EndPosition))
                throw new ArgumentException("Overlapping ranges are not allowed", nameof(operations));
        }

        TextRange firstRange = operations[0].Range;
        TextRange lastRange = operations[^1].Range;
        TextRange combinedRange = new(
            firstRange.StartLineIndex,
            firstRange.StartColumnIndex,
            lastRange.EndLineIndex,
            lastRange.EndColumnIndex);
        int lastEndLineIndex = firstRange.StartLineIndex;
        int lastEndColumnIndex = firstRange.StartColumnIndex;
        bool forceMoveMarkers = false;
        StringBuilder text = new();

        foreach (var operation in sortedOperations)
        {
            TextRange range = operation.Range;
            forceMoveMarkers |= operation.ForceMoveMarkers;
            text.Append(GetValueInRange(
                new TextRange(
                    lastEndLineIndex,
                    lastEndColumnIndex,
                    range.StartLineIndex,
                    range.StartColumnIndex),
                EndOfLinePreference.TextDefined));
            text.Append(operation.Text);
            lastEndLineIndex = range.EndLineIndex;
            lastEndColumnIndex = range.EndColumnIndex;
        }

        // At one point, due to how events are emitted and how each operation is handled,
        // some operations can trigger a high amount of temporary string allocations,
        // that will immediately get edited again.
        // e.g. a formatter inserting ridiculous amounts of \n on a model with a single line
        // Therefore, the strategy is to collapse all the operations into a huge single edit operation
        return [new ModelEditOperation(combinedRange, NormalizeTextEOL(text.ToString()))
            {
                ForceMoveMarkers = forceMoveMarkers
            }
        ];
    }

    private List<AutoWhitespaceEdit> CaptureAutoWhitespaceEdits(ModelEditOperation[] operations)
    {
        List<AutoWhitespaceEdit> result = [];
        if (!Options.TrimAutoWhitespace)
            return result;

        for (int i = 0; i < operations.Length; i++)
        {
            var operation = operations[i];
            if (operation.IsAutowhitespaceEdit && operation.Range.IsEmpty)
            {
                result.Add(new AutoWhitespaceEdit(i, TextBuffer.GetLineContent(operation.Range.StartLineIndex)));
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

            string lineContent = TextBuffer.GetLineContent(candidate.LineIndex);
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
        TextBuffer.ApplyEdits([new TextReplacement(oldFullModelRange, normalizedText)], false);
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

    public TextPosition ValidatePosition(TextPosition position, bool allowInSurrogatePairs = false)
    {
        int lineIndex = position.LineIndex;
        int columnIndex = position.ColumnIndex;
        int lineCount = TextBuffer.LineCount;

        if (lineIndex < 0)
            return new TextPosition(0, 0);
        if (lineIndex >= lineCount)
            return new TextPosition(lineCount - 1, TextBuffer.GetLineLength(lineCount - 1));
        if (columnIndex <= 0)
            return new TextPosition(lineIndex, 0);
        int maxColumnIndex = TextBuffer.GetLineLength(lineIndex);
        if (columnIndex > maxColumnIndex)
            return new TextPosition(lineIndex, maxColumnIndex);

        if (!allowInSurrogatePairs)
        {
            // If the position would end up in the middle of a high-low surrogate pair,
            // we move it to before the pair. At this point, columnIndex > 0 is required.
            char charCodeBefore = TextBuffer.GetChar(new TextPosition(lineIndex, columnIndex - 1));
            if (char.IsHighSurrogate(charCodeBefore))
                return new TextPosition(lineIndex, columnIndex - 1);
        }

        return position;
    }

    /// <summary>
    /// Get all text
    /// </summary>
    public string GetValue(EndOfLinePreference eol = EndOfLinePreference.TextDefined, bool preserveBOM = false)
    {
        var fullRange = GetFullModelRange();
        var fullText = GetValueInRange(fullRange, eol);
        string bom = preserveBOM ? _bom : "";
        return $"{bom}{fullText}";
    }

    public string GetValueInRange(TextRange range, EndOfLinePreference eol = EndOfLinePreference.TextDefined)
    {
        string text = TextBuffer.GetTextInRange(range);
        string requestedEOL = eol switch
        {
            EndOfLinePreference.LF => "\n",
            EndOfLinePreference.CRLF => "\r\n",
            EndOfLinePreference.TextDefined => _eol,
            _ => throw new ArgumentOutOfRangeException(nameof(eol))
        };
        return StringExtensions.EndOfLinesRegex.Replace(text, requestedEOL);
    }

    public int GetValueLengthInRange(TextRange range, EndOfLinePreference eol = EndOfLinePreference.TextDefined)
    {
        if (range.IsEmpty)
            return 0;

        if (range.StartLineIndex == range.EndLineIndex)
            return range.EndColumnIndex - range.StartColumnIndex;

        int rawLength = TextBuffer.GetTextLengthInRange(range);
        string desiredEOL = eol switch
        {
            EndOfLinePreference.LF => "\n",
            EndOfLinePreference.CRLF => "\r\n",
            EndOfLinePreference.TextDefined => _eol,
            _ => throw new ArgumentOutOfRangeException(nameof(eol))
        };

        if (_isEOLNormalized)
        {
            int eolCount = range.EndLineIndex - range.StartLineIndex;
            return rawLength + (desiredEOL.Length - _eol.Length) * eolCount;
        }

        int eolOffsetCompensation = 0;
        for (int line = range.StartLineIndex; line < range.EndLineIndex; line++)
            eolOffsetCompensation += desiredEOL.Length - TextBuffer.GetLineEOL(line).Length;

        return rawLength + eolOffsetCompensation;
    }

    public int GetCharacterCountInRange(TextRange range, EndOfLinePreference eol = EndOfLinePreference.TextDefined)
    {
        string text = GetValueInRange(range, eol);
        int count = 0;
        for (int i = 0; i < text.Length; i++, count++)
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
        return count;
    }

    public ITextSnapshot CreateSnapshot(bool preserveBOM)
        => preserveBOM && _bom.Length > 0
            ? new BOMSnapshot(TextBuffer.CreateSnapshot(false), _bom)
            : TextBuffer.CreateSnapshot(false);

    public IReadOnlyList<FindMatch> FindMatchesLineByLine(
        TextRange searchRange, SearchData searchData, bool captureMatches, int limitResultCount)
        => TextBuffer.FindMatchesLineByLine(searchRange, searchData, captureMatches, limitResultCount);

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

        return StringExtensions.EndOfLinesRegex.Replace(text, preferredEOL);
    }

    private static string DetermineEOL(string text, DefaultEndOfLine defaultEOL)
    {
        int cr = 0, lf = 0, crlf = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (text[i] == '\n') lf++;
        }
        int total = cr + lf + crlf;
        if (total == 0) return defaultEOL == DefaultEndOfLine.LF ? "\n" : "\r\n";
        return cr + crlf > total / 2 ? "\r\n" : "\n";
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
            else if (text[i] == '\n' && eol != "\n")
            {
                return false;
            }
        }
        return true;
    }

    private void IncreaseVersionId()
    {
        VersionId++;
        AlternativeVersionId = VersionId;
    }

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

    private sealed class BOMSnapshot(ITextSnapshot snapshot, string bom) : ITextSnapshot
    {
        private bool _firstRead = true;

        public string? Read()
        {
            string? value = snapshot.Read();
            if (value is null) return null;
            if (!_firstRead) return value;
            _firstRead = false;
            return bom + value;
        }
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
