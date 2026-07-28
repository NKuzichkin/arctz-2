using System;
using System.Collections.Generic;

namespace ArctZ.Services.Program;

public sealed class JibProgram
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Новая программа";

    public List<KeyPoint> KeyPoints { get; } = new();

    /// <summary>Segment i describes the move from KeyPoints[i] to KeyPoints[i+1], using KeyPoints[i+1]'s own feed/ease/dwell settings.</summary>
    public IEnumerable<ProgramSegment> Segments()
    {
        for (var i = 0; i < KeyPoints.Count - 1; i++)
        {
            yield return new ProgramSegment(i, KeyPoints[i], KeyPoints[i + 1]);
        }
    }
}
