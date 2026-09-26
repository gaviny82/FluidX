using System.Collections;

namespace FluidX.Common.DataStructures;

/// <summary>
/// A bounded sequence whose oldest item is discarded when an append fills it.
/// Indices are logical, starting at the oldest retained item. Capacity can be changed;
/// shrinking retains the newest items.
/// </summary>
public sealed class CircularBuffer<T> : IReadOnlyList<T>
{
    private T[] _items;
    private int _head;

    public CircularBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    public int Capacity
    {
        get => _items.Length;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            if (value == _items.Length)
                return;

            T[] resized = new T[value];
            int retainedCount = Math.Min(Count, value);
            int firstRetained = Count - retainedCount;
            if (retainedCount > 0)
            {
                int start = PhysicalIndex(firstRetained);
                int firstSegment = Math.Min(retainedCount, _items.Length - start);
                Array.Copy(_items, start, resized, 0, firstSegment);
                if (firstSegment < retainedCount)
                    Array.Copy(_items, 0, resized, firstSegment, retainedCount - firstSegment);
            }

            _items = resized;
            _head = 0;
            Count = retainedCount;
        }
    }
    public int Count { get; private set; }
    public T Last => Count == 0 ? throw new InvalidOperationException("The buffer is empty.") : this[Count - 1];

    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _items[PhysicalIndex(index)];
        }
    }

    /// <summary>Appends an item and returns whether the oldest item was evicted.</summary>
    public bool Append(T item)
    {
        if (Count < Capacity)
        {
            _items[PhysicalIndex(Count++)] = item;
            return false;
        }

        _items[_head] = item;
        _head = (_head + 1) % Capacity;
        return true;
    }

    /// <summary>Discards items from the end until only <paramref name="count"/> remain.</summary>
    public void Truncate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Count);
        for (int i = count; i < Count; i++)
            _items[PhysicalIndex(i)] = default!;
        Count = count;
        if (Count == 0) _head = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int PhysicalIndex(int index) => (_head + index) % Capacity;
}
