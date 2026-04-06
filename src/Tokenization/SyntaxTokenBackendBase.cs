using FluidX.TextModels;
using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public abstract class SyntaxTokenBackendBase : IDisposable
{
    protected readonly ILanguageIdCodec _languageIdCodec;
    protected readonly TextModel _textModel;

    public abstract BackgroundTokenizationState BackgroundTokenizationState { get; }

    public abstract bool HasTokens { get; }

    internal EventHandler? BackgroundTokenizationStateChanged;
    internal EventHandler<ModelTokensChangedEventArgs>? TokensChanged;

    public SyntaxTokenBackendBase(ILanguageIdCodec languageIdCodec, TextModel textModel)
    {
        _languageIdCodec = languageIdCodec;
        _textModel = textModel;
    }

    public abstract void ResetTokenization(bool fireTokenChangeEvent);

    public abstract void HandleDidChangeAttached();

    public abstract void HandleDidChangeContent(ModelContentChangedEventArgs e);

    public abstract void ForceTokenization(int lineNumber);

    public abstract bool HasAccurateTokensForLine(int lineNumber);

    public abstract bool IsCheapToTokenize(int lineNumber);

    public void TokenizeIfCheap(int lineNumber)
    {
        if (IsCheapToTokenize(lineNumber))
            ForceTokenization(lineNumber);
    }

    public abstract LineTokens GetLineTokens(int lineNumber);

    public abstract StandardTokenType GetTokenTypeIfInsertingCharacter(int lineNumber, int column, string character);

    public abstract LineTokens[]? TokenizeLinesAt(int lineNumber, ReadOnlySpan<string> lines);

    public virtual void Dispose() { }
}

public record class ModelTokensChangedEventArgs(
    bool SemanticTokensApplied,
    Range[] Ranges
);
