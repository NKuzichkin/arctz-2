using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public sealed class ManualPeriodicTimer : IPeriodicTimer
{
    public bool IsRunning { get; private set; }
    public TimeSpan? LastInterval { get; private set; }

    public event Action? Elapsed;

    public void Start(TimeSpan interval)
    {
        IsRunning = true;
        LastInterval = interval;
    }

    public void Stop() => IsRunning = false;

    public void RaiseElapsed() => Elapsed?.Invoke();
}
