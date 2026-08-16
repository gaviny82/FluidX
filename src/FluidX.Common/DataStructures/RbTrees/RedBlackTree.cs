
namespace FluidX.Common.DataStructures.RbTrees;

public class RedBlackTree<TData>
{
    public sealed class TreeNode
    {
        public TData Data { get; set; }
        public TreeNode Parent { get; internal set; }
        public TreeNode Left { get; internal set; }
        public TreeNode Right { get; internal set; }
        public NodeColor Color { get; internal set; }
        public bool IsSentinel => this == Left;

        internal TreeNode(TData data, TreeNode sentinel)
        {
            Data = data;
            Parent = sentinel;
            Left = sentinel;
            Right = sentinel;
        }

        internal void Detach()
        {
            Parent = null!;
            Left = null!;
            Right = null!;
        }
    }

    /// <summary>
    /// A shared sentinel node representing all leaves and the parent of the root.
    /// Always colored black. Its <seealso cref="TreeNode.Parent"/>,
    /// <seealso cref="TreeNode.Left"/>, and <seealso cref="TreeNode.Right"/> point to itself.
    /// </summary>
    public TreeNode Sentinel { get; }
    private TreeNode _root;

    public RedBlackTree()
    {
        Sentinel = new TreeNode(default!, null!);
        Sentinel.Parent = Sentinel;
        Sentinel.Left = Sentinel;
        Sentinel.Right = Sentinel;
        Sentinel.Color = NodeColor.Black;
        _root = Sentinel;
    }

    public TreeNode Root => _root;

    protected virtual void OnAfterLeftRotate(TreeNode oldParent, TreeNode newParent) { }
    protected virtual void OnAfterRightRotate(TreeNode oldParent, TreeNode newParent) { }

    #region Insertion

    public TreeNode InsertRight(TreeNode node, TData data)
    {
        if (_root != Sentinel && node.IsSentinel)
            throw new ArgumentException("Cannot insert relative to Sentinel when the tree is non-empty.", nameof(node));

        var z = new TreeNode(data, Sentinel);
        z.Color = NodeColor.Red;

        if (_root == Sentinel)
        {
            _root = z;
            z.Color = NodeColor.Black;
            return z;
        }

        if (node.Right == Sentinel)
        {
            node.Right = z;
            z.Parent = node;
        }
        else
        {
            var nextNode = node.Right.Leftest();
            nextNode.Left = z;
            z.Parent = nextNode;
        }

        InsertFixup(z);
        return z;
    }

    public TreeNode InsertLeft(TreeNode node, TData data)
    {
        if (_root != Sentinel && node.IsSentinel)
            throw new ArgumentException("Cannot insert relative to Sentinel when the tree is non-empty.", nameof(node));

        var z = new TreeNode(data, Sentinel);
        z.Color = NodeColor.Red;

        if (_root == Sentinel)
        {
            _root = z;
            z.Color = NodeColor.Black;
            return z;
        }

        if (node.Left == Sentinel)
        {
            node.Left = z;
            z.Parent = node;
        }
        else
        {
            var prevNode = node.Left.Rightest();
            prevNode.Right = z;
            z.Parent = prevNode;
        }

        InsertFixup(z);
        return z;
    }

    private void InsertFixup(TreeNode z)
    {
        while (z != _root && z.Parent.Color == NodeColor.Red)
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

        _root.Color = NodeColor.Black;
    }

    #endregion

    #region Deletion

    /// <summary>
    /// Deletes <paramref name="z"/> from the tree, maintaining red-black invariants.
    /// Subclasses override <see cref="OnBeforeRemoval"/> and <see cref="OnAfterRemoval"/>
    /// to handle augmented metadata.
    /// </summary>
    public void Delete(TreeNode z)
    {
        if (z.IsSentinel)
            throw new ArgumentException("Cannot delete the Sentinel.", nameof(z));

        FindRemovalTargets(z, out var x, out var y);
        bool yWasRed = y.Color == NodeColor.Red;

        if (y == _root)
        {
            OnBeforeRemoval(z, x, y);

            _root = x;

            // If x is null, we are removing the only node
            x.Color = NodeColor.Black;
            z.Detach();
            Sentinel.Parent = Sentinel;
            _root.Parent = Sentinel;

            OnAfterRemoval(z, x, y);
            return;
        }

        OnBeforeRemoval(z, x, y);
        RelinkForRemoval(z, x, y);
        z.Detach();
        OnAfterRemoval(z, x, y);

        if (!yWasRed)
            DeleteFixup(x);

        Sentinel.Parent = Sentinel;
    }

