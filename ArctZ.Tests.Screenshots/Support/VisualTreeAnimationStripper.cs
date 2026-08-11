using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ArctZ.Tests.Screenshots.Support;

/// <summary>
/// MainView.axaml fades in its header/content panels via "reveal-1"/"reveal-3"
/// classes (Opacity 0→1 over real time, FillMode=Forward). Headless capture
/// doesn't advance that animation clock, so a frame taken without this would
/// risk landing on Opacity 0. Removing the classes before the first render
/// stops the animation selectors from ever matching, so the panels render at
/// their default (fully opaque) state deterministically.
/// </summary>
public static class VisualTreeAnimationStripper
{
    private static readonly string[] RevealClassNames = { "reveal-1", "reveal-2", "reveal-3" };

    public static void StripRevealAnimations(Control root)
    {
        RemoveRevealClasses(root);
        foreach (var descendant in root.GetVisualDescendants().OfType<StyledElement>())
        {
            RemoveRevealClasses(descendant);
        }
    }

    private static void RemoveRevealClasses(StyledElement element)
    {
        foreach (var name in RevealClassNames)
        {
            element.Classes.Remove(name);
        }
    }
}
