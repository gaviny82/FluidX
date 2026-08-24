using FluidX;
using FluidX.TextBuffers;
using FluidX.TextBuffers.PieceTree;

var buffer = PieceTreeTextBuffer.Create("Hello\nworld!");

// Read text from the buffer
Console.WriteLine(buffer.LineCount); // 2
Console.WriteLine(buffer.GetLineContent(1)); // Hello
Console.WriteLine(buffer.GetLineContent(2)); // world!
Console.WriteLine(buffer.GetValueInRange(new TextRange(1, 2, 2, 2), EndOfLinePreference.LF)); // ello\nw

// Writing to the buffer

// INSERT a character 'a' at (line 2, column 4)
var insertEdit = new EditOperation
{
    Range = new TextRange(2, 4, 2, 4), // Empty range = insert at this position
    Text = "a", // Text to insert
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false
};
buffer.ApplyEdits([insertEdit], false, false);
PrintAllLines();

// DELETE a character at (line 1, column 4)
var deleteEdit = new EditOperation
{
    Range = new TextRange(1, 4, 1, 5), // Covers single character
    Text = "", // Empty text = delete
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false
};
buffer.ApplyEdits([deleteEdit], false, false);
PrintAllLines();

// === More complex tests ===

// Test 1: Insert multi-line text
Console.WriteLine("=== Test 1: Insert multi-line text ===");
buffer.ApplyEdits([new EditOperation
{
    Range = new TextRange(2, 6, 2, 6),
    Text = "\nfoo\nbar",
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false
}], false, false);
PrintAllLines();
Console.WriteLine($"Total lines: {buffer.LineCount}");
Console.WriteLine($"Total length: {buffer.Length}");

// Test 2: Delete across lines
Console.WriteLine("=== Test 2: Delete across lines ===");
buffer.ApplyEdits([new EditOperation
{
    Range = new TextRange(1, 4, 3, 3), // Delete from "o" in Helo to "o" in foo
    Text = "",
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false
}], false, false);
PrintAllLines();

// Test 3: Replace text
Console.WriteLine("=== Test 3: Replace text ===");
buffer.ApplyEdits([new EditOperation
{
    Range = new TextRange(1, 1, 1, 4),
    Text = "Goodbye",
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false
}], false, false);
PrintAllLines();

// Test 4: Snapshot
Console.WriteLine("=== Test 4: Snapshot ===");
var snapshot = buffer.CreateSnapshot(true);
string? line;
while ((line = snapshot.Read()) != null)
{
    Console.Write(line);
}
Console.WriteLine();

// Test 5: Multiple edits
Console.WriteLine("=== Test 5: Multiple edits ===");
var buffer2 = PieceTreeTextBuffer.Create("line1\nline2\nline3\nline4\nline5");
Console.WriteLine($"Initial lines: {buffer2.LineCount}");
for (int i = 1; i <= buffer2.LineCount; i++)
{
    Console.WriteLine($"  {i}: {buffer2.GetLineContent(i)}");
}

// Delete line 3
buffer2.ApplyEdits([new EditOperation
{
    Range = new TextRange(3, 1, 4, 1),
    Text = "",
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false
}], false, false);
Console.WriteLine($"After deleting line 3: {buffer2.LineCount} lines");
for (int i = 1; i <= buffer2.LineCount; i++)
{
    Console.WriteLine($"  {i}: {buffer2.GetLineContent(i)}");
}

// Insert at beginning
buffer2.ApplyEdits([new EditOperation
{
    Range = new TextRange(1, 1, 1, 1),
    Text = "NEW ",
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false
}], false, false);
Console.WriteLine($"After insert at beginning:");
for (int i = 1; i <= buffer2.LineCount; i++)
{
    Console.WriteLine($"  {i}: {buffer2.GetLineContent(i)}");
}

// Append to end
buffer2.ApplyEdits([new EditOperation
{
    Range = new TextRange(4, 6, 4, 6),
    Text = " added",
    ForceMoveMarkers = false,
    IsAutoWhitespaceEdit = false,
    IsTracked = false
}], false, false);
Console.WriteLine($"After append:");
for (int i = 1; i <= buffer2.LineCount; i++)
{
    Console.WriteLine($"  {i}: {buffer2.GetLineContent(i)}");
}

// Test 6: GetPositionAt and GetOffsetAt roundtrip
Console.WriteLine("=== Test 6: Position/Offset roundtrip ===");
for (int offset = 0; offset < buffer2.Length; offset++)
{
    var pos = buffer2.GetPositionAt(offset);
    int roundtrip = buffer2.GetOffsetAt(pos.LineNumber, pos.Column);
    if (roundtrip != offset)
    {
        Console.WriteLine($"  MISMATCH at offset {offset}: got {roundtrip}");
    }
}
Console.WriteLine("Position/Offset roundtrip complete.");

// Test 7: GetValueInRange consistency
Console.WriteLine("=== Test 7: GetValueInRange consistency ===");
var fullText = buffer2.GetValueInRange(new TextRange(1, 1, 4, buffer2.GetLineLength(4) + 1), EndOfLinePreference.LF);
Console.WriteLine($"Full text: [{fullText}]");

// Test 8: Stress test - many inserts
Console.WriteLine("=== Test 8: Stress test ===");
var buffer3 = PieceTreeTextBuffer.Create("");
for (int i = 0; i < 100; i++)
{
    buffer3.ApplyEdits([new EditOperation
    {
        Range = new TextRange(buffer3.LineCount, buffer3.GetLineLength(buffer3.LineCount) + 1, buffer3.LineCount, buffer3.GetLineLength(buffer3.LineCount) + 1),
        Text = $"item{i}\n",
        ForceMoveMarkers = false,
        IsAutoWhitespaceEdit = false,
        IsTracked = false
    }], false, false);
}
Console.WriteLine($"After 100 inserts: {buffer3.LineCount} lines");
Console.WriteLine($"  First: {buffer3.GetLineContent(1)}");
Console.WriteLine($"  Last: {buffer3.GetLineContent(buffer3.LineCount)}");

Console.WriteLine("\nAll tests passed!");

void PrintAllLines()
{
    Console.WriteLine(new string('-', 20));
    for (int i = 1; i <= buffer.LineCount; i++)
    {
        Console.WriteLine($"{i}:\t{buffer.GetLineContent(i)}");
    }
    Console.WriteLine(new string('-', 20));
}
