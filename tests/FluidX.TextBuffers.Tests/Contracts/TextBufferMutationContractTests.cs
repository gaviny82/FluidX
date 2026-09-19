using FluidX.TextBuffers.Tests.Infrastructure;

namespace FluidX.TextBuffers.Tests.Contracts;

public abstract class TextBufferMutationContractTests
{
    protected abstract ITextBuffer Create(string text);

    private void ApplyAndAssert(string original, params (int Offset, int Length, string Text)[] edits)
    {
        ITextBuffer buffer = Create(original);
        var reference = new ReferenceText(original);
        TextReplacement[] replacements = edits.Select(edit =>
            new TextReplacement(new TextRange(reference.PositionAt(edit.Offset), reference.PositionAt(edit.Offset + edit.Length)), edit.Text)).ToArray();
        string expected = reference.Apply(replacements);
        buffer.ApplyEdits(replacements);
        BufferAssertions.Matches(expected, buffer);
    }

    [TestMethod]
    public void Insert_AtStartMiddleAndEnd()
    {
        ApplyAndAssert("abc", (0, 0, "X"));
        ApplyAndAssert("abc", (1, 0, "X"));
        ApplyAndAssert("abc", (3, 0, "X"));
        ApplyAndAssert("", (0, 0, "A\r\n😀"));
    }

    [TestMethod]
    public void Insert_PreservesMixedEolAndArbitraryUtf16()
    {
        ApplyAndAssert("a\r\nb", (1, 0, "\nX\rY\r\n"));
        ApplyAndAssert("😀", (1, 0, "\0\uFEFF"));
    }

    [TestMethod]
    public void Delete_HandlesBoundariesLinesAndPartialCodeUnits()
    {
        ApplyAndAssert("abc", (0, 1, ""));
        ApplyAndAssert("abc", (1, 1, ""));
        ApplyAndAssert("abc", (2, 1, ""));
        ApplyAndAssert("a\r\nb\nc", (1, 4, ""));
        ApplyAndAssert("😀", (0, 1, ""));
    }

    [TestMethod]
    public void Delete_TextBetweenCrAndLf_FormsSingleCrlfLineBreak()
        => ApplyAndAssert("a\rX\nb", (2, 1, ""));

    [TestMethod]
    public void Replace_TextStartingWithLfCombinesWithPreviousBareCr()
        => ApplyAndAssert("a\rYb", (2, 1, "\nX"));

    [TestMethod]
    public void Replace_TextEndingWithCrCombinesWithNextBareLf()
        => ApplyAndAssert("aY\nb", (1, 1, "X\r"));

    [TestMethod]
    public void Replace_HandlesSizeAndLineChanges()
    {
        ApplyAndAssert("abcdef", (1, 4, "X"));
        ApplyAndAssert("abcdef", (1, 2, "XY"));
        ApplyAndAssert("abcdef", (1, 1, "XYZ"));
        ApplyAndAssert("a\r\nb\nc", (1, 4, "X\rY"));
        ApplyAndAssert("abc", (0, 3, "\r\n😀"));
        ApplyAndAssert("a\r\nb", (1, 2, "\n"));
    }

    [TestMethod]
    public void NoOpEdits_LeaveDocumentUnchanged()
    {
        ITextBuffer buffer = Create("a\r\nb");
        buffer.ApplyEdits([]);
        buffer.ApplyEdits([new(buffer.GetRangeAt(1, 0), "")]);
        buffer.ApplyEdits([new(buffer.GetRangeAt(3, 1), "b")]);
        BufferAssertions.Matches("a\r\nb", buffer);
    }

    [TestMethod]
    public void BatchEdits_UsePreEditCoordinates()
    {
        ApplyAndAssert("0123456789", (0, 1, "AA"), (3, 2, ""), (7, 2, "Z"), (10, 0, "!"));
        ApplyAndAssert("a\r\nb\nc", (0, 0, "X\n"), (3, 2, "Y\r"), (6, 0, "!"));
    }

    [TestMethod]
    public void BatchEdits_AllowTouchingRanges()
    {
        ApplyAndAssert("abcdef", (0, 2, "A"), (2, 2, "B"), (4, 2, "C"));
    }

