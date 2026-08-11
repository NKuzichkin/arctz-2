using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Screenshots.Support;
using ArctZ.ViewModels;
using ArctZ.Views;

namespace ArctZ.Tests.Screenshots;

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
            DwellSeconds: 0, FeedRateUnitsPerMin: 500, EaseMode.None, ContinuousBlend: false));
        demoProgram.KeyPoints.Add(new KeyPoint(
            Guid.NewGuid(), 2, "Точка 2", new MachinePose(120, 45, 80, 15),
            DwellSeconds: 1, FeedRateUnitsPerMin: 500, EaseMode.None, ContinuousBlend: false));
        await storage.SaveAsync(demoProgram);

        var connection = new ConnectionViewModel(
            realTransport,
            () => demoTransport,
            new DeviceSessionFactory(MachineLimits.Default));
        var programViewModel = new ProgramViewModel(connection, storage, new TrajectoryCompiler());

        var mainView = new MainView { DataContext = programViewModel };
        VisualTreeAnimationStripper.StripRevealAnimations(mainView);

        var window = new Window { Width = 390, Height = 844, Content = mainView };
        window.Classes.Add("print");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var screens = ScreenCatalog.Build(demoTransport);
        WriteScreensMarkdown(screenshotsDir, screens);

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

        window.Close();

        Assert.True(File.Exists(Path.Combine(screenshotsDir, "SCREENS.md")));
        for (var i = 0; i < screens.Count; i++)
        {
            Assert.True(File.Exists(Path.Combine(screenshotsDir, $"{i + 1:D2}-{screens[i].Id}.png")));
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
