using ArctZ;
using ArctZ.Browser;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Avalonia;
using Avalonia.Browser;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI.Avalonia;
using System.Runtime.Versioning;
using System.Threading.Tasks;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        await SerialInterop.InitializeAsync();

        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IDeviceTransport, BrowserSerialTransport>();
        services.AddSingleton<IProgramStorage, InMemoryProgramStorage>();
        App.Services = services.BuildServiceProvider();

        await BuildAvaloniaApp()
            .WithInterFont()
            .UseReactiveUI(b => b.WithAvalonia())
#if DEBUG
            .WithDeveloperTools()
#endif
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
