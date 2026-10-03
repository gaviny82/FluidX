using System;
using System.Collections.Generic;
using System.Text;
using FluidX.TextBuffers;
using FluidX.TextBuffers.PersistentPieceTree;
using FluidX.TextModels;

namespace FluidX.TextModels.Tests;

[TestClass]
public class PlainTextModelTests
{
    [TestMethod]
    public void PlainTextModelOwnsDocumentPolicyWhileBufferPreservesRawEOLs()
    {
        var model = new PlainTextModel("\uFEFFone\r\ntwo\nthree\rfour");

        Assert.IsTrue(model.HasBOM);
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
        Assert.AreEqual("one\r\ntwo\nthree\rfour", model.TextBuffer.GetTextInRange(model.GetFullModelRange()));
        Assert.AreEqual("one\r\ntwo\nthree\rfour", model.GetAllText());
        Assert.AreEqual("one\r\ntwo\r\nthree\r\nfour", model.GetAllText(EndOfLine.CRLF));
        Assert.AreEqual(19, model.GetTextLengthInRange(model.GetFullModelRange()));
        Assert.AreEqual(18, model.GetTextLengthInRange(model.GetFullModelRange(), EndOfLine.LF));
        Assert.AreEqual(21, model.GetTextLengthInRange(model.GetFullModelRange(), EndOfLine.CRLF));
        Assert.AreEqual("one\rtwo\rthree\rfour", model.GetTextInRange(model.GetFullModelRange(), EndOfLine.CR));
        Assert.AreEqual(18, model.GetTextLengthInRange(model.GetFullModelRange(), EndOfLine.CR));
        Assert.AreEqual(19, model.GetCharacterCountInRange(model.GetFullModelRange()));
        Assert.AreEqual(18, model.GetCharacterCountInRange(model.GetFullModelRange(), EndOfLine.CR));
        Assert.AreEqual("\uFEFFone\r\ntwo\nthree\rfour", model.GetAllText(preserveBOM: true));

        model.HasBOM = false;
        Assert.AreEqual("one\r\ntwo\nthree\rfour", model.GetAllText(preserveBOM: true));
        model.HasBOM = true;
        Assert.IsTrue(model.HasBOM);
        Assert.AreEqual("\uFEFFone\r\ntwo\nthree\rfour", model.GetAllText(preserveBOM: true));

        model.NormalizeEOL(EndOfLine.LF);

        Assert.AreEqual("one\ntwo\nthree\nfour", model.TextBuffer.GetTextInRange(model.GetFullModelRange()));
        Assert.AreEqual(DocumentEndOfLine.LF, model.EOL);
        Assert.AreEqual(21, model.GetTextLengthInRange(model.GetFullModelRange(), EndOfLine.CRLF));
        Assert.AreEqual(18, model.GetTextLengthInRange(model.GetFullModelRange(), EndOfLine.CR));
    }

    [TestMethod]
    public void PlainTextModelSupportsEditUndoAndRedo()
    {
        var model = new PlainTextModel("hello");
        model.ApplyEdits([new TextReplacement(new TextRange(0, 5, 0, 5), " world")]);

        Assert.AreEqual("hello world", model.GetAllText());
        model.Undo();
        Assert.AreEqual("hello", model.GetAllText());
        model.Redo();
        Assert.AreEqual("hello world", model.GetAllText());
    }

    [TestMethod]
    public void PlainTextModelRejectsEditEndpointsInsideCrlfBeforeApplyingBatch()
    {
        var model = new PlainTextModel("a\r\nb");
        TextReplacement[] operations =
        [
            new(new TextRange(0, 0, 0, 1), "A"),
            new(new TextRange(0, 2, 0, 2), "X")
        ];

        Assert.IsFalse(model.ApplyEdits(operations).Succeeded);
        Assert.AreEqual("a\r\nb", model.GetAllText());
    }

    [TestMethod]
    public void PlainTextModelRejectsEveryKindOfEndpointInsideCrlf()
    {
        TextRange[] invalidRanges =
        [
            new(0, 2, 0, 2), // insertion inside CRLF
            new(0, 2, 1, 0), // start inside CRLF
            new(0, 1, 0, 2)  // end inside CRLF
        ];

        foreach (TextRange range in invalidRanges)
        {
            var model = new PlainTextModel("a\r\nb");
            Assert.IsFalse(model.ApplyEdits([new TextReplacement(range, "X")]).Succeeded);
            Assert.AreEqual("a\r\nb", model.GetAllText());
        }
    }

