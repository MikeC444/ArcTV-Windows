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

    /// <summary>
    /// How far down the row the arrows' middle sits, when the middle of the whole row is not the middle of what matters (an episode's picture has its name and a few lines
    /// under it): set it to the middle of the pictures. Left alone, the arrows are centred on the row.
    /// </summary>
    public static readonly DependencyProperty ArrowCenterProperty = DependencyProperty.Register(nameof(ArrowCenter), typeof(double), typeof(ArrowScrollViewer), new PropertyMetadata(double.NaN));

    public double ArrowCenter { get => (double)GetValue(ArrowCenterProperty); set => SetValue(ArrowCenterProperty, value); }

    /// <summary>True for a row of posters (or the cards beside them): the arrows' height then follows the posters' size, which follows the window's.</summary>
    public bool ScalesWithPosters { get; set; }

    private Button? _leftArrow;
    private Button? _rightArrow;

    public ArrowScrollViewer()
    {
        SetResourceReference(StyleProperty, "ArrowRowScroll");
        Loaded += (_, _) =>
        {
            PosterMetrics.Instance.PropertyChanged -= OnPosterScale;
            PosterMetrics.Instance.PropertyChanged += OnPosterScale;
            if (ScalesWithPosters) PlaceArrows();
        };
        Unloaded += (_, _) => PosterMetrics.Instance.PropertyChanged -= OnPosterScale;
    }

    private void OnPosterScale(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!CheckAccess())
        {
            PosterMetrics.Instance.PropertyChanged -= OnPosterScale; // a row from a window that is gone (its thread is not this one): it is not listening any more
            return;
        }

        if (ScalesWithPosters) PlaceArrows();
    }

    private void PlaceArrows()
    {
        if (_leftArrow is not null) Place(_leftArrow);
        if (_rightArrow is not null) Place(_rightArrow);
    }

    private SmoothScroller Smooth => _smooth ??= new SmoothScroller(this, 1.1, 0.12, horizontal: true);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("RowLeft") is Button left)
        {
            left.Click += (_, _) => Smooth.By(-ViewportWidth * 0.8);
            Place(_leftArrow = left);
        }

        if (GetTemplateChild("RowRight") is Button right)
        {
            right.Click += (_, _) => Smooth.By(ViewportWidth * 0.8);
            Place(_rightArrow = right);
        }
    }

    /// <summary>Moves an arrow so its middle is at <see cref="ArrowCenter"/> (when that is set) instead of the row's middle.</summary>
    private void Place(Button arrow)
    {
        if (double.IsNaN(ArrowCenter)) return;
        arrow.VerticalAlignment = VerticalAlignment.Top;
        var center = ArrowCenter * (ScalesWithPosters ? PosterMetrics.Instance.Scale : 1);
        arrow.Margin = new Thickness(8, Math.Max(0, center - arrow.Height / 2), 8, 0);
    }

    /// <summary>A sideways swipe (two fingers on a touchpad, a tilt wheel): <paramref name="delta"/> is positive towards the right, 120 for a notch.</summary>
    public void Swipe(int delta) => Smooth.By(delta * 1.4);

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
