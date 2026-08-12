import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

// serial.js (imported from C# via JSHost.ImportAsync in SerialInterop) calls back
// into .NET through this global — it has no other way to reach the assembly's
// [JSExport] methods since it isn't itself loaded through the dotnet runtime's
// module resolution.
globalThis.__arctzSerialExports = await dotnetRuntime.getAssemblyExports(config.mainAssemblyName);

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
