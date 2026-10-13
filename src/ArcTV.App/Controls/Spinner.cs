using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ArcTV.App.Controls;

/// <summary>A loading symbol: an arc in the logo's colours that turns round and round.</summary>
public sealed class Spinner : Grid
{
    public Spinner()
    {
        Width = Height = 52;
        var rotation = new RotateTransform(0);
        // the dim ring sits behind the turning arc
        Children.Add(new Ellipse { Width = 46, Height = 46, StrokeThickness = 4, Stroke = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        // a ring with one long dash: the circumference is about 3.14 x 46, in units of the 4-pixel stroke
        Children.Add(new Ellipse
        {
            Width = 46, Height = 46, StrokeThickness = 4, StrokeDashCap = PenLineCap.Round, StrokeDashArray = [18, 40],
            Stroke = (Brush)Application.Current.FindResource("PillGradient"), RenderTransform = rotation, RenderTransformOrigin = new Point(0.5, 0.5),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });

        var turn = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };
        Loaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty, turn);
        Unloaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }
}
