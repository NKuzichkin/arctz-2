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
    /// The palette is restricted to black, white, and the two approved greys
    /// (#CCCCCC, #666666); Light1/Light2 collapse to #CCCCCC and Light3 uses
    /// the darker #666666 so it still reads as distinct.
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
            resources["SystemAccentColorLight1"] = Color.Parse("#CCCCCC");
            resources["SystemAccentColorLight2"] = Color.Parse("#CCCCCC");
            resources["SystemAccentColorLight3"] = Color.Parse("#666666");
            resources["SystemAccentColorDark1"] = Colors.Black;
            resources["SystemAccentColorDark2"] = Colors.Black;
            resources["SystemAccentColorDark3"] = Colors.Black;
        }
    }
}
