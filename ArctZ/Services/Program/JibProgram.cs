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

    /// <summary>Maps a segment index (as produced by <see cref="Segments"/>, or by compiling a
    /// reversed program for a backward/PingPong pass) to the key point it targets in
    /// <paramref name="forwardKeyPoints"/> — the pass's own original (forward) order. A backward
    /// pass compiles a reversed program, whose own segment index 0 targets the *last* forward
    /// point, hence <c>Count - 1 - segmentIndex</c>.</summary>
    public static Guid? TargetKeyPoint(IReadOnlyList<KeyPoint> forwardKeyPoints, int? segmentIndex, bool backward)
    {
        if (segmentIndex is not { } index)
        {
            return null;
        }

        var targetIndex = backward ? forwardKeyPoints.Count - 1 - index : index;
        return targetIndex >= 0 && targetIndex < forwardKeyPoints.Count
            ? forwardKeyPoints[targetIndex].Id
            : null;
    }
}
