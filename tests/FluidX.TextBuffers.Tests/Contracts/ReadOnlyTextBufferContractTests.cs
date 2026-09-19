using FluidX.TextBuffers.Tests.Infrastructure;

namespace FluidX.TextBuffers.Tests.Contracts;

public abstract class ReadOnlyTextBufferContractTests
{
    protected abstract IReadOnlyTextBuffer Create(string text);

    public static IEnumerable<object[]> RepresentativeTexts =>
    [
        [""], ["a"], ["abc"], ["\n"], ["\r"], ["\r\n"], ["\n\n"],
        ["one\r\ntwo\nthree\rfour"], ["\rfirst\r\n\nlast\n"],
        ["A😀e\u0301中\0\uFEFF"], ["\uD800x\uDC00"]
    ];

    [TestMethod]
    [DynamicData(nameof(RepresentativeTexts))]
    public void DocumentAndLines_PreserveRawText(string text) => BufferAssertions.Matches(text, Create(text));

    [TestMethod]
    [DynamicData(nameof(RepresentativeTexts))]
    public void Coordinates_RoundTripEveryUtf16Boundary(string text)
    {
        var reference = new ReferenceText(text);
        IReadOnlyTextBuffer buffer = Create(text);
        foreach (int offset in Enumerable.Range(0, text.Length + 1).Reverse())
        {
            TextPosition expected = reference.PositionAt(offset);
            Assert.AreEqual(expected, buffer.GetPositionAt(offset));
            Assert.AreEqual(offset, buffer.GetOffsetAt(expected));
        }
    }

    [TestMethod]
    public void Ranges_ReturnExactUtf16Slices()
    {
        const string text = "a\r\n😀\nb\r";
        IReadOnlyTextBuffer buffer = Create(text);
        for (int start = 0; start <= text.Length; start++)
        for (int length = 0; length <= text.Length - start; length++)
        {
            TextRange range = buffer.GetRangeAt(start, length);
            Assert.AreEqual(buffer.GetPositionAt(start), range.StartPosition, $"Start {start}, length {length}");
            Assert.AreEqual(buffer.GetPositionAt(start + length), range.EndPosition, $"Start {start}, length {length}");
            Assert.AreEqual(text.Substring(start, length), buffer.GetTextInRange(range));
            Assert.AreEqual(length, buffer.GetTextLengthInRange(range));
        }
    }

    [TestMethod]
    public void Characters_ReturnIndividualEolAndSurrogateCodeUnits()
    {
        const string text = "\0\r\n😀\uD800x\uDC00\uFEFF";
        IReadOnlyTextBuffer buffer = Create(text);
        for (int offset = text.Length - 1; offset >= 0; offset--)
        {
            Assert.AreEqual(text[offset], buffer.GetChar(offset));
            Assert.AreEqual(text[offset], buffer.GetChar(buffer.GetPositionAt(offset)));
        }
    }

    [TestMethod]
    public void WhitespaceColumns_UseExclusiveLastIndex()
    {
        IReadOnlyTextBuffer buffer = Create("\n \n\t\t\n  abc  \n\tab c\t");
        int[] first = [-1, -1, -1, 2, 1];
        int[] last = [-1, -1, -1, 5, 5];
        for (int line = 0; line < first.Length; line++)
        {
            Assert.AreEqual(first[line], buffer.GetLineFirstNonWhitespaceColumnIndex(line));
            Assert.AreEqual(last[line], buffer.GetLineLastNonWhitespaceColumnIndex(line));
        }
    }

    [TestMethod]
    public void RepeatedReads_DoNotChangeObservableState()
    {
        const string text = "a\r\nb\n😀";
        IReadOnlyTextBuffer buffer = Create(text);
        BufferAssertions.Matches(text, buffer);
        for (int i = 0; i < 3; i++)
        {
            _ = buffer.GetLinesContent();
            _ = buffer.GetTextInRange(buffer.GetRangeAt(1, 4));
            _ = buffer.GetPositionAt(3);
        }
        BufferAssertions.Matches(text, buffer);
    }
}

[TestClass]
public sealed class LineArrayReadOnlyTextBufferContractTests : ReadOnlyTextBufferContractTests
{
    protected override IReadOnlyTextBuffer Create(string text) => TextBufferFactory.LineArray(text);
}

[TestClass]
public sealed class PieceTreeReadOnlyTextBufferContractTests : ReadOnlyTextBufferContractTests
{
    protected override IReadOnlyTextBuffer Create(string text) => TextBufferFactory.PieceTree(text);
}

