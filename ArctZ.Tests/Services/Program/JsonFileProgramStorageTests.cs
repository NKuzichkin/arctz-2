using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
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

    [Fact]
    public void Location_IsTheDirectoryProgramsAreStoredIn()
    {
        Assert.Equal(_directory, _storage.Location);
    }

    [Fact]
    public void Location_IsUnknownForAStorageThatIsNotOnDisk()
    {
        IProgramStorage storage = new FakeProgramStorage();

        Assert.Null(storage.Location);
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
        program.KeyPoints.Add(new KeyPoint(Guid.NewGuid(), 1, "A", new MachinePose(1, 2, 3, 4), DwellSeconds: 0, TransitionSeconds: 5, EaseMode.None, ContinuousBlend: false));
        program.KeyPoints.Add(new KeyPoint(Guid.NewGuid(), 2, "B", new MachinePose(5, 6, 7, 8), DwellSeconds: 1.5, TransitionSeconds: 5, EaseMode.EaseInOut, ContinuousBlend: true));
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
        Assert.Equal(2, loaded.KeyPoints.Count);
        Assert.Equal(program.KeyPoints[0].Pose, loaded.KeyPoints[0].Pose);
        Assert.Equal(1.5, loaded.KeyPoints[1].DwellSeconds);
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

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsCompletionSettings()
    {
        var program = SampleProgram("С повторами");
        program.CompletionMode = ProgramCompletionMode.PingPong;
        program.ReturnToStartOnFinish = true;
        program.RepeatCount = 7;

        await _storage.SaveAsync(program);
        var loaded = await _storage.LoadAsync(program.Id);

        Assert.Equal(ProgramCompletionMode.PingPong, loaded.CompletionMode);
        Assert.True(loaded.ReturnToStartOnFinish);
        Assert.Equal(7, loaded.RepeatCount);
    }

    [Fact]
    public async Task LoadAsync_JsonWithoutCompletionFields_DefaultsToStopWithNoReturnAndNoRepeatLimit()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(_directory);
        var legacyJson = $$"""
    {
      "Id": "{{id}}",
      "Name": "Старая программа",
      "KeyPoints": []
    }
    """;
        await File.WriteAllTextAsync(Path.Combine(_directory, $"{id}.json"), legacyJson);

        var loaded = await _storage.LoadAsync(id);

        Assert.Equal(ProgramCompletionMode.Stop, loaded.CompletionMode);
        Assert.False(loaded.ReturnToStartOnFinish);
        Assert.Null(loaded.RepeatCount);
    }

    /// <summary>
    /// Pins the production-safety claim the G93 design relies on: a pre-G93 program file has
    /// "FeedRateUnitsPerMin" and no "TransitionSeconds" at all. System.Text.Json silently skips
    /// the unknown member and leaves TransitionSeconds at its default (0) rather than throwing —
    /// today, because Options carries no [JsonConstructor]/required member/RespectRequiredConstructorParameters/
    /// source-generated context that would change that. If any of those were ever introduced, this
    /// test (not just the InverseTimeMove/TrajectoryCompiler rescue tests) is what would catch it.
    /// </summary>
    [Fact]
    public async Task LoadAsync_LegacyFileWithFeedRateInsteadOfTransitionSeconds_TransitionSecondsDefaultsToZero()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(_directory);
        var legacyJson = $$"""
    {
      "Id": "{{id}}",
      "Name": "Старая программа с подачей",
      "KeyPoints": [
        {
          "Id": "{{Guid.NewGuid()}}",
          "Number": 1,
          "Label": "A",
          "Pose": { "X": 0, "Y": 0, "Z": 0, "A": 0 },
          "DwellSeconds": 0,
          "FeedRateUnitsPerMin": 500,
          "Ease": 0,
          "ContinuousBlend": false
        },
        {
          "Id": "{{Guid.NewGuid()}}",
          "Number": 2,
          "Label": "B",
          "Pose": { "X": 60, "Y": 0, "Z": 0, "A": 0 },
          "DwellSeconds": 0,
          "FeedRateUnitsPerMin": 500,
          "Ease": 0,
          "ContinuousBlend": false
        }
      ]
    }
    """;
        await File.WriteAllTextAsync(Path.Combine(_directory, $"{id}.json"), legacyJson);

        var loaded = await _storage.LoadAsync(id);

        Assert.Equal(0, loaded.KeyPoints[0].TransitionSeconds);
        Assert.Equal(0, loaded.KeyPoints[1].TransitionSeconds);

        // End-to-end: the rescue actually happens — TrajectoryCompiler must emit the 5-second
        // default (F12), not F<huge> from an unclamped 60/0. Point A also compiles to its own
        // self-move (segment 0, rescued the same way), so pick the move to B specifically.
        var steps = new TrajectoryCompiler().Compile(loaded);
        var motionLine = steps
            .Select(s => ((GCodeLineCommand)s.Command).Line)
            .Single(l => l.StartsWith("G93", StringComparison.Ordinal) && l.Contains("X60", StringComparison.Ordinal));

        Assert.Equal("G93 G1 X60 Y0 Z0 A0 F12", motionLine);
    }
}
