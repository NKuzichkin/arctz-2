using Android.App;
using Android.Runtime;
using ArctZ.Services.App;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI.Avalonia;
using System.IO;

namespace ArctZ.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            var services = new ServiceCollection();
            services.AddArctZCore();
            var permissions = new AndroidPermissions();
            services.AddSingleton(permissions);
            services.AddSingleton<IDeviceTransport>(new AndroidBluetoothTransport(permissions));
            services.AddSingleton<IDeviceEndpointProvider>(new AndroidBluetoothEndpointProvider(permissions));
            // После AddArctZCore() намеренно: при резолве одиночного сервиса побеждает последняя
            // регистрация, и эта вытесняет NullBackgroundSessionHost из ядра.
            services.AddSingleton<IBackgroundSessionHost, AndroidBackgroundSessionHost>();
            services.AddSingleton<IProgramStorage>(_ => new JsonFileProgramStorage(
                Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "ArctZ", "Programs")));
            App.Services = services.BuildServiceProvider();

            return base.CustomizeAppBuilder(builder)
                .UseReactiveUI(b => b.WithAvalonia())
                .WithInterFont();
        }
    }
}
