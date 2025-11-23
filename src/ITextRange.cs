namespace FluidX;

/// <summary>
/// A range in a text document.
/// </summary>
public interface ITextRange
{
    /// <summary>
    /// Line number on which the range starts (starts at 1).
    /// </summary>
    int StartLineNumber { get; }

    /// <summary>
    /// Column on which the range starts in line <see cref="StartLineNumber"/> (starts at 1).
    /// </summary>
    int StartColumn { get; }

    /// <summary>
    /// Line number on which the range ends.
    /// </summary>
    int EndLineNumber { get; }

    /// <summary>
    /// Column on which the range ends in line <see cref="EndLineNumber"/>.
    /// </summary>
    int EndColumn { get; }
}

public static class ITextRangeExtensions
{
    extension(ITextRange range)
    {
        public bool ContainsPosition(ITextPosition position)
        => range.Lift().ContainsPosition(position.Lift());

        public bool StrictContainsPosition(ITextPosition position)
            => range.Lift().StrictContainsPosition(position.Lift());

        /// <summary>
        /// Test if <paramref name="otherRange"/> is in <paramref name="range"/>. If the ranges are equal, will return <see langword="true"/>.
        /// </summary>
        /// <param name="range">The range to test if it contains <paramref name="otherRange"/>.</param>
        /// <param name="otherRange">The range to test if it is contained by <paramref name="range"/>.</param>
        /// <returns></returns>
        public bool ContainsRange(ITextRange otherRange)
            => range.Lift().ContainsRange(Lift(otherRange));

        /// <summary>
        /// Test if <paramref name="otherRange"/> is in <paramref name="range"/> (must start after, and end before). If the ranges are equal, will return <see langword="false"/>.
        /// </summary>
        /// <param name="range">The range to test if it contains <paramref name="otherRange"/>.</param>
        /// <param name="otherRange">The range to test if it is contained by <paramref name="range"/>.</param>
        /// <returns></returns>
        public bool StrictContainsRange(ITextRange otherRange)
            => range.Lift().StrictContainsRange(otherRange.Lift());

        public TextRange PlusRange(ITextRange otherRange)
            => range.Lift().PlusRange(otherRange.Lift());

        public TextRange? Intersection(ITextRange otherRange)
            => range.Lift().Intersection(otherRange.Lift());

        public TextRange CollapseToStart()
            => range.Lift().CollapseToStart();

        public TextRange CollapseToEnd()
            => range.Lift().CollapseToEnd();

        public TextRange Lift()
            => new(range.StartLineNumber, range.StartColumn, range.EndLineNumber, range.EndColumn);
    }

    extension(ITextRange? range)
    {
        public bool Equals(ITextRange? otherRange)
        {
            if (range is null && otherRange is null)
                return true;

            return range is not null && otherRange is not null &&
                   range.StartLineNumber == otherRange.StartLineNumber &&
                   range.StartColumn == otherRange.StartColumn &&
                   range.EndLineNumber == otherRange.EndLineNumber &&
                   range.EndColumn == otherRange.EndColumn;
        }
    }
}
