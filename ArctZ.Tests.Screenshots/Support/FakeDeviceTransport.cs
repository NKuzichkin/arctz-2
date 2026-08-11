using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Screenshots.Support;

public sealed class FakeDeviceTransport : IDeviceTransport
{
    public List<string> SentLines { get; } = new();
    public List<byte> SentRawBytes { get; } = new();
    public bool IsConnected { get; private set; }

    public int ConnectFailuresRemaining { get; set; }

    public event Action<string>? LineReceived;
    public event Action? Disconnected;

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (ConnectFailuresRemaining > 0)
        {
            ConnectFailuresRemaining--;
            throw new InvalidOperationException("Simulated connect failure");
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        SentLines.Add(line);
        return Task.CompletedTask;
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        SentRawBytes.Add(value);
        return Task.CompletedTask;
    }

    public void SimulateReceivedLine(string line) => LineReceived?.Invoke(line);

    public void SimulateDisconnect()
    {
        IsConnected = false;
        Disconnected?.Invoke();
    }
}
