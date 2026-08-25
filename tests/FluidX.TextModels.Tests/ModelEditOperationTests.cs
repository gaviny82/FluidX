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
    public void TrackedBatchPreservesIndividualInverseOperations()
    {
        const int operationCount = 1000;
        var model = CreateModel(new string('a', operationCount));
        var operations = Enumerable.Range(0, operationCount)
            .Select(index => new ModelEditOperation(
                new TextRange(1, index + 1, 1, index + 2),
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
                new TextRange(1, index + 1, 1, index + 2),
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
                new ModelEditOperation(new TextRange(1,1,1,1), "\n    ")
                {
                    IsAutowhitespaceEdit = true
                }
            ],
            beforeCursorState: null,
            cursorStateComputer: null);

        Assert.AreEqual("\n    ", model.GetValue());

        model.PushEditOperations(
            [
                new ModelEditOperation(new TextRange(1,1,1,1), "x")
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
