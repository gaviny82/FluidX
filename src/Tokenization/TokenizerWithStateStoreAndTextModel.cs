using FluidX.TextBuffers;
using FluidX.TextModels;
using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;

public class TokenizerWithStateStoreAndTextModel
{
    private readonly ITokenizerState _initialState;
    private readonly ITokenizationSupport _tokenizationSupport;

    public TrackingTokenizationStateStore Store { get; }
    public TextModel TextModel { get; } // TODO: use ITextModel
    public GlobalLanguageId LanguageId => TextModel.LanguageId;
    public ModelLanguageIdMapper LanguageIdMapper { get; }

    private const int CheapTokenizationLengthLimit = 2048;

    public EventHandler<Exception>? TokenizationErrorOccurred;

    public TokenizerWithStateStoreAndTextModel(
        int lineCount,
        ITokenizationSupport tokenizationSupport,
        TextModel textModel,
        ModelLanguageIdMapper languageIdMapper)
    {
        _tokenizationSupport = tokenizationSupport;
        _initialState = tokenizationSupport.GetInitialState();
        Store = new TrackingTokenizationStateStore(lineCount);
        TextModel = textModel;
        LanguageIdMapper = languageIdMapper;
    }

    public ITokenizerState? GetStartState(int lineNumber)
        => Store.GetStartState(lineNumber, _initialState);

    public (int lineNumber, ITokenizerState startState)? GetFirstInvalidLine()
        => Store.GetFirstInvalidLine(_initialState);

    public void UpdateTokensUntilLine(ContiguousMultilineTokensBuilder builder, int lineNumber)
    {
        var languageId = LanguageId;
        while (true)
        {
            if (GetFirstInvalidLine() is not (int invalidLineNumber, ITokenizerState startState)
                || invalidLineNumber > lineNumber)
                return;
            string text = TextModel.TextBuffer.GetLineContent(invalidLineNumber);
            var r = SafeTokenize(languageId, _tokenizationSupport, text, true, startState);
            // Convert EncodedTokenizerToken to LineToken (StartIndex => EndOffset)
            var lineTokens = new LineToken[r.Tokens.Length];
            for (int i = 0; i < r.Tokens.Length; i++)
            {
                var token = r.Tokens[i];
                int endOffset = (i + 1 < r.Tokens.Length) ? r.Tokens[i + 1].StartIndex : text.Length;
                lineTokens[i] = new LineToken(endOffset, token.Metadata);
            }
            builder.Add(invalidLineNumber, lineTokens);
            Store.SetEndState(invalidLineNumber, r.EndState);
        }
    }

    /** assumes state is up to date */
    public StandardTokenType GetTokenTypeIfInsertingCharacter(TextPosition position, string character)
    {
        var lineStartState = GetStartState(position.LineNumber);
        if (lineStartState is null)
            return StandardTokenType.Other;

        var languageId = LanguageId;
        string lineContent = TextModel.TextBuffer.GetLineContent(position.LineNumber);

        // Create the text as if `character` was inserted
        string text = $"{lineContent[0..(position.Column - 1)]}{character}{lineContent[(position.Column - 1)..]}";
            var r = SafeTokenize(
                languageId,
                _tokenizationSupport,
                text,
                true,
                lineStartState
            );
        // TODO: Confirm the definitions of EncodedTokenizerToken and LineToken are compatible
        var lineTokens = new LineTokens(r.Tokens.Select(t => new LineToken(t.StartIndex, t.Metadata)).ToArray(), text);
        if (lineTokens.Count == 0)
            return StandardTokenType.Other;

        int tokenIndex = lineTokens.FindTokenIndexAtOffset(position.Column - 1);
        return lineTokens.GetMetadata(tokenIndex).TokenType;
    }

    /** assumes state is up to date */
    public List<LineTokens>? TokenizeLinesAt(int lineNumber, string[] lines)
    {
        var lineStartState = GetStartState(lineNumber);
        if (lineStartState is null)
            return null;

        var languageId = LanguageId;
        List<LineTokens> result = [];

        var state = lineStartState;
        foreach (var line in lines)
        {
            var r = SafeTokenize(
                languageId,
                _tokenizationSupport,
                line,
                true,
                state
            );
            result.Add(new LineTokens(r.Tokens.Select(t => new LineToken(t.StartIndex, t.Metadata)).ToArray(), line));
            state = r.EndState;
        }

        return result;
    }

    public bool HasAccurateTokensForLine(int lineNumber)
    {
        int firstInvalidLineNumber = Store.FirstInvalidEndStateLineNumberOrMax;
        return lineNumber < firstInvalidLineNumber;
    }

