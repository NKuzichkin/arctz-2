using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class FluidNcDeviceNameTests
{
    [Theory]
    [InlineData("FluidNC")]
    [InlineData("FluidNC-1234")]
    [InlineData("fluidnc_jib")]
    [InlineData("FLUIDNC")]
    public void Matches_NameStartingWithFluidNcCaseInsensitive_ReturnsTrue(string name)
    {
        Assert.True(FluidNcDeviceName.Matches(name));
    }

    [Theory]
    [InlineData("Jib FluidNC")]
    [InlineData("Some Other Device")]
    [InlineData("")]
    [InlineData(null)]
    public void Matches_NameNotStartingWithFluidNc_ReturnsFalse(string? name)
    {
        Assert.False(FluidNcDeviceName.Matches(name));
    }
}
