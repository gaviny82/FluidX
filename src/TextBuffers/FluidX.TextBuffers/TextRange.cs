namespace FluidX.TextBuffers;

/// <summary>
/// A range in a text document with zero-based line and UTF-16 column indices.
/// The start is inclusive and the end is exclusive.
/// </summary>
public readonly record struct TextRange : IEquatable<TextRange>
{
    public int StartLineIndex { get; }
    public int StartColumnIndex { get; }
    public int EndLineIndex { get; }
    public int EndColumnIndex { get; }

    public TextPosition StartPosition => new(StartLineIndex, StartColumnIndex);
    public TextPosition EndPosition => new(EndLineIndex, EndColumnIndex);
    public bool IsEmpty => StartLineIndex == EndLineIndex && StartColumnIndex == EndColumnIndex;
    public bool SpansMultipleLines => EndLineIndex > StartLineIndex;

    public TextRange(int startLineIndex, int startColumnIndex, int endLineIndex, int endColumnIndex)
    {
        // When the start position is after the end position, set the start position to the end position
        if (startLineIndex > endLineIndex || (startLineIndex == endLineIndex && startColumnIndex > endColumnIndex))
        {
            StartLineIndex = endLineIndex;
            StartColumnIndex = endColumnIndex;
            EndLineIndex = startLineIndex;
            EndColumnIndex = startColumnIndex;
        }
        else
        {
            StartLineIndex = startLineIndex;
            StartColumnIndex = startColumnIndex;
            EndLineIndex = endLineIndex;
            EndColumnIndex = endColumnIndex;
        }
    }

    public TextRange(TextPosition start, TextPosition end) : this(
        start.LineIndex,
        start.ColumnIndex,
        end.LineIndex,
        end.ColumnIndex)
    { }

    #region Overlapping Checks

    public bool ContainsPosition(TextPosition position)
    {
        if (position.LineIndex < StartLineIndex || position.LineIndex > EndLineIndex)
            return false;
        if (position.LineIndex == StartLineIndex && position.ColumnIndex < StartColumnIndex)
            return false;
        if (position.LineIndex == EndLineIndex && position.ColumnIndex > EndColumnIndex)
            return false;
        return true;
    }

    public bool StrictContainsPosition(TextPosition position)
    {
        if (position.LineIndex < StartLineIndex || position.LineIndex > EndLineIndex)
            return false;
        if (position.LineIndex == StartLineIndex && position.ColumnIndex <= StartColumnIndex)
            return false;
        if (position.LineIndex == EndLineIndex && position.ColumnIndex >= EndColumnIndex)
            return false;
        return true;
    }

    public bool ContainsRange(TextRange range)
    {
        if (range.StartLineIndex < StartLineIndex || range.EndLineIndex < StartLineIndex)
            return false;
        if (range.StartLineIndex > EndLineIndex || range.EndLineIndex > EndLineIndex)
            return false;
        if (range.StartLineIndex == StartLineIndex && range.StartColumnIndex < StartColumnIndex)
            return false;
        if (range.EndLineIndex == EndLineIndex && range.EndColumnIndex > EndColumnIndex)
            return false;
        return true;
    }

    public bool StrictContainsRange(TextRange range)
    {
        if (range.StartLineIndex < StartLineIndex || range.EndLineIndex < StartLineIndex)
            return false;
        if (range.StartLineIndex > EndLineIndex || range.EndLineIndex > EndLineIndex)
            return false;
        if (range.StartLineIndex == StartLineIndex && range.StartColumnIndex <= StartColumnIndex)
            return false;
        if (range.EndLineIndex == EndLineIndex && range.EndColumnIndex >= EndColumnIndex)
            return false;
        return true;
    }

    public bool IsIntersectingOrTouching(TextRange range)
    {
        // Check if `this` is before `range`
        if (EndLineIndex < range.StartLineIndex || (EndLineIndex == range.StartLineIndex && EndColumnIndex < range.StartColumnIndex))
            return false;
        // Check if `range` is before `this`
        if (range.EndLineIndex < StartLineIndex || (range.EndLineIndex == StartLineIndex && range.EndColumnIndex < StartColumnIndex))
            return false;
        // These ranges must intersect
        return true;
    }

    public bool IsIntersecting(TextRange range)
    {
        // Check if `this` is before `range`
        if (EndLineIndex < range.StartLineIndex || (EndLineIndex == range.StartLineIndex && EndColumnIndex <= range.StartColumnIndex))
            return false;
        // Check if `range` is before `this`
        if (range.EndLineIndex < StartLineIndex || (range.EndLineIndex == StartLineIndex && range.EndColumnIndex <= StartColumnIndex))
            return false;
        // These ranges must intersect
        return true;
    }

    #endregion

    #region Modifications

    public TextRange PlusRange(TextRange range)
    {
        int startLineIndex, startColumnIndex, endLineIndex, endColumnIndex;

        if (range.StartLineIndex < StartLineIndex)
        {
            startLineIndex = range.StartLineIndex;
            startColumnIndex = range.StartColumnIndex;
        }
        else if (range.StartLineIndex == StartLineIndex)
        {
            startLineIndex = range.StartLineIndex;
            startColumnIndex = Math.Min(range.StartColumnIndex, StartColumnIndex);
        }
        else
        {
            startLineIndex = StartLineIndex;
            startColumnIndex = StartColumnIndex;
        }

        if (range.EndLineIndex > EndLineIndex)
        {
            endLineIndex = range.EndLineIndex;
            endColumnIndex = range.EndColumnIndex;
        }
        else if (range.EndLineIndex == EndLineIndex)
        {
            endLineIndex = range.EndLineIndex;
            endColumnIndex = Math.Max(range.EndColumnIndex, EndColumnIndex);
        }
        else
        {
            endLineIndex = EndLineIndex;
            endColumnIndex = EndColumnIndex;
        }

        return new TextRange(startLineIndex, startColumnIndex, endLineIndex, endColumnIndex);
    }

    public TextRange? Intersection(TextRange range)
    {
        int resultStartLineIndex = StartLineIndex;
        int resultStartColumnIndex = StartColumnIndex;
        int resultEndLineIndex = EndLineIndex;
        int resultEndColumnIndex = EndColumnIndex;

        int otherStartLineIndex = range.StartLineIndex;
        int otherStartColumnIndex = range.StartColumnIndex;
        int otherEndLineIndex = range.EndLineIndex;
        int otherEndColumnIndex = range.EndColumnIndex;

        if (resultStartLineIndex < otherStartLineIndex)
        {
            resultStartLineIndex = otherStartLineIndex;
            resultStartColumnIndex = otherStartColumnIndex;
        }
        else if (resultStartLineIndex == otherStartLineIndex)
        {
            resultStartColumnIndex = Math.Max(resultStartColumnIndex, otherStartColumnIndex);
        }

        if (resultEndLineIndex > otherEndLineIndex)
        {
            resultEndLineIndex = otherEndLineIndex;
            resultEndColumnIndex = otherEndColumnIndex;
        }
        else if (resultEndLineIndex == otherEndLineIndex)
        {
            resultEndColumnIndex = Math.Min(resultEndColumnIndex, otherEndColumnIndex);
        }

        // Check if selection is now empty
        if (resultStartLineIndex > resultEndLineIndex)
            return null;
        if (resultStartLineIndex == resultEndLineIndex && resultStartColumnIndex > resultEndColumnIndex)
            return null;

        return new TextRange(resultStartLineIndex, resultStartColumnIndex, resultEndLineIndex, resultEndColumnIndex);
    }

    public TextRange WithNewEndPosition(int endLineIndex, int endColumnIndex)
        => new(StartLineIndex, StartColumnIndex, endLineIndex, endColumnIndex);

    public TextRange WithNewStartPosition(int startLineIndex, int startColumnIndex)
        => new(startLineIndex, startColumnIndex, EndLineIndex, EndColumnIndex);

    public TextRange CollapseToStart()
        => new(StartLineIndex, StartColumnIndex, StartLineIndex, StartColumnIndex);

    public TextRange CollapseToEnd()
        => new(EndLineIndex, EndColumnIndex, EndLineIndex, EndColumnIndex);

    public TextRange WithLineDelta(int lineCount)
        => new(StartLineIndex + lineCount, StartColumnIndex, EndLineIndex + lineCount, EndColumnIndex);

    #endregion

    #region Comparisons

    public static int CompareRangesUsingStarts(TextRange a, TextRange b)
    {
        int aStartLineIndex = a.StartLineIndex;
        int bStartLineIndex = b.StartLineIndex;

        if (aStartLineIndex == bStartLineIndex)
        {
            int aStartColumnIndex = a.StartColumnIndex;
            int bStartColumnIndex = b.StartColumnIndex;

            if (aStartColumnIndex == bStartColumnIndex)
            {
                int aEndLineIndex = a.EndLineIndex;
                int bEndLineIndex = b.EndLineIndex;

                if (aEndLineIndex == bEndLineIndex)
                {
                    int aEndColumnIndex = a.EndColumnIndex;
                    int bEndColumnIndex = b.EndColumnIndex;
                    return aEndColumnIndex - bEndColumnIndex;
                }
                return aEndLineIndex - bEndLineIndex;
            }
            return aStartColumnIndex - bStartColumnIndex;
        }
        return aStartLineIndex - bStartLineIndex;
    }

    public static int CompareRangesUsingEnds(TextRange a, TextRange b)
    {
        if (a.EndLineIndex == b.EndLineIndex)
        {
            if (a.EndColumnIndex == b.EndColumnIndex)
            {
                if (a.StartLineIndex == b.StartLineIndex)
                {
                    return a.StartColumnIndex - b.StartColumnIndex;
                }
                return a.StartLineIndex - b.StartLineIndex;
            }
            return a.EndColumnIndex - b.EndColumnIndex;
        }
        return a.EndLineIndex - b.EndLineIndex;
    }

    #endregion

    public override string ToString()
        => $"[{StartLineIndex},{StartColumnIndex} -> {EndLineIndex},{EndColumnIndex}]";
}
