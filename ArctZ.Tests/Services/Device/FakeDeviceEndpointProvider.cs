using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public sealed class FakeDeviceEndpointProvider : IDeviceEndpointProvider
{
    public bool SupportsDiscovery { get; set; } = true;
    public List<DeviceEndpointInfo> KnownEndpoints { get; set; } = new();
    public Exception? GetKnownEndpointsException { get; set; }
    public Exception? PairException { get; set; }
    public bool PairResult { get; set; } = true;
    public List<string> PairedIds { get; } = new();
    public Subject<DeviceEndpointInfo> DiscoverySubject { get; } = new();

    public Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default)
    {
        if (GetKnownEndpointsException is not null)
        {
            throw GetKnownEndpointsException;
        }

        return Task.FromResult<IReadOnlyList<DeviceEndpointInfo>>(KnownEndpoints);
    }

    public IObservable<DeviceEndpointInfo> Discover() => DiscoverySubject;

    public Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (PairException is not null)
        {
            throw PairException;
        }

        if (PairResult)
        {
            PairedIds.Add(deviceId);
        }

        return Task.FromResult(PairResult);
    }
}
