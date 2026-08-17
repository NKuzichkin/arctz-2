using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace ArctZ.Services.App;

/// <summary>
/// Desktop exposes IControlledApplicationLifetime.Shutdown(), which lets the
/// process wind down normally. Android/iOS/Browser don't have an equivalent
/// sanctioned "quit" API, so those fall back to a best-effort process kill;
/// Environment.Exit throws PlatformNotSupportedException on Browser (WASM
/// sandbox), which is swallowed rather than surfaced to the user.
/// </summary>
public sealed class AppExitService : IAppExitService
{
    public void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IControlledApplicationLifetime controlled)
        {
            controlled.Shutdown();
            return;
        }

        try
        {
            Environment.Exit(0);
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
