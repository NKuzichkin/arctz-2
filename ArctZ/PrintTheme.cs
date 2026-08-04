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
