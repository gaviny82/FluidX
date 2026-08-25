using System.Text;
using FluidX.TextBuffers;
using FluidX.TextBuffers.PieceTree;
using FluidX.Tokenization;

namespace FluidX.TextModels;

public class TextModel : IDecorationTreesHost
{
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

    public GlobalLanguageId LanguageId => Tokenization.LanguageId;

    public TokenizationTextModelPart Tokenization { get; }

    public TextModelOptions Options { get; private set; } = new(
        TabSize: 4,
        IndentSize: 4,
        InsertSpaces: true,
        DefaultEOL: DefaultEndOfLine.LF,
        TrimAutoWhitespace: true
    );

    public bool IsTooLargeForTokenization { get; private init; }

    private const int LargeFileSizeThreshold = 20 * 1024 * 1024; // 20 MB;
    private const int LargeFileLineCountThreshold = 300 * 1000; // 300K lines

    public event TextModelModelContentChangedEventHandler? ContentChanged;

    public EndOfLineSequence EOL => TextBuffer.GetEOL() switch
    {
        "\n" => EndOfLineSequence.LF,
        _ => EndOfLineSequence.CRLF,
    };

    public bool CanUndo => _undoRedoStack.CanUndo;
    public bool CanRedo => _undoRedoStack.CanRedo;

    TextRange IDecorationTreesHost.GetRangeAt(int start, int end)
        => TextBuffer.GetRangeAt(start, end - start);

    private readonly UndoRedoStack _undoRedoStack = new();
    private readonly TextModelDecorationTrees _decorationTrees = new();
    private int[]? _trimAutoWhitespaceLineNumbers;

