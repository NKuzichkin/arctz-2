using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ArctZ.Services.Program;

public sealed class JsonFileProgramStorage : IProgramStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
    };

    private readonly string _directoryPath;

    public JsonFileProgramStorage(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public async Task<IReadOnlyList<ProgramSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directoryPath))
        {
            return Array.Empty<ProgramSummary>();
        }

        var summaries = new List<ProgramSummary>();
        foreach (var file in Directory.EnumerateFiles(_directoryPath, "*.json"))
        {
            await using var stream = File.OpenRead(file);
            var program = await JsonSerializer.DeserializeAsync<JibProgram>(stream, Options, cancellationToken).ConfigureAwait(false);
            if (program is not null)
            {
                summaries.Add(new ProgramSummary(program.Id, program.Name, File.GetLastWriteTimeUtc(file)));
            }
        }

        return summaries;
    }

    public async Task<JibProgram> LoadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = PathFor(id);
        try
        {
            await using var stream = File.OpenRead(path);
            var program = await JsonSerializer.DeserializeAsync<JibProgram>(stream, Options, cancellationToken).ConfigureAwait(false);
            return program ?? throw new InvalidOperationException($"Program file '{path}' deserialized to null.");
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new FileNotFoundException($"Program file not found: {path}", path, ex);
        }
    }

    public async Task SaveAsync(JibProgram program, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);
        await using var stream = File.Create(PathFor(program.Id));
        await JsonSerializer.SerializeAsync(stream, program, Options, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = PathFor(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(Guid id) => Path.Combine(_directoryPath, $"{id}.json");
}
