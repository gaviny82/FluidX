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

        Assert.AreEqual("\uFEFF", model.BOM);
        Assert.AreEqual(DocumentEndOfLine.Mixed, model.EOL);
        model.DefaultEOL = EndOfLine.CRLF;
        Assert.AreEqual("one\r\ntwo\nthree\rfour", model.TextBuffer.GetTextInRange(model.GetFullModelRange()));
        Assert.AreEqual("one\r\ntwo\r\nthree\r\nfour", model.GetValue());
        Assert.AreEqual(21, model.GetValueLengthInRange(model.GetFullModelRange()));
        Assert.AreEqual(18, model.GetValueLengthInRange(model.GetFullModelRange(), EndOfLinePreference.LF));
        Assert.AreEqual("\uFEFFone\r\ntwo\r\nthree\r\nfour", model.GetValue(preserveBOM: true));

        model.SetEOL(EndOfLine.LF);

        Assert.AreEqual("one\ntwo\nthree\nfour", model.TextBuffer.GetTextInRange(model.GetFullModelRange()));
    }

    [TestMethod]
    public void PlainTextModelSupportsEditUndoAndRedo()
    {
        var model = new PlainTextModel("hello", EndOfLine.LF);
        model.Edit(new TextEdit([new TextReplacement(new TextRange(0, 5, 0, 5), " world")]));

        Assert.AreEqual("hello world", model.GetValue());
        model.Undo();
        Assert.AreEqual("hello", model.GetValue());
        model.Redo();
        Assert.AreEqual("hello world", model.GetValue());
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
        Assert.AreEqual("a\r\nb", model.GetValue());
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
            Assert.AreEqual("a\r\nb", model.GetValue());
        }
    }

    [TestMethod]
    public void PlainTextModelAllowsReplacingCompleteCrlf()
    {
        var model = new PlainTextModel("a\r\nb", EndOfLine.LF);

        model.ApplyEdits(
            [new ModelEditOperation(new TextRange(0, 1, 1, 0), "\n")],
            computeUndoEdits: false);

        Assert.AreEqual("a\nb", model.GetValue());
    }

    [TestMethod]
    public void TextModelInheritsPlainTextModel()
    {
        PlainTextModel model = CreateModel("text");
        Assert.AreEqual("text", model.GetValue());
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
        Assert.AreEqual("", model.GetValue());
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
        Assert.AreEqual("", model.GetValue());
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

        Assert.AreEqual("\n    ", model.GetValue());

        model.PushEditOperations(
            [
                new ModelEditOperation(new TextRange(0, 0, 0, 0), "x")
            ],
            beforeCursorState: null,
            cursorStateComputer: null);

        Assert.AreEqual("x\n", model.GetValue());

        model.Undo();

        Assert.AreEqual("\n    ", model.GetValue());
        model.Undo();
        Assert.AreEqual("", model.GetValue());
    }

    private static TextModel CreateModel(string text)
        => new(text, EndOfLine.LF, Tokenization.GlobalLanguageId.PlainText);
}
