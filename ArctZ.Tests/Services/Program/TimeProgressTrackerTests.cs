using System;
using System.Collections.Generic;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Services.Program;
using Xunit;

namespace ArctZ.Tests.Services.Program;

public class TimeProgressTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static CompiledStep Move(int segmentIndex, double x, double estimatedSeconds = 5, bool isDwell = false) =>
        new(segmentIndex, new GCodeLineCommand("G93 G1 X" + x), SegmentProgress: 1.0, EstimatedDurationSeconds: estimatedSeconds, Pose: new MachinePose(x, 0, 0, 0), IsDwellStep: isDwell);

    [Fact]
    public void OnClockTick_HalfwayThroughTheEstimatedTimeOfTheOnlySegment_ReportsHalfOverallAndHalfStep()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(5));

        Assert.Equal(0.5, tracker.OverallFraction);
        Assert.Equal(0.5, tracker.CurrentStepFraction);
        Assert.Equal(0, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnClockTick_TimeDoesNotDependOnPosition_KeepsGrowingWhileTheMachineStandsStill()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        // Position never changes (as if the machine were dwelling) — OnPositionUpdated isn't even called.
        tracker.OnClockTick(T0.AddSeconds(3));
        var afterThree = tracker.OverallFraction;
        tracker.OnClockTick(T0.AddSeconds(6));

        Assert.True(tracker.OverallFraction > afterThree);
    }

    [Fact]
    public void OnPositionUpdated_MovingIntoTheNextSegment_ResetsCurrentStepFractionForThatSegment()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10), Move(1, x: 20, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnPositionUpdated(new MachinePose(15, 0, 0, 0), T0.AddSeconds(12)); // crosses into segment 1's territory
        Assert.Equal(1, tracker.CurrentSegmentIndex);
        Assert.Equal(0.0, tracker.CurrentStepFraction); // just entered, no time elapsed in segment 1 yet

        tracker.OnClockTick(T0.AddSeconds(14)); // 2s later, still in segment 1

        Assert.Equal(0.2, tracker.CurrentStepFraction); // 2 of 10s estimate for segment 1
    }

    [Fact]
    public void OnPositionUpdated_NoisyPositionThatLooksLikeItWentBackward_NeverDecreasesTheActiveSegment()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10), Move(1, x: 20, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnPositionUpdated(new MachinePose(15, 0, 0, 0), T0.AddSeconds(12));
        Assert.Equal(1, tracker.CurrentSegmentIndex);

        tracker.OnPositionUpdated(new MachinePose(8, 0, 0, 0), T0.AddSeconds(13)); // noise projecting back into segment 0's own span — without the monotonic guard this would flip CurrentSegmentIndex back to 0

        Assert.Equal(1, tracker.CurrentSegmentIndex);
    }

    [Fact]
    public void OnPositionUpdated_ZeroLengthFirstSegment_CurrentSegmentIndexIsZeroFromConstruction()
    {
        // Real segment 0 is From == To == KeyPoints[0] in the model; here the compiled step's own
        // pose equals the starting pose, producing a zero-length edge — but its estimated time
        // still applies, since EstimatedDurationSeconds is a time estimate, not a distance one.
        var steps = new List<CompiledStep> { Move(0, x: 0, estimatedSeconds: 5) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        Assert.Equal(0, tracker.CurrentSegmentIndex);

        tracker.OnClockTick(T0.AddSeconds(2.5));

        Assert.Equal(0.5, tracker.CurrentStepFraction);
    }

    [Fact]
    public void Construction_DoesNotResetTheEntryClockOnTheFirstTick()
    {
        // If the first Recompute call mistook "no prior recorded segment" for "just entered this
        // segment", it would reset the entry clock to whenever that first call happens instead of
        // passStartedAt — undercounting elapsed time for segment 0 by however long the caller
        // waited before the first tick/position update.
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 5) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(2));

        Assert.Equal(0.4, tracker.CurrentStepFraction); // 2 of 5, measured from passStartedAt, not from this first tick
    }

    [Fact]
    public void Changed_FiresOnPositionUpdateAndOnClockTick()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);
        var raiseCount = 0;
        tracker.Changed += () => raiseCount++;

        tracker.OnPositionUpdated(new MachinePose(5, 0, 0, 0), T0.AddSeconds(1));
        tracker.OnClockTick(T0.AddSeconds(2));

        Assert.Equal(2, raiseCount);
    }

    [Fact]
    public void CurrentPointHasWarning_ElapsedTwentyPercentOverEstimate_IsTrue()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(12)); // 12 of 10 estimated = 20% over

        Assert.True(tracker.CurrentPointHasWarning);
    }

    [Fact]
    public void CurrentPointHasWarning_ElapsedTenPercentOverEstimate_IsFalse()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(11)); // 11 of 10 estimated = 10% over, under the 15% threshold

        Assert.False(tracker.CurrentPointHasWarning);
    }

    [Fact]
    public void CurrentPointHasWarning_ClearsImmediatelyOnMovingToTheNextSegment()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10), Move(1, x: 20, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(15)); // segment 0, 50% over its 10s estimate
        Assert.True(tracker.CurrentPointHasWarning);

        tracker.OnPositionUpdated(new MachinePose(11, 0, 0, 0), T0.AddSeconds(15)); // real motion into segment 1

        Assert.False(tracker.CurrentPointHasWarning);
    }

    [Fact]
    public void CurrentStepFraction_ZeroEstimatedSecondsForTheSegment_IsOneAndNeverWarns()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 0) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(5));

        Assert.Equal(1.0, tracker.CurrentStepFraction);
        Assert.False(tracker.CurrentPointHasWarning);
    }

    [Fact]
    public void OverallFraction_ZeroTotalEstimatedSeconds_IsOne()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 0) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(5));

        Assert.Equal(1.0, tracker.OverallFraction);
    }

    [Fact]
    public void Constructor_SkippedLeadingEstimatedSeconds_PadsTheTotalEstimateWithoutAddingAnEdge()
    {
        // A redundant leading self-move (segment 0) can be skipped rather than dispatched — see
        // ProgramViewModel.SkipRedundantLeadingSelfMove — but its own estimated duration must still
        // count toward the pass's total, or OverallFraction saturates far earlier than an equivalent
        // un-skipped pass would have, relative to real elapsed time. `steps` here has no segment 0 at
        // all (already filtered, as the real caller passes it), so CurrentSegmentIndex must resolve
        // straight to the one real segment present, not a phantom segment 0.
        var steps = new List<CompiledStep> { Move(1, x: 10, estimatedSeconds: 6) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0, skippedLeadingEstimatedSeconds: 6);

        Assert.Equal(1, tracker.CurrentSegmentIndex);

        tracker.OnClockTick(T0.AddSeconds(6));

        Assert.Equal(0.5, tracker.OverallFraction); // 6 of (6 skipped + 6 real) = 12s total
    }

    [Fact]
    public void OverallFraction_TimeBeyondTheWholePassEstimate_ClampsAtOne()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(50));

        Assert.Equal(1.0, tracker.OverallFraction);
    }

    [Fact]
    public void CurrentStepFraction_TimeBeyondTheSegmentEstimate_ClampsAtOne()
    {
        // Real hardware routinely overruns a segment's estimate several times over (see
        // docs/firmware/fluidnc-slow-motion-limits.md) — CurrentStepFraction must clamp the same
        // way OverallFraction already does above it, or PhysicalPointRemainingFraction (1 -
        // CurrentStepFraction) goes negative and the execution log prints step percentages like
        // "838%" instead of a sane 0-100% figure.
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        tracker.OnClockTick(T0.AddSeconds(50));

        Assert.Equal(1.0, tracker.CurrentStepFraction);
    }

    [Fact]
    public void OverallFraction_SumsEstimatedSecondsAcrossAllStepsIncludingDwell()
    {
        var steps = new List<CompiledStep>
        {
            Move(0, x: 10, estimatedSeconds: 10),
            new(0, new GCodeLineCommand("G4 P5"), SegmentProgress: 1.0, EstimatedDurationSeconds: 5, Pose: new MachinePose(10, 0, 0, 0), IsDwellStep: true),
        };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);

        // Total estimated for the pass is 10 (transition) + 5 (dwell) = 15; halfway through that is 7.5s.
        tracker.OnClockTick(T0.AddSeconds(7.5));

        Assert.Equal(0.5, tracker.OverallFraction);
    }

    [Fact]
    public void SegmentTimeOverage_FiresWhenLeavingASegmentThatWasOverTime()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10), Move(1, x: 20, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);
        (int SegmentIndex, double ActualSeconds, double EstimatedSeconds)? raised = null;
        tracker.SegmentTimeOverage += (segmentIndex, actualSeconds, estimatedSeconds) => raised = (segmentIndex, actualSeconds, estimatedSeconds);

        tracker.OnClockTick(T0.AddSeconds(15)); // segment 0, 50% over its 10s estimate
        Assert.Null(raised); // still inside the segment — nothing to report yet

        tracker.OnPositionUpdated(new MachinePose(11, 0, 0, 0), T0.AddSeconds(15)); // real motion into segment 1

        Assert.NotNull(raised);
        Assert.Equal(0, raised!.Value.SegmentIndex);
        Assert.Equal(15, raised.Value.ActualSeconds);
        Assert.Equal(10, raised.Value.EstimatedSeconds);
    }

    [Fact]
    public void SegmentTimeOverage_DoesNotFireWhenLeavingASegmentThatWasOnTime()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10), Move(1, x: 20, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);
        var raised = false;
        tracker.SegmentTimeOverage += (_, _, _) => raised = true;

        tracker.OnPositionUpdated(new MachinePose(11, 0, 0, 0), T0.AddSeconds(9)); // moved on comfortably within the 10s estimate

        Assert.False(raised);
    }

    [Fact]
    public void FlushCurrentSegment_ActiveSegmentIsOverTime_FiresTheEvent()
    {
        // The last point of a pass never gets a "next segment" to move into, so nothing would
        // otherwise report its overage — FlushCurrentSegment covers that on pass end/stop.
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);
        (int SegmentIndex, double ActualSeconds, double EstimatedSeconds)? raised = null;
        tracker.SegmentTimeOverage += (segmentIndex, actualSeconds, estimatedSeconds) => raised = (segmentIndex, actualSeconds, estimatedSeconds);

        tracker.OnClockTick(T0.AddSeconds(15)); // 50% over the 10s estimate, no next segment to move into
        tracker.FlushCurrentSegment(T0.AddSeconds(15));

        Assert.NotNull(raised);
        Assert.Equal(0, raised!.Value.SegmentIndex);
        Assert.Equal(15, raised.Value.ActualSeconds);
        Assert.Equal(10, raised.Value.EstimatedSeconds);
    }

    [Fact]
    public void FlushCurrentSegment_ActiveSegmentIsOnTime_DoesNotFireTheEvent()
    {
        var steps = new List<CompiledStep> { Move(0, x: 10, estimatedSeconds: 10) };
        var tracker = new TimeProgressTracker(steps, startingPose: MachinePose.Zero, passStartedAt: T0);
        var raised = false;
        tracker.SegmentTimeOverage += (_, _, _) => raised = true;

        tracker.OnClockTick(T0.AddSeconds(5));
        tracker.FlushCurrentSegment(T0.AddSeconds(5));

        Assert.False(raised);
    }
}
