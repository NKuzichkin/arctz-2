using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using UIKit;

namespace ArctZ.iOS
{
    // The UIApplicationDelegate for the application. This class is responsible for launching the
    // User Interface of the application, as well as listening (and optionally responding) to
    // application events from iOS.
    [Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            var services = new ServiceCollection();
            services.AddArctZCore();
            services.AddSingleton<IDeviceTransport, NotSupportedDeviceTransport>();
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            services.AddSingleton<IProgramStorage>(_ => new JsonFileProgramStorage(Path.Combine(documentsPath, "ArctZ", "Programs")));
            App.Services = services.BuildServiceProvider();

            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
