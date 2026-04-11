using FluidX.TextBuffers;
using FluidX.TextModels;
using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public class TokenizerSyntaxTokenBackend : SyntaxTokenBackendBase
{
    private DefaultBackgroundTokenizer? _backgroundTokenizer = null;
    private readonly ContiguousTokensStore _tokens;
    private readonly GlobalLanguageId _languageId;
    private readonly ModelLanguageIdMapper _languageIdMapper;
    private LanguageId LocalLanguageId => _languageIdMapper.Encode(_languageId);

    private TokenizerWithStateStoreAndTextModel? _tokenizer => _backgroundTokenizer?.TokenizerWithStateStore;

    // TODO: private readonly _attachedViewStates = this._register(new DisposableMap<IAttachedView, AttachedViewHandler>());

    // TODO: TokensChanged, TokenizerStateChanged event

    public override bool HasTokens => _tokens.HasTokens;

    public TokenizerSyntaxTokenBackend(
        TextModel textModel,
        GlobalLanguageId languageId,
        ModelLanguageIdMapper languageIdMapper)
        : base(textModel)
    {
        _tokens = new ContiguousTokensStore();
        _languageId = languageId;
        _languageIdMapper = languageIdMapper;
        LanguageRegistry.Instance.SupportsChanged += OnTokenizationSupportsChanged;
    }

    private void OnTokenizationSupportsChanged(object? sender, LanguageSupportsChangedEventArgs e)
    {
        if (e.ChangedLanguages.Contains(_languageId))
            ResetTokenization();
    }

    public override void Dispose()
    {
        LanguageRegistry.Instance.SupportsChanged -= OnTokenizationSupportsChanged;
        if (_backgroundTokenizer is not null)
        {
            _backgroundTokenizer.BackgroundTokenizationStateChanged -= OnBackgroundTokenizationStateChanged;
            _backgroundTokenizer.Dispose();
            _backgroundTokenizer = null;
        }
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
            var tokenizationSupport = LanguageRegistry.Instance.GetSupport(_languageId);
            if (tokenizationSupport is null)
                return (null, null);
            return (tokenizationSupport, tokenizationSupport.GetInitialState());
        };
        var (tokenizationSupport, initialState) = initializeTokenization();
        if (_backgroundTokenizer is not null)
        {
            _backgroundTokenizer.BackgroundTokenizationStateChanged -= OnBackgroundTokenizationStateChanged;
        }
        if (tokenizationSupport is not null && initialState is not null)
        {
            _backgroundTokenizer?.Dispose();
            _backgroundTokenizer = new DefaultBackgroundTokenizer(
                new TokenizerWithStateStoreAndTextModel(
                _textModel.TextBuffer.LineCount,
                tokenizationSupport,
                _textModel,
                _languageIdMapper),
                _tokens
            );
            _backgroundTokenizer.BackgroundTokenizationStateChanged += OnBackgroundTokenizationStateChanged;
            _backgroundTokenizer.BeginTextBufferEdit();
            _backgroundTokenizer.EndTextBufferEdit();
        }
        else
        {
            _backgroundTokenizer?.Dispose();
            _backgroundTokenizer = null;
        }

        BackgroundTokenizationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackgroundTokenizationStateChanged(object? sender, EventArgs e)
    {
        BackgroundTokenizationStateChanged?.Invoke(this, EventArgs.Empty);
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
        else if (!e.IsEolChange) // We don't have to do anything on an EOL change
        {
            _backgroundTokenizer?.BeginTextBufferEdit();
            try
            {
                foreach (var c in e.Changes)
                {
                    (int eolCount, int firstLineLength, _, _) = EOLCounter.CountEOL(c.Text);
                    _tokens.AcceptEdit(c.Range, eolCount, firstLineLength);
                }
                _tokenizer?.Store.AcceptChanges([..e.Changes]);
            }
            finally
            {
                _backgroundTokenizer?.EndTextBufferEdit();
            }
        }
    }

    private LineTokenChangeRange[] SetTokens(ContiguousMultilineTokens[] tokens)
    {
        var changes = _tokens.SetMultilineTokens(tokens, _textModel);
        if (changes.Length > 0)
        {
            TokensChanged?.Invoke(this, new ModelTokensChangedEventArgs(
                false,
                changes.Select(c => new Range(c.FromLineNumber, c.ToLineNumber + 1)).ToArray()
            ));
        }
        return changes;
    }

    private void RefreshAllVisibleLineTokens()
    {
        // TODO: attached view states
    }

    private void RefreshRanges(Range[] ranges)
    {
        foreach (Range range in ranges)
        {
            RefreshRange(range.Start.Value, range.End.Value - 1);
        }
    }

    private void RefreshRange(int startLineNumber, int endLineNumber)
    {
        if (_tokenizer is null)
            return;

        int lineCount = _textModel.TextBuffer.LineCount;
        startLineNumber = Math.Clamp(startLineNumber, 1, lineCount);
        endLineNumber = Math.Clamp(endLineNumber, 1, lineCount);

        var builder = new ContiguousMultilineTokensBuilder();
        bool heuristicTokens = _tokenizer.TokenizeHeuristically(builder, startLineNumber, endLineNumber);
        var changedTokens = SetTokens(builder.Finalize().ToArray());
        if (heuristicTokens)
        {
            // We overrode tokens with heuristically computed ones.
            // Because old states might get reused (thus stopping invalidation),
            // we have to explicitly request the tokens for the changed ranges again.
            foreach (var c in changedTokens)
            {
                _backgroundTokenizer?.RequestTokens(c.FromLineNumber, c.ToLineNumber + 1);
            }
        }
    }

    public override void ForceTokenization(int lineNumber)
    {
        var builder = new ContiguousMultilineTokensBuilder();
        // Ensure we don't have concurrent edits
        _backgroundTokenizer?.BeginTextBufferEdit();
        _tokenizer?.UpdateTokensUntilLine(builder, lineNumber);
        SetTokens(builder.Finalize().ToArray());
        _backgroundTokenizer?.EndTextBufferEdit();
    }

    public override bool HasAccurateTokensForLine(int lineNumber)
    {
        if (_tokenizer is null)
            return true;
        return _tokenizer.HasAccurateTokensForLine(lineNumber);
    }

    public override bool IsCheapToTokenize(int lineNumber)
    {
        if (_tokenizer is null)
            return true;
        return _tokenizer.IsCheapToTokenize(lineNumber);
    }

    public override BackgroundTokenizationState BackgroundTokenizationState
        => _backgroundTokenizer?.BackgroundTokenizationState ?? BackgroundTokenizationState.Done;

    public override LineTokens GetLineTokens(int lineNumber)
    {
        string lineText = _textModel.TextBuffer.GetLineContent(lineNumber);
        return _tokens.GetTokens(LocalLanguageId, lineNumber - 1, lineText);
    }

    public override StandardTokenType GetTokenTypeIfInsertingCharacter(int lineNumber, int column, string character)
    {
        if (_tokenizer is null)
            return StandardTokenType.Other;
        TextPosition position = _textModel.ValidatePosition(new(lineNumber, column));
        ForceTokenization(lineNumber);
        return _tokenizer.GetTokenTypeIfInsertingCharacter(position, character);
    }

    public override LineTokens[]? TokenizeLinesAt(int lineNumber, ReadOnlySpan<string> lines)
    {
        if (_tokenizer is null)
            return null;
        ForceTokenization(lineNumber);
        return _tokenizer.TokenizeLinesAt(lineNumber, lines.ToArray())?.ToArray();
    }
}

public enum BackgroundTokenizationState
{
    InProgress = 1,
    Done = 2,
}
