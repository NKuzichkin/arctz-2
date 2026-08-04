using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;

namespace ArctZ.Tests;

/// <summary>
/// Minimal Avalonia application for headless UI tests. Deliberately does not
/// use the real ArctZ.App: that class resolves ViewModels from a static DI
/// container and constructs MainWindow/MainView on startup, none of which a
/// control-level test (e.g. VirtualJoystick) needs. It merges just enough —
/// the HUD color palette and the VirtualJoystick ControlTheme — for
/// VirtualJoystick's template to apply without unresolved resource
/// lookups.
/// </summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        // Order matters: Colors.axaml must already be merged in by the time
        // HudControls.axaml's styles are added below, because HudControls.axaml still
        // uses eager StaticResource lookups for its font-related keys (HudFontBody,
        // HudFontMono, HudFontSizeBody, HudFontSizeTelemetry) that resolve when the
        // style is added. VirtualJoystick.axaml and HudControls.axaml's own
        // Hud*Color/Hud*Brush references use DynamicResource and re-resolve live, so
        // they don't strictly need this ordering, but keeping Colors.axaml first for
        // all of them avoids relying on that distinction.
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://ArctZ.Tests/"))
        {
            Source = new Uri("avares://ArctZ/Themes/Colors.axaml"),
        });

        Styles.Add(new StyleInclude(new Uri("avares://ArctZ.Tests/"))
        {
            Source = new Uri("avares://ArctZ/Themes/VirtualJoystick.axaml"),
        });

        Styles.Add(new StyleInclude(new Uri("avares://ArctZ.Tests/"))
        {
            Source = new Uri("avares://ArctZ/Themes/HudControls.axaml"),
        });
    }
}

/// <summary>
/// Bootstraps the Avalonia headless platform once per test process. There is
/// no Avalonia.Headless.XUnit package involved here: that package requires
/// xunit.v3, which collides (duplicate FactAttribute) with the xunit v2
/// packages the rest of ArctZ.Tests is built on. AppBuilder.Setup can only
/// run once per process, so this is guarded by Lazy&lt;T&gt; and must be
/// invoked (via <see cref="EnsureInitialized"/>) from the same thread that
/// will later touch any Avalonia control, before doing so.
/// </summary>
public static class AvaloniaHeadlessBootstrap
{
    private static readonly Lazy<bool> Init = new(() =>
    {
        AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
        return true;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureInitialized() => _ = Init.Value;
}
