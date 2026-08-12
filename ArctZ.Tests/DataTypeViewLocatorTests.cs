using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;
using ArctZ.Views;
using Avalonia.Controls.Templates;
using Zafiro.Avalonia.ViewLocators;

namespace ArctZ.Tests;

// AvaloniaHeadlessBootstrap.EnsureInitialized (see TestApp.cs) guards Avalonia's
// AppBuilder.Setup with a process-wide Lazy<T> and documents that it "must be
// invoked from the same thread that will later touch any Avalonia control." That
// held by accident while VirtualJoystickTests was the only consumer, since xUnit
// runs each test class as its own collection by default, and different
// collections can end up dispatched onto different OS threads (via the
// ThreadPool) — even an assembly-wide `[CollectionBehavior(DisableTestParallelization
// = true)]` only prevents collections running *concurrently*, it does not
// guarantee they share a thread. Explicitly merging both Avalonia-headless test
// classes into ONE named collection (below) makes xUnit treat them as a single
// sequential unit instead of two separately-serialized ones, which is what
// actually needs to hold for AvaloniaHeadlessBootstrap's single "whichever thread
// ran Setup() first" assumption to be safe.
[CollectionDefinition("AvaloniaHeadless", DisableParallelization = true)]
public class AvaloniaHeadlessCollection { }

[Collection("AvaloniaHeadless")]
public class DataTypeViewLocatorTests
{
    [Fact]
    public void Build_ConnectionViewModel_ResolvesToConnectionView()
    {
        AvaloniaHeadlessBootstrap.EnsureInitialized();
        IDataTemplate locator = new DataTypeViewLocator();
        var vm = new ConnectionViewModel(
            new FakeDeviceTransport(),
            () => new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            new SingleRealDeviceEndpointProvider());

        Assert.True(locator.Match(vm));
        Assert.IsType<ConnectionView>(locator.Build(vm));
    }
}
