namespace FluidX.Decorations;

public class ModelDecorationOptions
{
    public string Description { get; init; } = "";
    public string? BlockClassName { get; init; }
    public bool? BlockIsAfterEnd { get; init; }
    public bool? BlockDoesNotCollapse { get; init; }
    public (int Top, int Right, int Bottom, int Left)? BlockPadding { get; init; }
    public TrackedRangeStickiness Stickiness { get; init; } = TrackedRangeStickiness.AlwaysGrowsWhenTypingAtEdges;
    public int ZIndex { get; init; } = 0;
    public string? ClassName { get; init; }
    public bool? ShouldFillLineOnLineBreak { get; init; }
    public string[]? HoverMessage { get; init; } // MarkdownString[]
    public string[]? GlyphMarginHoverMessage { get; init; } // MarkdownString[]
    public bool IsWholeLine { get; init; }
    public int? LineHeight { get; init; }
    public string? FontFamily { get; init; }
    public string? FontSize { get; init; }
    public string? FontWeight { get; init; }
    public string? FontStyle { get; init; }
    public bool ShowIfCollapsed { get; init; } = false;
    public bool CollapseOnReplaceEdit { get; init; } = false;
    public ModelDecorationOverviewRulerOptions? OverviewRuler { get; init; }
    public ModelDecorationMinimapOptions? Minimap { get; init; }
    public ModelDecorationGlyphMarginOptions? GlyphMargin { get; init; }
    public string? GlyphMarginClassName { get; init; }
    public string? LinesDecorationsClassName { get; init; }
    public string? LineNumberClassName { get; init; }
    public string[]? LineNumberHoverMessage { get; init; } // MarkdownString[]
    public string? LinesDecorationsTooltip { get; init; }
    public string? FirstLineDecorationClassName { get; init; }
    public string? MarginClassName { get; init; }
    public string? InlineClassName { get; init; }
    public bool InlineClassNameAffectsLetterSpacing { get; init; } = false;
    public string? BeforeContentClassName { get; init; }
    public string? AfterContentClassName { get; init; }
    public ModelDecorationInjectedTextOptions? After { get; init; }
    public ModelDecorationInjectedTextOptions? Before { get; init; }
    public bool? HideInCommentTokens { get; init; } = false;
    public bool? HideInStringTokens { get; init; } = false;
    public bool AffectsFont => FontSize is not null || FontFamily is not null || FontWeight is not null || FontStyle is not null;
    public TextDirection? TextDirection { get; init; }
}

public enum TextDirection
{
    LTR = 0,
    RTL = 1
}