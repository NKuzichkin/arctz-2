using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public interface IRealtimeCommandChannel
{
    Task SendAsync(RealtimeCommand command, CancellationToken cancellationToken = default);
}
