using FluidX.TextBuffers;
using FluidX.TextModels;
using FluidX.Tokenization;

namespace FluidX.TextModels.Tests;

[TestClass]
public class PlainTextModelEolTests
{
    [TestMethod]
    public void MixedPersistsUntilNormalizationOrSingleLineAndHistoryRestoresState()
    {
        var model = new PlainTextModel("a\nb");
        var normalized = model.CurrentRecord;
        model.ApplyEdits([new(new TextRange(1, 1, 1, 1), "\r")]);
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
        model.ApplyEdits([new(new TextRange(1, 1, 2, 0), "")]);
        Assert.AreEqual("a\nb", model.GetAllText());
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
        var conservative = model.CurrentRecord;
        model.NormalizeEOL(EndOfLine.LF);
        Assert.AreEqual(DocumentEndOfLine.LF, model.EOL);
        model.Undo();
        Assert.AreSame(conservative, model.CurrentRecord);
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
        model.Redo();
        Assert.AreEqual(DocumentEndOfLine.LF, model.EOL);
        model.JumpToRecord(normalized.RecordId);
        Assert.AreEqual(DocumentEndOfLine.LF, model.EOL);
        model.JumpToRecord(conservative.RecordId);
        model.ApplyEdits([new(new TextRange(0, 1, 1, 0), "")]);
        Assert.AreEqual(DocumentEndOfLine.Unknown, model.EOL);
    }

    [TestMethod]
    [DataRow("a\nb", 1, "\r", DocumentEndOfLine.Mixed)]
    [DataRow("a\rb", 2, "\n", DocumentEndOfLine.Mixed)]
    [DataRow("a\r\nb", 1, "\r\n", DocumentEndOfLine.CRLF)]
    [DataRow("a\nb", 1, "\n", DocumentEndOfLine.LF)]
    [DataRow("a\rb", 1, "\r", DocumentEndOfLine.CR)]
    [DataRow("ab", 1, "\r\n", DocumentEndOfLine.CRLF)]
    [DataRow("ab", 1, "\n", DocumentEndOfLine.LF)]
    [DataRow("ab", 1, "\r", DocumentEndOfLine.CR)]
    [DataRow("ab", 1, "\r\n\n", DocumentEndOfLine.Mixed)]
    public void InsertedEndingsUseFinalBoundaryContext(string source, int offset, string text,
        DocumentEndOfLine expected)
    {
        var model = new PlainTextModel(source);
        Assert.IsTrue(model.ApplyEdits([new(model.TextBuffer.GetRangeAt(offset, 0), text)]).Succeeded);
        Assert.AreEqual(expected, model.EOL);
    }

    [TestMethod]
    public void AdjacentEditsClassifyTheirCombinedFinalEndings()
    {
        var model = new PlainTextModel("abc");
        model.ApplyEdits([
            new(new TextRange(0, 2, 0, 2), "\n"),
            new(new TextRange(0, 1, 0, 1), "\r"),
            new(new TextRange(0, 1, 0, 2), "")]);
        Assert.AreEqual("a\r\nc", model.GetAllText());
        Assert.AreEqual(DocumentEndOfLine.CRLF, model.EOL);
        model.Undo();
        Assert.AreEqual(DocumentEndOfLine.Unknown, model.EOL);
        model.Redo();
        Assert.AreEqual(DocumentEndOfLine.CRLF, model.EOL);
    }

    [TestMethod]
    public void ReplacingAllEndingsWithAnotherKindIsConservativelyMixed()
    {
        var model = new PlainTextModel("a\nb");
        model.ApplyEdits([new(new TextRange(0, 1, 1, 0), "\r\n")]);
        Assert.AreEqual("a\r\nb", model.GetAllText());
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
    }

