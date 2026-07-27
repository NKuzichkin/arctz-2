namespace ArctZ.Services.Device;

public interface IDeviceSessionFactory
{
    IDeviceSession Create(IDeviceTransport transport);
}
