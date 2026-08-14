namespace FluidX.Common.DataStructures.RbTrees;

public abstract class RedBlackTreeBase<TNode> where TNode : class, IRbTreeNode<TNode>
{
    protected readonly TNode Sentinel;

    protected RedBlackTreeBase()
    {
        Sentinel = CreateSentinel();
    }

    protected abstract TNode CreateSentinel();
    protected abstract TNode Root { get; set; }

    protected abstract void OnAfterLeftRotate(TNode oldParent, TNode newParent);
    protected abstract void OnAfterRightRotate(TNode oldParent, TNode newParent);

    protected void ResetSentinel()
    {
        Sentinel.Parent = Sentinel;
        Sentinel.Color = NodeColor.Black;
    }

    #region Traversal

    protected TNode Leftest(TNode node)
    {
        while (node.Left != Sentinel)
            node = node.Left;
        return node;
    }

    protected TNode Rightest(TNode node)
    {
        while (node.Right != Sentinel)
            node = node.Right;
        return node;
    }

    protected TNode Next(TNode node)
    {
        if (node.Right != Sentinel)
            return Leftest(node.Right);

        while (node.Parent != Sentinel)
        {
            if (node.Parent.Left == node)
                return node.Parent;
            node = node.Parent;
        }

        return Sentinel;
    }

    protected TNode Prev(TNode node)
    {
        if (node.Left != Sentinel)
            return Rightest(node.Left);

        while (node.Parent != Sentinel)
        {
            if (node.Parent.Right == node)
                return node.Parent;
            node = node.Parent;
        }

        return Sentinel;
    }

    #endregion

    #region Insertion

    /// <summary>
    /// Inserts <paramref name="z"/> as the in-order successor of <paramref name="node"/>.
    /// If the tree is empty, <paramref name="z"/> becomes the root (pass null or Sentinel).
    /// <para/>
    /// <paramref name="z"/> must be initialized by the caller: set domain data fields.
    /// Color, Left, Right, Parent will be set by this method.
    /// </summary>
    protected TNode InsertRight(TNode? node, TNode z)
    {
        z.Color = NodeColor.Red;
        z.Left = Sentinel;
        z.Right = Sentinel;
        z.Parent = Sentinel;

        if (Root == Sentinel)
        {
            Root = z;
            z.Color = NodeColor.Black;
            return z;
        }

        if (node!.Right == Sentinel)
        {
            node.Right = z;
            z.Parent = node;
        }
        else
        {
            var nextNode = Leftest(node.Right);
            nextNode.Left = z;
            z.Parent = nextNode;
        }

        InsertFixup(z);
        return z;
    }

    /// <summary>
    /// Inserts <paramref name="z"/> as the in-order predecessor of <paramref name="node"/>.
    /// If the tree is empty, <paramref name="z"/> becomes the root (pass null or Sentinel).
    /// <para/>
    /// <paramref name="z"/> must be initialized by the caller: set domain data fields.
    /// Color, Left, Right, Parent will be set by this method.
    /// </summary>
    protected TNode InsertLeft(TNode? node, TNode z)
    {
        z.Color = NodeColor.Red;
        z.Left = Sentinel;
        z.Right = Sentinel;
        z.Parent = Sentinel;

        if (Root == Sentinel)
        {
            Root = z;
            z.Color = NodeColor.Black;
            return z;
        }

        if (node!.Left == Sentinel)
        {
            node.Left = z;
            z.Parent = node;
        }
        else
        {
            var prevNode = Rightest(node.Left);
            prevNode.Right = z;
            z.Parent = prevNode;
        }

        InsertFixup(z);
        return z;
    }

    protected void InsertFixup(TNode z)
    {
        while (z != Root && z.Parent.Color == NodeColor.Red)
        {
            if (z.Parent == z.Parent.Parent.Left)
            {
                var uncle = z.Parent.Parent.Right;

                if (uncle.Color == NodeColor.Red)
                {
                    z.Parent.Color = NodeColor.Black;
                    uncle.Color = NodeColor.Black;
                    z.Parent.Parent.Color = NodeColor.Red;
                    z = z.Parent.Parent;
                }
                else
                {
                    if (z == z.Parent.Right)
                    {
                        z = z.Parent;
                        LeftRotate(z);
                    }

                    z.Parent.Color = NodeColor.Black;
                    z.Parent.Parent.Color = NodeColor.Red;
                    RightRotate(z.Parent.Parent);
                }
            }
            else
            {
                var uncle = z.Parent.Parent.Left;

                if (uncle.Color == NodeColor.Red)
                {
                    z.Parent.Color = NodeColor.Black;
                    uncle.Color = NodeColor.Black;
                    z.Parent.Parent.Color = NodeColor.Red;
                    z = z.Parent.Parent;
                }
                else
                {
                    if (z == z.Parent.Left)
                    {
                        z = z.Parent;
                        RightRotate(z);
                    }

                    z.Parent.Color = NodeColor.Black;
                    z.Parent.Parent.Color = NodeColor.Red;
                    LeftRotate(z.Parent.Parent);
                }
            }
        }

        Root.Color = NodeColor.Black;
    }

