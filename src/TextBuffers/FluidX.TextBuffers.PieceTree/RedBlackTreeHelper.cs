namespace FluidX.TextBuffers.PieceTree;

internal static class RedBlackTreeHelper
{
    /// <summary>
    /// Left most node in the tree rooted at <paramref name="node"/>
    /// </summary>
    /// <param name="node">Root node of a tree</param>
    /// <returns>The left most node of the tree rooted at <paramref name="node"/></returns>
    public static TreeNode Leftest(TreeNode node)
    {
        while (node.Left != TreeNode.Sentinel)
        {
            node = node.Left;
        }
        return node;
    }

    /// <summary>
    /// Right most node in the tree rooted at<paramref name="node"/>
    /// </summary>
    /// <param name="node">Root node of a tree</param>
    /// <returns>The right most node of the tree rooted at <paramref name="node"/></returns>
    public static TreeNode Rightest(TreeNode node)
    {
        while (node.Right != TreeNode.Sentinel)
        {
            node = node.Right;
        }
        return node;
    }

    private static int CalculateSize(TreeNode node)
    {
        if (node == TreeNode.Sentinel)
            return 0;

        return node.SizeLeft + node.Piece.Length + CalculateSize(node.Right);
    }

    private static int CalculateLF(TreeNode node)
    {
        if (node == TreeNode.Sentinel)
            return 0;

        return node.LfLeft + node.Piece.LineFeedCount + CalculateLF(node.Right);
    }

    private static void ResetSentinel()
    {
        TreeNode.Sentinel.Parent = TreeNode.Sentinel;
    }

    public static void LeftRotate(PieceTreeBase tree, TreeNode x)
    {
        TreeNode y = x.Right;

        // Fix SizeLeft
        y.SizeLeft += x.SizeLeft + (x.Piece?.Length ?? 0);
        y.LfLeft += x.LfLeft + (x.Piece?.LineFeedCount ?? 0);
        x.Right = y.Left;

        if (y.Left != TreeNode.Sentinel)
            y.Left.Parent = x;

        y.Parent = x.Parent;

        if (x.Parent == TreeNode.Sentinel)
            tree.Root = y;
        else if (x.Parent.Left == x)
            x.Parent.Left = y;
        else
            x.Parent.Right = y;

        y.Left = x;
        x.Parent = y;
    }

    public static void RightRotate(PieceTreeBase tree, TreeNode y)
    {
        TreeNode x = y.Left;
        y.Left = x.Right;

        if (x.Right != TreeNode.Sentinel)
            x.Right.Parent = y;

        x.Parent = y.Parent;

        // Fix SizeLeft
        y.SizeLeft -= x.SizeLeft + (x.Piece?.Length ?? 0);
        y.LfLeft -= x.LfLeft + (x.Piece?.LineFeedCount ?? 0);

        if (y.Parent == TreeNode.Sentinel)
            tree.Root = x;
        else if (y == y.Parent.Right)
            y.Parent.Right = x;
        else
            y.Parent.Left = x;

        x.Right = y;
        y.Parent = x;
    }

