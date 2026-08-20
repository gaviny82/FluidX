using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

[TestClass]
public class RedBlackTreeTests
{
    #region Helpers

    private static RedBlackTree<int> CreateTree() => new();

    private static RedBlackTree<int>.TreeNode Insert(RedBlackTree<int> tree, int value)
    {
        if (tree.Root == RedBlackTree<int>.TreeNode.Sentinel)
            return tree.InsertRight(RedBlackTree<int>.TreeNode.Sentinel, value);

        var x = tree.Root;
        while (true)
        {
            if (value <= x.Data)
            {
                if (x.Left == RedBlackTree<int>.TreeNode.Sentinel)
                    return tree.InsertLeft(x, value);
                x = x.Left;
            }
            else
            {
                if (x.Right == RedBlackTree<int>.TreeNode.Sentinel)
                    return tree.InsertRight(x, value);
                x = x.Right;
            }
        }
    }

    private static RedBlackTree<int>.TreeNode? Find(RedBlackTree<int> tree, int value)
    {
        var x = tree.Root;
        while (x != RedBlackTree<int>.TreeNode.Sentinel)
        {
            if (value < x.Data)
                x = x.Left;
            else if (value > x.Data)
                x = x.Right;
            else
                return x;
        }
        return null;
    }

    private static List<int> GetInOrderTraversal(RedBlackTree<int> tree)
    {
        var result = new List<int>();
        if (tree.Root == RedBlackTree<int>.TreeNode.Sentinel)
            return result;

        var node = tree.Root.LeftMost();
        while (!node.IsSentinel)
        {
            result.Add(node.Data);
            node = node.Next();
        }
        return result;
    }

    private static bool IsEmpty(RedBlackTree<int> tree) => tree.Root == RedBlackTree<int>.TreeNode.Sentinel;

    private static string DumpTree(RedBlackTree<int> tree)
    {
        if (tree.Root == RedBlackTree<int>.TreeNode.Sentinel)
            return "(empty)";
        return DumpNode(tree, tree.Root, "", true);
    }

    private static string DumpNode(RedBlackTree<int> tree, RedBlackTree<int>.TreeNode node, string indent, bool isLast)
    {
        if (node == RedBlackTree<int>.TreeNode.Sentinel)
            return indent + (isLast ? "└── " : "├── ") + "S(B)\n";

        var color = node.Color == NodeColor.Red ? "R" : "B";
        var line = indent + (isLast ? "└── " : "├── ") + $"{node.Data}({color})\n";
        var childIndent = indent + (isLast ? "    " : "│   ");
        line += DumpNode(tree, node.Left, childIndent, false);
        line += DumpNode(tree, node.Right, childIndent, true);
        return line;
    }

    private static void AssertNoRedRedViolation(RedBlackTree<int> tree)
    {
        AssertNoRedRedViolationRecursive(tree, tree.Root);
    }

    private static void AssertNoRedRedViolationRecursive(RedBlackTree<int> tree, RedBlackTree<int>.TreeNode node)
    {
        if (node == RedBlackTree<int>.TreeNode.Sentinel)
            return;

        if (node.Color == NodeColor.Red)
        {
            Assert.AreEqual(NodeColor.Black, node.Left.Color, $"Red node {node.Data} has red left child");
            Assert.AreEqual(NodeColor.Black, node.Right.Color, $"Red node {node.Data} has red right child");
        }

        AssertNoRedRedViolationRecursive(tree, node.Left);
        AssertNoRedRedViolationRecursive(tree, node.Right);
    }

    private static int AssertBalancedBlackHeight(RedBlackTree<int> tree)
    {
        return AssertBalancedBlackHeightRecursive(tree, tree.Root);
    }

    private static int AssertBalancedBlackHeightRecursive(RedBlackTree<int> tree, RedBlackTree<int>.TreeNode node)
    {
        if (node == RedBlackTree<int>.TreeNode.Sentinel)
            return 1;

        int leftBlack = AssertBalancedBlackHeightRecursive(tree, node.Left);
        int rightBlack = AssertBalancedBlackHeightRecursive(tree, node.Right);

        Assert.AreEqual(leftBlack, rightBlack,
            $"Black height mismatch at node {node.Data}: left={leftBlack}, right={rightBlack}");

        return leftBlack + (node.Color == NodeColor.Black ? 1 : 0);
    }

    private static void AssertRootIsBlack(RedBlackTree<int> tree)
    {
        if (!IsEmpty(tree))
            Assert.AreEqual(NodeColor.Black, tree.Root.Color, "Root must be black");
    }

