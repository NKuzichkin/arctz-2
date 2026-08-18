using System;

namespace ArctZ.Services.Device;

/// <summary>Demo-only knobs for MockDeviceTransport, surfaced to the UI via ConnectionViewModel.</summary>
public interface IMockDeviceControl
{
    /// <summary>Makes the next dequeued command report an error instead of ok, and skips its effect.</summary>
    void ForceNextCommandError(int code);

    /// <summary>Immediately puts the mock into the Alarm state and raises an ALARM: line, mirroring an unsolicited push from a real controller.</summary>
    void TriggerAlarm(int code);

    /// <summary>Extra delay before ok/error is returned for each queued line command, on top of the normal one-line-per-tick pacing.</summary>
    void SetResponseDelay(TimeSpan delay);

    /// <summary>Teleports the simulated machine to a pose (clamped to the machine limits), cancelling any move in flight.</summary>
    void SetPose(MachinePose pose);
}
