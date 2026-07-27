using System;
using ArctZ.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Services.Device;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything platform-independent. Each head additionally
    /// registers its own IDeviceTransport for the real device (see Task 24)
    /// — this method deliberately does not touch that registration.
    /// </summary>
    public static IServiceCollection AddArctZCore(this IServiceCollection services)
    {
        services.AddSingleton(MachineLimits.Default);
        services.AddSingleton<IDeviceSessionFactory, DeviceSessionFactory>();
        services.AddSingleton<Func<IDeviceTransport>>(sp => () => new Simulation.MockDeviceTransport(
            sp.GetRequiredService<MachineLimits>(),
            new SystemPeriodicTimer(),
            TimeSpan.FromMilliseconds(100)));
        services.AddTransient<ConnectionViewModel>();
        return services;
    }
}
