using FluidX.Tokenization.TokenStores;

namespace FluidX.TokenizationTextMateDemo;

internal enum TokenCategory
{
    Other = 0,
    Comment = 1,
    StringLike = 2,
    Number = 3,
    Keyword = 4,
    FunctionName = 5,
    ClassName = 6,
    TypeName = 7,
    NamespaceName = 8,
    PropertyName = 9,
    ParameterName = 10,
    FieldName = 11,
    Operator = 12,
    Preprocessor = 13,
}

internal readonly record struct TokenStyle(ConsoleColor Color, bool Italic, bool Bold);

internal static class TokenCategoryCodec
{
    private const int CategoryColorBase = 100;

    public static ColorId EncodeToForeground(TokenCategory category)
        => (ColorId)(CategoryColorBase + (int)category);

    public static TokenCategory DecodeFromForeground(ColorId foreground)
    {
        int raw = (int)foreground;
        int category = raw - CategoryColorBase;
        if (category < 0 || category > (int)TokenCategory.Preprocessor)
            return TokenCategory.Other;
        return (TokenCategory)category;
    }

    public static TokenStyle GetStyle(TokenCategory category) => category switch
    {
        TokenCategory.Comment => new(ConsoleColor.DarkGreen, Italic: true, Bold: false),
        TokenCategory.StringLike => new(ConsoleColor.DarkYellow, Italic: false, Bold: false),
        TokenCategory.Number => new(ConsoleColor.Cyan, Italic: false, Bold: false),
        TokenCategory.Keyword => new(ConsoleColor.Blue, Italic: false, Bold: true),
        TokenCategory.FunctionName => new(ConsoleColor.Magenta, Italic: false, Bold: true),
        TokenCategory.ClassName => new(ConsoleColor.Yellow, Italic: false, Bold: true),
        TokenCategory.TypeName => new(ConsoleColor.DarkCyan, Italic: false, Bold: true),
        TokenCategory.NamespaceName => new(ConsoleColor.DarkBlue, Italic: false, Bold: false),
        TokenCategory.PropertyName => new(ConsoleColor.DarkMagenta, Italic: false, Bold: false),
        TokenCategory.ParameterName => new(ConsoleColor.Gray, Italic: true, Bold: false),
        TokenCategory.FieldName => new(ConsoleColor.DarkGray, Italic: false, Bold: false),
        TokenCategory.Operator => new(ConsoleColor.White, Italic: false, Bold: true),
        TokenCategory.Preprocessor => new(ConsoleColor.Red, Italic: false, Bold: true),
        _ => new(ConsoleColor.Gray, Italic: false, Bold: false),
    };
}
