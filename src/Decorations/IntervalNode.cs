using Range = FluidX.TextBuffers.Range;

namespace FluidX.Decorations;

public class IntervalNode : IModelDecoration
{
    #region Metadata Flags

    private int _metadata = 0;

    public NodeColor NodeColor
    {
        get => (NodeColor)((_metadata & Constants.ColorMask) >> Constants.ColorOffset);
        set => _metadata = (_metadata & Constants.ColorMaskInverse) | ((int)value << Constants.ColorOffset);
    }

    public bool IsVisited
    {
        get => ((_metadata & Constants.IsVisitedMask) >> Constants.IsVisitedOffset) != 0;
        set => _metadata = (_metadata & Constants.IsVisitedMaskInverse) | ((value ? 1 : 0) << Constants.IsVisitedOffset);
    }

    public bool IsForValidation
    {
        get => ((_metadata & Constants.IsForValidationMask) >> Constants.IsForValidationOffset) != 0;
        set => _metadata = (_metadata & Constants.IsForValidationMaskInverse) | ((value ? 1 : 0) << Constants.IsForValidationOffset);
    }

    public bool IsInGlyphMargin
    {
        get => ((_metadata & Constants.IsMarginMask) >> Constants.IsMarginOffset) != 0;
        set => _metadata = (_metadata & Constants.IsMarginMaskInverse) | ((value ? 1 : 0) << Constants.IsMarginOffset);
    }

    public bool AffectsFont
    {
        get => ((_metadata & Constants.AffectsFontMask) >> Constants.AffectsFontOffset) != 0;
        set => _metadata = (_metadata & Constants.AffectsFontMaskInverse) | ((value ? 1 : 0) << Constants.AffectsFontOffset);
    }

    public TrackedRangeStickiness Stickiness
    {
        get => (TrackedRangeStickiness)((_metadata & Constants.StickinessMask) >> Constants.StickinessOffset);
        set => _metadata = (_metadata & Constants.StickinessMaskInverse) | ((int)value << Constants.StickinessOffset);
    }

    public bool CollapseOnReplaceEdit
    {
        get => ((_metadata & Constants.CollapseOnReplaceEditMask) >> Constants.CollapseOnReplaceEditOffset) != 0;
        set => _metadata = (_metadata & Constants.CollapseOnReplaceEditMaskInverse) | ((value ? 1 : 0) << Constants.CollapseOnReplaceEditOffset);
    }

    #endregion

    public IntervalNode Parent { get; set; }
    public IntervalNode Left { get; set; }
    public IntervalNode Right { get; set; }

    public int Start { get; set; }
    public int End { get; set; }
    public int Delta { get; set; }
    public int MaxEnd { get; set; }

    public string Id { get; set; }
    public int OwnerId { get; set; }
    public ModelDecorationOptions Options { get; private set; }

    public long CachedVersionId { get; set; }
    public int CachedAbsoluteStart { get; set; }
    public int CachedAbsoluteEnd { get; set; }
    public Range? Range { get; set; }
    Range IModelDecoration.Range => (Range)Range!; // Range must not be null when accessed via IModelDecoration

    public IntervalNode(string id, int start, int end)
    {
        Parent = this;
        Left = this;
        Right = this;
        NodeColor = NodeColor.Red;

        Start = start;
        End = end;
        Delta = 0;
        MaxEnd = end;

        Id = id;
        OwnerId = 0;
        Options = null!;
        IsForValidation = false;
        IsInGlyphMargin = false;
        Stickiness = TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges;
        CollapseOnReplaceEdit = false;
        AffectsFont = false;

        CachedVersionId = 0;
        CachedAbsoluteStart = start;
        CachedAbsoluteEnd = end;
        Range = null;

        IsVisited = false;
    }

    public void Reset(int versionId, int start, int end, Range range)
    {
        Start = start;
        End = end;
        MaxEnd = end;
        CachedVersionId = versionId;
        CachedAbsoluteStart = start;
        CachedAbsoluteEnd = end;
        Range = range;
    }

    public void SetOptions(ModelDecorationOptions options)
    {
        Options = options;
        string? className = Options.ClassName;
        IsForValidation = className == ClassNames.EditorErrorDecoration
            || className == ClassNames.EditorWarningDecoration
            || className == ClassNames.EditorInfoDecoration;
        IsInGlyphMargin = Options.GlyphMarginClassName is not null;
        Stickiness = Options.Stickiness;
        CollapseOnReplaceEdit = options.CollapseOnReplaceEdit;
        AffectsFont = options.AffectsFont;
    }

    public void SetCachedOffsets(int absoluteStart, int absoluteEnd, long cachedVersionId)
    {
        if (CachedVersionId != cachedVersionId)
            Range = null;

        CachedVersionId = cachedVersionId;
        CachedAbsoluteStart = absoluteStart;
        CachedAbsoluteEnd = absoluteEnd;
    }

    public void Detach()
    {
        Parent = null!;
        Left = null!;
        Right = null!;
    }

    internal static readonly IntervalNode Sentinel = new IntervalNode(string.Empty, 0, 0)
    {
        NodeColor = NodeColor.Black,
        Parent = null!,
        Left = null!,
        Right = null!,
    };

    internal static void ResetSentinel()
    {
        Sentinel.Parent = Sentinel;
        Sentinel.Delta = 0;
        Sentinel.Start = 0;
        Sentinel.End = 0;
    }
}
