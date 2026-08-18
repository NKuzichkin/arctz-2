using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ArctZ.Services.Diagnostics;

/// <summary>
/// What the app is running on, measured at the moment the report is taken. Memory and
/// storage figures are momentary, which is why this is a captured snapshot rather than
/// the process-wide constants in <see cref="EnvironmentInfo"/>.
/// </summary>
/// <param name="CpuModel">Marketing name of the processor, or null where the platform won't say.</param>
/// <param name="TotalMemoryBytes">Physical RAM in the machine, or null where it can't be read.</param>
/// <param name="UsedMemoryBytes">Physical RAM in use system-wide, or null where it can't be read.</param>
/// <param name="ProcessMemoryBytes">Resident memory of this process.</param>
/// <param name="StorageLocation">Directory the saved programs live in, or null when they aren't on disk.</param>
public sealed record HardwareSnapshot(
    string? CpuModel,
    int LogicalProcessors,
    long? TotalMemoryBytes,
    long? UsedMemoryBytes,
    long ProcessMemoryBytes,
    string? StorageLocation,
    long? TotalStorageBytes,
    long? UsedStorageBytes);

public static class HardwareInfo
{
    /// <summary>
    /// Gathers what this platform is willing to tell us. Every probe is individually guarded:
    /// a report is most often taken when something is already wrong, so a missing registry key
    /// or an unreadable path must degrade to a dash rather than take the dialog down with it.
    /// </summary>
    public static HardwareSnapshot Capture(string? storageLocation)
    {
        var (totalMemory, usedMemory) = ReadSystemMemory();
        var (totalStorage, usedStorage) = ReadStorage(storageLocation);

        return new HardwareSnapshot(
            ReadCpuModel(),
            Environment.ProcessorCount,
            totalMemory,
            usedMemory,
            ReadProcessMemory(),
            storageLocation,
            totalStorage,
            usedStorage);
    }

    private static string? ReadCpuModel()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return ReadWindowsCpuModel();
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            {
                return ReadProcCpuInfoModel();
            }
        }
        catch
        {
            // Fall through to "unknown" — see the guarantee on Capture.
        }

        return null;
    }

    /// <summary>
    /// Reads the processor's marketing name straight out of the registry. Done through
    /// RegGetValue rather than Microsoft.Win32.Registry because this project targets plain
    /// net10.0, where those managed types are not in the reference assemblies — and a
    /// windows-specific target framework would be a heavy price for one string.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsCpuModel()
    {
        const string subKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
        const string valueName = "ProcessorNameString";

        uint byteCount = 0;
        if (RegGetValue(HkeyLocalMachine, subKey, valueName, RrfRtRegSz, 0, null, ref byteCount) != 0)
        {
            return null;
        }

        var buffer = new char[byteCount / sizeof(char)];
        if (RegGetValue(HkeyLocalMachine, subKey, valueName, RrfRtRegSz, 0, buffer, ref byteCount) != 0)
        {
            return null;
        }

        return NullIfBlank(new string(buffer).TrimEnd('\0'));
    }

    private static string? ReadProcCpuInfoModel()
    {
        // "model name" is the x86 spelling; ARM parts (every Android phone of interest)
        // publish "Hardware" instead, and some kernels only offer "Processor".
        string[] keys = { "model name", "Hardware", "Processor" };

        foreach (var wanted in keys)
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                var separator = line.IndexOf(':');
                if (separator < 0)
                {
                    continue;
                }

                if (line[..separator].Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    if (NullIfBlank(line[(separator + 1)..]) is { } value)
                    {
                        return value;
                    }
                }
            }
        }

        return null;
    }

    private static (long? Total, long? Used) ReadSystemMemory()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (GlobalMemoryStatusEx(ref status))
                {
                    var total = (long)status.TotalPhys;
                    return (total, total - (long)status.AvailPhys);
                }

                return (null, null);
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            {
                return ReadProcMemInfo();
            }
        }
        catch
        {
            // Fall through to "unknown" — see the guarantee on Capture.
        }

        return (null, null);
    }

    private static (long? Total, long? Used) ReadProcMemInfo()
    {
        long? total = null;
        long? available = null;

        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            // MemAvailable, not MemFree: the kernel's own estimate of what a new allocation
            // could get, which counts reclaimable cache. MemFree alone reads as "almost no
            // memory left" on any healthy Linux or Android system.
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                total = ParseMemInfoKilobytes(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                available = ParseMemInfoKilobytes(line);
            }

            if (total is not null && available is not null)
            {
                break;
            }
        }

        return total is { } totalBytes && available is { } availableBytes
            ? (totalBytes, totalBytes - availableBytes)
            : (total, null);
    }

    private static long? ParseMemInfoKilobytes(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var kilobytes) ? kilobytes * 1024 : null;
    }

    private static long ReadProcessMemory()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.WorkingSet64;
        }
        catch
        {
            // Process is not implemented in the browser runtime.
            return 0;
        }
    }

    private static (long? Total, long? Used) ReadStorage(string? storageLocation)
    {
        if (string.IsNullOrWhiteSpace(storageLocation))
        {
            return (null, null);
        }

        try
        {
            var drive = new DriveInfo(Path.GetFullPath(storageLocation));
            if (!drive.IsReady)
            {
                return (null, null);
            }

            // TotalFreeSpace, not AvailableFreeSpace: the report is about the volume as a
            // whole, so per-user quotas shouldn't make it look fuller than it is.
            return (drive.TotalSize, drive.TotalSize - drive.TotalFreeSpace);
        }
        catch
        {
            // Unmounted, permission-denied or simply nonexistent path.
            return (null, null);
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    // DllImport rather than the newer LibraryImport: the latter's generated marshalling code
    // requires AllowUnsafeBlocks, and loosening that for the whole project would be a poor
    // trade for two calls that only read numbers out of the OS.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static readonly nint HkeyLocalMachine = unchecked((nint)0x80000002);

    /// <summary>RRF_RT_REG_SZ — refuse anything that isn't a plain string value.</summary>
    private const uint RrfRtRegSz = 0x00000002;

    [DllImport("advapi32.dll", EntryPoint = "RegGetValueW", CharSet = CharSet.Unicode)]
    [SupportedOSPlatform("windows")]
    private static extern int RegGetValue(
        nint key,
        string subKey,
        string valueName,
        uint flags,
        nint type,
        [Out] char[]? data,
        ref uint dataByteCount);
}
