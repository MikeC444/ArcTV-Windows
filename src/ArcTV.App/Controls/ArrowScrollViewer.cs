using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ArcTV.App.Controls;

/// <summary>
/// A row that scrolls sideways: arrows appear at its ends when the pointer is over it (only the ones that lead somewhere), Shift plus the wheel moves along it, and both
/// glide. Its look is the "ArrowRowScroll" style in the theme.
/// </summary>
public class ArrowScrollViewer : ScrollViewer
{
    private SmoothScroller? _smooth;

    public ArrowScrollViewer()
    {
        SetResourceReference(StyleProperty, "ArrowRowScroll");
    }

    private SmoothScroller Smooth => _smooth ??= new SmoothScroller(this, 1.1, 0.25, horizontal: true);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("RowLeft") is Button left) left.Click += (_, _) => Smooth.By(-ViewportWidth * 0.8);
        if (GetTemplateChild("RowRight") is Button right) right.Click += (_, _) => Smooth.By(ViewportWidth * 0.8);
    }

    /// <summary>Shift plus the wheel moves along the row; a plain wheel is left for the page to scroll.</summary>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Smooth.Wheel(e);
        base.OnPreviewMouseWheel(e);
    }
}

/// <summary>Shows the left arrow once the row has been scrolled along.</summary>
public sealed class PastStartVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double offset && offset > 1 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Shows the right arrow while there is more of the row to the right (values: how far along it is, how far it can go).</summary>
public sealed class MoreToComeVisibility : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [double offset, double furthest] && furthest - offset > 1 ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
