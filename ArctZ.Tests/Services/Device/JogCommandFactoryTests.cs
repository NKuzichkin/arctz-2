using System;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class JogCommandFactoryTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private const double Lookahead = 1.5;

    // 1000 units/min = 16.667 units/s; 16.667 * 0.1s * 1.5 = 2.5 units
    private const double FullDeflectionStep = 2.5;

    private readonly JogCommandFactory _factory =
        new(MachineLimits.Default, Interval, maxFeedUnitsPerMin: 1000.0, lookaheadFactor: Lookahead);

    [Fact]
    public void Create_BothSticksNeutral_ZeroDeltasAndMinimumFeed()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(0, 0, 0));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(MachinePose.Zero, command.Deltas);
        Assert.Equal(1.0, command.Feed);
    }

    [Fact]
    public void Create_LeftStickX_MapsToBoomLiftAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(FullDeflectionStep, command.Deltas.X, 6);
        Assert.Equal(0, command.Deltas.Y);
        Assert.Equal(0, command.Deltas.Z);
        Assert.Equal(0, command.Deltas.A);
    }

    [Fact]
    public void Create_LeftStickY_MapsToBoomRotationAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 1, 1), new JoystickAxisInput(0, 0, 0));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(FullDeflectionStep, command.Deltas.Y, 6);
    }

    [Fact]
    public void Create_RightStickX_MapsToCameraPanAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(1, 0, 1));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(FullDeflectionStep, command.Deltas.Z, 6);
    }

    [Fact]
    public void Create_RightStickY_MapsToCameraTiltAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(0, 1, 1));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(FullDeflectionStep, command.Deltas.A, 6);
    }

    [Fact]
    public void Create_NearUpperXLimit_ClampsDeltaToRemainingRoom()
    {
        var state = new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0));
        var currentPose = new MachinePose(X: 64, Y: 0, Z: 0, A: 0);

        var command = _factory.Create(state, currentPose);

        Assert.Equal(1, command.Deltas.X, 6);
    }

    [Fact]
    public void Create_WrappingAxisNearBoundary_DeltaPassesThroughUnclamped()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(1, 0, 1));
        var currentPose = new MachinePose(X: 0, Y: 0, Z: 359, A: 0);

        var command = _factory.Create(state, currentPose);

        Assert.Equal(FullDeflectionStep, command.Deltas.Z, 6);
    }

    [Fact]
    public void Create_FeedUsesLargerOfTheTwoStickForces()
    {
        var state = new DualJoystickState(new JoystickAxisInput(1, 0, 0.3), new JoystickAxisInput(0, 1, 0.8));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(800, command.Feed);
    }

    /// <summary>
    /// The scheduler emits one jog block per timer interval, so a block that encodes more
    /// travel time than the interval makes the planner queue grow without bound — the machine
    /// then lags seconds behind the stick and overruns on release.
    /// </summary>
    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.5, 0.0)]
    [InlineData(0.05, 0.0)]
    [InlineData(0.7071, 0.7071)]
    public void Create_BlockTravelTimeAlwaysMatchesIntervalTimesLookahead(double x, double y)
    {
        var force = Math.Sqrt(x * x + y * y);
        var state = new DualJoystickState(new JoystickAxisInput(x, y, force), new JoystickAxisInput(0, 0, 0));

        var command = _factory.Create(state, MachinePose.Zero);

        var distance = Math.Sqrt(
            command.Deltas.X * command.Deltas.X +
            command.Deltas.Y * command.Deltas.Y +
            command.Deltas.Z * command.Deltas.Z +
            command.Deltas.A * command.Deltas.A);
        var travelSeconds = distance / (command.Feed / 60.0);

        Assert.Equal(Interval.TotalSeconds * Lookahead, travelSeconds, 6);
    }
}
