using Range = FluidX.TextBuffers.Range;
using System;
using System.Collections.Generic;
using System.Text;

namespace FluidX.TextModels;

/// <summary>
/// A selection in the editor. The selection is a <see cref="FluidX.TextBuffers.Range"/> that has an orientation.
/// </summary>
/// <param name="SelectionStartLineNumber">The line number on which the selection has started.</param>
/// <param name="SelectionStartColumn">The column on `selectionStartLineNumber` where the selection has started.</param>
/// <param name="PositionLineNumber">The line number on which the selection has ended.</param>
/// <param name="PositionColumn">The column on `positionLineNumber` where the selection has ended.</param>
public record class Selection(
    int SelectionStartLineNumber,
    int SelectionStartColumn,
    int PositionLineNumber,
    int PositionColumn
    )
{
    public Selection(Range range) : this(
        range.StartLineNumber,
        range.StartColumn,
        range.EndLineNumber,
        range.EndColumn)
    { }

    public SelectionDirection Direction
    {
        get
        {
            if (SelectionStartLineNumber < PositionLineNumber)
                return SelectionDirection.LTR;
            if (SelectionStartLineNumber > PositionLineNumber)
                return SelectionDirection.RTL;
            // Same line
            if (SelectionStartColumn <= PositionColumn)
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
