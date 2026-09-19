using FluidX.TextBuffers;
using FluidX.Tokenization;
using FluidX.Tokenization.TokenStores;

namespace FluidX.TextModels.Tests;

[TestClass]
public sealed class ZeroBasedModelTests
{
    [TestMethod]
    public void ValidationClampsToZeroBasedDocumentAndSurrogateBoundaries()
    {
        var model = new PlainTextModel("😀\nabc", DefaultEndOfLine.LF);
        Assert.AreEqual(new TextRange(0, 0, 1, 3), model.GetFullModelRange());
        Assert.AreEqual(default(TextPosition), model.ValidatePosition(new(-1, 9)));
        Assert.AreEqual(new TextPosition(1, 3), model.ValidatePosition(new(2, 0)));
        Assert.AreEqual(new TextPosition(1, 0), model.ValidatePosition(new(1, -1)));
        Assert.AreEqual(default(TextPosition), model.ValidatePosition(new(0, 1)));
        Assert.AreEqual(new TextPosition(0, 1), model.ValidatePosition(new(0, 1), true));
        Assert.AreEqual(default(TextRange), new PlainTextModel("", DefaultEndOfLine.LF).GetFullModelRange());
    }

    [TestMethod]
    public void TextModelEditsFirstAndLastLinesAndRestoresThem()
    {
        var model = new TextModel("abc\ndef", DefaultEndOfLine.LF, GlobalLanguageId.PlainText);
        try
        {
            model.Edit(new TextEdit([new(new TextRange(0, 0, 1, 3), "x\ny\nz")]));
            Assert.AreEqual("x\ny\nz", model.GetValue());
            Assert.AreEqual(new TextRange(0, 0, 2, 1), model.GetFullModelRange());
            model.Undo();
            Assert.AreEqual("abc\ndef", model.GetValue());
            model.Redo();
            Assert.AreEqual("x\ny\nz", model.GetValue());
            Assert.IsNotNull(model.Tokenization.GetLineTokens(0));
            Assert.IsNotNull(model.Tokenization.GetLineTokens(2));
        }
        finally
        {
            model.Tokenization.Dispose();
        }
    }

    [TestMethod]
    public void TokenizationStateIncludesFirstLineAndStopsAfterLastLine()
    {
        var store = new TrackingTokenizationStateStore(2);
        var state = new State();
        Assert.AreEqual(0, store.FirstInvalidEndStateLineIndex);
        Assert.AreSame(state, store.GetStartState(0, state));
        store.SetEndState(0, state);
        Assert.AreEqual(1, store.FirstInvalidEndStateLineIndex);
        Assert.AreSame(state, store.GetStartState(1, state));
        store.SetEndState(1, state);
        Assert.IsTrue(store.AllStatesValid);
        store.AcceptChange(new Range(0, 1), 2);
        Assert.AreEqual(0, store.FirstInvalidEndStateLineIndex);
        store.SetEndState(0, state);
        store.SetEndState(1, state);
        store.SetEndState(2, state);
        Assert.IsTrue(store.AllStatesValid);
    }

    private sealed class State : ITokenizerState
    {
        public ITokenizerState Clone() => this;
        public bool Equals(ITokenizerState? other) => ReferenceEquals(this, other);
    }

    [TestMethod]
    public void TokenBlocksInsertAfterTheEditedLineAndExposeZeroBasedRanges()
    {
        var block = new ContiguousMultilineTokens(5,
            [[new LineToken(3, default)], [new LineToken(4, default)]]);
        block.ApplyEdit(new TextRange(5, 1, 5, 1), "\n");
        Assert.AreEqual(1, block.GetLineTokens(5)[0].EndOffset);
        Assert.AreEqual(0, block.GetLineTokens(6).Length);
        Assert.AreEqual(4, block.GetLineTokens(7)[0].EndOffset);

        var sparse = new SparseMultilineTokens(5, new SparseMultilineTokenStorage(
            [new SparseLineToken(0, 0, 3, default)]));
        Assert.AreEqual(new TextRange(5, 0, 5, 3), sparse.GetRange());
    }
}
