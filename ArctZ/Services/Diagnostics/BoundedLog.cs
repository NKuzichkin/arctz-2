using System;
using System.Collections.Generic;

namespace ArctZ.Services.Diagnostics;

/// <summary>
/// Fixed-size FIFO log: keeps the most recent <paramref name="capacity"/> entries and
/// silently drops older ones. Used for the diagnostic logs shown in the "О программе"
/// dialog, where only a recent tail is ever of interest and unbounded growth over a
/// long session would be a leak.
/// </summary>
public sealed class BoundedLog<T>
{
    private readonly Queue<T> _entries;
    private readonly int _capacity;

    public BoundedLog(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        _entries = new Queue<T>(capacity);
    }

    public void Add(T entry)
    {
        if (_entries.Count == _capacity)
        {
            _entries.Dequeue();
        }

        _entries.Enqueue(entry);
    }

    public void Clear() => _entries.Clear();

    /// <summary>Entries in insertion order, oldest first. Detached from the log: later adds don't change it.</summary>
    public IReadOnlyList<T> Snapshot() => _entries.ToArray();
}
