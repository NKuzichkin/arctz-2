using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArctZ.Services.Program;

/// <summary>
/// Text log of one program run — see
/// docs/superpowers/specs/2026-08-21-program-execution-log-design.md. Plain, UI-agnostic: time is
/// passed by the caller, never read from the system clock, for testability. Lives entirely in
/// memory for the duration of the app session; ProgramViewModel owns the instance and exposes its
/// Text through the "О программе" dialog.
/// </summary>
public sealed class ProgramExecutionLog
{
    private readonly DateTimeOffset _startedAt;
    private readonly List<string> _lines = new();
    private readonly object _gate = new();

    public ProgramExecutionLog(string programName, int keyPointCount, DateTimeOffset startedAt)
    {
        _startedAt = startedAt;
        lock (_gate)
        {
            _lines.Add(FormatLine(startedAt, $"Программа запущена: «{programName}», {keyPointCount} точек"));
        }
    }

    public void LogMovementEnded(string? pointLabel, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append($"Окончание движения к точке «{pointLabel}»", overallProgress, stepProgress, now);

    public void LogMovementStarted(string? pointLabel, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append($"Начало движения к точке «{pointLabel}»", overallProgress, stepProgress, now);

    public void LogPauseStarted(double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append("Пауза", overallProgress, stepProgress, now);

    public void LogPauseEnded(double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append("Возобновление", overallProgress, stepProgress, now);

    public void LogAckDesync(int ackSegmentIndex, int physicalSegmentIndex, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append(
            $"Рассинхронизация: буфер контроллера опережает факт на {ackSegmentIndex - physicalSegmentIndex} точки (ack: сегмент {ackSegmentIndex}, факт: сегмент {physicalSegmentIndex})",
            overallProgress, stepProgress, now);

    public void LogTimeOverage(string? pointLabel, double actualSeconds, double estimatedSeconds, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append(
            $"Рассинхронизация: превышение расчётного времени точки «{pointLabel}» ({actualSeconds.ToString("F1", CultureInfo.InvariantCulture)}с факт / {estimatedSeconds.ToString("F1", CultureInfo.InvariantCulture)}с расчёт)",
            overallProgress, stepProgress, now);

    public void LogProgramEnded(string outcomeLabel, double overallProgress, double stepProgress, DateTimeOffset now) =>
        Append($"Программа завершена: {outcomeLabel}", overallProgress, stepProgress, now);

    public string Text
    {
        get
        {
            lock (_gate)
            {
                return string.Join(Environment.NewLine, _lines);
            }
        }
    }

    private void Append(string eventText, double overallProgress, double stepProgress, DateTimeOffset now)
    {
        lock (_gate)
        {
            _lines.Add(FormatLine(now, $"{eventText} — общий {FormatPercent(overallProgress)}, шаг {FormatPercent(stepProgress)}"));
        }
    }

    private string FormatLine(DateTimeOffset now, string text) => $"[{FormatElapsed(now)}] {text}";

    private string FormatElapsed(DateTimeOffset now)
    {
        var totalMilliseconds = (long)Math.Round((now - _startedAt).TotalMilliseconds, MidpointRounding.AwayFromZero);
        var minutes = totalMilliseconds / 60000;
        var seconds = (totalMilliseconds / 1000) % 60;
        var milliseconds = totalMilliseconds % 1000;
        return $"{minutes:D2}:{seconds:D2}.{milliseconds:D3}";
    }

    private static string FormatPercent(double fraction) => $"{fraction * 100:F0}%";
}
