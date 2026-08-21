using System;
using ArctZ.Services.Program;
using Xunit;

namespace ArctZ.Tests.Services.Program;

public class ProgramExecutionLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WritesTheProgramStartedHeaderLine()
    {
        var log = new ProgramExecutionLog("Тест", keyPointCount: 5, startedAt: T0);

        Assert.Equal("[00:00.000] Программа запущена: «Тест», 5 точек", log.Text);
    }

    [Fact]
    public void LogMovementStarted_FormatsPointLabelAndBothProgressValues()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogMovementStarted("Точка 1", overallProgress: 0.18, stepProgress: 0.0, T0.AddSeconds(4.32));

        Assert.Contains("[00:04.320] Начало движения к точке «Точка 1» — общий 18%, шаг 0%", log.Text);
    }

    [Fact]
    public void LogMovementEnded_FormatsPointLabelAndBothProgressValues()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogMovementEnded("Точка 1", overallProgress: 0.18, stepProgress: 1.0, T0.AddSeconds(4.32));

        Assert.Contains("Окончание движения к точке «Точка 1» — общий 18%, шаг 100%", log.Text);
    }

    [Fact]
    public void LogPauseStarted_AndLogPauseEnded_AppendInOrder()
    {
        var log = new ProgramExecutionLog("Тест", 1, T0);

        log.LogPauseStarted(0.34, 0.55, T0.AddSeconds(7.1));
        log.LogPauseEnded(0.34, 0.55, T0.AddSeconds(12.4));

        var lines = log.Text.Split(Environment.NewLine);
        var pauseIndex = Array.IndexOf(lines, "[00:07.100] Пауза — общий 34%, шаг 55%");
        var resumeIndex = Array.IndexOf(lines, "[00:12.400] Возобновление — общий 34%, шаг 55%");
        Assert.True(pauseIndex >= 0);
        Assert.True(resumeIndex > pauseIndex);
    }

    [Fact]
    public void LogAckDesync_ReportsTheGapBetweenAckAndPhysicalSegments()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogAckDesync(ackSegmentIndex: 2, physicalSegmentIndex: 0, overallProgress: 0.61, stepProgress: 0.2, T0.AddSeconds(15));

        Assert.Contains(
            "Рассинхронизация: буфер контроллера опережает факт на 2 точки (ack: сегмент 2, факт: сегмент 0) — общий 61%, шаг 20%",
            log.Text);
    }

    [Fact]
    public void LogTimeOverage_ReportsActualVsEstimatedSeconds()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogTimeOverage("Точка 3", actualSeconds: 14.2, estimatedSeconds: 8.0, overallProgress: 0.75, stepProgress: 1.0, T0.AddSeconds(20));

        Assert.Contains(
            "Рассинхронизация: превышение расчётного времени точки «Точка 3» (14.2с факт / 8.0с расчёт) — общий 75%, шаг 100%",
            log.Text);
    }

    [Fact]
    public void LogProgramEnded_ReportsTheOutcomeLabel()
    {
        var log = new ProgramExecutionLog("Тест", 3, T0);

        log.LogProgramEnded("Завершено", overallProgress: 1.0, stepProgress: 1.0, T0.AddSeconds(41.22));

        Assert.Contains("[00:41.220] Программа завершена: Завершено — общий 100%, шаг 100%", log.Text);
    }

    [Fact]
    public void FormatElapsed_MinutesRollOverPastFiftyNineSeconds()
    {
        var log = new ProgramExecutionLog("Тест", 1, T0);

        log.LogPauseStarted(0, 0, T0.AddSeconds(65));

        Assert.Contains("[01:05.000] Пауза", log.Text);
    }
}
