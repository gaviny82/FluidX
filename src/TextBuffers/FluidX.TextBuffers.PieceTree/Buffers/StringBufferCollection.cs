using System.Runtime.InteropServices;

namespace FluidX.TextBuffers.PieceTree.Buffers;

/// <summary>
/// A wrapper around <see cref="List{T}"/> of <see cref="InlineStringBuffer"/> that provides
/// a <see cref="ref"/>-returning indexer to avoid copying the struct elements.
/// </summary>
public readonly struct StringBufferCollection
{
    private readonly List<InlineStringBuffer> _buffers;

    /// <summary>
    /// Creates an empty collection.
    /// </summary>
    public StringBufferCollection()
    {
        _buffers = [];
    }

    /// <summary>
    /// Gets a mutable reference to the buffer at the specified index.
    /// </summary>
    public ref InlineStringBuffer this[int index]
    {
        get => ref CollectionsMarshal.AsSpan(_buffers)[index];
    }

    /// <summary>
    /// Number of buffers in the collection.
    /// </summary>
    public int Count => _buffers.Count;

    /// <summary>
    /// Appends a buffer to the end of the collection.
    /// </summary>
    public void Add(InlineStringBuffer buffer) => _buffers.Add(buffer);
}
