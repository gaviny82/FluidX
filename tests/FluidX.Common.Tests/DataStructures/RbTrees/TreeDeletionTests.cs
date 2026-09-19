using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

[TestClass]
public sealed class TreeDeletionTests
{
    [TestMethod]
    public void DeleteOnlyNode_EmptiesTreeAndAllowsReuse()
    {
        var tree = new RedBlackTree<int>();
        var node = TreeTestSupport.Insert(tree, 1);
        tree.Delete(node);
        TreeTestSupport.AssertInvariants(tree);
        var replacement = TreeTestSupport.Insert(tree, 2);
        TreeTestSupport.AssertInvariants(tree, [replacement]);
    }

    [TestMethod]
    public void DeleteLeafOneChildTwoChildrenAndRoot_MaintainsIdentities()
    {
        foreach (int removed in new[] { 1, 2, 5, 10 })
        {
            var tree = new RedBlackTree<int>();
            int[] values = [10, 5, 15, 2, 7, 12, 18, 1, 3];
            var nodes = values.ToDictionary(x => x, x => TreeTestSupport.Insert(tree, x));
            tree.Delete(nodes[removed]);
            nodes.Remove(removed);
            TreeTestSupport.AssertInvariants(tree, nodes.Values);
            CollectionAssert.AreEqual(values.Where(x => x != removed).Order().ToArray(),
                TreeTestSupport.NodesInOrder(tree).Select(x => x.Data).ToArray());
        }
    }

    [TestMethod]
    public void DeleteMinimumAndMaximum_MaintainsTraversal()
    {
        var tree = new RedBlackTree<int>();
        var nodes = Enumerable.Range(0, 20).ToDictionary(x => x, x => TreeTestSupport.Insert(tree, x));
        tree.Delete(nodes[0]);
        nodes.Remove(0);
        tree.Delete(nodes[19]);
        nodes.Remove(19);
        TreeTestSupport.AssertInvariants(tree, nodes.Values);
        CollectionAssert.AreEqual(Enumerable.Range(1, 18).ToArray(), TreeTestSupport.NodesInOrder(tree).Select(x => x.Data).ToArray());
    }

    [TestMethod]
    public void DeleteAll_InSeveralOrders_MaintainsInvariants()
    {
        var random = new Random(913);
        int[][] orders =
        [
            Enumerable.Range(0, 40).ToArray(),
            Enumerable.Range(0, 40).Reverse().ToArray(),
            Enumerable.Range(0, 40).OrderBy(_ => random.Next()).ToArray()
        ];
        foreach (int[] order in orders)
        {
            var tree = new RedBlackTree<int>();
            var nodes = Enumerable.Range(0, 40).ToDictionary(x => x, x => TreeTestSupport.Insert(tree, x));
            foreach (int value in order)
            {
                tree.Delete(nodes[value]);
                nodes.Remove(value);
                TreeTestSupport.AssertInvariants(tree, nodes.Values);
            }
        }
    }

    [TestMethod]
    public void RepeatedlyDeleteCurrentRoot_EmptiesTree()
    {
        var tree = new RedBlackTree<int>();
        HashSet<RedBlackTree<int>.TreeNode> live =
            Enumerable.Range(0, 50).Select(x => TreeTestSupport.Insert(tree, x)).ToHashSet();
        while (!tree.Root.IsSentinel)
        {
            live.Remove(tree.Root);
            tree.Delete(tree.Root);
            TreeTestSupport.AssertInvariants(tree, live);
        }
    }

    [TestMethod]
    public void DeleteSentinel_IsRejected()
    {
        var tree = new RedBlackTree<int>();
        Assert.ThrowsExactly<ArgumentException>(() => tree.Delete(RedBlackTree<int>.TreeNode.Sentinel));
    }
}
