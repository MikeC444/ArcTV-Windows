using System.Windows;
using System.Windows.Controls;

namespace ArcTV.App.Controls;

/// <summary>Lays children out left to right, each at its own width, with <see cref="Extra"/> more space between them than they already carry.</summary>
public sealed class SpacedRowPanel : Panel
{
    public static readonly DependencyProperty ExtraProperty = DependencyProperty.Register(nameof(Extra), typeof(double), typeof(SpacedRowPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Extra { get => (double)GetValue(ExtraProperty); set => SetValue(ExtraProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width + Math.Max(0, InternalChildren.Count - 1) * Math.Max(0, Extra), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width + Math.Max(0, Extra);
        }

        return finalSize;
    }
}
