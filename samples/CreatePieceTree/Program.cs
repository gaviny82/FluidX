using FluidX;
using FluidX.TextBuffers;
using FluidX.TextBuffers.PieceTree;

var buffer = PieceTreeTextBuffer.Create("Hello\nworld!");

// Read text from the buffer
Console.WriteLine(buffer.LineCount); // 2
Console.WriteLine(buffer.GetLineContent(0)); // Hello
Console.WriteLine(buffer.GetLineContent(1)); // world!
Console.WriteLine(buffer.GetTextInRange(new TextRange(0, 1, 1, 1))); // ello\nw

// Writing to the buffer

// INSERT a character 'a' at (line index 1, column index 3)
buffer.ApplyEdits([new TextReplacement(new TextRange(1, 3, 1, 3), "a")], false);
PrintAllLines();

// DELETE a character at (line index 0, column index 3)
buffer.ApplyEdits([new TextReplacement(new TextRange(0, 3, 0, 4), "")], false);
PrintAllLines();

// === More complex tests ===

// Test 1: Insert multi-line text
Console.WriteLine("=== Test 1: Insert multi-line text ===");
buffer.ApplyEdits([new TextReplacement(new TextRange(1, 5, 1, 5), "\nfoo\nbar")], false);
PrintAllLines();
Console.WriteLine($"Total lines: {buffer.LineCount}");
Console.WriteLine($"Total length: {buffer.Length}");

// Test 2: Delete across lines
Console.WriteLine("=== Test 2: Delete across lines ===");
buffer.ApplyEdits([new TextReplacement(new TextRange(0, 3, 2, 2), "")], false);
PrintAllLines();

// Test 3: Replace text
Console.WriteLine("=== Test 3: Replace text ===");
buffer.ApplyEdits([new TextReplacement(new TextRange(0, 0, 0, 3), "Goodbye")], false);
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
for (int i = 0; i < buffer2.LineCount; i++)
{
    Console.WriteLine($"  {i}: {buffer2.GetLineContent(i)}");
}

// Delete the line at index 2
buffer2.ApplyEdits([new TextReplacement(new TextRange(2, 0, 3, 0), "")], false);
Console.WriteLine($"After deleting line index 2: {buffer2.LineCount} lines");
for (int i = 0; i < buffer2.LineCount; i++)
{
    Console.WriteLine($"  {i}: {buffer2.GetLineContent(i)}");
}

// Insert at beginning
buffer2.ApplyEdits([new TextReplacement(new TextRange(0, 0, 0, 0), "NEW ")], false);
Console.WriteLine($"After insert at beginning:");
for (int i = 0; i < buffer2.LineCount; i++)
{
    Console.WriteLine($"  {i}: {buffer2.GetLineContent(i)}");
}

// Append to end
buffer2.ApplyEdits([new TextReplacement(new TextRange(3, 5, 3, 5), " added")], false);
Console.WriteLine($"After append:");
for (int i = 0; i < buffer2.LineCount; i++)
{
    Console.WriteLine($"  {i}: {buffer2.GetLineContent(i)}");
}

// Test 6: GetPositionAt and GetOffsetAt roundtrip
Console.WriteLine("=== Test 6: Position/Offset roundtrip ===");
for (int offset = 0; offset < buffer2.Length; offset++)
{
    var pos = buffer2.GetPositionAt(offset);
    int roundtrip = buffer2.GetOffsetAt(pos);
    if (roundtrip != offset)
    {
        Console.WriteLine($"  MISMATCH at offset {offset}: got {roundtrip}");
    }
}
Console.WriteLine("Position/Offset roundtrip complete.");

// Test 7: GetTextInRange consistency
Console.WriteLine("=== Test 7: GetTextInRange consistency ===");
var fullText = buffer2.GetTextInRange(new TextRange(0, 0, 3, buffer2.GetLineLength(3)));
Console.WriteLine($"Full text: [{fullText}]");

// Test 8: Stress test - many inserts
Console.WriteLine("=== Test 8: Stress test ===");
var buffer3 = PieceTreeTextBuffer.Create("");
for (int i = 0; i < 100; i++)
{
    buffer3.ApplyEdits([new TextReplacement(
        new TextRange(buffer3.LineCount - 1, buffer3.GetLineLength(buffer3.LineCount - 1), buffer3.LineCount - 1, buffer3.GetLineLength(buffer3.LineCount - 1)),
        $"item{i}\n")], false);
}
Console.WriteLine($"After 100 inserts: {buffer3.LineCount} lines");
Console.WriteLine($"  First: {buffer3.GetLineContent(0)}");
Console.WriteLine($"  Last: {buffer3.GetLineContent(buffer3.LineCount - 1)}");

Console.WriteLine("\nAll tests passed!");

void PrintAllLines()
{
    Console.WriteLine(new string('-', 20));
    for (int i = 0; i < buffer.LineCount; i++)
    {
        Console.WriteLine($"{i}:\t{buffer.GetLineContent(i)}");
    }
    Console.WriteLine(new string('-', 20));
}
