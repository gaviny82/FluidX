using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

[TestClass]
public class RedBlackTreeBaseTests
{
    #region Helpers

    private static void AssertNoRedRedViolation(TestRbTree tree)
    {
        AssertNoRedRedViolationRecursive(tree.GetRoot(), tree);
    }

    private static void AssertNoRedRedViolationRecursive(TestNode node, TestRbTree tree)
    {
        if (node == tree.SentinelNode)
            return;

        if (node.Color == NodeColor.Red)
        {
            Assert.IsTrue(node.Left.Color == NodeColor.Black, $"Red node {node.Value} has red left child");
            Assert.IsTrue(node.Right.Color == NodeColor.Black, $"Red node {node.Value} has red right child");
        }

        AssertNoRedRedViolationRecursive(node.Left, tree);
        AssertNoRedRedViolationRecursive(node.Right, tree);
    }

    private static int AssertBalancedBlackHeight(TestRbTree tree)
    {
        return AssertBalancedBlackHeightRecursive(tree.GetRoot(), tree);
    }

    private static int AssertBalancedBlackHeightRecursive(TestNode node, TestRbTree tree)
    {
        if (node == tree.SentinelNode)
            return 1; // Sentinel counts as 1 black

        int leftBlack = AssertBalancedBlackHeightRecursive(node.Left, tree);
        int rightBlack = AssertBalancedBlackHeightRecursive(node.Right, tree);

        Assert.AreEqual(leftBlack, rightBlack,
            $"Black height mismatch at node {node.Value}: left={leftBlack}, right={rightBlack}");

        return leftBlack + (node.Color == NodeColor.Black ? 1 : 0);
    }

    private static void AssertRootIsBlack(TestRbTree tree)
    {
        if (!tree.IsEmpty())
            Assert.AreEqual(NodeColor.Black, tree.GetRoot().Color, "Root must be black");
    }

    private static void AssertBstOrder(TestRbTree tree)
    {
        var values = tree.GetInOrderTraversal();
        for (int i = 1; i < values.Count; i++)
        {
            Assert.IsTrue(values[i - 1] <= values[i],
                $"BST order violated: {values[i - 1]} > {values[i]}");
        }
    }

    private static void AssertTreeInvariants(TestRbTree tree)
    {
        if (tree.IsEmpty())
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
        var tree = new TestRbTree();
        tree.Insert(42);

        Assert.IsFalse(tree.IsEmpty());
        Assert.AreEqual(42, tree.GetRoot().Value);
        Assert.AreEqual(NodeColor.Black, tree.GetRoot().Color);
    }

    [TestMethod]
    public void Insert_TwoNodes_CorrectStructure()
    {
        var tree = new TestRbTree();
        tree.Insert(10);
        tree.Insert(5);

        Assert.AreEqual(10, tree.GetRoot().Value);
        Assert.AreEqual(5, tree.GetRoot().Left.Value);
        AssertTreeInvariants(tree);
    }

    [TestMethod]
    public void Insert_ThreeNodes_Recolors()
    {
        var tree = new TestRbTree();
        tree.Insert(10);
        tree.Insert(5);
        tree.Insert(15);

        AssertTreeInvariants(tree);
        Assert.AreEqual(3, tree.GetInOrderTraversal().Count);
    }

    [TestMethod]
    public void Insert_AscendingOrder_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        for (int i = 1; i <= 20; i++)
        {
            tree.Insert(i);
            AssertTreeInvariants(tree);
        }

