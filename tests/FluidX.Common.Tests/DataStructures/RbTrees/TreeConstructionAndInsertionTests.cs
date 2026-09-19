using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

[TestClass]
public sealed class TreeConstructionAndInsertionTests
{
    [TestMethod]
    public void NewTree_HasSentinelRootAndValidSentinel()
    {
        var tree = new RedBlackTree<int>();
        Assert.IsTrue(tree.Root.IsSentinel);
        TreeTestSupport.AssertInvariants(tree);
    }

    [TestMethod]
    public void EitherInsertionMethod_CanCreateRoot()
    {
        foreach (bool insertLeft in new[] { false, true })
        {
            var tree = new RedBlackTree<int>();
            var node = insertLeft ? tree.InsertLeft(null, 42) : tree.InsertRight(null, 42);
            Assert.AreSame(node, tree.Root);
            Assert.AreEqual(42, node.Data);
            TreeTestSupport.AssertInvariants(tree, [node]);
        }
    }

    [TestMethod]
    public void InsertLeftAndRight_PreserveInOrderPosition()
    {
        var tree = new RedBlackTree<int>();
        var root = tree.InsertRight(null, 20);
        var left = tree.InsertLeft(root, 10);
        var right = tree.InsertRight(root, 30);
        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, TreeTestSupport.NodesInOrder(tree).Select(x => x.Data).ToArray());
        TreeTestSupport.AssertInvariants(tree, [root, left, right]);
    }

    [TestMethod]
    public void InsertionIntoOccupiedSides_UsesAdjacentInOrderPosition()
    {
        var tree = new RedBlackTree<int>();
        var root = tree.InsertRight(null, 20);
        _ = tree.InsertLeft(root, 10);
        _ = tree.InsertLeft(root, 15);
        _ = tree.InsertRight(root, 30);
        _ = tree.InsertRight(root, 25);
        CollectionAssert.AreEqual(new[] { 10, 15, 20, 25, 30 }, TreeTestSupport.NodesInOrder(tree).Select(x => x.Data).ToArray());
        TreeTestSupport.AssertInvariants(tree);
    }

    [TestMethod]
    public void AscendingDescendingAndZigzagSequences_MaintainInvariants()
    {
        int[][] sequences =
        [
            Enumerable.Range(0, 100).ToArray(),
            Enumerable.Range(0, 100).Reverse().ToArray(),
            Enumerable.Range(0, 50).SelectMany(x => new[] { x, 99 - x }).ToArray()
        ];
        foreach (int[] sequence in sequences)
        {
            var tree = new RedBlackTree<int>();
            var nodes = new List<RedBlackTree<int>.TreeNode>();
            foreach (int value in sequence)
            {
                nodes.Add(TreeTestSupport.Insert(tree, value));
                TreeTestSupport.AssertInvariants(tree, nodes);
            }
            CollectionAssert.AreEqual(sequence.Order().ToArray(), TreeTestSupport.NodesInOrder(tree).Select(x => x.Data).ToArray());
        }
    }

    [TestMethod]
    public void DuplicatePayloads_AreDistinctReachableNodes()
    {
        var tree = new RedBlackTree<int>();
        var nodes = Enumerable.Range(0, 5).Select(_ => TreeTestSupport.Insert(tree, 7)).ToArray();
        Assert.HasCount(5, nodes.Distinct(ReferenceEqualityComparer.Instance));
        TreeTestSupport.AssertInvariants(tree, nodes);
    }

    [TestMethod]
    public void InsertingRelativeToSentinelInNonemptyTree_IsRejected()
    {
        var tree = new RedBlackTree<int>();
        _ = tree.InsertRight(null, 1);
        Assert.ThrowsExactly<ArgumentException>(() => tree.InsertLeft(tree.Sentinel, 0));
        Assert.ThrowsExactly<ArgumentException>(() => tree.InsertRight(tree.Sentinel, 2));
        TreeTestSupport.AssertInvariants(tree);
    }
}