    public static void RbDelete(PieceTreeBase tree, TreeNode z)
    {
        TreeNode x, y;

        if (z.Left == TreeNode.Sentinel)
        {
            y = z;
            x = y.Right;
        }
        else if (z.Right == TreeNode.Sentinel)
        {
            y = z;
            x = y.Left;
        }
        else
        {
            y = Leftest(z.Right);
            x = y.Right;
        }

        if (y == tree.Root)
        {
            tree.Root = x;

            // If x is null, we are removing the only node
            x.Color = NodeColor.Black;
            z.Detach();
            ResetSentinel();
            tree.Root.Parent = TreeNode.Sentinel;

            return;
        }

        bool yWasRed = (y.Color == NodeColor.Red);

        if (y == y.Parent.Left)
            y.Parent.Left = x;
        else
            y.Parent.Right = x;

        if (y == z)
        {
            x.Parent = y.Parent;
            RecomputeTreeMetadata(tree, x);
        }
        else
        {
            if (y.Parent == z)
                x.Parent = y;
            else
                x.Parent = y.Parent;

            // as we make changes to x's hierarchy, update SizeLeft of subtree first
            RecomputeTreeMetadata(tree, x);

            y.Left = z.Left;
            y.Right = z.Right;
            y.Parent = z.Parent;
            y.Color = z.Color;

            if (z == tree.Root)
                tree.Root = y;
            else if (z == z.Parent.Left)
                z.Parent.Left = y;
            else
                z.Parent.Right = y;

            if (y.Left != TreeNode.Sentinel)
                y.Left.Parent = y;
            if (y.Right != TreeNode.Sentinel)
                y.Right.Parent = y;

            // update metadata
            // we replace z with y, so in this sub tree, the length change is z.item.length
            y.SizeLeft = z.SizeLeft;
            y.LfLeft = z.LfLeft;
            RecomputeTreeMetadata(tree, y);
        }

        z.Detach();

        if (x.Parent.Left == x)
        {
            int newSizeLeft = CalculateSize(x);
            int newLFLeft = CalculateLF(x);
            if (newSizeLeft != x.Parent.SizeLeft || newLFLeft != x.Parent.LfLeft)
            {
                int delta = newSizeLeft - x.Parent.SizeLeft;
                int lfDelta = newLFLeft - x.Parent.LfLeft;
                x.Parent.SizeLeft = newSizeLeft;
                x.Parent.LfLeft = newLFLeft;
                UpdateTreeMetadata(tree, x.Parent, delta, lfDelta);
            }
        }

        RecomputeTreeMetadata(tree, x.Parent);

        if (yWasRed)
        {
            ResetSentinel();
            return;
        }

        // RB-DELETE-FIXUP
        TreeNode w;
        while (x != tree.Root && x.Color == NodeColor.Black)
        {
            if (x == x.Parent.Left)
            {
                w = x.Parent.Right;

                if (w.Color == NodeColor.Red)
                {
                    w.Color = NodeColor.Black;
                    x.Parent.Color = NodeColor.Red;
                    LeftRotate(tree, x.Parent);
                    w = x.Parent.Right;
                }

                if (w.Left.Color == NodeColor.Black && w.Right.Color == NodeColor.Black)
                {
                    w.Color = NodeColor.Red;
                    x = x.Parent;
                }
                else
                {
                    if (w.Right.Color == NodeColor.Black)
                    {
                        w.Left.Color = NodeColor.Black;
                        w.Color = NodeColor.Red;
                        RightRotate(tree, w);
                        w = x.Parent.Right;
                    }

                    w.Color = x.Parent.Color;
                    x.Parent.Color = NodeColor.Black;
                    w.Right.Color = NodeColor.Black;
                    LeftRotate(tree, x.Parent);
                    x = tree.Root;
                }
            }
            else
            {
                w = x.Parent.Left;

                if (w.Color == NodeColor.Red)
                {
                    w.Color = NodeColor.Black;
                    x.Parent.Color = NodeColor.Red;
                    RightRotate(tree, x.Parent);
                    w = x.Parent.Left;
                }

                if (w.Left.Color == NodeColor.Black && w.Right.Color == NodeColor.Black)
                {
                    w.Color = NodeColor.Red;
                    x = x.Parent;
                }
                else
                {
                    if (w.Left.Color == NodeColor.Black)
                    {
                        w.Right.Color = NodeColor.Black;
                        w.Color = NodeColor.Red;
                        LeftRotate(tree, w);
                        w = x.Parent.Left;
                    }

                    w.Color = x.Parent.Color;
                    x.Parent.Color = NodeColor.Black;
                    w.Left.Color = NodeColor.Black;
                    RightRotate(tree, x.Parent);
                    x = tree.Root;
                }
            }
        }
        x.Color = NodeColor.Black;
        ResetSentinel();
    }

    public static void FixInsert(PieceTreeBase tree, TreeNode x)
    {
        RecomputeTreeMetadata(tree, x);

        while (x != tree.Root && x.Parent.Color == NodeColor.Red)
        {
            if (x.Parent == x.Parent.Parent.Left)
            {
                TreeNode y = x.Parent.Parent.Right;

                if (y.Color == NodeColor.Red)
                {
                    x.Parent.Color = NodeColor.Black;
                    y.Color = NodeColor.Black;
                    x.Parent.Parent.Color = NodeColor.Red;
                    x = x.Parent.Parent;
                }
                else
                {
                    if (x == x.Parent.Right)
                    {
                        x = x.Parent;
                        LeftRotate(tree, x);
                    }

                    x.Parent.Color = NodeColor.Black;
                    x.Parent.Parent.Color = NodeColor.Red;
                    RightRotate(tree, x.Parent.Parent);
                }
            }
            else
            {
                TreeNode y = x.Parent.Parent.Left;

                if (y.Color == NodeColor.Red)
                {
                    x.Parent.Color = NodeColor.Black;
                    y.Color = NodeColor.Black;
                    x.Parent.Parent.Color = NodeColor.Red;
                    x = x.Parent.Parent;
                }
                else
                {
                    if (x == x.Parent.Left)
                    {
                        x = x.Parent;
                        RightRotate(tree, x);
                    }
                    x.Parent.Color = NodeColor.Black;
                    x.Parent.Parent.Color = NodeColor.Red;
                    LeftRotate(tree, x.Parent.Parent);
                }
            }
        }

        tree.Root.Color = NodeColor.Black;
    }

    public static void UpdateTreeMetadata(PieceTreeBase tree, TreeNode x, int delta, int lineFeedCntDelta)
    {
        // node length change or line feed count change
        while (x != tree.Root && x != TreeNode.Sentinel)
        {
            if (x.Parent.Left == x)
            {
                x.Parent.SizeLeft += delta;
                x.Parent.LfLeft += lineFeedCntDelta;
            }

            x = x.Parent;
        }
    }

    public static void RecomputeTreeMetadata(PieceTreeBase tree, TreeNode x)
    {
        if (x == tree.Root)
            return;

        // go upwards till the node whose left subtree is changed.
        while (x != tree.Root && x == x.Parent.Right)
        {
            x = x.Parent;
        }

        if (x == tree.Root)
            return; // well, it means we add a node to the end (inorder)

        // x is the node whose right subtree is changed.
        x = x.Parent;

        int delta = CalculateSize(x.Left) - x.SizeLeft;
        int lfDelta = CalculateLF(x.Left) - x.LfLeft;
        x.SizeLeft += delta;
        x.LfLeft += lfDelta;

        // Go upwards till root. O(logN)
        while (x != tree.Root && (delta != 0 || lfDelta != 0))
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
