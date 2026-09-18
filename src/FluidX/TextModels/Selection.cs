using FluidX.TextBuffers;

namespace FluidX.TextModels;

/// <summary>
/// A selection in the editor. The selection is a <see cref="TextRange"/> that has an orientation.
/// </summary>
/// <param name="SelectionStartLineIndex">The zero-based line index on which the selection has started.</param>
/// <param name="SelectionStartColumnIndex">The zero-based column index on `selectionStartLineIndex` where the selection has started.</param>
/// <param name="PositionLineIndex">The zero-based line index on which the selection has ended.</param>
/// <param name="PositionColumnIndex">The zero-based exclusive column index on `positionLineIndex` where the selection has ended.</param>
public record class Selection(
    int SelectionStartLineIndex,
    int SelectionStartColumnIndex,
    int PositionLineIndex,
    int PositionColumnIndex
    )
{
    public Selection(TextRange range) : this(
        range.StartLineIndex,
        range.StartColumnIndex,
        range.EndLineIndex,
        range.EndColumnIndex)
    { }

    public SelectionDirection Direction
    {
        get
        {
            if (SelectionStartLineIndex < PositionLineIndex)
                return SelectionDirection.LTR;
            if (SelectionStartLineIndex > PositionLineIndex)
                return SelectionDirection.RTL;
            // Same line
            if (SelectionStartColumnIndex <= PositionColumnIndex)
                return SelectionDirection.LTR;
            else
                return SelectionDirection.RTL;
        }
    }
}

/// <summary>
/// The direction of a <see cref="Selection"/>.
/// </summary>
public enum SelectionDirection
{
    /// <summary>
    /// The selection starts above where it ends.
    /// </summary>
    LTR,
    /// <summary>
    /// The selection starts below where it ends.
    /// </summary>
    RTL
}
