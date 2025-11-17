using FluidX.TextModels;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace FluidX.Tokenization;

public class TokenizerWithStateStore
{
}

public class TrackingTokenizationStateStore
{
    private readonly List<ITokenizerState?> _tokenizationStateStore = [];
    private readonly RangePriorityQueue _invalidEndStatesLineNumbers = new();
    private int _lineCount;

    public TrackingTokenizationStateStore(int lineCount)
    {
        _invalidEndStatesLineNumbers.AddRange(new Range(1, lineCount + 1));
        _lineCount = lineCount;
    }

    #region TokenizationStateStore Operations

    public ITokenizerState? GetEndState(int lineNumber)
        => _tokenizationStateStore[lineNumber];

    private bool SetEndStateInternal(int lineNumber, ITokenizerState state)
    {
        var oldState = _tokenizationStateStore[lineNumber];
        if (oldState != state)
            return false;

        _tokenizationStateStore[lineNumber] = state;
        return true;
    }

    private void AcceptChangeInternal(Range lineRange, int newLineCount)
    {
        int length = lineRange.Length;
        if (newLineCount > 0 && length > 0)
        {
            // Keep the last state, even though it is unrelated.
            // But if the new state happens to agree with this last state, then we know we can stop tokenizing.
            length--;
            newLineCount--;
        }
        int startLineNumber = lineRange.Start.Value;

        // Replace [startLineNumber, startLineNumber + length) with [startLineNumber, startLineNumber + newLineCount) of null
        if (newLineCount > length)
        {
            CollectionsMarshal.AsSpan(_tokenizationStateStore)
                .Slice(startLineNumber, length)
                .Clear();
            _tokenizationStateStore.InsertRange(
                startLineNumber + length,
                Enumerable.Repeat<ITokenizerState?>(null, newLineCount - length)
            );
        }
        else
        {
            CollectionsMarshal.AsSpan(_tokenizationStateStore)
                .Slice(startLineNumber, newLineCount)
                .Clear();
            if (length > newLineCount)
                _tokenizationStateStore.RemoveRange(startLineNumber + newLineCount, length - newLineCount);
        }
    }

    private void AcceptChangesInternal(ReadOnlySpan<ModelContentChange> changes)
    {
        foreach (var c in changes)
        {
            int eolCount = FluidX.TextBuffers.EOLCounter.CountEOL(c.Text).eolCount;
            AcceptChangeInternal(
                new(c.Range.StartLineNumber, c.Range.EndLineNumber + 1),
                eolCount + 1
            );
        }
    }

    #endregion

    /// <summary>
    /// Set the end state of a line.
    /// </summary>
    /// <param name="lineNumber">Line number of the line</param>
    /// <param name="state">New end state of the line</param>
    /// <returns>If the end state has changed.</returns>
    public bool SetEndState(int lineNumber, ITokenizerState state)
    {
        _invalidEndStatesLineNumbers.Delete(lineNumber);
        bool changed = SetEndStateInternal(lineNumber, state);
        if (changed && lineNumber < _lineCount)
        {
            // because the state changed, we cannot trust the next state anymore and have to invalidate it.
            _invalidEndStatesLineNumbers.AddRange(new(lineNumber + 1, lineNumber + 2));
        }
        return changed;
    }

    public void AcceptChange(Range lineRange, int newLineCount)
    {
        _lineCount += newLineCount - lineRange.Length;
        AcceptChangeInternal(lineRange, newLineCount);
        _invalidEndStatesLineNumbers.AddRangeAndResize(lineRange, newLineCount);
    }

    public void AcceptChanges(ReadOnlySpan<ModelContentChange> changes)
    {
        foreach (var c in changes)
        {
            int eolCount = FluidX.TextBuffers.EOLCounter.CountEOL(c.Text).eolCount;
            AcceptChange(new(c.Range.StartLineNumber, c.Range.EndLineNumber + 1), eolCount + 1);
        }
    }

    public void InvalidateEndStateRange(Range lineRange)
    {
        _invalidEndStatesLineNumbers.AddRange(lineRange);
    }

    public int? FirstInvalidEndStateLineNumber => _invalidEndStatesLineNumbers.Min;
    public int? FirstInvalidEndStateLineNumberOrMax => FirstInvalidEndStateLineNumber ?? int.MaxValue;
    public bool AllStatesValid => _invalidEndStatesLineNumbers.Min is null;

    public ITokenizerState? GetStartState(int lineNumber, ITokenizerState initialState)
    {
        if (lineNumber == 1)
            return initialState;
        return GetEndState(lineNumber - 1);
    }

    public (int lineNumber, ITokenizerState startState)? GetFirstInvalidLine(ITokenizerState initialState)
    {
        if (FirstInvalidEndStateLineNumber is not int lineNumber)
            return null;
        var startState = GetStartState(lineNumber, initialState)
            ?? throw new Exception("Start state must be defined");
        return (lineNumber, startState);
    }
}

