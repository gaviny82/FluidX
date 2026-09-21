// Adapted from fredbuf by Cameron DaCamara (MIT); see LICENSE.fredbuf.
namespace FluidX.TextBuffers.PersistentPieceTree;

// Functional red-black tree. There are no parent links, mutable nodes or sentinels.
// Every constructor recomputes augmentation; untouched subtrees retain identity.
internal sealed class PieceNode
{
    internal bool Red { get; }
    internal PieceNode? Left { get; }
    internal PieceNode? Right { get; }
    internal Piece Piece { get; }
    internal TextSummary Summary { get; }
    internal int Length => Summary.Length;

    internal PieceNode(bool red, PieceNode? left, Piece piece, PieceNode? right)
    {
        Red = red;
        Left = left;
        Piece = piece;
        Right = right;
        Summary = (left?.Summary ?? default) + piece.Summary + (right?.Summary ?? default);
    }

    private static bool IsRed(PieceNode? n) => n is { Red: true };
    private static PieceNode Paint(PieceNode n, bool red) => n.Red == red ? n : new(red, n.Left, n.Piece, n.Right);
    internal static PieceNode Insert(PieceNode? root, Piece piece, int offset) => Paint(Ins(root, piece, offset), false);
    private static PieceNode Ins(PieceNode? n, Piece piece, int offset)
    {
        if (n is null) return new(true, null, piece, null);
        int end = (n.Left?.Length ?? 0) + n.Piece.Length;
        return offset < end
            ? Balance(n.Red, Ins(n.Left, piece, offset), n.Piece, n.Right)
            : Balance(n.Red, n.Left, n.Piece, Ins(n.Right, piece, offset - end));
    }

    private static PieceNode Balance(bool red, PieceNode? l, Piece x, PieceNode? r)
    {
        if (!red && IsRed(l))
        {
            if (IsRed(l!.Left))
                return new(true, Paint(l.Left!, false), l.Piece, new(false, l.Right, x, r));
            if (IsRed(l.Right))
            {
                var m = l.Right!;
                return new(true, new(false, l.Left, l.Piece, m.Left), m.Piece, new(false, m.Right, x, r));
            }
        }
        if (!red && IsRed(r))
        {
            if (IsRed(r!.Left))
            {
                var m = r.Left!;
                return new(true, new(false, l, x, m.Left), m.Piece, new(false, m.Right, r.Piece, r.Right));
            }
            if (IsRed(r.Right))
                return new(true, new(false, l, x, r.Left), r.Piece, Paint(r.Right!, false));
        }
        return new(red, l, x, r);
    }

    // Changing a piece preserves tree shape and colors; copy just one path.
    internal static PieceNode Replace(PieceNode node, int offset, Piece piece)
    {
        int start = node.Left?.Length ?? 0;
        if (offset < start) return new(node.Red, Replace(node.Left!, offset, piece), node.Piece, node.Right);
        if (offset == start) return new(node.Red, node.Left, piece, node.Right);
        return new(node.Red, node.Left, node.Piece, Replace(node.Right!, offset - start - node.Piece.Length, piece));
    }

    internal static PieceNode? Remove(PieceNode root, int offset)
    {
        var result = Rem(root, offset);
        return result is null ? null : Paint(result, false);
    }

    private static PieceNode? Rem(PieceNode? n, int offset)
    {
        if (n is null) return null;
        int start = n.Left?.Length ?? 0;
        if (offset == start) return Fuse(n.Left, n.Right);
        if (offset < start)
        {
            var result = new PieceNode(true, Rem(n.Left, offset), n.Piece, n.Right);
            return n.Left is { Red: false } ? BalanceLeft(result) : result;
        }
        else
        {
            var result = new PieceNode(true, n.Left, n.Piece, Rem(n.Right, offset - start - n.Piece.Length));
            return n.Right is { Red: false } ? BalanceRight(result) : result;
        }
    }

    private static PieceNode? Fuse(PieceNode? l, PieceNode? r)
    {
        if (l is null) return r;
        if (r is null) return l;
        if (!l.Red && r.Red) return new(true, Fuse(l, r.Left), r.Piece, r.Right);
        if (l.Red && !r.Red) return new(true, l.Left, l.Piece, Fuse(l.Right, r));
        var m = Fuse(l.Right, r.Left);
        if (IsRed(m))
            return new(true, new(l.Red, l.Left, l.Piece, m!.Left), m.Piece,
                new(r.Red, m.Right, r.Piece, r.Right));
        var result = new PieceNode(true, l.Left, l.Piece, new(r.Red, m, r.Piece, r.Right));
        return l.Red ? result : BalanceLeft(result);
    }

