using FluidX.TextBuffers.Contracts;

namespace FluidX.TextBuffers.PieceTree;

/// <summary>
/// Readonly snapshot for <see cref="PieceTreeTextBuffer"/>.
/// In a real multiple thread environment, to make snapshot reading always work correctly, we need to <br/>
/// 1. Make <see cref="TreeNode.Piece"/> immutable, then reading and writing can run in parallel. <br/>
/// 2. TreeNode/Buffers normalization should not happen during snapshot reading.
/// </summary>
public class PieceTreeSnapshot : ITextSnapshot
{
    private readonly List<Piece> _pieces;
    private readonly PieceTreeBase _tree;
    private readonly string _BOM;

    private int _index;

    /// <summary>
    /// Creates a snapshot of the given piece tree.
    /// </summary>
    /// <param name="tree">Piece tree to snapshot</param>
    /// <param name="BOM">Byte order mark added to the start of this snapshot</param>
    public PieceTreeSnapshot(PieceTreeBase tree, string BOM)
    {
        _pieces = [];
        _tree = tree;
        _BOM = BOM;
        _index = 0;
        if (tree.Root != TreeNode.Sentinel)
        {
            PieceTreeBase.Iterate(tree.Root, node =>
            {
                if (node != TreeNode.Sentinel)
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
                return _BOM;
            }
            else
            {
                return null;
            }
        }

        if (_index > _pieces.Count - 1)
            return null;

        if (_index == 0)
            return _BOM + _tree.GetPieceContent(_pieces[_index++]);

        return _tree.GetPieceContent(_pieces[_index++]);
    }
}
