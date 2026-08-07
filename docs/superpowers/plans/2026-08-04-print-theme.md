# Print-тема для монохромной печати — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `--theme=print` launch flag to `ArctZ.Desktop` that swaps the dark HUD palette for a black-border/white-background theme suited to screenshots printed on a monochrome printer, without touching the app's behavior when the flag is absent.

**Architecture:** A pure arg-parsing helper (`PrintThemeOptions.IsPrintMode`) decides the flag in `Program.cs` before Avalonia starts. `App.axaml.cs` reads a static `App.PrintMode` flag and, when set, switches `RequestedThemeVariant` to `Light`, merges a self-contained `Themes/PrintColors.axaml` palette (black/white/grey, no dependency on Colors.axaml's own brushes re-resolving), and overrides the 7 `SystemAccentColor*` keys directly (they're declared as direct child keys in App.axaml, not reachable via a merged dictionary). A `"print"` `StyleClass` on the root `Window` scopes the handful of behavioral overrides that colors alone can't express — VirtualJoystick's glow/blur/drop-shadow, and a border-thickness distinction between `Button.primary`/`Button.danger`.

**Tech Stack:** Avalonia UI (.NET 10), xUnit + `Avalonia.Headless` for tests (existing `ArctZ.Tests` project, headless-Avalonia patterns already established in `TestApp.cs`).

## Global Constraints

- Only `ArctZ.Desktop` gets this feature — Android/iOS/Browser are out of scope (see spec "Область действия").
- Flag syntax is exactly `--theme=print`; any other/missing value leaves current behavior unchanged.
- Palette: black (`#000000`) borders/text/accent, white (`#FFFFFF`) backgrounds, grey (`#CCCCCC`/`#666666`) only for hover/pressed/disabled/secondary-text states — no gradients, blur, or drop shadows anywhere in print mode.
- Zero behavior change when the flag is absent — the existing dark HUD theme must render pixel-identical to before this work.
- Design source of truth: `docs/superpowers/specs/2026-08-04-print-theme-design.md`. One deviation from that spec is intentional and explained in Task 3: `PrintColors.axaml` redefines both the `Hud*Color` **and** `Hud*Brush` keys (not just the Colors), because Colors.axaml's brushes reference their colors via `StaticResource`, and relying on that indirection to pick up a later-merged override is a risk not worth taking — a self-contained palette file sidesteps it entirely.

---

## File Structure

| File | Responsibility |
|---|---|
| `ArctZ/PrintThemeOptions.cs` (new) | Pure `--theme=print` arg parsing. No Avalonia dependency — usable from `ArctZ.Desktop` and unit-testable from `ArctZ.Tests`. |
| `ArctZ/PrintTheme.cs` (new) | Applies the print palette to an `IResourceDictionary`: merges `PrintColors.axaml`, overrides `SystemAccentColor*`. |
| `ArctZ/Themes/PrintColors.axaml` (new) | Self-contained monochrome palette — every `Hud*Color`/`Hud*Brush` key redefined directly. |
| `ArctZ/Themes/Colors.axaml` (modify) | Add `HudBackgroundDeepColor` (joystick base gradient inner stop) and `HudScrimBrush` (modal overlay, currently a literal hex repeated 5× in `MainView.axaml`). |
| `ArctZ/Themes/VirtualJoystick.axaml` (modify) | Replace 6 hardcoded hex colors with `StaticResource`; name the ambient-glow `Ellipse`; add `Window.print`-scoped overrides that strip glow/blur/drop-shadow and force opaque strokes. |
| `ArctZ/Themes/HudControls.axaml` (modify) | Add `Window.print Button.danger` border-thickness override so primary/danger stay distinguishable without color. |
| `ArctZ/Views/MainView.axaml` (modify) | Replace 5× `Background="#CC0A0E12"` with `{StaticResource HudScrimBrush}`. |
| `ArctZ/App.axaml.cs` (modify) | `PrintMode` static property; apply theme in `Initialize()`; add `"print"` class to `MainWindow` in `OnFrameworkInitializationCompleted`. |
| `ArctZ.Desktop/Program.cs` (modify) | Set `App.PrintMode` from `args` before building the Avalonia app. |
| `ArctZ.Tests/PrintThemeOptionsTests.cs` (new) | Tests for arg parsing. |
| `ArctZ.Tests/Themes/ColorsTests.cs` (new) | Tests for the two new `Colors.axaml` keys. |
| `ArctZ.Tests/Themes/PrintThemeTests.cs` (new) | Tests for `PrintTheme.Apply`. |
| `ArctZ.Tests/Components/VirtualJoystickPrintThemeTests.cs` (new) | Tests for the joystick's print-mode style overrides. |
| `ArctZ.Tests/Themes/HudControlsPrintThemeTests.cs` (new) | Tests for the danger-button print override. |
| `ArctZ.Tests/TestApp.cs` (modify) | Register `HudControls.axaml` styles (needed by the new HudControls test; currently only Colors.axaml + VirtualJoystick.axaml are merged). |

