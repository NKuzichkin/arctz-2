using ArctZ.Views;

namespace ArctZ.Tests.Views;

public class MainViewJoystickRadiusTests
{
    [Theory]
    [InlineData(1200, 800, 60, 110)]     // просторный десктоп: упирается в верхний предел MaxRadius
    [InlineData(360, 700, 90, 70.5)]     // узкий телефон-портрет: ширина ограничивает
    [InlineData(250, 800, 60, 50)]       // вырожденная ширина: floor-clamp по MinRadius
    [InlineData(800, 300, 60, 50)]       // очень низкое окно: высота floor-clamp по MinRadius
    [InlineData(1000, 500, 60, 101)]     // высота ограничивает, но не floor/ceiling
    [InlineData(500, 400, 0, 59)]        // headerHeight=0 → включается HeaderFallbackHeight
    [InlineData(500, 400, -10, 59)]      // отрицательный headerHeight тоже триггерит фолбэк
    [InlineData(500, 400, 1, 80.5)]      // headerHeight=1 (>0) — фолбэк НЕ включается, используется как есть
    public void ComputeJoystickRadius_ReturnsExpectedRadius(
        double mainViewWidth, double mainViewHeight, double headerHeight, double expectedRadius)
    {
        var radius = MainView.ComputeJoystickRadius(mainViewWidth, mainViewHeight, headerHeight);

        Assert.Equal(expectedRadius, radius, precision: 3);
    }
}
