using System.Runtime.InteropServices;

namespace FluidX.TextBuffers.LineArray;

internal class LineArrayTextBuffer : ITextBuffer
{
    private readonly List<List<char>> _lines = [];

    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public bool Equals(IReadOnlyTextBuffer? other)
    {
        if (other is not LineArrayTextBuffer lineArrayTextBuffer)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        // TODO: Check BOM and other properties
        if (_lines.Count != lineArrayTextBuffer._lines.Count)
            return false;

        for (int i = 0; i < _lines.Count; i++)
        {
            ReadOnlySpan<char> thisLineSpan = CollectionsMarshal.AsSpan(_lines[i]);
            ReadOnlySpan<char> otherLineSpan = CollectionsMarshal.AsSpan(lineArrayTextBuffer._lines[i]);

            if (!thisLineSpan.SequenceEqual(otherLineSpan))
                return false;
        }

        return true;
    }

    #region IReadOnlyTextBuffer Members

    public string BOM => throw new NotImplementedException();

    public bool MightContainRTL => throw new NotImplementedException();

    public bool MightContainUnusualLineTerminators => throw new NotImplementedException();

    public bool MightContainNonBasicASCII => throw new NotImplementedException();

    public int Length => throw new NotImplementedException();

    public int LineCount { get => _lines.Count; }

    public event EventHandler? OnDpiChangeContent;

    public ITextSnapshot CreateSnapshot(bool preserveBOM)
    {
        throw new NotImplementedException();
    }

    public int GetCharacterCountInRange(TextRange range, EndOfLinePreference eol)
    {
        throw new NotImplementedException();
    }

    public char GetCharCode(int offset)
    {
        throw new NotImplementedException();
    }

    public char GetLineCharCode(int lineNumber, int index)
    {
        return _lines[lineNumber][index];
    }

    public string GetLineContent(int lineNumber)
    {
        return new string(CollectionsMarshal.AsSpan(_lines[lineNumber]));
    }

    public int GetLineFirstNonWhitespaceColumn(int lineNumber)
    {
        throw new NotImplementedException();
    }

    public int GetLineLastNonWhitespaceColumn(int lineNumber)
    {
        throw new NotImplementedException();
    }

    public int GetLineLength(int lineNumber)
    {
        return _lines[lineNumber].Count;
    }

    public int GetLineMaxColumn(int lineNumber)
    {
        throw new NotImplementedException();
    }

    public int GetLineMinColumn(int lineNumber)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<string> GetLinesContent()
    {
        List<string> lines = [];
        for (int i = 0; i < _lines.Count; i++)
        {
            string line = GetLineContent(i);
            lines.Add(line);
        }
        return lines;
    }

    public int GetOffsetAt(int lineNumber, int column)
    {
        throw new NotImplementedException();
    }

    public TextPosition GetPositionAt(int offset)
    {
        throw new NotImplementedException();
    }

    public TextRange GetRangeAt(int offset, int length)
    {
        throw new NotImplementedException();
    }

    public string GetValueInRange(TextRange range, EndOfLinePreference eol)
    {
        throw new NotImplementedException();
    }

    public int GetValueLengthInRange(TextRange range, EndOfLinePreference eol)
    {
        throw new NotImplementedException();
    }

    public void ResetMightContainUnusualLineTerminators()
    {
        throw new NotImplementedException();
    }

    #endregion

    #region ITextBuffer Members

    public void SetEOL(string eol)
    {
        throw new NotImplementedException();
    }

    public ApplyEditsResult ApplyEdits(EditOperation[] rawOperations, bool recordTrimAutoWhitespace, bool computeUndoEdits)
    {
        throw new NotImplementedException();
    }

    public string GetEOL()
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<FindMatch> FindMatchesLineByLine(TextRange searchRange, SearchData searchData, bool captureMatches, int limitResultCount)
    {
        throw new NotImplementedException();
    }

    #endregion
}
