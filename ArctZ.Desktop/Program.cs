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
            services.AddSingleton<IDeviceTransport, DesktopSerialTransport>();
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
