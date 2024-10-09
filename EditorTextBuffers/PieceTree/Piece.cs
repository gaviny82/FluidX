namespace EditorTextBuffers.PieceTree;

public class Piece
{
    public int BufferIndex { get; }
    public BufferCursor Start { get; }
    public BufferCursor End { get; }
    public int Length { get; }
    public int LineFeedCount { get; }

    public Piece(int bufferIndex, BufferCursor start, BufferCursor end, int lineFeedCount, int length)
    {
        BufferIndex = bufferIndex;
        Start = start;
        End = end;
        LineFeedCount = lineFeedCount;
        Length = length;
    }
}