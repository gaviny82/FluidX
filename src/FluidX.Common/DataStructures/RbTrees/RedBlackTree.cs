
namespace FluidX.Common.DataStructures.RbTrees;

public class RedBlackTree<TData>
{
    public sealed class TreeNode
    {
        /// <summary>
        /// A shared sentinel node representing all leaves and the parent of the root.
        /// Always colored black. Its <seealso cref="TreeNode.Parent"/>,
        /// <seealso cref="TreeNode.Left"/>, and <seealso cref="TreeNode.Right"/> point to itself.
        /// </summary>
        public static TreeNode Sentinel { get; }

        public TData Data { get; set; }
        public TreeNode Parent { get; internal set; }
        public TreeNode Left { get; internal set; }
        public TreeNode Right { get; internal set; }
        public NodeColor Color { get; internal set; }
        public bool IsSentinel => this == Left;

        static TreeNode()
        {
            Sentinel = new TreeNode(default!);
            Sentinel.Parent = Sentinel;
            Sentinel.Left = Sentinel;
            Sentinel.Right = Sentinel;
            Sentinel.Color = NodeColor.Black;
        }

        internal TreeNode(TData data)
        {
            Data = data;
            Parent = Sentinel;
            Left = Sentinel;
            Right = Sentinel;
        }

        internal void Detach()
        {
            Parent = null!;
            Left = null!;
            Right = null!;
        }
    }

    private TreeNode _root;

    public RedBlackTree()
    {
        _root = TreeNode.Sentinel;
    }

    public TreeNode Root => _root;

    protected virtual void OnAfterLeftRotate(TreeNode oldParent, TreeNode newParent) { }
    protected virtual void OnAfterRightRotate(TreeNode oldParent, TreeNode newParent) { }
    protected virtual void OnAfterInsert(TreeNode z) { }

    #region Insertion

    // FUTURE: Consider exposing a single Insert method for the RB-tree, without explicilty
    // specifying the parent node. This would require a comparison function to be provided.

    /// <summary>
    /// Inserts a new node with <paramref name="data"/> as the right child of <paramref name="node"/>.
    /// </summary>
    /// <param name="node">The node to insert the new node as the right child of. <see langword="null"/> is allowed only if the root is the sentinel.</param>
    /// <param name="data">Data for the new node.</param>
    /// <returns>The new node created.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="node"/> is the sentinel and the tree is non-empty.</exception>
    public TreeNode InsertRight(TreeNode? node, TData data)
    {
        if (_root != TreeNode.Sentinel && node!.IsSentinel)
            throw new ArgumentException("Cannot insert relative to Sentinel when the tree is non-empty.", nameof(node));

        var z = new TreeNode(data);
        z.Color = NodeColor.Red;

        if (_root == TreeNode.Sentinel)
        {
            _root = z;
            z.Color = NodeColor.Black;
            return z;
        }

        if (node!.Right == TreeNode.Sentinel)
        {
            node.Right = z;
            z.Parent = node;
        }
        else
        {
            var nextNode = node.Right.LeftMost();
            nextNode.Left = z;
            z.Parent = nextNode;
        }

        InsertFixup(z);
        OnAfterInsert(z);
        return z;
    }

    /// <summary>
    /// Inserts a new node with <paramref name="data"/> as the left child of <paramref name="node"/>.
    /// </summary>
    /// <param name="node">The node to insert the new node as the leftchild of. <see langword="null"/> is allowed only if the root is the sentinel.</param>
    /// <param name="data">Data for the new node.</param>
    /// <returns>The new node created.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="node"/> is the sentinel and the tree is non-empty.</exception>
    public TreeNode InsertLeft(TreeNode? node, TData data)
    {
        if (_root != TreeNode.Sentinel && node!.IsSentinel)
            throw new ArgumentException("Cannot insert relative to Sentinel when the tree is non-empty.", nameof(node));

        var z = new TreeNode(data);
        z.Color = NodeColor.Red;

        if (_root == TreeNode.Sentinel)
        {
            _root = z;
            z.Color = NodeColor.Black;
            return z;
        }

        if (node!.Left == TreeNode.Sentinel)
        {
            node.Left = z;
            z.Parent = node;
        }
        else
        {
            var prevNode = node.Left.RighMost();
            prevNode.Right = z;
            z.Parent = prevNode;
        }

        InsertFixup(z);
        OnAfterInsert(z);
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
            TreeNode.Sentinel.Parent = TreeNode.Sentinel;
            _root.Parent = TreeNode.Sentinel;

            OnAfterRemoval(z, x, y);
            return;
        }

        OnBeforeRemoval(z, x, y);
        RelinkForRemoval(z, x, y);
        z.Detach();
        OnAfterRemoval(z, x, y);

        if (!yWasRed)
            DeleteFixup(x);

        TreeNode.Sentinel.Parent = TreeNode.Sentinel;
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
            y = z.Right.LeftMost();
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

            if (y.Left != TreeNode.Sentinel)
                y.Left.Parent = y;
            if (y.Right != TreeNode.Sentinel)
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
        if (y.Left != TreeNode.Sentinel)
            y.Left.Parent = x;

        y.Parent = x.Parent;

        if (x.Parent == TreeNode.Sentinel)
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
        if (x.Right != TreeNode.Sentinel)
            x.Right.Parent = y;

        x.Parent = y.Parent;

        if (y.Parent == TreeNode.Sentinel)
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
        /// <summary>
        /// Returns the left-most node in the sub-tree rooted at this <paramref name="node"/>.
        /// </summary>
        /// <returns>The left-most node.</returns>
        public RedBlackTree<TData>.TreeNode LeftMost()
        {
            var current = node;
            while (!current.Left.IsSentinel)
                current = current.Left;
            return current;
        }

        /// <summary>
        /// Returns the right-most node in the sub-tree rooted at this <paramref name="node"/>.
        /// </summary>
        /// <returns>The right-most node.</returns>
        public RedBlackTree<TData>.TreeNode RighMost()
        {
            var current = node;
            while (!current.Right.IsSentinel)
                current = current.Right;
            return current;
        }

        /// <summary>
        /// Returns the next node in an in-order traversal of the tree.
        /// </summary>
        /// <returns>The next node in an in-order traversal of the tree.</returns>
        public RedBlackTree<TData>.TreeNode Next()
        {
            if (!node.Right.IsSentinel)
                return node.Right.LeftMost();

            var current = node;
            while (!current.Parent.IsSentinel)
            {
                if (current.Parent.Left == current)
                    return current.Parent;
                current = current.Parent;
            }

            return RedBlackTree<TData>.TreeNode.Sentinel;
        }

        /// <summary>
        /// Returns the previous node in an in-order traversal of the tree.
        /// </summary>
        /// <returns>The previous node in an in-order traversal of the tree.</returns>
        public RedBlackTree<TData>.TreeNode Prev()
        {
            if (!node.Left.IsSentinel)
                return node.Left.RighMost();

            var current = node;
            while (!current.Parent.IsSentinel)
            {
                if (current.Parent.Right == current)
                    return current.Parent;
                current = current.Parent;
            }

            return RedBlackTree<TData>.TreeNode.Sentinel;
        }
    }
}
