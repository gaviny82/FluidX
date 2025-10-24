using FluidX.TextBuffers;
using FluidX.TextBuffers.PieceTree;

var pieceTreeBuilder = new PieceTreeTextBufferBuilder();
pieceTreeBuilder.AcceptChunk("Hello\n");
pieceTreeBuilder.AcceptChunk("world!");
var pieceTreeFactory = pieceTreeBuilder.Finish(true);
var buffer = pieceTreeFactory.Create(DefaultEndOfLine.LF);

// Read text from the buffer
Console.WriteLine(buffer.LineCount); // 2
Console.WriteLine(buffer.GetLineContent(1)); // Hello
Console.WriteLine(buffer.GetLineContent(2)); // world
Console.WriteLine(buffer.GetValueInRange(new FluidX.TextBuffers.Range(1, 2, 2, 2), EndOfLinePreference.LF)); // ello\nw

// Writing to the buffer

// INSERT a character 'a' at (line 2, column 4)
var insertEdit = new ValidAnnotatedEditOperation
{
    Range = new FluidX.TextBuffers.Range(2, 4, 2, 4), // Empty range = insert at this position
    Text = "a", // Text to insert
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false,
};
buffer.ApplyEdits([insertEdit], false, false);
PrintAllLines();

// DELETE a character at (line 1, column 4)
// To delete, range must cover the character to remove.
// E.g., to delete a single character at (1,4), use range (1,4)-(1,5)
var deleteEdit = new ValidAnnotatedEditOperation
{
    Range = new FluidX.TextBuffers.Range(1, 4, 1, 5), // Covers single character
    Text = "", // Empty text = delete
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false,
};
buffer.ApplyEdits([deleteEdit], false, false);
PrintAllLines();

void PrintAllLines()
{
    Console.WriteLine(new string('-', 20));
    for (int i = 1; i <= buffer.LineCount; i++)
    {
        Console.WriteLine($"{i}:\t{buffer.GetLineContent(i)}");
    }
    Console.WriteLine(new string('-', 20));
}
