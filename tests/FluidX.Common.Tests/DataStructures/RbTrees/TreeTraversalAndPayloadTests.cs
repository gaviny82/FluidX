using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

[TestClass]
public sealed class TreeTraversalAndPayloadTests
{
    [TestMethod]
    public void ExtremaAndTraversal_ReturnEveryNodeInBothDirections()
    {
        var tree = new RedBlackTree<int>();
        foreach (int value in new[] { 20, 10, 30, 5, 15, 25, 35 })
            TreeTestSupport.Insert(tree, value);
        var forward = TreeTestSupport.NodesInOrder(tree);
        Assert.AreEqual(5, tree.Root.LeftMost().Data);
        Assert.AreEqual(35, tree.Root.RighMost().Data);
        Assert.IsTrue(forward[0].Prev().IsSentinel);
        Assert.IsTrue(forward[^1].Next().IsSentinel);
        for (int i = 0; i + 1 < forward.Count; i++)
        {
            Assert.AreSame(forward[i + 1], forward[i].Next());
            Assert.AreSame(forward[i], forward[i + 1].Prev());
        }
    }

    [TestMethod]
    public void SubtreeExtrema_ReturnSubtreeBoundaries()
    {
        var tree = new RedBlackTree<int>();
        foreach (int value in new[] { 20, 10, 30, 5, 15, 25, 35 })
            TreeTestSupport.Insert(tree, value);
        var ten = TreeTestSupport.Find(tree, 10)!;
        Assert.AreEqual(5, ten.LeftMost().Data);
        Assert.AreEqual(15, ten.RighMost().Data);
    }

    [TestMethod]
    public void ReferenceAndNullPayloads_DoNotRequireComparison()
    {
        var tree = new RedBlackTree<object?>();
        object first = new();
        object second = new();
        var a = tree.InsertRight(null, first);
        var b = tree.InsertRight(a, null);
        var c = tree.InsertLeft(a, second);
        CollectionAssert.AreEqual(new object?[] { second, first, null }, TreeTestSupport.NodesInOrder(tree).Select(x => x.Data).ToArray());
        TreeTestSupport.AssertInvariants(tree, [a, b, c]);
    }

    [TestMethod]
    public void ChangingPayload_DoesNotChangeTopologyOrIdentity()
    {
        var tree = new RedBlackTree<string>();
        var root = tree.InsertRight(null, "root");
        var right = tree.InsertRight(root, "right");
        var before = TreeTestSupport.NodesInOrder(tree).ToArray();
        root.Data = "changed";
        right.Data = "also changed";
        CollectionAssert.AreEqual(before, TreeTestSupport.NodesInOrder(tree).ToArray());
        TreeTestSupport.AssertInvariants(tree, before);
    }
}

