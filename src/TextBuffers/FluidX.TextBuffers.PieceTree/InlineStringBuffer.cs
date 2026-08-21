using System.Runtime.InteropServices;

namespace FluidX.TextBuffers.PieceTree;

/// <summary>
/// An append-only <see cref="char"/> buffer with cached line start positions.
/// </summary>
/// <remarks>
/// This is a <see cref="struct"/> so that a <see cref="List{T}"/> of buffers stores them inline,
/// eliminating one level of pointer indirection and per-element object headers.
/// Composes two <see cref="AppendOnlyList{T}"/> instances for chars and line starts.
/// Mutation must go through a <c>ref</c> to avoid modifying a copy.
/// </remarks>
public struct InlineStringBuffer
{
    private AppendOnlyList<char> _chars;
    private AppendOnlyList<int> _lineStarts;

    /// <summary>
    /// Creates a buffer from existing content (used for readonly original buffers loaded at construction).
    /// </summary>
    public InlineStringBuffer(string content, List<int> lineStarts)
    {
        _chars = new AppendOnlyList<char>(content.Length);
        _chars.AddRange(content.AsSpan());
        _lineStarts = new AppendOnlyList<int>(lineStarts.Count);
        _lineStarts.AddRange(CollectionsMarshal.AsSpan(lineStarts));
    }

    /// <summary>
    /// Creates an empty buffer.
    /// </summary>
    public static InlineStringBuffer CreateEmpty() => new()
    {
        _chars = AppendOnlyList<char>.CreateEmpty(),
        _lineStarts = AppendOnlyList<int>.CreateEmpty()
    };

    /// <summary>
    /// Current number of characters stored in the buffer.
    /// </summary>
    public readonly int Length => _chars.Count;

    /// <summary>
    /// Mutable list of line start offsets into the char buffer.
    /// The first element is always <c>0</c>.
    /// </summary>
    public AppendOnlyList<int> LineStarts
    {
        readonly get => _lineStarts;
        set => _lineStarts = value;
    }

    /// <summary>
    /// Gets the character at the specified index.
    /// </summary>
    public readonly char this[int index] => _chars[index];

    /// <summary>
    /// Returns a <see cref="ReadOnlySpan{T}"/> over the entire buffer content.
    /// Zero allocation — references the underlying array directly.
    /// </summary>
    public readonly ReadOnlySpan<char> GetContent() => _chars.AsSpan();

    /// <summary>
    /// Appends a span of characters to the end of the buffer.
    /// Amortized O(1) via exponential capacity growth.
    /// </summary>
    public void Append(ReadOnlySpan<char> text) => _chars.AddRange(text);

    /// <summary>
    /// Appends a single character to the end of the buffer.
    /// Amortized O(1) via exponential capacity growth.
    /// </summary>
    public void Append(char c) => _chars.Add(c);

    /// <summary>
    /// Appends text and automatically computes + appends line start offsets.
    /// Encapsulates the common pattern shared by all mutation sites in the piece tree.
    /// </summary>
    public void AppendText(ReadOnlySpan<char> text)
    {
        int startOffset = _chars.Count;
        _chars.AddRange(text);

        var newStarts = TextBuffers.PieceTree.LineStarts.CreateFast(text);
        for (int i = 0; i < newStarts.Count; i++)
            newStarts[i] += startOffset;

        _lineStarts.AddRange(CollectionsMarshal.AsSpan(newStarts).Slice(1));
    }

    /// <summary>
    /// Removes the last line start offset. O(1).
    /// Used for the rare CRLF edge case where a split \r\n boundary
    /// needs to be re-joined before appending new line starts.
    /// </summary>
    public void RemoveLastLineStart() => _lineStarts.RemoveLast();
}
