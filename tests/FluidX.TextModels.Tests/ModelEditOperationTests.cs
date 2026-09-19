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
        var model = new PlainTextModel("\uFEFFone\r\ntwo\nthree\rfour", DefaultEndOfLine.LF);

        Assert.AreEqual("\uFEFF", model.BOM);
        Assert.AreEqual(EndOfLineSequence.CRLF, model.EOL);
        Assert.AreEqual("one\r\ntwo\nthree\rfour", model.TextBuffer.GetTextInRange(model.GetFullModelRange()));
        Assert.AreEqual("one\r\ntwo\r\nthree\r\nfour", model.GetValue());
        Assert.AreEqual(21, model.GetValueLengthInRange(model.GetFullModelRange()));
        Assert.AreEqual(18, model.GetValueLengthInRange(model.GetFullModelRange(), EndOfLinePreference.LF));
        Assert.AreEqual("\uFEFFone\r\ntwo\r\nthree\r\nfour", model.GetValue(preserveBOM: true));

        model.SetEOL(EndOfLineSequence.LF);

        Assert.AreEqual("one\ntwo\nthree\nfour", model.TextBuffer.GetTextInRange(model.GetFullModelRange()));
    }

    [TestMethod]
    public void PlainTextModelSupportsEditUndoAndRedo()
    {
        var model = new PlainTextModel("hello", DefaultEndOfLine.LF);
        model.Edit(new TextEdit([new TextReplacement(new TextRange(0, 5, 0, 5), " world")]));

        Assert.AreEqual("hello world", model.GetValue());
        model.Undo();
        Assert.AreEqual("hello", model.GetValue());
        model.Redo();
        Assert.AreEqual("hello world", model.GetValue());
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

        Assert.AreEqual("", model.GetValue());
    }

    private static TextModel CreateModel(string text)
        => new(text, DefaultEndOfLine.LF, Tokenization.GlobalLanguageId.PlainText);
}
