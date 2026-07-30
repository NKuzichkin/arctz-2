using ArctZ.Views;

namespace ArctZ.Tests.Views;

public class MainViewNarrowJoystickRadiusTests
{
    [Theory]
    [InlineData(400, 800, 74.5)]    // typical tall portrait: width-bound, height not limiting
    [InlineData(320, 700, 54.5)]    // very narrow portrait: width-bound
    [InlineData(200, 800, 50)]      // degenerate width: width formula floor-clamped to 50
    [InlineData(667, 375, 50)]      // ordinary landscape phone: height ceiling floor-clamped to 50
    [InlineData(690, 500, 87)]      // moderate landscape: height ceiling binds above the floor (87 < width-only 147)
    public void ComputeNarrowJoystickRadius_ReturnsExpectedRadius(double mainViewWidth, double mainViewHeight, double expectedRadius)
    {
        var radius = MainView.ComputeNarrowJoystickRadius(mainViewWidth, mainViewHeight);

        Assert.Equal(expectedRadius, radius, precision: 3);
    }
}
