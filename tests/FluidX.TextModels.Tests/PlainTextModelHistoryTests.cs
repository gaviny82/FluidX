using FluidX.TextBuffers;
using FluidX.TextModels;

namespace FluidX.TextModels.Tests;

[TestClass]
public class PlainTextModelHistoryTests
{
    private sealed class CommandMetadata(string command) : TextModelOperationMetadata
    {
        public string Command { get; } = command;
    }

    [TestMethod]
    public void EveryOperationRetainsItsOwnMetadataOnThePublishedVersion()
    {
        var model = new PlainTextModel("a\nb");
        var initial = model.CurrentVersion;
        Assert.IsNull(initial.Metadata);
        var notifications = new List<TextModelContentChangedEventArgs>();
        model.ContentChanged += (_, change) => notifications.Add(change);
        var edit = new CommandMetadata("type");
        var undo = new CommandMetadata("keyboard undo");
        var redo = new CommandMetadata("menu redo");
        var jump = new CommandMetadata("history jump");
        var normalize = new CommandMetadata("normalize");
        var replace = new CommandMetadata("reload");

        model.ApplyEdits([new(new TextRange(0, 0, 0, 1), "A")], edit);
        long editedRecordId = model.RecordId;
        model.Undo(undo);
        model.Redo(redo);
        Assert.AreEqual(editedRecordId, model.RecordId);
        model.JumpToRecord(0, jump);
        model.NormalizeEOL(EndOfLine.CRLF, normalize);
        model.ReplaceContent(model.TextBuffer, replace);

        TextModelOperationMetadata[] expected = [edit, undo, redo, jump, normalize, replace];
        TextVersionKind[] kinds = [TextVersionKind.Edit, TextVersionKind.Undo, TextVersionKind.Redo,
            TextVersionKind.Jump, TextVersionKind.EolNormalization, TextVersionKind.Replacement];
        var versions = model.GetStoredVersions();
        Assert.HasCount(7, versions);
        Assert.HasCount(6, notifications);
        Assert.AreSame(initial, versions[0]);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreSame(expected[i], versions[i + 1].Metadata);
            Assert.AreEqual(kinds[i], versions[i + 1].Kind);
            Assert.AreSame(versions[i + 1], notifications[i].Version);
        }
        Assert.AreSame(versions[^1], model.CurrentVersion);
        // A discarded redo record does not erase the chronological operation metadata.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.GetRecord(editedRecordId));
    }

    [TestMethod]
    public void NoOpsDoNotStoreMetadataOrPublishVersions()
    {
        var model = new PlainTextModel("a\nb");
        var metadata = new CommandMetadata("no-op");
        var initial = model.CurrentVersion;
        int events = 0;
        model.ContentChanged += (_, _) => events++;
        model.Undo(metadata);
        model.Redo(metadata);
        model.JumpToRecord(0, metadata);
        model.NormalizeEOL(EndOfLine.LF, metadata);
        model.ApplyEdits(default, metadata);
        Assert.AreSame(initial, model.CurrentVersion);
        Assert.AreEqual(1, model.StoredVersionCount);
        Assert.AreEqual(0, events);

        model.ApplyEdits([new(new TextRange(0, 0, 0, 1), "A")]);
        Assert.IsNull(model.CurrentVersion.Metadata);
        model.Undo(metadata);
        var undo = model.CurrentVersion;
        model.ApplyEdits([], new CommandMetadata("empty batch"));
        Assert.AreSame(undo, model.CurrentVersion);
        Assert.AreSame(metadata, undo.Metadata);
        Assert.IsTrue(model.CanRedo);
    }

    [TestMethod]
    public void ReadOnlyHistoryQueriesDescribeNavigationAndRetainedSnapshots()
    {
        var model = new PlainTextModel("abc");
        var initial = model.CurrentRecord;
        model.ApplyEdits([new(new TextRange(0, 0, 0, 1), "XY")]);
        var first = model.CurrentRecord;
        model.ApplyEdits([new(new TextRange(0, 4, 0, 4), "!")]);
        var second = model.CurrentRecord;
        var retained = model.GetStoredVersions();
        model.Undo();

        Assert.AreEqual(4, model.StoredVersionCount);
        Assert.HasCount(3, retained);
        Assert.AreSame(model.CurrentVersion, model.GetStoredVersion(0));
        Assert.AreSame(retained[^1], model.GetStoredVersion(1));
        Assert.AreEqual(1, model.HistoryRecordCount);
        Assert.AreEqual(1, model.FutureRecordCount);
        Assert.AreSame(initial, model.GetHistoryRecord(0));
        Assert.AreSame(initial, model.GetHistoryRecords()[0]);
        Assert.AreSame(second, model.GetFutureRecord(0));
        Assert.AreSame(second, model.GetFutureRecords()[0]);
        Assert.AreSame(first, model.GetRecord(first.RecordId));

        var backward = model.GetTransitionsBetweenRecords(second.RecordId, initial.RecordId);
        Assert.HasCount(2, backward);
        Assert.AreSame(second, backward[0].Source);
        Assert.AreSame(first, backward[0].Target);
        Assert.AreSame(first, backward[1].Source);
        Assert.AreSame(initial, backward[1].Target);
        Assert.AreEqual(new TextChangeSpan(4, 1, 4, 0), backward[0].ChangeSpans[0]);
        Assert.IsTrue(model.GetTransitionsBetweenRecords(first.RecordId, first.RecordId).IsEmpty);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.GetStoredVersion(4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.GetHistoryRecord(1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.GetFutureRecord(1));
    }

    [TestMethod]
    public void SpanSlicePreservesInputOrderWithoutIncludingOtherArrayEntries()
    {
        var model = new PlainTextModel("abcd");
        TextReplacement[] edits =
        [
            null!,
            new(new TextRange(0, 3, 0, 4), "Z"),
            new(new TextRange(0, 0, 0, 1), "XY"),
            null!
        ];
        var first = edits[1];
        var second = edits[2];
        var result = model.ApplyEdits(edits.AsSpan(1, 2));
        Assert.AreEqual("XYbcZ", model.GetAllText());
        Assert.AreEqual(new TextChangeSpan(3, 1, 4, 1), result.ChangeSpans[0]);
        Assert.AreEqual(new TextChangeSpan(0, 1, 0, 2), result.ChangeSpans[1]);
        Assert.AreSame(first, edits[1]);
        Assert.AreSame(second, edits[2]);
    }
}
