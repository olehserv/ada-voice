using System.Windows;
using System.Windows.Media;

namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// Shared by the Phase D "motion" regression tests (<see cref="BackdropCrossfadeTests"/>,
/// <see cref="StateDotMotionTests"/>) to read live property values off a rendered window's
/// visual tree, rather than pixel-sampling a screenshot.
/// </summary>
internal static class VisualTreeSearch
{
    public static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }
}
