using EditorTextBuffers;
using EditorTextBuffers.Contracts;
using Microsoft.VisualBasic;
using System;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace EditorTextBuffers.PieceTree;

public class PieceTreeTextBuffer : ITextBuffer
{
    private readonly string _BOM;

    private PieceTreeBase _pieceTree;
    private bool _mightContainRTL;
    private bool _mightContainUnusualLineTerminators;
    private bool _mightContainNonBasicASCII;

    public PieceTreeTextBuffer(
        IList<StringBuffer> chunks,
        string bom,
        string eol, // either "\r\n" or "\n"
        bool containsRTL,
        bool containsUnusualLineTerminators,
        bool isBasicASCII,
        bool eolNormalized)
    {
        _BOM = bom;
        _mightContainNonBasicASCII = !isBasicASCII;
        _mightContainRTL = containsRTL;
        _mightContainUnusualLineTerminators = containsUnusualLineTerminators;
        _pieceTree = new PieceTreeBase(chunks, eol, eolNormalized);
    }

    // TODO:
    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public bool Equals(IReadOnlyTextBuffer? other)
    {
        if (other is not PieceTreeTextBuffer otherBuffer)
            return false;

        if (_BOM != otherBuffer._BOM)
            return false;

        if (EOL != otherBuffer.EOL)
            return false;

        return _pieceTree.Equals(otherBuffer._pieceTree);
    }

    #region IReadOnlyTextBuffer Members

    public event EventHandler? OnDpiChangeContent;

    public bool MightContainRTL { get => _mightContainRTL; }

    public bool MightContainUnusualLineTerminators { get => _mightContainUnusualLineTerminators; }

    public void ResetMightContainUnusualLineTerminators()
    {
        _mightContainUnusualLineTerminators = false;
    }

    public bool MightContainNonBasicASCII { get => _mightContainNonBasicASCII; }

    public string BOM { get => _BOM; }

    public string EOL { get => _pieceTree.EOL; }

    public ITextSnapshot CreateSnapshot(bool preserveBOM) => _pieceTree.CreateSnapshot(preserveBOM ? _BOM : "");

    public int GetOffsetAt(int lineNumber, int column) => _pieceTree.GetOffsetAt(lineNumber, column);

    public Position GetPositionAt(int offset) => _pieceTree.GetPositionAt(offset);

    public Range GetRangeAt(int start, int length)
    {
        int end = start + length;
        var startPosition = GetPositionAt(start);
        var endPosition = GetPositionAt(end);
        return new Range(startPosition.LineNumber, startPosition.Column, endPosition.LineNumber, endPosition.Column);
    }

    public string GetValueInRange(Range range, EndOfLinePreference eol = EndOfLinePreference.TextDefined)
    {
        if (range.IsEmpty())
            return "";

        string lineEnding = _getEndOfLine(eol);
        return _pieceTree.GetValueInRange(range, lineEnding);
    }

    public int GetValueLengthInRange(Range range, EndOfLinePreference eol = EndOfLinePreference.TextDefined)
    {
        if (range.IsEmpty())
            return 0;

        if (range.StartLineNumber == range.EndLineNumber)
        {
            return (range.EndColumn - range.StartColumn);
        }

        int startOffset = GetOffsetAt(range.StartLineNumber, range.StartColumn);
        int endOffset = GetOffsetAt(range.EndLineNumber, range.EndColumn);

        // offsets use the text EOL, so we need to compensate for length differences
        // if the requested EOL doesn't match the text EOL
        int eolOffsetCompensation = 0;
        string desiredEOL = _getEndOfLine(eol);
        string actualEOL = EOL;
        if (desiredEOL.Length != actualEOL.Length)
        {
            int delta = desiredEOL.Length - actualEOL.Length;
            int eolCount = range.EndLineNumber - range.StartLineNumber;
            eolOffsetCompensation = delta * eolCount;
        }

        return endOffset - startOffset + eolOffsetCompensation;
    }

