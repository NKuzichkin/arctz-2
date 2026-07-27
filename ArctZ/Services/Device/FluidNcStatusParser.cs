using System;
using System.Globalization;

namespace ArctZ.Services.Device;

public sealed class FluidNcStatusParser : IStatusParser
{
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

    private static FluidNcLine ParseStatusReport(string body, string rawLine)
    {
        var fields = body.Split('|');
        if (fields.Length == 0)
        {
            return new UnrecognizedLine(rawLine);
        }

        var state = Enum.TryParse<MachineState>(fields[0], ignoreCase: true, out var parsedState)
            ? parsedState
            : MachineState.Unknown;

        var pose = MachinePose.Zero;
        var wPosField = Array.Find(fields, f => f.StartsWith("WPos:", StringComparison.Ordinal));
        if (wPosField is not null)
        {
            var coords = wPosField["WPos:".Length..].Split(',');
            pose = new MachinePose(
                X: ParseCoordinate(coords, 0),
                Y: ParseCoordinate(coords, 1),
                Z: ParseCoordinate(coords, 2),
                A: ParseCoordinate(coords, 3));
        }

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

    private static double ParseCoordinate(string[] coords, int index) =>
        index < coords.Length &&
        double.TryParse(coords[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}
