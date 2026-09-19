using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

internal static class TreeTestSupport
{
    public static RedBlackTree<int>.TreeNode Insert(RedBlackTree<int> tree, int value)
    {
        if (tree.Root.IsSentinel)
            return tree.InsertRight(null, value);

        RedBlackTree<int>.TreeNode node = tree.Root;
        while (true)
        {
            if (value <= node.Data)
            {
                if (node.Left.IsSentinel)
                    return tree.InsertLeft(node, value);
                node = node.Left;
            }
            else
            {
                if (node.Right.IsSentinel)
                    return tree.InsertRight(node, value);
                node = node.Right;
            }
        }
    }

    public static RedBlackTree<int>.TreeNode? Find(RedBlackTree<int> tree, int value)
    {
        RedBlackTree<int>.TreeNode node = tree.Root;
        while (!node.IsSentinel)
        {
            if (value == node.Data)
                return node;
            node = value < node.Data ? node.Left : node.Right;
        }
        return null;
    }

    public static IReadOnlyList<RedBlackTree<T>.TreeNode> NodesInOrder<T>(RedBlackTree<T> tree)
    {
        var nodes = new List<RedBlackTree<T>.TreeNode>();
        if (tree.Root.IsSentinel)
            return nodes;
        for (var node = tree.Root.LeftMost(); !node.IsSentinel; node = node.Next())
            nodes.Add(node);
        return nodes;
    }

    public static void AssertInvariants<T>(RedBlackTree<T> tree, IReadOnlyCollection<RedBlackTree<T>.TreeNode>? expected = null)
    {
        var sentinel = tree.Sentinel;
        Assert.AreEqual(NodeColor.Black, sentinel.Color);
        Assert.AreSame(sentinel, sentinel.Parent);
        Assert.AreSame(sentinel, sentinel.Left);
        Assert.AreSame(sentinel, sentinel.Right);

        if (tree.Root.IsSentinel)
        {
            Assert.AreSame(sentinel, tree.Root);
            Assert.AreEqual(0, expected?.Count ?? 0);
            return;
        }

        Assert.AreSame(sentinel, tree.Root.Parent);
        Assert.AreEqual(NodeColor.Black, tree.Root.Color);
        var visited = new HashSet<RedBlackTree<T>.TreeNode>(ReferenceEqualityComparer.Instance);
        _ = Visit(tree.Root, sentinel, visited);

        if (expected is not null)
        {
            Assert.HasCount(expected.Count, visited);
            foreach (var node in expected)
                Assert.Contains(node, visited, "An expected node is unreachable.");
        }

        var forward = NodesInOrder(tree);
        var backward = new List<RedBlackTree<T>.TreeNode>();
        for (var node = tree.Root.RighMost(); !node.IsSentinel; node = node.Prev())
            backward.Add(node);
        CollectionAssert.AreEqual(forward.Reverse().ToArray(), backward.ToArray());

        int height = Height(tree.Root);
        int maximum = (int)(2 * Math.Log2(visited.Count + 1)) + 1;
        Assert.IsLessThanOrEqualTo(maximum, height);
    }

    private static int Visit<T>(RedBlackTree<T>.TreeNode node, RedBlackTree<T>.TreeNode sentinel,
        HashSet<RedBlackTree<T>.TreeNode> visited)
    {
        if (node.IsSentinel)
        {
            Assert.AreSame(sentinel, node, "A leaf belongs to another tree.");
            return 1;
        }
        Assert.IsTrue(visited.Add(node), "Cycle or duplicate reachable node detected.");
        if (!node.Left.IsSentinel)
            Assert.AreSame(node, node.Left.Parent);
        if (!node.Right.IsSentinel)
            Assert.AreSame(node, node.Right.Parent);
        if (node.Color == NodeColor.Red)
        {
            Assert.AreEqual(NodeColor.Black, node.Left.Color);
            Assert.AreEqual(NodeColor.Black, node.Right.Color);
        }
        int left = Visit(node.Left, sentinel, visited);
        int right = Visit(node.Right, sentinel, visited);
        Assert.AreEqual(left, right, "Black heights differ.");
        return left + (node.Color == NodeColor.Black ? 1 : 0);
    }

    private static int Height<T>(RedBlackTree<T>.TreeNode node) =>
        node.IsSentinel ? 0 : 1 + Math.Max(Height(node.Left), Height(node.Right));
}
