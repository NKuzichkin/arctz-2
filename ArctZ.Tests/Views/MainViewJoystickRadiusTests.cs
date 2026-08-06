using ArctZ.Views;

namespace ArctZ.Tests.Views;

public class MainViewJoystickRadiusTests
{
    [Theory]
    [InlineData(1200, 800, 60, 110)]     // просторный десктоп: упирается в верхний предел MaxRadius
    [InlineData(360, 700, 90, 67.5)]     // узкий телефон-портрет: ширина ограничивает
    [InlineData(250, 800, 60, 50)]       // вырожденная ширина: floor-clamp по MinRadius
    [InlineData(800, 300, 60, 50)]       // очень низкое окно: высота floor-clamp по MinRadius
    [InlineData(1000, 500, 60, 83)]      // высота ограничивает, но не floor/ceiling
    [InlineData(500, 400, 0, 50)]        // headerHeight=0 → включается HeaderFallbackHeight
    [InlineData(500, 400, -10, 50)]      // отрицательный headerHeight тоже триггерит фолбэк
    [InlineData(500, 400, 1, 62.5)]      // headerHeight=1 (>0) — фолбэк НЕ включается, используется как есть
    [InlineData(400, 150, 60, 50)]       // отрицательный бюджет высоты: heightRadius = -74 → floor
    [InlineData(0, 0, 0, 50)]            // вырожденный 0×0 (первый кадр): widthRadius/heightRadius отрицательны → floor
    public void ComputeJoystickRadius_ReturnsExpectedRadius(
        double mainViewWidth, double mainViewHeight, double headerHeight, double expectedRadius)
    {
        var radius = MainView.ComputeJoystickRadius(mainViewWidth, mainViewHeight, headerHeight);

        Assert.Equal(expectedRadius, radius, precision: 3);
    }
}
