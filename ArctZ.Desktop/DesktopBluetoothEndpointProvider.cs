using System;
using System.Collections.Generic;
using System.Management;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Desktop;

/// <summary>
/// IDeviceEndpointProvider for Desktop: enumerates already-paired Bluetooth SPP COM ports
/// through WMI, so ConnectionViewModel's name-based auto-connect (FluidNcDeviceName.Matches)
/// has a real device name to match against. Replaces the default SingleRealDeviceEndpointProvider
/// (registered by AddArctZCore()), which only exposed a single synthetic "Устройство" endpoint
/// with no real name — auto-connect by name was impossible on Desktop before this. Pairing new
/// devices is left to Windows Bluetooth Settings, same as SingleRealDeviceEndpointProvider before it.
/// </summary>
public sealed class DesktopBluetoothEndpointProvider : IDeviceEndpointProvider
{
    // Win32_PnPEntity.Name for a Bluetooth SPP COM port looks like "FluidNC (COM5)" — the
    // friendly (paired) device name followed by the assigned port in parentheses.
    private static readonly Regex ComPortNamePattern = new(@"^(?<friendly>.+)\s\((?<port>COM\d+)\)$", RegexOptions.Compiled);

    public bool SupportsDiscovery => false;

    public Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceEndpointInfo>();

#pragma warning disable CA1416 // WMI is Windows-only; ArctZ.Desktop only ships on Windows.
        using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
        using var devices = searcher.Get();
        foreach (var device in devices)
        {
            using (device)
            {
                if (device["Name"] is not string name)
                {
                    continue;
                }

                var match = ComPortNamePattern.Match(name);
                if (!match.Success)
                {
                    continue;
                }

                result.Add(new DeviceEndpointInfo(match.Groups["port"].Value, match.Groups["friendly"].Value.Trim(), true));
            }
        }
#pragma warning restore CA1416

        return Task.FromResult<IReadOnlyList<DeviceEndpointInfo>>(result);
    }

    public IObservable<DeviceEndpointInfo> Discover() => Observable.Empty<DeviceEndpointInfo>();

    public Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
