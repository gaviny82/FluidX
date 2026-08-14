namespace FluidX.Common.DataStructures.RbTrees;

public interface IRbTreeNode<TSelf> where TSelf : class, IRbTreeNode<TSelf>
{
    TSelf Parent { get; set; }
    TSelf Left { get; set; }
    TSelf Right { get; set; }
    NodeColor Color { get; set; }
    bool IsSentinel { get; }
    void Detach();
}
