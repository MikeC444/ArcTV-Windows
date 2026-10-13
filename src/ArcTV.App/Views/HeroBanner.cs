using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ArcTV.App.Controls;
using ArcTV.Core;
using ArcTV.Core.Addons;

namespace ArcTV.App.Views;

/// <summary>The banner at the top of Home: featured titles that change by themselves every few seconds (stopped while the pointer is over it), with dots to jump to one.</summary>
public sealed class HeroBanner : Grid
{
    private readonly IReadOnlyList<MetaPreview> _items;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly Grid _slide = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 36, 78) };
    private int _index;

    public HeroBanner(IReadOnlyList<MetaPreview> items)
    {
        _items = items;
        ClipToBounds = true;
        Background = (Brush)Application.Current.FindResource("SurfaceBrush");
        Children.Add(_slide);
        if (items.Count > 1)
        {
            Children.Add(_dots);
            for (var i = 0; i < items.Count; i++)
            {
                var target = i;
                var dot = new Button { Style = (Style)Application.Current.FindResource("DotButton"), Tag = target };
                dot.Click += (_, _) => Show(target);
                _dots.Children.Add(dot);
            }
        }

        _timer.Tick += (_, _) => { if (!IsMouseOver) Show((_index + 1) % _items.Count); };
        Show(0);
        if (items.Count > 1) _timer.Start();
    }

    public bool Shows(IReadOnlyList<MetaPreview> items) => items.Count == _items.Count && items.Zip(_items).All(pair => pair.First.Id == pair.Second.Id);

    public void Stop() => _timer.Stop();

    private void Show(int index)
    {
        _index = index;
        _slide.Children.Clear();
        _slide.Children.Add(Slide(_items[index]));
        _slide.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
        for (var i = 0; i < _dots.Children.Count; i++) ((Button)_dots.Children[i]).Opacity = i == index ? 1 : 0.4;
        _timer.Stop();
        if (_items.Count > 1) _timer.Start();
    }

    private static UIElement Slide(MetaPreview item)
    {
        var grid = new Grid();
        grid.Children.Add(new RemoteImage { Url = item.Background ?? item.Poster, DecodeWidth = 1600, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top });
        // fades into the page at the bottom, and darkens the left so the words can be read
        grid.Children.Add(new System.Windows.Shapes.Rectangle { Fill = new LinearGradientBrush(Colors.Transparent, (Color)Application.Current.FindResource("BgColor"), 90) { GradientStops = { new GradientStop(Colors.Transparent, 0.45) } } });
        grid.Children.Add(new System.Windows.Shapes.Rectangle { Fill = new LinearGradientBrush(Color.FromArgb(0xD9, 8, 8, 10), Colors.Transparent, 0) { GradientStops = { new GradientStop(Colors.Transparent, 0.6) } } });

        var details = new StackPanel { Margin = new Thickness(44, 96, 0, 70), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 600 };
        if (!string.IsNullOrEmpty(item.Logo))
        {
            details.Children.Add(new RemoteImage { Url = item.Logo, DecodeWidth = 760, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 380, MaxHeight = 110 });
        }
        else
        {
            details.Children.Add(new TextBlock { Text = item.Name, FontSize = 44, FontWeight = FontWeights.ExtraBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 120 });
        }

        var facts = string.Join("   ", new[] { item.ReleaseInfo, item.Runtime, item.ImdbRating is null ? null : "★ " + item.ImdbRating }.Where(f => !string.IsNullOrEmpty(f)));
        if (facts.Length > 0) details.Children.Add(new TextBlock { Text = facts, FontSize = 14, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 12, 0, 0) });
        if (item.Genres is { Count: > 0 }) details.Children.Add(new TextBlock { Text = string.Join("  ·  ", item.Genres.Take(3)), FontSize = 13, Opacity = 0.9, Margin = new Thickness(0, 6, 0, 0) });
        var description = Text.HtmlDecoded(item.Description);
        if (description.Length > 0) details.Children.Add(new TextBlock { Text = description, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxHeight = 66, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 0) });
        grid.Children.Add(details);
        return grid;
    }
}