    [TestMethod]
    public void PlainTextModelAllowsReplacingCompleteCrlf()
    {
        var model = new PlainTextModel("a\r\nb");

        model.ApplyEdits(
            [new TextReplacement(new TextRange(0, 1, 1, 0), "\n")]);

        Assert.AreEqual("a\nb", model.GetAllText());
    }

    [TestMethod]
    public void TextModelInheritsPlainTextModel()
    {
        PlainTextModel model = CreateModel("text");
        Assert.AreEqual("text", model.GetAllText());
    }

    [TestMethod]
    public void ReplaceContentAcceptsMutableBufferAndRetainedSnapshot()
    {
        var model = CreateModel("before");
        var source = new PersistentPieceTreeTextBuffer("after\r\ntext");

        model.ReplaceContent(source);
        Assert.AreEqual("after\r\ntext", model.GetAllText());
        Assert.AreEqual(DocumentEndOfLine.CRLF, model.EOL);

        source.ApplyEdits([new(source.GetRangeAt(0, source.Length), "changed")]);
        Assert.AreEqual("after\r\ntext", model.GetAllText());

        model.Undo();
        Assert.AreEqual("before", model.GetAllText());
        model.Redo();
        Assert.AreEqual("after\r\ntext", model.GetAllText());

        var snapshot = source.CreateSnapshot();
        model.ReplaceContent(snapshot);
        Assert.AreEqual("changed", model.GetAllText());
        model.Undo();
        Assert.AreEqual("after\r\ntext", model.GetAllText());
    }

    [TestMethod]
    public void LargeBatchPreservesIndividualEditSpans()
    {
        const int operationCount = 1000;
        var model = CreateModel(new string('a', operationCount));
        var operations = Enumerable.Range(0, operationCount)
            .Select(index => new TextReplacement(
                new TextRange(0, index, 0, index + 1),
                ""))
            .ToArray();

        var result = model.ApplyEdits(operations);

        Assert.HasCount(operationCount, result.ChangeSpans);
        Assert.AreEqual("", model.GetAllText());
    }

    [TestMethod]
    public void LargeBatchPreservesSeparateChangeRanges()
    {
        const int operationCount = 1000;
        string original = new('a', operationCount * 2);
        var model = CreateModel(original);
        var operations = Enumerable.Range(0, operationCount)
            .Select(index => new TextReplacement(
                new TextRange(0, index * 2, 0, index * 2 + 1),
                "b"))
            .ToArray();

        var result = model.ApplyEdits(operations);

        Assert.HasCount(operationCount, result.ChangeSpans);
        Assert.HasCount(operationCount, model.CurrentRecord.ChangeSpans);
        for (int i = 0; i < operationCount; i++)
        {
            Assert.AreEqual(i * 2, model.CurrentRecord.ChangeSpans[i].OldPosition);
            Assert.AreEqual(1, model.CurrentRecord.ChangeSpans[i].OldLength);
            Assert.AreEqual(1, model.CurrentRecord.ChangeSpans[i].NewLength);
        }
        Assert.AreEqual(string.Concat(Enumerable.Repeat("ba", operationCount)), model.GetAllText());
        Assert.AreEqual(1L, model.VersionId);
        model.Undo();
        Assert.AreEqual(original, model.GetAllText());
    }

    [TestMethod]
    public void ExplicitWhitespaceIsPreservedAcrossLaterEdits()
    {
        var model = CreateModel("");
        model.ApplyEdits([new(new TextRange(0, 0, 0, 0), "\n    ")]);
        model.ApplyEdits([new(new TextRange(0, 0, 0, 0), "x")]);
        Assert.AreEqual("x\n    ", model.GetAllText());
        model.Undo();
        Assert.AreEqual("\n    ", model.GetAllText());
        model.Undo();
        Assert.AreEqual("", model.GetAllText());
    }

    private static TextModel CreateModel(string text)
        => new(text, Tokenization.GlobalLanguageId.PlainText);
}
