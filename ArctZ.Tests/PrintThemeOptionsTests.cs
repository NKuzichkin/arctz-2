using System;
using ArctZ;

namespace ArctZ.Tests;

public class PrintThemeOptionsTests
{
    [Fact]
    public void IsPrintMode_WithPrintFlag_ReturnsTrue()
    {
        Assert.True(PrintThemeOptions.IsPrintMode(new[] { "--theme=print" }));
    }

    [Fact]
    public void IsPrintMode_WithNoArgs_ReturnsFalse()
    {
        Assert.False(PrintThemeOptions.IsPrintMode(Array.Empty<string>()));
    }

    [Fact]
    public void IsPrintMode_WithUnrelatedArgs_ReturnsFalse()
    {
        Assert.False(PrintThemeOptions.IsPrintMode(new[] { "--theme=dark", "--verbose" }));
    }

    [Fact]
    public void IsPrintMode_FlagAmongOtherArgs_ReturnsTrue()
    {
        Assert.True(PrintThemeOptions.IsPrintMode(new[] { "--verbose", "--theme=print" }));
    }
}
