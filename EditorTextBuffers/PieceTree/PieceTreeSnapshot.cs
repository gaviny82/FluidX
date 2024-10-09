using EditorTextBuffers.Contracts;

namespace EditorTextBuffers.PieceTree;

public class PieceTreeSnapshot : ITextSnapshot
{
    private readonly Piece[] _pieces;
    private readonly PieceTreeBase _tree;
    private readonly string _BOM;

    private int _index;

    public PieceTreeSnapshot(PieceTreeBase tree, string BOM)
    {
        _pieces = [];
        _tree = tree;
        _BOM = BOM;
        _index = 0;
        if (tree.Root != TreeNode.Sentinel)
        {
            // TODO: complete after PieceTreeBase is implemented
            throw new NotImplementedException();
        }
    }

    public string? Read()
    {
        // TODO: complete after PieceTreeBase is implemented
        throw new NotImplementedException();
    }
}