    public bool IsCheapToTokenize(int lineNumber)
    {
        int firstInvalidLineNumber = Store.FirstInvalidEndStateLineNumberOrMax;
        if (lineNumber < firstInvalidLineNumber)
            return true;
        if (lineNumber == firstInvalidLineNumber
            && TextModel.TextBuffer.GetLineLength(lineNumber) < CheapTokenizationLengthLimit)
            return true;
        return false;
    }

    /**
     * The result is not cached.
     */
    public bool TokenizeHeuristically(ContiguousMultilineTokensBuilder builder, int startLineNumber, int endLineNumber)
    {
        if (endLineNumber <= Store.FirstInvalidEndStateLineNumberOrMax)
            return false; // nothing to do

        if (startLineNumber <= Store.FirstInvalidEndStateLineNumberOrMax)
        {
            // tokenization has reached the viewport start...
            UpdateTokensUntilLine(builder, endLineNumber);
            return false;
        }

        var state = GuessStartState(startLineNumber);
        var languageId = LanguageId;

        for (int lineNumber = startLineNumber; lineNumber <= endLineNumber; lineNumber++)
        {
            string text = TextModel.TextBuffer.GetLineContent(lineNumber);
            var r = SafeTokenize(languageId, _tokenizationSupport, text, true, state);
            // TODO: Confirm the definitions of EncodedTokenizerToken and LineToken are compatible
            builder.Add(lineNumber, r.Tokens.Select(t => new LineToken(t.StartIndex, t.Metadata)).ToArray());
            state = r.EndState;
        }

        return true;
    }

    public ITokenizerState GuessStartState(int lineNumber)
    {
        var (likelyRelevantLines, initialState) = FindLikelyRelevantLines(TextModel, lineNumber, this);

        if (initialState is null)
            initialState = _tokenizationSupport.GetInitialState();

        var languageId = LanguageId;
        var state = initialState;
        foreach (var line in likelyRelevantLines)
        {
            var r = SafeTokenize(languageId, _tokenizationSupport, line, false, state);
            state = r.EndState;
        }
        return state;
    }

    private static (List<string> likelyRelevantLines, ITokenizerState? initialState) FindLikelyRelevantLines(
        TextModel model,
        int lineNumber,
        TokenizerWithStateStoreAndTextModel? store)
    {
        int nonWhitespaceColumn = model.TextBuffer.GetLineFirstNonWhitespaceColumn(lineNumber);
        ITokenizerState? initialState = null;
        List<string> likelyRelevantLines = [];
        for (int i = lineNumber - 1; nonWhitespaceColumn > 1 && i >= 1; i--)
        {
            int newNonWhitespaceIndex = model.TextBuffer.GetLineFirstNonWhitespaceColumn(i);
            // Ignore lines full of whitespace
            if (newNonWhitespaceIndex == 0)
                continue;
            if (newNonWhitespaceIndex < nonWhitespaceColumn)
            {
                likelyRelevantLines.Add(model.TextBuffer.GetLineContent(i));
                nonWhitespaceColumn = newNonWhitespaceIndex;
                initialState = store?.GetStartState(i);
                if (initialState is not null)
                    break;
            }
        }
        likelyRelevantLines.Reverse();
        return (likelyRelevantLines, initialState);
    }

    private EncodedTokenizationResult SafeTokenize(
        GlobalLanguageId languageId,
        ITokenizationSupport? tokenizationSupport,
        string text,
        bool hasEOL,
        ITokenizerState state)
    {
        EncodedTokenizationResult? r = null;

        if (tokenizationSupport is not null)
        {
            try
            {
                r = tokenizationSupport.TokenizeEncoded(text, hasEOL, state.Clone());
            }
            catch (Exception e)
            {
                TokenizationErrorOccurred?.Invoke(this, e);
            }
        }

        return r ?? NullState.NullTokenizeEncoded(LanguageIdMapper.Encode(languageId), state);
    }
}

public struct NullState : ITokenizerState
{
    public ITokenizerState Clone() => this;

    public bool Equals(ITokenizerState? other) => other is NullState;

    public static TokenizationResult NullTokenize(GlobalLanguageId languageId, ITokenizerState state)
        => new([new TokenizerToken(0, "", new(languageId.Value))], state);

    public static EncodedTokenizationResult NullTokenizeEncoded(LanguageId languageId, ITokenizerState? state)
    {
        var metadata = new LineTokenMetadata
        {
            LanguageId = languageId,
            TokenType = StandardTokenType.Other,
            FontStyle = FontStyle.None,
            Foreground = ColorId.DefaultForeground,
            Background = ColorId.DefaultBackground
        };
        return new EncodedTokenizationResult([new EncodedTokenizerToken(0, metadata)], state ?? new NullState());
    }
}
