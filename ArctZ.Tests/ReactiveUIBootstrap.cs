using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
using ReactiveUI;
using ReactiveUI.Builder;

namespace ArctZ.Tests;

/// <summary>
/// Runs once when the test assembly loads (before any test executes). Plain xUnit
/// tests never touch Avalonia's AppBuilder/UseReactiveUI, so without this every
/// ReactiveObject-derived ViewModel's constructor throws
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
