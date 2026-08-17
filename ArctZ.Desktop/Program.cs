using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI.Avalonia;
using System;
using System.IO;

namespace ArctZ.Desktop
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            App.PrintMode = PrintThemeOptions.IsPrintMode(args);

            var services = new ServiceCollection();
            services.AddArctZCore();
            // TEMPORARY: JogDiagnosticsTransport timestamps every byte in both directions so the
            // residual jog overrun after stick release can be measured. Restore the plain
            // registration below once that is done.
            services.AddSingleton<IDeviceTransport>(_ => new JogDiagnosticsTransport(new DesktopSerialTransport()));

            // TEMPORARY: 150 ms status polling (default 250 ms). 50 ms was tried first and saturated
            // the BT SPP link — corrupted status lines, an error:3 from a mangled command, and ok
            // latencies in the seconds — which distorted the very timings being measured. Registered
            // after AddArctZCore so it overrides the default factory. Remove with the diagnostics.
            services.AddSingleton<IDeviceSessionFactory>(sp => new DeviceSessionFactory(
                sp.GetRequiredService<MachineLimits>(),
                TimeSpan.FromMilliseconds(150)));
            services.AddSingleton<IDeviceEndpointProvider, DesktopComPortEndpointProvider>();
            services.AddSingleton<IProgramStorage>(_ => new JsonFileProgramStorage(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArctZ", "Programs")));
            App.Services = services.BuildServiceProvider();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseReactiveUI(b => b.WithAvalonia())
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
