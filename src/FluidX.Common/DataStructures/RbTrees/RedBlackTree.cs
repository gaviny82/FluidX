
namespace FluidX.Common.DataStructures.RbTrees;

/// <summary>
/// A generic red-black tree data structure.
/// </summary>
/// <remarks>
/// Concurrent access to the same instance requires external synchronization.
/// </remarks>
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

        private TreeNode()
        {
            Data = default!;
            Parent = this;
            Left = this;
            Right = this;
            Color = NodeColor.Black;
        }

        internal static TreeNode CreateSentinel() => new();

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

    private TreeNode _root;

    /// <summary>
    /// The instance specific sentinel represents missing children and the root's parent.
    /// Its links point to itself between operations.
    /// </summary>
    /// <remarks>
    /// During deletion, Parent temporarily identifies the replacement node's parent for
    /// fixup and metadata hooks.
    /// </remarks>
    public TreeNode Sentinel { get; } = TreeNode.CreateSentinel();

    public RedBlackTree()
    {
        _root = Sentinel;
    }

    public TreeNode Root => _root;

    protected virtual void OnAfterLeftRotate(TreeNode oldParent, TreeNode newParent) { }
    protected virtual void OnAfterRightRotate(TreeNode oldParent, TreeNode newParent) { }
    /// <summary>
    /// Called after the node is linked into the tree and before insertion fixup can rotate it.
    /// Subclasses can update augmented metadata required by their rotation callbacks.
    /// </summary>
    protected virtual void OnBeforeInsertFixup(TreeNode z) { }
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
        if (_root != Sentinel && node!.IsSentinel)
            throw new ArgumentException("Cannot insert relative to Sentinel when the tree is non-empty.", nameof(node));

        var z = new TreeNode(data, Sentinel);
        z.Color = NodeColor.Red;

        if (_root == Sentinel)
        {
            _root = z;
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
            var nextNode = node.Right.LeftMost();
            nextNode.Left = z;
            z.Parent = nextNode;
        }

        OnBeforeInsertFixup(z);
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
        if (_root != Sentinel && node!.IsSentinel)
            throw new ArgumentException("Cannot insert relative to Sentinel when the tree is non-empty.", nameof(node));

        var z = new TreeNode(data, Sentinel);
        z.Color = NodeColor.Red;

        if (_root == Sentinel)
        {
            _root = z;
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
            var prevNode = node.Left.RighMost();
            prevNode.Right = z;
            z.Parent = prevNode;
        }

        OnBeforeInsertFixup(z);
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
    /// Subclasses can override the removal hooks to maintain augmented metadata at each
    /// structural stage before deletion fixup can rotate the tree.
    /// </summary>
    public void Delete(TreeNode z)
    {
        if (z.IsSentinel)
            throw new ArgumentException("Cannot delete the Sentinel.", nameof(z));

        FindRemovalTargets(z, out var x, out var y);
        bool yWasRed = y.Color == NodeColor.Red;
        OnBeforeRemoval(z, x, y);

        if (y == _root)
        {
            _root = x;

            // If x is the sentinel, we are removing the only node
            x.Color = NodeColor.Black;
            z.Detach();
            Sentinel.Parent = Sentinel;
            _root.Parent = Sentinel;

            OnAfterRemoval(z, x, y);
            return;
        }

        // Remove y from its original location and establish x's parent before
        // subclasses repair metadata for that path.
        if (y == y.Parent.Left)
            y.Parent.Left = x;
        else
            y.Parent.Right = x;

        if (y == z)
        {
            x.Parent = y.Parent;
        }
        else if (y.Parent == z)
        {
            x.Parent = y;
        }
        else
        {
            x.Parent = y.Parent;
        }

        OnAfterRemovalFromOriginalPosition(z, x, y);

        if (y != z)
        {
            RelinkSuccessor(z, y);
            OnAfterRemovalRelink(z, x, y);
        }

        z.Detach();
        OnAfterRemoval(z, x, y);
        OnBeforeRemovalFixup(z, x, y);

        if (!yWasRed)
            DeleteFixup(x);

        Sentinel.Parent = Sentinel;
    }

    /// <summary>
    /// Called before any structural removal changes are made.
    /// </summary>
    protected virtual void OnBeforeRemoval(TreeNode z, TreeNode x, TreeNode y) { }

    /// <summary>
    /// Called after the node physically removed from its original position has been
    /// replaced by <paramref name="x"/>, but before a successor is moved into the
    /// requested node's position.
    /// <para/>
    /// <paramref name="z"/> is the node the caller wants removed.
    /// <paramref name="y"/> is the node physically removed from its position
    /// (same as <paramref name="z"/> unless a successor swap occurs).
    /// <paramref name="x"/> is the replacement node that takes <paramref name="y"/>'s place.
    /// </summary>
    protected virtual void OnAfterRemovalFromOriginalPosition(TreeNode z, TreeNode x, TreeNode y) { }

    /// <summary>
    /// Called after a successor has been moved into the requested node's position.
    /// This hook is not called when <paramref name="y"/> and <paramref name="z"/> are
    /// the same node.
    /// <para/>
    /// <paramref name="z"/> is the node the caller wants removed; it remains attached
    /// until this hook returns so subclasses can copy its augmented metadata.
    /// <paramref name="y"/> is the node that was physically removed from its position
    /// (same as <paramref name="z"/> unless a successor swap occurred — in which case
    /// <paramref name="y"/> took <paramref name="z"/>'s position before <paramref name="z"/> was detached).
    /// <paramref name="x"/> is the replacement node that took <paramref name="y"/>'s original position.
    /// </summary>
    protected virtual void OnAfterRemovalRelink(TreeNode z, TreeNode x, TreeNode y) { }

    /// <summary>
    /// Called after the requested node is detached and before deletion fixup.
    /// </summary>
    protected virtual void OnAfterRemoval(TreeNode z, TreeNode x, TreeNode y) { }

    /// <summary>
    /// Called after the removed node is detached and before deletion fixup can rotate
    /// the tree.
    /// </summary>
    protected virtual void OnBeforeRemovalFixup(TreeNode z, TreeNode x, TreeNode y) { }

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
            y = z.Right.LeftMost();
            x = y.Right;
        }
    }

    private void RelinkSuccessor(TreeNode z, TreeNode y)
    {
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
            if (node.IsSentinel)
                return node;

            if (!node.Right.IsSentinel)
                return node.Right.LeftMost();

            var current = node;
            while (!current.Parent.IsSentinel)
            {
                if (current.Parent.Left == current)
                    return current.Parent;
                current = current.Parent;
            }

            return current.Parent;
        }

        /// <summary>
        /// Returns the previous node in an in-order traversal of the tree.
        /// </summary>
        /// <returns>The previous node in an in-order traversal of the tree.</returns>
        public RedBlackTree<TData>.TreeNode Prev()
        {
            if (node.IsSentinel)
                return node;

            if (!node.Left.IsSentinel)
                return node.Left.RighMost();

            var current = node;
            while (!current.Parent.IsSentinel)
            {
                if (current.Parent.Right == current)
                    return current.Parent;
                current = current.Parent;
            }

            return current.Parent;
        }
    }
}
