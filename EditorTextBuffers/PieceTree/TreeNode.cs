namespace EditorTextBuffers.PieceTree;

internal enum NodeColor
{
    Black = 0,
    Red = 1
}

internal class TreeNode
{
    public TreeNode Parent { get; set; }
    public TreeNode Left { get; set; }
    public TreeNode Right { get; set; }
    public NodeColor Color { get; set; }

    // Piece
    public Piece Piece { get; set; }
    public int SizeLeft { get; set; } // Size of the left subtree (not in order)
    public int LfLeft { get; set; } // Line feeds count in the left subtree (not in order)

    public TreeNode(Piece piece, NodeColor color)
    {
        Piece = piece;
        Color = color;
        SizeLeft = 0;
        LfLeft = 0;
        Parent = this;
        Left = this;
        Right = this;
    }

    /// <summary>
    /// Returns the next node in an in-order traversal.
    /// </summary>
    /// <returns>The next <see cref="TreeNode"/> in an in-order traversal</returns>
    public TreeNode Next()
    {
        if (Right != Sentinel)
            return RedBlackTreeHelper.Leftest(Right);

        TreeNode node = this;

        while (node.Parent != Sentinel)
        {
            if (node.Parent.Left == node)
                break;

            node = node.Parent;
        }

        if (node.Parent == Sentinel)
            return Sentinel;
        else
            return node.Parent;
    }

    /// <summary>
    /// Returns the previous node in an in-order traversal.
    /// </summary>
    /// <returns>The previous <see cref="TreeNode"/> in an in-order traversal</returns>
    public TreeNode Prev()
    {
        if (Left != Sentinel)
            return RedBlackTreeHelper.Rightest(Left);

        TreeNode node = this;

        while (node.Parent != Sentinel)
        {
            if (node.Parent.Right == node)
                break;

            node = node.Parent;
        }

        if (node.Parent == Sentinel)
            return Sentinel;
        else
            return node.Parent;
    }

    /// <summary>
    /// Detaches the node from the tree by setting its parent, left, and right references to <see langword="null"/>.
    /// </summary>
    public void Detach()
    {
        Parent = null!;
        Left = null!;
        Right = null!;
    }

    public static TreeNode Sentinel;

    static TreeNode()
    {
        TreeNode sentinel = new(null!, NodeColor.Black);
        sentinel.Parent = sentinel;
        sentinel.Left = sentinel;
        sentinel.Right = sentinel;
        Sentinel = sentinel;
    }
}
