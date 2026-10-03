using FluidX.TextBuffers;
using FluidX.TextModels;

namespace FluidX.TextModels.Tests;

[TestClass]
public class PlainTextModelEditTests
{
    private sealed class CommandMetadata(string command) : TextModelOperationMetadata
    {
        public string Command { get; } = command;
    }

    private static TextReplacement Replace(int start, int end, string text) =>
        new(new TextRange(0, start, 0, end), text);

    [TestMethod]
    public void UnsortedBatchReturnsOldAndNewSpansInInputOrder()
    {
        var model = new TextModel("abcdef");
        var result = model.ApplyEdits([Replace(4, 6, "Z"), Replace(0, 1, "XY"), Replace(2, 3, "")]);
        Assert.AreEqual("XYbdZ", model.GetAllText());
        CollectionAssert.AreEqual(new[] { new TextChangeSpan(4, 2, 4, 1), new TextChangeSpan(0, 1, 0, 2), new TextChangeSpan(2, 1, 3, 0) }, result.ChangeSpans.ToArray());
        model.Undo();
        Assert.AreEqual("abcdef", model.GetAllText());
        model.Redo();
        Assert.AreEqual("XYbdZ", model.GetAllText());
    }

    [TestMethod]
    public void BoundaryInsertionsPreserveInputOrderAroundTouchingReplacements()
    {
        var model = new TextModel("abcd");
        TextReplacement[] edits = [Replace(1, 3, "X"), Replace(3, 3, "E"), Replace(1, 1, "A"), Replace(1, 1, "B"), Replace(3, 4, "Y")];
        var result = model.ApplyEdits(edits);
        Assert.AreEqual("aABXEY", model.GetAllText());
        CollectionAssert.AreEqual(new[] { new TextChangeSpan(1, 2, 3, 1), new TextChangeSpan(3, 0, 4, 1),
            new TextChangeSpan(1, 0, 1, 1), new TextChangeSpan(1, 0, 2, 1), new TextChangeSpan(3, 1, 5, 1) }, result.ChangeSpans.ToArray());
        for (int i = 0; i < edits.Length; i++)
            Assert.AreEqual(edits[i].Text, model.GetTextInRange(
                model.TextBuffer.GetRangeAt(result.ChangeSpans[i].NewPosition, result.ChangeSpans[i].NewLength)));
        model.Undo();
        Assert.AreEqual("abcd", model.GetAllText());
    }

    [TestMethod]
    public void EmptyBatchPreservesRecordVersionAndRedo()
    {
        var model = new TextModel("ab");
        model.ApplyEdits([Replace(2, 2, "c")]);
        model.Undo();
        var record = model.CurrentRecord;
        long version = model.VersionId;
        int events = 0;
        model.ContentChanged += (_, _) => events++;
        var empty = model.ApplyEdits([]);
        Assert.IsTrue(empty.Succeeded);
        Assert.AreSame(record, empty.Source);
        Assert.AreSame(record, empty.Target);
        Assert.IsEmpty(empty.ChangeSpans);
        Assert.AreSame(record, model.CurrentRecord);
        Assert.AreEqual(version, model.VersionId);
        Assert.AreEqual(0, events);
        Assert.IsTrue(model.CanRedo);
        model.Redo();
        Assert.AreEqual("abc", model.GetAllText());
    }

    [TestMethod]
    public void NonemptyUnchangedBatchesCommitAllSpansAndDiscardRedo()
    {
        TextReplacement[][] batches =
        [
            [Replace(0, 1, "a")],
            [Replace(0, 1, ""), Replace(1, 1, "a")],
            [Replace(1, 1, "")]
        ];
        TextChangeSpan[][] expected =
        [
            [new(0, 1, 0, 1)],
            [new(0, 1, 0, 0), new(1, 0, 0, 1)],
            [new(1, 0, 1, 0)]
        ];
        for (int i = 0; i < batches.Length; i++)
        {
            var model = new TextModel("ab");
            model.ApplyEdits([Replace(2, 2, "c")]);
            model.Undo();
            var before = model.CurrentRecord;
            long version = model.VersionId;
            var metadata = new CommandMetadata("unchanged edit");
            var notifications = new List<TextModelContentChangedEventArgs>();
            model.ContentChanged += (_, change) => notifications.Add(change);
            var result = model.ApplyEdits(batches[i], metadata);
            Assert.IsTrue(result.Succeeded);
            Assert.AreSame(before, result.Source);
            Assert.AreSame(model.CurrentRecord, result.Target);
            Assert.AreNotEqual(before.RecordId, model.RecordId);
            Assert.AreEqual(version + 1, model.VersionId);
            Assert.AreSame(metadata, model.CurrentVersion.Metadata);
            Assert.AreEqual("ab", model.GetAllText());
            Assert.IsFalse(model.CanRedo);
            Assert.HasCount(1, notifications);
            Assert.AreSame(model.CurrentVersion, notifications[0].Version);
            CollectionAssert.AreEqual(expected[i], result.ChangeSpans.ToArray());
            CollectionAssert.AreEqual(expected[i], result.Target.ChangeSpans.ToArray());
            Assert.AreSame(result.Source, notifications[0].Before);
            Assert.AreSame(result.Target, notifications[0].After);
            model.Undo();
            Assert.AreSame(before, model.CurrentRecord);
            Assert.AreEqual("ab", model.GetAllText());
            model.Redo();
            Assert.AreSame(result.Target, model.CurrentRecord);
            Assert.AreEqual("ab", model.GetAllText());
        }
    }

