using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class RealtimeCommandChannel : IRealtimeCommandChannel
{
    private readonly IDeviceTransport _transport;

    public RealtimeCommandChannel(IDeviceTransport transport)
    {
        _transport = transport;
    }

    public Task SendAsync(RealtimeCommand command, CancellationToken cancellationToken = default) =>
        _transport.SendRawByteAsync(command.Value, cancellationToken);
}
