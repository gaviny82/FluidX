namespace FluidX.TextBuffers.PieceTree.Buffers;

/// <summary>
/// A generic, append-only list backed by a contiguous array with exponential growth.
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
/// <remarks>
/// This is a <see cref="struct"/> for inline storage in arrays or parent structs,
/// eliminating per-instance object headers. Mutation must go through a <see langword="ref"/>
/// to avoid modifying a copy.
/// </remarks>
public struct AppendOnlyList<T>
{
    private T[] _items;
    private int _count;

    /// <summary>
    /// Creates an empty list with a small initial allocation.
    /// </summary>
    /// <remarks>
    /// An explicit parameterless constructor is required for structs: without it,
    /// <c>new AppendOnlyList&lt;T&gt;()</c> zero-initializes the struct (null backing array)
    /// instead of running a constructor with optional parameters.
    /// </remarks>
    public AppendOnlyList() : this(8)
    {
    }

    /// <summary>
    /// Creates an empty list with the specified initial capacity.
    /// </summary>
    /// <param name="capacity">Initial capacity of the underlying array.</param>
    public AppendOnlyList(int capacity)
    {
        _items = new T[capacity];
        _count = 0;
    }

    /// <summary>
    /// Creates a list that wraps an existing array, taking ownership of it (no copy).
    /// </summary>
    /// <param name="items">The array to use as the backing storage. Must not be mutated afterwards.</param>
    public AppendOnlyList(T[] items)
    {
        _items = items;
        _count = items.Length;
    }

    /// <summary>
    /// Current number of elements.
    /// </summary>
    public readonly int Count => _count;

    /// <summary>
    /// Current capacity of the underlying array.
    /// </summary>
    public readonly int Capacity => _items.Length;

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    public readonly T this[int index] => _items[index];

    /// <summary>
    /// Returns a <see cref="ReadOnlySpan{T}"/> over the valid elements.
    /// Zero allocation — references the underlying array directly.
    /// </summary>
    public readonly ReadOnlySpan<T> AsSpan() => _items.AsSpan(0, _count);

    /// <summary>
    /// Returns a <see cref="ReadOnlySpan{T}"/> over a slice of the valid elements.
    /// Zero allocation — references the underlying array directly.
    /// </summary>
    public readonly ReadOnlySpan<T> Slice(int start, int length) => _items.AsSpan(start, length);

    /// <summary>
    /// Appends a single element. Amortized O(1) via exponential growth.
    /// </summary>
    public void Add(T item)
    {
        EnsureCapacity(_count + 1);
        _items[_count] = item;
        _count++;
    }

    /// <summary>
    /// Appends a span of elements. Amortized O(1) via exponential growth.
    /// </summary>
    public void AddRange(ReadOnlySpan<T> items)
    {
        if (items.IsEmpty)
            return;

        EnsureCapacity(_count + items.Length);
        items.CopyTo(_items.AsSpan(_count));
        _count += items.Length;
    }

    /// <summary>
    /// Removes the last element. O(1).
    /// Does not clear the slot — acceptable for value types (<c>int</c>, <c>char</c>).
    /// </summary>
    public void RemoveLast()
    {
        _count--;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _items.Length)
            return;

        int newCapacity = Math.Max(_items.Length * 2, required);
        newCapacity = Math.Max(newCapacity, 4);

        T[] newItems = new T[newCapacity];
        _items.AsSpan(0, _count).CopyTo(newItems);
        _items = newItems;
    }
}
