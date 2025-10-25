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
    public ReverseSingleEditOperation[]? ApplyEdits(EditOperation[] rawOperations, bool computeUndoEdits)
    {
        // TODO: Emit events
        int oldLineCount = TextBuffer.LineCount;
        var result = TextBuffer.ApplyEdits(rawOperations, Options.TrimAutoWhitespace, computeUndoEdits);
        int newLineCount = TextBuffer.LineCount;

        var contentChanges = result.Changes;
        _trimAutoWhitespaceLineNumbers = result.TrimAutoWhitespaceLineNumbers?.ToArray();

        if (contentChanges.Count != 0)
        {
            // TODO: Update decorations, injected text and compute event args

            IncreaseVersionId();
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

        ApplyEdits(edits, false);
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

        ApplyEdits(edits, false);
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

        if (TextBuffer.GetEOL() == newEOL) return;

        // Set EOL only if different
        TextBuffer.SetEOL(newEOL);
        IncreaseVersionId();
    }

    private void IncreaseVersionId()
    {
        VersionId++;
        AlternativeVersionId = VersionId;
    }
}

public record struct TextModelOptions(
    int TabSize,
    int IndentSize,
    bool InsertSpaces,
    DefaultEndOfLine DefaultEOL,
    bool TrimAutoWhitespace
);

public class SingleModelEditStackElement : IUndoRedoElement
{
    public TextModel Model { get; }

    public long BeforeVersionId { get; }
    public long AfterVersionId { get; private set; }

    public EndOfLineSequence BeforeEOL { get; }
    public EndOfLineSequence AfterEOL { get; private set; }

    public Selection[]? BeforeCursorState { get; }
    public Selection[]? AfterCursorState { get; private set; }

    public TextChange[] Changes { get; private set; }

    public SingleModelEditStackElement(TextModel model, Selection[]? beforeCursorState)
    {
        Model = model;
        long versionId = model.AlternativeVersionId;
        EndOfLineSequence eol = model.EOL;
        BeforeVersionId = versionId;
        AfterVersionId = versionId;
        BeforeEOL = eol;
        AfterEOL = eol;
        BeforeCursorState = beforeCursorState;
        AfterCursorState = beforeCursorState;
        Changes = [];
    }

    public void Append(
        TextChange[] textChanges,
        EndOfLineSequence afterEOL,
        long afterVersionId,
        Selection[]? afterCursorState)
    {
        if (textChanges.Length > 0)
            Changes = CompressConsecutiveTextChanges(Changes, textChanges);
        AfterEOL = afterEOL;
        AfterVersionId = afterVersionId;
        AfterCursorState = afterCursorState;
    }

    private TextChange[] CompressConsecutiveTextChanges(TextChange[]? prevEdits, TextChange[] currEdits)
    {
        if (prevEdits is null || prevEdits.Length == 0)
            return currEdits;
        var compressor = new TextChangeCompressor(prevEdits, currEdits);
        return compressor.Compress();
    }

    public void Undo()
    {
        Model.ApplyUndo(Changes, BeforeEOL, BeforeVersionId, BeforeCursorState);
    }

    public void Redo()
    {
        Model.ApplyRedo(Changes, AfterEOL, AfterVersionId, AfterCursorState);
    }

    private class TextChangeCompressor
    {
        private TextChange[] _prevEdits;
        private TextChange[] _currEdits;

        private List<TextChange> _result;

        private int _prevLen;
        private int _prevDeltaOffset;

        private int _currLen;
        private int _currDeltaOffset;

        public TextChangeCompressor(TextChange[] prevEdits, TextChange[] currEdits)
        {
            _prevEdits = prevEdits;
            _currEdits = currEdits;

            _result = [];

            _prevLen = _prevEdits.Length;
            _prevDeltaOffset = 0;

            _currLen = _currEdits.Length;
            _currDeltaOffset = 0;
        }

