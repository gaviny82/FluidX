using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

[TestClass]
public sealed class TreeSequenceAndIsolationTests
{
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
