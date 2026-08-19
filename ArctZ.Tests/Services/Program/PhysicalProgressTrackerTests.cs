using System.Collections.Generic;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Services.Program;
using Xunit;

namespace ArctZ.Tests.Services.Program;

public class PhysicalProgressTrackerTests
{
    private static CompiledStep Move(int segmentIndex, double x, bool isDwell = false) =>
        new(segmentIndex, new GCodeLineCommand("G93 G1 X" + x), SegmentProgress: 1.0, EstimatedDurationSeconds: 1, Pose: new MachinePose(x, 0, 0, 0), IsDwellStep: isDwell);

    [Fact]
    public void OnPositionUpdated_HalfwayThroughTheOnlySegment_ReportsHalfOverallAndHalfApproach()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(new MachinePose(5, 0, 0, 0));

        Assert.Equal(0.5, tracker.OverallFraction);
        Assert.Equal(0.5, tracker.ApproachFraction);
        Assert.Equal(0, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnPositionUpdated_ReachingTheEndOfASegment_ApproachFractionSaturatesAtOneForThatSegmentOnly()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10), Move(1, x: 20) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(new MachinePose(10, 0, 0, 0));

        Assert.Equal(0.5, tracker.OverallFraction); // 10 of 20 total units travelled
        Assert.Equal(1.0, tracker.ApproachFraction); // segment 0 itself is fully covered
        Assert.Equal(0, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnPositionUpdated_MovingIntoTheNextSegment_ApproachFractionResetsForThatSegment()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10), Move(1, x: 20) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(new MachinePose(10, 0, 0, 0));
        tracker.OnPositionUpdated(new MachinePose(15, 0, 0, 0));

        Assert.Equal(0.75, tracker.OverallFraction); // 15 of 20
        Assert.Equal(0.5, tracker.ApproachFraction); // halfway from 10 to 20
        Assert.Equal(1, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnPositionUpdated_NoisyPositionThatLooksLikeItWentBackward_NeverDecreasesProgress()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10), Move(1, x: 20) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(new MachinePose(15, 0, 0, 0));
        var overallAfterFifteen = tracker.OverallFraction;
        tracker.OnPositionUpdated(new MachinePose(12, 0, 0, 0)); // controller cornering smoothing noise

        Assert.Equal(overallAfterFifteen, tracker.OverallFraction);
    }

    [Fact]
    public void OnPositionUpdated_ZeroLengthFirstSegment_ApproachFractionIsInstantlyOne()
    {
        // Real segment 0 is From == To == KeyPoints[0] in the model; here the compiled step's own
        // pose equals the starting pose, producing a zero-length edge.
        var steps = new List<CompiledStep> { Move(0, x: 0) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);

        tracker.OnPositionUpdated(MachinePose.Zero);

        Assert.Equal(1.0, tracker.ApproachFraction);
        Assert.Equal(1.0, tracker.OverallFraction);
    }

    [Fact]
    public void Changed_FiresOnEveryPositionUpdate()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10) };
        var tracker = new PhysicalProgressTracker(steps, startingPose: MachinePose.Zero);
        var raiseCount = 0;
        tracker.Changed += () => raiseCount++;

        tracker.OnPositionUpdated(new MachinePose(5, 0, 0, 0));

        Assert.Equal(1, raiseCount);
    }
}
