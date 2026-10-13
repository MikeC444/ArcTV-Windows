using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ArcTV.App.Controls;
using ArcTV.Core;
using ArcTV.Core.Addons;
using ArcTV.Core.Details;
using ArcTV.Core.MyList;

namespace ArcTV.App.Views;

/// <summary>The banner at the top of Home: featured titles that change by themselves every few seconds (stopped while the pointer is over it), with dots to jump to one.</summary>
public sealed class HeroBanner : Grid
{
    private readonly IReadOnlyList<MetaPreview> _items;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly Grid _slide = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 36, 78) };
    private readonly MyListModel? _list;
    private Button? _listButton;
    private int _index;

    public HeroBanner(IReadOnlyList<MetaPreview> items, MyListModel? list = null)
    {
        _items = items;
        _list = list;
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

        // the My List button follows the list: it reads "In My List" once the title is on it, from here or from the title's page
        Loaded += (_, _) =>
        {
            if (_list is null) return;
            _list.PropertyChanged -= OnListChanged;
            _list.PropertyChanged += OnListChanged;
            RefreshListButton();
        };
        Unloaded += (_, _) =>
        {
            if (_list is not null) _list.PropertyChanged -= OnListChanged;
        };
    }

    private void OnListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshListButton();

    private void RefreshListButton()
    {
        if (_listButton is null || _list is null) return;
        var saved = _list.IsSaved(_items[_index].Id);
        _listButton.Content = saved ? "\u2713  In My List" : "+  My List";
        _listButton.Tag = saved ? "selected" : "";
    }

    /// <summary>Play (not yet), More info (opens the title's page: the page sees the click on a button that stands for a title) and My List.</summary>
    private StackPanel Buttons(MetaPreview item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        row.Children.Add(new Button { Content = "\u25B6  Play", Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 44, Padding = new Thickness(24, 0, 24, 0), IsEnabled = false, ToolTip = "Playing is coming soon to Arc TV for Windows.", Margin = new Thickness(0, 0, 12, 0) });
        row.Children.Add(new Button { Content = "More info", Style = (Style)Application.Current.FindResource("ChoicePill"), Height = 44, Margin = new Thickness(0, 0, 12, 0), DataContext = item });
        if (_list is not null)
        {
            var list = new Button { Style = (Style)Application.Current.FindResource("ChoicePill"), Height = 44 };
            list.Click += async (_, _) => await _list.ToggleSavedAsync(item.AsDetail());
            _listButton = list;
            row.Children.Add(list);
        }

        return row;
    }

    public bool Shows(IReadOnlyList<MetaPreview> items) => items.Count == _items.Count && items.Zip(_items).All(pair => pair.First.Id == pair.Second.Id);

    public void Stop() => _timer.Stop();

    public void Start()
    {
        if (_items.Count > 1) _timer.Start();
    }

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

    private UIElement Slide(MetaPreview item)
    {
        var grid = new Grid();
        var picture = new RemoteImage { Url = Artwork.Bigger(item.Background) ?? item.Poster, FallbackUrl = item.Background ?? item.Poster, DecodeWidth = 0, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(picture);
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
        details.Children.Add(Buttons(item));
        grid.Children.Add(details);
        RefreshListButton();
        return grid;
    }
}
