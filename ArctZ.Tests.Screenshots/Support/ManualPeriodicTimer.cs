using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Screenshots.Support;

/// <summary>
/// Copy of ArctZ.Tests' timer of the same name: this project references ArctZ only, not the
/// unit-test project. Lets a screen's Setup drive TimeProgressTracker's clock ticks by hand, so
/// the playback screenshots show a deterministic progress value instead of whatever a real
/// 200 ms timer happened to have produced by capture time.
/// </summary>
public sealed class ManualPeriodicTimer : IPeriodicTimer
{
    public bool IsRunning { get; private set; }

    public event Action? Elapsed;

    public void Start(TimeSpan interval) => IsRunning = true;

    public void Stop() => IsRunning = false;

    public void RaiseElapsed() => Elapsed?.Invoke();
}
