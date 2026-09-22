using System.Text;

namespace FluidX.TextBuffers.PersistentPieceTree;

// Summaries form an associative concatenation operation, including CRLF seams.
internal readonly record struct TextSummary(int Length, int Breaks, char First, char Last)
{
    public static TextSummary operator +(TextSummary a, TextSummary b)
    {
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        return new(checked(a.Length + b.Length), a.Breaks + b.Breaks -
            (a.Last == '\r' && b.First == '\n' ? 1 : 0), a.First, b.Last);
    }
}

// Original/large insertions retain immutable strings. Small edits use fixed append-only
// chunks. Published characters and index entries never change; arrays never resize.
// Only the writer reads the mutable lengths. Pieces capture an index bound, so readers
// never inspect unpublished cells, even while the writer appends to the same chunk.
internal sealed class TextStorage
{
    private readonly string? _text;
    private readonly char[]? _characters;
    private readonly int[] _breakStarts;
    internal int Length { get; private set; }
    internal int BreakCount { get; private set; }
    internal int Available => (_characters?.Length ?? Length) - Length;

    internal TextStorage(string text)
    {
        _text = text;
        Length = text.Length;
        var breaks = new List<int>();
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\r' || (text[i] == '\n' && (i == 0 || text[i - 1] != '\r')))
                breaks.Add(i);
        _breakStarts = breaks.ToArray();
        BreakCount = _breakStarts.Length;
    }

    internal TextStorage(int capacity)
    {
        _characters = new char[capacity];
        _breakStarts = new int[capacity];
    }

    // Writer-only. The returned piece is published only after Append completes.
    internal Piece Append(string text)
    {
        int start = Length;
        text.AsSpan().CopyTo(_characters.AsSpan(start));
        for (int i = start; i < start + text.Length; i++)
            if (_characters![i] == '\r' || (_characters[i] == '\n' && (i == 0 || _characters[i - 1] != '\r')))
                _breakStarts[BreakCount++] = i;
        Length += text.Length;
        return new(this, start, text.Length);
    }

    internal char CharAt(int offset) => _text is null ? _characters![offset] : _text[offset];
    internal int BreakStart(int index) => _breakStarts[index];
    internal void CopyTo(int start, Span<char> destination)
    {
        if (_text is null) _characters.AsSpan(start, destination.Length).CopyTo(destination);
        else _text.AsSpan(start, destination.Length).CopyTo(destination);
    }

    internal int LowerBound(int offset, int limit)
    {
        int index = Array.BinarySearch(_breakStarts, 0, limit, offset);
        return index < 0 ? ~index : index;
    }
}

internal readonly struct Piece
{
    internal TextStorage Storage { get; }
    internal int Start { get; }
    internal int Length { get; }
    internal TextSummary Summary { get; }
    private readonly int _firstBreak;
    private readonly int _breakLimit;
    private readonly bool _leadingLf;

    // Used only by the writer, which owns the storage's current append position.
    internal Piece(TextStorage storage, int start, int length) : this(storage, start, length, storage.BreakCount) { }

    private Piece(TextStorage storage, int start, int length, int breakLimit)
    {
        Storage = storage;
        Start = start;
        Length = length;
        _breakLimit = breakLimit;
        _firstBreak = storage.LowerBound(start, breakLimit);
        _leadingLf = length > 0 && start > 0 && storage.CharAt(start) == '\n' && storage.CharAt(start - 1) == '\r';
        Summary = length == 0 ? default : new(length,
            storage.LowerBound(start + length, breakLimit) - _firstBreak + (_leadingLf ? 1 : 0),
            storage.CharAt(start), storage.CharAt(start + length - 1));
    }

    internal Piece Slice(int start, int length) => new(Storage, Start + start, length, _breakLimit);
    internal int BreakStart(int index) => _leadingLf && index == 0 ? 0 :
        Storage.BreakStart(_firstBreak + index - (_leadingLf ? 1 : 0)) - Start;
}
