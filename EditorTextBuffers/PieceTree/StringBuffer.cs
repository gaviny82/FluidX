namespace EditorTextBuffers.PieceTree;

public class StringBuffer
{
    public string Buffer { get; set; }
    public IReadOnlyList<int> LineStarts { get; set; }

    public StringBuffer(string buffer, IReadOnlyList<int> lineStarts)
    {
        Buffer = buffer;
        LineStarts = lineStarts;
    }
}
