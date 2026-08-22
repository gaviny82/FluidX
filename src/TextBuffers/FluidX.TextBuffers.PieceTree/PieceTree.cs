using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.TextBuffers.PieceTree;

// TODO: Confirm 0-based or 1-based indexing
/// <summary>
/// A position in a text buffer
/// </summary>
/// <param name="Line">Line number in current buffer</param>
/// <param name="Column">Column number in current buffer</param>
internal readonly record struct BufferCursor(int Line, int Column);

/// <summary>
/// A piece of text in a text buffer.
/// </summary>
/// <param name="BufferIndex">The index of <see cref="InlineStringBuffer"/> in a list of append-only buffers maintained by a text buffer system.</param>
/// <param name="Start">The start position of the piece in the buffer.</param>
/// <param name="End">The end position of the piece in the buffer.</param>
/// <param name="LineFeedCount">The number of line feeds in the piece.</param>
/// <param name="Length">The length of <see cref="char"/> in the piece.</param>
internal record class Piece(int BufferIndex, BufferCursor Start, BufferCursor End, int LineFeedCount, int Length);

/// <summary>
/// Represents a node in the piece tree, containing a piece of text and metadata about the left subtree.
/// </summary>
internal class PieceNodeData(Piece piece)
{
    /// <summary>
    /// Piece of text stored by this node.
    /// </summary>
    public Piece Piece { get; set; } = piece;

    /// <summary>
    /// Size of char stored in the left subtree.
    /// </summary>
    public int SizeLeft { get; set; } = 0;

    /// <summary>
    /// Line feeds count in the left subtree.
    /// </summary>
    public int LfLeft { get; set; } = 0;
}

internal sealed class PieceTree : RedBlackTree<PieceNodeData>
{
    protected override void OnAfterLeftRotate(TreeNode oldParent, TreeNode newParent)
    {
        // FUTURE: the null check might be redundant
        newParent.SizeLeft += oldParent.SizeLeft + (oldParent.Piece?.Length ?? 0);
        newParent.LfLeft += oldParent.LfLeft + (oldParent.Piece?.LineFeedCount ?? 0);
    }

    protected override void OnAfterRightRotate(TreeNode oldParent, TreeNode newParent)
    {
        // FUTURE: the null check might be redundant
        oldParent.SizeLeft -= newParent.SizeLeft + (newParent.Piece?.Length ?? 0);
        oldParent.LfLeft -= newParent.LfLeft + (newParent.Piece?.LineFeedCount ?? 0);
    }

    protected override void OnAfterInsert(TreeNode z)
    {
        RecomputeTreeMetadata(z);
    }

    protected override void OnBeforeRemoval(TreeNode z, TreeNode x, TreeNode y)
    {
        if (y != z)
        {
            y.SizeLeft = z.SizeLeft;
            y.LfLeft = z.LfLeft;
        }
    }

    protected override void OnAfterRemoval(TreeNode z, TreeNode x, TreeNode y)
    {
        RecomputeTreeMetadata(x);

        if (y != z)
        {
            RecomputeTreeMetadata(y);
        }
    }

    private static int CalculateSize(TreeNode node)
    {
        if (node.IsSentinel)
            return 0;
        return node.SizeLeft + node.Piece.Length + CalculateSize(node.Right);
    }

    private static int CalculateLF(TreeNode node)
    {
        if (node.IsSentinel)
            return 0;
        return node.LfLeft + node.Piece.LineFeedCount + CalculateLF(node.Right);
    }

    internal void UpdateTreeMetadata(TreeNode x, int delta, int lineFeedCntDelta)
    {
        // node length change or line feed count change
        while (x != Root && !x.IsSentinel)
        {
            if (x.Parent.Left == x)
            {
                x.Parent.SizeLeft += delta;
                x.Parent.LfLeft += lineFeedCntDelta;
            }
            x = x.Parent;
        }
    }

    /// <summary>
    /// Performs an in-order traversal of a sub-tree, invoking the provided callback for each node. The traversal stops if the callback returns false.
    /// </summary>
    /// <param name="node">Root of the sub-tree.</param>
    /// <param name="callback">Callback invoked for each node.</param>
    /// <returns><see langword="false"/> if the <paramref name="callback"/> returns <see langword="false"/> at any node.</returns>
    internal static bool IterateInOrder(TreeNode node, Func<TreeNode, bool> callback)
    {
        if (node.IsSentinel)
            return true;

        var current = node.LeftMost();
        while (!current.IsSentinel)
        {
            if (!callback(current))
                return false;
            current = current.Next();
        }
        return true;
    }

    private void RecomputeTreeMetadata(TreeNode x)
    {
        if (x == Root)
            return;

        // Go upwards till the node whose left subtree is changed.
        while (x != Root && x == x.Parent.Right)
        {
            x = x.Parent;
        }

        if (x == Root)
            return; // This means a node is added to the end.

        // x is the node whose right subtree is changed.
        x = x.Parent;

        int delta = CalculateSize(x.Left) - x.SizeLeft;
        int lfDelta = CalculateLF(x.Left) - x.LfLeft;
        x.SizeLeft += delta;
        x.LfLeft += lfDelta;

        // Go upwards till root. O(logN)
        while (x != Root && (delta != 0 || lfDelta != 0))
        {
            if (x.Parent.Left == x)
            {
                x.Parent.SizeLeft += delta;
                x.Parent.LfLeft += lfDelta;
            }
            x = x.Parent;
        }
    }
}

internal static class PieceTreeNodeDataExtensions
{
    extension(RedBlackTree<PieceNodeData>.TreeNode node)
    {
        public Piece Piece
        {
            get => node.Data.Piece;
            set => node.Data.Piece = value;
        }

        public int SizeLeft
        {
            get => node.Data.SizeLeft;
            set => node.Data.SizeLeft = value;
        }

        public int LfLeft
        {
            get => node.Data.LfLeft;
            set => node.Data.LfLeft = value;
        }
    }
}
