using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>
/// Провайдер по умолчанию для платформ без нескольких реальных устройств
/// (Desktop, Browser): один эндпоинт "real", без сканирования, без спаривания.
/// Сохраняет поведение, которое ConnectionViewModel имел до появления
/// IDeviceEndpointProvider — Android переопределяет эту регистрацию своей.
/// </summary>
public sealed class SingleRealDeviceEndpointProvider : IDeviceEndpointProvider
{
    public const string RealDeviceId = "real";

    public bool SupportsDiscovery => false;

    public Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DeviceEndpointInfo>>(new[] { new DeviceEndpointInfo(RealDeviceId, "Устройство", true) });

    public IObservable<DeviceEndpointInfo> Discover() => Observable.Empty<DeviceEndpointInfo>();

    public Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