    [TestMethod]
    [DataRow("a\rb\nc\r\n", EndOfLine.LF, "a\nb\nc\n", DocumentEndOfLine.LF)]
    [DataRow("a\rb\nc\r\n", EndOfLine.CRLF, "a\r\nb\r\nc\r\n", DocumentEndOfLine.CRLF)]
    [DataRow("a\nb", EndOfLine.CRLF, "a\r\nb", DocumentEndOfLine.CRLF)]
    [DataRow("a\r\nb", EndOfLine.LF, "a\nb", DocumentEndOfLine.LF)]
    [DataRow("a\rb", EndOfLine.LF, "a\nb", DocumentEndOfLine.LF)]
    public void NormalizationRecordsWholeDocumentAndNavigates(string source, EndOfLine eol,
        string expected, DocumentEndOfLine classification)
    {
        var model = new TextModel(source, GlobalLanguageId.PlainText);
        try
        {
            var before = model.CurrentRecord;
            var events = new List<TextModelContentChangedEventArgs>();
            model.ContentChanged += (_, e) => events.Add(e);
            model.NormalizeEOL(eol);
            var after = model.CurrentRecord;
            Assert.AreEqual(expected, model.GetAllText());
            Assert.AreEqual(classification, model.EOL);
            Assert.AreEqual(1L, model.VersionId);
            Assert.HasCount(1, events);
            Assert.AreEqual(TextVersionKind.EolNormalization, events[0].Version.Kind);
            Assert.HasCount(1, after.ChangeSpans);
            Assert.AreEqual(new TextChangeSpan(0, source.Length, 0, expected.Length), after.ChangeSpans[0]);
            var change = events[0].Transitions[0].GetTextChanges();
            Assert.HasCount(1, change);
            model.Undo();
            Assert.AreSame(before, model.CurrentRecord);
            Assert.AreEqual(source, model.GetAllText());
            model.Redo();
            Assert.AreSame(after, model.CurrentRecord);
            Assert.AreEqual(expected, model.GetAllText());
            model.JumpToRecord(before.RecordId);
            Assert.AreEqual(source, model.GetAllText());
            model.JumpToRecord(after.RecordId);
            Assert.AreEqual(expected, model.GetAllText());
            for (int line = 0; line < model.TextBuffer.LineCount; line++)
                Assert.IsNotNull(model.Tokenization.GetLineTokens(line));
        }
        finally { model.Tokenization.Dispose(); }
    }

    [TestMethod]
    [DataRow("", EndOfLine.LF)]
    [DataRow("abc", EndOfLine.CRLF)]
    [DataRow("a\nb", EndOfLine.LF)]
    [DataRow("a\r\nb", EndOfLine.CRLF)]
    public void NoOpPreservesRecordSnapshotVersionAndRedo(string source, EndOfLine eol)
    {
        var model = new PlainTextModel(source);
        model.ApplyEdits([new(new TextRange(0, 0, 0, 0), "x")]);
        model.Undo();
        var before = model.CurrentRecord;
        long version = model.VersionId;
        int events = 0;
        model.ContentChanged += (_, _) => events++;
        model.NormalizeEOL(eol);
        Assert.AreSame(before, model.CurrentRecord);
        Assert.AreSame(before.Snapshot, model.CurrentRecord.Snapshot);
        Assert.AreEqual(version, model.VersionId);
        Assert.AreEqual(0, events);
        Assert.IsTrue(model.CanRedo);
        model.Redo();
        Assert.AreEqual("x" + source, model.GetAllText());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("abc")]
    [DataRow("a\rb")]
    [DataRow("a\nb")]
    public void UnsupportedAndInvalidRequestsDoNotChangeState(string source)
    {
        var model = new PlainTextModel(source);
        var before = model.CurrentRecord;
        int events = 0;
        model.ContentChanged += (_, _) => events++;
        Assert.ThrowsExactly<InvalidOperationException>(() => model.NormalizeEOL(EndOfLine.CR));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.NormalizeEOL((EndOfLine)99));
        Assert.AreSame(before, model.CurrentRecord);
        Assert.AreEqual(source, model.GetAllText());
        Assert.AreEqual(0L, model.VersionId);
        Assert.AreEqual(0, events);
    }
}
