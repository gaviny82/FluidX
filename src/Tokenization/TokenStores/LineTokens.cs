using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace FluidX.Tokenization.TokenStores;

public class LineTokens : IEnumerable<LineToken>
{
    private readonly LineToken[] _tokens;
    private readonly string _text;
    public ILanguageIdCodec LanguageIdCodec { get; init; }

    public string LineContent => _text;
    public int TextLength => _text.Length;
    public int Count => _tokens.Length;

    public LineTokens(LineToken[] tokens, string text, ILanguageIdCodec decoder)
    {
        _tokens = tokens;
        _text = text;
        LanguageIdCodec = decoder;
    }

    public readonly static LineTokenMetadata DefaultTokenMetadata = new()
    {
        FontStyle = FontStyle.None,
        Foreground = ColorId.DefaultForeground,
        Background = ColorId.DefaultBackground,
    };

    public static LineTokens CreateEmpty(string lineContent, ILanguageIdCodec decoder)
    {
        LineToken[] tokens = [new LineToken(lineContent.Length, DefaultTokenMetadata)];
        return new(tokens, lineContent, decoder);
    }

    public static LineTokens CreateFromTextAndMetadata((string text, LineTokenMetadata metadata)[] data, ILanguageIdCodec decoder)
    {
        int offset = 0;
        string fullText = "";
        List<LineToken> tokens = [];
        foreach(var (text, metadata) in data)
        {
            tokens.Add(new(offset + text.Length, metadata));
            offset += text.Length;
            fullText += text;
        }
        return new(tokens.ToArray(), fullText, decoder);
    }

    #region Equality

    public override bool Equals(object? obj)
    {
        if (obj is not LineTokens other)
            return false;
        return other == this;
    }

    public static bool operator ==(LineTokens left, LineTokens right)
    {
        if (left._text !=  right._text)
            return false;
        return left._tokens.SequenceEqual(right._tokens);
    }

    public static bool operator !=(LineTokens left, LineTokens right)
    {
        return !(left == right);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_text, _tokens);
    }

    public bool SlicedEquals(LineTokens other, int sliceFromTokenIndex, int sliceTokenCount)
    {
        if (_text != other._text)
            return false;
        var thisSlice = _tokens.AsSpan().Slice(sliceFromTokenIndex, sliceTokenCount);
        var otherSlice = other._tokens.AsSpan().Slice(sliceFromTokenIndex, sliceTokenCount);
        return thisSlice.SequenceEqual(otherSlice);
    }

    #endregion

    public int GetStartOffset(int tokenIndex)
    {
        if (tokenIndex <= 0)
            return 0;
        return _tokens[tokenIndex - 1].EndOffset;
    }

    public int GetEndOffset(int tokenIndex)
    {
        return _tokens[tokenIndex].EndOffset;
    }

    public LineTokenMetadata GetMetadata(int tokenIndex)
    {
        return _tokens[tokenIndex].Metadata;
    }

    public string GetLanguageId(int tokenIndex)
    {
        LanguageId langId = _tokens[tokenIndex].Metadata.LanguageId;
        return LanguageIdCodec.DecodeLanguageId(langId);
    }

    /// <summary>
    /// Find the token containing offset `offset`.
    /// </summary>
    /// <param name="offset">The search offset</param>
    /// <returns>The index of the token containing the offset.</returns>
    public int FindTokenIndexAtOffset(int offset)
        => FindIndexInTokensArray(_tokens, offset);

    public static int FindIndexInTokensArray(LineToken[] tokens, int offset)
    {
        // Binary search
        int low = 0;
        int high = tokens.Length - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            int midEndOffset = tokens[mid].EndOffset;
            if (offset < midEndOffset)
                high = mid - 1;
            else
                low = mid + 1;
        }
        return low;
    }

    public ReadOnlySpan<char> GetTokenText(int tokenIndex)
    {
        int startOffset = GetStartOffset(tokenIndex);
        int endOffset = GetEndOffset(tokenIndex);
        return _text.AsSpan(startOffset, endOffset - startOffset);
    }

    public SliceLineTokens Slice(int startOffset, int endOffset, int deltaOffset)
        => new(this, startOffset, endOffset, deltaOffset);

    public TokenArray GetTokensInRange(Range range)
        => new(Slice(0, Count, 0));

    /// <summary>
    /// </summary>
    /// <param name="insertTokens">Must be sorted by offset.</param>
    /// <returns></returns>
    public LineTokens WithInserted((int offset, string text, LineTokenMetadata metadata)[] insertTokens)
    {
        if (insertTokens.Length == 0)
            return this;

        int nextOriginalTokenIdx = 0;
        int nextInsertTokenIdx = 0;
        string text = "";
        List<LineToken> newTokens = [];

        int originalEndOffset = 0;
        while (true)
        {
            int nextOriginalTokenEndOffset = nextOriginalTokenIdx < _tokens.Length ? _tokens[nextOriginalTokenIdx].EndOffset : -1;
            (int offset, string text, LineTokenMetadata metadata)? nextInsertToken = nextInsertTokenIdx < insertTokens.Length ? insertTokens[nextInsertTokenIdx] : null;

            if (nextOriginalTokenEndOffset != -1 && (nextInsertToken == null || nextOriginalTokenEndOffset <= nextInsertToken?.offset))
            {
                // original token ends before next insert token
                text += _text.Substring(originalEndOffset, nextOriginalTokenEndOffset - originalEndOffset);
                var metadata = _tokens[nextOriginalTokenIdx].Metadata;
                newTokens.Add(new(text.Length, metadata));
                nextOriginalTokenIdx++;
                originalEndOffset = nextOriginalTokenEndOffset;
            }
            else if (nextInsertToken is not null)
            {
                if (nextInsertToken?.offset > originalEndOffset)
                {
                    // insert token is in the middle of the next token.
                    text += _text.Substring(originalEndOffset, (int)nextInsertToken?.offset! - originalEndOffset);
                    var metadata = _tokens[nextOriginalTokenIdx].Metadata;
                    newTokens.Add(new (text.Length, metadata));
                    originalEndOffset = (int)nextInsertToken?.offset!;
                }

                text += nextInsertToken?.text;
                newTokens.Add(new (text.Length, (LineTokenMetadata)nextInsertToken?.metadata!));
                nextInsertTokenIdx++;
            }
            else
            {
                break;
            }
        }

        return new LineTokens(newTokens.ToArray(), text, LanguageIdCodec);
    }

    #region IEnumerable Members

    public IEnumerator<LineToken> GetEnumerator()
    {
        return ((IEnumerable<LineToken>)_tokens).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _tokens.GetEnumerator();
    }

    #endregion
}

