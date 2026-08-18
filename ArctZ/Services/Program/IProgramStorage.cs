using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Program;

public interface IProgramStorage
{
    /// <summary>
    /// Directory the programs are kept in, for the "О программе" report to measure the
    /// volume they compete for. Null for storages that aren't backed by a filesystem —
    /// the browser head keeps programs in memory. Defaulted so those implementations
    /// need say nothing, the same way IDeviceTransport.IsSupported works.
    /// </summary>
    string? Location => null;

    Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
