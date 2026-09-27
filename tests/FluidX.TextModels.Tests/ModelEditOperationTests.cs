using System;
using System.Collections.Generic;
using System.Text;
using FluidX.TextBuffers;
using FluidX.TextModels;

namespace FluidX.TextModels.Tests;

[TestClass]
public class ModelEditOperationTests
{
    [TestMethod]
    public void PlainTextModelOwnsDocumentPolicyWhileBufferPreservesRawEOLs()
    {
        var model = new PlainTextModel("\uFEFFone\r\ntwo\nthree\rfour", EndOfLine.LF);

        Assert.IsTrue(model.HasBOM);
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
        model.DefaultEOL = EndOfLine.CRLF;
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

        model.SetEOL(EndOfLine.LF);

        Assert.AreEqual("one\ntwo\nthree\nfour", model.TextBuffer.GetTextInRange(model.GetFullModelRange()));
        Assert.AreEqual(DocumentEndOfLine.LF, model.EOL);
        Assert.AreEqual(21, model.GetTextLengthInRange(model.GetFullModelRange(), EndOfLine.CRLF));
        Assert.AreEqual(18, model.GetTextLengthInRange(model.GetFullModelRange(), EndOfLine.CR));
    }

    [TestMethod]
    public void PlainTextModelSupportsEditUndoAndRedo()
    {
        var model = new PlainTextModel("hello", EndOfLine.LF);
        model.Edit(new TextEdit([new TextReplacement(new TextRange(0, 5, 0, 5), " world")]));

        Assert.AreEqual("hello world", model.GetAllText());
        model.Undo();
        Assert.AreEqual("hello", model.GetAllText());
        model.Redo();
        Assert.AreEqual("hello world", model.GetAllText());
    }

    [TestMethod]
    public void PlainTextModelRejectsEditEndpointsInsideCrlfBeforeApplyingBatch()
    {
        var model = new PlainTextModel("a\r\nb", EndOfLine.LF);
        ModelEditOperation[] operations =
        [
            new(new TextRange(0, 0, 0, 1), "A"),
            new(new TextRange(0, 2, 0, 2), "X")
        ];

        Assert.ThrowsExactly<ArgumentException>(() => model.ApplyEdits(operations, computeUndoEdits: false));
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
            var model = new PlainTextModel("a\r\nb", EndOfLine.LF);
            Assert.ThrowsExactly<ArgumentException>(() => model.ApplyEdits(
                [new ModelEditOperation(range, "X")],
                computeUndoEdits: false));
            Assert.AreEqual("a\r\nb", model.GetAllText());
        }
    }

    [TestMethod]
    public void PlainTextModelAllowsReplacingCompleteCrlf()
    {
        var model = new PlainTextModel("a\r\nb", EndOfLine.LF);

        model.ApplyEdits(
            [new ModelEditOperation(new TextRange(0, 1, 1, 0), "\n")],
            computeUndoEdits: false);

        Assert.AreEqual("a\nb", model.GetAllText());
    }

    [TestMethod]
    public void TextModelInheritsPlainTextModel()
    {
        PlainTextModel model = CreateModel("text");
        Assert.AreEqual("text", model.GetAllText());
    }

    [TestMethod]
    public void TrackedBatchPreservesIndividualInverseOperations()
    {
        const int operationCount = 1000;
        var model = CreateModel(new string('a', operationCount));
        var operations = Enumerable.Range(0, operationCount)
            .Select(index => new ModelEditOperation(
                new TextRange(0, index, 0, index + 1),
                ""))
            .ToArray();
        operations[0] = operations[0] with { IsTracked = true };

        var inverseOperations = model.ApplyEdits(operations, computeUndoEdits: true);

        Assert.HasCount(operationCount, inverseOperations!);
        Assert.AreEqual("", model.GetAllText());
    }

    [TestMethod]
    public void UntrackedLargeBatchCanBeReduced()
    {
        const int operationCount = 1000;
        var model = CreateModel(new string('a', operationCount));
        var operations = Enumerable.Range(0, operationCount)
            .Select(index => new ModelEditOperation(
                new TextRange(0, index, 0, index + 1),
                ""))
            .ToArray();

        var inverseOperations = model.ApplyEdits(operations, computeUndoEdits: true);

        Assert.HasCount(1, inverseOperations!);
        Assert.AreEqual("", model.GetAllText());
    }

    [TestMethod]
    public void AutomaticWhitespaceIsTrimmedByNextModelEdit()
    {
        var model = CreateModel("");
        model.PushEditOperations(
            [
                new ModelEditOperation(new TextRange(0, 0, 0, 0), "\n    ")
                {
                    IsAutowhitespaceEdit = true
                }
            ],
            beforeCursorState: null,
            cursorStateComputer: null);

        Assert.AreEqual("\n    ", model.GetAllText());

        model.PushEditOperations(
            [
                new ModelEditOperation(new TextRange(0, 0, 0, 0), "x")
            ],
            beforeCursorState: null,
            cursorStateComputer: null);

        Assert.AreEqual("x\n", model.GetAllText());

        model.Undo();

        Assert.AreEqual("\n    ", model.GetAllText());
        model.Undo();
        Assert.AreEqual("", model.GetAllText());
    }

    private static TextModel CreateModel(string text)
        => new(text, EndOfLine.LF, Tokenization.GlobalLanguageId.PlainText);
}
