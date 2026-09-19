using System.Text;
using System.Text.RegularExpressions;
using FluidX.TextBuffers.Tests.Infrastructure;

namespace FluidX.TextBuffers.Tests.Contracts;

// Every existing read contract also runs against each snapshot implementation.
public abstract class TextSnapshotContractTests : ReadOnlyTextBufferContractTests
{
    protected abstract ITextBuffer CreateBuffer(string text);
    protected override IReadOnlyTextBuffer Create(string text) => CreateBuffer(text).CreateSnapshot();

    [TestMethod]
    public void Snapshot_IsReadOnlyAndSnapshottingItPreservesContent()
    {
        ITextSnapshot snapshot = CreateBuffer("\uFEFFa\r\nb\rc\n😀").CreateSnapshot();
        Assert.IsFalse(snapshot is ITextBuffer);
        ITextSnapshot second = snapshot.CreateSnapshot();
        BufferAssertions.Matches("\uFEFFa\r\nb\rc\n😀", second);
        Assert.IsTrue(snapshot.Equals(second));
    }

    [TestMethod]
    public void SourceEditsAndNormalization_DoNotChangeEarlierSnapshots()
    {
        const string original = "one\r\ntwo\nthree\rfour";
        ITextBuffer source = CreateBuffer(original);
        ITextSnapshot first = source.CreateSnapshot();
        source.ApplyEdits([new(source.GetRangeAt(0, 0), "prefix\n")]);
        ITextSnapshot second = source.CreateSnapshot();
        source.ApplyEdits([new(source.GetRangeAt(0, 6), "")]);
        source.NormalizeEOL("\r\n");
        source.ApplyEdits([new(source.GetRangeAt(0, source.Length), "replacement")]);
        source.NormalizeEOL("\n");

        BufferAssertions.Matches(original, first);
        BufferAssertions.Matches("prefix\n" + original, second);
        BufferAssertions.Matches("replacement", source);
    }

    [TestMethod]
    public void AppendingLfAfterCapturedCr_PreservesFinalLineStartAndCharacterExtent()
    {
        ITextBuffer source = CreateBuffer("");
        ITextSnapshot empty = source.CreateSnapshot();
        // Insert into the change buffer, rather than creating an immutable original buffer.
        source.ApplyEdits([new(source.GetRangeAt(0, 0), "abc\r")]);
        ITextSnapshot beforeLf = source.CreateSnapshot();
        source.ApplyEdits([new(source.GetRangeAt(source.Length, 0), "\n")]);
        ITextSnapshot afterLf = source.CreateSnapshot();

        BufferAssertions.Matches("", empty);
        BufferAssertions.Matches("abc\r", beforeLf);
        BufferAssertions.Matches("abc\r\n", afterLf);
        AssertEveryRange("abc\r", beforeLf);

        // Grow character and line-start arrays, and later replace the source's storage.
        source.ApplyEdits([new(source.GetRangeAt(source.Length, 0), string.Concat(Enumerable.Repeat("x\n", 200)))]);
        source.ApplyEdits([new(source.GetRangeAt(source.Length, 0), new string('y', 70_000))]);
        source.NormalizeEOL("\r\n");
        BufferAssertions.Matches("abc\r", beforeLf);
        BufferAssertions.Matches("abc\r\n", afterLf);
    }

    [TestMethod]
    public void RepeatedCrLfMerges_PreserveDifferentSnapshotGenerations()
    {
        ITextBuffer source = CreateBuffer("");
        var captures = new List<(string Text, ITextSnapshot Snapshot)>();
        string expected = "";
        foreach (string appended in new[] { "a\r", "\nb\r", "\nc\r", "\n" })
        {
            source.ApplyEdits([new(source.GetRangeAt(source.Length, 0), appended)]);
            expected += appended;
            captures.Add((expected, source.CreateSnapshot()));
            foreach (var capture in captures)
                BufferAssertions.Matches(capture.Text, capture.Snapshot);
        }
    }

    [TestMethod]
    public void FragmentedSnapshots_RemainStableAcrossFurtherEdits()
    {
        ITextBuffer source = CreateBuffer("original\ntext");
        string expected = "original\ntext";
        var random = new Random(7321);
        var captures = new List<(string Text, ITextSnapshot Snapshot)>();
        string[] insertions = ["", "x", "\n", "a\nb", "😀", "\0\uFEFF"];
        for (int step = 0; step < 80; step++)
        {
            int offset = random.Next(expected.Length + 1);
            int length = random.Next(Math.Min(4, expected.Length - offset) + 1);
            string inserted = insertions[random.Next(insertions.Length)];
            source.ApplyEdits([new(source.GetRangeAt(offset, length), inserted)]);
            expected = expected.Remove(offset, length).Insert(offset, inserted);
            if (step % 10 == 0) captures.Add((expected, source.CreateSnapshot()));
            foreach (var capture in captures)
                BufferAssertions.Matches(capture.Text, capture.Snapshot);
        }
        source.ApplyEdits([new(source.GetRangeAt(0, source.Length), "")]);
        foreach (var capture in captures)
            AssertEveryRange(capture.Text, capture.Snapshot);
    }

