using System;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.App;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;
using static ArctZ.Tests.TestSupport.AsyncAssert;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelAboutTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, Func<DateTimeOffset>? now = null)
    {
        transport = new FakeDeviceTransport();
        var connection = new ConnectionViewModel(
            transport,
            () => new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            new SingleRealDeviceEndpointProvider());

        return new ProgramViewModel(connection, new FakeProgramStorage(), new TrajectoryCompiler(), new FakeAppExitService(), now);
    }

    private static KeyPoint Point(int number) =>
        new(Guid.NewGuid(), number, Label: null, MachinePose.Zero, DwellSeconds: 0, TransitionSeconds: 5, EaseMode.None, ContinuousBlend: false);

    [Fact]
    public void OpenAbout_ShowsTheDialogAndClosesTheSideMenu()
    {
        var vm = CreateViewModel(out _);
        vm.IsSideMenuOpen = true;

        vm.OpenAboutCommand.Execute(null);

        Assert.True(vm.IsAboutOpen);
        Assert.NotNull(vm.About);
        Assert.False(vm.IsSideMenuOpen);
    }

    [Fact]
    public void CloseAbout_HidesTheDialog()
    {
        var vm = CreateViewModel(out _);
        vm.OpenAboutCommand.Execute(null);

        vm.CloseAboutCommand.Execute(null);

        Assert.False(vm.IsAboutOpen);
        Assert.Null(vm.About);
    }

    [Fact]
    public void OpenAbout_ReportNamesTheApplicationAndItsVersion()
    {
        var vm = CreateViewModel(out _);

        vm.OpenAboutCommand.Execute(null);

        Assert.Contains("Название: ArctZ", vm.About!.ReportText);
        Assert.Contains("Версия:", vm.About.ReportText);
    }

    [Fact]
    public void OpenAbout_ReportDescribesTheLoadedProgram()
    {
        var vm = CreateViewModel(out _);
        vm.ProgramName = "Проезд по столу";
        vm.KeyPoints.Add(Point(1));
        vm.KeyPoints.Add(Point(2));

        vm.OpenAboutCommand.Execute(null);

        Assert.Contains("Название: Проезд по столу", vm.About!.ReportText);
        Assert.Contains("Ключевых точек: 2", vm.About.ReportText);
        Assert.Contains("Несохранённые изменения: да", vm.About.ReportText);
    }

    [Fact]
    public async Task OpenAbout_ReportCarriesTheRecordedDeviceExchange()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("error:9");

        vm.OpenAboutCommand.Execute(null);

        Assert.Contains("← error:9", vm.About!.ReportText);
    }

    [Fact]
    public async Task OpenAbout_ReportCarriesTheRecordedErrors()
    {
        var vm = CreateViewModel(out _);
        await vm.Connection.ConnectCommand.Execute();
        vm.Connection.LastAlarmCode = 1;

        vm.OpenAboutCommand.Execute(null);

        Assert.Contains("[авария] Авария FluidNC: код 1", vm.About!.ReportText);
    }

    [Fact]
    public void OpenAbout_ReportsHowLongTheAppHasBeenRunning()
    {
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var vm = CreateViewModel(out _, () => now);
        now = now.AddMinutes(5).AddSeconds(3);

        vm.OpenAboutCommand.Execute(null);

        Assert.Contains("Время работы: 5 мин 3 с", vm.About!.ReportText);
    }

    [Fact]
    public void OpenAbout_RebuildsTheReportEachTime()
    {
        var vm = CreateViewModel(out _);
        vm.OpenAboutCommand.Execute(null);
        var first = vm.About!;
        vm.CloseAboutCommand.Execute(null);
        vm.ProgramName = "Другая программа";

        vm.OpenAboutCommand.Execute(null);

        Assert.NotSame(first, vm.About);
        Assert.Contains("Название: Другая программа", vm.About!.ReportText);
    }

    [Fact]
    public void About_ExposesTheReportAsSectionsForDisplay()
    {
        var vm = CreateViewModel(out _);

        vm.OpenAboutCommand.Execute(null);

        Assert.Contains(vm.About!.Sections, s => s.Title == "Приложение");
        Assert.Contains(vm.About.Sections, s => s.Title == "Обмен с устройством");
    }

    [Fact]
    public void About_TracksThatTheReportWasCopied()
    {
        var vm = CreateViewModel(out _);
        vm.OpenAboutCommand.Execute(null);

        Assert.False(vm.About!.IsCopied);
        vm.About.MarkCopied();

        Assert.True(vm.About.IsCopied);
    }

    [Fact]
    public void OpenAbout_BeforeAnyProgramHasRun_HasExecutionLogIsFalse()
    {
        var vm = CreateViewModel(out _);

        vm.OpenAboutCommand.Execute(null);

        Assert.False(vm.About!.HasExecutionLog);
        Assert.Equal(string.Empty, vm.About.ExecutionLogText);
    }

    [Fact]
    public async Task OpenAbout_AfterACompletedRun_ExposesTheExecutionLog()
    {
        var vm = CreateViewModel(out var transport);
        await vm.Connection.ConnectCommand.Execute();

        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureKeyPointCommand.Execute(null);
        }

        for (var i = 0; i < vm.KeyPoints.Count; i++)
        {
            vm.KeyPoints[i] = vm.KeyPoints[i] with { TransitionSeconds = 5, ContinuousBlend = true };
        }

        vm.ProgramId = Guid.NewGuid();
        vm.IsDirty = false;

        var playTask = vm.PlayCommand.ExecuteAsync(null);
        transport.SimulateReceivedLine("ok");
        transport.SimulateReceivedLine("ok");
        await WaitUntilAsync(() => vm.IsAwaitingMotionIdle, TimeSpan.FromSeconds(1));
        transport.SimulateReceivedLine("<Idle|WPos:10.000,0.000,0.000,0.000|FS:0,0>");
        await playTask;

        vm.OpenAboutCommand.Execute(null);

        Assert.True(vm.About!.HasExecutionLog);
        Assert.Contains("Программа запущена", vm.About.ExecutionLogText);
        Assert.Contains("Программа завершена: Завершено", vm.About.ExecutionLogText);
    }

    [Fact]
    public void About_TracksThatTheExecutionLogWasCopied()
    {
        var vm = CreateViewModel(out _);
        vm.OpenAboutCommand.Execute(null);

        Assert.False(vm.About!.IsExecutionLogCopied);
        vm.About.MarkExecutionLogCopied();

        Assert.True(vm.About.IsExecutionLogCopied);
    }
}
