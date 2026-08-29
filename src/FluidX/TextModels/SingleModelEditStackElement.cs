using FluidX.TextBuffers;

namespace FluidX.TextModels;

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
                    var (e1, e2) = SplitPrev(prevEdit, currEdit.OldPosition - prevEdit.NewPosition);
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
            string postText = edit.OldText.Substring(offset);
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