    [TestMethod]
    public void EffectiveBatchPublishesOneTransitionAndKeepsUnchangedResultEntries()
    {
        var model = new TextModel("abc");
        var before = model.CurrentRecord;
        var metadata = new CommandMetadata("test command");
        var notifications = new List<TextModelContentChangedEventArgs>();
        model.ContentChanged += (_, change) => notifications.Add(change);
        var result = model.ApplyEdits([Replace(2, 3, "c"), Replace(0, 1, "XY")], metadata);
        Assert.HasCount(1, notifications);
        var change = notifications[0];
        Assert.AreSame(metadata, change.Version.Metadata);
        Assert.AreSame(before, change.Before);
        Assert.AreSame(model.CurrentRecord, change.After);
        Assert.IsTrue(result.Succeeded);
        Assert.AreSame(result.Source, change.Before);
        Assert.AreSame(result.Target, change.After);
        Assert.HasCount(2, change.Transitions[0].ChangeSpans);
        Assert.AreEqual("abc", before.Snapshot.GetTextInRange(new TextRange(0, 0, 0, 3)));
        CollectionAssert.AreEqual(new[] { new TextChangeSpan(2, 1, 3, 1), new TextChangeSpan(0, 1, 0, 2) }, result.ChangeSpans.ToArray());
        Assert.AreEqual(1L, model.VersionId);
    }

    [TestMethod]
    public void InvalidBatchesLeaveAllStateUntouched()
    {
        TextReplacement[][] batches =
        [
            [Replace(0, 2, "X"), Replace(1, 3, "Y")],
            [Replace(0, 3, "X"), Replace(1, 1, "Y")],
            [Replace(0, 1, "X"), Replace(5, 5, "Y")],
            [Replace(0, 1, "X"), new(new TextRange(2, 0, 2, 0), "Y")]
        ];
        foreach (var batch in batches)
        {
            var model = new TextModel("abcd");
            model.ApplyEdits([Replace(4, 4, "!")]);
            model.Undo();
            var before = model.CurrentRecord;
            var version = model.CurrentVersion;
            int events = 0;
            model.ContentChanged += (_, _) => events++;
            var result = model.ApplyEdits(batch);
            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(string.IsNullOrEmpty(result.Error));
            Assert.AreSame(before, result.Source);
            Assert.AreSame(before, result.Target);
            Assert.IsEmpty(result.ChangeSpans);
            Assert.AreEqual("abcd", model.GetAllText());
            Assert.AreSame(before, model.CurrentRecord);
            Assert.AreSame(version, model.CurrentVersion);
            Assert.AreEqual(0, events);
            Assert.IsTrue(model.CanRedo);
            // Validation failure must release the mutation guard.
            model.Redo();
            Assert.AreEqual("abcd!", model.GetAllText());
        }
    }

    [TestMethod]
    public void ResultsDescribeExactTextAcrossMultilineAndCrlfSeams()
    {
        var model = new TextModel("ab");
        var result = model.ApplyEdits([Replace(1, 1, "\r"), Replace(1, 1, "\nX\n")]);
        Assert.AreEqual("a\r\nX\nb", model.GetAllText());
        CollectionAssert.AreEqual(new[] { new TextChangeSpan(1, 0, 1, 1), new TextChangeSpan(1, 0, 2, 3) }, result.ChangeSpans.ToArray());
        var firstRange = model.TextBuffer.GetRangeAt(result.ChangeSpans[0].NewPosition, result.ChangeSpans[0].NewLength);
        var secondRange = model.TextBuffer.GetRangeAt(result.ChangeSpans[1].NewPosition, result.ChangeSpans[1].NewLength);
        Assert.AreEqual("\r", model.GetTextInRange(firstRange));
        Assert.AreEqual("\nX\n", model.GetTextInRange(secondRange));
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
        Assert.IsFalse(model.ApplyEdits([new(firstRange, "!")]).Succeeded);
        model.NormalizeEOL(EndOfLine.CRLF);
        Assert.AreEqual(DocumentEndOfLine.CRLF, model.EOL);
        model.Undo();
        Assert.AreEqual("a\r\nX\nb", model.GetAllText());
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
        model.Undo();
        Assert.AreEqual("ab", model.GetAllText());
        Assert.AreEqual(DocumentEndOfLine.Unknown, model.EOL);
    }

    [TestMethod]
    public void NotificationRejectsReentrancyAndSubscriberFailureDoesNotRollback()
    {
        var model = new TextModel("a");
        EventHandler<TextModelContentChangedEventArgs> subscriber = (_, _) =>
        {
            Assert.AreEqual("ab", model.GetAllText());
            Assert.ThrowsExactly<InvalidOperationException>(() => model.ApplyEdits([Replace(0, 0, "x")]));
            Assert.ThrowsExactly<InvalidOperationException>(() => model.Undo());
            throw new ApplicationException("subscriber failed");
        };
        model.ContentChanged += subscriber;
        Assert.ThrowsExactly<ApplicationException>(() => model.ApplyEdits([Replace(1, 1, "b")]));
        Assert.AreEqual("ab", model.GetAllText());
        Assert.AreEqual(1L, model.VersionId);
        model.ContentChanged -= subscriber;
        model.Undo();
        Assert.AreEqual("a", model.GetAllText());
    }
}