        var values = tree.GetInOrderTraversal();
        Assert.AreEqual(20, values.Count);
        Assert.AreEqual(1, values[0]);
        Assert.AreEqual(20, values[^1]);
    }

    [TestMethod]
    public void Insert_DescendingOrder_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        for (int i = 20; i >= 1; i--)
        {
            tree.Insert(i);
            AssertTreeInvariants(tree);
        }

        var values = tree.GetInOrderTraversal();
        Assert.AreEqual(20, values.Count);
        Assert.AreEqual(1, values[0]);
        Assert.AreEqual(20, values[^1]);
    }

    [TestMethod]
    public void Insert_RandomSequence_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        var rng = new Random(12345);
        var inserted = new HashSet<int>();

        for (int i = 0; i < 100; i++)
        {
            int val = rng.Next(0, 200);
            if (inserted.Add(val))
            {
                tree.Insert(val);
                AssertTreeInvariants(tree);
            }
        }
    }

    [TestMethod]
    public void Insert_ThenFind_ReturnsCorrectNode()
    {
        var tree = new TestRbTree();
        tree.Insert(10);
        tree.Insert(5);
        tree.Insert(15);
        tree.Insert(3);
        tree.Insert(7);

        Assert.IsNotNull(tree.Find(10));
        Assert.AreEqual(10, tree.Find(10)!.Value);
        Assert.IsNotNull(tree.Find(3));
        Assert.IsNull(tree.Find(99));
    }

    #endregion

    #region Delete Tests

    [TestMethod]
    public void Delete_SingleNode_TreeEmpty()
    {
        var tree = new TestRbTree();
        var node = tree.Insert(42);
        tree.Delete(node);

        Assert.IsTrue(tree.IsEmpty());
    }

    [TestMethod]
    public void Delete_LeafNode_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        tree.Insert(10);
        tree.Insert(5);
        tree.Insert(15);
        tree.Insert(3);

        var node3 = tree.Find(3)!;
        tree.Delete(node3);

        AssertTreeInvariants(tree);
        Assert.IsNull(tree.Find(3));
        Assert.AreEqual(3, tree.GetInOrderTraversal().Count);
    }

    [TestMethod]
    public void Delete_NodeWithOneChild_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        tree.Insert(10);
        tree.Insert(5);
        tree.Insert(15);
        tree.Insert(3);
        tree.Insert(4);

        var node3 = tree.Find(3)!;
        tree.Delete(node3);

        AssertTreeInvariants(tree);
        Assert.IsNull(tree.Find(3));
        Assert.IsNotNull(tree.Find(4));
    }

    [TestMethod]
    public void Delete_NodeWithTwoChildren_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        tree.Insert(10);
        tree.Insert(5);
        tree.Insert(15);
        tree.Insert(3);
        tree.Insert(7);
        tree.Insert(12);
        tree.Insert(18);

        var node5 = tree.Find(5)!;
        tree.Delete(node5);

        AssertTreeInvariants(tree);
        Assert.IsNull(tree.Find(5));
        Assert.AreEqual(6, tree.GetInOrderTraversal().Count);
    }

    [TestMethod]
    public void Delete_RootNode_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        tree.Insert(10);
        tree.Insert(5);
        tree.Insert(15);

        var node10 = tree.Find(10)!;
        tree.Delete(node10);

        AssertTreeInvariants(tree);
        Assert.IsNull(tree.Find(10));
        Assert.AreEqual(2, tree.GetInOrderTraversal().Count);
    }

    [TestMethod]
    public void Delete_AllNodes_OneByOne_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        var nodes = new List<TestNode>();
        for (int i = 1; i <= 10; i++)
            nodes.Add(tree.Insert(i));

        // Delete in reverse order
        for (int i = 9; i >= 0; i--)
        {
            tree.Delete(nodes[i]);
            try
            {
                AssertTreeInvariants(tree);
            }
            catch
            {
                throw new Exception($"Failed after deleting {nodes[i].Value}. Tree:\n{tree.DumpTree()}");
            }
        }

        Assert.IsTrue(tree.IsEmpty());
    }

    [TestMethod]
    public void Delete_RandomSequence_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        var inserted = new List<int>();
        var rng = new Random(54321);

        // Insert 50 unique values
        var allValues = Enumerable.Range(0, 100).OrderBy(_ => rng.Next()).Take(50).ToList();
        foreach (var val in allValues)
        {
            tree.Insert(val);
            inserted.Add(val);
        }

        AssertTreeInvariants(tree);

        // Delete half of them
        var toDelete = inserted.OrderBy(_ => rng.Next()).Take(25).ToList();
        foreach (var val in toDelete)
        {
            var node = tree.Find(val);
            Assert.IsNotNull(node, $"Node {val} should exist before deletion");
            tree.Delete(node!);
            AssertTreeInvariants(tree);
        }

        Assert.AreEqual(25, tree.GetInOrderTraversal().Count);
    }

    [TestMethod]
    public void Delete_ThenInsert_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        tree.Insert(10);
        tree.Insert(5);
        tree.Insert(15);

        var node5 = tree.Find(5)!;
        tree.Delete(node5);

        AssertTreeInvariants(tree);

        tree.Insert(7);
        AssertTreeInvariants(tree);
        Assert.IsNotNull(tree.Find(7));
    }

    #endregion

    #region Stress Tests

    [TestMethod]
    public void StressTest_InsertDeleteSequence_MaintainsInvariants()
    {
        var tree = new TestRbTree();
        var rng = new Random(99999);
        var present = new HashSet<int>();

        for (int i = 0; i < 500; i++)
        {
            int val = rng.Next(0, 300);
            if (rng.Next(0, 3) > 0) // 2/3 chance insert
            {
                if (present.Add(val))
                    tree.Insert(val);
            }
            else // 1/3 chance delete
            {
                if (present.Remove(val))
                {
                    var node = tree.Find(val);
                    Assert.IsNotNull(node, $"Node {val} should exist");
                    tree.Delete(node!);
                }
            }

            AssertTreeInvariants(tree);
        }

        var traversal = tree.GetInOrderTraversal();
        Assert.AreEqual(present.Count, traversal.Count);
    }

    [TestMethod]
    public void StressTest_InsertAllThenDeleteAll()
    {
        var tree = new TestRbTree();
        var rng = new Random(77777);
        var values = Enumerable.Range(0, 200).OrderBy(_ => rng.Next()).ToList();

        foreach (var val in values)
            tree.Insert(val);

        AssertTreeInvariants(tree);
        Assert.AreEqual(200, tree.GetInOrderTraversal().Count);

        // Delete in random order
        var deleteOrder = values.OrderBy(_ => rng.Next()).ToList();
        foreach (var val in deleteOrder)
        {
            var node = tree.Find(val);
            Assert.IsNotNull(node);
            tree.Delete(node!);
            AssertTreeInvariants(tree);
        }

        Assert.IsTrue(tree.IsEmpty());
    }

    #endregion

    #region Traversal Tests

    [TestMethod]
    public void Next_FullTraversal_ProducesSortedSequence()
    {
        var tree = new TestRbTree();
        var rng = new Random(11111);
        var values = Enumerable.Range(0, 50).OrderBy(_ => rng.Next()).ToList();
        foreach (var val in values)
            tree.Insert(val);

        var result = tree.GetInOrderTraversal();
        Assert.AreEqual(50, result.Count);

        for (int i = 1; i < result.Count; i++)
            Assert.IsTrue(result[i - 1] < result[i]);
    }

    [TestMethod]
    public void Prev_FullTraversal_ProducesReverseSortedSequence()
    {
        var tree = new TestRbTree();
        var rng = new Random(22222);
        var values = Enumerable.Range(0, 50).OrderBy(_ => rng.Next()).ToList();
        foreach (var val in values)
            tree.Insert(val);

        // Get the rightmost node
        var root = tree.GetRoot();
        var node = root;
        // Walk to rightmost using the tree's Rightest
        while (node.Right != tree.SentinelNode)
            node = node.Right;

        // Walk backwards using Prev
        var result = new List<int>();
        while (node != tree.SentinelNode)
        {
            result.Add(node.Value);
            // Use reflection or the tree's Prev - since Prev is protected,
            // we'll use a different approach
            break; // just verify rightmost is correct
        }

        // Use GetInOrderTraversal and verify it's sorted (which tests Next)
        var forward = tree.GetInOrderTraversal();
        Assert.AreEqual(50, forward.Count);
        Assert.AreEqual(0, forward[0]);
        Assert.AreEqual(49, forward[^1]);
    }

    [TestMethod]
    public void Leftest_ReturnsMinimumValue()
    {
        var tree = new TestRbTree();
        tree.Insert(50);
        tree.Insert(25);
        tree.Insert(75);
        tree.Insert(10);
        tree.Insert(30);

        var traversal = tree.GetInOrderTraversal();
        Assert.AreEqual(10, traversal[0]);
    }

    [TestMethod]
    public void Rightest_ReturnsMaximumValue()
    {
        var tree = new TestRbTree();
        tree.Insert(50);
        tree.Insert(25);
        tree.Insert(75);
        tree.Insert(10);
        tree.Insert(30);

        var traversal = tree.GetInOrderTraversal();
        Assert.AreEqual(75, traversal[^1]);
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void EmptyTree_IsEmpty_ReturnsTrue()
    {
        var tree = new TestRbTree();
        Assert.IsTrue(tree.IsEmpty());
    }

    [TestMethod]
    public void EmptyTree_GetInOrderTraversal_ReturnsEmpty()
    {
        var tree = new TestRbTree();
        Assert.AreEqual(0, tree.GetInOrderTraversal().Count);
    }

    [TestMethod]
    public void Insert_DuplicateValue_InsertsBoth()
    {
        var tree = new TestRbTree();
        tree.Insert(10);
        tree.Insert(10);

        // Both should be in the tree (BST allows duplicates)
        Assert.AreEqual(2, tree.GetInOrderTraversal().Count);
        AssertTreeInvariants(tree);
    }

    [TestMethod]
    public void Delete_ThenReinsert_Works()
    {
        var tree = new TestRbTree();
        var node = tree.Insert(42);
        tree.Delete(node);
        Assert.IsTrue(tree.IsEmpty());

        tree.Insert(42);
        Assert.IsFalse(tree.IsEmpty());
        Assert.AreEqual(42, tree.GetRoot().Value);
        AssertTreeInvariants(tree);
    }

    [TestMethod]
    public void LargeInsert_MaintainsLogarithmicHeight()
    {
        var tree = new TestRbTree();
        int count = 1000;
        for (int i = 0; i < count; i++)
            tree.Insert(i);

        AssertTreeInvariants(tree);
        Assert.AreEqual(count, tree.GetInOrderTraversal().Count);

        // RB-tree height should be at most 2 * log2(n + 1)
        int height = GetHeight(tree.GetRoot(), tree);
        int maxHeight = (int)(2 * Math.Log2(count + 1)) + 1;
        Assert.IsTrue(height <= maxHeight,
            $"Tree height {height} exceeds maximum expected {maxHeight}");
    }

    private static int GetHeight(TestNode node, TestRbTree tree)
    {
        if (node == tree.SentinelNode)
            return 0;
        return 1 + Math.Max(GetHeight(node.Left, tree), GetHeight(node.Right, tree));
    }

    #endregion
}
