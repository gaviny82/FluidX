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
}
