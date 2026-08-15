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
        public bool IsSentinel => this == Parent;

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
    /// Always colored black. Its Parent/Left/Right point to itself.
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

    public void ResetSentinel()
    {
        Sentinel.Parent = Sentinel;
        Sentinel.Color = NodeColor.Black;
    }

    #region Traversal

    public TreeNode Leftest(TreeNode node)
    {
        while (node.Left != Sentinel)
            node = node.Left;
        return node;
    }

    public TreeNode Rightest(TreeNode node)
    {
        while (node.Right != Sentinel)
            node = node.Right;
        return node;
    }

    public TreeNode? Next(TreeNode node)
    {
        if (node.Right != Sentinel)
            return Leftest(node.Right);

        while (node.Parent != Sentinel)
        {
            if (node.Parent.Left == node)
                return node.Parent;
            node = node.Parent;
        }

        return null;
    }

    public TreeNode? Prev(TreeNode node)
    {
        if (node.Left != Sentinel)
            return Rightest(node.Left);

        while (node.Parent != Sentinel)
        {
            if (node.Parent.Right == node)
                return node.Parent;
            node = node.Parent;
        }

        return null;
    }

    #endregion

    #region Insertion

    public TreeNode InsertRight(TreeNode? node, TData data)
    {
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
            var nextNode = Leftest(node.Right);
            nextNode.Left = z;
            z.Parent = nextNode;
        }

        InsertFixup(z);
        return z;
    }

    public TreeNode InsertLeft(TreeNode? node, TData data)
    {
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
            var prevNode = Rightest(node.Left);
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

    public (TreeNode Removed, TreeNode Replacement, bool RemovedWasRed) BstRemove(TreeNode z)
    {
        TreeNode x, y;

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

        bool yWasRed = y.Color == NodeColor.Red;

        if (y == _root)
        {
            _root = x;
            x.Color = NodeColor.Black;
            z.Detach();
            _root.Parent = Sentinel;
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

        z.Detach();

        return (z, x, yWasRed);
    }

    public void DeleteFixup(TreeNode x)
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
