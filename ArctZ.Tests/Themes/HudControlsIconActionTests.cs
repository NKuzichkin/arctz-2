using Avalonia.Controls;
using Avalonia.Threading;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class HudControlsIconActionTests
{
    public HudControlsIconActionTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void IconActionButton_GetsMinimumTouchTarget()
    {
        var button = new Button();
        button.Classes.Add("icon-action");

        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(44, button.MinWidth);
        Assert.Equal(44, button.MinHeight);

        window.Close();
    }
}
