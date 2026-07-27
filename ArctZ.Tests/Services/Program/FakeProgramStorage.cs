using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public sealed class FakeProgramStorage : IProgramStorage
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
