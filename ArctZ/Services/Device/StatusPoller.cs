using System;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class StatusPoller : IStatusPoller
{
    private readonly IRealtimeCommandChannel _realtimeChannel;
    private readonly IPeriodicTimer _timer;
    private readonly TimeSpan _interval;

    public StatusPoller(IRealtimeCommandChannel realtimeChannel, IPeriodicTimer timer, TimeSpan interval)
    {
        _realtimeChannel = realtimeChannel;
        _timer = timer;
        _interval = interval;
        _timer.Elapsed += OnElapsed;
    }

    public void Start() => _timer.Start(_interval);

    public void Stop() => _timer.Stop();

    private void OnElapsed() => _ = _realtimeChannel.SendAsync(RealtimeCommand.StatusQuery);
}
