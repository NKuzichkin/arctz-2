using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class ExponentialBackoffReconnectPolicyTests
{
    [Fact]
    public void MaxAttempts_EqualsNumberOfDelays()
    {
        var policy = new ExponentialBackoffReconnectPolicy(new[]
        {
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
        });

        Assert.Equal(3, policy.MaxAttempts);
    }

    [Fact]
    public async Task WaitBeforeRetryAsync_UsesDelayForGivenAttempt()
    {
        var policy = new ExponentialBackoffReconnectPolicy(new[]
        {
            TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(60),
        });

        var stopwatch = Stopwatch.StartNew();
        await policy.WaitBeforeRetryAsync(attemptNumber: 2);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 50, $"Expected >= 50ms, was {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Constructor_EmptyDelays_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ExponentialBackoffReconnectPolicy(Array.Empty<TimeSpan>()));
    }

    [Fact]
    public void DefaultDelays_Has5EntriesEndingAt8Seconds()
    {
        Assert.Equal(5, ExponentialBackoffReconnectPolicy.DefaultDelays.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), ExponentialBackoffReconnectPolicy.DefaultDelays[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), ExponentialBackoffReconnectPolicy.DefaultDelays[1]);
        Assert.Equal(TimeSpan.FromSeconds(4), ExponentialBackoffReconnectPolicy.DefaultDelays[2]);
        Assert.Equal(TimeSpan.FromSeconds(8), ExponentialBackoffReconnectPolicy.DefaultDelays[3]);
        Assert.Equal(TimeSpan.FromSeconds(8), ExponentialBackoffReconnectPolicy.DefaultDelays[4]);
    }
}