    /// <summary>
    /// Called before the structural relink. Override to transfer augmented data
    /// from the removed node to its replacement (e.g., IntervalTree delta transfer).
    /// <para/>
    /// <paramref name="z"/> is the node the caller wants removed.
    /// <paramref name="y"/> is the node physically removed from its position
    /// (same as <paramref name="z"/> unless a successor swap occurs).
    /// <paramref name="x"/> is the replacement node that takes <paramref name="y"/>'s place.
    /// </summary>
    protected virtual void OnBeforeRemoval(TreeNode z, TreeNode x, TreeNode y) { }

    /// <summary>
    /// Called after the structural relink and detach. Override to recompute
    /// augmented metadata (e.g., PieceTree SizeLeft/LfLeft, IntervalTree MaxEnd).
    /// <para/>
    /// <paramref name="z"/> is the node the caller wanted removed (already detached).
    /// <paramref name="y"/> is the node that was physically removed from its position
    /// (same as <paramref name="z"/> unless a successor swap occurred — in which case
    /// <paramref name="y"/> took <paramref name="z"/>'s position before <paramref name="z"/> was detached).
    /// <paramref name="x"/> is the replacement node that took <paramref name="y"/>'s original position.
    /// </summary>
    protected virtual void OnAfterRemoval(TreeNode z, TreeNode x, TreeNode y) { }

    private void FindRemovalTargets(TreeNode z, out TreeNode x, out TreeNode y)
    {
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
            y = z.Right.Leftest();
            x = y.Right;
        }
    }

    private void RelinkForRemoval(TreeNode z, TreeNode x, TreeNode y)
    {
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

            if (z == _root)
                _root = y;
            else if (z == z.Parent.Left)
                z.Parent.Left = y;
            else
                z.Parent.Right = y;

            if (y.Left != Sentinel)
                y.Left.Parent = y;
            if (y.Right != Sentinel)
                y.Right.Parent = y;
        }
    }

    private void DeleteFixup(TreeNode x)
    {
        while (x != _root && x.Color == NodeColor.Black)
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
                    x = _root;
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
                    x = _root;
                }
            }
        }

        x.Color = NodeColor.Black;
    }

    #endregion

    #region Rotations

    private void LeftRotate(TreeNode x)
    {
        var y = x.Right;

        x.Right = y.Left;
        if (y.Left != Sentinel)
            y.Left.Parent = x;

        y.Parent = x.Parent;

        if (x.Parent == Sentinel)
            _root = y;
        else if (x == x.Parent.Left)
            x.Parent.Left = y;
        else
            x.Parent.Right = y;

        y.Left = x;
        x.Parent = y;

        OnAfterLeftRotate(x, y);
    }

    private void RightRotate(TreeNode y)
    {
        var x = y.Left;

        y.Left = x.Right;
        if (x.Right != Sentinel)
            x.Right.Parent = y;

        x.Parent = y.Parent;

        if (y.Parent == Sentinel)
            _root = x;
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

public static class RedBlackTreeNodeTraversalExtensions
{
    extension<TData>(RedBlackTree<TData>.TreeNode node)
    {
        public RedBlackTree<TData>.TreeNode Leftest()
        {
            var current = node;
            while (!current.Left.IsSentinel)
                current = current.Left;
            return current;
        }

        public RedBlackTree<TData>.TreeNode Rightest()
        {
            var current = node;
            while (!current.Right.IsSentinel)
                current = current.Right;
            return current;
        }

        public RedBlackTree<TData>.TreeNode? Next()
        {
            if (!node.Right.IsSentinel)
                return node.Right.Leftest();

            var current = node;
            while (!current.Parent.IsSentinel)
            {
                if (current.Parent.Left == current)
                    return current.Parent;
                current = current.Parent;
            }

            return null;
        }

        public RedBlackTree<TData>.TreeNode? Prev()
        {
            if (!node.Left.IsSentinel)
                return node.Left.Rightest();

            var current = node;
            while (!current.Parent.IsSentinel)
            {
                if (current.Parent.Right == current)
                    return current.Parent;
                current = current.Parent;
            }

            return null;
        }
    }
}
