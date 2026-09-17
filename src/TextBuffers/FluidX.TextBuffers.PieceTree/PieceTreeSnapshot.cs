using FluidX.Common.DataStructures.RbTrees;

namespace FluidX.TextBuffers.PieceTree;

using TreeNode = FluidX.Common.DataStructures.RbTrees.RedBlackTree<PieceNodeData>.TreeNode;

/// <summary>
/// Readonly snapshot for <see cref="PieceTreeTextBuffer"/>.
/// In a real multiple thread environment, to make snapshot reading always work correctly, we need to <br/>
/// 1. Make <see cref="PieceNodeData.Piece"/> immutable, then reading and writing can run in parallel. <br/>
/// 2. TreeNode/Buffers normalization should not happen during snapshot reading.
/// </summary>
public class PieceTreeSnapshot : ITextSnapshot
{
    private readonly List<Piece> _pieces;
    private readonly PieceTreeTextBuffer _tree;
    private int _index;

    /// <summary>
    /// Creates a snapshot of the given piece tree.
    /// </summary>
    /// <param name="tree">Piece tree to snapshot</param>
    public PieceTreeSnapshot(PieceTreeTextBuffer tree)
    {
        _pieces = [];
        _tree = tree;
        _index = 0;
        if (!tree._pieceTree.Root.IsSentinel)
        {
            PieceTree.IterateInOrder(tree._pieceTree.Root, node =>
            {
                _pieces.Add(node.Piece);
                return true;
            });
        }
    }

    ///<inheritdoc/>
    public string? Read()
    {
        if (_pieces.Count == 0)
        {
            if (_index == 0)
            {
                _index++;
                return string.Empty;
            }
            else
            {
                return null;
            }
        }

        if (_index > _pieces.Count - 1)
            return null;

        return _tree.GetPieceContent(_pieces[_index++]);
    }
}
