using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ArctZ;

namespace ArctZ.Tests.Themes;

// Regression coverage for the StaticResource-in-Setter bug: Avalonia resolves
// {StaticResource ...} inside a Setter.Value once, when the owning Styles
// collection is populated by AvaloniaXamlLoader.Load. For Application.Styles
// that happens in App.Initialize() *before* PrintTheme.Apply merges the
// print-mode palette a few lines later in the same method, so a
// StaticResource Setter permanently bakes in the dark HUD color regardless of
// --theme=print. DynamicResource re-resolves live and is immune. Unlike
// PrintThemeTests (which applies PrintTheme.Apply to an isolated
// ResourceDictionary and only proves the palette keys resolve correctly in
// isolation), this test drives the real Application.Current.Resources/Styles
// pipeline that TestApp wires up, which is the only place the bug actually
// manifests.
[Collection("AvaloniaHeadless")]
public class PrintThemeRenderingTests
{
    public PrintThemeRenderingTests() => AvaloniaHeadlessBootstrap.EnsureInitialized();

    [Fact]
    public void ButtonPrimary_UnderPrintTheme_RendersWhiteBackground()
    {
        var resources = Application.Current!.Resources;
        var mergedCountBefore = resources.MergedDictionaries.Count;
        var previousAccentColors = new (string Key, object? Value)[]
        {
            ("SystemAccentColor", resources.TryGetResource("SystemAccentColor", null, out var v0) ? v0 : null),
            ("SystemAccentColorLight1", resources.TryGetResource("SystemAccentColorLight1", null, out var v1) ? v1 : null),
            ("SystemAccentColorLight2", resources.TryGetResource("SystemAccentColorLight2", null, out var v2) ? v2 : null),
            ("SystemAccentColorLight3", resources.TryGetResource("SystemAccentColorLight3", null, out var v3) ? v3 : null),
            ("SystemAccentColorDark1", resources.TryGetResource("SystemAccentColorDark1", null, out var v4) ? v4 : null),
            ("SystemAccentColorDark2", resources.TryGetResource("SystemAccentColorDark2", null, out var v5) ? v5 : null),
            ("SystemAccentColorDark3", resources.TryGetResource("SystemAccentColorDark3", null, out var v6) ? v6 : null),
        };

        Window? window = null;
        try
        {
            PrintTheme.Apply(resources);

            var button = new Button();
            button.Classes.Add("primary");
            window = new Window { Content = button };
            window.Classes.Add("print");
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var brush = Assert.IsType<SolidColorBrush>(button.Background);
            Assert.Equal(Colors.White, brush.Color);
        }
        finally
        {
            window?.Close();

            while (resources.MergedDictionaries.Count > mergedCountBefore)
            {
                resources.MergedDictionaries.RemoveAt(resources.MergedDictionaries.Count - 1);
            }

            foreach (var (key, value) in previousAccentColors)
            {
                if (value is not null)
                {
                    resources[key] = value;
                }
                else
                {
                    resources.Remove(key);
                }
            }
        }
    }
}
