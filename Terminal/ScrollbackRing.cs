using System;
using System.Collections;
using System.Collections.Generic;

namespace CxShell.Terminal;

/// <summary>
/// Bounded history of scrolled-off rows, oldest first. Evicting the oldest row from
/// a List copied every remaining reference, so a single output run cost
/// O(rows x capacity); this keeps append and eviction at O(1).
/// </summary>
internal sealed class ScrollbackRing<T> : IEnumerable<T>
{
    private readonly T[] _items;
    private readonly int _capacity;
    private int _head;
    private int _count;

    public ScrollbackRing(int capacity)
    {
        _capacity = Math.Max(0, capacity);
        _items = new T[Math.Max(1, capacity)];
    }

    /// <summary>Builds a full ring from oldest to newest, keeping the newest entries.</summary>
    public ScrollbackRing(int capacity, IReadOnlyList<T> items)
        : this(capacity)
    {
        for (var index = Math.Max(0, items.Count - _capacity); index < items.Count; index++)
            Add(items[index]);
    }

    public int Capacity => _capacity;

    public int Count => _count;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _items[Wrap(_head + index)];
        }
        set
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));

            _items[Wrap(_head + index)] = value;
        }
    }

    /// <summary>Appends a row, overwriting the oldest one when the ring is full.</summary>
    public void Add(T item)
    {
        if (_capacity == 0)
            return;

        if (_count == _capacity)
        {
            _items[_head] = item;
            _head = Wrap(_head + 1);
            return;
        }

        _items[Wrap(_head + _count)] = item;
        _count++;
    }

    public void RemoveOldest()
    {
        if (_count == 0)
            return;

        // Clear the slot so a dropped row array is not kept alive by the buffer.
        _items[_head] = default!;
        _head = Wrap(_head + 1);
        _count--;
    }

    /// <summary>Drops the newest entry; used to re-align paired rings after a resize.</summary>
    public void RemoveNewest()
    {
        if (_count == 0)
            return;

        _count--;
        _items[Wrap(_head + _count)] = default!;
    }

    public void Clear()
    {
        Array.Clear(_items, 0, _items.Length);
        _head = 0;
        _count = 0;
    }

    private int Wrap(int index) => index >= _capacity ? index - _capacity : index;

    public IEnumerator<T> GetEnumerator()
    {
        for (var index = 0; index < _count; index++)
            yield return _items[Wrap(_head + index)];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
