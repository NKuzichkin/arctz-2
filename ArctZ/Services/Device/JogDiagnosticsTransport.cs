using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Device;

/// <summary>
/// TEMPORARY diagnostic decorator for measuring jog overrun after the stick is released.
/// Timestamps every byte in both directions so the residual overrun can be split into its
/// parts: the wait for the ok that gates the cancel, and the firmware's deceleration ramp.
/// Remove once the measurement is done — nothing in the app depends on it.
/// </summary>
public sealed class JogDiagnosticsTransport : IDeviceTransport
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private readonly IDeviceTransport _inner;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Entries are queued rather than written inline: the read path must stay unblocked,
    /// both because jog smoothness depends on it and because a blocking write would distort the
    /// very timings being measured.</summary>
    private readonly ConcurrentQueue<string> _pending = new();

    private readonly Timer _flushTimer;
    private readonly string _path;
    private int _isFlushing;

    public JogDiagnosticsTransport(IDeviceTransport inner)
    {
        _inner = inner;
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArctZ",
            "jog-diagnostics.log");

        _inner.LineReceived += OnLineReceived;
        JogTrace.Sink = message => Log("jog", message);

        Log("session", $"log opened at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _flushTimer = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);

        // Closing the window never goes through DisconnectAsync, so without this the last couple of
        // seconds — usually the release that was being measured — never reach the file.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    public bool IsConnected => _inner.IsConnected;

    public bool IsSupported => _inner.IsSupported;

    public event Action<string>? LineReceived;

    public event Action? Disconnected
    {
        add => _inner.Disconnected += value;
        remove => _inner.Disconnected -= value;
    }

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await _inner.ConnectAsync(deviceId, cancellationToken).ConfigureAwait(false);

        // TEMPORARY: dump the firmware's settings ($120-$123 hold the per-axis acceleration) into
        // the log, so the measured decel ramp can be checked against what the machine is configured
        // for. Sent past the command queue on purpose — BufferAwareCommandQueue.Complete() ignores
        // an ok with nothing in flight, and this runs before any jog, so no jog ack can be stolen.
        // Delayed because the firmware talks over the link for a moment right after connecting.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                Log("probe", "requesting $$ (firmware settings)");
                await _inner.SendLineAsync("$$", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Diagnostics must never take the app down.
            }
        }, cancellationToken);
    }

    public Task DisconnectAsync()
    {
        Log("disconnect", string.Empty);
        Flush();
        return _inner.DisconnectAsync();
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        Log("tx", line);
        return _inner.SendLineAsync(line, cancellationToken);
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default)
    {
        // '?' is the status poll and would drown everything else at one per 100 ms.
        if (value != (byte)'?')
        {
            Log("tx-rt", $"0x{value:X2}{DescribeRealtime(value)}");
        }

        return _inner.SendRawByteAsync(value, cancellationToken);
    }

    private static string DescribeRealtime(byte value) => value switch
    {
        0x85 => " JOG-CANCEL",
        0x18 => " SOFT-RESET",
        (byte)'!' => " FEED-HOLD",
        (byte)'~' => " RESUME",
        _ => string.Empty,
    };

    private void OnLineReceived(string line)
    {
        Log("rx", line);
        LineReceived?.Invoke(line);
    }

    private void Log(string kind, string payload) =>
        _pending.Enqueue(string.Create(
            CultureInfo.InvariantCulture,
            $"{_clock.Elapsed.TotalMilliseconds,10:F1} {kind,-8} {payload}"));

    private void Flush()
    {
        // A flush that overruns its interval must not have a second one pile up behind it.
        if (Interlocked.Exchange(ref _isFlushing, 1) == 1)
        {
            return;
        }

        try
        {
            var buffer = new StringBuilder();
            while (_pending.TryDequeue(out var entry))
            {
                buffer.AppendLine(entry);
            }

            if (buffer.Length == 0)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, buffer.ToString());
        }
        catch
        {
            // Diagnostics must never take the app down.
        }
        finally
        {
            Interlocked.Exchange(ref _isFlushing, 0);
        }
    }
}
