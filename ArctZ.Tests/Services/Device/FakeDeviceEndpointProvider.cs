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

    /// <summary>Defaults to true (NOT the interface's false default) so every existing test that
    /// exercises AutoConnectAsync through this fake keeps working unchanged; tests that need the
    /// opt-out path set it to false explicitly.</summary>
    public bool SupportsAutoConnect { get; set; } = true;
    public List<DeviceEndpointInfo> KnownEndpoints { get; set; } = new();
    public Exception? GetKnownEndpointsException { get; set; }
    public Exception? PairException { get; set; }
    public bool PairResult { get; set; } = true;
    public List<string> PairedIds { get; } = new();
    public Subject<DeviceEndpointInfo> DiscoverySubject { get; } = new();

    /// <summary>
    /// When set, Discover() returns this instead of DiscoverySubject — lets tests exercise a
    /// provider whose Discover() completes/errors synchronously on subscribe (e.g.
    /// Observable.Empty, matching SingleRealDeviceEndpointProvider/AndroidBluetoothEndpointProvider
    /// with no adapter), which DiscoverySubject (a Subject driven explicitly after Execute()
    /// returns) cannot reproduce.
    /// </summary>
    public Func<IObservable<DeviceEndpointInfo>>? DiscoverOverride { get; set; }

    /// <summary>Number of times Discover() has been called — lets tests prove a scan was
    /// actually (re)started rather than just observing IsScanning, which can be
    /// true-then-false again within the same synchronous call when the underlying
    /// observable completes immediately (e.g. Observable.Empty under ImmediateScheduler).</summary>
    public int DiscoverCallCount { get; private set; }

    public Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default)
    {
        if (GetKnownEndpointsException is not null)
        {
            throw GetKnownEndpointsException;
        }

        return Task.FromResult<IReadOnlyList<DeviceEndpointInfo>>(KnownEndpoints);
    }

    public IObservable<DeviceEndpointInfo> Discover()
    {
        DiscoverCallCount++;
        return DiscoverOverride?.Invoke() ?? DiscoverySubject;
    }

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
