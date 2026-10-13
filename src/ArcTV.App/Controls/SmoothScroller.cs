using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArcTV.App.Controls;

/// <summary>
/// Makes the mouse wheel (or an arrow button) glide a scroll viewer to a stop instead of jumping a few lines at a time: each notch moves the target further than WPF's own
/// step, and every frame the view eases a fraction of the way there. Goes up and down, or (<paramref name="horizontal"/>) along a row.
/// </summary>
public sealed class SmoothScroller(ScrollViewer scroll, double step = 1.3, double glide = 0.3, bool horizontal = false)
{
    private double _target;
    private bool _gliding;

    private double Offset => horizontal ? scroll.HorizontalOffset : scroll.VerticalOffset;

    private double Furthest => horizontal ? scroll.ScrollableWidth : scroll.ScrollableHeight;

    /// <summary>Handles one turn of the wheel. Marks it handled; leave Shift-wheel alone yourself if rows should scroll sideways.</summary>
    public void Wheel(MouseWheelEventArgs e)
    {
        if (Furthest <= 0) return;
        By(-e.Delta * step);
        e.Handled = true;
    }

    /// <summary>Glides a further <paramref name="distance"/> on from where the last move is heading (or from where it is now).</summary>
    public void By(double distance)
    {
        if (Furthest <= 0) return;
        if (!_gliding) _target = Offset;
        _target = Math.Clamp(_target + distance, 0, Furthest);
        if (!_gliding)
        {
            _gliding = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    public void Stop()
    {
        if (!_gliding) return;
        _gliding = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var current = Offset;
        var next = current + (_target - current) * glide;
        if (Math.Abs(_target - next) < 0.5)
        {
            next = _target;
            Stop();
        }

        if (horizontal) scroll.ScrollToHorizontalOffset(next);
        else scroll.ScrollToVerticalOffset(next);
    }
}
