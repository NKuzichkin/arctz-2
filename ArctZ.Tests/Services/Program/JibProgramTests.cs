using System;
using System.Linq;
using ArctZ.Services.Device;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class JibProgramTests
{
    private static TransitionSettings DefaultTransition => new(500, 0, EaseMode.None, ContinuousBlend: false);

    [Fact]
    public void Segments_ZipsWaypointsAndTransitionsInOrder()
    {
        var program = new JibProgram();
        var a = new Waypoint(Guid.NewGuid(), "A", new MachinePose(0, 0, 0, 0));
        var b = new Waypoint(Guid.NewGuid(), "B", new MachinePose(10, 0, 0, 0));
        var c = new Waypoint(Guid.NewGuid(), "C", new MachinePose(20, 0, 0, 0));
        program.Waypoints.AddRange(new[] { a, b, c });
        program.Transitions.AddRange(new[] { DefaultTransition, DefaultTransition });

        var segments = program.Segments().ToList();

        Assert.Equal(2, segments.Count);
        Assert.Equal((0, a, b), (segments[0].Index, segments[0].From, segments[0].To));
        Assert.Equal((1, b, c), (segments[1].Index, segments[1].From, segments[1].To));
    }

    [Fact]
    public void Segments_FewerThanTwoWaypoints_IsEmpty()
    {
        var program = new JibProgram();
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "A", MachinePose.Zero));

        Assert.Empty(program.Segments());
    }

    [Fact]
    public void Segments_WaypointAddedWithoutMatchingTransition_StopsBeforeIt()
    {
        var program = new JibProgram();
        var a = new Waypoint(Guid.NewGuid(), "A", MachinePose.Zero);
        var b = new Waypoint(Guid.NewGuid(), "B", new MachinePose(10, 0, 0, 0));
        var c = new Waypoint(Guid.NewGuid(), "C", new MachinePose(20, 0, 0, 0));
        program.Waypoints.AddRange(new[] { a, b, c });
        program.Transitions.Add(DefaultTransition); // only one transition for 2 segments

        var segments = program.Segments().ToList();

        Assert.Single(segments);
        Assert.Equal(a, segments[0].From);
        Assert.Equal(b, segments[0].To);
    }
}
