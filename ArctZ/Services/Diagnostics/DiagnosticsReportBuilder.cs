using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ArctZ.Services.Diagnostics;

/// <summary>
/// The app state the "О программе" report describes, captured at the moment the dialog opens.
/// A plain value record rather than the live view models, so the report is a stable snapshot
/// and can be assembled — and tested — without any UI.
/// </summary>
public sealed record DiagnosticsSnapshot(
    HardwareSnapshot Hardware,
    BuildInfo Build,
    TimeSpan Uptime,
    string ConnectionState,
    string? EndpointName,
    string? FirmwareBanner,
    string ProgramName,
    int KeyPointCount,
    string PlaybackState,
    bool HasUnsavedChanges,
    IReadOnlyList<DiagnosticErrorEntry> Errors,
    IReadOnlyList<DeviceExchangeEntry> Exchange);

public static class DiagnosticsReportBuilder
{
    public static DiagnosticsReport Build(DiagnosticsSnapshot snapshot) => new(new[]
    {
        new DiagnosticsSection("Приложение", new[]
        {
            $"Название: {BuildInfo.AppName}",
            $"Версия: {snapshot.Build.Version}",
            $"Дата сборки: {FormatBuildDate(snapshot.Build.CommitDate)}",
            $"Время работы: {UptimeFormatter.Format(snapshot.Uptime)}",
        }),
        new DiagnosticsSection("Платформа", EnvironmentInfo.PlatformLines),
        new DiagnosticsSection("Оборудование", BuildHardwareLines(snapshot.Hardware)),
        new DiagnosticsSection("Среда выполнения", EnvironmentInfo.RuntimeLines),
        new DiagnosticsSection("Библиотеки", EnvironmentInfo.LibraryLines),
        new DiagnosticsSection("Подключение", new[]
        {
            $"Состояние: {snapshot.ConnectionState}",
            $"Устройство: {OrDash(snapshot.EndpointName)}",
            $"Прошивка: {OrDash(snapshot.FirmwareBanner)}",
        }),
        new DiagnosticsSection("Программа", new[]
        {
            $"Название: {snapshot.ProgramName}",
            $"Ключевых точек: {snapshot.KeyPointCount}",
            $"Воспроизведение: {snapshot.PlaybackState}",
            $"Несохранённые изменения: {(snapshot.HasUnsavedChanges ? "да" : "нет")}",
        }),
        new DiagnosticsSection("Последние ошибки", snapshot.Errors.Select(e => e.Format()).ToArray()),
        new DiagnosticsSection("Обмен с устройством", snapshot.Exchange.Select(e => e.Format()).ToArray()),
    });

    private static IReadOnlyList<string> BuildHardwareLines(HardwareSnapshot hardware) => new[]
    {
        $"Процессор: {OrDash(hardware.CpuModel)}",
        $"Ядер (логических): {hardware.LogicalProcessors}",
        $"ОЗУ: {FormatUsage(hardware.UsedMemoryBytes, hardware.TotalMemoryBytes)}",
        $"ОЗУ приложения: {ByteSizeFormatter.Format(hardware.ProcessMemoryBytes)}",
        $"Хранилище: {FormatUsage(hardware.UsedStorageBytes, hardware.TotalStorageBytes)}",
        $"Каталог программ: {OrDash(hardware.StorageLocation)}",
    };

    /// <summary>"занято X из Y (N %)", or a dash when the platform wouldn't give up the numbers.</summary>
    private static string FormatUsage(long? used, long? total)
    {
        if (used is not { } usedBytes || total is not { } totalBytes)
        {
            return EnvironmentInfo.Unknown;
        }

        var percent = ByteSizeFormatter.Percent(usedBytes, totalBytes);
        var share = percent is { } value ? $" ({value} %)" : string.Empty;

        return $"занято {ByteSizeFormatter.Format(usedBytes)} из {ByteSizeFormatter.Format(totalBytes)}{share}";
    }

    private static string FormatBuildDate(DateTimeOffset? commitDate) => commitDate is { } date
        ? date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
        : BuildInfo.UnknownVersion;

    private static string OrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? EnvironmentInfo.Unknown : value;
}
