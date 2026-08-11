using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Screenshots.Support;

public sealed class FakeProgramStorage : IProgramStorage
{
    private readonly Dictionary<Guid, JibProgram> _programs = new();
    private readonly Dictionary<Guid, DateTimeOffset> _createdAt = new();
    private readonly Dictionary<Guid, DateTimeOffset> _modifiedAt = new();

    public Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProgramSummary>>(
            _programs.Values.Select(p => new ProgramSummary(p.Id, p.Name, _createdAt[p.Id], _modifiedAt[p.Id])).ToList());

    public Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_programs[id]);

    public Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default)
    {
        _programs[program.Id] = program;
        var now = DateTimeOffset.UtcNow;
        if (!_createdAt.ContainsKey(program.Id))
        {
            _createdAt[program.Id] = now;
        }

        _modifiedAt[program.Id] = now;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _programs.Remove(id);
        _createdAt.Remove(id);
        _modifiedAt.Remove(id);
        return Task.CompletedTask;
    }
}
