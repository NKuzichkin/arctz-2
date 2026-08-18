using System;
using System.Collections.Generic;

namespace ArctZ.Services.Program;

public sealed class JibProgram
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Новая программа";

    public ProgramCompletionMode CompletionMode { get; set; } = ProgramCompletionMode.Stop;

    public bool ReturnToStartOnFinish { get; set; }

    /// <summary>Repeats for Loop/PingPong; null means unlimited. Unused (always null) in Stop mode.</summary>
    public int? RepeatCount { get; set; }

    public List<KeyPoint> KeyPoints { get; } = new();

    /// <summary>
    /// Segment i describes the move to KeyPoints[i], using that point's own feed/ease/dwell
    /// settings. Segment 0 targets KeyPoints[0] itself (From == To, zero distance) so the very
    /// first key point is dispatched to the controller — and dwells there — like every other
    /// point, instead of being silently assumed as the machine's starting pose.
    /// </summary>
    public IEnumerable<ProgramSegment> Segments()
    {
        if (KeyPoints.Count == 0)
        {
            yield break;
        }

        yield return new ProgramSegment(0, KeyPoints[0], KeyPoints[0]);

        for (var i = 1; i < KeyPoints.Count; i++)
        {
            yield return new ProgramSegment(i, KeyPoints[i - 1], KeyPoints[i]);
        }
    }
}