---

## Task 1: `PrintThemeOptions` — parse `--theme=print`

**Files:**
- Create: `ArctZ/PrintThemeOptions.cs`
- Test: `ArctZ.Tests/PrintThemeOptionsTests.cs`

**Interfaces:**
- Produces: `ArctZ.PrintThemeOptions.IsPrintMode(IEnumerable<string> args) : bool` — used by Task 8 (`Program.cs`).

- [ ] **Step 1: Write the failing tests**

```csharp
// ArctZ.Tests/PrintThemeOptionsTests.cs
using System;
using ArctZ;

namespace ArctZ.Tests;

public class PrintThemeOptionsTests
{
    [Fact]
    public void IsPrintMode_WithPrintFlag_ReturnsTrue()
    {
        Assert.True(PrintThemeOptions.IsPrintMode(new[] { "--theme=print" }));
    }

    [Fact]
    public void IsPrintMode_WithNoArgs_ReturnsFalse()
    {
        Assert.False(PrintThemeOptions.IsPrintMode(Array.Empty<string>()));
    }

    [Fact]
    public void IsPrintMode_WithUnrelatedArgs_ReturnsFalse()
    {
        Assert.False(PrintThemeOptions.IsPrintMode(new[] { "--theme=dark", "--verbose" }));
    }

    [Fact]
    public void IsPrintMode_FlagAmongOtherArgs_ReturnsTrue()
    {
        Assert.True(PrintThemeOptions.IsPrintMode(new[] { "--verbose", "--theme=print" }));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter PrintThemeOptionsTests`
Expected: FAIL to build — `PrintThemeOptions` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// ArctZ/PrintThemeOptions.cs
using System.Collections.Generic;
using System.Linq;

namespace ArctZ
{
    public static class PrintThemeOptions
    {
        private const string PrintFlag = "--theme=print";

