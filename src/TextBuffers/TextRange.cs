namespace FluidX.TextBuffers;

/// <summary>
/// A range in a text document.
/// </summary>
public readonly record struct TextRange : ITextRange, IEquatable<TextRange>
{
    public int StartLineNumber { get; }
    public int StartColumn { get; }
    public int EndLineNumber { get; }
    public int EndColumn { get; }

    public TextRange(int startLineNumber, int startColumn, int endLineNumber, int endColumn)
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

    public TextRange(TextPosition start, TextPosition end) : this(
        start.LineNumber,
        start.Column,
        end.LineNumber,
        end.Column)
    { }

    public bool IsEmpty() => IsEmpty(this);

    public static bool IsEmpty(ITextRange range) => range.StartLineNumber == range.EndLineNumber && range.StartColumn == range.EndColumn;

    public bool ContainsPosition(ITextPosition position) => ContainsPosition(this, position);

    public static bool ContainsPosition(ITextRange range, ITextPosition position)
    {
        if (position.LineNumber < range.StartLineNumber || position.LineNumber > range.EndLineNumber)
            return false;
        if (position.LineNumber == range.StartLineNumber && position.Column < range.StartColumn)
            return false;
        if (position.LineNumber == range.EndLineNumber && position.Column > range.EndColumn)
            return false;
        return true;
    }

    public static bool StrictContainsPosition(ITextRange range, ITextPosition position)
    {
        if (position.LineNumber < range.StartLineNumber || position.LineNumber > range.EndLineNumber)
            return false;
        if (position.LineNumber == range.StartLineNumber && position.Column <= range.StartColumn)
            return false;
        if (position.LineNumber == range.EndLineNumber && position.Column >= range.EndColumn)
            return false;
        return true;
    }

    public bool ContainsRange(ITextRange otherRange) => ContainsRange(this, otherRange);

    /// <summary>
    /// Test if <paramref name="otherRange"/> is in <paramref name="range"/>. If the ranges are equal, will return <see langword="true"/>.
    /// </summary>
    /// <param name="range">The range to test if it contains <paramref name="otherRange"/>.</param>
    /// <param name="otherRange">The range to test if it is contained by <paramref name="range"/>.</param>
    /// <returns></returns>
    public static bool ContainsRange(ITextRange range, ITextRange otherRange)
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

    public bool StrictContainsRange(ITextRange range) => StrictContainsRange(this, range);

    /// <summary>
    /// Test if <paramref name="otherRange"/> is in <paramref name="range"/> (must start after, and end before). If the ranges are equal, will return <see langword="false"/>.
    /// </summary>
    /// <param name="range">The range to test if it contains <paramref name="otherRange"/>.</param>
    /// <param name="otherRange">The range to test if it is contained by <paramref name="range"/>.</param>
    /// <returns></returns>
    public static bool StrictContainsRange(ITextRange range, ITextRange otherRange)
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

    public TextRange PlusRange(ITextRange range) => PlusRange(this, range);

    public static TextRange PlusRange(ITextRange a, ITextRange b)
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

        return new TextRange(startLineNumber, startColumn, endLineNumber, endColumn);
    }

    public TextRange? IntersectRanges(ITextRange range) => IntersectRanges(this, range);

    public static TextRange? IntersectRanges(ITextRange a, ITextRange b)
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

        return new TextRange(resultStartLineNumber, resultStartColumn, resultEndLineNumber, resultEndColumn);
    }

    public bool EqualsRange(ITextRange? other) => EqualsRange(this, other);

    public static bool EqualsRange(ITextRange? a, ITextRange? b)
    {
        if (a is null && b is null)
            return true;

        return a is not null && b is not null &&
               a.StartLineNumber == b.StartLineNumber &&
               a.StartColumn == b.StartColumn &&
               a.EndLineNumber == b.EndLineNumber &&
               a.EndColumn == b.EndColumn;
    }

    public TextPosition GetEndPosition() => GetEndPosition(this);

    public static TextPosition GetEndPosition(ITextRange range)
        => new TextPosition(range.EndLineNumber, range.EndColumn);

    public TextPosition GetStartPosition() => GetStartPosition(this);

    public static TextPosition GetStartPosition(ITextRange range)
        => new TextPosition(range.StartLineNumber, range.EndLineNumber);

    public override string ToString()
        => $"[{StartLineNumber},{StartColumn} -> {EndLineNumber},{EndColumn}]";

    public TextRange SetEndPosition(int endLineNumber, int endColumn)
        => new TextRange(StartLineNumber, StartColumn, endLineNumber, endColumn);

    public TextRange SetStartPosition(int startLineNumber, int startColumn)
        => new TextRange(startLineNumber, startColumn, EndLineNumber, EndColumn);

    public TextRange CollapseToStart() => CollapseToStart(this);

    public static TextRange CollapseToStart(ITextRange range)
        => new TextRange(range.StartLineNumber, range.StartColumn, range.StartLineNumber, range.StartColumn);

    public TextRange CollapseToEnd() => CollapseToEnd(this);

    public static TextRange CollapseToEnd(ITextRange range)
        => new TextRange(range.EndLineNumber, range.EndColumn, range.EndLineNumber, range.EndColumn);

    public TextRange Delta(int lineCount)
        => new TextRange(StartLineNumber + lineCount, StartColumn, EndLineNumber + lineCount, EndColumn);

    public static TextRange FromPositions(TextPosition start, TextPosition? end = null)
    {
        TextPosition endPos = end ?? start;
        return new TextRange(start.LineNumber, start.Column, endPos.LineNumber, endPos.Column);
    }

    public static TextRange? Lift(ITextRange? range)
    {
        if (range is null)
            return null;
        return new TextRange(range.StartLineNumber, range.StartColumn, range.EndLineNumber, range.EndColumn);
    }

    public static bool AreIntersectingOrTouching(ITextRange a, ITextRange b)
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

    public static bool AreIntersecting(ITextRange a, ITextRange b)
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

    public static int CompareRangesUsingStarts(ITextRange? a, ITextRange? b)
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

    public static int CompareRangesUsingEnds(ITextRange a, ITextRange b)
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

    public static bool SpansMultipleLines(ITextRange range) => range.EndLineNumber > range.StartLineNumber;
}
