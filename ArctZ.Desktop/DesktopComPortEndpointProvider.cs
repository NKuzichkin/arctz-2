using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Device;

namespace ArctZ.Desktop;

/// <summary>
/// IDeviceEndpointProvider for Desktop: probes every available COM port for a live FluidNC
/// response instead of trusting a paired-device friendly name. Replaces an earlier WMI-based
/// approach (Win32_PnPEntity "FriendlyName (COMx)" parsing) that real-hardware testing showed
/// unreliable — Windows does not consistently expose a usable friendly name for a paired
/// Bluetooth SPP port. Opens each port at 115200 (matching DesktopSerialTransport), sends the
/// realtime status-query byte '?' (the same byte StatusPoller sends once connected), and waits
/// up to ProbeTimeout for a "&lt;...&gt;" status-report line (FluidNcStatusParser recognizes this
/// as StatusReportLine) — the strongest available signal that a real FluidNC/GRBL controller is
/// on the other end, as opposed to "ok"/"error:" which some other serial device could
/// coincidentally emit. Pairing/discovery beyond this probe is not supported — Windows Bluetooth
/// Settings still owns pairing new devices, same as before this class existed.
/// </summary>
public sealed class DesktopComPortEndpointProvider : IDeviceEndpointProvider
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1.5);
    private const int BaudRate = 115200;

    public bool SupportsDiscovery => false;

    public bool SupportsAutoConnect => true;

    public async Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var probes = SerialPort.GetPortNames().Select(port => ProbePortAsync(port, cancellationToken));
        var results = await Task.WhenAll(probes).ConfigureAwait(false);
        return results.Where(r => r is not null).Select(r => r!).ToList();
    }

    public IObservable<DeviceEndpointInfo> Discover() => Observable.Empty<DeviceEndpointInfo>();

    public Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default) => Task.FromResult(true);

    private static Task<DeviceEndpointInfo?> ProbePortAsync(string portName, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var port = new SerialPort(portName, BaudRate) { NewLine = "\n", ReadTimeout = (int)ProbeTimeout.TotalMilliseconds };

            try
            {
                port.Open();
            }
            catch
            {
                // Busy, permission denied, or no device present — not a candidate.
                return null;
            }

            try
            {
                port.Write("?");
                var deadline = DateTime.UtcNow + ProbeTimeout;

                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string line;
                    try
                    {
                        line = port.ReadLine();
                    }
                    catch (TimeoutException)
                    {
                        break;
                    }

                    var trimmed = line.Trim();
                    if (trimmed.Length > 0 && trimmed.StartsWith('<') && trimmed.EndsWith('>'))
                    {
                        return new DeviceEndpointInfo(portName, "FluidNC", true);
                    }
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
            finally
            {
                try
                {
                    if (port.IsOpen)
                    {
                        port.Close();
                    }
                }
                catch
                {
                    // Best-effort cleanup — the `using` statement still disposes the object.
                }
            }
        }, cancellationToken);
}
