using Avalonia.Controls;
using Avalonia.Threading;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class HudControlsHeaderActionTests
{
    public HudControlsHeaderActionTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void HeaderActionButton_GetsMinimumTouchHeight()
    {
        var button = new Button();
        button.Classes.Add("header-action");

        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(44, button.MinHeight);

        window.Close();
    }

    [Fact]
    public void HeaderDividerBorder_GetsHairlineWidth()
    {
        var border = new Border();
        border.Classes.Add("header-divider");

        var window = new Window { Content = border };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, border.Width);

        window.Close();
    }
}
