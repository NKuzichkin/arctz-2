using System;
using System.Collections.Generic;

namespace ArctZ.Services.Program;

public sealed class JibProgram
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Новая программа";

    public List<Waypoint> Waypoints { get; } = new();

    /// <summary>Transitions[i] describes the move from Waypoints[i] to Waypoints[i+1].</summary>
    public List<TransitionSettings> Transitions { get; } = new();

    public IEnumerable<ProgramSegment> Segments()
    {
        var count = Math.Min(Waypoints.Count - 1, Transitions.Count);
        for (var i = 0; i < count; i++)
        {
            yield return new ProgramSegment(i, Waypoints[i], Waypoints[i + 1], Transitions[i]);
        }
    }
}
