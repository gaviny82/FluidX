using FluidX.TextBuffers.PersistentPieceTree;
using FluidX.TextBuffers.Tests.Infrastructure;

namespace FluidX.TextBuffers.Tests.Contracts;

[TestClass]
public sealed class PersistentPieceTreeStructureTests
{
    [TestMethod]
    [DataRow(17)]
    [DataRow(7321)]
    [DataRow(982451653)]
    public void RandomEdits_PreserveInvariantsAndHistoricalVersions(int seed)
    {
        var random = new Random(seed);
        string expected = "start\r\n😀\0end\r";
        var buffer = new PersistentPieceTreeTextBuffer(expected);
        var history = new List<(string Text, ITextSnapshot Snapshot)>();
        string[] insertions = ["", "x", "\r", "\n", "\r\n", "😀", "a\0b", "\uD800", "one\ntwo\rthree"];
        for (int step = 0; step < 2000; step++)
        {
            if (step % 41 == 0) history.Add((expected, buffer.CreateSnapshot()));
            int[] boundaries = Enumerable.Range(0, expected.Length + 1)
                .Where(i => i == 0 || i == expected.Length || expected[i - 1] != '\r' || expected[i] != '\n').ToArray();
            int first = random.Next(boundaries.Length);
            int last = Math.Min(boundaries.Length - 1, first + random.Next(5));
            int offset = boundaries[first], length = boundaries[last] - offset;
            string inserted = insertions[random.Next(insertions.Length)];
            buffer.ApplyEdits([new(buffer.GetRangeAt(offset, length), inserted)]);
            expected = expected.Remove(offset, length).Insert(offset, inserted);
            CheckRoot(buffer);
            BufferAssertions.Matches(expected, buffer);
        }
        foreach (var (text, snapshot) in history) BufferAssertions.Matches(text, snapshot);
    }

    [TestMethod]
    public void Tree_InsertAndDeleteInRandomOrder_MaintainsBlackHeight()
    {
        var random = new Random(6157);
        PieceNode? root = null;
        var storage = new TextStorage("x");
        for (int i = 0; i < 1500; i++)
        {
            root = PieceNode.Insert(root, new(storage, 0, 1), random.Next(i + 1));
            Assert.IsFalse(root.Red);
            Validate(root);
        }
        for (int remaining = 1500; remaining > 0; remaining--)
        {
            root = PieceNode.Remove(root!, random.Next(remaining));
            Assert.AreEqual(remaining - 1, root?.Length ?? 0);
            if (root is not null) Assert.IsFalse(root.Red);
            Validate(root);
        }
    }

