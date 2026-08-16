using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>
/// Источник доступных устройств для ConnectionViewModel. Desktop/Browser получают
/// SingleRealDeviceEndpointProvider (один эндпоинт, без поиска); Android — свою
/// реализацию поверх BluetoothAdapter (см. ArctZ.Android.AndroidBluetoothEndpointProvider).
/// </summary>
public interface IDeviceEndpointProvider
{
    /// <summary>Умеет ли платформа искать новые устройства в эфире и спаривать их.</summary>
    bool SupportsDiscovery { get; }

    /// <summary>Whether this provider can meaningfully drive name-based auto-connect
    /// (ConnectionViewModel.AutoConnectAsync) — false for providers with no real device names to
    /// match against (e.g. Browser's single synthetic "Устройство" endpoint, which can never
    /// satisfy FluidNcDeviceName.Matches). Defaults to false; platforms with real device-name
    /// discovery override it to true.</summary>
    bool SupportsAutoConnect => false;

    /// <summary>Уже известные (спаренные) устройства.</summary>
    Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default);

    /// <summary>Поиск в эфире: подписка запускает скан, dispose или естественное завершение (OnCompleted) — его конец.</summary>
    IObservable<DeviceEndpointInfo> Discover();

    /// <summary>Спаривание устройства. true — устройство спарено к моменту возврата.</summary>
    Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default);
}
