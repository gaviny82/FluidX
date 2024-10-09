namespace EditorTextBuffers;

/// <summary>
/// A position in the editor.
/// </summary>
/// <param name="LineNumber">Line number (starts at 1)</param>
/// <param name="Column">Column (the first character in a line is between column 1 and column 2)</param>
public readonly record struct Position(int LineNumber, int Column) : IPosition
{
    /// <summary>
    /// Derive a new position from this position.
    /// </summary>
    /// <param name="deltaLineNumber">Line number delta</param>
    /// <param name="deltaColumn">Column delta</param>
    /// <returns></returns>
    public Position Delta(int deltaLineNumber = 0, int deltaColumn = 0) => this with
    {
        LineNumber = LineNumber + deltaLineNumber,
        Column = Column + deltaColumn
    };

    /// <summary>
    /// Test if this position equals other position.
    /// </summary>
    /// <param name="other">Other position</param>
    public bool Equals(IPosition other) => Equals(this, other);

    /// <summary>
    /// Test if position <paramref name="a"/> equals position <paramref name="b"/>.
    /// </summary>
    public static bool Equals(IPosition a, IPosition b)
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
    public bool IsBefore(IPosition other) => IsBefore(this, other);

    /// <summary>
    /// Test if position <paramref name="a"/> equals position <paramref name="b"/>.
    /// </summary>
    /// <returns>If the two positions are equal, the result will be <see langword="false"/>.</returns>
    public static bool IsBefore(IPosition a, IPosition b)
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
    public bool IsBeforeOrEqual(IPosition other) => IsBeforeOrEqual(this, other);

    /// <summary>
    /// Test if position <paramref name="a"/> is before or equal to position <paramref name="b"/>.
    /// </summary>
    /// <returns>If the two positions are equal, the result will be <see langword="true"/>.</returns>
    public static bool IsBeforeOrEqual(IPosition a, IPosition b)
    {
        if (a.LineNumber < b.LineNumber)
            return true;
        if (a.LineNumber > b.LineNumber)
            return false;
        return a.Column <= b.Column;
    }

    /// <summary>
    /// Compare two positions, useful for sorting.
    /// </summary>
    public static int Compare(IPosition a, IPosition b)
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
    /// Create a <see cref="Position"/> from an <see cref="IPosition"/>.
    /// </summary>
    public static Position Lift(IPosition pos)
    {
        return new Position(pos.LineNumber, pos.Column);
    }
}
