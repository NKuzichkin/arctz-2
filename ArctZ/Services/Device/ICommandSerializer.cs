using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public interface ICommandSerializer
{
    string Serialize(IDeviceCommand command);
}
