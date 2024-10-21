using EditorTextBuffers.Contracts;
using System.Reflection.Emit;

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

    public string GetValueInRange(Range range, EndOfLinePreference eol)
    {
        if (range.IsEmpty())
            return "";

        string lineEnding = _getEndOfLine(eol);
        return _pieceTree.GetValueInRange(range, lineEnding);
    }

    public int GetValueLengthInRange(Range range, EndOfLinePreference eol)
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

    #region ITextBuffer Members (Edit Operations)

    public void SetEOL(string eol)
    {
        _pieceTree.EOL = eol;
    }

    #endregion

    public string GetNearestChunk(int offset) => _pieceTree.GetNearestChunk(offset);

    #region Helpers (testing purpose)

    internal PieceTreeBase GetPieceTree() => _pieceTree;

    internal static Range _getInverseEditRange(Range range, string text)
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

    //private static int _sortOpsAscending(a: IValidatedEditOperation, b: IValidatedEditOperation)
    //{
    //    throw new NotImplementedException();
    //}

    //private static int _sortOpsDescending(a: IValidatedEditOperation, b: IValidatedEditOperation)
    //{
    //    throw new NotImplementedException();
    //}

    #endregion
}