    public int GetCharacterCountInRange(Range range, EndOfLinePreference eol)
    {
        if (_mightContainNonBasicASCII)
        {
            // we must count by iterating
            int result = 0;

            int fromLineNumber = range.StartLineNumber;
            int toLineNumber = range.EndLineNumber;
            for (int lineNumber = fromLineNumber; lineNumber <= toLineNumber; lineNumber++)
            {
                string lineContent = GetLineContent(lineNumber);
                int fromOffset = (lineNumber == fromLineNumber ? range.StartColumn - 1 : 0);
                int toOffset = (lineNumber == toLineNumber ? range.EndColumn - 1 : lineContent.Length);

                for (int offset = fromOffset; offset < toOffset; offset++)
                {
                    if (char.IsHighSurrogate(lineContent[offset]))
                    {
                        result++;
                        offset++;
                    }
                    else
                    {
                        result++;
                    }
                }
            }

            result += _getEndOfLine(eol).Length * (toLineNumber - fromLineNumber);

            return result;
        }

        return GetValueLengthInRange(range, eol);
    }

    public int Length { get => _pieceTree.Length; }

    public int LineCount { get => _pieceTree.LineCount; }

    public string GetLineContent(int lineNumber) => _pieceTree.GetLineContent(lineNumber);

    public IReadOnlyList<string> GetLinesContent() => _pieceTree.GetLinesContent();

    public char GetLineCharCode(int lineNumber, int index) => _pieceTree.GetLineCharCode(lineNumber, index);

    public char GetCharCode(int offset) => _pieceTree.GetCharCode(offset);

    public int GetLineLength(int lineNumber) => _pieceTree.GetLineLength(lineNumber);

    public int GetLineMinColumn(int lineNumber) => 1;

    public int GetLineMaxColumn(int lineNumber) => GetLineLength(lineNumber) + 1;

    public int GetLineFirstNonWhitespaceColumn(int lineNumber)
    {
        int result = GetLineContent(lineNumber).FirstNonWhitespaceIndex();
        if (result == -1)
            return 0;
        return result + 1;
    }

    public int GetLineLastNonWhitespaceColumn(int lineNumber)
    {
        int result = GetLineContent(lineNumber).LastNonWhitespaceIndex();
        if (result == -1)
            return 0;
        return result + 2;
    }

    private string _getEndOfLine(EndOfLinePreference eol) => eol switch
    {
        EndOfLinePreference.LF => "\n",
        EndOfLinePreference.CRLF => "\r\n",
        EndOfLinePreference.TextDefined => EOL,
        _ => throw new Exception("Unknown EOL preference"),
    };

    #endregion

    private string GetEOL()
    {
        return _pieceTree.EOL; // either "\r\n" or "\n"
    }

    public event EventHandler? OnDidChangeContent;

    #region ITextBuffer Members (Edit Operations)

    public void SetEOL(string eol)
    {
        _pieceTree.EOL = eol;
    }

