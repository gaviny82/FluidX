using FluidX.TextModels;
using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public abstract class SyntaxTokenBackendBase : IDisposable
{
    protected readonly TextModel _textModel;

    public abstract BackgroundTokenizationState BackgroundTokenizationState { get; }

    public abstract bool HasTokens { get; }

    internal EventHandler? BackgroundTokenizationStateChanged;
    internal EventHandler<ModelTokensChangedEventArgs>? TokensChanged;

    public SyntaxTokenBackendBase(TextModel textModel)
    {
        _textModel = textModel;
    }

    public abstract void ResetTokenization(bool fireTokenChangeEvent);

    public abstract void HandleDidChangeAttached();

    public abstract void HandleDidChangeContent(ModelContentChangedEventArgs e);

    public abstract void ForceTokenization(int lineIndex);

    public abstract bool HasAccurateTokensForLine(int lineIndex);

    public abstract bool IsCheapToTokenize(int lineIndex);

    public void TokenizeIfCheap(int lineIndex)
    {
        if (IsCheapToTokenize(lineIndex))
            ForceTokenization(lineIndex);
    }

    public abstract LineTokens GetLineTokens(int lineIndex);

    public abstract StandardTokenType GetTokenTypeIfInsertingCharacter(int lineIndex, int columnIndex, string character);

    public abstract LineTokens[]? TokenizeLinesAt(int lineIndex, ReadOnlySpan<string> lines);

    public virtual void Dispose() { }
}

public record class ModelTokensChangedEventArgs(
    bool SemanticTokensApplied,
    Range[] Ranges
);
