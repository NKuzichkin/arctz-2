using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>Per spec: 3 attempts, 200ms apart, then give up.</summary>
public sealed class FixedDelayReconnectPolicy : IReconnectPolicy
{
    private readonly TimeSpan _delay;

    public FixedDelayReconnectPolicy(int maxAttempts, TimeSpan delay)
    {
        MaxAttempts = maxAttempts;
        _delay = delay;
    }

    public int MaxAttempts { get; }

    public Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken cancellationToken = default) =>
        Task.Delay(_delay, cancellationToken);
}
