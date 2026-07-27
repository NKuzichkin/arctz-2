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

    /// <summary>
    /// Raised when an enqueued action throws. The exception is swallowed rather
    /// than propagated: callers include System.Threading.Timer callbacks
    /// (JogScheduler, StatusPoller) where an escaping exception crashes the
    /// process and strands the rest of the queue.
    /// </summary>
    public event Action<Exception>? UnhandledException;

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
                    var next = _queue.Dequeue();
                    try
                    {
                        next();
                    }
                    catch (Exception ex)
                    {
                        UnhandledException?.Invoke(ex);
                    }
                }
            }
            finally
            {
                _isDraining = false;
            }
        }
    }
}
