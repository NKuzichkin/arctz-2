using System;
using System.Globalization;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Services.Device;

public sealed class FluidNcCommandSerializer : ICommandSerializer
{
    public string Serialize(IDeviceCommand command) => command switch
    {
        JogCommand jog => SerializeJog(jog),
        GCodeLineCommand line => line.Line,
        RealtimeCommand realtime => ((char)realtime.Value).ToString(),
        _ => throw new NotSupportedException($"Unknown command type: {command.GetType()}")
    };

    private static string SerializeJog(JogCommand jog)
    {
        var x = Format(jog.Deltas.X);
        var y = Format(jog.Deltas.Y);
        var z = Format(jog.Deltas.Z);
        var a = Format(jog.Deltas.A);
        var feed = Format(jog.Feed);
        return $"$J=G91 G21 X{x} Y{y} Z{z} A{a} F{feed}";
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
