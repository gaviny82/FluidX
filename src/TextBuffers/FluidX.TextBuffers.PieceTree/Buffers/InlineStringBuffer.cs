using System.Runtime.InteropServices;

namespace FluidX.TextBuffers.PieceTree.Buffers;

/// <summary>
/// An append-only <see cref="char"/> buffer with cached line start positions.
/// </summary>
/// <remarks>
/// This is a <see cref="struct"/> so that a <see cref="List{T}"/> of buffers stores them inline,
/// eliminating one level of pointer indirection and per-element object headers.
/// Composes two <see cref="AppendOnlyList{T}"/> instances for chars and line starts.
/// Mutation must go through a <see langword="ref"/> to avoid modifying a copy.
/// </remarks>
public struct InlineStringBuffer
{
    private AppendOnlyList<char> _chars;
    private AppendOnlyList<int> _lineStarts;

    /// <summary>
    /// Creates an empty buffer with a small pre-allocated character storage.
    /// </summary>
    /// <remarks>
    /// An explicit parameterless constructor is required for structs: without it,
    /// <c>new InlineStringBuffer()</c> zero-initializes the struct (null backing arrays)
    /// instead of running a constructor with optional parameters.
    /// </remarks>
    public InlineStringBuffer() : this(64)
    {
    }

    /// <summary>
    /// Creates an empty buffer with a pre-allocated character storage.
    /// </summary>
    /// <param name="capacity">The size of the pre-allocated character storage.</param>
    public InlineStringBuffer(int capacity)
    {
        _chars = new AppendOnlyList<char>(capacity);
        _lineStarts = new AppendOnlyList<int>();
        _lineStarts.Add(0);
    }

    /// <summary>
    /// Creates a buffer from existing content (used for readonly original buffers loaded at construction).
    /// </summary>
    public InlineStringBuffer(ReadOnlySpan<char> content, List<int> lineStarts)
    {
        _chars = new AppendOnlyList<char>(content.Length);
        _chars.AddRange(content);
        _lineStarts = new AppendOnlyList<int>(lineStarts.Count);
        _lineStarts.AddRange(CollectionsMarshal.AsSpan(lineStarts));
    }

    /// <summary>
    /// Creates a buffer that wraps existing text and line start arrays, taking ownership without copy.
    /// </summary>
    public InlineStringBuffer(char[] text, int[] lineStarts)
    {
        _chars = new AppendOnlyList<char>(text);
        _lineStarts = new AppendOnlyList<int>(lineStarts);
    }

    /// <summary>
    /// Returns a readonly view of the text buffer.
    /// </summary>
    public readonly ReadOnlySpan<char> Text => _chars.AsSpan();

    /// <summary>
    /// Line start offsets into the <see langword="char"/> buffer.
    /// </summary>
    /// <remarks>The first element is always 0.</remarks>
    public readonly ReadOnlySpan<int> LineStarts => _lineStarts.AsSpan();

    #region Append

    /// <summary>
    /// Appends text and automatically computes + appends line start offsets.
    /// Encapsulates the common pattern shared by all mutation sites in the piece tree.
    /// </summary>
    public void AppendText(ReadOnlySpan<char> text)
    {
        int startOffset = _chars.Count;
        if (startOffset > 0 && !text.IsEmpty
            && _chars[startOffset - 1] == '\r' && text[0] == '\n'
            && _lineStarts[_lineStarts.Count - 1] == startOffset)
        {
            // The trailing \r and the leading \n form a single \r\n line break.

            // Note: this means the line starts are not strictly append-only. The only behavior
            // that violates the append-only assumption is that the last line start is increased
            // by 1 when \n is appended after a trailing \r. A descriptor copy shares this slot,
            // so snapshots must preserve its original value separately: otherwise frozen piece
            // cursors can move past their captured character extent. PieceTreeSnapshot's
            // GetLineStartInBuffer accessor supplies this correction for change buffer 0.

            // AppendOnlyList provides this API internally for code reuse. Another option is to
            // create another type for this purpose or duplicate the code in this class to provide
            // a strictly append-only list.

            // FUTURE: Make string buffers (including line starts) strictly append-only.
            _lineStarts.RemoveLast();
        }

        _chars.AddRange(text);

        var newStarts = TextBuffers.PieceTree.LineStarts.CreateFast(text);
        for (int i = 0; i < newStarts.Count; i++)
            newStarts[i] += startOffset;

        _lineStarts.AddRange(CollectionsMarshal.AsSpan(newStarts).Slice(1));
    }

    #endregion
}
