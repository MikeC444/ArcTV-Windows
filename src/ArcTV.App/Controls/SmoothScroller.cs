using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArcTV.App.Controls;

/// <summary>
/// Makes the mouse wheel glide a scroll viewer to a stop instead of jumping a few lines at a time: each notch moves the target further than WPF's own step, and every
/// frame the view eases a fraction of the way there.
/// </summary>
public sealed class SmoothScroller(ScrollViewer scroll, double step = 1.3, double glide = 0.3)
{
    private double _target;
    private bool _gliding;

    /// <summary>Handles one turn of the wheel. Marks it handled; leave Shift-wheel alone yourself if rows should scroll sideways.</summary>
    public void Wheel(MouseWheelEventArgs e)
    {
        if (scroll.ScrollableHeight <= 0) return;
        if (!_gliding) _target = scroll.VerticalOffset;
        _target = Math.Clamp(_target - e.Delta * step, 0, scroll.ScrollableHeight);
        if (!_gliding)
        {
            _gliding = true;
            CompositionTarget.Rendering += OnFrame;
        }

        e.Handled = true;
    }

    public void Stop()
    {
        if (!_gliding) return;
        _gliding = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var current = scroll.VerticalOffset;
        var next = current + (_target - current) * glide;
        if (Math.Abs(_target - next) < 0.5)
        {
            next = _target;
            Stop();
        }

        scroll.ScrollToVerticalOffset(next);
    }
}