        public TextChange[] Compress()
        {
            int prevIndex = 0;
            int currIndex = 0;

            TextChange? prevEdit = GetPrev(prevIndex);
            TextChange? currEdit = GetCurr(currIndex);

            while (prevIndex < _prevLen || currIndex < _currLen)
            {
                if (prevEdit is null)
                {
                    AcceptCurr(currEdit!);
                    currIndex++;
                    currEdit = GetCurr(currIndex);
                    continue;
                }
                if (currEdit is null)
                {
                    AcceptPrev(prevEdit);
                    prevIndex++;
                    prevEdit = GetPrev(prevIndex);
                    continue;
                }
                if (currEdit.OldEnd <= prevEdit.NewPosition)
                {
                    AcceptCurr(currEdit);
                    currIndex++;
                    currEdit = GetCurr(currIndex);
                    continue;
                }
                if (prevEdit.NewEnd <= currEdit.OldPosition)
                {
                    AcceptPrev(prevEdit);
                    prevIndex++;
                    prevEdit = GetPrev(prevIndex);
                    continue;
                }

                if (currEdit.OldPosition < prevEdit.NewPosition)
                {
                    var (e1, e2) = SplitCurr(currEdit, prevEdit.NewPosition - currEdit.OldPosition);
                    AcceptCurr(e1);
                    currEdit = e2;
                    continue;
                }
                if (prevEdit.NewPosition < currEdit.OldPosition)
                {
                    var (e1, e2) = SplitPrev(currEdit, prevEdit.NewPosition - currEdit.OldPosition);
                    AcceptPrev(e1);
                    prevEdit = e2;
                    continue;
                }

                // At this point, currEdit.oldPosition == prevEdit.newPosition

                TextChange mergePrev;
                TextChange mergeCurr;
                if (currEdit.OldEnd == prevEdit.NewEnd)
                {
                    mergePrev = prevEdit;
                    mergeCurr = currEdit;
                    prevEdit = GetPrev(++prevIndex);
                    currEdit = GetCurr(++currIndex);
                }
                else if (currEdit.OldEnd < prevEdit.NewEnd)
                {
                    var (e1, e2) = SplitPrev(prevEdit, currEdit.OldLength);
                    mergePrev = e1;
                    mergeCurr = currEdit;
                    prevEdit = e2;
                    currEdit = GetCurr(++currIndex);
                }
                else
                {
                    var (e1, e2) = SplitCurr(currEdit, prevEdit.NewLength);
                    mergePrev = prevEdit;
                    mergeCurr = e1;
                    prevEdit = GetPrev(++prevIndex);
                    currEdit = e2;
                }

                _result.Add(new TextChange(
                    mergePrev.OldPosition,
                    mergePrev.OldText,
                    mergeCurr.NewPosition,
                    mergeCurr.NewText
                ));
                _prevDeltaOffset += mergePrev.NewLength - mergePrev.OldLength;
                _currDeltaOffset += mergeCurr.NewLength - mergeCurr.OldLength;
            }

            var merged = Merge(_result);
            var cleaned = RemoveNoOps(merged);
            return cleaned.ToArray();
        }

        private void AcceptCurr(TextChange currEdit)
        {
            _result.Add(RebaseCurr(_prevDeltaOffset, currEdit));
            _currDeltaOffset += currEdit.NewLength - currEdit.OldLength;
        }

        private void AcceptPrev(TextChange prevEdit)
        {
            _result.Add(RebasePrev(_currDeltaOffset, prevEdit));
            _prevDeltaOffset += prevEdit.NewLength - prevEdit.OldLength;
        }

        private TextChange? GetCurr(int currIndex) => currIndex < _currLen ? _currEdits[currIndex] : null;

        private TextChange? GetPrev(int prevIndex) => prevIndex < _prevLen ? _prevEdits[prevIndex] : null;

        private static TextChange RebaseCurr(int prevDeltaOffset, TextChange currEdit) => new TextChange(
            currEdit.OldPosition - prevDeltaOffset,
            currEdit.OldText,
            currEdit.NewPosition,
            currEdit.NewText
        );

        private static TextChange RebasePrev(int currDeltaOffset, TextChange prevEdit) => new TextChange(
            prevEdit.OldPosition,
            prevEdit.OldText,
            prevEdit.NewPosition + currDeltaOffset,
            prevEdit.NewText
        );

        private static (TextChange, TextChange) SplitPrev(TextChange edit, int offset)
        {
            string preText = edit.NewText.Substring(0, offset);
            string postText = edit.NewText.Substring(offset);
            return (
                new TextChange(
                    edit.OldPosition,
                    edit.OldText,
                    edit.NewPosition,
                    preText
                ),
                new TextChange(
                    edit.OldEnd,
                    "",
                    edit.NewPosition + offset,
                    postText
                )
            );
        }

