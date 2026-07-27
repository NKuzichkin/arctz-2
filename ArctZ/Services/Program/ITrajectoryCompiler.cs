using System.Collections.Generic;

namespace ArctZ.Services.Program;

public interface ITrajectoryCompiler
{
    IReadOnlyList<CompiledStep> Compile(JibProgram program);
}
