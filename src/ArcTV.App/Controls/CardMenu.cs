using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ArcTV.Core.Addons;

namespace ArcTV.App.Controls;

/// <summary>
/// Marks a card as standing for a title, so that right-clicking it opens the card menu. Posters and the banner's buttons already carry their title as their data; the cards made in
/// code (Continue Watching, Picked for you) carry it here.
/// </summary>
public static class CardMenu
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.RegisterAttached("Item", typeof(MetaPreview), typeof(CardMenu));

    /// <summary>True on a card of Picked for you (its menu then offers to take the title out of that row).</summary>
    public static readonly DependencyProperty FromPickedProperty = DependencyProperty.RegisterAttached("FromPicked", typeof(bool), typeof(CardMenu));

    public static void SetItem(DependencyObject element, MetaPreview? value) => element.SetValue(ItemProperty, value);

    public static MetaPreview? GetItem(DependencyObject element) => (MetaPreview?)element.GetValue(ItemProperty);

    public static void SetFromPicked(DependencyObject element, bool value) => element.SetValue(FromPickedProperty, value);

    public static bool GetFromPicked(DependencyObject element) => (bool)element.GetValue(FromPickedProperty);

    /// <summary>The title of the card the pointer is over (<paramref name="start"/> is what was clicked, somewhere inside it), if it is on one.</summary>
    public static (MetaPreview Item, bool FromPicked)? Find(DependencyObject start)
    {
        for (var element = start; element is not null; element = Parent(element))
        {
            if (GetItem(element) is { } marked) return (marked, GetFromPicked(element));
            if (element is Button { DataContext: MetaPreview preview }) return (preview, false);
        }

        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
