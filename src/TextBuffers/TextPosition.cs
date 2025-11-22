namespace FluidX.TextBuffers;

/// <summary>
/// A position in a text document.
/// </summary>
/// <param name="LineNumber">Line number (starts at 1)</param>
/// <param name="Column">Column (the first character in a line is between column 1 and column 2)</param>
public readonly record struct TextPosition(int LineNumber, int Column)
    : ITextPosition, IComparable<TextPosition>, IEquatable<TextPosition>
{
    /// <summary>
    /// Derive a new position from this position.
    /// </summary>
    /// <param name="deltaLineNumber">Line number delta</param>
    /// <param name="deltaColumn">Column delta</param>
    /// <returns></returns>
    public TextPosition Delta(int deltaLineNumber = 0, int deltaColumn = 0) => this with
    {
        LineNumber = LineNumber + deltaLineNumber,
        Column = Column + deltaColumn
    };

    /// <summary>
    /// Test if this position equals other position.
    /// </summary>
    /// <param name="other">Other position</param>
    public bool Equals(ITextPosition other) => Equals(this, other);

    /// <summary>
    /// Test if position <paramref name="a"/> equals position <paramref name="b"/>.
    /// </summary>
    public static bool Equals(ITextPosition a, ITextPosition b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        return a.LineNumber == b.LineNumber && a.Column == b.Column;
    }

    /// <summary>
    /// Test if this position is before another position.
    /// </summary>
    /// <param name="other">Another position</param>
    /// <returns>If the two positions are equal, the result will be <see langword="false"/>.</returns>
    public bool IsBefore(ITextPosition other) => IsBefore(this, other);

    /// <summary>
    /// Test if position <paramref name="a"/> equals position <paramref name="b"/>.
    /// </summary>
    /// <returns>If the two positions are equal, the result will be <see langword="false"/>.</returns>
    public static bool IsBefore(ITextPosition a, ITextPosition b)
    {
        if (a.LineNumber < b.LineNumber)
            return true;
        if (a.LineNumber > b.LineNumber)
            return false;
        return a.Column < b.Column;
    }

    /// <summary>
    /// Test if this position is before or equal to another position.
    /// </summary>
    /// <returns>If the two positions are equal, the result will be <see langword="true"/>.</returns>
    public bool IsBeforeOrEqual(ITextPosition other) => IsBeforeOrEqual(this, other);

    /// <summary>
    /// Test if position <paramref name="a"/> is before or equal to position <paramref name="b"/>.
    /// </summary>
    /// <returns>If the two positions are equal, the result will be <see langword="true"/>.</returns>
    public static bool IsBeforeOrEqual(ITextPosition a, ITextPosition b)
    {
        if (a.LineNumber < b.LineNumber)
            return true;
        if (a.LineNumber > b.LineNumber)
            return false;
        return a.Column <= b.Column;
    }

    public int CompareTo(TextPosition other) => Compare(this, other);

    /// <summary>
    /// Compare two positions, useful for sorting.
    /// </summary>
    public static int Compare(ITextPosition a, ITextPosition b)
    {
        int aLineNumber = a.LineNumber;
        int bLineNumber = b.LineNumber;

        if (aLineNumber == bLineNumber)
            return a.Column - b.Column;

        return aLineNumber - bLineNumber;
    }

    /// <summary>
    /// Convert to a human-readable representation.
    /// </summary>
    public override string ToString() => $"({LineNumber}, {Column})";

    /// <summary>
    /// Create a <see cref="TextPosition"/> from an <see cref="ITextPosition"/>.
    /// </summary>
    public static TextPosition Lift(ITextPosition pos)
    {
        return new TextPosition(pos.LineNumber, pos.Column);
    }
}