        private static (TextChange, TextChange) SplitCurr(TextChange edit, int offset)
        {
            string preText = edit.OldText.Substring(0, offset);
            string postText = edit.NewText.Substring(offset);
            return (
                new TextChange(
                    edit.OldPosition,
                    preText,
                    edit.NewPosition,
                    edit.NewText
                ),
                new TextChange(
                    edit.OldPosition + offset,
                    postText,
                    edit.NewEnd,
                    ""
                )
            );
        }

        private static List<TextChange> Merge(List<TextChange> edits)
        {
            if (edits.Count == 0)
                return edits;

            List<TextChange> result = [];
            var prev = edits[0];
            for (int i = 1; i < edits.Count; i++)
            {
                var curr = edits[i];
                if (prev.OldEnd == curr.OldPosition)
                {
                    // Merge into `prev`
                    prev = new TextChange(
                        prev.OldPosition,
                        prev.OldText + curr.OldText,
                        prev.NewPosition,
                        prev.NewText + curr.NewText
                    );
                }
                else
                {
                    result.Add(prev);
                    prev = curr;
                }
            }
            result.Add(prev);
            return result;
        }

        private static List<TextChange> RemoveNoOps(List<TextChange> edits)
        {
            if (edits.Count == 0)
                return [];

            List<TextChange> result = [];
            for (int i = 0; i < edits.Count; i++)
            {
                var edit = edits[i];
                if (edit.OldText == edit.NewText)
                    continue;
                result.Add(edit);
            }
            return result;
        }
    }
}

#region Event Data

public enum RawContentChangedType
{
    Flush = 1,
    LineChanged = 2,
    LinesDeleted = 3,
    LinesInserted = 4,
    EOLChanged = 5
}

public abstract class ModelRawChange
{
    public abstract RawContentChangedType Type { get; }
}

/// <summary>
/// An event describing that a model has been reset to a new value.
/// </summary>
public class ModelRawFlush : ModelRawChange
{
    public override RawContentChangedType Type => RawContentChangedType.Flush;
}

/// <summary>
/// An event describing that a line has changed in a model.
/// </summary>
public class ModelRawLineChanged : ModelRawChange
{
    public override RawContentChangedType Type => RawContentChangedType.LineChanged;

    /// <summary>
    /// The line that has changed (1-based)
    /// </summary>
    public int LineNumber { get; }
    /// <summary>
    /// The new value of the line.
    /// </summary>
    public string Detail { get; }

    // TODO: InjectedText

    public ModelRawLineChanged(int lineNumber, string detail)
    {
        LineNumber = lineNumber;
        Detail = detail;
    }
}

public class ModelRawLinesDeleted : ModelRawChange
{
    public override RawContentChangedType Type => RawContentChangedType.LinesDeleted;
    public int FromLineNumber { get; }
    public int ToLineNumber { get; }
    public ModelRawLinesDeleted(int fromLineNumber, int toLineNumber)
    {
        FromLineNumber = fromLineNumber;
        ToLineNumber = toLineNumber;
    }
}

public class ModelRawLinesInserted : ModelRawChange
{
    public override RawContentChangedType Type => RawContentChangedType.LinesInserted;
    public int FromLineNumber { get; }
    public int ToLineNumber { get; }
    public string[] Details { get; }

    // TODO: InjectedText

    public ModelRawLinesInserted(int fromLineNumber, int toLineNumber, string[] details)
    {
        FromLineNumber = fromLineNumber;
        ToLineNumber = toLineNumber;
        Details = details;
    }
}

public class ModelRawContentChangedEventArgs
{
    public ModelRawChange[] Changes { get; }
    public int VersionId { get; }
    public bool IsUndoing { get; }
    public bool IsRedoing { get; }
    public Selection[]? ResultingSelection { get; set; }

    public ModelRawContentChangedEventArgs(ModelRawChange[] changes, int versionId, bool isUndoing, bool isRedoing)
    {
        Changes = changes;
        VersionId = versionId;
        IsUndoing = isUndoing;
        IsRedoing = isRedoing;
        ResultingSelection = null;
    }
}

#endregion

public enum EndOfLineSequence
{
    LF = 0,
    CRLF = 1,
}