    public TextModel(
        string source,
        DefaultEndOfLine eol,
        GlobalLanguageId languageId)
    {
        TextBuffer = PieceTreeTextBuffer.Create(source, eol);

        int bufferLineCount = TextBuffer.LineCount;
        int bufferTextLength = TextBuffer.GetValueLengthInRange(new(1, 1, bufferLineCount, TextBuffer.GetLineMaxColumn(bufferLineCount)), EndOfLinePreference.TextDefined);
        IsTooLargeForTokenization = bufferTextLength > LargeFileSizeThreshold || bufferLineCount > LargeFileLineCountThreshold;

        Tokenization = new TokenizationTextModelPart(this, languageId);
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

        // TODO: Emit events
        int oldLineCount = TextBuffer.LineCount;
        bool computeBufferUndoEdits = computeUndoEdits || autoWhitespaceEdits.Count > 0;
        var result = TextBuffer.ApplyEdits(
            editOperations.Select(operation => operation.Replacement).ToArray(),
            computeUndoEdits);
        int newLineCount = TextBuffer.LineCount;

        var contentChanges = result.Changes;
        _trimAutoWhitespaceLineNumbers = ComputeAutoWhitespaceLineNumbers(operations, autoWhitespaceEdits, result.ReverseEdits);

        if (contentChanges.Count != 0)
        {
            // TODO: Update decorations, injected text and compute event args

            // We do a first pass to update decorations
            // because we want to read decorations in the second pass
            // where we will emit content change events
            // and we want to read the final decorations
            for (int i = 0, len = contentChanges.Count; i < len; i++)
            {
                var change = contentChanges[i];
                var operation = operations[change.SortIndex];
                _decorationTrees.AcceptReplace(
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

                int startLineNumber = change.Range.StartLineNumber;
                int endLineNumber = change.Range.EndLineNumber;

                int deletingLinesCnt = endLineNumber - startLineNumber;
                int insertingLinesCnt = eolCount;
                int editingLinesCnt = Math.Min(deletingLinesCnt, insertingLinesCnt);

                int changeLineCountDelta = (insertingLinesCnt - deletingLinesCnt);

                int currentEditStartLineNumber = newLineCount - lineCount - changeLineCountDelta + startLineNumber;
                int firstEditLineNumber = currentEditStartLineNumber;
                int lastInsertedLineNumber = currentEditStartLineNumber + insertingLinesCnt;

                //var decorationsWithInjectedTextInEditedRange = this._decorationsTree.getInjectedTextInInterval(
                //    this,
                //    TextBuffer.GetOffsetAt(new TextPosition(firstEditLineNumber, 1)),
                //    TextBuffer.GetOffsetAt(new TextPosition(lastInsertedLineNumber, TextBuffer.GetLineMaxColumn(lastInsertedLineNumber))),
                //    0
                //);

                //var injectedTextInEditedRange = LineInjectedText.fromDecorations(decorationsWithInjectedTextInEditedRange);
                //var injectedTextInEditedRangeQueue = new ArrayQueue(injectedTextInEditedRange);

                for (int j = editingLinesCnt; j >= 0; j--)
                {
                    int editLineNumber = startLineNumber + j;
                    int currentEditLineNumber = currentEditStartLineNumber + j;

                    //injectedTextInEditedRangeQueue.takeFromEndWhile(r => r.lineNumber > currentEditLineNumber);
                    //var decorationsInCurrentLine = injectedTextInEditedRangeQueue.takeFromEndWhile(r => r.lineNumber === currentEditLineNumber);

                    rawContentChanges.Add(
                        new ModelRawLineChanged(
                            editLineNumber,
                            TextBuffer.GetLineContent(currentEditLineNumber)
                        //decorationsInCurrentLine // Not implemented yet
                        ));
                }

                if (editingLinesCnt < deletingLinesCnt)
                {
                    // Must delete some lines
                    int spliceStartLineNumber = startLineNumber + editingLinesCnt;
                    rawContentChanges.Add(new ModelRawLinesDeleted(spliceStartLineNumber + 1, endLineNumber));
                }

                if (editingLinesCnt < insertingLinesCnt)
                {
                    //var injectedTextInEditedRangeQueue = new ArrayQueue(injectedTextInEditedRange);

                    // Must insert some lines
                    int spliceLineNumber = startLineNumber + editingLinesCnt;
                    int cnt = insertingLinesCnt - editingLinesCnt;
                    int fromLineNumber = newLineCount - lineCount - cnt + spliceLineNumber + 1;
                    //LineInjectedText[]?[] injectedTexts = [];
                    string[] newLines = new string[cnt];
                    for (int j = 0; j < cnt; j++)
                    {
                        int lineNumber = fromLineNumber + i;
                        newLines[j] = TextBuffer.GetLineContent(lineNumber);

                        //injectedTextInEditedRangeQueue.takeWhile(r => r.lineNumber < lineNumber);
                        //injectedTexts[i] = injectedTextInEditedRangeQueue.takeWhile(r => r.lineNumber === lineNumber);
                    }

                    rawContentChanges.Add(
                        new ModelRawLinesInserted(
                            spliceLineNumber + 1,
                            startLineNumber + insertingLinesCnt,
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
                    TextBuffer.GetEOL(),
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
        if (!Options.TrimAutoWhitespace || _trimAutoWhitespaceLineNumbers is null)
            return editOperations;

        int[] trimLineNumbers = _trimAutoWhitespaceLineNumbers;
        _trimAutoWhitespaceLineNumbers = null;

        bool editsAreNearCursors = true;
        if (beforeCursorState is not null)
        {
            foreach (var selection in beforeCursorState)
            {
                int selectionStartLine = Math.Min(selection.SelectionStartLineNumber, selection.PositionLineNumber);
                int selectionEndLine = Math.Max(selection.SelectionStartLineNumber, selection.PositionLineNumber);
                bool foundNearbyEdit = editOperations.Any(operation =>
                    operation.Range.StartLineNumber <= selectionEndLine
                    && operation.Range.EndLineNumber >= selectionStartLine);
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
        foreach (int trimLineNumber in trimLineNumbers)
        {
            int maxLineColumn = TextBuffer.GetLineMaxColumn(trimLineNumber);
            bool allowTrimLine = true;

            foreach (var operation in editOperations)
            {
                TextRange editRange = operation.Range;
                if (trimLineNumber < editRange.StartLineNumber || trimLineNumber > editRange.EndLineNumber)
                    continue;

                bool insertsLineAfter = editRange.IsEmpty
                    && editRange.StartLineNumber == trimLineNumber
                    && editRange.StartColumn == maxLineColumn
                    && StartsWithLineBreak(operation.Text);
                bool insertsLineBefore = editRange.IsEmpty
                    && editRange.StartLineNumber == trimLineNumber
                    && editRange.StartColumn == 1
                    && EndsWithLineBreak(operation.Text);
                if (insertsLineAfter || insertsLineBefore)
                    continue;

                allowTrimLine = false;
                break;
            }

            if (allowTrimLine)
            {
                result.Add(new ModelEditOperation(
                    new TextRange(trimLineNumber, 1, trimLineNumber, maxLineColumn),
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

        for (int i = 0; i < sortedOperations.Length; i++)
        {
            if (sortedOperations[i + 1].Range.StartPosition.IsBefore(sortedOperations[i].Range.EndPosition))
                throw new ArgumentException("Overlapping ranges are not allowed", nameof(operations));
        }

        TextRange firstRange = operations[0].Range;
        TextRange lastRange = operations[^1].Range;
        TextRange combinedRange = new(
            firstRange.StartLineNumber,
            firstRange.StartColumn,
            lastRange.EndLineNumber,
            lastRange.EndColumn);
        int lastEndLineNumber = firstRange.StartLineNumber;
        int lastEndColumn = firstRange.StartColumn;
        bool forceMoveMarkers = false;
        StringBuilder text = new();

        foreach (var operation in sortedOperations)
        {
            TextRange range = operation.Range;
            forceMoveMarkers |= operation.ForceMoveMarkers;
            text.Append(TextBuffer.GetValueInRange(
                new TextRange(
                    lastEndLineNumber,
                    lastEndColumn,
                    range.StartLineNumber,
                    range.StartColumn),
                EndOfLinePreference.TextDefined));
            text.Append(operation.Text);
            lastEndLineNumber = range.EndLineNumber;
            lastEndColumn = range.EndColumn;
        }

        // At one point, due to how events are emitted and how each operation is handled,
        // some operations can trigger a high amount of temporary string allocations,
        // that will immediately get edited again.
        // e.g. a formatter inserting ridiculous amounts of \n on a model with a single line
        // Therefore, the strategy is to collapse all the operations into a huge single edit operation
        return [new ModelEditOperation(combinedRange, text.ToString())
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
                result.Add(new AutoWhitespaceEdit(i, TextBuffer.GetLineContent(operation.Range.StartLineNumber)));
            }
        }

        return result;
    }

    private int[]? ComputeAutoWhitespaceLineNumbers(
        ModelEditOperation[] operations,
        List<AutoWhitespaceEdit> autoWhitespaceEdits,
        ReverseSingleEditOperation[]? reverseOperations)
    {
        if (autoWhitespaceEdits.Count == 0 || reverseOperations is null)
            return null;

        Dictionary<int, ReverseSingleEditOperation> reverseOperationsByIndex =
            reverseOperations.ToDictionary(op => op.SortIndex);
        List<(int LineNumber, string OldContent)> candidates = [];

        foreach (var edit in autoWhitespaceEdits)
        {
            if (!reverseOperationsByIndex.TryGetValue(edit.SortIndex, out var reverseOperation))
                continue;

            for (int lineNumber = reverseOperation.Range.StartLineNumber;
                lineNumber <= reverseOperation.Range.EndLineNumber;
                lineNumber++)
            {
                string oldContent = lineNumber == reverseOperation.Range.StartLineNumber
                    ? edit.OldLineContent
                    : "";
                if (lineNumber == reverseOperation.Range.StartLineNumber && ContainsNonWhitespace(oldContent))
                    continue;
                candidates.Add((lineNumber, oldContent));
            }
        }

        candidates.Sort((a, b) => b.LineNumber - a.LineNumber);
        List<int> result = [];
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (i > 0 && candidates[i - 1].LineNumber == candidate.LineNumber)
                continue;

            string lineContent = TextBuffer.GetLineContent(candidate.LineNumber);
            if (lineContent.Length == 0
                || lineContent == candidate.OldContent
                || ContainsNonWhitespace(lineContent))
            {
                continue;
            }
            result.Add(candidate.LineNumber);
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
                    rangeStart.LineNumber,
                    rangeStart.Column,
                    rangeEnd.LineNumber,
                    rangeEnd.Column
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
                    rangeStart.LineNumber,
                    rangeStart.Column,
                    rangeEnd.LineNumber,
                    rangeEnd.Column
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
        if (TextBuffer.GetEOL() == newEOL) return;

        var oldFullModelRange = GetFullModelRange();
        int oldModelValueLength = TextBuffer.GetValueLengthInRange(oldFullModelRange, EndOfLinePreference.TextDefined);
        int endLineNumber = TextBuffer.LineCount;
        int endColumn = TextBuffer.GetLineMaxColumn(endLineNumber);

        // TODO: OnEOLChanging
        TextBuffer.SetEOL(newEOL);
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
                        Range = new TextRange(1, 1, endLineNumber, endColumn),
                        RangeOffset = 0,
                        RangeLength = oldModelValueLength,
                        Text = GetValue()
                    }
                ],
                TextBuffer.GetEOL(),
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
        int endColumn = TextBuffer.GetLineMaxColumn(lineCount);
        return new(1, 1, lineCount, endColumn);
    }

    public TextPosition ValidatePosition(TextPosition position, bool allowInSurrogatePairs = false)
    {
        int lineNumber = position.LineNumber;
        int column = position.Column;
        int lineCount = TextBuffer.LineCount;

        if (lineNumber < 1)
            return new TextPosition(1, 1);
        if (lineNumber > lineCount)
            return new TextPosition(lineCount, TextBuffer.GetLineMaxColumn(lineCount));
        if (column <= 1)
            return new TextPosition(lineNumber, 1);
        int maxColumn = TextBuffer.GetLineMaxColumn(lineNumber);
        if (column > maxColumn)
            return new TextPosition(lineNumber, maxColumn);

        if (!allowInSurrogatePairs)
        {
            // If the position would end up in the middle of a high-low surrogate pair,
            // we move it to before the pair. At this point, column > 1 is requried.
            char charCodeBefore = TextBuffer.GetLineCharCode(lineNumber, column - 2);
            if (char.IsHighSurrogate(charCodeBefore))
                return new TextPosition(lineNumber, column - 1);
        }

        return position;
    }

    /// <summary>
    /// Get all text
    /// </summary>
    public string GetValue(EndOfLinePreference eol = EndOfLinePreference.TextDefined, bool preserveBOM = false)
    {
        var fullRange = GetFullModelRange();
        var fullText = TextBuffer.GetValueInRange(fullRange, eol);
        string bom = preserveBOM ? TextBuffer.BOM : "";
        return $"{bom}{fullText}";
    }

    private void IncreaseVersionId()
    {
        VersionId++;
        AlternativeVersionId = VersionId;
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
