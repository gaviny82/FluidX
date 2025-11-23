namespace FluidX;

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
    /// Test if this position is before another position.
    /// </summary>
    /// <param name="other">Another position</param>
    /// <returns>If the two positions are equal, the result will be <see langword="false"/>.</returns>
    public bool IsBefore(TextPosition other)
    {
        if (LineNumber < other.LineNumber)
            return true;
        if (LineNumber > other.LineNumber)
            return false;
        return Column < other.Column;
    }

    /// <summary>
    /// Test if this position is before or equal to another position.
    /// </summary>
    /// <returns>If the two positions are equal, the result will be <see langword="true"/>.</returns>
    public bool IsBeforeOrEqual(TextPosition other)
    {
        if (LineNumber < other.LineNumber)
            return true;
        if (LineNumber > other.LineNumber)
            return false;
        return Column <= other.Column;
    }

    /// <summary>
    /// Compare <see langword="this"/> with another position for sorting.
    /// </summary>
    /// <param name="other">The other position to compare with.</param>
    /// <returns>A value indicating the comparison between <see langword="this"/> and <paramref name="other"/>.</returns>
    public int CompareTo(TextPosition other)
    {
        if (LineNumber == other.LineNumber)
            return Column - other.Column;

        return LineNumber - LineNumber;
    }

    /// <summary>
    /// Convert to a human-readable representation.
    /// </summary>
    public override string ToString() => $"({LineNumber}, {Column})";

    #region ITextPosition Helpers

    /// <summary>
    /// Test if position <paramref name="a"/> equals position <paramref name="b"/>.
    /// </summary>
    /// <returns>If the two positions are equal, the result will be <see langword="false"/>.</returns>
    public static bool IsBefore(ITextPosition a, ITextPosition b)
        => Lift(a).IsBefore(Lift(b));

    /// <summary>
    /// Test if position <paramref name="a"/> is before or equal to position <paramref name="b"/>.
    /// </summary>
    /// <returns>If the two positions are equal, the result will be <see langword="true"/>.</returns>
    public static bool IsBeforeOrEqual(ITextPosition a, ITextPosition b)
        => Lift(a).IsBeforeOrEqual(Lift(b));

    /// <summary>
    /// Compare two positions for sorting.
    /// </summary>
    public static int CompareTo(ITextPosition a, ITextPosition b)
        => Lift(a).CompareTo(Lift(b));

    /// <summary>
    /// Test if this position equals other position.
    /// </summary>
    /// <param name="other">Other position</param>
    public bool Equals(ITextPosition other) => Equals(Lift(other));

    /// <summary>
    /// Test if position <paramref name="a"/> equals position <paramref name="b"/>.
    /// </summary>
    public static bool Equals(ITextPosition a, ITextPosition b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        return Lift(a).Equals(Lift(b));
    }

    /// <summary>
    /// Create a <see cref="TextPosition"/> from an <see cref="ITextPosition"/>.
    /// </summary>
    public static TextPosition Lift(ITextPosition pos)
        => new(pos.LineNumber, pos.Column);

    #endregion
}