public readonly struct SliceLineTokens
{
    private readonly LineTokens _source;
    private readonly int _startOffset;
    private readonly int _endOffset;
    private readonly int _deltaOffset;

    private readonly int _firstTokenIndex;
    private readonly int _tokensCount;

    public ILanguageIdCodec LanguageIdCodec => _source.LanguageIdCodec;

    public SliceLineTokens(LineTokens source, int startOffset, int endOffset, int deltaOffset)
    {
        _source = source;
        _startOffset = startOffset;
        _endOffset = endOffset;
        _deltaOffset = deltaOffset;

        _firstTokenIndex = source.FindTokenIndexAtOffset(startOffset);
        _tokensCount = 0;
        for (int i = 0; i < source.Count; i++)
        {
            int tokenStartOffset = source.GetStartOffset(i);
            if (tokenStartOffset >= endOffset)
                break;
            _tokensCount++;
        }
    }

    public LineTokenMetadata GetMetadata(int tokenIndex)
        => _source.GetMetadata(_firstTokenIndex + tokenIndex);

    public ReadOnlySpan<char> LineContent
        => _source.LineContent.AsSpan()[_startOffset.._endOffset];

    public int Count => _tokensCount;

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (!(obj is SliceLineTokens other))
            return false;
        return other.Equals(this);
    }

    public bool Equals(SliceLineTokens other)
    {
        return _startOffset == other._startOffset
            && _endOffset == other._endOffset
            && _deltaOffset == other._deltaOffset
            && _source.SlicedEquals(other._source, _firstTokenIndex, _tokensCount);
    }

    public int GetStartOffset(int tokenIndex)
    {
        int tokenStartOffset = _source.GetStartOffset(_firstTokenIndex + tokenIndex);
        return Math.Max(tokenStartOffset, _startOffset) - _startOffset + _deltaOffset;
    }

    public int GetEndOffset(int tokenIndex)
    {
        int tokenEndOffset = _source.GetEndOffset(_firstTokenIndex + tokenIndex);
        return Math.Min(tokenEndOffset, _endOffset) - _startOffset + _deltaOffset;
    }

    public int FindTokenIndexAtOffset(int offset)
        => _source.FindTokenIndexAtOffset(offset + _startOffset - _deltaOffset) - _firstTokenIndex;

    public ReadOnlySpan<char> GetTokenText(int tokenIndex)
    {
        int adjustedTokenIndex = _firstTokenIndex + tokenIndex;
        int tokenStartOffset = _source.GetStartOffset(adjustedTokenIndex);
        int tokenEndOffset = _source.GetEndOffset(adjustedTokenIndex);
        var text = _source.GetTokenText(adjustedTokenIndex);
        if (tokenStartOffset < _startOffset)
            text = text.Slice(_startOffset - tokenStartOffset);
        if(tokenEndOffset > _endOffset)
            text = text.Slice(0, text.Length - (tokenEndOffset - _endOffset));
        return text;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_source, _startOffset, _endOffset, _deltaOffset);
    }

    public static bool operator ==(SliceLineTokens left, SliceLineTokens right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SliceLineTokens left, SliceLineTokens right)
    {
        return !(left == right);
    }
}