using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class JogCommandFactoryTests
{
    private readonly JogCommandFactory _factory =
        new(MachineLimits.Default, maxStepDegrees: 5.0, maxFeedUnitsPerMin: 1000.0);

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

        Assert.Equal(5, command.Deltas.X);
        Assert.Equal(0, command.Deltas.Y);
        Assert.Equal(0, command.Deltas.Z);
        Assert.Equal(0, command.Deltas.A);
    }

    [Fact]
    public void Create_LeftStickY_MapsToBoomRotationAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 1, 1), new JoystickAxisInput(0, 0, 0));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(5, command.Deltas.Y);
    }

    [Fact]
    public void Create_RightStickX_MapsToCameraPanAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(1, 0, 1));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(5, command.Deltas.Z);
    }

    [Fact]
    public void Create_RightStickY_MapsToCameraTiltAxis()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(0, 1, 1));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(5, command.Deltas.A);
    }

    [Fact]
    public void Create_NearUpperXLimit_ClampsDeltaToRemainingRoom()
    {
        var state = new DualJoystickState(new JoystickAxisInput(1, 0, 1), new JoystickAxisInput(0, 0, 0));
        var currentPose = new MachinePose(X: 63, Y: 0, Z: 0, A: 0);

        var command = _factory.Create(state, currentPose);

        Assert.Equal(2, command.Deltas.X);
    }

    [Fact]
    public void Create_WrappingAxisNearBoundary_DeltaPassesThroughUnclamped()
    {
        var state = new DualJoystickState(new JoystickAxisInput(0, 0, 0), new JoystickAxisInput(1, 0, 1));
        var currentPose = new MachinePose(X: 0, Y: 0, Z: 359, A: 0);

        var command = _factory.Create(state, currentPose);

        Assert.Equal(5, command.Deltas.Z);
    }

    [Fact]
    public void Create_FeedUsesLargerOfTheTwoStickForces()
    {
        var state = new DualJoystickState(new JoystickAxisInput(1, 0, 0.3), new JoystickAxisInput(0, 1, 0.8));

        var command = _factory.Create(state, MachinePose.Zero);

        Assert.Equal(800, command.Feed);
    }
}
