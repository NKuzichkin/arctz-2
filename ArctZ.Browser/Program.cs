using ArctZ;
using ArctZ.Browser;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using Avalonia;
using Avalonia.Browser;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using System.Threading.Tasks;

internal sealed partial class Program
{
    private static Task Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddArctZCore();
        services.AddSingleton<IDeviceTransport, NotSupportedDeviceTransport>();
        services.AddSingleton<IProgramStorage, InMemoryProgramStorage>();
        App.Services = services.BuildServiceProvider();

        return BuildAvaloniaApp()
            .WithInterFont()
#if DEBUG
            .WithDeveloperTools()
#endif
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