    private static void AssertBstOrder(RedBlackTree<int> tree)
    {
        var values = GetInOrderTraversal(tree);
        for (int i = 1; i < values.Count; i++)
        {
            Assert.IsLessThanOrEqualTo(values[i], values[i - 1],
                $"BST order violated: {values[i - 1]} > {values[i]}");
        }
    }

    private static void AssertTreeInvariants(RedBlackTree<int> tree)
    {
        if (IsEmpty(tree))
            return;
        AssertRootIsBlack(tree);
        AssertNoRedRedViolation(tree);
        AssertBalancedBlackHeight(tree);
        AssertBstOrder(tree);
    }

    #endregion

    #region Insert Tests

    [TestMethod]
    public void Insert_SingleNode_BecomesRootAndBlack()
    {
        var tree = CreateTree();
        Insert(tree, 42);

        Assert.IsFalse(IsEmpty(tree));
        Assert.AreEqual(42, tree.Root.Data);
        Assert.AreEqual(NodeColor.Black, tree.Root.Color);
    }

    [TestMethod]
    public void Insert_TwoNodes_CorrectStructure()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);

        Assert.AreEqual(10, tree.Root.Data);
        Assert.AreEqual(5, tree.Root.Left.Data);
        AssertTreeInvariants(tree);
    }

    [TestMethod]
    public void Insert_ThreeNodes_Recolors()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);

        AssertTreeInvariants(tree);
        Assert.HasCount(3, GetInOrderTraversal(tree));
    }

    [TestMethod]
    public void Insert_AscendingOrder_MaintainsInvariants()
    {
        var tree = CreateTree();
        for (int i = 1; i <= 20; i++)
        {
            Insert(tree, i);
            AssertTreeInvariants(tree);
        }

        var values = GetInOrderTraversal(tree);
        Assert.HasCount(20, values);
        Assert.AreEqual(1, values[0]);
        Assert.AreEqual(20, values[^1]);
    }

    [TestMethod]
    public void Insert_DescendingOrder_MaintainsInvariants()
    {
        var tree = CreateTree();
        for (int i = 20; i >= 1; i--)
        {
            Insert(tree, i);
            AssertTreeInvariants(tree);
        }

        var values = GetInOrderTraversal(tree);
        Assert.HasCount(20, values);
        Assert.AreEqual(1, values[0]);
        Assert.AreEqual(20, values[^1]);
    }

    [TestMethod]
    public void Insert_RandomSequence_MaintainsInvariants()
    {
        var tree = CreateTree();
        var rng = new Random(12345);
        var inserted = new HashSet<int>();

        for (int i = 0; i < 100; i++)
        {
            int val = rng.Next(0, 200);
            if (inserted.Add(val))
            {
                Insert(tree, val);
                AssertTreeInvariants(tree);
            }
        }
    }

    [TestMethod]
    public void Insert_ThenFind_ReturnsCorrectNode()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);
        Insert(tree, 3);
        Insert(tree, 7);

        Assert.IsNotNull(Find(tree, 10));
        Assert.AreEqual(10, Find(tree, 10)!.Data);
        Assert.IsNotNull(Find(tree, 3));
        Assert.IsNull(Find(tree, 99));
    }

    #endregion

    #region Delete Tests

    [TestMethod]
    public void Delete_SingleNode_TreeEmpty()
    {
        var tree = CreateTree();
        var node = Insert(tree, 42);
        tree.Delete(node);

        Assert.IsTrue(IsEmpty(tree));
    }

    [TestMethod]
    public void Delete_LeafNode_MaintainsInvariants()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);
        Insert(tree, 3);

        var node3 = Find(tree, 3)!;
        tree.Delete(node3);

        AssertTreeInvariants(tree);
        Assert.IsNull(Find(tree, 3));
        Assert.HasCount(3, GetInOrderTraversal(tree));
    }

    [TestMethod]
    public void Delete_LeafNode_SentinelParentReset()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);
        Insert(tree, 3);

        var node3 = Find(tree, 3)!;
        tree.Delete(node3);

        Assert.AreEqual(RedBlackTree<int>.TreeNode.Sentinel, RedBlackTree<int>.TreeNode.Sentinel.Parent,
            "Sentinel.Parent must always point back to Sentinel");
    }

    [TestMethod]
    public void Delete_MultipleLeaves_SentinelParentReset()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);
        Insert(tree, 3);
        Insert(tree, 7);

        tree.Delete(Find(tree, 3)!);
        Assert.AreEqual(RedBlackTree<int>.TreeNode.Sentinel, RedBlackTree<int>.TreeNode.Sentinel.Parent,
            "Sentinel.Parent corrupted after first delete");

        tree.Delete(Find(tree, 7)!);
        Assert.AreEqual(RedBlackTree<int>.TreeNode.Sentinel, RedBlackTree<int>.TreeNode.Sentinel.Parent,
            "Sentinel.Parent corrupted after second delete");

        tree.Delete(Find(tree, 5)!);
        Assert.AreEqual(RedBlackTree<int>.TreeNode.Sentinel, RedBlackTree<int>.TreeNode.Sentinel.Parent,
            "Sentinel.Parent corrupted after third delete");
    }

    [TestMethod]
    public void Delete_SuccessorWithNoRightChild_SentinelParentReset()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);
        Insert(tree, 3);
        Insert(tree, 7);
        Insert(tree, 6);

        var node5 = Find(tree, 5)!;
        tree.Delete(node5);

        Assert.AreEqual(RedBlackTree<int>.TreeNode.Sentinel, RedBlackTree<int>.TreeNode.Sentinel.Parent,
            "Sentinel.Parent must be reset after successor-swap delete");
        AssertTreeInvariants(tree);
    }

    [TestMethod]
    public void Delete_NodeWithOneChild_MaintainsInvariants()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);
        Insert(tree, 3);
        Insert(tree, 4);

        var node3 = Find(tree, 3)!;
        tree.Delete(node3);

        AssertTreeInvariants(tree);
        Assert.IsNull(Find(tree, 3));
        Assert.IsNotNull(Find(tree, 4));
    }

    [TestMethod]
    public void Delete_NodeWithTwoChildren_MaintainsInvariants()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);
        Insert(tree, 3);
        Insert(tree, 7);
        Insert(tree, 12);
        Insert(tree, 18);

        var node5 = Find(tree, 5)!;
        tree.Delete(node5);

        AssertTreeInvariants(tree);
        Assert.IsNull(Find(tree, 5));
        Assert.HasCount(6, GetInOrderTraversal(tree));
    }

    [TestMethod]
    public void Delete_RootNode_MaintainsInvariants()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);

        var node10 = Find(tree, 10)!;
        tree.Delete(node10);

        AssertTreeInvariants(tree);
        Assert.IsNull(Find(tree, 10));
        Assert.HasCount(2, GetInOrderTraversal(tree));
    }

    [TestMethod]
    public void Delete_AllNodes_OneByOne_MaintainsInvariants()
    {
        var tree = CreateTree();
        var nodes = new List<RedBlackTree<int>.TreeNode>();
        for (int i = 1; i <= 10; i++)
            nodes.Add(Insert(tree, i));

        for (int i = 9; i >= 0; i--)
        {
            tree.Delete(nodes[i]);
            try
            {
                AssertTreeInvariants(tree);
            }
            catch
            {
                throw new Exception($"Failed after deleting {nodes[i].Data}. Tree:\n{DumpTree(tree)}");
            }
        }

        Assert.IsTrue(IsEmpty(tree));
    }

    [TestMethod]
    public void Delete_RandomSequence_MaintainsInvariants()
    {
        var tree = CreateTree();
        var inserted = new List<int>();
        var rng = new Random(54321);

        var allValues = Enumerable.Range(0, 100).OrderBy(_ => rng.Next()).Take(50).ToList();
        foreach (var val in allValues)
        {
            Insert(tree, val);
            inserted.Add(val);
        }

        AssertTreeInvariants(tree);

        var toDelete = inserted.OrderBy(_ => rng.Next()).Take(25).ToList();
        foreach (var val in toDelete)
        {
            var node = Find(tree, val);
            Assert.IsNotNull(node, $"Node {val} should exist before deletion");
            tree.Delete(node!);
            AssertTreeInvariants(tree);
        }

        Assert.HasCount(25, GetInOrderTraversal(tree));
    }

    [TestMethod]
    public void Delete_ThenInsert_MaintainsInvariants()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 5);
        Insert(tree, 15);

        var node5 = Find(tree, 5)!;
        tree.Delete(node5);

        AssertTreeInvariants(tree);

        Insert(tree, 7);
        AssertTreeInvariants(tree);
        Assert.IsNotNull(Find(tree, 7));
    }

    #endregion

    #region Stress Tests

    [TestMethod]
    public void StressTest_InsertDeleteSequence_MaintainsInvariants()
    {
        var tree = CreateTree();
        var rng = new Random(99999);
        var present = new HashSet<int>();

        for (int i = 0; i < 500; i++)
        {
            int val = rng.Next(0, 300);
            if (rng.Next(0, 3) > 0)
            {
                if (present.Add(val))
                    Insert(tree, val);
            }
            else
            {
                if (present.Remove(val))
                {
                    var node = Find(tree, val);
                    Assert.IsNotNull(node, $"Node {val} should exist");
                    tree.Delete(node!);
                }
            }

            AssertTreeInvariants(tree);
        }

        var traversal = GetInOrderTraversal(tree);
        Assert.HasCount(present.Count, traversal);
    }

    [TestMethod]
    public void StressTest_InsertAllThenDeleteAll()
    {
        var tree = CreateTree();
        var rng = new Random(77777);
        var values = Enumerable.Range(0, 200).OrderBy(_ => rng.Next()).ToList();

        foreach (var val in values)
            Insert(tree, val);

        AssertTreeInvariants(tree);
        Assert.HasCount(200, GetInOrderTraversal(tree));

        var deleteOrder = values.OrderBy(_ => rng.Next()).ToList();
        foreach (var val in deleteOrder)
        {
            var node = Find(tree, val);
            Assert.IsNotNull(node);
            tree.Delete(node!);
            AssertTreeInvariants(tree);
        }

        Assert.IsTrue(IsEmpty(tree));
    }

    #endregion

    #region Traversal Tests

    [TestMethod]
    public void Next_FullTraversal_ProducesSortedSequence()
    {
        var tree = CreateTree();
        var rng = new Random(11111);
        var values = Enumerable.Range(0, 50).OrderBy(_ => rng.Next()).ToList();
        foreach (var val in values)
            Insert(tree, val);

        var result = GetInOrderTraversal(tree);
        Assert.HasCount(50, result);

        for (int i = 1; i < result.Count; i++)
            Assert.IsLessThan(result[i], result[i - 1]);
    }

    [TestMethod]
    public void Prev_FullTraversal_ProducesReverseSortedSequence()
    {
        var tree = CreateTree();
        var rng = new Random(22222);
        var values = Enumerable.Range(0, 50).OrderBy(_ => rng.Next()).ToList();
        foreach (var val in values)
            Insert(tree, val);

        var forward = GetInOrderTraversal(tree);
        Assert.HasCount(50, forward);
        Assert.AreEqual(0, forward[0]);
        Assert.AreEqual(49, forward[^1]);
    }

    [TestMethod]
    public void Leftest_ReturnsMinimumValue()
    {
        var tree = CreateTree();
        Insert(tree, 50);
        Insert(tree, 25);
        Insert(tree, 75);
        Insert(tree, 10);
        Insert(tree, 30);

        var traversal = GetInOrderTraversal(tree);
        Assert.AreEqual(10, traversal[0]);
    }

    [TestMethod]
    public void Rightest_ReturnsMaximumValue()
    {
        var tree = CreateTree();
        Insert(tree, 50);
        Insert(tree, 25);
        Insert(tree, 75);
        Insert(tree, 10);
        Insert(tree, 30);

        var traversal = GetInOrderTraversal(tree);
        Assert.AreEqual(75, traversal[^1]);
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void EmptyTree_IsEmpty_ReturnsTrue()
    {
        var tree = CreateTree();
        Assert.IsTrue(IsEmpty(tree));
    }

    [TestMethod]
    public void EmptyTree_GetInOrderTraversal_ReturnsEmpty()
    {
        var tree = CreateTree();
        Assert.IsEmpty(GetInOrderTraversal(tree));
    }

    [TestMethod]
    public void Insert_DuplicateValue_InsertsBoth()
    {
        var tree = CreateTree();
        Insert(tree, 10);
        Insert(tree, 10);

        Assert.HasCount(2, GetInOrderTraversal(tree));
        AssertTreeInvariants(tree);
    }

    [TestMethod]
    public void Delete_ThenReinsert_Works()
    {
        var tree = CreateTree();
        var node = Insert(tree, 42);
        tree.Delete(node);
        Assert.IsTrue(IsEmpty(tree));

        Insert(tree, 42);
        Assert.IsFalse(IsEmpty(tree));
        Assert.AreEqual(42, tree.Root.Data);
        AssertTreeInvariants(tree);
    }

    [TestMethod]
    public void LargeInsert_MaintainsLogarithmicHeight()
    {
        var tree = CreateTree();
        int count = 1000;
        for (int i = 0; i < count; i++)
            Insert(tree, i);

        AssertTreeInvariants(tree);
        Assert.HasCount(count, GetInOrderTraversal(tree));

        int height = GetHeight(tree, tree.Root);
        int maxHeight = (int)(2 * Math.Log2(count + 1)) + 1;
        Assert.IsLessThanOrEqualTo(maxHeight, height,
            $"Tree height {height} exceeds maximum expected {maxHeight}");
    }

    private static int GetHeight(RedBlackTree<int> tree, RedBlackTree<int>.TreeNode node)
    {
        if (node == RedBlackTree<int>.TreeNode.Sentinel)
            return 0;
        return 1 + Math.Max(GetHeight(tree, node.Left), GetHeight(tree, node.Right));
    }

    #endregion
}
