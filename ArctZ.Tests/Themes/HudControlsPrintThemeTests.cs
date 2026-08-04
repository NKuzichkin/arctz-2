using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class HudControlsPrintThemeTests
{
    public HudControlsPrintThemeTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    private static (Window Window, Button Button) CreateHostedDangerButton(bool printMode)
    {
        var button = new Button();
        button.Classes.Add("danger");

        var window = new Window { Content = button };
        if (printMode)
        {
            window.Classes.Add("print");
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, button);
    }

    [Fact]
    public void PrintMode_ThickensDangerButtonBorder()
    {
        var (window, button) = CreateHostedDangerButton(printMode: true);

        Assert.Equal(new Thickness(2), button.BorderThickness);

        window.Close();
    }

    [Fact]
    public void NonPrintMode_KeepsDefaultDangerButtonBorder()
    {
        var (window, button) = CreateHostedDangerButton(printMode: false);

        Assert.Equal(new Thickness(1), button.BorderThickness);

        window.Close();
    }
}
