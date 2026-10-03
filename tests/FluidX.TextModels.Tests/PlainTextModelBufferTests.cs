using System.Reflection;
using System.Runtime.ExceptionServices;
using FluidX.TextBuffers;
using FluidX.TextBuffers.PersistentPieceTree;
using FluidX.TextModels;

namespace FluidX.TextModels.Tests;

[TestClass]
public class PlainTextModelBufferTests
{
    private static (PlainTextModel Model, RecordingBuffer Proxy) Create(string text, bool? restore = null)
    {
        var model = new PlainTextModel(text);
        ITextBuffer buffer = restore.HasValue
            ? DispatchProxy.Create<ISnapshotRestorableTextBuffer, RecordingBuffer>()
            : DispatchProxy.Create<ITextBuffer, RecordingBuffer>();
        var proxy = (RecordingBuffer)buffer;
        proxy.Inner = new PersistentPieceTreeTextBuffer(text);
        proxy.Restore = restore == true;
        // Exercise alternate capabilities without introducing a production injection API.
        typeof(PlainTextModel).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, buffer);
        return (model, proxy);
    }

    [TestMethod]
    public void NormalizationUsesBufferApiAndWholeDocumentReplay()
    {
        var (model, proxy) = Create("a\rb\nc");
        model.NormalizeEOL(EndOfLine.CRLF);
        Assert.AreEqual(1, proxy.NormalizeCalls);
        Assert.HasCount(0, proxy.Batches);
        model.NormalizeEOL(EndOfLine.CRLF);
        Assert.AreEqual(1, proxy.NormalizeCalls);
        model.Undo();
        Assert.AreEqual("a\rb\nc", model.GetAllText());
        Assert.HasCount(1, proxy.Batches);
        Assert.AreEqual("a\rb\nc", proxy.Batches[0][0].Text);
        model.Redo();
        Assert.AreEqual("a\r\nb\r\nc", model.GetAllText());
        Assert.HasCount(2, proxy.Batches);
    }

    [TestMethod]
    public void SnapshotRestorationSkipsReplay()
    {
        var (model, proxy) = Create("abc", true);
        model.ApplyEdits([new(new TextRange(0, 1, 0, 2), "XY")]);
        proxy.Batches.Clear();
        model.Undo();
        model.Redo();
        Assert.AreEqual("aXYc", model.GetAllText());
        Assert.AreEqual(2, proxy.RestoreCalls);
        Assert.HasCount(0, proxy.Batches);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NavigationReplaysEachTransitionWithoutReplacingUnchangedText(bool rejectsRestore)
    {
        var (model, proxy) = Create("prefix abc suffix", rejectsRestore ? false : null);
        model.ApplyEdits([new(new TextRange(0, 7, 0, 8), "XY")]);
        model.ApplyEdits([new(new TextRange(0, 9, 0, 10), "!")]);
        var final = model.CurrentRecord;
        long version = model.VersionId;
        var events = new List<TextModelContentChangedEventArgs>();
        model.ContentChanged += (_, e) => events.Add(e);
        proxy.Batches.Clear();
        model.JumpToRecord(0);
        Assert.AreEqual("prefix abc suffix", model.GetAllText());
        Assert.HasCount(2, proxy.Batches);
        Assert.IsTrue(proxy.Batches.All(batch => batch.Length == 1 && batch[0].Text.Length <= 2));
        Assert.HasCount(1, events);
        Assert.HasCount(2, events[0].Transitions);
        Assert.AreEqual(version + 1, model.VersionId);
        model.JumpToRecord(final.RecordId);
        Assert.AreEqual("prefix XY!c suffix", model.GetAllText());
        Assert.AreSame(final, model.CurrentRecord);
        model.Undo();
        Assert.AreEqual("prefix XYbc suffix", model.GetAllText());
        model.Redo();
        Assert.AreEqual("prefix XY!c suffix", model.GetAllText());
    }

    [TestMethod]
    public void ReplayExpandsAndMergesCrlfSeams()
    {
        var (model, proxy) = Create("prefix ab suffix");
        model.ApplyEdits([new(new TextRange(0, 8, 0, 8), "\r"), new(new TextRange(0, 8, 0, 8), "\nX\n")]);
        proxy.Batches.Clear();
        model.Undo();
        Assert.AreEqual("prefix ab suffix", model.GetAllText());
        Assert.HasCount(1, proxy.Batches);
        Assert.AreEqual("", proxy.Batches[0][0].Text);
        model.Redo();
        Assert.AreEqual("prefix a\r\nX\nb suffix", model.GetAllText());
        model.NormalizeEOL(EndOfLine.CRLF);
        Assert.AreEqual(DocumentEndOfLine.CRLF, model.EOL);
        model.Undo();
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
        model.Undo();
        Assert.AreEqual("prefix ab suffix", model.GetAllText());
    }

    [TestMethod]
    public void ReplayBorrowsUnchangedHalfOfCrlf()
    {
        foreach (string insertion in new[] { "\r", "\n" })
        {
            string original = insertion == "\r" ? "a\nb" : "a\rb";
            var (model, _) = Create(original);
            int offset = insertion == "\r" ? 1 : 2;
            model.ApplyEdits([new(model.TextBuffer.GetRangeAt(offset, 0), insertion)]);
            Assert.AreEqual("a\r\nb", model.GetAllText());
            model.Undo();
            Assert.AreEqual(original, model.GetAllText());
            model.Redo();
            Assert.AreEqual("a\r\nb", model.GetAllText());
        }
    }

    [TestMethod]
    public void ReplayRoundTripsRandomBatchesAcrossRawLineEndings()
    {
        var random = new Random(741);
        string[] insertions = ["", "X", "\r", "\n", "\r\n", "\nX\r", "XY"];
        for (int trial = 0; trial < 100; trial++)
        {
            string original = string.Concat(Enumerable.Range(0, 20).Select(_ => insertions[random.Next(insertions.Length)]));
            var (model, _) = Create(original);
            var snapshot = model.TextBuffer;
            var boundaries = Enumerable.Range(0, snapshot.Length + 1)
                .Where(offset => offset == 0 || offset == snapshot.Length
                    || snapshot.GetChar(offset - 1) != '\r' || snapshot.GetChar(offset) != '\n').ToArray();
            var edits = new List<TextReplacement>();
            int index = 0;
            while (index < boundaries.Length)
            {
                int endIndex = Math.Min(index + random.Next(3), boundaries.Length - 1);
                edits.Add(new(snapshot.GetRangeAt(boundaries[index], boundaries[endIndex] - boundaries[index]),
                    insertions[random.Next(insertions.Length)]));
                index = endIndex + 1;
            }
            model.ApplyEdits(edits.ToArray());
            string expected = model.GetAllText();
            model.Undo();
            Assert.AreEqual(original, model.GetAllText(), $"Undo trial {trial}");
            model.Redo();
            Assert.AreEqual(expected, model.GetAllText(), $"Redo trial {trial}");
        }
    }

    [TestMethod]
    public void ReplacementAlwaysCommitsWithoutCheckingEqualityAndRemainsUndoable()
    {
        var (model, proxy) = Create("a\r\nb", false);
        var before = model.CurrentRecord;
        var events = new List<TextModelContentChangedEventArgs>();
        model.ContentChanged += (_, e) => events.Add(e);
        // The proxy rejects Equals calls, including when used as replacement input.
        model.ReplaceContent((ITextBuffer)proxy);
        Assert.AreEqual(1L, model.VersionId);
        Assert.AreNotEqual(before.RecordId, model.RecordId);
        Assert.HasCount(1, events);
        Assert.AreEqual(TextVersionKind.Replacement, events[0].Version.Kind);
        model.ReplaceContent(new PersistentPieceTreeTextBuffer(""));
        Assert.AreEqual("", model.GetAllText());
        model.Undo();
        Assert.AreEqual("a\r\nb", model.GetAllText());
        model.Undo();
        Assert.AreSame(before, model.CurrentRecord);
        Assert.AreEqual("a\r\nb", before.Snapshot.GetTextInRange(before.Snapshot.GetRangeAt(0, before.Snapshot.Length)));
    }

    [TestMethod]
    public void UnretainedRecordMustBeRecoveredThroughReplacement()
    {
        var (model, proxy) = Create("original");
        model.ReplaceContent(new PersistentPieceTreeTextBuffer("saved\r\ntext"));
        var saved = model.CurrentRecord;
        model.Undo();
        model.ReplaceContent(new PersistentPieceTreeTextBuffer("new branch"));
        var before = model.CurrentRecord;
        long version = model.VersionId;
        var events = new List<TextModelContentChangedEventArgs>();
        model.ContentChanged += (_, change) => events.Add(change);
        proxy.Batches.Clear();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.JumpToRecord(saved.RecordId));
        Assert.AreSame(before, model.CurrentRecord);
        Assert.AreEqual(version, model.VersionId);
        Assert.AreEqual("new branch", model.GetAllText());
        Assert.HasCount(0, proxy.Batches);
        Assert.HasCount(0, events);

        model.ReplaceContent(saved.Snapshot);
        Assert.AreEqual("saved\r\ntext", model.GetAllText());
        Assert.AreEqual(DocumentEndOfLine.CRLF, model.EOL);
        Assert.AreNotEqual(saved.RecordId, model.RecordId);
        Assert.AreEqual(version + 1, model.VersionId);
        Assert.HasCount(1, events);
        Assert.AreEqual(TextVersionKind.Replacement, events[0].Version.Kind);
        model.Undo();
        Assert.AreSame(before, model.CurrentRecord);
        Assert.AreEqual("new branch", model.GetAllText());
    }

    [TestMethod]
    public void FailedReplayOrMutationPropagatesWithoutRestoringContent()
    {
        foreach (string action in new[] { "jump", "edit", "eol", "replace" })
        {
            var (model, proxy) = Create("a\nb");
            model.ApplyEdits([new(new TextRange(0, 0, 0, 1), "A")]);
            model.ApplyEdits([new(new TextRange(1, 0, 1, 1), "B")]);
            var before = model.CurrentRecord;
            long version = model.VersionId;
            int events = 0;
            model.ContentChanged += (_, _) => events++;
            proxy.Batches.Clear();
            proxy.FailAfterBatches = action == "jump" ? 2 : 1;
            Assert.ThrowsExactly<ApplicationException>(() =>
            {
                switch (action)
                {
                    case "jump": model.JumpToRecord(0); break;
                    case "edit": model.ApplyEdits([new(new TextRange(0, 0, 0, 1), "x")]); break;
                    case "eol": model.NormalizeEOL(EndOfLine.CRLF); break;
                    case "replace": model.ReplaceContent(new PersistentPieceTreeTextBuffer("replacement")); break;
                }
            });
            Assert.AreEqual(action switch
            {
                "jump" => "a\nb",
                "edit" => "x\nB",
                "eol" => "A\r\nB",
                _ => "replacement"
            }, model.GetAllText());
            Assert.HasCount(action == "jump" ? 2 : action == "eol" ? 0 : 1, proxy.Batches);
            Assert.AreSame(before, model.CurrentRecord);
            Assert.AreEqual(version, model.VersionId);
            Assert.AreEqual(0, events);
            // A subsequent explicit replacement verifies that the mutation guard was reset.
            model.ReplaceContent(new PersistentPieceTreeTextBuffer("reset"));
            Assert.AreEqual("reset", model.GetAllText());
        }
    }

    public class RecordingBuffer : DispatchProxy
    {
        public PersistentPieceTreeTextBuffer Inner = null!;
        public bool Restore;
        public int RestoreCalls;
        public int NormalizeCalls;
        public int FailAfterBatches;
        public List<TextReplacement[]> Batches = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(ISnapshotRestorableTextBuffer.TryRestoreSnapshot))
            {
                RestoreCalls++;
                return Restore && Inner.TryRestoreSnapshot((ITextSnapshot)args![0]!);
            }
            if (method.Name == nameof(ITextBuffer.Equals))
                throw new AssertFailedException("Replacement must not compare content.");
            if (method.Name == nameof(ITextBuffer.NormalizeEOL))
            {
                NormalizeCalls++;
                Inner.NormalizeEOL((string)args![0]!);
                if (FailAfterBatches > 0 && --FailAfterBatches == 0)
                    throw new ApplicationException("Buffer failed after mutation.");
                return null;
            }
            if (method.Name == nameof(ITextBuffer.ApplyEdits))
            {
                var batch = (TextReplacement[])args![0]!;
                foreach (var edit in batch)
                    foreach (var position in new[] { edit.Range.StartPosition, edit.Range.EndPosition })
                        Assert.IsLessThanOrEqualTo(Inner.GetLineLength(position.LineIndex), position.ColumnIndex, "Replay endpoint is inside CRLF.");
                Batches.Add(batch);
                Inner.ApplyEdits(batch);
                if (FailAfterBatches > 0 && --FailAfterBatches == 0)
                    throw new ApplicationException("Buffer failed after mutation.");
                return null;
            }
            try { return method.Invoke(Inner, args); }
            catch (TargetInvocationException error)
            {
                ExceptionDispatchInfo.Capture(error.InnerException!).Throw();
                throw;
            }
        }
    }
}
