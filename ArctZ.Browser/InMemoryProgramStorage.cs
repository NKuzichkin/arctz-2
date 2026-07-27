using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Program;

namespace ArctZ.Browser;

/// <summary>
/// Non-persistent stand-in for Browser until IndexedDB-backed storage is
/// built (spec's open question — WASM has no ordinary filesystem).
/// Programs are lost on page reload.
/// </summary>
public sealed class InMemoryProgramStorage : IProgramStorage
{
    private readonly Dictionary<Guid, JibProgram> _programs = new();

    public Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProgramSummary>>(
            _programs.Values.Select(p => new ProgramSummary(p.Id, p.Name, DateTimeOffset.UtcNow)).ToList());

    public Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_programs[id]);

    public Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default)
    {
        _programs[program.Id] = program;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _programs.Remove(id);
        return Task.CompletedTask;
    }
}