    public ApplyEditsResult ApplyEdits(ValidAnnotatedEditOperation[] rawOperations, bool recordTrimAutoWhitespace, bool computeUndoEdits)
    {
        bool mightContainRTL = _mightContainRTL;
        bool mightContainUnusualLineTerminators = _mightContainUnusualLineTerminators;
        bool mightContainNonBasicASCII = _mightContainNonBasicASCII;
        bool canReduceOperations = true;

        var operations = new IValidatedEditOperation[rawOperations.Length];
        for (int i = 0; i < rawOperations.Length; i++)
        {
            ValidAnnotatedEditOperation op = rawOperations[i];
            if (canReduceOperations && op.IsTracked)
                canReduceOperations = false;

            Range validatedRange = op.Range;
            if (!string.IsNullOrEmpty(op.Text))
            {
                bool textMightContainNonBasicASCII = true;
                if (!mightContainNonBasicASCII)
                {
                    textMightContainNonBasicASCII = !op.Text.IsBasicASCII();
                    mightContainNonBasicASCII = textMightContainNonBasicASCII;
                }
                if (!mightContainRTL && textMightContainNonBasicASCII)
                {
                    // check if the new inserted text contains RTL
                    mightContainRTL = op.Text.ContainsRTL();
                }
                if (!mightContainUnusualLineTerminators && textMightContainNonBasicASCII)
                {
                    // check if the new inserted text contains unusual line terminators
                    mightContainUnusualLineTerminators = op.Text.ContainsUnusualLineTerminators();
                }
            }

            string validText = "";
            int eolCount = 0;
            int firstLineLength = 0;
            int lastLineLength = 0;
            if(!string.IsNullOrEmpty(op.Text))
            {
                (eolCount, firstLineLength, lastLineLength, StringEndOfLine strEOL) = 
                    EOLCounter.CountEOL(op.Text);

                string bufferEOL = GetEOL();
                StringEndOfLine expectedStrEOL = (bufferEOL == "\r\n" ? StringEndOfLine.CRLF : StringEndOfLine.LF);
                if (strEOL == StringEndOfLine.Unknown || strEOL == expectedStrEOL)
                    validText = op.Text;
                else
                    validText = Regex.Replace(op.Text, "\r\n|\r|\n", bufferEOL); // TODO: Use generated regex
            }
            operations[i] = new ValidatedEditOperation
            {
                SortIndex = i,
                Identifier = op.Identifier,
                Range = validatedRange,
                RangeOffset = GetOffsetAt(validatedRange.StartLineNumber, validatedRange.StartColumn),
                RangeLength = GetValueLengthInRange(validatedRange),
                Text = validText,
                EOLCount = eolCount,
                FirstLineLength = firstLineLength,
                LastLineLength = lastLineLength,
                ForceMoveMarkers = op.ForceMoveMarkers,
                IsAutoWhitespaceEdit = op.IsAutoWhitespaceEdit
            };
        }

        // Sort operations ascending
        Array.Sort(operations, _sortOpsAscending);

        bool hasTouchingRanges = false;
        for (int i = 0, count = operations.Length - 1; i < count; i++)
        {
            var rangeEnd = operations[i].Range.GetEndPosition();
            var nextRangeStart = operations[i + 1].Range.GetStartPosition();

            if (nextRangeStart.IsBeforeOrEqual(rangeEnd))
            {
                if (nextRangeStart.IsBefore(rangeEnd))
                {
                    // overlapping ranges
                    throw new Exception("Overlapping ranges are not allowed.");
                }
                hasTouchingRanges = true;
            }
        }

        if (canReduceOperations)
            operations = ReduceOperations(operations);

        // Delta encode operations
        Range[] reverseRanges = computeUndoEdits || recordTrimAutoWhitespace
            ? _getInverseEditRanges(operations)
            : [];
        List<(int lineNumber, string oldContent)> newTrimAutoWhitespaceCandidates = [];
        if (recordTrimAutoWhitespace)
        {
            for (int i = 0; i < operations.Length; i++)
            {
                var op = operations[i];
                var reverseRange = reverseRanges[i];

                if (op.IsAutoWhitespaceEdit && op.Range.IsEmpty())
                {
                    // Record already the future line numbers that might be auto whitespace removal candidates on next edit
                    for (int lineNumber = reverseRange.StartLineNumber; lineNumber < reverseRange.EndLineNumber; lineNumber++)
                    {
                        string currentLineContent = "";
                        if (lineNumber == reverseRange.StartLineNumber)
                        {
                            currentLineContent = GetLineContent(op.Range.StartLineNumber);
                            if (currentLineContent.FirstNonWhitespaceIndex() != -1)
                                continue;
                        }
                        newTrimAutoWhitespaceCandidates.Add((lineNumber, currentLineContent));
                    }
                }
            }
        }

        IReverseSingleEditOperation[]? reverseOperations = null;
        if (computeUndoEdits)
        {
            int reverseRangeDeltaOffset = 0;
            reverseOperations = new IReverseSingleEditOperation[operations.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                IValidatedEditOperation op = operations[i];
                Range reverseRange = reverseRanges[i];
                string bufferText = GetValueInRange(op.Range);
                int reverseRangeOffset = op.RangeOffset + reverseRangeDeltaOffset;
                reverseRangeDeltaOffset += op.Text.Length - bufferText.Length;

                reverseOperations[i] = new ReverseSingleEditOperation
                {
                    SortIndex = op.SortIndex,
                    Identifier = op.Identifier,
                    Range = reverseRange,
                    Text = bufferText,
                    TextChange = new TextChange(op.RangeOffset, bufferText, reverseRangeOffset, op.Text)
                };
            }

            // Can only sort reverse operations when the order is not significant
            if (!hasTouchingRanges)
            {
                Array.Sort(reverseOperations, (a, b) => a.SortIndex - b.SortIndex);
            }
        }

        _mightContainRTL = mightContainRTL;
        _mightContainUnusualLineTerminators = mightContainUnusualLineTerminators;
        _mightContainNonBasicASCII = mightContainNonBasicASCII;

        var contentChanges = DoApplyEdits(operations);

        List<int>? trimAutoWhitespaceLineNumbers = null;
        if (recordTrimAutoWhitespace && newTrimAutoWhitespaceCandidates.Count > 0)
        {
            // sort line numbers auto whitespace removal candidates for next edit descending
            newTrimAutoWhitespaceCandidates.Sort((a, b) => b.lineNumber - a.lineNumber);

            trimAutoWhitespaceLineNumbers = [];
            for (int i = 0, len = newTrimAutoWhitespaceCandidates.Count; i < len; i++)
            {
                int lineNumber = newTrimAutoWhitespaceCandidates[i].lineNumber;
                if (i > 0 && newTrimAutoWhitespaceCandidates[i - 1].lineNumber == lineNumber)
                    continue; // Do not have the same line number twice

                string prevContent = newTrimAutoWhitespaceCandidates[i].oldContent;
                string lineContent = GetLineContent(lineNumber);

                if (lineContent.Length == 0 || lineContent == prevContent || lineContent.FirstNonWhitespaceIndex() != -1)
                    continue;

                trimAutoWhitespaceLineNumbers.Add(lineNumber);
            }
        }

        OnDidChangeContent?.Invoke(this, EventArgs.Empty);

        return new ApplyEditsResult
        {
            ReverseEdits = reverseOperations,
            Changes = contentChanges,
            TrimAutoWhitespaceLineNumbers = trimAutoWhitespaceLineNumbers
        };
    }

