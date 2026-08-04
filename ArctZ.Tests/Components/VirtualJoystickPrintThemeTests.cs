using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArctZ.Components.VirtualJoystick;

namespace ArctZ.Tests.Components;

[Collection("AvaloniaHeadless")]
public class VirtualJoystickPrintThemeTests
{
    public VirtualJoystickPrintThemeTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    private static (Window Window, VirtualJoystick Joystick) CreateHostedJoystick(bool printMode)
    {
        var joystick = new VirtualJoystick
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var window = new Window { Content = joystick, Width = 400, Height = 400 };
        if (printMode)
        {
            window.Classes.Add("print");
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, joystick);
    }

    private static Ellipse FindPart(VirtualJoystick joystick, string name) =>
        joystick.GetVisualDescendants().OfType<Ellipse>().First(e => e.Name == name);

    [Fact]
    public void PrintMode_HidesAmbientGlow()
    {
        var (window, joystick) = CreateHostedJoystick(printMode: true);

        Assert.False(FindPart(joystick, "PART_Glow").IsVisible);

        window.Close();
    }

    [Fact]
    public void NonPrintMode_ShowsAmbientGlow()
    {
        var (window, joystick) = CreateHostedJoystick(printMode: false);

        Assert.True(FindPart(joystick, "PART_Glow").IsVisible);

        window.Close();
    }

    [Fact]
    public void PrintMode_RemovesBaseAndKnobEffects()
    {
        var (window, joystick) = CreateHostedJoystick(printMode: true);

        Assert.Null(FindPart(joystick, "PART_Base").Effect);
        Assert.Null(FindPart(joystick, "PART_Knob").Effect);

        window.Close();
    }

    [Fact]
    public void NonPrintMode_KeepsKnobDropShadow()
    {
        var (window, joystick) = CreateHostedJoystick(printMode: false);

        Assert.NotNull(FindPart(joystick, "PART_Knob").Effect);

        window.Close();
    }
}
