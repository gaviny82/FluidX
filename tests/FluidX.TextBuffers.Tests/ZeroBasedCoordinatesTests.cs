using System.Text.RegularExpressions;
using FluidX.TextBuffers.LineArray;
using FluidX.TextBuffers.PieceTree;

namespace FluidX.TextBuffers.Tests;

[TestClass]
public sealed class ZeroBasedCoordinatesTests
{
    private static ITextBuffer[] Buffers(string text)
        => [new LineArrayTextBuffer(text), PieceTreeTextBuffer.Create(text)];

    [TestMethod]
    public void PositionsAndLinesStartAtZero()
    {
        foreach (string text in new[] { "", "abc", "a\r\nb\nc\r", "\n\n", "😀\nx" })
        foreach (ITextBuffer buffer in Buffers(text))
        {
            Assert.AreEqual(default(TextPosition), buffer.GetPositionAt(0));
            int offset = 0;
            for (int line = 0; line < buffer.LineCount; line++)
            {
                string content = buffer.GetLineContent(line);
                Assert.AreEqual(content.Length, buffer.GetLineLength(line));
                for (int column = 0; column <= content.Length; column++)
                {
                    var position = new TextPosition(line, column);
                    Assert.AreEqual(offset + column, buffer.GetOffsetAt(position));
                    Assert.AreEqual(position, buffer.GetPositionAt(offset + column));
                    if (column < content.Length)
                        Assert.AreEqual(content[column], buffer.GetChar(position));
                }
                offset += content.Length + buffer.GetLineEOL(line).Length;
            }
            Assert.AreEqual(text, buffer.GetTextInRange(new TextRange(default, buffer.GetPositionAt(text.Length))));
        }
    }

    [TestMethod]
    public void WhitespaceAndSearchUseZeroBasedColumns()
    {
        foreach (ITextBuffer buffer in Buffers("foo foo\n  foo\n \t"))
        {
            Assert.AreEqual(0, buffer.GetLineFirstNonWhitespaceColumnIndex(0));
            Assert.AreEqual(7, buffer.GetLineLastNonWhitespaceColumnIndex(0));
            Assert.AreEqual(2, buffer.GetLineFirstNonWhitespaceColumnIndex(1));
            Assert.AreEqual(5, buffer.GetLineLastNonWhitespaceColumnIndex(1));
            Assert.AreEqual(-1, buffer.GetLineFirstNonWhitespaceColumnIndex(2));
            Assert.AreEqual(-1, buffer.GetLineLastNonWhitespaceColumnIndex(2));
            foreach (string? simpleSearch in new string?[] { "foo", null })
            {
                var matches = buffer.FindMatchesLineByLine(new(0, 1, 1, 5),
                    new SearchData(new Regex("foo"), null, simpleSearch), true, 10);
                CollectionAssert.AreEqual(new[] { new TextRange(0, 4, 0, 7), new TextRange(1, 2, 1, 5) },
                    matches.Select(match => match.Range).ToArray());
            }
        }
    }

    [TestMethod]
    public void FragmentedTreeMatchesLineArrayAndUndoRanges()
    {
        ITextBuffer[] buffers = Buffers("first\nsecond\nthird\n");
        var random = new Random(123);
        string expected = "first\nsecond\nthird\n";
        string[] insertions = ["", "x", "\n", "ab\ncd", "😀"];
        for (int edit = 0; edit < 1_000; edit++)
        {
            int offset = random.Next(expected.Length + 1);
            int length = random.Next(Math.Min(4, expected.Length - offset) + 1);
            string inserted = insertions[random.Next(insertions.Length)];
            string previous = expected;
            expected = expected.Remove(offset, length).Insert(offset, inserted);
            foreach (ITextBuffer buffer in buffers)
            {
                var result = buffer.ApplyEdits([new(buffer.GetRangeAt(offset, length), inserted)], true);
                Assert.AreEqual(expected, buffer.GetTextInRange(buffer.GetRangeAt(0, buffer.Length)), $"Edit {edit}, buffer {buffer.GetType().Name}");
                Assert.IsNotNull(result.ReverseEdits);
                buffer.ApplyEdits(result.ReverseEdits.Select(e => new TextReplacement(e.Range, e.Text)).ToArray(), false);
                Assert.AreEqual(previous, buffer.GetTextInRange(buffer.GetRangeAt(0, buffer.Length)), $"Undo edit {edit}, offset {offset}, length {length}, inserted {inserted}, buffer {buffer.GetType().Name}");
                buffer.ApplyEdits([new(buffer.GetRangeAt(offset, length), inserted)], false);
                Assert.AreEqual(expected, buffer.GetTextInRange(buffer.GetRangeAt(0, buffer.Length)), $"Redo edit {edit}, buffer {buffer.GetType().Name}");
            }
            for (int i = 0; i <= expected.Length; i++)
                Assert.AreEqual(buffers[0].GetPositionAt(i), buffers[1].GetPositionAt(i), $"Edit {edit}, offset {i}");
            for (int line = 0; line < buffers[0].LineCount; line++)
            {
                Assert.AreEqual(buffers[0].GetLineContent(line), buffers[1].GetLineContent(line));
                Assert.AreEqual(buffers[0].GetLineLength(line), buffers[1].GetLineLength(line));
            }
        }
    }

    [TestMethod]
    public void ValueTypesCompareAndContainZeroBasedPositions()
    {
        Assert.IsLessThan(0, new TextPosition(0, 9).CompareTo(new(1, 0)));
        Assert.IsGreaterThan(0, new TextPosition(1, 0).CompareTo(new(0, 9)));
        var range = new TextRange(1, 2, 0, 0);
        Assert.AreEqual(default(TextPosition), range.StartPosition);
        Assert.IsTrue(range.ContainsPosition(default));
        Assert.AreEqual(new TextRange(0, 0, 0, 0), range.CollapseToStart());
        Assert.AreEqual(range, range.PlusRange(default));
    }
}