    #endregion

    /**
     * Transform operations such that they represent the same logic edit,
     * but that they also do not cause OOM crashes.
     */
    private IValidatedEditOperation[] ReduceOperations(IValidatedEditOperation[] operations)
    {
        if (operations.Length < 1000)
            return operations; // We know from empirical testing that a thousand edits work fine regardless of their shape.

        IValidatedEditOperation toSingleEditOperation(IValidatedEditOperation[] operations)
        {
            bool forceMoveMarkers = false;
            Range firstEditRange = operations[0].Range;
            Range lastEditRange = operations[operations.Length - 1].Range;
            Range entireEditRange = new Range(firstEditRange.StartLineNumber, firstEditRange.StartColumn, lastEditRange.EndLineNumber, lastEditRange.EndColumn);
            int lastEndLineNumber = firstEditRange.StartLineNumber;
            int lastEndColumn = firstEditRange.StartColumn;
            List<string> result = [];

            for (int i = 0, len = operations.Length; i < len; i++)
            {
                IValidatedEditOperation operation = operations[i];
                Range range = operation.Range;

                forceMoveMarkers = forceMoveMarkers || operation.ForceMoveMarkers;

                // (1) -- Push old text
                result.Add(GetValueInRange(new Range(lastEndLineNumber, lastEndColumn, range.StartLineNumber, range.StartColumn)));

                // (2) -- Push new text
                if (operation.Text.Length > 0)
                    result.Add(operation.Text);

                lastEndLineNumber = range.EndLineNumber;
                lastEndColumn = range.EndColumn;
            }

            string text = string.Concat(result);
            var (eolCount, firstLineLength, lastLineLength, _) = EOLCounter.CountEOL(text);

            return new ValidatedEditOperation
            {
                SortIndex = 0,
                Identifier = operations[0].Identifier,
                Range = entireEditRange,
                RangeOffset = GetOffsetAt(entireEditRange.StartLineNumber, entireEditRange.StartColumn),
                RangeLength = GetValueLengthInRange(entireEditRange, EndOfLinePreference.TextDefined),
                Text = text,
                EOLCount = eolCount,
                FirstLineLength = firstLineLength,
                LastLineLength = lastLineLength,
                ForceMoveMarkers = forceMoveMarkers,
                IsAutoWhitespaceEdit = false
            };
        }

        // At one point, due to how events are emitted and how each operation is handled,
        // some operations can trigger a high amount of temporary string allocations,
        // that will immediately get edited again.
        // e.g. a formatter inserting ridiculous amounts of \n on a model with a single line
        // Therefore, the strategy is to collapse all the operations into a huge single edit operation
        return [toSingleEditOperation(operations)];
    }

