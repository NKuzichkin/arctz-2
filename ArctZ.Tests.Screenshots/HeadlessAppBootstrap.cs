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
/// <summary>
/// Every test class in this project must join this collection. AppBuilder.Setup binds
/// Avalonia to whichever thread called it, and HeadlessAppBootstrap calls it exactly once
/// per process — so all Avalonia work in this assembly has to happen on that one thread.
/// xUnit runs each test class as its own collection by default and can dispatch different
/// collections onto different ThreadPool threads; whichever class loses that race then
/// builds controls from a foreign thread. That surfaces as unrelated-looking failures —
/// "The calling thread cannot access this object", or a corrupted static registry inside
/// MenuItem/RadioButtonGroupManager — rather than as an obvious threading error.
///
/// DisableParallelization alone would not be enough: it only stops collections running
/// concurrently, not from running on different threads. Naming ONE collection for every
/// class is what makes xUnit treat them as a single sequential unit. Same reasoning, and
/// same fix, as ArctZ.Tests' "AvaloniaHeadless" collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class HeadlessAppCollection
{
    public const string Name = "HeadlessApp";
}

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
