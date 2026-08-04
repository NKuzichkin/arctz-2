using Avalonia;
using Avalonia.Media;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class ColorsTests
{
    public ColorsTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void HudScrimBrush_ResolvesToExpectedColor()
    {
        Application.Current!.TryGetResource("HudScrimBrush", null, out var value);

        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.Equal(Color.Parse("#CC0A0E12"), brush.Color);
    }

    [Fact]
    public void HudBackgroundDeepColor_ResolvesToExpectedColor()
    {
        Application.Current!.TryGetResource("HudBackgroundDeepColor", null, out var value);

        Assert.Equal(Color.Parse("#0C1116"), value);
    }
}
