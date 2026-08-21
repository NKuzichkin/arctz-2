using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Screenshots.Support;
using ArctZ.ViewModels;
using ArctZ.Views;

namespace ArctZ.Tests.Screenshots;

[Collection(HeadlessAppCollection.Name)]
public class ScreenshotGalleryTests
{
    public ScreenshotGalleryTests() => HeadlessAppBootstrap.EnsureInitialized();

    [Fact]
    public async Task GeneratesScreenshotsForAllScreens()
    {
        var screenshotsDir = Path.Combine(RepoRoot.Find(), "screenshots");
        Directory.CreateDirectory(screenshotsDir);

        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var storage = new FakeProgramStorage();

        var demoProgram = new JibProgram { Name = "Демо программа" };
        demoProgram.KeyPoints.Add(new KeyPoint(
            Guid.NewGuid(), 1, "Точка 1", new MachinePose(0, 0, 0, 0),
            DwellSeconds: 0, TransitionSeconds: 5, EaseMode.None, ContinuousBlend: false));
        demoProgram.KeyPoints.Add(new KeyPoint(
            Guid.NewGuid(), 2, "Точка 2", new MachinePose(120, 45, 80, 15),
            DwellSeconds: 1, TransitionSeconds: 5, EaseMode.None, ContinuousBlend: false));
        await storage.SaveAsync(demoProgram);

        var connection = new ConnectionViewModel(
            realTransport,
            () => demoTransport,
            new DeviceSessionFactory(MachineLimits.Default),
            new SingleRealDeviceEndpointProvider());
        // Hand-driven clock and progress timer: the playback screens need several seconds of
        // program time to have elapsed, and the About report shows uptime — both would otherwise
        // depend on how long capture happened to take.
        var clock = new MutableClock(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
        var progressTimer = new ManualPeriodicTimer();
        var programViewModel = new ProgramViewModel(
            connection, storage, new TrajectoryCompiler(), new FakeAppExitService(),
            now: () => clock.Now, progressTimer: progressTimer);

        var mainView = new MainView { DataContext = programViewModel };
        VisualTreeAnimationStripper.StripRevealAnimations(mainView);

        var window = new Window { Width = 390, Height = 844, Content = mainView };
        window.Classes.Add("print");
        window.Show();
        // TextOptions.TextRenderingMode (the API the obsolete warning points to) doesn't exist in the
        // installed Avalonia 11.3.17 — RenderOptions.SetTextRenderingMode is the only usable API for this.
#pragma warning disable CS0618 // Type or member is obsolete
        RenderOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);
#pragma warning restore CS0618
        Dispatcher.UIThread.RunJobs();

        var runStartedAt = DateTime.UtcNow;
        var context = new ScreenCatalogContext(demoTransport, progressTimer, clock);
        var screens = ScreenCatalog.Build(context);
        WriteScreensMarkdown(screenshotsDir, screens);

        try
        {
            for (var i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                var setupTask = screen.Setup(programViewModel);
                Dispatcher.UIThread.RunJobs();

                var frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException($"No frame captured for screen '{screen.Id}'.");
                frame.Save(Path.Combine(screenshotsDir, $"{i + 1:D2}-{screen.Id}.png"));

                var teardownTask = screen.Teardown(programViewModel);
                await setupTask;
                await teardownTask;
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            window.Close();
        }

        // The playback screens leave one PlayCommand running across three entries; their last
        // Teardown stops it and feeds the acks it was still waiting on. Draining it here rather
        // than inside that Teardown keeps the driver loop's own awaits on already-completed
        // tasks, and the timeout means a future change that leaves it genuinely stuck fails the
        // test instead of hanging the whole `dotnet test` run.
        if (context.PlaybackTask is { } playbackTask)
        {
            Dispatcher.UIThread.RunJobs();
            var finished = await Task.WhenAny(playbackTask, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(playbackTask, finished);
            await playbackTask;
        }

        // After the loop, not before it: a run that throws partway then leaves the previously
        // committed gallery intact instead of deleting files it never got round to replacing.
        DeleteScreenshotsNotInCatalog(screenshotsDir, screens);

        AssertRewrittenSince(Path.Combine(screenshotsDir, "SCREENS.md"), runStartedAt);
        for (var i = 0; i < screens.Count; i++)
        {
            AssertRewrittenSince(Path.Combine(screenshotsDir, $"{i + 1:D2}-{screens[i].Id}.png"), runStartedAt);
        }
    }

    // The repo lives on a VirtualBox shared folder (Z:); its mtime clock and this process's
    // DateTime.UtcNow are observed to drift by a few seconds. A stale file left over from a
    // previous (committed) run is stale by minutes/hours/days, so a generous tolerance here
    // still catches the failure mode this assertion exists for without flaking on clock skew.
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromSeconds(30);

    private static void AssertRewrittenSince(string path, DateTime runStartedAt)
    {
        Assert.True(File.Exists(path), $"Expected file to exist: {path}");
        var info = new FileInfo(path);
        Assert.True(info.LastWriteTimeUtc >= runStartedAt - ClockSkewTolerance,
            $"File was not rewritten by this run: {path} (LastWriteTimeUtc={info.LastWriteTimeUtc:O}, runStartedAt={runStartedAt:O})");
        Assert.True(info.Length > 1024, $"File is suspiciously small ({info.Length} bytes): {path}");
    }

    /// <summary>
    /// Screens are numbered by their position in the catalog, so inserting one renames every file
    /// after it and leaves the old name behind — a stale PNG that no longer matches any screen but
    /// still resolves in any document that linked to it. Only files this run is about to write are
    /// kept.
    /// </summary>
    private static void DeleteScreenshotsNotInCatalog(string screenshotsDir, System.Collections.Generic.IReadOnlyList<ScreenDefinition> screens)
    {
        var expected = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < screens.Count; i++)
        {
            expected.Add($"{i + 1:D2}-{screens[i].Id}.png");
        }

        foreach (var file in Directory.EnumerateFiles(screenshotsDir, "*.png"))
        {
            if (!expected.Contains(Path.GetFileName(file)))
            {
                File.Delete(file);
            }
        }
    }

    private static void WriteScreensMarkdown(string screenshotsDir, System.Collections.Generic.IReadOnlyList<ScreenDefinition> screens)
    {
        var md = new StringBuilder();
        md.AppendLine("# Экраны ArctZ — галерея скриншотов");
        md.AppendLine();
        md.AppendLine("Сгенерировано автоматически тестом `ScreenshotGalleryTests` (`ArctZ.Tests.Screenshots`). Не редактировать вручную — при следующем запуске тест перезапишет файл.");
        md.AppendLine();
        md.AppendLine("| # | Экран | Файл |");
        md.AppendLine("|---|---|---|");
        for (var i = 0; i < screens.Count; i++)
        {
            var fileName = $"{i + 1:D2}-{screens[i].Id}.png";
            md.AppendLine($"| {i + 1} | {screens[i].Title} (`{screens[i].Id}`) | [{fileName}]({fileName}) |");
        }

        File.WriteAllText(Path.Combine(screenshotsDir, "SCREENS.md"), md.ToString());
    }
}
