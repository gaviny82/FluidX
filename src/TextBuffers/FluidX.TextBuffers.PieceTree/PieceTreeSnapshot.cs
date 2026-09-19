using FluidX.TextBuffers.PieceTree.Buffers;

namespace FluidX.TextBuffers.PieceTree;

using TreeNode = FluidX.Common.DataStructures.RbTrees.RedBlackTree<PieceNodeData>.TreeNode;

/// <summary>
/// An immutable copy of a piece tree's structure, sharing its append-only character storage.
/// </summary>
/// <remarks>
/// Capture costs O(pieces + buffers) time and space and does not copy characters or line-start
/// arrays. Nodes, buffer descriptors, and their captured array references/counts belong to this
/// snapshot. Reads use only frozen state, with no mutable caches or source-buffer references,
/// so concurrent readers need no locks. Source mutation must be excluded during capture.
/// </remarks>
public sealed class PieceTreeSnapshot : ITextSnapshot
{
    // Snapshot nodes deliberately have no editing operations, parent pointers, or sentinel.
    // Copy the existing balanced shape and subtree metadata without changing RedBlackTree.
    private sealed record Node(Piece Piece, int SizeLeft, int LfLeft, Node? Left, Node? Right);

    private readonly Node? _root;
    private readonly InlineStringBuffer[] _buffers;

    // Only buffer 0 is appendable. LF following its trailing CR overwrites the final existing
    // line-start slot. Value-copying descriptors freezes counts/array references, but shares
    // that slot. Preserve its VALUE (not its index); all snapshot metadata access goes through
    // GetLineStartInBuffer. Earlier entries never change and later entries lie outside the
    // captured count, so this single int suffices across repeated appends and reallocations.
    // FUTURE: Make string buffers, including line-start metadata, strictly append-only and
    // remove this workaround. This is an internal storage exception, never a snapshot exception.
    private readonly int _changeBufferLastLineStart;

    internal PieceTreeSnapshot(PieceTree tree, StringBufferCollection buffers, int length, int lineCount)
    {
        _root = Copy(tree.Root);
        _buffers = new InlineStringBuffer[buffers.Count];
        for (int i = 0; i < _buffers.Length; i++)
            _buffers[i] = buffers[i];
        _changeBufferLastLineStart = _buffers[0].LineStarts[^1];
        Length = length;
        LineCount = lineCount;
    }

    private static Node? Copy(TreeNode node) => node.IsSentinel ? null : new(
        node.Piece, node.SizeLeft, node.LfLeft, Copy(node.Left), Copy(node.Right));

    public int Length { get; }
    public int LineCount { get; }
    public ITextSnapshot CreateSnapshot() => this;
    public bool Equals(IReadOnlyTextBuffer? other) => TextBufferContentEquality.Equals(this, other);

    private int GetLineStartInBuffer(int bufferIndex, int lineIndex)
    {
        var starts = _buffers[bufferIndex].LineStarts;
        return bufferIndex == 0 && lineIndex == starts.Length - 1
            ? _changeBufferLastLineStart : starts[lineIndex];
    }

    private int GetPieceStart(Piece piece)
        => GetLineStartInBuffer(piece.BufferIndex, piece.Start.LineIndex) + piece.Start.ColumnIndex;

