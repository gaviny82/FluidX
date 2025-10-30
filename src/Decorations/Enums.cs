namespace FluidX.Decorations;

// The red-black tree is based on the "Introduction to Algorithms" by Cormen, Leiserson and Rivest.
internal enum NodeColor
{
    Black = 0,
    Red = 1
}

/**
 * Describes the behavior of decorations when typing/editing near their edges.
 * Note: Please do not edit the values, as they very carefully match `DecorationRangeBehavior`
 */
public enum TrackedRangeStickiness
{
    AlwaysGrowsWhenTypingAtEdges = 0,
    NeverGrowsWhenTypingAtEdges = 1,
    GrowsOnlyWhenTypingBefore = 2,
    GrowsOnlyWhenTypingAfter = 3,
}

internal static class ClassNames
{
    public const string EditorHintDecoration = "squiggly-hint";
    public const string EditorInfoDecoration = "squiggly-info";
    public const string EditorWarningDecoration = "squiggly-warning";
    public const string EditorErrorDecoration = "squiggly-error";
    public const string EditorUnnecessaryDecoration = "squiggly-unnecessary";
    public const string EditorUnnecessaryInlineDecoration = "squiggly-inline-unnecessary";
    public const string EditorDeprecatedInlineDecoration = "squiggly-inline-deprecated";
}

internal static class Constants
{
    public const byte ColorMask = 0b00000001;
    public const byte ColorMaskInverse = 0b11111110;
    public const byte ColorOffset = 0;

    public const byte IsVisitedMask = 0b00000010;
    public const byte IsVisitedMaskInverse = 0b11111101;
    public const byte IsVisitedOffset = 1;

    public const byte IsForValidationMask = 0b00000100;
    public const byte IsForValidationMaskInverse = 0b11111011;
    public const byte IsForValidationOffset = 2;

    public const byte StickinessMask = 0b00011000;
    public const byte StickinessMaskInverse = 0b11100111;
    public const byte StickinessOffset = 3;

    public const byte CollapseOnReplaceEditMask = 0b00100000;
    public const byte CollapseOnReplaceEditMaskInverse = 0b11011111;
    public const byte CollapseOnReplaceEditOffset = 5;

    public const byte IsMarginMask = 0b01000000;
    public const byte IsMarginMaskInverse = 0b10111111;
    public const byte IsMarginOffset = 6;

    public const byte AffectsFontMask = 0b10000000;
    public const byte AffectsFontMaskInverse = 0b01111111;
    public const byte AffectsFontOffset = 7;

    /**
     * Due to how deletion works (in order to avoid always walking the right subtree of the deleted node),
     * the deltas for nodes can grow and shrink dramatically. It has been observed, in practice, that unless
     * the deltas are corrected, integer overflow will occur.
     *
     * The integer overflow occurs when 53 bits are used in the numbers, but we will try to avoid it as
     * a node's delta gets below a negative 30 bits number.
     *
     * MIN SMI (SMall Integer) as defined in v8.
     * one bit is lost for boxing/unboxing flag.
     * one bit is lost for sign flag.
     * See https://thibaultlaurens.github.io/javascript/2013/04/29/how-the-v8-engine-works/#tagged-values
     */
    public const int MIN_SAFE_DELTA = -(1 << 30);
    /**
     * MAX SMI (SMall Integer) as defined in v8.
     * one bit is lost for boxing/unboxing flag.
     * one bit is lost for sign flag.
     * See https://thibaultlaurens.github.io/javascript/2013/04/29/how-the-v8-engine-works/#tagged-values
     */
    public const int MAX_SAFE_DELTA = 1 << 30;
}
