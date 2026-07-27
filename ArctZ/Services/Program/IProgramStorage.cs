using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Program;

public interface IProgramStorage
{
    Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
