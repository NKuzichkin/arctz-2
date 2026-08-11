using System;
using System.IO;
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

        var screens = ScreenCatalog.Build();
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

        Assert.True(File.Exists(Path.Combine(screenshotsDir, "01-connection.png")));
    }
}