    #endregion

    #region Deletion

    /// <summary>
    /// Performs the standard BST removal of node <paramref name="z"/>.
    /// Returns the removed node, its replacement, and whether the physically removed node was red.
    /// <para/>
    /// Caller is responsible for metadata fixup on the replacement node
    /// before calling <see cref="DeleteFixup"/> (if RemovedWasRed is false).
    /// </summary>
    protected (TNode Removed, TNode Replacement, bool RemovedWasRed) BstRemove(TNode z)
    {
        TNode x, y;

        if (z.Left == Sentinel)
        {
            y = z;
            x = y.Right;
        }
        else if (z.Right == Sentinel)
        {
            y = z;
            x = y.Left;
        }
        else
        {
            y = Leftest(z.Right);
            x = y.Right;
        }

        // Save y's original color before any modifications
        bool yWasRed = y.Color == NodeColor.Red;

        if (y == Root)
        {
            Root = x;
            x.Color = NodeColor.Black;
            z.Detach();
            Root.Parent = Sentinel;
            return (z, x, yWasRed);
        }

        if (y == y.Parent.Left)
            y.Parent.Left = x;
        else
            y.Parent.Right = x;

        if (y == z)
        {
            x.Parent = y.Parent;
        }
        else
        {
            if (y.Parent == z)
                x.Parent = y;
            else
                x.Parent = y.Parent;

            y.Left = z.Left;
            y.Right = z.Right;
            y.Parent = z.Parent;
            y.Color = z.Color;

            if (z == Root)
                Root = y;
            else if (z == z.Parent.Left)
                z.Parent.Left = y;
            else
                z.Parent.Right = y;

            if (y.Left != Sentinel)
                y.Left.Parent = y;
            if (y.Right != Sentinel)
                y.Right.Parent = y;
        }

        z.Detach();

        return (z, x, yWasRed);
    }

    protected void DeleteFixup(TNode x)
    {
        while (x != Root && x.Color == NodeColor.Black)
        {
            if (x == x.Parent.Left)
            {
                var w = x.Parent.Right;

                if (w.Color == NodeColor.Red)
                {
                    w.Color = NodeColor.Black;
                    x.Parent.Color = NodeColor.Red;
                    LeftRotate(x.Parent);
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
                        RightRotate(w);
                        w = x.Parent.Right;
                    }

                    w.Color = x.Parent.Color;
                    x.Parent.Color = NodeColor.Black;
                    w.Right.Color = NodeColor.Black;
                    LeftRotate(x.Parent);
                    x = Root;
                }
            }
            else
            {
                var w = x.Parent.Left;

                if (w.Color == NodeColor.Red)
                {
                    w.Color = NodeColor.Black;
                    x.Parent.Color = NodeColor.Red;
                    RightRotate(x.Parent);
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
                        LeftRotate(w);
                        w = x.Parent.Left;
                    }

                    w.Color = x.Parent.Color;
                    x.Parent.Color = NodeColor.Black;
                    w.Left.Color = NodeColor.Black;
                    RightRotate(x.Parent);
                    x = Root;
                }
            }
        }

        x.Color = NodeColor.Black;
    }

    #endregion

    #region Rotations

    protected void LeftRotate(TNode x)
    {
        var y = x.Right;

        x.Right = y.Left;
        if (y.Left != Sentinel)
            y.Left.Parent = x;

        y.Parent = x.Parent;

        if (x.Parent == Sentinel)
            Root = y;
        else if (x == x.Parent.Left)
            x.Parent.Left = y;
        else
            x.Parent.Right = y;

        y.Left = x;
        x.Parent = y;

        OnAfterLeftRotate(x, y);
    }

    protected void RightRotate(TNode y)
    {
        var x = y.Left;

        y.Left = x.Right;
        if (x.Right != Sentinel)
            x.Right.Parent = y;

        x.Parent = y.Parent;

        if (y.Parent == Sentinel)
            Root = x;
        else if (y == y.Parent.Right)
            y.Parent.Right = x;
        else
            y.Parent.Left = x;

        x.Right = y;
        y.Parent = x;

        OnAfterRightRotate(y, x);
    }

    #endregion
}
