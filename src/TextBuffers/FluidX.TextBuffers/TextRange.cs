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

    public TextPosition StartPosition => new(StartLineNumber, StartColumn);
    public TextPosition EndPosition => new(EndLineNumber, EndColumn);
    public bool IsEmpty => StartLineNumber == EndLineNumber && StartColumn == EndColumn;
    public bool SpansMultipleLines => EndLineNumber > StartLineNumber;

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

    #region Overlapping Checks

    public bool ContainsPosition(TextPosition position)
    {
        if (position.LineNumber < StartLineNumber || position.LineNumber > EndLineNumber)
            return false;
        if (position.LineNumber == StartLineNumber && position.Column < StartColumn)
            return false;
        if (position.LineNumber == EndLineNumber && position.Column > EndColumn)
            return false;
        return true;
    }

    public bool StrictContainsPosition(TextPosition position)
    {
        if (position.LineNumber < StartLineNumber || position.LineNumber > EndLineNumber)
            return false;
        if (position.LineNumber == StartLineNumber && position.Column <= StartColumn)
            return false;
        if (position.LineNumber == EndLineNumber && position.Column >= EndColumn)
            return false;
        return true;
    }

    public bool ContainsRange(TextRange range)
    {
        if (range.StartLineNumber < StartLineNumber || range.EndLineNumber < StartLineNumber)
            return false;
        if (range.StartLineNumber > EndLineNumber || range.EndLineNumber > EndLineNumber)
            return false;
        if (range.StartLineNumber == StartLineNumber && range.StartColumn < StartColumn)
            return false;
        if (range.EndLineNumber == EndLineNumber && range.EndColumn > EndColumn)
            return false;
        return true;
    }

    public bool StrictContainsRange(TextRange range)
    {
        if (range.StartLineNumber < StartLineNumber || range.EndLineNumber < StartLineNumber)
            return false;
        if (range.StartLineNumber > EndLineNumber || range.EndLineNumber > EndLineNumber)
            return false;
        if (range.StartLineNumber == StartLineNumber && range.StartColumn <= StartColumn)
            return false;
        if (range.EndLineNumber == EndLineNumber && range.EndColumn >= EndColumn)
            return false;
        return true;
    }

    public bool IsIntersectingOrTouching(TextRange range)
    {
        // Check if `this` is before `range`
        if (EndLineNumber < range.StartLineNumber || (EndLineNumber == range.StartLineNumber && EndColumn < range.StartColumn))
            return false;
        // Check if `range` is before `this`
        if (range.EndLineNumber < StartLineNumber || (range.EndLineNumber == StartLineNumber && range.EndColumn < StartColumn))
            return false;
        // These ranges must intersect
        return true;
    }

    public bool IsIntersecting(TextRange range)
    {
        // Check if `this` is before `range`
        if (EndLineNumber < range.StartLineNumber || (EndLineNumber == range.StartLineNumber && EndColumn <= range.StartColumn))
            return false;
        // Check if `range` is before `this`
        if (range.EndLineNumber < StartLineNumber || (range.EndLineNumber == StartLineNumber && range.EndColumn <= StartColumn))
            return false;
        // These ranges must intersect
        return true;
    }

    #endregion

    #region Modifications

    public TextRange PlusRange(TextRange range)
    {
        int startLineNumber, startColumn, endLineNumber, endColumn;

        if (range.StartLineNumber < StartLineNumber)
        {
            startLineNumber = range.StartLineNumber;
            startColumn = range.StartColumn;
        }
        else if (range.StartLineNumber == StartLineNumber)
        {
            startLineNumber = range.StartLineNumber;
            startColumn = Math.Min(range.StartColumn, StartColumn);
        }
        else
        {
            startLineNumber = StartLineNumber;
            startColumn = StartColumn;
        }

        if (range.EndLineNumber > EndLineNumber)
        {
            endLineNumber = range.EndLineNumber;
            endColumn = range.EndColumn;
        }
        else if (range.EndLineNumber == EndLineNumber)
        {
            endLineNumber = range.EndLineNumber;
            endColumn = Math.Max(range.EndColumn, EndColumn);
        }
        else
        {
            endLineNumber = EndLineNumber;
            endColumn = EndColumn;
        }

        return new TextRange(startLineNumber, startColumn, endLineNumber, endColumn);
    }

    public TextRange? Intersection(TextRange range)
    {
        int resultStartLineNumber = StartLineNumber;
        int resultStartColumn = StartColumn;
        int resultEndLineNumber = EndLineNumber;
        int resultEndColumn = EndColumn;

        int otherStartLineNumber = range.StartLineNumber;
        int otherStartColumn = range.StartColumn;
        int otherEndLineNumber = range.EndLineNumber;
        int otherEndColumn = range.EndColumn;

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

    public TextRange WithNewEndPosition(int endLineNumber, int endColumn)
        => new(StartLineNumber, StartColumn, endLineNumber, endColumn);

    public TextRange WithNewStartPosition(int startLineNumber, int startColumn)
        => new(startLineNumber, startColumn, EndLineNumber, EndColumn);

    public TextRange CollapseToStart()
        => new(StartLineNumber, StartColumn, StartLineNumber, StartColumn);

    public TextRange CollapseToEnd()
        => new(EndLineNumber, EndColumn, EndLineNumber, EndColumn);

    public TextRange WithLineDelta(int lineCount)
        => new(StartLineNumber + lineCount, StartColumn, EndLineNumber + lineCount, EndColumn);

    #endregion

    #region Comparisons

    public static int CompareRangesUsingStarts(TextRange a, TextRange b)
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

    public static int CompareRangesUsingEnds(TextRange a, TextRange b)
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

    #endregion

    public override string ToString()
        => $"[{StartLineNumber},{StartColumn} -> {EndLineNumber},{EndColumn}]";
}
