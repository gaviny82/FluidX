using FluidX.TextBuffers;
using FluidX.TextModels;
using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public class TokenizerSyntaxTokenBackend : SyntaxTokenBackendBase
{
    private DefaultBackgroundTokenizer? _backgroundTokenizer = null;
    private readonly ContiguousTokensStore _tokens;
    private readonly string _languageId;

    private TokenizerWithStateStoreAndTextModel? _tokenizer => _backgroundTokenizer?.TokenizerWithStateStore;

    // TODO: private readonly _attachedViewStates = this._register(new DisposableMap<IAttachedView, AttachedViewHandler>());

    // TODO: TokensChanged, TokenizerStateChanged event

    public override bool HasTokens => throw new NotImplementedException();

    public TokenizerSyntaxTokenBackend(
        ILanguageIdCodec languageIdCodec,
        TextModel textModel,
        string languageId)
        : base(languageIdCodec, textModel)
    {
        _tokens = new ContiguousTokensStore(languageIdCodec);
        _languageId = languageId;
        TokenizationRegistry.Instance.TokenizationSupportsChanged += OnTokenizationSupportsChanged;
    }

    private void OnTokenizationSupportsChanged(object? sender, TokenizationSupportsChangedEventArgs e)
    {
        if (e.ChangedLanguages.Contains(_languageId))
            ResetTokenization();
    }

    public override void Dispose()
    {
        TokenizationRegistry.Instance.TokenizationSupportsChanged -= OnTokenizationSupportsChanged;
        GC.SuppressFinalize(this);
    }

    public override void ResetTokenization(bool fireTokenChangeEvent = true)
    {
        _tokens.Flush();
        if (fireTokenChangeEvent)
        {
            TokensChanged?.Invoke(this, new ModelTokensChangedEventArgs(
                false,
                [new Range(1, _textModel.TextBuffer.LineCount)]
            ));
        }

        Func<(ITokenizationSupport?, ITokenizerState?)> initializeTokenization = () =>
        {
            if (_textModel.IsTooLargeForTokenization)
                return (null, null);
            var tokenizationSupport = TokenizationRegistry.Instance.GetSupport(_languageId);
            if (tokenizationSupport is null)
                return (null, null);
            return (tokenizationSupport, tokenizationSupport.GetInitialState());
        };
        var (tokenizationSupport, initialState) = initializeTokenization();
        if (tokenizationSupport is not null && initialState is not null)
        {
            _tokenizer = new TokenizerWithStateStoreAndTextModel(
                _textModel.TextBuffer.LineCount,
                tokenizationSupport,
                _textModel,
                _languageIdCodec);
        }
        else
        {
            _tokenizer = null;
        }
    }

    public override void HandleDidChangeAttached()
    {
        // TODO:
    }

    public override void HandleDidChangeContent(ModelContentChangedEventArgs e)
    {
        if (e.IsFlush)
        {
            ResetTokenization(); // Don't fire the event, as the view might not have got the text c event yet
        }
        else if (!e.IsEolChange) // We don't have to do anything on an EOL c
        {
            foreach (var c in e.Changes)
            {
                (int eolCount, int firstLineLength, _, _) = EOLCounter.CountEOL(c.Text);
                _tokens.AcceptEdit(c.Range, eolCount, firstLineLength);
            }
            _tokenizer?.Store.AcceptChanges([..e.Changes]);
        }
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

    public override bool HasAccurateTokensForLine(int lineNumber)
    {
        throw new NotImplementedException();
    }

    public override bool IsCheapToTokenize(int lineNumber)
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
    Done = 2,
}
