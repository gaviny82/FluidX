using FluidX.TextModels;
using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public class TokenizerSyntaxTokenBackend : SyntaxTokenBackendBase
{
    private TokenizerWithStateStoreAndTextModel? _tokenizer = null;
    private DefaultBackgroundTokenizer ? _defaultBackgroundTokenizer = null;
    // TODO: private readonly _attachedViewStates = this._register(new DisposableMap<IAttachedView, AttachedViewHandler>());

    private BackgroundTokenizationState BackgroundTokenizationState
    {
        get => field;
        set
        {
            field = value;
            BackgroundTokenizationStateChanged?.Invoke(this, EventArgs.Empty);
        }
    } = BackgroundTokenizationState.InProgress;

    private readonly ContiguousTokensStore _tokens;


    public override bool HasTokens => throw new NotImplementedException();

    public TokenizerSyntaxTokenBackend(
        ILanguageIdCodec languageIdCodec,
        TextModel textModel,
        string languageId)
        : base(languageIdCodec, textModel)
    {
        _tokens = new ContiguousTokensStore(languageIdCodec);
        // TODO: Register tokenization support changed event
    }

    public override void ForceTokenization(int lineNumber)
    {
        throw new NotImplementedException();
    }

    public override LineTokens GetLineTokens(int lineNumber)
    {
        throw new NotImplementedException();
    }

    public override StandardTokenType GetTokenTypeIfInsertingCharacter(int lineNumber, int column, string character)
    {
        throw new NotImplementedException();
    }

    public override void HandleDidChangeAttached()
    {
        throw new NotImplementedException();
    }

    public override void HandleDidChangeContent(ModelContentChangedEventArgs e)
    {
        throw new NotImplementedException();
    }

    public override bool HasAccurateTokensForLine(int lineNumber)
    {
        throw new NotImplementedException();
    }

    public override bool IsCheapToTokenize(int lineNumber)
    {
        throw new NotImplementedException();
    }

    public override void ResetTokenization(bool fireTokenChangeEvent)
    {
        throw new NotImplementedException();
    }

    public override LineTokens[]? TokenizeLinesAt(int lineNumber, ReadOnlySpan<string> lines)
    {
        throw new NotImplementedException();
    }
}

public enum BackgroundTokenizationState
{
    InProgress = 1,
    Completed = 2,
}
