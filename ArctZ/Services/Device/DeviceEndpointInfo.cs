namespace ArctZ.Services.Device;

/// <summary>Одно устройство, о котором знает IDeviceEndpointProvider — уже известное или найденное сканом.</summary>
public sealed record DeviceEndpointInfo(string Id, string Name, bool IsPaired);
