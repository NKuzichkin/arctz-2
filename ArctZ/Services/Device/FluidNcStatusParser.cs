using System;
using System.Globalization;

namespace ArctZ.Services.Device;

public sealed class FluidNcStatusParser : IStatusParser
{
    /// <summary>Last work-coordinate offset the firmware reported. It rides along on only every
    /// tenth status report or so, but applies to every MPos in between, so it has to be remembered.
    /// Zero until the first WCO arrives — which is also the right answer while no work offset is
    /// set, the usual case for this machine.</summary>
    private MachinePose _workCoordinateOffset = MachinePose.Zero;

    public FluidNcLine Parse(string rawLine)
    {
        var line = rawLine.Trim();

        if (line.Length == 0)
        {
            return new UnrecognizedLine(rawLine);
        }

        if (line == "ok")
        {
            return new OkLine();
        }

        if (line.StartsWith("error:", StringComparison.Ordinal) &&
            int.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var errorCode))
        {
            return new ErrorLine(errorCode);
        }

        if (line.StartsWith("ALARM:", StringComparison.Ordinal) &&
            int.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var alarmCode))
        {
            return new AlarmLine(alarmCode);
        }

        if (line.StartsWith('<') && line.EndsWith('>'))
        {
            return ParseStatusReport(line[1..^1], rawLine);
        }

        return new UnrecognizedLine(rawLine);
    }

    private FluidNcLine ParseStatusReport(string body, string rawLine)
    {
        var fields = body.Split('|');
        if (fields.Length == 0)
        {
            return new UnrecognizedLine(rawLine);
        }

        var state = Enum.TryParse<MachineState>(fields[0], ignoreCase: true, out var parsedState)
            ? parsedState
            : MachineState.Unknown;

        if (TryReadPose(fields, "WCO:") is { } offset)
        {
            _workCoordinateOffset = offset;
        }

        // Which of the two the firmware sends is decided by `$10` bit 0, not by us, so both have to
        // be understood. WPos already has the offset applied; MPos is machine-absolute and needs it
        // subtracted to give the rest of the app the work position it expects.
        var pose = TryReadPose(fields, "WPos:")
            ?? (TryReadPose(fields, "MPos:") is { } machinePose
                ? Subtract(machinePose, _workCoordinateOffset)
                : MachinePose.Zero);

        int? plannerBlocksAvailable = null;
        int? rxBytesAvailable = null;
        var bfField = Array.Find(fields, f => f.StartsWith("Bf:", StringComparison.Ordinal));
        if (bfField is not null)
        {
            var parts = bfField["Bf:".Length..].Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var planner) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rx))
            {
                plannerBlocksAvailable = planner;
                rxBytesAvailable = rx;
            }
        }

        return new StatusReportLine(new DeviceStatus(state, pose, plannerBlocksAvailable, rxBytesAvailable));
    }

    private static MachinePose? TryReadPose(string[] fields, string prefix)
    {
        var field = Array.Find(fields, f => f.StartsWith(prefix, StringComparison.Ordinal));
        if (field is null)
        {
            return null;
        }

        var coords = field[prefix.Length..].Split(',');
        return new MachinePose(
            X: ParseCoordinate(coords, 0),
            Y: ParseCoordinate(coords, 1),
            Z: ParseCoordinate(coords, 2),
            A: ParseCoordinate(coords, 3));
    }

    private static MachinePose Subtract(MachinePose pose, MachinePose offset) =>
        new(pose.X - offset.X, pose.Y - offset.Y, pose.Z - offset.Z, pose.A - offset.A);

    private static double ParseCoordinate(string[] coords, int index) =>
        index < coords.Length &&
        double.TryParse(coords[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}
