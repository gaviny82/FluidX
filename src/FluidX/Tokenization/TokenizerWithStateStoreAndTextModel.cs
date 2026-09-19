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

    public ITokenizerState? GetStartState(int lineIndex)
        => Store.GetStartState(lineIndex, _initialState);

    public (int lineIndex, ITokenizerState startState)? GetFirstInvalidLine()
        => Store.GetFirstInvalidLine(_initialState);

    public void UpdateTokensUntilLine(ContiguousMultilineTokensBuilder builder, int lineIndex)
    {
        var languageId = LanguageId;
        while (true)
        {
            if (GetFirstInvalidLine() is not (int invalidLineIndex, ITokenizerState startState)
                || invalidLineIndex > lineIndex)
                return;
            string text = TextModel.TextBuffer.GetLineContent(invalidLineIndex);
            var r = SafeTokenize(languageId, _tokenizationSupport, text, true, startState);
            // Convert EncodedTokenizerToken to LineToken (StartIndex => EndOffset)
            var lineTokens = new LineToken[r.Tokens.Length];
            for (int i = 0; i < r.Tokens.Length; i++)
            {
                var token = r.Tokens[i];
                int endOffset = (i + 1 < r.Tokens.Length) ? r.Tokens[i + 1].StartIndex : text.Length;
                lineTokens[i] = new LineToken(endOffset, token.Metadata);
            }
            builder.Add(invalidLineIndex, lineTokens);
            Store.SetEndState(invalidLineIndex, r.EndState);
        }
    }

    /** assumes state is up to date */
    public StandardTokenType GetTokenTypeIfInsertingCharacter(TextPosition position, string character)
    {
        var lineStartState = GetStartState(position.LineIndex);
        if (lineStartState is null)
            return StandardTokenType.Other;

        var languageId = LanguageId;
        string lineContent = TextModel.TextBuffer.GetLineContent(position.LineIndex);

        // Create the text as if `character` was inserted
        string text = $"{lineContent[0..(position.ColumnIndex)]}{character}{lineContent[(position.ColumnIndex)..]}";
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

        int tokenIndex = lineTokens.FindTokenIndexAtOffset(position.ColumnIndex);
        return lineTokens.GetMetadata(tokenIndex).TokenType;
    }

    /** assumes state is up to date */
    public List<LineTokens>? TokenizeLinesAt(int lineIndex, string[] lines)
    {
        var lineStartState = GetStartState(lineIndex);
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

    public bool HasAccurateTokensForLine(int lineIndex)
    {
        int firstInvalidLineIndex = Store.FirstInvalidEndStateLineIndexOrMax;
        return lineIndex < firstInvalidLineIndex;
    }

    public bool IsCheapToTokenize(int lineIndex)
    {
        int firstInvalidLineIndex = Store.FirstInvalidEndStateLineIndexOrMax;
        if (lineIndex < firstInvalidLineIndex)
            return true;
        if (lineIndex == firstInvalidLineIndex
            && TextModel.TextBuffer.GetLineLength(lineIndex) < CheapTokenizationLengthLimit)
            return true;
        return false;
    }

    /**
     * The result is not cached.
     */
    public bool TokenizeHeuristically(ContiguousMultilineTokensBuilder builder, int startLineIndex, int endLineIndex)
    {
        if (endLineIndex <= Store.FirstInvalidEndStateLineIndexOrMax)
            return false; // nothing to do

        if (startLineIndex <= Store.FirstInvalidEndStateLineIndexOrMax)
        {
            // tokenization has reached the viewport start...
            UpdateTokensUntilLine(builder, endLineIndex);
            return false;
        }

        var state = GuessStartState(startLineIndex);
        var languageId = LanguageId;

        for (int lineIndex = startLineIndex; lineIndex <= endLineIndex; lineIndex++)
        {
            string text = TextModel.TextBuffer.GetLineContent(lineIndex);
            var r = SafeTokenize(languageId, _tokenizationSupport, text, true, state);
            // TODO: Confirm the definitions of EncodedTokenizerToken and LineToken are compatible
            builder.Add(lineIndex, r.Tokens.Select(t => new LineToken(t.StartIndex, t.Metadata)).ToArray());
            state = r.EndState;
        }

        return true;
    }

    public ITokenizerState GuessStartState(int lineIndex)
    {
        var (likelyRelevantLines, initialState) = FindLikelyRelevantLines(TextModel, lineIndex, this);

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
        int lineIndex,
        TokenizerWithStateStoreAndTextModel? store)
    {
        int nonWhitespaceColumnIndex = model.TextBuffer.GetLineFirstNonWhitespaceColumnIndex(lineIndex);
        ITokenizerState? initialState = null;
        List<string> likelyRelevantLines = [];
        for (int i = lineIndex - 1; nonWhitespaceColumnIndex > 0 && i >= 0; i--)
        {
            int newNonWhitespaceIndex = model.TextBuffer.GetLineFirstNonWhitespaceColumnIndex(i);
            // Ignore lines full of whitespace
            if (newNonWhitespaceIndex == -1)
                continue;
            if (newNonWhitespaceIndex < nonWhitespaceColumnIndex)
            {
                likelyRelevantLines.Add(model.TextBuffer.GetLineContent(i));
                nonWhitespaceColumnIndex = newNonWhitespaceIndex;
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
