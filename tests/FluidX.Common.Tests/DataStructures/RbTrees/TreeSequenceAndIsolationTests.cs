using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

[TestClass]
public sealed class TreeSequenceAndIsolationTests
{
    [TestMethod]
    public void SameDataTypeTrees_HaveDistinctSentinelsAndTraversalBoundaries()
    {
        var first = new RedBlackTree<int>();
        var second = new RedBlackTree<int>();
        Assert.AreNotSame(first.Sentinel, second.Sentinel);
        foreach (var tree in new[] { first, second })
        {
            TreeTestSupport.AssertInvariants(tree);
            Assert.AreSame(tree.Sentinel, tree.Sentinel.Next());
            Assert.AreSame(tree.Sentinel, tree.Sentinel.Prev());
            Assert.AreSame(tree.Sentinel, tree.Root.LeftMost());
            Assert.AreSame(tree.Sentinel, tree.Root.RighMost());
            foreach (int value in new[] { 2, 1, 3 })
                TreeTestSupport.Insert(tree, value);
            Assert.AreSame(tree.Sentinel, tree.Root.LeftMost().Prev());
            Assert.AreSame(tree.Sentinel, tree.Root.RighMost().Next());
            TreeTestSupport.AssertInvariants(tree);
        }
    }

    [TestMethod]
    public void DeletionInAnotherTree_PreservesTemporarySentinelParent()
    {
        var first = new RemovalObserverTree();
        var second = new RedBlackTree<int>();
        var otherNode = TreeTestSupport.Insert(second, 42);
        var nodes = Enumerable.Range(0, 32).Select(value => TreeTestSupport.Insert(first, value)).ToList();
        var leaf = nodes.First(node => node.Color == NodeColor.Black && node.Left.IsSentinel && node.Right.IsSentinel);
        bool observed = false;
        first.ObserveRemoval = replacement =>
        {
            observed = true;
            Assert.AreSame(first.Sentinel, replacement);
            var parent = replacement.Parent;
            Assert.IsFalse(parent.IsSentinel);
            second.Delete(otherNode);
            Assert.AreSame(parent, replacement.Parent);
            Assert.AreSame(first.Sentinel, first.Sentinel.Next());
            Assert.AreSame(first.Sentinel, first.Sentinel.Prev());
            TreeTestSupport.AssertInvariants(second);
        };
        first.Delete(leaf);
        nodes.Remove(leaf);
        Assert.IsTrue(observed);
        TreeTestSupport.AssertInvariants(first, nodes);
    }

    [TestMethod]
    public async Task ConcurrentIndependentTrees_MatchReferenceSets()
    {
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            var tree = new RedBlackTree<int>();
            var live = new Dictionary<int, RedBlackTree<int>.TreeNode>();
            var random = new Random(17713 + worker);
            for (int step = 0; step < 1000; step++)
            {
                int value = random.Next(100);
                if (random.Next(2) == 0)
                {
                    if (!live.ContainsKey(value))
                        live.Add(value, TreeTestSupport.Insert(tree, value));
                }
                else if (live.Remove(value, out var node))
                    tree.Delete(node);
                TreeTestSupport.AssertInvariants(tree, live.Values);
                CollectionAssert.AreEqual(live.Keys.Order().ToArray(),
                    TreeTestSupport.NodesInOrder(tree).Select(node => node.Data).ToArray(), $"Worker {worker}, step {step}");
            }
            foreach (var node in live.Values)
                tree.Delete(node);
            TreeTestSupport.AssertInvariants(tree);
        })));
    }

    private sealed class RemovalObserverTree : RedBlackTree<int>
    {
        public Action<TreeNode>? ObserveRemoval { get; set; }

        protected override void OnAfterRemovalFromOriginalPosition(TreeNode z, TreeNode x, TreeNode y)
            => ObserveRemoval?.Invoke(x);
    }

    [TestMethod]
    public void SeededMixedSequence_MatchesReferenceSet()
    {
        var tree = new RedBlackTree<int>();
        var live = new Dictionary<int, RedBlackTree<int>.TreeNode>();
        var random = new Random(17713);
        for (int step = 0; step < 1000; step++)
        {
            int value = random.Next(200);
            if (random.Next(3) != 0)
            {
                if (!live.ContainsKey(value))
                    live.Add(value, TreeTestSupport.Insert(tree, value));
            }
            else if (live.Remove(value, out var node))
            {
                tree.Delete(node);
            }
            TreeTestSupport.AssertInvariants(tree, live.Values);
            CollectionAssert.AreEqual(live.Keys.Order().ToArray(), TreeTestSupport.NodesInOrder(tree).Select(x => x.Data).ToArray(), $"Step {step}");
        }
    }

    [TestMethod]
    public void InterleavedTrees_DoNotAffectEachOther()
    {
        var first = new RedBlackTree<int>();
        var second = new RedBlackTree<int>();
        var firstNodes = new List<RedBlackTree<int>.TreeNode>();
        var secondNodes = new List<RedBlackTree<int>.TreeNode>();
        for (int i = 0; i < 50; i++)
        {
            firstNodes.Add(TreeTestSupport.Insert(first, i));
            secondNodes.Add(TreeTestSupport.Insert(second, 100 + i));
            TreeTestSupport.AssertInvariants(first, firstNodes);
            TreeTestSupport.AssertInvariants(second, secondNodes);
        }
        foreach (var node in firstNodes.ToArray())
        {
            first.Delete(node);
            firstNodes.Remove(node);
            TreeTestSupport.AssertInvariants(first, firstNodes);
            TreeTestSupport.AssertInvariants(second, secondNodes);
        }
    }

    [TestMethod]
    public void RepeatedFillAndDrainCycles_MaintainInvariants()
    {
        var tree = new RedBlackTree<int>();
        for (int cycle = 0; cycle < 5; cycle++)
        {
            var nodes = Enumerable.Range(0, 100).Select(x => TreeTestSupport.Insert(tree, x)).ToList();
            TreeTestSupport.AssertInvariants(tree, nodes);
            foreach (var node in nodes.OrderBy(x => (x.Data * 37) % 101))
                tree.Delete(node);
            TreeTestSupport.AssertInvariants(tree);
        }
    }
}