    private static PieceNode Balance(PieceNode n) => IsRed(n.Left) && IsRed(n.Right)
        ? new(true, Paint(n.Left!, false), n.Piece, Paint(n.Right!, false))
        : Balance(n.Red, n.Left, n.Piece, n.Right);

    private static PieceNode BalanceLeft(PieceNode n)
    {
        if (IsRed(n.Left)) return new(true, Paint(n.Left!, false), n.Piece, n.Right);
        if (n.Right is { Red: false } r) return Balance(new(false, n.Left, n.Piece, Paint(r, true)));
        if (n.Right is { Red: true, Left: { Red: false } m } right)
            return new(true, new(false, n.Left, n.Piece, m.Left), m.Piece,
                Balance(new(false, m.Right, right.Piece, Paint(right.Right!, true))));
        throw new InvalidOperationException("Invalid red-black deletion state.");
    }

    private static PieceNode BalanceRight(PieceNode n)
    {
        if (IsRed(n.Right)) return new(true, n.Left, n.Piece, Paint(n.Right!, false));
        if (n.Left is { Red: false } l) return Balance(new(false, Paint(l, true), n.Piece, n.Right));
        if (n.Left is { Red: true, Right: { Red: false } m } left)
            return new(true, Balance(new(false, Paint(left.Left!, true), left.Piece, m.Left)), m.Piece,
                new(false, m.Right, n.Piece, n.Right));
        throw new InvalidOperationException("Invalid red-black deletion state.");
    }

    internal static (Piece Piece, int Start) Find(PieceNode root, int offset)
    {
        int start = 0;
        for (var n = root; ;)
        {
            int at = start + (n.Left?.Length ?? 0);
            if (offset < at) n = n.Left!;
            else if (offset < at + n.Piece.Length) return (n.Piece, at);
            else { start = at + n.Piece.Length; n = n.Right!; }
        }
    }

    internal static TextSummary Prefix(PieceNode? n, int length)
    {
        if (n is null || length == 0) return default;
        if (length == n.Length) return n.Summary;
        int left = n.Left?.Length ?? 0;
        if (length <= left) return Prefix(n.Left, length);
        var summary = n.Left?.Summary ?? default;
        int inPiece = Math.Min(length - left, n.Piece.Length);
        summary += inPiece == n.Piece.Length ? n.Piece.Summary : n.Piece.Slice(0, inPiece).Summary;
        return summary + Prefix(n.Right, length - left - inPiece);
    }

    // Select the start of a break. A leading LF is suppressed after an external CR.
    internal static int BreakStart(PieceNode n, int index, bool previousCr = false)
    {
        int leftLength = n.Left?.Length ?? 0;
        if (n.Left is { } left)
        {
            int count = left.Summary.Breaks - (previousCr && left.Summary.First == '\n' ? 1 : 0);
            if (index < count) return BreakStart(left, index, previousCr);
            index -= count;
            previousCr = left.Summary.Last == '\r';
        }
        bool skip = previousCr && n.Piece.Summary.First == '\n';
        int pieceCount = n.Piece.Summary.Breaks - (skip ? 1 : 0);
        if (index < pieceCount) return leftLength + n.Piece.BreakStart(index + (skip ? 1 : 0));
        return leftLength + n.Piece.Length + BreakStart(n.Right!, index - pieceCount, n.Piece.Summary.Last == '\r');
    }

    internal static void CopyTo(PieceNode? n, int offset, Span<char> output)
    {
        if (n is null || output.Length == 0) return;
        int length = output.Length;
        int left = n.Left?.Length ?? 0;
        if (offset < left)
        {
            int count = Math.Min(length, left - offset);
            CopyTo(n.Left, offset, output[..count]);
        }
        int first = Math.Max(offset, left);
        int last = Math.Min(offset + length, left + n.Piece.Length);
        if (first < last)
        {
            int count = last - first;
            int destination = first - offset;
            n.Piece.Storage.CopyTo(n.Piece.Start + first - left, output.Slice(destination, count));
        }
        int end = left + n.Piece.Length;
        if (offset + length > end)
        {
            int rightOffset = Math.Max(0, offset - end);
            int destination = Math.Max(0, end - offset);
            CopyTo(n.Right, rightOffset, output[destination..]);
        }
    }
}
