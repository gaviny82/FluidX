namespace FluidX.Tokenization;

public interface ITokenizerState : IEquatable<ITokenizerState>
{
    ITokenizerState Clone();
}
