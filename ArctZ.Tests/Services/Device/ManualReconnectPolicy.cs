using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public sealed class ManualReconnectPolicy : IReconnectPolicy
{
    private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int MaxAttempts { get; init; } = 3;

    public Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken cancellationToken = default) => _gate.Task;

    public void ReleaseCurrentWait()
    {
        var previous = _gate;
        _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }
}