    [TestMethod]
    [DataRow(17)]
    [DataRow(7321)]
    public void FragmentedMixedLineEndings_PreserveSnapshotCoordinates(int seed)
    {
        string expected = "original\r\ntext\rthird\n";
        ITextBuffer source = CreateBuffer(expected);
        var captures = new List<(string Text, ITextSnapshot Snapshot)>();
        var random = new Random(seed);
        string[] insertions = ["", "x", "\r", "\n", "\r\n", "a\rb\nc", "😀"];
        for (int step = 0; step < 80; step++)
        {
            // Edits may address any boundary except the middle of an existing CRLF.
            int[] boundaries = Enumerable.Range(0, expected.Length + 1)
                .Where(offset => offset == 0 || offset == expected.Length
                    || expected[offset - 1] != '\r' || expected[offset] != '\n').ToArray();
            int startIndex = random.Next(boundaries.Length);
            int offset = boundaries[startIndex];
            int end = boundaries[random.Next(startIndex, boundaries.Length)];
            string inserted = insertions[random.Next(insertions.Length)];
            source.ApplyEdits([new(source.GetRangeAt(offset, end - offset), inserted)]);
            expected = expected.Remove(offset, end - offset).Insert(offset, inserted);
            BufferAssertions.Matches(expected, source.CreateSnapshot());
            if (step % 10 == 0) captures.Add((expected, source.CreateSnapshot()));
            foreach (var capture in captures)
            {
                BufferAssertions.Matches(capture.Text, capture.Snapshot);
                AssertEveryRange(capture.Text, capture.Snapshot);
            }
        }
    }

    [TestMethod]
    public void WhitespaceAndSearch_UseCapturedContent()
    {
        ITextBuffer source = CreateBuffer("");
        const string text = "  alpha \r\n\tbeta\t\n  alpha\r";
        source.ApplyEdits([new(source.GetRangeAt(0, 0), text)]);
        ITextSnapshot snapshot = source.CreateSnapshot();
        source.ApplyEdits([new(source.GetRangeAt(source.Length, 0), "\nchanged")]);
        Assert.AreEqual(2, snapshot.GetLineFirstNonWhitespaceColumnIndex(0));
        Assert.AreEqual(7, snapshot.GetLineLastNonWhitespaceColumnIndex(0));
        Assert.AreEqual(1, snapshot.GetLineFirstNonWhitespaceColumnIndex(1));
        Assert.AreEqual(5, snapshot.GetLineLastNonWhitespaceColumnIndex(1));
        Assert.AreEqual(-1, snapshot.GetLineFirstNonWhitespaceColumnIndex(3));
        Assert.AreEqual(-1, snapshot.GetLineLastNonWhitespaceColumnIndex(3));
        var search = new SearchData(new Regex("(alpha)"), null, null);
        var matches = snapshot.FindMatchesLineByLine(snapshot.GetRangeAt(0, snapshot.Length), search, true, 10);
        Assert.HasCount(2, matches);
        Assert.AreEqual(new TextRange(0, 2, 0, 7), matches[0].Range);
        Assert.AreEqual(new TextRange(2, 2, 2, 7), matches[1].Range);
        CollectionAssert.AreEqual(new[] { "alpha" }, matches[0].Matches!);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("\r")]
    [DataRow("\uFEFFone\r\ntwo\nthree\rfour\r\n😀\n")]
    public async Task SaveAsync_IsRepeatableAndPreservesRawLineEndings(string text)
    {
        ITextBuffer source = CreateBuffer(text);
        ITextSnapshot snapshot = source.CreateSnapshot();
        source.ApplyEdits([new(source.GetRangeAt(0, source.Length), "changed")]);
        for (int i = 0; i < 2; i++)
        {
            using var stream = new MemoryStream();
            await snapshot.SaveAsync(stream);
            Assert.IsTrue(stream.CanWrite);
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(text), stream.ToArray());
            BufferAssertions.Matches(text, snapshot);
        }
    }

    [TestMethod]
    public async Task ConcurrentSnapshotReaders_AreIndependentOfSourceWriter()
    {
        const string text = "one\ntwo\nthree\r";
        ITextBuffer source = CreateBuffer("");
        source.ApplyEdits([new(source.GetRangeAt(0, 0), text)]);
        ITextSnapshot snapshot = source.CreateSnapshot();
        ITextSnapshot peer = CreateBuffer(text).CreateSnapshot();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] readers = Enumerable.Range(0, 4).Select(worker => Task.Run(async () =>
        {
            await start.Task;
            for (int i = 0; i < 100; i++)
            {
                BufferAssertions.Matches(text, snapshot);
                Assert.IsTrue(worker % 2 == 0 ? snapshot.Equals(peer) : peer.Equals(snapshot));
            }
        })).ToArray();
        Task writer = Task.Run(async () =>
        {
            await start.Task;
            source.ApplyEdits([new(source.GetRangeAt(source.Length, 0), "\n")]);
            for (int i = 0; i < 100; i++)
            {
                source.ApplyEdits([new(source.GetRangeAt(0, 0), "x\n")]);
                if (i % 10 == 0) source.NormalizeEOL(i % 20 == 0 ? "\r\n" : "\n");
            }
        });
        start.SetResult();
        await Task.WhenAll(readers.Append(writer)).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static void AssertEveryRange(string text, ITextSnapshot snapshot)
    {
        for (int offset = 0; offset <= text.Length; offset++)
        for (int length = 0; length <= text.Length - offset; length++)
        {
            TextRange range = snapshot.GetRangeAt(offset, length);
            Assert.AreEqual(text.Substring(offset, length), snapshot.GetTextInRange(range));
            Assert.AreEqual(length, snapshot.GetTextLengthInRange(range));
        }
    }
}

[TestClass]
public sealed class LineArraySnapshotContractTests : TextSnapshotContractTests
{
    protected override ITextBuffer CreateBuffer(string text) => TextBufferFactory.LineArray(text);

}

[TestClass]
public sealed class PieceTreeSnapshotContractTests : TextSnapshotContractTests
{
    protected override ITextBuffer CreateBuffer(string text) => TextBufferFactory.PieceTree(text);

}
