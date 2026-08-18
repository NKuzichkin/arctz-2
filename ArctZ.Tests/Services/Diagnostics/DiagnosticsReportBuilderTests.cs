using System;
using System.Linq;
using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class DiagnosticsReportBuilderTests
{
    private static readonly DateTimeOffset CommitAt = new(2026, 8, 17, 22, 44, 2, TimeSpan.FromHours(3));

    private static DiagnosticsSnapshot Snapshot(
        BuildInfo? build = null,
        TimeSpan? uptime = null,
        string connectionState = "Подключено",
        string? endpointName = "FluidNC (COM5)",
        string? firmwareBanner = "Grbl 3.7 [FluidNC v3.7.0]",
        string programName = "Проезд по столу",
        int keyPointCount = 4,
        string playbackState = "Остановлена",
        bool hasUnsavedChanges = false,
        DiagnosticErrorEntry[]? errors = null,
        DeviceExchangeEntry[]? exchange = null) =>
        new(
            build ?? BuildInfo.Create("7bf6f5f-dirty", "1.0.0.0", CommitAt.ToString("o")),
            uptime ?? new TimeSpan(0, 5, 3),
            connectionState,
            endpointName,
            firmwareBanner,
            programName,
            keyPointCount,
            playbackState,
            hasUnsavedChanges,
            errors ?? Array.Empty<DiagnosticErrorEntry>(),
            exchange ?? Array.Empty<DeviceExchangeEntry>());

    private static string[] SectionLines(DiagnosticsReport report, string title) =>
        report.Sections.Single(s => s.Title == title).Lines.ToArray();

    [Fact]
    public void Build_OrdersSectionsFromTheAppOutwards()
    {
        var report = DiagnosticsReportBuilder.Build(Snapshot());

        Assert.Equal(
            new[]
            {
                "Приложение",
                "Платформа",
                "Среда выполнения",
                "Библиотеки",
                "Подключение",
                "Программа",
                "Последние ошибки",
                "Обмен с устройством",
            },
            report.Sections.Select(s => s.Title).ToArray());
    }

    [Fact]
    public void Build_DescribesTheApplication()
    {
        var report = DiagnosticsReportBuilder.Build(Snapshot());

        var lines = SectionLines(report, "Приложение");
        Assert.Contains("Название: ArctZ", lines);
        Assert.Contains("Версия: 7bf6f5f-dirty", lines);
        Assert.Contains("Дата сборки: 17.08.2026 22:44", lines);
        Assert.Contains("Время работы: 5 мин 3 с", lines);
    }

    [Fact]
    public void Build_SaysSoWhenTheBuildDateWasNotStamped()
    {
        var report = DiagnosticsReportBuilder.Build(
            Snapshot(build: BuildInfo.Create("7bf6f5f", "1.0.0.0", null)));

        Assert.Contains("Дата сборки: неизвестно", SectionLines(report, "Приложение"));
    }

    [Fact]
    public void Build_DescribesTheConnection()
    {
        var report = DiagnosticsReportBuilder.Build(Snapshot());

        var lines = SectionLines(report, "Подключение");
        Assert.Contains("Состояние: Подключено", lines);
        Assert.Contains("Устройство: FluidNC (COM5)", lines);
        Assert.Contains("Прошивка: Grbl 3.7 [FluidNC v3.7.0]", lines);
    }

    [Fact]
    public void Build_MarksAnUnknownDeviceAndFirmwareRatherThanOmittingThem()
    {
        var report = DiagnosticsReportBuilder.Build(Snapshot(endpointName: null, firmwareBanner: null));

        var lines = SectionLines(report, "Подключение");
        Assert.Contains("Устройство: —", lines);
        Assert.Contains("Прошивка: —", lines);
    }

    [Fact]
    public void Build_DescribesTheLoadedProgram()
    {
        var report = DiagnosticsReportBuilder.Build(Snapshot(hasUnsavedChanges: true));

        var lines = SectionLines(report, "Программа");
        Assert.Contains("Название: Проезд по столу", lines);
        Assert.Contains("Ключевых точек: 4", lines);
        Assert.Contains("Воспроизведение: Остановлена", lines);
        Assert.Contains("Несохранённые изменения: да", lines);
    }

    [Fact]
    public void Build_FormatsRecordedErrors()
    {
        var report = DiagnosticsReportBuilder.Build(Snapshot(errors: new[]
        {
            new DiagnosticErrorEntry(CommitAt, DiagnosticErrorKind.Alarm, "Авария FluidNC: код 1"),
        }));

        Assert.Equal(new[] { "22:44:02 [авария] Авария FluidNC: код 1" }, SectionLines(report, "Последние ошибки"));
    }

    [Fact]
    public void Build_FormatsTheRecordedExchange()
    {
        var report = DiagnosticsReportBuilder.Build(Snapshot(exchange: new[]
        {
            new DeviceExchangeEntry(CommitAt, DeviceExchangeDirection.Sent, "G0 X10"),
            new DeviceExchangeEntry(CommitAt, DeviceExchangeDirection.Received, "error:9"),
        }));

        Assert.Equal(
            new[] { "22:44:02 → G0 X10", "22:44:02 ← error:9" },
            SectionLines(report, "Обмен с устройством"));
    }

    [Fact]
    public void Build_LeavesTheLogSectionsEmptyWhenNothingWasRecorded()
    {
        var report = DiagnosticsReportBuilder.Build(Snapshot());

        Assert.Empty(SectionLines(report, "Последние ошибки"));
        Assert.Empty(SectionLines(report, "Обмен с устройством"));
    }

    [Fact]
    public void Build_FillsTheEnvironmentSectionsFromTheRunningHost()
    {
        var report = DiagnosticsReportBuilder.Build(Snapshot());

        Assert.NotEmpty(SectionLines(report, "Платформа"));
        Assert.NotEmpty(SectionLines(report, "Среда выполнения"));
        Assert.NotEmpty(SectionLines(report, "Библиотеки"));
    }
}
