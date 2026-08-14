using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.Common.Tests.DataStructures.RbTrees;

internal sealed class TestNode : IRbTreeNode<TestNode>
{
    public TestNode Parent { get; set; } = null!;
    public TestNode Left { get; set; } = null!;
    public TestNode Right { get; set; } = null!;
    public NodeColor Color { get; set; }
    public bool IsSentinel { get; }

    public int Value { get; set; }

    public TestNode(int value, bool isSentinel = false)
    {
        Value = value;
        IsSentinel = isSentinel;
        Parent = this;
        Left = this;
        Right = this;
    }

    public void Detach()
    {
        Parent = null!;
        Left = null!;
        Right = null!;
    }
}
