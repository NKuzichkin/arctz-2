using System;
using System.IO;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;

namespace ArctZ.Tests.Services.Program;

public class JsonFileProgramStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ArctZTests_" + Guid.NewGuid());
    private readonly JsonFileProgramStorage _storage;

    public JsonFileProgramStorageTests()
    {
        _storage = new JsonFileProgramStorage(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static JibProgram SampleProgram(string name)
    {
        var program = new JibProgram { Name = name };
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "A", new MachinePose(1, 2, 3, 4)));
        program.Waypoints.Add(new Waypoint(Guid.NewGuid(), "B", new MachinePose(5, 6, 7, 8)));
        program.Transitions.Add(new TransitionSettings(500, 1.5, EaseMode.EaseInOut, ContinuousBlend: true));
        return program;
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsProgramContent()
    {
        var program = SampleProgram("Демо-программа");

        await _storage.SaveAsync(program);
        var loaded = await _storage.LoadAsync(program.Id);

        Assert.Equal(program.Id, loaded.Id);
        Assert.Equal("Демо-программа", loaded.Name);
        Assert.Equal(2, loaded.Waypoints.Count);
        Assert.Equal(program.Waypoints[0].Pose, loaded.Waypoints[0].Pose);
        Assert.Single(loaded.Transitions);
        Assert.Equal(1.5, loaded.Transitions[0].DwellSeconds);
    }

    [Fact]
    public async Task ListAsync_EmptyDirectory_ReturnsEmpty()
    {
        var summaries = await _storage.ListAsync();

        Assert.Empty(summaries);
    }

    [Fact]
    public async Task ListAsync_AfterSavingTwoPrograms_ReturnsBothSummaries()
    {
        await _storage.SaveAsync(SampleProgram("Первая"));
        await _storage.SaveAsync(SampleProgram("Вторая"));

        var summaries = await _storage.ListAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.Name == "Первая");
        Assert.Contains(summaries, s => s.Name == "Вторая");
    }

    [Fact]
    public async Task DeleteAsync_RemovesProgramFromList()
    {
        var program = SampleProgram("Удаляемая");
        await _storage.SaveAsync(program);

        await _storage.DeleteAsync(program.Id);

        var summaries = await _storage.ListAsync();
        Assert.DoesNotContain(summaries, s => s.Id == program.Id);
    }

    [Fact]
    public async Task LoadAsync_UnknownId_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.LoadAsync(Guid.NewGuid()));
    }
}
