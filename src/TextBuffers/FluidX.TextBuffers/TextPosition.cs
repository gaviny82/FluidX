namespace FluidX.TextBuffers;

/// <summary>
/// A position in a text document.
/// </summary>
/// <param name="LineIndex">Line index from the start of the document. The first line in a document has index 0.</param>
/// <param name="ColumnIndex">Zero-based UTF-16 offset from the start of the line; the line end is its length.</param>
public readonly record struct TextPosition(int LineIndex, int ColumnIndex)
    : IComparable<TextPosition>, IEquatable<TextPosition>
{
    /// <summary>
    /// Derive a new position from this position.
    /// </summary>
    /// <param name="deltaLineIndex">Line index delta</param>
    /// <param name="deltaColumnIndex">Column index delta</param>
    /// <returns></returns>
    public TextPosition Delta(int deltaLineIndex = 0, int deltaColumnIndex = 0) => this with
    {
        LineIndex = LineIndex + deltaLineIndex,
        ColumnIndex = ColumnIndex + deltaColumnIndex
    };

    /// <summary>
    /// Test if this position is before another position.
    /// </summary>
    /// <param name="other">Another position</param>
    /// <returns>If the two positions are equal, the result will be <see langword="false"/>.</returns>
    public bool IsBefore(TextPosition other)
    {
        if (LineIndex < other.LineIndex)
            return true;
        if (LineIndex > other.LineIndex)
            return false;
        return ColumnIndex < other.ColumnIndex;
    }

    /// <summary>
    /// Test if this position is before or equal to another position.
    /// </summary>
    /// <returns>If the two positions are equal, the result will be <see langword="true"/>.</returns>
    public bool IsBeforeOrEqual(TextPosition other)
    {
        if (LineIndex < other.LineIndex)
            return true;
        if (LineIndex > other.LineIndex)
            return false;
        return ColumnIndex <= other.ColumnIndex;
    }

    /// <summary>
    /// Compare <see langword="this"/> with another position for sorting.
    /// </summary>
    /// <param name="other">The other position to compare with.</param>
    /// <returns>A value indicating the comparison between <see langword="this"/> and <paramref name="other"/>.</returns>
    public int CompareTo(TextPosition other)
    {
        if (LineIndex == other.LineIndex)
            return ColumnIndex.CompareTo(other.ColumnIndex);

        return LineIndex.CompareTo(other.LineIndex);
    }

    /// <summary>
    /// Convert to a human-readable representation.
    /// </summary>
    public override string ToString() => $"({LineIndex}, {ColumnIndex})";
}