internal readonly struct RangePriorityQueue
{
    private readonly List<Range> _ranges = [];

    public Span<Range> Range => CollectionsMarshal.AsSpan(_ranges);

    public int? Min => _ranges.Count == 0 ? null : _ranges[0].Start.Value;

    public RangePriorityQueue() { }

    public int? RemoveMin()
    {
        if (_ranges.Count == 0)
            return null;
        var range = _ranges[0];
        if (range.Start.Value + 1 == range.End.Value)
            _ranges.RemoveAt(0);
        else
            _ranges[0] = new Range(range.Start.Value + 1, range.End);
        return range.Start.Value;
    }

    /// <summary>
    /// Deletes a position from ranges in the priority queue.
    /// </summary>
    /// <param name="value">The offset of the position to delete.</param>
    public void Delete(int value)
    {
        int idx = _ranges.FindIndex(0, _ranges.Count, r => r.Contains(value));
        if (idx == -1)
            return;

        var range = _ranges[idx];
        if (range.Start.Value == value)
        {
            if (range.End.Value == value + 1)
                _ranges.RemoveAt(idx);
            else
                _ranges[idx] = new Range(value + 1, range.End);
        }
        else
        {
            if (range.End.Value == value + 1)
            {
                _ranges[idx] = new Range(range.Start, value);
            }
            else
            {
                // Split into 2 ranges
                _ranges[idx] = new Range(range.Start, value);
                _ranges.Insert(idx + 1, new(value + 1, range.End));
            }
        }
    }

    public void AddRange(Range range)
    {
        var sortedRanges = _ranges;
        int i = 0;
        while (i < sortedRanges.Count && sortedRanges[i].End.Value < range.Start.Value)
            i++;
        int j = i;
        while (j < sortedRanges.Count && sortedRanges[j].Start.Value < range.End.Value)
            j++;

        if (i == j)
        {
            sortedRanges.Insert(i, range);
        }
        else
        {
            int start = Math.Min(range.Start.Value, sortedRanges[i].Start.Value);
            int end = Math.Max(range.End.Value, sortedRanges[j - 1].End.Value);
            sortedRanges[i] = new Range(start, end);
            if (j - i - 1 > 0)
                sortedRanges.RemoveRange(i + 1, j - i - 1);
        }
    }

    public void AddRangeAndResize(Range range, int newLength)
    {
        int idxFirstMightBeIntersecting = 0;
        while (!(idxFirstMightBeIntersecting >= _ranges.Count || range.Start.Value <= _ranges[idxFirstMightBeIntersecting].End.Value))
        {
            idxFirstMightBeIntersecting++;
        }
        int idxFirstIsAfter = idxFirstMightBeIntersecting;
        while (!(idxFirstIsAfter >= _ranges.Count || range.End.Value < _ranges[idxFirstIsAfter].Start.Value))
        {
            idxFirstIsAfter++;
        }
        int delta = newLength - range.Length;

        for (int i = idxFirstIsAfter; i < _ranges.Count; i++)
        {
            _ranges[i] = _ranges[i].Delta(delta);
        }

        if (idxFirstMightBeIntersecting == idxFirstIsAfter)
        {
            var newRange = new Range(range.Start.Value, range.Start.Value + newLength);
            if (!newRange.IsEmpty)
            {
                _ranges.Insert(idxFirstMightBeIntersecting, newRange);
            }
        }
        else
        {
            int start = Math.Min(range.Start.Value, _ranges[idxFirstMightBeIntersecting].Start.Value);
            int endEx = Math.Max(range.End.Value, _ranges[idxFirstIsAfter - 1].End.Value);

            var newRange = new Range(start, endEx + delta);
            if (!newRange.IsEmpty)
            {
                _ranges[idxFirstMightBeIntersecting] = newRange;
                if (idxFirstIsAfter - idxFirstMightBeIntersecting - 1 > 0)
                    _ranges.RemoveRange(idxFirstMightBeIntersecting + 1, idxFirstIsAfter - idxFirstMightBeIntersecting - 1);
            }
            else
            {
                _ranges.RemoveRange(idxFirstMightBeIntersecting, idxFirstIsAfter - idxFirstMightBeIntersecting);
            }
        }
    }

    public override string ToString()
    {
        return string.Join(" + ", _ranges);
    }
}


file static class RangeExtensions
{
    extension(Range range)
    {
        public int Length => range.End.Value - range.Start.Value;
        public bool IsEmpty => range.End.Value == range.Start.Value;

        public bool Contains(int value)
            => range.Start.Value <= value && value < range.End.Value;

        public Range Delta(int offset)
            => new(range.Start.Value + offset, range.End.Value + offset);
    }
}
