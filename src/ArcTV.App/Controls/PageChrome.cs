using System.Windows;

namespace ArcTV.App.Controls;

/// <summary>
/// How much room a page leaves at its top for the top bar: all of it while the bar is showing, a small gap once the bar has slid away, so a page's heading moves up into the freed
/// space instead of leaving an empty black strip. The shell animates it with the bar.
/// </summary>
public sealed class PageChrome : FrameworkElement
{
    /// <summary>The height of the top bar.</summary>
    public const double BarHeight = 124;

    /// <summary>The gap left at the top once the bar is gone.</summary>
    public const double HiddenGap = 18;

    public static PageChrome Instance { get; } = new();

    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(nameof(Inset), typeof(double), typeof(PageChrome), new PropertyMetadata(BarHeight));

    public double Inset { get => (double)GetValue(InsetProperty); set => SetValue(InsetProperty, value); }
}