    [TestMethod]
    public void BatchEdits_PreserveSamePositionInsertionOrder()
    {
        ApplyAndAssert("ab", (1, 0, "X"), (1, 0, "\nY"), (1, 0, "Z\r"));
    }

    [TestMethod]
    public void BatchEdits_ReclassifyCrlfAcrossEveryEditedBoundary()
    {
        ApplyAndAssert("a\rX\nb\rY\nc", (2, 1, ""), (6, 1, ""));
    }

    [TestMethod]
    public void NormalizeEol_RewritesAllLineBreaks()
    {
        foreach (string target in new[] { "\n", "\r\n" })
        {
            ITextBuffer buffer = Create("\rone\r\ntwo\n\nthree\r");
            buffer.NormalizeEOL(target);
            string expected = string.Join(target, new[] { "", "one", "two", "", "three", "" });
            BufferAssertions.Matches(expected, buffer);
            buffer.NormalizeEOL(target);
            BufferAssertions.Matches(expected, buffer);
        }
    }

    [TestMethod]
    public void NormalizeEol_LeavesDocumentsWithoutBreaksUnchanged()
    {
        foreach (string text in new[] { "", "abc", "😀" })
        {
            ITextBuffer buffer = Create(text);
            buffer.NormalizeEOL("\n");
            BufferAssertions.Matches(text, buffer);
            buffer.NormalizeEOL("\r\n");
            BufferAssertions.Matches(text, buffer);
        }
    }

    [TestMethod]
    public void NormalizeEol_TreatsCrlfFormedByAnEditAsOneLineBreak()
    {
        ITextBuffer buffer = Create("a\rX\nb");
        buffer.ApplyEdits([new(buffer.GetRangeAt(2, 1), "")]);
        BufferAssertions.Matches("a\r\nb", buffer);

        buffer.NormalizeEOL("\n");
        BufferAssertions.Matches("a\nb", buffer);
    }

    [TestMethod]
    public void MutationSequences_MatchStringReferenceModel()
    {
        string expected = "one\r\ntwo\n😀";
        ITextBuffer buffer = Create(expected);
        var random = new Random(7319);
        string[] insertions = ["", "x", "\n", "\r", "\r\n", "😀", "a\0b"];
        // Keep the generated smoke sequence bounded. Boundary interactions have
        // dedicated cases above, which also produce much smaller failure reports.
        for (int step = 0; step < 10; step++)
        {
            int[] validOffsets = Enumerable.Range(0, expected.Length + 1)
                .Where(offset => offset == 0 || offset == expected.Length
                    || expected[offset - 1] != '\r' || expected[offset] != '\n')
                .ToArray();
            int offset = validOffsets[random.Next(validOffsets.Length)];
            int[] validEnds = validOffsets.Where(end => end >= offset && end <= offset + 5).ToArray();
            int end = validEnds[random.Next(validEnds.Length)];
            int length = end - offset;
            string inserted = insertions[random.Next(insertions.Length)];
            buffer.ApplyEdits([new(buffer.GetRangeAt(offset, length), inserted)]);
            expected = expected.Remove(offset, length).Insert(offset, inserted);
            try
            {
                BufferAssertions.Matches(expected, buffer);
            }
            catch (Exception exception)
            {
                Assert.Fail($"Step {step}: offset={offset}, length={length}, inserted={Escape(inserted)}, expected={Escape(expected)}. {exception}");
            }
        }
    }

    private static string Escape(string value) => value
        .Replace("\0", "\\0")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n");

    [TestMethod]
    public void BufferCanBeReusedAfterDeletingEverything()
    {
        ITextBuffer buffer = Create("a\r\nb");
        buffer.ApplyEdits([new(buffer.GetRangeAt(0, buffer.Length), "")]);
        BufferAssertions.Matches("", buffer);
        buffer.ApplyEdits([new(buffer.GetRangeAt(0, 0), "x\ny")]);
        BufferAssertions.Matches("x\ny", buffer);
    }
}

[TestClass]
public sealed class LineArrayTextBufferMutationContractTests : TextBufferMutationContractTests
{
    protected override ITextBuffer Create(string text) => TextBufferFactory.LineArray(text);
}

[TestClass]
public sealed class PieceTreeTextBufferMutationContractTests : TextBufferMutationContractTests
{
    protected override ITextBuffer Create(string text) => TextBufferFactory.PieceTree(text);
}
