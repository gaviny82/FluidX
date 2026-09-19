namespace FluidX.TextBuffers.LineArray;

/// <summary>
/// An immutable snapshot that reuses line-array reads through an exclusively owned buffer copy.
/// </summary>
/// <remarks>
/// Construction copies the outer list and every character list. The private buffer is never
/// exposed or mutated, and its reads have no mutable caches, so concurrent snapshot reads
/// need no locks, even while the source is edited. Callers must exclude source mutation during
/// construction. Capture costs O(characters + lines).
/// </remarks>
internal sealed class FrozenTextSnapshot : ITextSnapshot
{
    private readonly LineArrayTextBuffer _buffer;

    internal FrozenTextSnapshot(LineArrayTextBuffer source)
    {
        _buffer = new LineArrayTextBuffer(source);
    }

    public int Length => _buffer.Length;
    public int LineCount => _buffer.LineCount;
    public ITextSnapshot CreateSnapshot() => this;
    public bool Equals(IReadOnlyTextBuffer? other) => _buffer.Equals(other);

    public int GetOffsetAt(TextPosition position) => _buffer.GetOffsetAt(position);
    public TextPosition GetPositionAt(int offset) => _buffer.GetPositionAt(offset);
    public TextRange GetRangeAt(int offset, int length) => _buffer.GetRangeAt(offset, length);
    public string GetTextInRange(TextRange range) => _buffer.GetTextInRange(range);
    public int GetTextLengthInRange(TextRange range) => _buffer.GetTextLengthInRange(range);
    public IReadOnlyList<string> GetLinesContent() => _buffer.GetLinesContent();
    public string GetLineContent(int lineIndex) => _buffer.GetLineContent(lineIndex);
    public string GetLineEOL(int lineIndex) => _buffer.GetLineEOL(lineIndex);
    public int GetLineLength(int lineIndex) => _buffer.GetLineLength(lineIndex);
    public int GetLineFirstNonWhitespaceColumnIndex(int lineIndex)
        => _buffer.GetLineFirstNonWhitespaceColumnIndex(lineIndex);
    public int GetLineLastNonWhitespaceColumnIndex(int lineIndex)
        => _buffer.GetLineLastNonWhitespaceColumnIndex(lineIndex);
    public char GetChar(TextPosition position) => _buffer.GetChar(position);
    public char GetChar(int offset) => _buffer.GetChar(offset);
    public IReadOnlyList<FindMatch> FindMatchesLineByLine(
        TextRange searchRange, SearchData searchData, bool captureMatches, int limitResultCount)
        => _buffer.FindMatchesLineByLine(searchRange, searchData, captureMatches, limitResultCount);
}
