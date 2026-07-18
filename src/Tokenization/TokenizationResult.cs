using FluidX.Tokenization.TokenStores;

namespace FluidX.Tokenization;


public record struct TokenizerToken(int Offset, string Type, GlobalLanguageId Language)
{
    public override readonly string ToString() => $"({Offset}, {Type})";
}
public record struct TokenizationResult(TokenizerToken[] Tokens, ITokenizerState EndState);

public record struct EncodedTokenizerToken(int StartIndex, LineTokenMetadata Metadata);
public record struct EncodedTokenizationResult(EncodedTokenizerToken[] Tokens, ITokenizerState EndState);
