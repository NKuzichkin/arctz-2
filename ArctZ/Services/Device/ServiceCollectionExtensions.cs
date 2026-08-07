using System;
using ArctZ.Services.Program;
using ArctZ.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Services.Device;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything platform-independent. Each head additionally
    /// registers its own IDeviceTransport (real transport) and
    /// IProgramStorage (storage location) — this method deliberately does
    /// not touch either.
    /// </summary>
    public static IServiceCollection AddArctZCore(this IServiceCollection services)
    {
        services.AddSingleton(MachineLimits.Default);
        services.AddSingleton<IDeviceSessionFactory, DeviceSessionFactory>();
        services.AddSingleton<Func<IDeviceTransport>>(sp => () => new Simulation.MockDeviceTransport(
            sp.GetRequiredService<MachineLimits>(),
            new SystemPeriodicTimer(),
            TimeSpan.FromMilliseconds(100)));
        services.AddSingleton<ITrajectoryCompiler, TrajectoryCompiler>();
        services.AddSingleton<ConnectionViewModel>();
        services.AddSingleton<ProgramViewModel>(sp => new ProgramViewModel(
            sp.GetRequiredService<ConnectionViewModel>(),
            sp.GetRequiredService<IProgramStorage>(),
            sp.GetRequiredService<ITrajectoryCompiler>(),
            new SystemPeriodicTimer(),
            TimeSpan.FromMilliseconds(100)));
        return services;
    }
}