    [TestMethod]
    public void SnapshotCapture_AllocatesNothingAndRetainsRoot()
    {
        var buffer = new PersistentPieceTreeTextBuffer("original");
        for (int i = 0; i < 1000; i++) buffer.ApplyEdits([new(buffer.GetRangeAt(0, 0), "x")]);
        ITextSnapshot captured = buffer.CreateSnapshot();
        ITextSnapshot? last = null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) last = buffer.CreateSnapshot();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0L, allocated);
        Assert.AreSame(captured, last);
        Assert.AreSame(captured, captured.CreateSnapshot());
    }

    [TestMethod]
    public void LocalEdit_SharesMostNodesAndOriginalTextStorage()
    {
        var buffer = new PersistentPieceTreeTextBuffer(new string('a', 10000));
        for (int i = 0; i < 1000; i++) buffer.ApplyEdits([new(buffer.GetRangeAt(0, 0), "x")]);
        var before = (PersistentPieceTreeSnapshot)buffer.CreateSnapshot();
        var oldNodes = Nodes(before.Root).ToHashSet();
        buffer.ApplyEdits([new(buffer.GetRangeAt(buffer.Length - 1, 0), "z")]);
        var after = (PersistentPieceTreeSnapshot)buffer.CreateSnapshot();
        var newNodes = Nodes(after.Root).ToArray();
        Assert.IsGreaterThan(oldNodes.Count - 100, newNodes.Count(oldNodes.Contains));
        var originalStorage = PieceNode.Find(before.Root!, before.Length - 1).Piece.Storage;
        Assert.AreSame(originalStorage, PieceNode.Find(after.Root!, after.Length - 1).Piece.Storage);
        BufferAssertions.Matches(new string('x', 1000) + new string('a', 10000), before);
    }

    [TestMethod]
    public void SequentialTyping_CoalescesPiecesAcrossChunkBoundaries()
    {
        var buffer = new PersistentPieceTreeTextBuffer();
        var history = new List<ITextSnapshot>();
        for (int i = 0; i < 5000; i++)
        {
            buffer.ApplyEdits([new(buffer.GetRangeAt(buffer.Length, 0), "x")]);
            if (i % 1000 == 0) history.Add(buffer.CreateSnapshot());
        }
        var root = ((PersistentPieceTreeSnapshot)buffer.CreateSnapshot()).Root;
        Assert.IsLessThanOrEqualTo(5, Nodes(root).Count());
        for (int i = 0; i < history.Count; i++) BufferAssertions.Matches(new string('x', i * 1000 + 1), history[i]);
        CheckRoot(buffer);
    }

    [TestMethod]
    public async Task AppendingToSharedStorage_DoesNotChangeSnapshotPrefixQueries()
    {
        var buffer = new PersistentPieceTreeTextBuffer();
        buffer.ApplyEdits([new(buffer.GetRangeAt(0, 0), "a\r")]);
        var snapshot = buffer.CreateSnapshot();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            for (int i = 0; i < 200; i++) BufferAssertions.Matches("a\r", snapshot);
        })).ToArray();
        Task writer = Task.Run(async () =>
        {
            await start.Task;
            for (int i = 0; i < 500; i++) buffer.ApplyEdits([new(buffer.GetRangeAt(buffer.Length, 0), "\nx\r")]);
        });
        start.SetResult();
        await Task.WhenAll(readers.Append(writer));
        BufferAssertions.Matches("a\r", snapshot);
        CheckRoot(buffer);
    }

    [TestMethod]
    public void BatchTouchingEdits_UseOriginalCoordinatesEvenWhenSeamsChange()
    {
        var buffer = new PersistentPieceTreeTextBuffer("a\rX\nb");
        buffer.ApplyEdits([new(buffer.GetRangeAt(1, 1), "!"), new(buffer.GetRangeAt(2, 1), "")]);
        BufferAssertions.Matches("a!\nb", buffer);
    }

    private static void CheckRoot(PersistentPieceTreeTextBuffer buffer)
    {
        var root = ((PersistentPieceTreeSnapshot)buffer.CreateSnapshot()).Root;
        if (root is not null) Assert.IsFalse(root.Red);
        Validate(root);
    }

    private static (int BlackHeight, TextSummary Summary) Validate(PieceNode? node)
    {
        if (node is null) return (1, default);
        var left = Validate(node.Left);
        var right = Validate(node.Right);
        Assert.AreEqual(left.BlackHeight, right.BlackHeight);
        if (node.Red)
        {
            Assert.IsFalse(node.Left?.Red ?? false);
            Assert.IsFalse(node.Right?.Red ?? false);
        }
        Assert.IsGreaterThan(0, node.Piece.Length);
        TextSummary piece = default;
        for (int i = 0; i < node.Piece.Length; i++)
        {
            char c = node.Piece.Storage.CharAt(node.Piece.Start + i);
            piece += new TextSummary(1, c is '\r' or '\n' ? 1 : 0, c, c);
        }
        Assert.AreEqual(piece, node.Piece.Summary);
        var summary = left.Summary + piece + right.Summary;
        Assert.AreEqual(summary, node.Summary);
        return (left.BlackHeight + (node.Red ? 0 : 1), summary);
    }

    private static IEnumerable<PieceNode> Nodes(PieceNode? node)
    {
        if (node is null) yield break;
        yield return node;
        foreach (var child in Nodes(node.Left)) yield return child;
        foreach (var child in Nodes(node.Right)) yield return child;
    }
}
