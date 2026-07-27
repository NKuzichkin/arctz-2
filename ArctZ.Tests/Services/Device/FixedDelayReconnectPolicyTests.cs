using System;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class FixedDelayReconnectPolicyTests
{
    [Fact]
    public void MaxAttempts_ReturnsConfiguredValue()
    {
        var policy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(200));

        Assert.Equal(3, policy.MaxAttempts);
    }

    [Fact]
    public async Task WaitBeforeRetryAsync_CompletesWithoutThrowing()
    {
        var policy = new FixedDelayReconnectPolicy(maxAttempts: 3, delay: TimeSpan.FromMilliseconds(1));

        await policy.WaitBeforeRetryAsync(attemptNumber: 1);
    }
}
