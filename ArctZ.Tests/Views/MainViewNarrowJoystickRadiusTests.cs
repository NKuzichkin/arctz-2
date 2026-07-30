using ArctZ.Views;

namespace ArctZ.Tests.Views;

public class MainViewNarrowJoystickRadiusTests
{
    [Theory]
    [InlineData(400, 74.5)]   // typical narrow phone width: (400-54)/2/2-12 = 74.5
    [InlineData(320, 54.5)]   // very narrow: (320-54)/2/2-12 = 54.5
    [InlineData(200, 50)]     // degenerate width: formula gives 24.5, floor clamps to 50
    public void ComputeNarrowJoystickRadius_ReturnsExpectedRadius(double mainViewWidth, double expectedRadius)
    {
        var radius = MainView.ComputeNarrowJoystickRadius(mainViewWidth);

        Assert.Equal(expectedRadius, radius, precision: 3);
    }
}
