using FluidX.TextModels;
using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public interface ITokenizationSupport
{
    ITokenizerState GetInitialState();
    TokenizationResult Tokenize(string line, bool hasEOL, ITokenizerState state);
    EncodedTokenizationResult TokenizeEncoded(string line, bool hasEOL, ITokenizerState state);
    IBackgroundTokenizer CreateBackgroundTokenizer(TextModel textModel, IBackgroundTokenizationStore store); // TODO: Use ITextModel
}

public interface IBackgroundTokenizer : IDisposable
{
    /**
     * Instructs the background tokenizer to set the tokens for the given range again.
     *
     * This might be necessary if the renderer overwrote those tokens with heuristically computed ones for some viewport,
     * when the change does not even propagate to that viewport.
     */
    void RequestTokens(int startLineNumber, int endLineNumberExclusive);

    void ReportMismatchingTokens(int lineNumber);
}

public interface IBackgroundTokenizationStore
{
    void SetTokens(ContiguousMultilineTokens[] tokens);
    void SetEndState(int lineNumber, ITokenizerState endState);
    /**
     * Should be called to indicate that the background tokenization has finished for now.
     * (This triggers bracket pair colorization to re-parse the bracket pairs with token information)
     */
    void BackgroundTokenizationFinished();
}
