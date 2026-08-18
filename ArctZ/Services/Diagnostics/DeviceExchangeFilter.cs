using System;

namespace ArctZ.Services.Diagnostics;

/// <summary>
/// Decides which lines of the FluidNC conversation are worth keeping in the
/// diagnostic exchange log.
///
/// The link is dominated by two high-frequency, zero-information flows: status
/// reports (one every 250 ms from the status poller) and jog commands with their
/// acknowledgements (dozens per second while a joystick is held). Left in, they
/// would push everything diagnostically useful out of the log within a second.
/// What remains — program moves, $-settings, errors, alarms, [MSG:] lines, the
/// firmware banner — is exactly what a problem report needs.
/// </summary>
public static class DeviceExchangeFilter
{
    public static bool ShouldLog(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.Length == 0)
        {
            return false;
        }

        // Status report: "<Idle|MPos:...>"
        if (trimmed[0] == '<')
        {
            return false;
        }

        if (trimmed.StartsWith("$J=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A bare "ok" says only that the previous line was accepted; "error:N" and
        // "ALARM:N" — the answers that matter — are not filtered here.
        if (trimmed.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
