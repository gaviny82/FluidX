using FluidX.TextModels;
using FluidX.Tokenization;
using FluidX.Tokenization.TokenStores;
using TextMateSharp.Grammars;

namespace FluidX.TokenizationTextMateDemo;

internal sealed class TextMateTokenizationSupport : ITokenizationSupport
{
    private readonly IGrammar _grammar;
    private readonly GlobalLanguageId _languageId;

    public TextMateTokenizationSupport(IGrammar grammar, GlobalLanguageId languageId)
    {
        _grammar = grammar;
        _languageId = languageId;
    }

    public ITokenizerState GetInitialState()
        => new TextMateTokenizerState(null);

    public TokenizationResult Tokenize(string line, bool hasEOL, ITokenizerState state)
    {
        var encoded = TokenizeEncoded(line, hasEOL, state);
        var tokens = encoded.Tokens
            .Select(t => new TokenizerToken(t.StartIndex, string.Empty, _languageId))
            .ToArray();
        return new TokenizationResult(tokens, encoded.EndState);
    }

    public EncodedTokenizationResult TokenizeEncoded(string line, bool hasEOL, ITokenizerState state)
    {
        var tmState = state as TextMateTokenizerState
            ?? throw new ArgumentException("Unexpected tokenizer state type.", nameof(state));

        var lineText = new LineText(line);
        var result = tmState.Stack is null
            ? _grammar.TokenizeLine(lineText)
            : _grammar.TokenizeLine(lineText, tmState.Stack, TimeSpan.FromMilliseconds(100));

        int lineLength = line.Length;
        var encodedTokens = result.Tokens
            .Select(t =>
            {
                int start = Math.Clamp(t.StartIndex, 0, lineLength);
                var metadata = BuildMetadata(t.Scopes);
                return new EncodedTokenizerToken(start, metadata);
            })
            .ToArray();

        if (encodedTokens.Length == 0)
        {
            var defaultMetadata = new LineTokenMetadata
            {
                TokenType = StandardTokenType.Other,
                FontStyle = FontStyle.None,
                Foreground = ColorId.DefaultForeground,
                Background = ColorId.DefaultBackground,
                ContainsBalancedBrackets = true,
            };
            encodedTokens = [new EncodedTokenizerToken(0, defaultMetadata)];
        }

        return new EncodedTokenizationResult(
            encodedTokens,
            new TextMateTokenizerState(result.RuleStack));
    }

    public IBackgroundTokenizer CreateBackgroundTokenizer(TextModel textModel, IBackgroundTokenizationStore store)
        => new NoOpBackgroundTokenizer();

    private LineTokenMetadata BuildMetadata(List<string> scopes)
    {
        var metadata = new LineTokenMetadata
        {
            TokenType = MapTokenType(scopes),
            FontStyle = FontStyle.None,
            Foreground = ColorId.DefaultForeground,
            Background = ColorId.DefaultBackground,
            ContainsBalancedBrackets = true,
        };

        return metadata;
    }

    private static StandardTokenType MapTokenType(List<string> scopes)
    {
        foreach (var scope in scopes)
        {
            if (scope.Contains("comment", StringComparison.OrdinalIgnoreCase))
                return StandardTokenType.Comment;
            if (scope.Contains("string", StringComparison.OrdinalIgnoreCase))
                return StandardTokenType.String;
            if (scope.Contains("regex", StringComparison.OrdinalIgnoreCase)
                || scope.Contains("regexp", StringComparison.OrdinalIgnoreCase))
                return StandardTokenType.RegEx;
        }

        return StandardTokenType.Other;
    }

    private sealed class NoOpBackgroundTokenizer : IBackgroundTokenizer
    {
        public void RequestTokens(int startLineNumber, int endLineNumberExclusive)
        {
        }

        public void Dispose()
        {
        }
    }
}

internal sealed class TextMateTokenizerState : ITokenizerState
{
    public IStateStack? Stack { get; }

    public TextMateTokenizerState(IStateStack? stack)
    {
        Stack = stack;
    }

    public ITokenizerState Clone() => new TextMateTokenizerState(Stack);

    public bool Equals(ITokenizerState? other)
    {
        if (other is not TextMateTokenizerState tmOther)
            return false;
        return ReferenceEquals(Stack, tmOther.Stack);
    }
}