    // Select the preceding line break using the copied tree's aggregate line counts.
    // A break ending at a piece boundary may end inside the backing buffer's CRLF; in that
    // case the piece end cursor, rather than the following backing line start, is authoritative.
    private int GetLineStart(int lineIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lineIndex, LineCount);
        int offset = 0;
        Node? node = _root;
        while (node is not null)
        {
            if (node.Left is not null && node.LfLeft >= lineIndex)
            {
                node = node.Left;
            }
            else if (node.LfLeft + node.Piece.LineFeedCount >= lineIndex)
            {
                offset += node.SizeLeft;
                int relativeLine = lineIndex - node.LfLeft;
                if (relativeLine == 0) return offset;
                Piece piece = node.Piece;
                int backingLine = piece.Start.LineIndex + relativeLine;
                int end = backingLine > piece.End.LineIndex
                    ? GetLineStartInBuffer(piece.BufferIndex, piece.End.LineIndex) + piece.End.ColumnIndex
                    : GetLineStartInBuffer(piece.BufferIndex, backingLine);
                return offset + end - GetPieceStart(piece);
            }
            else
            {
                lineIndex -= node.LfLeft + node.Piece.LineFeedCount;
                offset += node.SizeLeft + node.Piece.Length;
                node = node.Right;
            }
        }
        return offset;
    }

    public int GetOffsetAt(TextPosition position)
    {
        int start = GetLineStart(position.LineIndex);
        int end = position.LineIndex + 1 < LineCount ? GetLineStart(position.LineIndex + 1) : Length;
        ArgumentOutOfRangeException.ThrowIfNegative(position.ColumnIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position.ColumnIndex, end - start);
        return start + position.ColumnIndex;
    }

    public TextPosition GetPositionAt(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Length);
        // Upper-bound search also maps EOF to the final empty line after a trailing EOL.
        int low = 0, high = LineCount;
        while (low + 1 < high)
        {
            int middle = low + (high - low) / 2;
            if (GetLineStart(middle) <= offset) low = middle;
            else high = middle;
        }
        return new TextPosition(low, offset - GetLineStart(low));
    }

    public TextRange GetRangeAt(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > Length - length)
            throw new ArgumentOutOfRangeException(nameof(length));
        return new TextRange(GetPositionAt(offset), GetPositionAt(offset + length));
    }

    public int GetTextLengthInRange(TextRange range)
    {
        int length = GetOffsetAt(range.EndPosition) - GetOffsetAt(range.StartPosition);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return length;
    }

    public string GetTextInRange(TextRange range)
        => GetText(GetOffsetAt(range.StartPosition), GetTextLengthInRange(range));

    private string GetText(int offset, int length) => string.Create(length, (Snapshot: this, Offset: offset),
        static (destination, state) => state.Snapshot.CopyText(state.Snapshot._root, state.Offset, destination));

    // Visit only the subtrees intersecting the requested range; allocate only the output string.
    private void CopyText(Node? node, int offset, Span<char> destination)
    {
        if (destination.IsEmpty) return;
        if (node is null) throw new InvalidOperationException("Range exceeds captured tree.");
        if (offset < node.SizeLeft)
        {
            int count = Math.Min(destination.Length, node.SizeLeft - offset);
            CopyText(node.Left, offset, destination[..count]);
            destination = destination[count..];
            offset += count;
        }
        int pieceEnd = node.SizeLeft + node.Piece.Length;
        if (!destination.IsEmpty && offset < pieceEnd)
        {
            int count = Math.Min(destination.Length, pieceEnd - offset);
            _buffers[node.Piece.BufferIndex].Text.Slice(
                GetPieceStart(node.Piece) + offset - node.SizeLeft, count).CopyTo(destination);
            destination = destination[count..];
            offset += count;
        }
        if (!destination.IsEmpty) CopyText(node.Right, offset - pieceEnd, destination);
    }

    public char GetChar(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, Length);
        Node? node = _root;
        while (node is not null)
        {
            if (offset < node.SizeLeft) node = node.Left;
            else if (offset < node.SizeLeft + node.Piece.Length)
                return _buffers[node.Piece.BufferIndex].Text[GetPieceStart(node.Piece) + offset - node.SizeLeft];
            else
            {
                offset -= node.SizeLeft + node.Piece.Length;
                node = node.Right;
            }
        }
        throw new InvalidOperationException("Offset exceeds captured tree.");
    }

    public char GetChar(TextPosition position) => GetChar(GetOffsetAt(position));

    public string GetLineEOL(int lineIndex)
    {
        _ = GetLineStart(lineIndex); // Validate even for the final line.
        if (lineIndex == LineCount - 1) return "";
        int end = GetLineStart(lineIndex + 1);
        return GetChar(end - 1) == '\r' ? "\r"
            : end > 1 && GetChar(end - 2) == '\r' ? "\r\n" : "\n";
    }

    public int GetLineLength(int lineIndex)
    {
        int start = GetLineStart(lineIndex);
        int end = lineIndex + 1 < LineCount ? GetLineStart(lineIndex + 1) : Length;
        return end - start - GetLineEOL(lineIndex).Length;
    }

    public string GetLineContent(int lineIndex) => GetText(GetLineStart(lineIndex), GetLineLength(lineIndex));

    public IReadOnlyList<string> GetLinesContent()
    {
        var lines = new string[LineCount];
        for (int i = 0; i < lines.Length; i++) lines[i] = GetLineContent(i);
        return lines;
    }

    public int GetLineFirstNonWhitespaceColumnIndex(int lineIndex)
    {
        string text = GetLineContent(lineIndex);
        for (int i = 0; i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i])) return i;
        return -1;
    }

    public int GetLineLastNonWhitespaceColumnIndex(int lineIndex)
    {
        string text = GetLineContent(lineIndex);
        for (int i = text.Length - 1; i >= 0; i--)
            if (!char.IsWhiteSpace(text[i])) return i + 1;
        return -1;
    }

    public IReadOnlyList<FindMatch> FindMatchesLineByLine(
        TextRange searchRange, SearchData searchData, bool captureMatches, int limitResultCount)
    {
        var result = new List<FindMatch>();
        var searcher = new Searcher(searchData.WordSeparators, searchData.Regex);
        for (int line = searchRange.StartLineIndex; line <= searchRange.EndLineIndex && result.Count < limitResultCount; line++)
        {
            string text = GetLineContent(line);
            int start = line == searchRange.StartLineIndex ? Math.Min(searchRange.StartColumnIndex, text.Length) : 0;
            int end = line == searchRange.EndLineIndex ? Math.Min(searchRange.EndColumnIndex, text.Length) : text.Length;
            string searchedText = text[start..end];
            searcher.Reset(0);
            while (result.Count < limitResultCount && searcher.Next(searchedText) is { } match)
                result.Add(SearchUtils.CreateFindMatch(
                    new TextRange(line, start + match.Index, line, start + match.Index + match.Length),
                    [match], captureMatches));
        }
        return result;
    }
}
