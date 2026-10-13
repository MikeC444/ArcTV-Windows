using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ArcTV.App.Controls;

/// <summary>The lift a poster gives under the pointer on Home's rows (a little bigger, settling back when it leaves), for cards made in code.</summary>
public static class HoverLift
{
    public static void Attach(FrameworkElement card, double scale = 1.06)
    {
        var transform = new ScaleTransform(1, 1);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = transform;
        card.MouseEnter += (_, _) => Animate(transform, scale, 0.14);
        card.MouseLeave += (_, _) => Animate(transform, 1, 0.18);
    }

    private static void Animate(ScaleTransform transform, double to, double seconds)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}
