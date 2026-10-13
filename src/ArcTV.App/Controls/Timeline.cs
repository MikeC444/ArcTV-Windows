using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArcTV.App.Controls;

/// <summary>
/// A thin bar you can click or drag, for the playing position (and the volume): the part up to <see cref="Value"/> out of <see cref="Total"/> is filled with the logo's colours,
/// and a round handle shows where it is.
/// </summary>
public sealed class Timeline : Grid
{
    private readonly Border _fill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
    private readonly Border _handle = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-7, 0, 0, 0), IsHitTestVisible = false };
    private double _value;
    private double _total;
    private bool _dragging;

    /// <summary>Raised while dragging (with the position under the pointer).</summary>
    public event Action<double>? Scrubbing;

    /// <summary>Raised when the pointer is let go (or the bar is clicked): the position to go to.</summary>
    public event Action<double>? Scrubbed;

    public Timeline()
    {
        Height = 24;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent; // so the whole height can be clicked
        Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x4D, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        _fill.Background = (Brush)Application.Current.FindResource("PillGradient");
        _fill.IsHitTestVisible = false;
        Children.Add(_fill);
        Children.Add(_handle);
        SizeChanged += (_, _) => Layout();
    }

    public double Total
    {
        get => _total;
        set
        {
            _total = value;
            Layout();
        }
    }

    public double Value
    {
        get => _value;
        set
        {
            if (_dragging) return; // while it is dragged, it follows the pointer, not the player
            _value = value;
            Layout();
        }
    }

    private void Layout()
    {
        var fraction = _total > 0 ? Math.Clamp(_value / _total, 0, 1) : 0;
        var width = ActualWidth * fraction;
        _fill.Width = width;
        _handle.Margin = new Thickness(width - 7, 0, 0, 0);
    }

    private double At(MouseEventArgs e) => ActualWidth <= 0 ? 0 : Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1) * _total;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_total <= 0) return;
        _dragging = true;
        CaptureMouse();
        _value = At(e);
        Layout();
        Scrubbing?.Invoke(_value);
        e.Handled = true; // a click on the bar is not a click on the video (which would pause it)
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            _value = At(e);
            Layout();
            Scrubbing?.Invoke(_value);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            _value = At(e);
            Layout();
            Scrubbed?.Invoke(_value);
        }

        base.OnMouseLeftButtonUp(e);
    }
}
