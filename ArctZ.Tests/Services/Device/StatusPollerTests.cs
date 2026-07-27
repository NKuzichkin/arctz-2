using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class StatusPollerTests
{
    private readonly FakeDeviceTransport _transport = new();
    private readonly ManualPeriodicTimer _timer = new();
    private readonly StatusPoller _poller;

    public StatusPollerTests()
    {
        _poller = new StatusPoller(new RealtimeCommandChannel(_transport), _timer, TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void Start_StartsTimerAtConfiguredInterval()
    {
        _poller.Start();

        Assert.True(_timer.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(250), _timer.LastInterval);
    }

    [Fact]
    public void Tick_SendsStatusQueryByte()
    {
        _poller.Start();

        _timer.RaiseElapsed();

        Assert.Equal(new byte[] { (byte)'?' }, _transport.SentRawBytes);
    }

    [Fact]
    public void Stop_StopsTimer()
    {
        _poller.Start();

        _poller.Stop();

        Assert.False(_timer.IsRunning);
    }
}
