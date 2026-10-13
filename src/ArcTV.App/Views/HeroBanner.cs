using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ArcTV.App.Controls;
using ArcTV.Core;
using ArcTV.Core.Addons;
using ArcTV.Core.Browse;
using ArcTV.Core.Details;
using ArcTV.Core.MyList;
using ArcTV.Core.Sources;

namespace ArcTV.App.Views;

/// <summary>The banner at the top of Home: featured titles that change by themselves every few seconds (stopped while the pointer is over it), with dots to jump to one.</summary>
public sealed class HeroBanner : Grid
{
    /// <summary>The banner is this much taller than the part with words on it: the picture goes on under them and fades slowly into the page, so there is no edge to see.</summary>
    public const double FadeExtra = 140;

    private readonly IReadOnlyList<MetaPreview> _items;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly Grid _slide = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 36, 78 + FadeExtra) };
    private readonly MyListModel? _list;
    private readonly IDetailExtras? _extras;
    private readonly Action<PlayTarget>? _play;
    private Button? _listButton;
    private int _index;

    public HeroBanner(IReadOnlyList<MetaPreview> items, MyListModel? list = null, IDetailExtras? extras = null, Action<PlayTarget>? play = null)
    {
        _items = items;
        _play = play;
        _list = list;
        _extras = extras;
        ClipToBounds = true;
        Background = (Brush)Application.Current.FindResource("BgBrush");
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

    /// <summary>Turns the Trailer button on when the account's server knows a trailer for this title (it stays off when there is none).</summary>
    private async Task FindTrailerAsync(MetaPreview item, Button button)
    {
        if (_extras is null) return;
        try
        {
            if (await _extras.TrailerIdAsync(item.Name, item.Type, item.Year()) is { } id)
            {
                button.IsEnabled = true;
                button.ToolTip = "Watch the trailer on YouTube";
                button.Click += (_, _) => WebLinks.OpenTrailer(id);
            }
            else
            {
                button.ToolTip = "No trailer found for this title";
            }
        }
        catch (Exception)
        {
            button.ToolTip = "No trailer found for this title";
        }
    }

    /// <summary>Play (not yet), More info (opens the title's page: the page sees the click on a button that stands for a title) and My List.</summary>
    private StackPanel Buttons(MetaPreview item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        var play = new Button { Content = "\u25B6  Play", Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 44, Padding = new Thickness(24, 0, 24, 0), Margin = new Thickness(0, 0, 12, 0) };
        if (_play is null)
        {
            play.IsEnabled = false;
            play.ToolTip = "Playing is coming soon to Arc TV for Windows.";
        }
        else if (item.Type == "movie")
        {
            play.Click += (_, _) => _play(new PlayTarget(item.AsDetail()));
        }
        else
        {
            play.DataContext = item; // a show needs an episode: its page (which sees the click on a button that stands for a title) has them
            play.ToolTip = "Opens the show to choose an episode";
        }

        row.Children.Add(play);
        var trailer = new Button { Content = "Trailer", Style = (Style)Application.Current.FindResource("ChoicePill"), Height = 44, Margin = new Thickness(0, 0, 12, 0), IsEnabled = false, ToolTip = "Looking for a trailer…" };
        row.Children.Add(trailer);
        _ = FindTrailerAsync(item, trailer);
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
        // fades into the page at the bottom, over the lower half and ending fully the page's colour, in steps so there is no visible edge; and darkens the left (where the words are) so they can be read
        var page = (Color)Application.Current.FindResource("BgColor");
        Color Page(byte alpha) => Color.FromArgb(alpha, page.R, page.G, page.B); // the page's own colour at every step: fading from "transparent black" would pass through a grey
        grid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Page(0), 0.58), new GradientStop(Page(30), 0.68), new GradientStop(Page(90), 0.77), new GradientStop(Page(165), 0.85),
                    new GradientStop(Page(225), 0.92), new GradientStop(Page(252), 0.97), new GradientStop(Page(255), 0.995),
                },
            },
        });
        // the very bottom is the page's own colour for certain: a gradient's last pixel row can show the picture through at an uneven window height, which looked like a line
        grid.Children.Add(new System.Windows.Shapes.Rectangle { Height = 6, VerticalAlignment = VerticalAlignment.Bottom, Fill = new SolidColorBrush(page) });
        grid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                GradientStops = { new GradientStop(Color.FromArgb(0xD9, page.R, page.G, page.B), 0), new GradientStop(Color.FromArgb(0x80, page.R, page.G, page.B), 0.35), new GradientStop(Page(0), 0.7) },
            },
        });

        var details = new StackPanel { Margin = new Thickness(24, 120, 24, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 600 }; // top left, under the top bar
        // every part has a fixed height whatever it holds (a tall or short logo, no genres, a one-line description), so the buttons are in exactly the same place on every slide
        var heading = new Grid { Height = 110, HorizontalAlignment = HorizontalAlignment.Left };
        if (!string.IsNullOrEmpty(item.Logo))
            heading.Children.Add(new RemoteImage { Url = item.Logo, DecodeWidth = 760, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 380, MaxHeight = 110 });
        else
            heading.Children.Add(new TextBlock { Text = item.Name, FontSize = 44, FontWeight = FontWeights.ExtraBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis });
        details.Children.Add(heading);

        var facts = string.Join("   ", new[] { item.ReleaseInfo, item.Runtime, item.ImdbRating is null ? null : "★ " + item.ImdbRating }.Where(f => !string.IsNullOrEmpty(f)));
        details.Children.Add(new TextBlock { Text = facts, Height = 20, FontSize = 14, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 12, 0, 0) });
        details.Children.Add(new TextBlock { Text = string.Join("  ·  ", (item.Genres ?? []).Take(3)), Height = 18, FontSize = 13, Opacity = 0.9, Margin = new Thickness(0, 6, 0, 0) });
        details.Children.Add(new TextBlock { Text = Text.HtmlDecoded(item.Description), Height = 66, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 0) });
        details.Children.Add(Buttons(item));

        // the block grows and shrinks with the banner: its size follows the smaller of how wide and how tall the banner is (measured against a 1300 x 560 banner), within limits,
        // and it sits a fifth of the way down (never under the top bar)
        var scale = new ScaleTransform(1, 1);
        details.LayoutTransform = scale;
        grid.SizeChanged += (_, e) =>
        {
            var shown = e.NewSize.Height - FadeExtra; // the part with words on it
            scale.ScaleX = scale.ScaleY = Math.Clamp(Math.Min(e.NewSize.Width / 1300.0, shown / 560.0), 0.7, 1.6);
            details.Margin = new Thickness(24, Math.Max(104, shown * 0.2), 24, 0);
        };
        grid.Children.Add(details);
        RefreshListButton();
        return grid;
    }
}
