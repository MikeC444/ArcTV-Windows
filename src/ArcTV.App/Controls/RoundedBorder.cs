using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArcTV.App.Controls;

/// <summary>
/// A border whose corners really are round: a plain border draws a round outline but lets what is inside (a picture) poke out square at the corners, so this one also
/// clips its contents to the rounded shape.
/// </summary>
public class RoundedBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        var radius = CornerRadius.TopLeft;
        Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
    }
}
