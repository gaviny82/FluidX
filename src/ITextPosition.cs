namespace FluidX;

/// <summary>
/// A position in a text document.
/// </summary>
public interface ITextPosition
{
    /// <summary>
    /// Line number (starts at 1).
    /// </summary>
    int LineNumber { get; }

    /// <summary>
    /// Column (the first character in a line is between column 1 and column 2).
    /// </summary>
    int Column { get; }
}

public static class ITextPositionExtensions
{
    extension(ITextPosition pos)
    {
        /// <summary>
        /// Test if position <paramref name="a"/> equals position <paramref name="otherPos"/>.
        /// </summary>
        /// <returns>If the two positions are equal, the result will be <see langword="false"/>.</returns>
        public bool IsBefore(ITextPosition otherPos)
            => pos.Lift().IsBefore(otherPos.Lift());

        /// <summary>
        /// Test if position <paramref name="a"/> is before or equal to position <paramref name="otherPos"/>.
        /// </summary>
        /// <returns>If the two positions are equal, the result will be <see langword="true"/>.</returns>
        public bool IsBeforeOrEqual(ITextPosition otherPos)
            => pos.Lift().IsBeforeOrEqual(otherPos.Lift());

        /// <summary>
        /// Compare two positions for sorting.
        /// </summary>
        public int CompareTo(ITextPosition otherPos)
            => pos.Lift().CompareTo(otherPos.Lift());

        /// <summary>
        /// Create a <see cref="TextPosition"/> from an <see cref="ITextPosition"/>.
        /// </summary>
        public TextPosition Lift()
            => new(pos.LineNumber, pos.Column);
    }

    extension(ITextPosition? pos)
    {
        /// <summary>
        /// Test if this position equals another position.
        /// </summary>
        /// <param name="otherPos">Other position</param>
        public bool Equals(ITextPosition? otherPos)
        {
            if (pos is null && otherPos is null)
                return true;
            if (pos is null || otherPos is null)
                return false;
            return pos.Lift().Equals(otherPos.Lift());
        }
    }
}
