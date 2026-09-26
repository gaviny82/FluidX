using FluidX.TextModels;
using FluidX.TextBuffers;
using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public sealed class TokenizationTextModelPart : IDisposable
{
    private readonly TextModel _textModel;
    private readonly ModelLanguageIdMapper _languageIdMapper;
    private readonly SparseTokensStore _semanticTokens;
    private SyntaxTokenBackendBase _tokens;

    public GlobalLanguageId LanguageId { get; private set; }

    public LanguageId LocalLanguageId => _languageIdMapper.Encode(LanguageId);

    public event EventHandler<ModelLanguageChangedEventArgs>? LanguageChanged;
    public event EventHandler<ModelLanguageConfigurationChangedEventArgs>? LanguageConfigurationChanged;
    public event EventHandler<ModelTokensChangedEventArgs>? TokensChanged;
    public event EventHandler<ModelFontTokensChangedEventArgs>? FontTokensChanged;
    public event EventHandler? BackgroundTokenizationStateChanged;

    public TokenizationTextModelPart(
        TextModel textModel,
        GlobalLanguageId languageId)
    {
        _textModel = textModel;
        _languageIdMapper = new ModelLanguageIdMapper();
        LanguageId = languageId;

        _tokens = CreateSyntaxBackend(LanguageId, _languageIdMapper);
        _tokens.TokensChanged += OnTokensChanged;
        _tokens.BackgroundTokenizationStateChanged += OnBackgroundTokenizationStateChanged;

        _semanticTokens = new SparseTokensStore();

        _tokens.ResetTokenization(fireTokenChangeEvent: false);
    }

    private SyntaxTokenBackendBase CreateSyntaxBackend(GlobalLanguageId languageId, ModelLanguageIdMapper languageIdMapper)
        => new TokenizerSyntaxTokenBackend(_textModel, languageId, languageIdMapper);

    public bool HasTokens => _tokens.HasTokens;

    public BackgroundTokenizationState BackgroundTokenizationState
        => _tokens.BackgroundTokenizationState;

    private void OnTokensChanged(object? sender, ModelTokensChangedEventArgs e)
    {
        TokensChanged?.Invoke(this, e);
    }

    private void OnBackgroundTokenizationStateChanged(object? sender, EventArgs e)
    {
        BackgroundTokenizationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void HandleDidChangeContent(ModelContentChangedEventArgs e)
    {
        if (e.IsFlush)
        {
            _semanticTokens.Flush();
        }
        else if (!e.IsEolChange)
        {
            foreach (var c in e.Changes)
            {
                var (eolCount, firstLineLength, lastLineLength, _) = EOLCounter.CountEOL(c.Text);
                _semanticTokens.AcceptEdit(
                    c.Range,
                    eolCount,
                    firstLineLength,
                    lastLineLength,
                    c.Text.Length > 0 ? c.Text[0] : '\0');
            }
        }

        _tokens.HandleDidChangeContent(e);
    }

    public void HandleDidChangeAttached()
    {
        _tokens.HandleDidChangeAttached();
    }

    public LineTokens GetLineTokens(int lineIndex)
    {
        ValidateLineIndex(lineIndex);
        var syntaxTokens = _tokens.GetLineTokens(lineIndex);
        return _semanticTokens.AddSparseTokens(lineIndex, syntaxTokens);
    }

    public void ResetTokenization()
    {
        _tokens.ResetTokenization(fireTokenChangeEvent: true);
    }

    public void ForceTokenization(int lineIndex)
    {
        ValidateLineIndex(lineIndex);
        _tokens.ForceTokenization(lineIndex);
    }

    public bool HasAccurateTokensForLine(int lineIndex)
    {
        ValidateLineIndex(lineIndex);
        return _tokens.HasAccurateTokensForLine(lineIndex);
    }

    public bool IsCheapToTokenize(int lineIndex)
    {
        ValidateLineIndex(lineIndex);
        return _tokens.IsCheapToTokenize(lineIndex);
    }

    public void TokenizeIfCheap(int lineIndex)
    {
        ValidateLineIndex(lineIndex);
        _tokens.TokenizeIfCheap(lineIndex);
    }

    public StandardTokenType GetTokenTypeIfInsertingCharacter(int lineIndex, int columnIndex, string character)
        => _tokens.GetTokenTypeIfInsertingCharacter(lineIndex, columnIndex, character);

    public LineTokens[]? TokenizeLinesAt(int lineIndex, ReadOnlySpan<string> lines)
        => _tokens.TokenizeLinesAt(lineIndex, lines);

    public GlobalLanguageId GetLanguageIdAtPosition(int lineIndex, int columnIndex)
    {
        var position = _textModel.ValidatePosition(new TextPosition(lineIndex, columnIndex));
        var lineTokens = GetLineTokens(position.LineIndex);
        var localLanguageId = lineTokens.GetLanguageId(lineTokens.FindTokenIndexAtOffset(position.ColumnIndex));
        return _languageIdMapper.Decode(localLanguageId);
    }

    public void SetLanguageId(GlobalLanguageId languageId, string source = "api")
    {
        if (LanguageId == languageId)
            return;

        var e = new ModelLanguageChangedEventArgs(LanguageId, languageId, source);

        LanguageId = languageId;

        ReplaceSyntaxBackend(languageId);
        LanguageChanged?.Invoke(this, e);
        LanguageConfigurationChanged?.Invoke(this, new ModelLanguageConfigurationChangedEventArgs());
    }

    private void ReplaceSyntaxBackend(GlobalLanguageId languageId)
    {
        _tokens.TokensChanged -= OnTokensChanged;
        _tokens.BackgroundTokenizationStateChanged -= OnBackgroundTokenizationStateChanged;
        _tokens.Dispose();

        _tokens = CreateSyntaxBackend(languageId, _languageIdMapper);
        _tokens.TokensChanged += OnTokensChanged;
        _tokens.BackgroundTokenizationStateChanged += OnBackgroundTokenizationStateChanged;
        _tokens.ResetTokenization(fireTokenChangeEvent: true);
    }

    public void HandleLanguageConfigurationServiceChange()
    {
        LanguageConfigurationChanged?.Invoke(this, new ModelLanguageConfigurationChangedEventArgs());
    }

    #region Sematic Tokens (Not Implemented)

    public void SetSemanticTokens(List<SparseMultilineTokens>? tokens, bool isComplete)
        => throw new NotImplementedException("Semantic tokenization is not implemented yet.");

    public bool HasCompleteSemanticTokens()
        => throw new NotImplementedException("Semantic tokenization is not implemented yet.");

    public bool HasSomeSemanticTokens()
        => throw new NotImplementedException("Semantic tokenization is not implemented yet.");

    public void SetPartialSemanticTokens(TextRange range, List<SparseMultilineTokens> tokens)
        => throw new NotImplementedException("Semantic tokenization is not implemented yet.");

    #endregion

    private void ValidateLineIndex(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _textModel.TextBuffer.LineCount)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));
    }

    public void Dispose()
    {
        _textModel.ContentChanged -= OnTextModelContentChanged;
        _tokens.TokensChanged -= OnTokensChanged;
        _tokens.BackgroundTokenizationStateChanged -= OnBackgroundTokenizationStateChanged;
        _tokens.Dispose();
        GC.SuppressFinalize(this);
    }
}

public readonly record struct ModelLanguageChangedEventArgs(
    GlobalLanguageId OldLanguage,
    GlobalLanguageId NewLanguage,
    string Source
);

public readonly record struct ModelLanguageConfigurationChangedEventArgs;

public readonly record struct ModelFontTokensChangedEventArgs;
