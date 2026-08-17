using System;
using ArctZ.Services.App;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Simulation;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.App;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Tests.Services.Device;

public class ServiceCollectionExtensionsTests
{
    /// <summary>
    /// Mirrors what each platform head does: AddArctZCore() plus the two
    /// registrations it deliberately leaves out (real IDeviceTransport,
    /// IProgramStorage). Catches DI wiring mistakes (missing registration,
    /// wrong lifetime, circular dependency) that would otherwise only
    /// surface at app startup on a real device.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IDeviceTransport, FakeDeviceTransport>();
        services.AddSingleton<IProgramStorage, FakeProgramStorage>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddArctZCore_ResolvesConnectionViewModel()
    {
        using var provider = BuildProvider();

        var viewModel = provider.GetRequiredService<ConnectionViewModel>();

        Assert.NotNull(viewModel);
    }

    [Fact]
    public void AddArctZCore_ResolvesProgramViewModel()
    {
        using var provider = BuildProvider();

        var viewModel = provider.GetRequiredService<ProgramViewModel>();

        Assert.NotNull(viewModel);
    }

    [Fact]
    public void AddArctZCore_RegistersSharedViewModelsAsSingletons()
    {
        using var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<ConnectionViewModel>(), provider.GetRequiredService<ConnectionViewModel>());
        Assert.Same(provider.GetRequiredService<ProgramViewModel>(), provider.GetRequiredService<ProgramViewModel>());
    }

    [Fact]
    public void AddArctZCore_DemoTransportFactory_CreatesIndependentMockTransports()
    {
        using var provider = BuildProvider();
        var createDemoTransport = provider.GetRequiredService<Func<IDeviceTransport>>();

        var first = createDemoTransport();
        var second = createDemoTransport();

        Assert.NotSame(first, second);
        Assert.IsType<MockDeviceTransport>(first);
    }

    [Fact]
    public void AddArctZCore_RegistersDefaultDeviceEndpointProvider()
    {
        using var provider = BuildProvider();

        var endpointProvider = provider.GetRequiredService<IDeviceEndpointProvider>();

        Assert.IsType<SingleRealDeviceEndpointProvider>(endpointProvider);
    }

    [Fact]
    public void AddArctZCore_RegistersANoOpBackgroundSessionHostByDefault()
    {
        using var provider = BuildProvider();

        Assert.IsType<NullBackgroundSessionHost>(provider.GetRequiredService<IBackgroundSessionHost>());
    }

    /// <summary>Голова платформы регистрирует свой хост после AddArctZCore() — последняя
    /// регистрация обязана победить, иначе на Android остался бы no-op.</summary>
    [Fact]
    public void AddArctZCore_LetsAPlatformHeadReplaceTheBackgroundSessionHost()
    {
        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IDeviceTransport, FakeDeviceTransport>();
        services.AddSingleton<IProgramStorage, FakeProgramStorage>();
        services.AddSingleton<IBackgroundSessionHost, FakeBackgroundSessionHost>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<FakeBackgroundSessionHost>(provider.GetRequiredService<IBackgroundSessionHost>());
    }

    [Fact]
    public void AddArctZCore_RegistersTheBackgroundSessionCoordinator()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<BackgroundSessionCoordinator>());
    }
}
