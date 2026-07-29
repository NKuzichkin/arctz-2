using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using ArctZ.Components.VirtualJoystick;

namespace ArctZ.Tests.Components;

[Collection("AvaloniaHeadless")]
public class VirtualJoystickTests
{
    public VirtualJoystickTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    private static (Window Window, VirtualJoystick Joystick) CreateHostedJoystick(
        JoystickMode mode = JoystickMode.Fixed,
        JoystickShape shape = JoystickShape.Circle,
        JoystickLock @lock = JoystickLock.None,
        double radius = 65,
        double threshold = 0.15)
    {
        var joystick = new VirtualJoystick
        {
            Radius = radius,
            Mode = mode,
            Shape = shape,
            Lock = @lock,
            Threshold = threshold,
            // Pin the control to the window's top-left origin regardless of window
            // size, so pointer coordinates below (relative to the window) map
            // directly onto coordinates relative to the joystick control itself.
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var window = new Window { Content = joystick, Width = 400, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, joystick);
    }

    /// <summary>
    /// window.MouseDown/MouseMove/MouseUp post to the headless dispatcher queue
    /// rather than raising the input event inline, so a RunJobs() after each is
    /// what actually drives it through VirtualJoystick's OnPointer* overrides —
    /// without it, assertions can observe stale state from before the call.
    /// </summary>
    private static void Press(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Move(Window window, Point to)
    {
        window.MouseMove(to);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Release(Window window, Point at)
    {
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void PressAtCenter_ThenDragRight_ReportsForceAngleAndDirection()
    {
        var (window, joystick) = CreateHostedJoystick();

        // Fixed mode: the pad's origin is its own center (Radius, Radius) regardless of
        // where the pointer first lands, so drag deltas below are measured from there.
        Press(window, new Point(65, 65));
        Move(window, new Point(65 + 32.5, 65));

        Assert.Equal(0.5, joystick.Force, precision: 3);
        Assert.Equal(0, joystick.Angle, precision: 3);
        Assert.Equal(JoystickDirection.E, joystick.Direction);

        Release(window, new Point(65 + 32.5, 65));
        window.Close();
    }

    [Fact]
    public void DragBeyondRadius_Circle_ClampsForceToOne()
    {
        var (window, joystick) = CreateHostedJoystick();

        Press(window, new Point(65, 65));
        Move(window, new Point(65 + 500, 65));

        Assert.Equal(1.0, joystick.Force, precision: 3);

        window.Close();
    }

    [Fact]
    public void DragBeyondRadius_Box_ClampsPerAxisNotByDistance()
    {
        var (window, joystick) = CreateHostedJoystick(shape: JoystickShape.Box);

        // Diagonal drag well past the radius on both axes: a Box clamp caps X/Y
        // independently, so the resulting force (hypot of two full-radius legs)
        // exceeds 1 — unlike Circle, which never lets force exceed 1.
        Press(window, new Point(65, 65));
        Move(window, new Point(65 + 500, 65 + 500));

        Assert.True(joystick.Force > 1.0);

        window.Close();
    }

    [Fact]
    public void LockX_ZeroesVerticalDeflection()
    {
        var (window, joystick) = CreateHostedJoystick(@lock: JoystickLock.X);

        Press(window, new Point(65, 65));
        Move(window, new Point(65 + 10, 65 + 40));

        Assert.Equal(0, joystick.Angle, precision: 3);

        window.Close();
    }

    [Fact]
    public void BelowThreshold_DirectionIsNone()
    {
        var (window, joystick) = CreateHostedJoystick(threshold: 0.5);

        Press(window, new Point(65, 65));
        Move(window, new Point(65 + 10, 65));

        Assert.True(joystick.Force < 0.5);
        Assert.Equal(JoystickDirection.None, joystick.Direction);

        window.Close();
    }

    [Fact]
    public void Release_ResetsForceAndDirectionToZero()
    {
        var (window, joystick) = CreateHostedJoystick();

        Press(window, new Point(65, 65));
        Move(window, new Point(65 + 32.5, 65));
        Release(window, new Point(65 + 32.5, 65));

        Assert.Equal(0, joystick.Force);
        Assert.Equal(JoystickDirection.None, joystick.Direction);

        window.Close();
    }

    [Fact]
    public void MoveAcrossThreshold_RaisesCapturedThenReleasedOnJoystickMove()
    {
        var (window, joystick) = CreateHostedJoystick(threshold: 0.2);
        JoystickEventArgs? lastMove = null;
        joystick.JoystickMove += (_, e) => lastMove = e;

        Press(window, new Point(65, 65));

        // Cross the threshold outward: Direction flips None -> E, so this move should
        // report Captured == E.
        Move(window, new Point(65 + 30, 65));
        Assert.Equal(JoystickDirection.E, lastMove!.Captured);
        Assert.Null(lastMove.Released);

        // Back under the threshold: Direction flips E -> None, so this move should
        // report Released == E instead.
        Move(window, new Point(65 + 2, 65));
        Assert.Null(lastMove.Captured);
        Assert.Equal(JoystickDirection.E, lastMove.Released);

        window.Close();
    }

    [Fact]
    public void JoystickUp_AfterActiveDirection_ReportsReleased()
    {
        var (window, joystick) = CreateHostedJoystick();
        JoystickEventArgs? lastUp = null;
        joystick.JoystickUp += (_, e) => lastUp = e;

        Press(window, new Point(65, 65));
        Move(window, new Point(65 + 32.5, 65));
        Release(window, new Point(65 + 32.5, 65));

        Assert.Equal(JoystickDirection.E, lastUp!.Released);
        Assert.Null(lastUp.Captured);

        window.Close();
    }

    [Fact]
    public void SemiMode_IgnoresPressOutsideBounds()
    {
        var (window, joystick) = CreateHostedJoystick(mode: JoystickMode.Semi);

        // Well outside the joystick's 130x130 bounds (pinned at the window's
        // top-left origin) but still inside the 400x400 window.
        Press(window, new Point(300, 300));
        Move(window, new Point(330, 300));

        Assert.Equal(0, joystick.Force);

        window.Close();
    }
}
