using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

internal sealed class TestRbTree : RedBlackTreeBase<TestNode>
{
    private TestNode _root;

    public TestRbTree()
    {
        _root = Sentinel;
    }

    protected override TestNode CreateSentinel() => new(0, isSentinel: true);

    protected override TestNode Root
    {
        get => _root;
        set => _root = value;
    }

    protected override void OnAfterLeftRotate(TestNode oldParent, TestNode newParent)
    {
        // No augmented metadata to maintain
    }

    protected override void OnAfterRightRotate(TestNode oldParent, TestNode newParent)
    {
        // No augmented metadata to maintain
    }

    public TestNode Insert(int value)
    {
        var z = new TestNode(value);
        if (IsEmpty())
            return InsertRight(null, z);

        var x = _root;
        while (true)
        {
            if (value <= x.Value)
            {
                if (x.Left == Sentinel)
                    return InsertLeft(x, z);
                x = x.Left;
            }
            else
            {
                if (x.Right == Sentinel)
                    return InsertRight(x, z);
                x = x.Right;
            }
        }
    }

    public void Delete(TestNode node)
    {
        var (removed, replacement, removedWasRed) = BstRemove(node);
        if (!removedWasRed)
            DeleteFixup(replacement);
        ResetSentinel();
    }

    public TestNode? Find(int value)
    {
        var x = _root;
        while (x != Sentinel)
        {
            if (value < x.Value)
                x = x.Left;
            else if (value > x.Value)
                x = x.Right;
            else
                return x;
        }
        return null;
    }

    public List<int> GetInOrderTraversal()
    {
        var result = new List<int>();
        var node = _root;
        if (node == Sentinel)
            return result;

        // Find leftmost
        node = Leftest(node);

        while (node != Sentinel)
        {
            result.Add(node.Value);
            node = Next(node);
        }
        return result;
    }

    public bool IsEmpty() => _root == Sentinel;

    public TestNode GetRoot() => _root;

    public TestNode SentinelNode => Sentinel;

    public string DumpTree()
    {
        if (_root == Sentinel)
            return "(empty)";
        return DumpNode(_root, "", true);
    }

    private string DumpNode(TestNode node, string indent, bool isLast)
    {
        if (node == Sentinel)
            return indent + (isLast ? "└── " : "├── ") + "S(B)\n";

        var color = node.Color == NodeColor.Red ? "R" : "B";
        var line = indent + (isLast ? "└── " : "├── ") + $"{node.Value}({color})\n";
        var childIndent = indent + (isLast ? "    " : "│   ");
        line += DumpNode(node.Left, childIndent, false);
        line += DumpNode(node.Right, childIndent, true);
        return line;
    }
}
