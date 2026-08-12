using System.Text;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class LineAssemblerTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Append_SingleChunkWithNewline_ReturnsOneLine()
    {
        var assembler = new LineAssembler();

        var lines = assembler.Append(Bytes("ok\n"), Bytes("ok\n").Length);

        Assert.Equal(new[] { "ok" }, lines);
    }

    [Fact]
    public void Append_SplitAcrossTwoChunks_ReassemblesLineOnSecondChunk()
    {
        var assembler = new LineAssembler();

        var first = assembler.Append(Bytes("o"), 1);
        var second = assembler.Append(Bytes("k\n"), 2);

        Assert.Empty(first);
        Assert.Equal(new[] { "ok" }, second);
    }

    [Fact]
    public void Append_CarriageReturnBeforeNewline_IsStripped()
    {
        var assembler = new LineAssembler();

        var lines = assembler.Append(Bytes("ok\r\n"), Bytes("ok\r\n").Length);

        Assert.Equal(new[] { "ok" }, lines);
    }

    [Fact]
    public void Append_MultipleLinesInOneChunk_ReturnsAllOfThem()
    {
        var assembler = new LineAssembler();
        var data = Bytes("ok\nok\n");

        var lines = assembler.Append(data, data.Length);

        Assert.Equal(new[] { "ok", "ok" }, lines);
    }

    [Fact]
    public void Append_EmptyLines_AreDropped()
    {
        var assembler = new LineAssembler();
        var data = Bytes("\n\nok\n");

        var lines = assembler.Append(data, data.Length);

        Assert.Equal(new[] { "ok" }, lines);
    }

    [Fact]
    public void Append_LineLongerThanLimit_IsDroppedEntirely()
    {
        var assembler = new LineAssembler();
        var overlong = Bytes(new string('A', 5000));

        var duringOverlong = assembler.Append(overlong, overlong.Length);
        var afterNewline = assembler.Append(Bytes("\n"), 1);

        Assert.Empty(duringOverlong);
        Assert.Empty(afterNewline);
    }

    [Fact]
    public void Append_LineWithinLimitAfterAnOverlongOne_IsStillAssembledCorrectly()
    {
        var assembler = new LineAssembler();
        var overlong = Bytes(new string('A', 5000));
        assembler.Append(overlong, overlong.Length);
        assembler.Append(Bytes("\n"), 1);

        var lines = assembler.Append(Bytes("ok\n"), 3);

        Assert.Equal(new[] { "ok" }, lines);
    }
}
