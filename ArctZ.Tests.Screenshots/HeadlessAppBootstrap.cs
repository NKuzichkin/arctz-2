using System;
using System.Threading;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Screenshots.Support;
using Avalonia;
using Avalonia.Headless;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Tests.Screenshots;

/// <summary>
/// Boots the real ArctZ.App (not a stand-in) headless, with App.PrintMode
/// set beforehand so App.Initialize() applies the existing --theme=print
/// palette exactly as ArctZ.Desktop.exe --theme=print would. AppBuilder.Setup
/// can only run once per process — this project exists specifically so that
/// "once" can be spent on the real App instead of ArctZ.Tests' stripped-down
/// TestApp — so this is guarded the same way AvaloniaHeadlessBootstrap
/// guards ArctZ.Tests' single Setup call.
///
/// SetupWithoutStarting still runs App.OnFrameworkInitializationCompleted,
/// which resolves a ProgramViewModel from App.Services the same way every
/// real platform head's Program.cs does (via AddArctZCore() plus that head's
/// own IDeviceTransport/IProgramStorage registrations) — so App.Services must
/// be populated the same way here, with fakes standing in for the per-head
/// registrations, even though ScreenshotGalleryTests builds its own
/// ProgramViewModel/MainView from fakes independently of this one for actual
/// capture.
/// </summary>
public static class HeadlessAppBootstrap
{
    private static readonly Lazy<bool> Init = new(() =>
    {
        App.PrintMode = true;

        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IDeviceTransport>(_ => new FakeDeviceTransport());
        services.AddSingleton<IProgramStorage>(_ => new FakeProgramStorage());
        App.Services = services.BuildServiceProvider();

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        return true;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureInitialized() => _ = Init.Value;
}
