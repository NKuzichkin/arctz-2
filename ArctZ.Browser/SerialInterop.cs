using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace ArctZ.Browser;

/// <summary>
/// Thin JS-interop bridge to wwwroot/serial.js (Web Serial API). ConnectAsync semantics
/// (reuse-saved-port-first, request-port-as-fallback) live in BrowserSerialTransport, not here.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class SerialInterop
{
    private const string ModuleName = "serial.js";

    /// <summary>Must be awaited once, before any other member of this class is used.</summary>
    public static async Task InitializeAsync()
    {
        await JSHost.ImportAsync(ModuleName, "./serial.js");
    }

    [JSImport("isSupported", ModuleName)]
    internal static partial bool IsSupported();

    [JSImport("requestPort", ModuleName)]
    internal static partial Task<bool> RequestPortAsync();

    [JSImport("reopenSavedPort", ModuleName)]
    internal static partial Task<bool> ReopenSavedPortAsync();

    [JSImport("write", ModuleName)]
    internal static partial Task WriteAsync(byte[] bytes);

    [JSImport("closePort", ModuleName)]
    internal static partial Task ClosePortAsync();

    [JSExport]
    internal static void OnLineReceived(string line) => BrowserSerialTransport.RaiseLineReceived(line);

    [JSExport]
    internal static void OnDisconnected() => BrowserSerialTransport.RaiseDisconnected();
}
