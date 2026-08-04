using Avalonia.Controls;
using Avalonia.Media;
using ArctZ;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class PrintThemeTests
{
    public PrintThemeTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void Apply_OverridesHudBackgroundBrushToWhite()
    {
        var resources = new ResourceDictionary();

        PrintTheme.Apply(resources);

        resources.TryGetResource("HudBackgroundBrush", null, out var value);
        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.Equal(Colors.White, brush.Color);
    }

    [Fact]
    public void Apply_OverridesHudAccentBrushToBlack()
    {
        var resources = new ResourceDictionary();

        PrintTheme.Apply(resources);

        resources.TryGetResource("HudAccentBrush", null, out var value);
        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void Apply_SetsSystemAccentColorToBlack()
    {
        var resources = new ResourceDictionary();

        PrintTheme.Apply(resources);

        Assert.Equal(Colors.Black, resources["SystemAccentColor"]);
    }

    [Fact]
    public void Apply_SetsAllSevenSystemAccentColorVariants()
    {
        var resources = new ResourceDictionary();

        PrintTheme.Apply(resources);

        Assert.True(resources.ContainsKey("SystemAccentColorLight1"));
        Assert.True(resources.ContainsKey("SystemAccentColorLight2"));
        Assert.True(resources.ContainsKey("SystemAccentColorLight3"));
        Assert.True(resources.ContainsKey("SystemAccentColorDark1"));
        Assert.True(resources.ContainsKey("SystemAccentColorDark2"));
        Assert.True(resources.ContainsKey("SystemAccentColorDark3"));
    }
}
