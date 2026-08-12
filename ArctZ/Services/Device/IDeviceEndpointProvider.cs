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

    /// <summary>Уже известные (спаренные) устройства.</summary>
    Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default);

    /// <summary>Поиск в эфире: подписка запускает скан, dispose или естественное завершение (OnCompleted) — его конец.</summary>
    IObservable<DeviceEndpointInfo> Discover();

    /// <summary>Спаривание устройства. true — устройство спарено к моменту возврата.</summary>
    Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default);
}
