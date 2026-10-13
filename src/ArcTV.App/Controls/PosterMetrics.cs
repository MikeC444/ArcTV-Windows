using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ArcTV.App.Controls;

/// <summary>
/// How big the posters (and the cards beside them) are drawn: every poster is designed at one size and drawn at <see cref="Scale"/> times it, and the scale follows the width of the
/// window, so about the same number fit across whether the window is small or the screen is large. The shell tells it the window's width.
/// </summary>
public sealed class PosterMetrics : INotifyPropertyChanged
{
    /// <summary>The window width at which posters are drawn at their designed size.</summary>
    public const double ReferenceWidth = 1700;

    public const double Smallest = 0.6;
    public const double Largest = 1.5;

    public static PosterMetrics Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Scale { get; private set; } = 1;

    /// <summary>The scale for a window this wide (in steps of 0.05, so a drag does not redraw every poster at every pixel).</summary>
    public static double ScaleFor(double windowWidth) => Math.Round(Math.Clamp(windowWidth / ReferenceWidth, Smallest, Largest) * 20) / 20;

    public void Update(double windowWidth)
    {
        if (windowWidth <= 0) return;
        var next = ScaleFor(windowWidth);
        if (Math.Abs(next - Scale) < 0.001) return;
        Scale = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scale)));
    }

    /// <summary>Makes a card made in code follow the scale (the cards made in XAML bind to it).</summary>
    public static void Apply(FrameworkElement card)
    {
        var transform = new ScaleTransform();
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            BindingOperations.SetBinding(transform, property, new Binding(nameof(Scale)) { Source = Instance, Mode = BindingMode.OneWay });
        card.LayoutTransform = transform;
    }
}
