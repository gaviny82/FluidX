using System.Runtime.InteropServices;

namespace FluidX.Tokenization.TokenStores;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct LineTokenMetadata(uint metadata)
{
    private readonly uint _metadata = metadata;

    public uint RawValue => _metadata;

    public LanguageId LanguageId
        => (LanguageId)((_metadata & MetadataConsts.LANGUAGEID_MASK) >> MetadataConsts.LANGUAGEID_OFFSET);

    public StandardTokenType TokenType
        => (StandardTokenType)((_metadata & MetadataConsts.TOKEN_TYPE_MASK) >> MetadataConsts.TOKEN_TYPE_OFFSET);

    public bool ContainsBalancedBrackets
        => ((_metadata & MetadataConsts.BALANCED_BRACKETS_MASK) >> MetadataConsts.BALANCED_BRACKETS_OFFSET) != 0;

    public FontStyle FontStyle
        => (FontStyle)((_metadata & MetadataConsts.FONT_STYLE_MASK) >> MetadataConsts.FONT_STYLE_OFFSET);

    public ColorId Foreground
        => (ColorId)((_metadata & MetadataConsts.FOREGROUND_MASK) >> MetadataConsts.FOREGROUND_OFFSET);

    public ColorId Background
        => (ColorId)((_metadata & MetadataConsts.BACKGROUND_MASK) >> MetadataConsts.BACKGROUND_OFFSET);

    public string GetClassName()
    {
        string className = $"mtk{Foreground}";
        FontStyle fontStyle = FontStyle;
        if (fontStyle.HasFlag(FontStyle.Italic))
            className += " mtki";
        if (fontStyle.HasFlag(FontStyle.Bold))
            className += " mtkb";
        if (fontStyle.HasFlag(FontStyle.Underline))
            className += " mtku";
        if (fontStyle.HasFlag(FontStyle.Strikethrough))
            className += " mtks";
        return className;
    }

    public string GetInlineStyle(string[] colorMap)
    {
        var foreground = Foreground;
        var fontStyle = FontStyle;

        string result = $"color: {colorMap[(int)foreground]};";
        if (fontStyle.HasFlag(FontStyle.Italic))
        {
            result += "font-style: italic;";
        }
        if (fontStyle.HasFlag(FontStyle.Bold))
        {
            result += "font-weight: bold;'";
        }
        string textDecoration = "";
        if (fontStyle.HasFlag(FontStyle.Underline))
        {
            textDecoration += " underline";
        }
        if (fontStyle.HasFlag(FontStyle.Strikethrough))
        {
            textDecoration += " line-through";
        }
        if (!string.IsNullOrEmpty(textDecoration))
        {
            result += $"text-decoration:{textDecoration};";
        }
        return result;
    }

    public TokenPresentation GetPresentation()
    {
        var foreground = Foreground;
        var fontStyle = FontStyle;
        return new TokenPresentation(
            foreground,
            fontStyle.HasFlag(FontStyle.Italic),
            fontStyle.HasFlag(FontStyle.Bold),
            fontStyle.HasFlag(FontStyle.Underline),
            fontStyle.HasFlag(FontStyle.Strikethrough)
        );
    }
}

public record struct TokenPresentation(ColorId Foreground, bool Italic, bool Bold, bool Underline, bool Strikethrough);

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct LineToken(int EndOffset, LineTokenMetadata Metadata);

/**
 * Helpers to manage the "collapsed" metadata of an entire StackElement stack.
 * The following assumptions have been made:
 *  - languageId < 256 => needs 8 bits
 *  - unique color count < 512 => needs 9 bits
 *
 * The binary format is:
 * - -------------------------------------------
 *     3322 2222 2222 1111 1111 1100 0000 0000
 *     1098 7654 3210 9876 5432 1098 7654 3210
 * - -------------------------------------------
 *     xxxx xxxx xxxx xxxx xxxx xxxx xxxx xxxx
 *     bbbb bbbb ffff ffff fFFF FBTT LLLL LLLL
 * - -------------------------------------------
 *  - L = LanguageId (8 bits)
 *  - T = StandardTokenType (2 bits)
 *  - B = Balanced bracket (1 bit)
 *  - F = FontStyle (4 bits)
 *  - f = foreground color (9 bits)
 *  - b = background color (8 bits)
 *
 */
public static class MetadataConsts
{
    public const uint LANGUAGEID_MASK /*            */ = 0b00000000_00000000_00000000_11111111;
    public const uint TOKEN_TYPE_MASK /*            */ = 0b00000000_00000000_00000011_00000000;
    public const uint BALANCED_BRACKETS_MASK /*     */ = 0b00000000_00000000_00000100_00000000;
    public const uint FONT_STYLE_MASK /*            */ = 0b00000000_00000000_01111000_00000000;
    public const uint FOREGROUND_MASK /*            */ = 0b00000000_11111111_10000000_00000000;
    public const uint BACKGROUND_MASK /*            */ = 0b11111111_00000000_00000000_00000000;

    public const uint ITALIC_MASK /*                */ = 0b00000000_00000000_00001000_00000000;
    public const uint BOLD_MASK /*                  */ = 0b00000000_00000000_00010000_00000000;
    public const uint UNDERLINE_MASK /*             */ = 0b00000000_00000000_00100000_00000000;
    public const uint STRIKETHROUGH_MASK /*         */ = 0b00000000_00000000_01000000_00000000;

    // Semantic tokens cannot set the language id, so we can
    // use the first 8 bits for control purposes
    public const uint SEMANTIC_USE_ITALIC /*        */ = 0b00000000_00000000_00000000_00000001;
    public const uint SEMANTIC_USE_BOLD /*          */ = 0b00000000_00000000_00000000_00000010;
    public const uint SEMANTIC_USE_UNDERLINE  /*    */ = 0b00000000_00000000_00000000_00000100;
    public const uint SEMANTIC_USE_STRIKETHROUGH /* */ = 0b00000000_00000000_00000000_00001000;
    public const uint SEMANTIC_USE_FOREGROUND /*    */ = 0b00000000_00000000_00000000_00010000;
    public const uint SEMANTIC_USE_BACKGROUND /*    */ = 0b00000000_00000000_00000000_00100000;

    public const int LANGUAGEID_OFFSET = 0;
    public const int TOKEN_TYPE_OFFSET = 8;
    public const int BALANCED_BRACKETS_OFFSET = 10;
    public const int FONT_STYLE_OFFSET = 11;
    public const int FOREGROUND_OFFSET = 15;
    public const int BACKGROUND_OFFSET = 24;
}

/**
 * Open ended enum at runtime
 */
public enum LanguageId
{
    Null = 0,
    PlainText = 1
}

/**
 * A font style. Values are 2^x such that a bit mask can be used.
 */
public enum FontStyle
{
    NotSet = -1,
    None = 0,
    Italic = 1,
    Bold = 2,
    Underline = 4,
    Strikethrough = 8,
}

/**
 * Open ended enum at runtime
 */
public enum ColorId
{
    None = 0,
    DefaultForeground = 1,
    DefaultBackground = 2
}

/**
 * A standard token type.
 */
public enum StandardTokenType
{
    Other = 0,
    Comment = 1,
    String = 2,
    RegEx = 3
}

public interface ILanguageIdCodec
{
    LanguageId EncodeLanguageId(string languageId);
    string DecodeLanguageId(LanguageId languageId);
}
