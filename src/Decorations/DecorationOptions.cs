using System;
using System.Collections.Generic;
using System.Text;
using TokenInfo = (int Length, int Metadata);

namespace FluidX.Decorations;

public record class DecorationOptions(string Color, string DarkColor);

public record class ModelDecorationOverviewRulerOptions(
    string Color,
    string DarkColor,
    OverviewRulerLane Position
) : DecorationOptions(Color, DarkColor)
{
    private string? _resolvedColor = null;

    // TODO: themes
}

public record class ModelDecorationGlyphMarginOptions(
    string Color,
    string DarkColor,
    bool? PersistLane = null,
    GlyphMarginLane Position = GlyphMarginLane.Center
) : DecorationOptions(Color, DarkColor);

public record class ModelDecorationMinimapOptions(
    string Color,
    string DarkColor,
    MinimapSectionHeaderStyle? SectionHeaderStyle = null,
    string? SectionHeaderText = null
) : DecorationOptions(Color, DarkColor)
{
    private string? _resolvedColor = null;

    // TODO: themes
}

public class ModelDecorationInjectedTextOptions
{
    public string Content { get; init; } = "";
    public TokenInfo[]? Tokens { get; init; } = null;
    public string? InlineClassName { get; init; } = null;
    public bool InlineClassNameAffectsLetterSpacing { get; init; } = false;
    public object? AttachedData { get; init; } = null;
    public InjectedTextCursorStops? CursorStops { get; init; } = null;
}

/**
* Vertical Lane in the overview ruler of the editor.
*/
public enum OverviewRulerLane
{
    Left = 1,
    Center = 2,
    Right = 4,
    Full = 7
}

/**
 * Vertical Lane in the glyph margin of the editor.
 */
public enum GlyphMarginLane
{
    Left = 1,
    Center = 2,
    Right = 3,
}

/**
 * Section header style.
 */
public enum MinimapSectionHeaderStyle
{
    Normal = 1,
    Underlined = 2
}

public enum InjectedTextCursorStops
{
    Both,
    Right,
    Left,
    None
}
