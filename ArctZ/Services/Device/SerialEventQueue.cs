using System;
using System.Collections.Generic;

namespace ArctZ.Services.Device;

/// <summary>
/// Serializes state mutations from independent callers (timer threads, transport
/// callbacks, UI-thread calls) through a single lock-guarded queue. Whichever
/// caller enqueues also drains the queue synchronously if nothing else is
/// mid-drain, so callers observe their own action's effects immediately.
/// </summary>
public sealed class SerialEventQueue : ISerialEventQueue
{
    private readonly object _lock = new();
    private readonly Queue<Action> _queue = new();
    private bool _isDraining;

    public void Enqueue(Action action)
    {
        lock (_lock)
        {
            _queue.Enqueue(action);
            if (_isDraining)
            {
                return;
            }

            _isDraining = true;
            try
            {
                while (_queue.Count > 0)
                {
                    _queue.Dequeue()();
                }
            }
            finally
            {
                _isDraining = false;
            }
        }
    }
}