    private IReadOnlyList<IInternalModelContentChange> DoApplyEdits(IValidatedEditOperation[] operations)
    {
        Array.Sort(operations, _sortOpsDescending);
        List<IInternalModelContentChange> contentChanges = [];

        // operations are from bottom to top
        for (int i = 0; i < operations.Length; i++)
        {
            IValidatedEditOperation op = operations[i];

            int startLineNumber = op.Range.StartLineNumber;
            int startColumn = op.Range.StartColumn;
            int endLineNumber = op.Range.EndLineNumber;
            int endColumn = op.Range.EndColumn;

            if (startLineNumber == endLineNumber && startColumn == endColumn && op.Text.Length == 0)
                continue; // no-op

            if (!string.IsNullOrEmpty(op.Text))
            {
                // replacement
                _pieceTree.Delete(op.RangeOffset, op.RangeLength);
                _pieceTree.Insert(op.RangeOffset, op.Text, true);
            }
            else
            {
                // deletion
                _pieceTree.Delete(op.RangeOffset, op.RangeLength);
            }

            Range contentChangeRange = new(startLineNumber, startColumn, endLineNumber, endColumn);
            contentChanges.Add(new InternalModelContentChange
            {
                Range = contentChangeRange,
                RangeLength = op.RangeLength,
                Text = op.Text,
                RangeOffset = op.RangeOffset,
                ForceMoveMarkers = op.ForceMoveMarkers
            });
        }
        return contentChanges;
    }

    public string GetNearestChunk(int offset) => _pieceTree.GetNearestChunk(offset);

    #region Helpers (testing purpose)

    internal PieceTreeBase GetPieceTree() => _pieceTree;

    internal static Range _getInverseEditRange(Range range, string text)
    {
        throw new NotImplementedException();
    }

    internal static Range[] _getInverseEditRanges(IValidatedEditOperation[] operations)
    {
        throw new NotImplementedException();
    }

    /**
     * Assumes `operations` are validated and sorted ascending
     */
    //internal static Range[] _getInverseEditRanges(IValidatedEditOperation[] operations)
    //{
    //    throw new NotImplementedException();
    //}

    private static int _sortOpsAscending(IValidatedEditOperation a, IValidatedEditOperation b)
    {
        int r = Range.CompareRangesUsingEnds(a.Range, b.Range);
        if (r == 0)
            return a.SortIndex - b.SortIndex;
        return r;
    }

    private static int _sortOpsDescending(IValidatedEditOperation a, IValidatedEditOperation b)
    {
        int r = Range.CompareRangesUsingEnds(a.Range, b.Range);
        if (r == 0)
            return b.SortIndex - a.SortIndex;
        return -r;
    }

    #endregion
}
