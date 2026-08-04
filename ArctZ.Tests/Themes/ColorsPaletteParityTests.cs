using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArctZ.Tests.Themes;

// Colors.axaml (dark HUD theme) and PrintColors.axaml (print theme) are
// intentionally independent, self-contained files that each redefine the
// same set of Hud*Color/Hud*Brush keys with different literal values (see
// PrintColors.axaml's header comment). Colors.axaml also defines Hud*Font*
// keys (HudFontBody, HudFontSizeSmall, ...) that PrintColors.axaml
// deliberately does NOT redefine — typography doesn't change between themes,
// so those are excluded from this comparison; only the *Color/*Brush palette
// is expected to have parity. Nothing enforces that the two files stay in
// sync: if someone adds a new Hud*Color/Hud*Brush key to one file and
// forgets the other, the app still builds and every existing test still
// passes, but print mode (or the dark theme) silently falls back to
// whatever default resolution applies to the missing key. This test fails
// loudly instead.
[Collection("AvaloniaHeadless")]
public class ColorsPaletteParityTests
{
    public ColorsPaletteParityTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void ColorsAndPrintColors_DefineTheSameSetOfHudKeys()
    {
        var colors = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://ArctZ/Themes/Colors.axaml"));
        var printColors = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://ArctZ/Themes/PrintColors.axaml"));

        var colorsHudKeys = HudKeys(colors);
        var printColorsHudKeys = HudKeys(printColors);

        var missingFromPrint = colorsHudKeys.Except(printColorsHudKeys).ToList();
        var missingFromColors = printColorsHudKeys.Except(colorsHudKeys).ToList();

        Assert.True(
            missingFromPrint.Count == 0 && missingFromColors.Count == 0,
            $"Hud* key mismatch between Colors.axaml and PrintColors.axaml. " +
            $"Missing from PrintColors.axaml: [{string.Join(", ", missingFromPrint)}]. " +
            $"Missing from Colors.axaml: [{string.Join(", ", missingFromColors)}].");
    }

    private static string[] HudKeys(ResourceDictionary dictionary) =>
        dictionary.Keys
            .OfType<string>()
            .Where(key => key.StartsWith("Hud", StringComparison.Ordinal)
                && (key.EndsWith("Color", StringComparison.Ordinal) || key.EndsWith("Brush", StringComparison.Ordinal)))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
}
