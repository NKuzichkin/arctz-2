using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
using ReactiveUI;
using ReactiveUI.Builder;

namespace ArctZ.Tests;

/// <summary>
/// Runs once when the test assembly loads (before any test executes). No test head calls
/// UseReactiveUI — VirtualJoystickTests' AvaloniaHeadlessBootstrap does build an Avalonia
/// AppBuilder, but never calls UseReactiveUI on it — so without this every ReactiveObject-derived
/// ViewModel's constructor throws
/// InvalidOperationException("ReactiveUI has not been initialized"). Also forces
/// RxSchedulers.MainThreadScheduler to ImmediateScheduler for the whole process —
/// simpler and safer than a per-test save/restore guard, since it's global mutable
/// state and xUnit can run different test classes in parallel.
/// </summary>
internal static class ReactiveUIBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();

        RxSchedulers.MainThreadScheduler = ImmediateScheduler.Instance;
    }
}
