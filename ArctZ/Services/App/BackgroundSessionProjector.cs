using System;
using ArctZ.ViewModels;

namespace ArctZ.Services.App;

public static class BackgroundSessionProjector
{
    /// <summary>Заголовок, когда у программы ещё нет имени.</summary>
    public const string AppName = "ArctZ";

    public static BackgroundSessionState Project(PlaybackState playback, string statusLabel, string? programName, double? overallFraction) =>
        new(
            Title: string.IsNullOrWhiteSpace(programName) ? AppName : programName,
            Status: statusLabel,
            CanPause: playback == PlaybackState.Running,
            CanResume: playback == PlaybackState.Paused,
            CanStop: playback is PlaybackState.Running or PlaybackState.Paused,
            ProgressPercent: overallFraction is { } fraction ? (int)(Math.Round(fraction * 100 / 5.0) * 5) : null);
}
