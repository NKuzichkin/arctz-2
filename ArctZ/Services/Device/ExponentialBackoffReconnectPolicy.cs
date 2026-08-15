using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>Reconnect policy with a caller-supplied, per-attempt delay schedule (e.g. 1/2/4/8/8s).
/// MaxAttempts is simply the schedule's length — attempt N waits delays[N-1]. Used by
/// ConnectionViewModel's name-search auto-connect orchestrator (see AutoConnectAsync); the
/// existing fast DeviceSession-internal reconnect-to-known-id loop is untouched by this class.</summary>
public sealed class ExponentialBackoffReconnectPolicy : IReconnectPolicy
{
    /// <summary>Production default: 5 attempts, 1/2/4/8/8 seconds apart.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultDelays = new[]
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
    };

    private readonly IReadOnlyList<TimeSpan> _delays;

    public ExponentialBackoffReconnectPolicy(IReadOnlyList<TimeSpan> delays)
    {
        if (delays.Count == 0)
        {
            throw new ArgumentException("At least one delay is required.", nameof(delays));
        }

        _delays = delays;
    }

    public int MaxAttempts => _delays.Count;

    public Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken cancellationToken = default) =>
        Task.Delay(_delays[attemptNumber - 1], cancellationToken);
}
