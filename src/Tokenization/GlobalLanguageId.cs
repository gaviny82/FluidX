namespace FluidX.Tokenization;

/// <summary>
/// Globally unique language identifier managed by <see cref="LanguageRegistry"/>.
/// This identifier is stable across text models and is not constrained by token metadata bit packing.
/// </summary>
public readonly record struct GlobalLanguageId(uint Value)
{
    public static readonly GlobalLanguageId Null = new(0);
    public static readonly GlobalLanguageId PlainText = new(1);

    public static implicit operator uint(GlobalLanguageId languageId) => languageId.Value;
    public static explicit operator GlobalLanguageId(uint value) => new(value);

    public override string ToString() => Value.ToString();
}