        public static bool IsPrintMode(IEnumerable<string> args) => args.Contains(PrintFlag);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter PrintThemeOptionsTests`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/PrintThemeOptions.cs ArctZ.Tests/PrintThemeOptionsTests.cs
git commit -m "feat: add --theme=print flag parsing"
```

---

## Task 2: `Colors.axaml` new keys + `MainView.axaml` scrim cleanup

**Files:**
- Modify: `ArctZ/Themes/Colors.axaml:7-9` (add `HudBackgroundDeepColor`), `:36-39` (add `HudScrimBrush`)
- Modify: `ArctZ/Views/MainView.axaml:194,222,238,253,300` (replace literal with `{StaticResource HudScrimBrush}`)
- Test: `ArctZ.Tests/Themes/ColorsTests.cs`

**Interfaces:**
- Produces: `HudBackgroundDeepColor` (Color key, `#0C1116`), `HudScrimBrush` (Brush key, `#CC0A0E12`) — consumed by Task 4 (joystick) and already-existing `MainView.axaml` modal overlays respectively. Task 3's `PrintColors.axaml` redefines both under the same key names.

- [ ] **Step 1: Write the failing tests**

```csharp
// ArctZ.Tests/Themes/ColorsTests.cs
using Avalonia;
using Avalonia.Media;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class ColorsTests
{
    public ColorsTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void HudScrimBrush_ResolvesToExpectedColor()
    {
        Application.Current!.TryGetResource("HudScrimBrush", null, out var value);

        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.Equal(Color.Parse("#CC0A0E12"), brush.Color);
    }

    [Fact]
    public void HudBackgroundDeepColor_ResolvesToExpectedColor()
    {
        Application.Current!.TryGetResource("HudBackgroundDeepColor", null, out var value);

        Assert.Equal(Color.Parse("#0C1116"), value);
    }
}
```

(`Application.Current` here is the shared headless `TestApp`, which already merges `Colors.axaml` at process start — see `TestApp.cs`. Reading resources doesn't mutate shared state, so this is safe alongside other tests.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ColorsTests`
Expected: FAIL — both `TryGetResource` calls return `false`/`null`, keys don't exist yet.

- [ ] **Step 3: Add the two resource keys**

In `ArctZ/Themes/Colors.axaml`, replace:

```xml
  <Color x:Key="HudBackgroundColor">#0A0E12</Color>
  <Color x:Key="HudPanelColor">#12181F</Color>
```

with:

```xml
  <Color x:Key="HudBackgroundColor">#0A0E12</Color>
  <!-- Slightly lighter than HudBackgroundColor: used only as the inner gradient
       stop on VirtualJoystick's base pad, not a general-purpose background. -->
  <Color x:Key="HudBackgroundDeepColor">#0C1116</Color>
  <Color x:Key="HudPanelColor">#12181F</Color>
```

Then replace:

```xml
  <SolidColorBrush x:Key="HudTextPrimaryBrush" Color="{StaticResource HudTextPrimaryColor}" />
  <SolidColorBrush x:Key="HudTextSecondaryBrush" Color="{StaticResource HudTextSecondaryColor}" />

  <!-- Typography: JetBrains Mono for telemetry/numeric readouts, Manrope for interface text.
```

with:

```xml
  <SolidColorBrush x:Key="HudTextPrimaryBrush" Color="{StaticResource HudTextPrimaryColor}" />
  <SolidColorBrush x:Key="HudTextSecondaryBrush" Color="{StaticResource HudTextSecondaryColor}" />

  <!-- Dark translucent scrim behind modal overlays (key point editor, rename,
       confirmation, library, connection modal). -->
  <SolidColorBrush x:Key="HudScrimBrush" Color="#CC0A0E12" />

  <!-- Typography: JetBrains Mono for telemetry/numeric readouts, Manrope for interface text.
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter ColorsTests`
Expected: PASS (2/2).

- [ ] **Step 5: Replace the 5 literal scrim backgrounds in MainView.axaml**

In `ArctZ/Views/MainView.axaml`, all 5 occurrences of:

```xml
Background="#CC0A0E12"
```

become:

```xml
Background="{StaticResource HudScrimBrush}"
```

(Lines 194, 222, 238, 253, 300 — same literal string each time, safe to replace all occurrences at once.)

- [ ] **Step 6: Confirm no visual regression**

Run: `dotnet build ArctZ/ArctZ.csproj` — expect success (confirms the XAML still parses; the actual pixel value is unchanged since `HudScrimBrush`'s color is the same literal that was inline before).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/Themes/Colors.axaml ArctZ/Views/MainView.axaml ArctZ.Tests/Themes/ColorsTests.cs
git commit -m "refactor: extract HudScrimBrush and HudBackgroundDeepColor resource keys"
```

---

## Task 3: `PrintColors.axaml` + `PrintTheme.Apply`

**Files:**
- Create: `ArctZ/Themes/PrintColors.axaml`
- Create: `ArctZ/PrintTheme.cs`
- Test: `ArctZ.Tests/Themes/PrintThemeTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (self-contained by design — see Global Constraints deviation note).
- Produces: `ArctZ.PrintTheme.Apply(IResourceDictionary resources) : void` — used by Task 7 (`App.axaml.cs`).

- [ ] **Step 1: Write the failing tests**

```csharp
// ArctZ.Tests/Themes/PrintThemeTests.cs
using Avalonia.Controls;
using Avalonia.Media;
using ArctZ;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class PrintThemeTests
{
    public PrintThemeTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void Apply_OverridesHudBackgroundBrushToWhite()
    {
        var resources = new ResourceDictionary();

        PrintTheme.Apply(resources);

        resources.TryGetResource("HudBackgroundBrush", null, out var value);
        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.Equal(Colors.White, brush.Color);
    }

    [Fact]
    public void Apply_OverridesHudAccentBrushToBlack()
    {
        var resources = new ResourceDictionary();

        PrintTheme.Apply(resources);

        resources.TryGetResource("HudAccentBrush", null, out var value);
        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void Apply_SetsSystemAccentColorToBlack()
    {
        var resources = new ResourceDictionary();

        PrintTheme.Apply(resources);

        Assert.Equal(Colors.Black, resources["SystemAccentColor"]);
    }

    [Fact]
    public void Apply_SetsAllSevenSystemAccentColorVariants()
    {
        var resources = new ResourceDictionary();

        PrintTheme.Apply(resources);

        Assert.True(resources.ContainsKey("SystemAccentColorLight1"));
        Assert.True(resources.ContainsKey("SystemAccentColorLight2"));
        Assert.True(resources.ContainsKey("SystemAccentColorLight3"));
        Assert.True(resources.ContainsKey("SystemAccentColorDark1"));
        Assert.True(resources.ContainsKey("SystemAccentColorDark2"));
        Assert.True(resources.ContainsKey("SystemAccentColorDark3"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter PrintThemeTests`
Expected: FAIL to build — `PrintTheme` does not exist.

- [ ] **Step 3: Create the palette file**

```xml
<!-- ArctZ/Themes/PrintColors.axaml -->
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- Monochrome palette for --theme=print: black borders/text/accent on white,
       grey reserved for hover/pressed/disabled/secondary-text states. Redefines
       every Hud*Color/Hud*Brush key from Colors.axaml directly — including the
       Brush keys, not just their underlying Color keys — so this file is
       self-contained and doesn't depend on Colors.axaml's own brushes
       re-resolving their nested StaticResource after this dictionary merges. -->

  <Color x:Key="HudBackgroundColor">#FFFFFF</Color>
  <Color x:Key="HudPanelColor">#FFFFFF</Color>
  <Color x:Key="HudPanelElevatedColor">#FFFFFF</Color>
  <Color x:Key="HudBackgroundDeepColor">#FFFFFF</Color>
  <Color x:Key="HudBorderColor">#000000</Color>
  <Color x:Key="HudBorderStrongColor">#000000</Color>

  <Color x:Key="HudAccentColor">#000000</Color>
  <Color x:Key="HudAccentDimColor">#CCCCCC</Color>
  <Color x:Key="HudAccentBrightColor">#000000</Color>

  <Color x:Key="HudWarningColor">#000000</Color>
  <Color x:Key="HudWarningDimColor">#CCCCCC</Color>

  <Color x:Key="HudTextPrimaryColor">#000000</Color>
  <Color x:Key="HudTextSecondaryColor">#666666</Color>

  <SolidColorBrush x:Key="HudBackgroundBrush" Color="#FFFFFF" />
  <SolidColorBrush x:Key="HudPanelBrush" Color="#FFFFFF" />
  <SolidColorBrush x:Key="HudPanelElevatedBrush" Color="#FFFFFF" />
  <SolidColorBrush x:Key="HudBorderBrush" Color="#000000" />
  <SolidColorBrush x:Key="HudBorderStrongBrush" Color="#000000" />

  <SolidColorBrush x:Key="HudAccentBrush" Color="#000000" />
  <SolidColorBrush x:Key="HudAccentDimBrush" Color="#CCCCCC" />
  <SolidColorBrush x:Key="HudAccentBrightBrush" Color="#000000" />

  <SolidColorBrush x:Key="HudWarningBrush" Color="#000000" />
  <SolidColorBrush x:Key="HudWarningDimBrush" Color="#CCCCCC" />

  <SolidColorBrush x:Key="HudTextPrimaryBrush" Color="#000000" />
  <SolidColorBrush x:Key="HudTextSecondaryBrush" Color="#666666" />

  <SolidColorBrush x:Key="HudScrimBrush" Color="#B3FFFFFF" />

</ResourceDictionary>
```

- [ ] **Step 4: Implement `PrintTheme.Apply`**

```csharp
// ArctZ/PrintTheme.cs
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;

namespace ArctZ
{
    /// <summary>
    /// Applies the monochrome print palette used by --theme=print. Merges
    /// PrintColors.axaml and overrides SystemAccentColor plus its 6 shade
    /// variants — App.axaml declares those as direct child keys of the root
    /// resource dictionary, which take priority over anything added later via
    /// MergedDictionaries, so they can only be overridden by reassigning them.
    /// </summary>
    public static class PrintTheme
    {
        public static void Apply(IResourceDictionary resources)
        {
            resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://ArctZ/"))
            {
                Source = new Uri("avares://ArctZ/Themes/PrintColors.axaml"),
            });

            resources["SystemAccentColor"] = Colors.Black;
            resources["SystemAccentColorLight1"] = Color.Parse("#333333");
            resources["SystemAccentColorLight2"] = Color.Parse("#4D4D4D");
            resources["SystemAccentColorLight3"] = Color.Parse("#666666");
            resources["SystemAccentColorDark1"] = Colors.Black;
            resources["SystemAccentColorDark2"] = Colors.Black;
            resources["SystemAccentColorDark3"] = Colors.Black;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter PrintThemeTests`
Expected: PASS (4/4). If `Apply_OverridesHudBackgroundBrushToWhite` or `Apply_OverridesHudAccentBrushToBlack` fails with the *old* (dark) color instead, it means `PrintColors.axaml`'s `avares://` URI isn't resolving — double check the `Source` URI matches the file's actual path exactly (`avares://ArctZ/Themes/PrintColors.axaml`).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Themes/PrintColors.axaml ArctZ/PrintTheme.cs ArctZ.Tests/Themes/PrintThemeTests.cs
git commit -m "feat: add print-theme palette and PrintTheme.Apply"
```

---

## Task 4: VirtualJoystick — hardcoded colors → `StaticResource`

**Files:**
- Modify: `ArctZ/Themes/VirtualJoystick.axaml:17,26-27,67-68,75,97`

**Interfaces:**
- Consumes: `HudPanelElevatedColor`, `HudBackgroundDeepColor`, `HudBorderStrongColor`, `HudAccentColor` (all from Task 2/existing Colors.axaml).
- Produces: names `PART_Glow` on the ambient-glow `Ellipse` — consumed by Task 5's tests and style overrides.

This task is a pure refactor — behavior for the existing (non-print) theme must be pixel-identical, since every replaced hex value already matches its target resource key's current value exactly. There's no new testable behavior here; correctness is verified by re-running the *existing* joystick test suite (Step 3) plus a visual build check.

- [ ] **Step 1: Name the ambient-glow ellipse**

In `ArctZ/Themes/VirtualJoystick.axaml`, replace:

```xml
            <Ellipse Fill="{StaticResource HudAccentDimBrush}" Opacity="0.4" IsHitTestVisible="False">
```

with:

```xml
            <Ellipse x:Name="PART_Glow" Fill="{StaticResource HudAccentDimBrush}" Opacity="0.4" IsHitTestVisible="False">
```

- [ ] **Step 2: Replace the 6 hardcoded hex colors**

Replace:

```xml
                  <GradientStop Color="#171F27" Offset="0" />
                  <GradientStop Color="#0C1116" Offset="1" />
```

with:

```xml
                  <GradientStop Color="{StaticResource HudPanelElevatedColor}" Offset="0" />
                  <GradientStop Color="{StaticResource HudBackgroundDeepColor}" Offset="1" />
```

Replace:

```xml
                  <GradientStop Color="#2A3840" Offset="0" />
                  <GradientStop Color="#171F27" Offset="1" />
```

with:

```xml
                  <GradientStop Color="{StaticResource HudBorderStrongColor}" Offset="0" />
                  <GradientStop Color="{StaticResource HudPanelElevatedColor}" Offset="1" />
```

Replace both occurrences of:

```xml
                <DropShadowEffect Color="#3DDBD9" BlurRadius="10" Opacity="0.35" OffsetX="0" OffsetY="0" />
```

and

```xml
      <DropShadowEffect Color="#3DDBD9" BlurRadius="22" Opacity="0.85" OffsetX="0" OffsetY="0" />
```

with (respectively):

```xml
                <DropShadowEffect Color="{StaticResource HudAccentColor}" BlurRadius="10" Opacity="0.35" OffsetX="0" OffsetY="0" />
```

```xml
      <DropShadowEffect Color="{StaticResource HudAccentColor}" BlurRadius="22" Opacity="0.85" OffsetX="0" OffsetY="0" />
```

- [ ] **Step 3: Confirm no regression in the existing joystick test suite**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter VirtualJoystickTests`
Expected: PASS (all pre-existing tests, unchanged — they test pointer/force/direction geometry, not color, so this confirms the refactor didn't break template loading).

- [ ] **Step 4: Commit**

```bash
git add ArctZ/Themes/VirtualJoystick.axaml
git commit -m "refactor: move VirtualJoystick hardcoded colors to StaticResource"
```

---

## Task 5: VirtualJoystick — print-mode style overrides

**Files:**
- Modify: `ArctZ/Themes/VirtualJoystick.axaml` (append new `Style` blocks at the end, before `</Styles>`)
- Test: `ArctZ.Tests/Components/VirtualJoystickPrintThemeTests.cs`

**Interfaces:**
- Consumes: `PART_Glow`, `PART_Base`, `PART_Knob` names (Task 4); `HudBorderStrongBrush` (existing, from Colors.axaml).
- Produces: the `Window.print` StyleClass convention that Task 6 also uses — any `Window` with `Classes="print"` gets flat/monochrome joystick rendering.

- [ ] **Step 1: Write the failing tests**

```csharp
// ArctZ.Tests/Components/VirtualJoystickPrintThemeTests.cs
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArctZ.Components.VirtualJoystick;

namespace ArctZ.Tests.Components;

[Collection("AvaloniaHeadless")]
public class VirtualJoystickPrintThemeTests
{
    public VirtualJoystickPrintThemeTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    private static (Window Window, VirtualJoystick Joystick) CreateHostedJoystick(bool printMode)
    {
        var joystick = new VirtualJoystick
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var window = new Window { Content = joystick, Width = 400, Height = 400 };
        if (printMode)
        {
            window.Classes.Add("print");
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, joystick);
    }

    private static Ellipse FindPart(VirtualJoystick joystick, string name) =>
        joystick.GetVisualDescendants().OfType<Ellipse>().First(e => e.Name == name);

    [Fact]
    public void PrintMode_HidesAmbientGlow()
    {
        var (window, joystick) = CreateHostedJoystick(printMode: true);

        Assert.False(FindPart(joystick, "PART_Glow").IsVisible);

        window.Close();
    }

    [Fact]
    public void NonPrintMode_ShowsAmbientGlow()
    {
        var (window, joystick) = CreateHostedJoystick(printMode: false);

        Assert.True(FindPart(joystick, "PART_Glow").IsVisible);

        window.Close();
    }

    [Fact]
    public void PrintMode_RemovesBaseAndKnobEffects()
    {
        var (window, joystick) = CreateHostedJoystick(printMode: true);

        Assert.Null(FindPart(joystick, "PART_Base").Effect);
        Assert.Null(FindPart(joystick, "PART_Knob").Effect);

        window.Close();
    }

    [Fact]
    public void NonPrintMode_KeepsKnobDropShadow()
    {
        var (window, joystick) = CreateHostedJoystick(printMode: false);

        Assert.NotNull(FindPart(joystick, "PART_Knob").Effect);

        window.Close();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter VirtualJoystickPrintThemeTests`
Expected: `PrintMode_HidesAmbientGlow` and `PrintMode_RemovesBaseAndKnobEffects` FAIL (no print styles exist yet); the two `NonPrintMode_*` tests already PASS against current behavior.

- [ ] **Step 3: Add the print-mode style overrides**

At the end of `ArctZ/Themes/VirtualJoystick.axaml`, immediately before the closing `</Styles>` tag, add:

```xml
  <!-- Print theme (Window.print, set by App when launched with --theme=print):
       the ambient glow, blur, and drop-shadow read as a grey smear on
       monochrome hardcopy even once their color resolves to black via
       PrintColors.axaml, so print mode drops them outright instead of
       recoloring them. The base/knob strokes get a fully opaque brush since
       their template strokes are intentionally translucent (Opacity 0.3-0.85)
       for the dark HUD look, which would read as pale grey on a white page. -->
  <Style Selector="Window.print local|VirtualJoystick /template/ Ellipse#PART_Glow">
    <Setter Property="IsVisible" Value="False" />
  </Style>

  <Style Selector="Window.print local|VirtualJoystick /template/ Ellipse#PART_Base">
    <Setter Property="Effect" Value="{x:Null}" />
    <Setter Property="Stroke" Value="{StaticResource HudBorderStrongBrush}" />
  </Style>

  <Style Selector="Window.print local|VirtualJoystick /template/ Ellipse#PART_Knob">
    <Setter Property="Effect" Value="{x:Null}" />
    <Setter Property="Stroke" Value="{StaticResource HudBorderStrongBrush}" />
  </Style>

  <Style Selector="Window.print local|VirtualJoystick:active /template/ Ellipse#PART_Base">
    <Setter Property="Stroke" Value="{StaticResource HudBorderStrongBrush}" />
  </Style>

  <Style Selector="Window.print local|VirtualJoystick:active /template/ Ellipse#PART_Knob">
    <Setter Property="Effect" Value="{x:Null}" />
  </Style>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter VirtualJoystickPrintThemeTests`
Expected: PASS (4/4).

- [ ] **Step 5: Re-run the full joystick suite to confirm no regression**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter VirtualJoystick`
Expected: PASS (original `VirtualJoystickTests` + new `VirtualJoystickPrintThemeTests`).

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Themes/VirtualJoystick.axaml ArctZ.Tests/Components/VirtualJoystickPrintThemeTests.cs
git commit -m "feat: strip joystick glow/blur/shadow under print theme"
```

---

## Task 6: `Button.danger` border-thickness distinction under print

**Files:**
- Modify: `ArctZ/Themes/HudControls.axaml` (append at end)
- Modify: `ArctZ.Tests/TestApp.cs` (register `HudControls.axaml` styles)
- Test: `ArctZ.Tests/Themes/HudControlsPrintThemeTests.cs`

**Interfaces:**
- Consumes: the `Window.print` StyleClass convention (Task 5).

- [ ] **Step 1: Register `HudControls.axaml` in the shared test app**

In `ArctZ.Tests/TestApp.cs`, `Initialize()` currently merges only `Colors.axaml` (resources) and `VirtualJoystick.axaml` (styles). Add a second `StyleInclude` for `HudControls.axaml` right after the existing one:

```csharp
        Styles.Add(new StyleInclude(new Uri("avares://ArctZ.Tests/"))
        {
            Source = new Uri("avares://ArctZ/Themes/VirtualJoystick.axaml"),
        });

        Styles.Add(new StyleInclude(new Uri("avares://ArctZ.Tests/"))
        {
            Source = new Uri("avares://ArctZ/Themes/HudControls.axaml"),
        });
```

This is additive only — it doesn't change what any existing test observes, since none of the current tests use `Button`/`ComboBox`/etc. styling.

- [ ] **Step 2: Write the failing tests**

```csharp
// ArctZ.Tests/Themes/HudControlsPrintThemeTests.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArctZ.Tests.Themes;

[Collection("AvaloniaHeadless")]
public class HudControlsPrintThemeTests
{
    public HudControlsPrintThemeTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    private static (Window Window, Button Button) CreateHostedDangerButton(bool printMode)
    {
        var button = new Button();
        button.Classes.Add("danger");

        var window = new Window { Content = button };
        if (printMode)
        {
            window.Classes.Add("print");
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, button);
    }

    [Fact]
    public void PrintMode_ThickensDangerButtonBorder()
    {
        var (window, button) = CreateHostedDangerButton(printMode: true);

        Assert.Equal(new Thickness(2), button.BorderThickness);

        window.Close();
    }

    [Fact]
    public void NonPrintMode_KeepsDefaultDangerButtonBorder()
    {
        var (window, button) = CreateHostedDangerButton(printMode: false);

        Assert.Equal(new Thickness(1), button.BorderThickness);

        window.Close();
    }
}
```

- [ ] **Step 3: Run tests to verify `PrintMode_ThickensDangerButtonBorder` fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter HudControlsPrintThemeTests`
Expected: `PrintMode_ThickensDangerButtonBorder` FAILS (expects `Thickness(2)`, gets `Thickness(1)`); `NonPrintMode_KeepsDefaultDangerButtonBorder` already PASSES.

- [ ] **Step 4: Add the print override**

At the end of `ArctZ/Themes/HudControls.axaml`, immediately before the closing `</Styles>` tag, add:

```xml
  <!-- Print theme (Window.print, set by App when launched with --theme=print):
       primary and danger buttons both resolve to black/grey once accent and
       warning colors collapse to the monochrome palette, so danger is told
       apart by a heavier border instead of a color. -->
  <Style Selector="Window.print Button.danger">
    <Setter Property="BorderThickness" Value="2" />
  </Style>
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter HudControlsPrintThemeTests`
Expected: PASS (2/2).

- [ ] **Step 6: Run the full test suite to confirm the `TestApp.cs` change caused no regressions**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS (all tests, including everything from Tasks 1-5).

- [ ] **Step 7: Commit**

```bash
git add ArctZ/Themes/HudControls.axaml ArctZ.Tests/TestApp.cs ArctZ.Tests/Themes/HudControlsPrintThemeTests.cs
git commit -m "feat: distinguish danger button from primary under print theme"
```

---

## Task 7: `App.axaml.cs` — wire up `PrintMode`

**Files:**
- Modify: `ArctZ/App.axaml.cs`

**Interfaces:**
- Consumes: `ArctZ.PrintTheme.Apply(IResourceDictionary)` (Task 3).
- Produces: `ArctZ.App.PrintMode : bool` (static, settable) — consumed by Task 8 (`Program.cs`). The `"print"` class added to `MainWindow` here is what makes Tasks 5/6's `Window.print` selectors actually match in the real app.

There's no dedicated automated test for this task: it wires together already-tested pieces (`PrintTheme.Apply`, already verified in Task 3) with Desktop-lifecycle code (`OnFrameworkInitializationCompleted`) that no test project currently reaches (`ArctZ.Tests` references only `ArctZ.csproj`, not `ArctZ.Desktop.csproj`). Correctness is verified end-to-end in Task 9.

- [ ] **Step 1: Add the `PrintMode` property and apply the theme in `Initialize()`**

In `ArctZ/App.axaml.cs`, replace:

```csharp
    public partial class App : Application
    {
        public static IServiceProvider? Services { get; set; }

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void RegisterViews()
        {
            DataTypeViewLocator.RegisterGlobal<ConnectionViewModel, ConnectionView>();
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }
```

with:

```csharp
    public partial class App : Application
    {
        public static IServiceProvider? Services { get; set; }

        public static bool PrintMode { get; set; }

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void RegisterViews()
        {
            DataTypeViewLocator.RegisterGlobal<ConnectionViewModel, ConnectionView>();
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            if (PrintMode)
            {
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                PrintTheme.Apply(Resources);
            }
        }
```

- [ ] **Step 2: Add the `"print"` class to the root `Window`**

In the same file, replace:

```csharp
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = viewModel
                };
            }
```

with:

```csharp
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = viewModel
                };

                if (PrintMode)
                {
                    desktop.MainWindow.Classes.Add("print");
                }
            }
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build ArctZ/ArctZ.csproj`
Expected: succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add ArctZ/App.axaml.cs
git commit -m "feat: wire App.PrintMode into theme variant and print-theme resources"
```

---

## Task 8: `Program.cs` — read the flag from `args`

**Files:**
- Modify: `ArctZ.Desktop/Program.cs`

**Interfaces:**
- Consumes: `ArctZ.PrintThemeOptions.IsPrintMode` (Task 1), `ArctZ.App.PrintMode` (Task 7).

- [ ] **Step 1: Set `App.PrintMode` before building the Avalonia app**

In `ArctZ.Desktop/Program.cs`, replace:

```csharp
        [STAThread]
        public static void Main(string[] args)
        {
            var services = new ServiceCollection();
```

with:

```csharp
        [STAThread]
        public static void Main(string[] args)
        {
            App.PrintMode = PrintThemeOptions.IsPrintMode(args);

            var services = new ServiceCollection();
```

(No new `using` needed: `Program` is declared in `namespace ArctZ.Desktop`, and C# searches enclosing namespace declarations before requiring a `using` — `App` is already referenced unqualified two lines below this change for the same reason, so `PrintThemeOptions` resolves the same way.)

- [ ] **Step 2: Build and smoke-test both code paths**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add ArctZ.Desktop/Program.cs
git commit -m "feat: parse --theme=print on ArctZ.Desktop startup"
```

---

## Task 9: End-to-end manual verification

**Files:** none (verification only).

- [ ] **Step 1: Run the full automated test suite**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, all tests (pre-existing + everything added in Tasks 1, 2, 3, 5, 6).

- [ ] **Step 2: Launch without the flag and confirm no regression**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`
Check: dark HUD background, cyan accent on buttons/joystick/telemetry, joystick glow/blur/drop-shadow all present — i.e. visually identical to before this branch. Close the app.

- [ ] **Step 3: Launch with `--theme=print` and check the checklist from the spec**

Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj -- --theme=print`
Check, per `docs/superpowers/specs/2026-08-04-print-theme-design.md` → "Тестирование":
- Standard controls (`Button`, `ComboBox`, `TextBox`, `ListBox`, `ProgressBar`): black border, white background, no residual color.
- Joystick at rest: flat white/black, no glow halo, no blur, no drop shadow.
- Joystick while dragging (`:active`): still flat, no drop shadow appears on the knob.
- One modal (e.g. trigger a program-delete confirmation): light/white scrim behind it, not the dark one.
- `Button.primary` vs `Button.danger`: visually distinguishable by border weight, not just color.
- Telemetry text: readable black-on-white, not pale cyan.

- [ ] **Step 4: Report results**

If everything in Step 3 matches, the feature is complete — no further commit needed (Step 3 is inspection only, no code changes). If something doesn't match, note the specific control/selector and fix it in the relevant task's file before considering the plan done.
