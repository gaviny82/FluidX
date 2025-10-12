namespace FluidX.TextBuffers;

public readonly record struct Range : IRange
{
    public int StartLineNumber { get; }
    public int StartColumn { get; }
    public int EndLineNumber { get; }
    public int EndColumn { get; }

    public Range(int startLineNumber, int startColumn, int endLineNumber, int endColumn)
    {
        // When the start position is after the end position, set the start position to the end position
        if (startLineNumber > endLineNumber || (startLineNumber == endLineNumber && startColumn > endColumn))
        {
            StartLineNumber = endLineNumber;
            StartColumn = endColumn;
            EndLineNumber = startLineNumber;
            EndColumn = startColumn;
        }
        else
        {
            StartLineNumber = startLineNumber;
            StartColumn = startColumn;
            EndLineNumber = endLineNumber;
            EndColumn = endColumn;
        }
    }

    public bool IsEmpty() => IsEmpty(this);

    public static bool IsEmpty(IRange range) => range.StartLineNumber == range.EndLineNumber && range.StartColumn == range.EndColumn;

    public bool ContainsPosition(IPosition position) => ContainsPosition(this, position);

    public static bool ContainsPosition(IRange range, IPosition position)
    {
        if (position.LineNumber < range.StartLineNumber || position.LineNumber > range.EndLineNumber)
            return false;
        if (position.LineNumber == range.StartLineNumber && position.Column < range.StartColumn)
            return false;
        if (position.LineNumber == range.EndLineNumber && position.Column > range.EndColumn)
            return false;
        return true;
    }

    public static bool StrictContainsPosition(IRange range, IPosition position)
    {
        if (position.LineNumber < range.StartLineNumber || position.LineNumber > range.EndLineNumber)
            return false;
        if (position.LineNumber == range.StartLineNumber && position.Column <= range.StartColumn)
            return false;
        if (position.LineNumber == range.EndLineNumber && position.Column >= range.EndColumn)
            return false;
        return true;
    }

    public bool ContainsRange(IRange otherRange) => ContainsRange(this, otherRange);

    public static bool ContainsRange(IRange range, IRange otherRange)
    {
        if (otherRange.StartLineNumber < range.StartLineNumber || otherRange.EndLineNumber < range.StartLineNumber)
            return false;
        if (otherRange.StartLineNumber > range.EndLineNumber || otherRange.EndLineNumber > range.EndLineNumber)
            return false;
        if (otherRange.StartLineNumber == range.StartLineNumber && otherRange.StartColumn < range.StartColumn)
            return false;
        if (otherRange.EndLineNumber == range.EndLineNumber && otherRange.EndColumn > range.EndColumn)
            return false;
        return true;
    }

    public bool StrictContainsRange(IRange range) => StrictContainsRange(this, range);

    public static bool StrictContainsRange(IRange range, IRange otherRange)
    {
        if (otherRange.StartLineNumber < range.StartLineNumber || otherRange.EndLineNumber < range.StartLineNumber)
            return false;
        if (otherRange.StartLineNumber > range.EndLineNumber || otherRange.EndLineNumber > range.EndLineNumber)
            return false;
        if (otherRange.StartLineNumber == range.StartLineNumber && otherRange.StartColumn <= range.StartColumn)
            return false;
        if (otherRange.EndLineNumber == range.EndLineNumber && otherRange.EndColumn >= range.EndColumn)
            return false;
        return true;
    }

    public Range PlusRange(IRange range) => PlusRange(this, range);

    public static Range PlusRange(IRange a, IRange b)
    {
        int startLineNumber, startColumn, endLineNumber, endColumn;

        if (b.StartLineNumber < a.StartLineNumber)
        {
            startLineNumber = b.StartLineNumber;
            startColumn = b.StartColumn;
        }
        else if (b.StartLineNumber == a.StartLineNumber)
        {
            startLineNumber = b.StartLineNumber;
            startColumn = Math.Min(b.StartColumn, a.StartColumn);
        }
        else
        {
            startLineNumber = a.StartLineNumber;
            startColumn = a.StartColumn;
        }

        if (b.EndLineNumber > a.EndLineNumber)
        {
            endLineNumber = b.EndLineNumber;
            endColumn = b.EndColumn;
        }
        else if (b.EndLineNumber == a.EndLineNumber)
        {
            endLineNumber = b.EndLineNumber;
            endColumn = Math.Max(b.EndColumn, a.EndColumn);
        }
        else
        {
            endLineNumber = a.EndLineNumber;
            endColumn = a.EndColumn;
        }

        return new Range(startLineNumber, startColumn, endLineNumber, endColumn);
    }

    public Range? IntersectRanges(IRange range) => IntersectRanges(this, range);

    public static Range? IntersectRanges(IRange a, IRange b)
    {
        int resultStartLineNumber = a.StartLineNumber;
        int resultStartColumn = a.StartColumn;
        int resultEndLineNumber = a.EndLineNumber;
        int resultEndColumn = a.EndColumn;

        int otherStartLineNumber = b.StartLineNumber;
        int otherStartColumn = b.StartColumn;
        int otherEndLineNumber = b.EndLineNumber;
        int otherEndColumn = b.EndColumn;

        if (resultStartLineNumber < otherStartLineNumber)
        {
            resultStartLineNumber = otherStartLineNumber;
            resultStartColumn = otherStartColumn;
        }
        else if (resultStartLineNumber == otherStartLineNumber)
        {
            resultStartColumn = Math.Max(resultStartColumn, otherStartColumn);
        }

        if (resultEndLineNumber > otherEndLineNumber)
        {
            resultEndLineNumber = otherEndLineNumber;
            resultEndColumn = otherEndColumn;
        }
        else if (resultEndLineNumber == otherEndLineNumber)
        {
            resultEndColumn = Math.Min(resultEndColumn, otherEndColumn);
        }

        // Check if selection is now empty
        if (resultStartLineNumber > resultEndLineNumber)
            return null;
        if (resultStartLineNumber == resultEndLineNumber && resultStartColumn > resultEndColumn)
            return null;

        return new Range(resultStartLineNumber, resultStartColumn, resultEndLineNumber, resultEndColumn);
    }

    public bool EqualsRange(IRange? other) => EqualsRange(this, other);

    public static bool EqualsRange(IRange? a, IRange? b)
    {
        if (a is null && b is null)
            return true;

        return a is not null && b is not null &&
               a.StartLineNumber == b.StartLineNumber &&
               a.StartColumn == b.StartColumn &&
               a.EndLineNumber == b.EndLineNumber &&
               a.EndColumn == b.EndColumn;
    }

    public Position GetEndPosition() => GetEndPosition(this);

    public static Position GetEndPosition(IRange range)
        => new Position(range.EndLineNumber, range.EndColumn);

    public Position GetStartPosition() => GetStartPosition(this);

    public static Position GetStartPosition(IRange range)
        => new Position(range.StartLineNumber, range.EndLineNumber);

    public override string ToString()
        => $"[{StartLineNumber},{StartColumn} -> {EndLineNumber},{EndColumn}]";

    public Range SetEndPosition(int endLineNumber, int endColumn)
        => new Range(StartLineNumber, StartColumn, endLineNumber, endColumn);

    public Range SetStartPosition(int startLineNumber, int startColumn)
        => new Range(startLineNumber, startColumn, EndLineNumber, EndColumn);

    public Range CollapseToStart() => CollapseToStart(this);

    public static Range CollapseToStart(IRange range)
        => new Range(range.StartLineNumber, range.StartColumn, range.StartLineNumber, range.StartColumn);

    public Range CollapseToEnd() => CollapseToEnd(this);

    public static Range CollapseToEnd(IRange range)
        => new Range(range.EndLineNumber, range.EndColumn, range.EndLineNumber, range.EndColumn);

    public Range Delta(int lineCount)
        => new Range(StartLineNumber + lineCount, StartColumn, EndLineNumber + lineCount, EndColumn);

    public static Range FromPositions(Position start, Position? end = null)
    {
        Position endPos = end ?? start;
        return new Range(start.LineNumber, start.Column, endPos.LineNumber, endPos.Column);
    }

    public static Range? Lift(IRange? range)
    {
        if (range is null)
            return null;
        return new Range(range.StartLineNumber, range.StartColumn, range.EndLineNumber, range.EndColumn);
    }

    public static bool AreIntersectingOrTouching(IRange a, IRange b)
    {
        // Check if `a` is before `b`
        if (a.EndLineNumber < b.StartLineNumber || (a.EndLineNumber == b.StartLineNumber && a.EndColumn < b.StartColumn))
            return false;

        // Check if `b` is before `a`
        if (b.EndLineNumber < a.StartLineNumber || (b.EndLineNumber == a.StartLineNumber && b.EndColumn < a.StartColumn))
            return false;

        // These ranges must intersect
        return true;
    }

    public static bool AreIntersecting(IRange a, IRange b)
    {
        // Check if `a` is before `b`
        if (a.EndLineNumber < b.StartLineNumber || (a.EndLineNumber == b.StartLineNumber && a.EndColumn <= b.StartColumn))
            return false;

        // Check if `b` is before `a`
        if (b.EndLineNumber < a.StartLineNumber || (b.EndLineNumber == a.StartLineNumber && b.EndColumn <= a.StartColumn))
            return false;

        // These ranges must intersect
        return true;
    }

    public static int CompareRangesUsingStarts(IRange? a, IRange? b)
    {
        if (a is not null && b is not null)
        {
            int aStartLineNumber = a.StartLineNumber;
            int bStartLineNumber = b.StartLineNumber;

            if (aStartLineNumber == bStartLineNumber)
            {
                int aStartColumn = a.StartColumn;
                int bStartColumn = b.StartColumn;

                if (aStartColumn == bStartColumn)
                {
                    int aEndLineNumber = a.EndLineNumber;
                    int bEndLineNumber = b.EndLineNumber;

                    if (aEndLineNumber == bEndLineNumber)
                    {
                        int aEndColumn = a.EndColumn;
                        int bEndColumn = b.EndColumn;
                        return aEndColumn - bEndColumn;
                    }
                    return aEndLineNumber - bEndLineNumber;
                }
                return aStartColumn - bStartColumn;
            }
            return aStartLineNumber - bStartLineNumber;
        }

        // Handle cases where one or both are null
        int aExists = a is not null ? 1 : 0;
        int bExists = b is not null ? 1 : 0;
        return aExists - bExists;
    }

    public static int CompareRangesUsingEnds(IRange a, IRange b)
    {
        if (a.EndLineNumber == b.EndLineNumber)
        {
            if (a.EndColumn == b.EndColumn)
            {
                if (a.StartLineNumber == b.StartLineNumber)
                {
                    return a.StartColumn - b.StartColumn;
                }
                return a.StartLineNumber - b.StartLineNumber;
            }
            return a.EndColumn - b.EndColumn;
        }
        return a.EndLineNumber - b.EndLineNumber;
    }

    public static bool SpansMultipleLines(IRange range) => range.EndLineNumber > range.StartLineNumber;
}
