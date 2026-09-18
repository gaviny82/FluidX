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

        var lineText = new LineText(line); // TODO: Probably need to include EOL here. Check states affected by whitespace. // Conclusion: TextMateSharp automatically appends \n if the passed string does not end with it. this causes an allocation of char[] but does not influence the result
        var result = tmState.Stack is null
            ? _grammar.TokenizeLine2(lineText)
            : _grammar.TokenizeLine2(lineText, tmState.Stack, TimeSpan.FromMilliseconds(100));
        var tokens = result.Tokens;
        int tokenCount = tokens.Length / 2;
        int lineLength = line.Length;
        var encodedTokens = new EncodedTokenizerToken[tokenCount];
        for (int i = 0; i < tokenCount; i++)
        {
            int startIndex = tokens[i * 2];
            uint metadataValue = unchecked((uint)tokens[i * 2 + 1]);
            var metadata = new LineTokenMetadata(metadataValue);
            startIndex = Math.Clamp(startIndex, 0, lineLength);
            encodedTokens[i] = new EncodedTokenizerToken(startIndex, metadata);
        }

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

    private sealed class NoOpBackgroundTokenizer : IBackgroundTokenizer
    {
        public void RequestTokens(int startLineIndex, int endLineIndexExclusive)
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
