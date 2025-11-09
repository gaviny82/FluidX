namespace FluidX.Tokenization.TokenStores;

public record struct TokenInfo(int Length, LineTokenMetadata Metadata);

public class TokenArray
{
    private readonly TokenInfo[] _tokenInfo;

    public TokenArray(TokenInfo[] tokenInfo)
    {
        _tokenInfo = tokenInfo;
    }

    public TokenArray(SliceLineTokens lineTokens)
    {
        _tokenInfo = new TokenInfo[lineTokens.Count];
        for (int i = 0; i < lineTokens.Count; i++)
        {
            _tokenInfo[i] = new TokenInfo(
                lineTokens.GetEndOffset(i) - lineTokens.GetStartOffset(i),
                lineTokens.GetMetadata(i)
            );
        }
    }

    public LineTokens ToLineTokens(string lineContent, ILanguageIdCodec decoder)
    {
        LineToken[] lineTokens = new LineToken[_tokenInfo.Length];
        int offset = 0;
        for (int i = 0; i < _tokenInfo.Length; i++)
        {
            var token = _tokenInfo[i];
            offset += token.Length;
            lineTokens[i] = new LineToken
            {
                EndOffset = offset,
                Metadata = token.Metadata
            };
        }
        return new LineTokens(lineTokens, lineContent, decoder);
    }

    public TokenArray Slice(int fromIndex, int length)
    {
        TokenInfo[] tokenInfo = new TokenInfo[length];
        Array.Copy(_tokenInfo, fromIndex, tokenInfo, 0, length);
        return new TokenArray(tokenInfo);
    }

    public static TokenArray Concat(TokenArray array1, TokenArray array2)
    {
        TokenInfo[] tokenInfo = new TokenInfo[array1._tokenInfo.Length + array2._tokenInfo.Length];
        Array.Copy(array1._tokenInfo, 0, tokenInfo, 0, array1._tokenInfo.Length);
        Array.Copy(array2._tokenInfo, 0, tokenInfo, array1._tokenInfo.Length, array2._tokenInfo.Length);
        return new TokenArray(tokenInfo);
    }
}
