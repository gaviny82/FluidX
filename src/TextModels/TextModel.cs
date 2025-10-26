using FluidX.TextBuffers;
using FluidX.TextBuffers.PieceTree;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;

namespace FluidX.TextModels;

public class TextModel
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

    public TextModelOptions Options { get; private set; } = new(
        TabSize: 4,
        IndentSize: 4,
        InsertSpaces: true,
        DefaultEOL: DefaultEndOfLine.LF,
        TrimAutoWhitespace: true
    );

    public event TextModelModelContentChangedEventHandler? ContentChanged;

    public EndOfLineSequence EOL => TextBuffer.GetEOL() switch
    {
        "\n" => EndOfLineSequence.LF,
        _ => EndOfLineSequence.CRLF,
    };

    public bool CanUndo => _undoRedoStack.CanUndo;
    public bool CanRedo => _undoRedoStack.CanRedo;

    private readonly UndoRedoStack _undoRedoStack = new();
    private int[]? _trimAutoWhitespaceLineNumbers;

    public TextModel(string source, DefaultEndOfLine eol)
    {
        var builder = new PieceTreeTextBufferBuilder();
        builder.AcceptChunk(source);
        TextBuffer = builder.Finish().Create(eol);
    }

    public void Edit(TextEdit edit)
    {
        // Ported from microsoft/vscode/src/vs/editor/common/model/textModel.ts
        // This is a simplified implementation of TextModel.edit=>pushEditOperations=>_pushEditOperations=>_commandManager.pushEditOperation
        // TODO: Emit events for ContentChanged and DecorationsChanged
        PushEditOperations(edit.Replacements.Select(r => new EditOperation
        {
            Range = r.Range,
            Text = r.Text,
            ForceMoveMarkers = false,
            IsAutoWhitespaceEdit = false,
            IsTracked = false,
        }).ToArray(), null, null);
    }

    public Selection[]? PushEditOperations(
        EditOperation[] editOperations,
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
    /// 
    /// </summary>
    /// <param name="rawOperations"></param>
    /// <param name="computeUndoEdits"></param>
    /// <returns>Not null when <paramref name="computeUndoEdits"/> is true</returns>
    /// <exception cref="NotImplementedException"></exception>
    public ReverseSingleEditOperation[]? ApplyEdits(
        EditOperation[] rawOperations,
        bool computeUndoEdits,
        TextModelEditSource? reason = null,
        bool isUndoing = false,
        bool isRedoing = false)
    {
        reason ??= EditSources.CreateApplyEdits();

        // TODO: Emit events
        int oldLineCount = TextBuffer.LineCount;
        var result = TextBuffer.ApplyEdits(rawOperations, Options.TrimAutoWhitespace, computeUndoEdits);
        int newLineCount = TextBuffer.LineCount;

        var contentChanges = result.Changes;
        _trimAutoWhitespaceLineNumbers = result.TrimAutoWhitespaceLineNumbers?.ToArray();

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
                //this._decorationsTree.acceptReplace(change.rangeOffset, change.rangeLength, change.text.length, change.forceMoveMarkers);
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
                //    TextBuffer.GetOffsetAt(new Position(firstEditLineNumber, 1)),
                //    TextBuffer.GetOffsetAt(new Position(lastInsertedLineNumber, TextBuffer.GetLineMaxColumn(lastInsertedLineNumber))),
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

        return result.ReverseEdits;
    }

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
            return new EditOperation
            {
                Range = new FluidX.TextBuffers.Range(
                    rangeStart.LineNumber,
                    rangeStart.Column,
                    rangeEnd.LineNumber,
                    rangeEnd.Column
                ),
                Text = change.OldText,
                ForceMoveMarkers = false,
                IsAutoWhitespaceEdit = false,
                IsTracked = false
            };
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
            return new EditOperation
            {
                Range = new FluidX.TextBuffers.Range(
                    rangeStart.LineNumber,
                    rangeStart.Column,
                    rangeEnd.LineNumber,
                    rangeEnd.Column
                ),
                Text = change.NewText,
                ForceMoveMarkers = false,
                IsAutoWhitespaceEdit = false,
                IsTracked = false
            };
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
                        Range = new FluidX.TextBuffers.Range(1, 1, endLineNumber, endColumn),
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

    public FluidX.TextBuffers.Range GetFullModelRange()
    {
        int lineCount = TextBuffer.LineCount;
        int endColumn = TextBuffer.GetLineMaxColumn(lineCount);
        return new(1, 1, lineCount, endColumn);
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
