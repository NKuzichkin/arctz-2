using System;
using System.Threading;

namespace ArctZ.Services.Device;

public sealed class SystemPeriodicTimer : IPeriodicTimer, IDisposable
{
    private readonly Timer _timer;

    public event Action? Elapsed;

    public SystemPeriodicTimer()
    {
        _timer = new Timer(_ => Elapsed?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(TimeSpan interval) => _timer.Change(interval, interval);

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void Dispose() => _timer.Dispose();
}
