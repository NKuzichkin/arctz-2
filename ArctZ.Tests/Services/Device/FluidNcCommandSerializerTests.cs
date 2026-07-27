using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

public class FluidNcCommandSerializerTests
{
    private readonly FluidNcCommandSerializer _serializer = new();

    [Fact]
    public void Serialize_JogCommand_ProducesRelativeJogLineWithAllFourAxes()
    {
        var command = new JogCommand(new MachinePose(X: 10, Y: -5, Z: 3, A: -2), Feed: 500);

        var result = _serializer.Serialize(command);

        Assert.Equal("$J=G91 G21 X10 Y-5 Z3 A-2 F500", result);
    }

    [Fact]
    public void Serialize_GCodeLineCommand_ReturnsLineUnchanged()
    {
        var command = new GCodeLineCommand("$H");

        var result = _serializer.Serialize(command);

        Assert.Equal("$H", result);
    }

    [Fact]
    public void Serialize_RealtimeCommand_ReturnsSingleCharacterString()
    {
        var result = _serializer.Serialize(RealtimeCommand.JogCancel);

        Assert.Equal(((char)0x85).ToString(), result);
    }
}
