using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

public interface IReconnectPolicy
{
    int MaxAttempts { get; }

    Task WaitBeforeRetryAsync(int attemptNumber, CancellationToken cancellationToken = default);
}
