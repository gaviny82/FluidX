using System.Text;

namespace FluidX.TextBuffers.PersistentPieceTree;

internal sealed class PersistentPieceTreeSnapshot(PieceNode? root) : ITextSnapshot
{
    internal PieceNode? Root { get; } = root;
    public int Length => Root?.Length ?? 0;
    public int LineCount => (Root?.Summary.Breaks ?? 0) + 1;
    public ITextSnapshot CreateSnapshot() => this;
    public bool Equals(IReadOnlyTextBuffer? other) =>
        other is PersistentPieceTreeSnapshot snapshot && ReferenceEquals(Root, snapshot.Root)
        || TextBufferContentEquality.Equals(this, other);

    private int LineStart(int line)
    {
        if ((uint)line >= (uint)LineCount) throw new ArgumentOutOfRangeException(nameof(line));
        if (line == 0) return 0;
        int start = PieceNode.BreakStart(Root!, line - 1);
        return start + (GetChar(start) == '\r' && start + 1 < Length && GetChar(start + 1) == '\n' ? 2 : 1);
    }

    public int GetOffsetAt(TextPosition position)
    {
        int start = LineStart(position.LineIndex);
        int end = position.LineIndex + 1 == LineCount ? Length : LineStart(position.LineIndex + 1);
        if ((uint)position.ColumnIndex > (uint)(end - start)) throw new ArgumentOutOfRangeException(nameof(position));
        return start + position.ColumnIndex;
    }

    public TextPosition GetPositionAt(int offset)
    {
        if ((uint)offset > (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var prefix = PieceNode.Prefix(Root, offset);
        int line = prefix.Breaks;
        // The boundary inside CRLF still belongs to the preceding line.
        if (offset > 0 && offset < Length && prefix.Last == '\r' && GetChar(offset) == '\n') line--;
        return new(line, offset - LineStart(line));
    }

    public TextRange GetRangeAt(int offset, int length)
    {
        if (length < 0 || offset < 0 || offset > Length - length) throw new ArgumentOutOfRangeException(nameof(length));
        return new(GetPositionAt(offset), GetPositionAt(offset + length));
    }

    public int GetTextLengthInRange(TextRange range) => GetOffsetAt(range.EndPosition) - GetOffsetAt(range.StartPosition);
    public string GetTextInRange(TextRange range)
    {
        int start = GetOffsetAt(range.StartPosition);
        return TextAt(start, GetOffsetAt(range.EndPosition) - start);
    }

    internal string TextAt(int offset, int length)
    {
        if (length < 0 || offset < 0 || offset > Length - length) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0) return string.Empty;
        var result = new StringBuilder(length);
        PieceNode.Append(Root, offset, length, result);
        return result.ToString();
    }

    private (int Start, int ContentEnd, int End) LineBounds(int line)
    {
        int start = LineStart(line);
        int end = line + 1 == LineCount ? Length : LineStart(line + 1);
        int contentEnd = end;
        if (line + 1 < LineCount)
        {
            contentEnd--;
            if (GetChar(contentEnd) == '\n' && contentEnd > start && GetChar(contentEnd - 1) == '\r') contentEnd--;
        }
        return (start, contentEnd, end);
    }

    public IReadOnlyList<string> GetLinesContent()
    {
        var lines = new string[LineCount];
        for (int i = 0; i < lines.Length; i++) lines[i] = GetLineContent(i);
        return lines;
    }

    public string GetLineContent(int lineIndex)
    {
        var (start, end, _) = LineBounds(lineIndex);
        return TextAt(start, end - start);
    }

    public string GetLineEOL(int lineIndex)
    {
        var (_, start, end) = LineBounds(lineIndex);
        return (end - start) switch { 0 => "", 2 => "\r\n", _ => GetChar(start) == '\r' ? "\r" : "\n" };
    }

    public int GetLineLength(int lineIndex)
    {
        var (start, end, _) = LineBounds(lineIndex);
        return end - start;
    }

    public int GetLineFirstNonWhitespaceColumnIndex(int lineIndex)
    {
        string line = GetLineContent(lineIndex);
        for (int i = 0; i < line.Length; i++) if (!char.IsWhiteSpace(line[i])) return i;
        return -1;
    }

    public int GetLineLastNonWhitespaceColumnIndex(int lineIndex)
    {
        string line = GetLineContent(lineIndex);
        for (int i = line.Length - 1; i >= 0; i--) if (!char.IsWhiteSpace(line[i])) return i + 1;
        return -1;
    }

    public char GetChar(TextPosition position)
    {
        int start = LineStart(position.LineIndex);
        int end = position.LineIndex + 1 == LineCount ? Length : LineStart(position.LineIndex + 1);
        if ((uint)position.ColumnIndex >= (uint)(end - start)) throw new ArgumentOutOfRangeException(nameof(position));
        return GetChar(start + position.ColumnIndex);
    }

    public char GetChar(int offset)
    {
        if ((uint)offset >= (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var (piece, start) = PieceNode.Find(Root!, offset);
        return piece.Storage.CharAt(piece.Start + offset - start);
    }

    public IReadOnlyList<FindMatch> FindMatchesLineByLine(TextRange searchRange, SearchData searchData, bool captureMatches, int limitResultCount)
    {
        List<FindMatch> result = [];
        var searcher = new Searcher(searchData.WordSeparators, searchData.Regex);
        for (int lineIndex = searchRange.StartLineIndex; lineIndex <= searchRange.EndLineIndex && result.Count < limitResultCount; lineIndex++)
        {
            string line = GetLineContent(lineIndex);
            int start = Math.Min(lineIndex == searchRange.StartLineIndex ? searchRange.StartColumnIndex : 0, line.Length);
            int end = Math.Min(lineIndex == searchRange.EndLineIndex ? searchRange.EndColumnIndex : line.Length, line.Length);
            string searchedText = line[start..end];
            searcher.Reset(0);
            System.Text.RegularExpressions.Match? match;
            while ((match = searcher.Next(searchedText)) is not null && result.Count < limitResultCount)
                result.Add(SearchUtils.CreateFindMatch(new TextRange(lineIndex, start + match.Index, lineIndex, start + match.Index + match.Length), [match], captureMatches));
        }
        return result;
    }
}
