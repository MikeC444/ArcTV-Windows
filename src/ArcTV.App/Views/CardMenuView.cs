using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ArcTV.App.Controls;
using ArcTV.Core.Addons;
using ArcTV.Core.Continue;
using ArcTV.Core.Details;
using ArcTV.Core.MyList;
using ArcTV.Core.Player;
using ArcTV.Core.Plus;
using ArcTV.Core.Recommend;
using ArcTV.Core.Sources;

namespace ArcTV.App.Views;

/// <summary>
/// What right-clicking a poster opens (the Mac and web apps' poster menu): the title's artwork, a big Play (or Resume), My List / Watched / Like / Not for me that stay open and show
/// their state, then View details, Choose source and the removals.
/// </summary>
public sealed class CardMenuView : Grid
{
    private const string Mdl2 = "Segoe MDL2 Assets";
    private const double Zoom = 1.3;

    private readonly MetaPreview _item;
    private readonly bool _fromPicked;
    private readonly MyListModel _list;
    private readonly PlusModel _plus;
    private readonly ContinueModel _continue;
    private readonly PickedModel _picked;
    private readonly Action _close;
    private readonly Action<PlayTarget> _openSources;
    private readonly Action<MetaPreview> _openDetail;
    private readonly ContentControl _toggles = new();
    private bool _busy;

    public CardMenuView(MetaPreview item, bool fromPicked, MyListModel list, PlusModel plus, ContinueModel cont, PickedModel picked, Action close, Action<PlayTarget> openSources,
        Action<MetaPreview> openDetail, Func<MetaDetail, Task<string?>> logoLookup)
    {
        (_item, _fromPicked, _list, _plus, _continue, _picked, _close, _openSources, _openDetail) = (item, fromPicked, list, plus, cont, picked, close, openSources, openDetail);

        var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)) };
        dim.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _close();
        };
        Children.Add(dim);

        var column = new StackPanel();
        column.Children.Add(Banner(logoLookup));
        var body = new StackPanel { Margin = new Thickness(26, 4, 26, 22) };
        body.Children.Add(PlayButton());
        _toggles.Margin = new Thickness(0, 14, 0, 4);
        body.Children.Add(_toggles);
        body.Children.Add(Rows());
        column.Children.Add(body);

        var panel = new RoundedBorder
        {
            Width = 500, CornerRadius = new CornerRadius(24), Background = Res("BgElevatedBrush"), BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 30, 0, 30),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = column },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Opacity = 0.6 },
        };
        // the panel is drawn a quarter bigger than its 500 units (less in a window too small for that), and as tall as the window allows
        var zoom = new ScaleTransform(1, 1);
        panel.LayoutTransform = zoom;
        SizeChanged += (_, e) =>
        {
            var scale = Math.Clamp(Math.Min(Zoom, (e.NewSize.Width - 40) / 500, (e.NewSize.Height - 60) / 640), 0.6, Zoom);
            zoom.ScaleX = zoom.ScaleY = scale;
            panel.MaxHeight = Math.Max(300, (e.NewSize.Height - 60) / scale);
        };
        Children.Add(panel);

        RefreshToggles();
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private static Style Sty(string key) => (Style)Application.Current.FindResource(key);

    private static TextBlock Glyph(string glyph, double size) => new() { Text = glyph, FontFamily = new FontFamily(Mdl2), FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private ContinueItem? Resume => _continue.ResumeItem(_item.Id) is { NearlyDone: false, PositionSeconds: >= 10 } left ? left : null;

    // ---- the picture and the name ----

    private UIElement Banner(Func<MetaDetail, Task<string?>> logoLookup)
    {
        var grid = new Grid { Height = 230, ClipToBounds = true };
        grid.Children.Add(new Border { Background = Res("SurfaceHighBrush") });
        grid.Children.Add(new RemoteImage { Url = Artwork.Bigger(_item.Background ?? _item.Poster), FallbackUrl = _item.Background ?? _item.Poster, DecodeWidth = 1000, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top });
        var elevated = (Color)Application.Current.FindResource("BgElevatedColor");
        grid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = { new GradientStop(Color.FromArgb(0, elevated.R, elevated.G, elevated.B), 0), new GradientStop(Color.FromArgb(0x8C, elevated.R, elevated.G, elevated.B), 0.55), new GradientStop(elevated, 1) },
            },
        });

        var name = new TextBlock { Text = _item.Name, FontSize = 32, FontWeight = FontWeights.ExtraBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 90, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(26, 0, 70, 18), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.8 } };
        var logo = new RemoteImage { Url = _item.Logo, DecodeWidth = 640, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 320, MaxHeight = 100, Margin = new Thickness(26, 0, 0, 18) };
        logo.Shown += (_, _) => name.Visibility = Visibility.Collapsed;
        grid.Children.Add(name);
        grid.Children.Add(logo);
        if (string.IsNullOrEmpty(_item.Logo)) _ = FindLogoAsync(logoLookup, logo);

        var close = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Close",
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 14, 0), Child = Glyph("\uE711", 14),
        };
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _close();
        };
        grid.Children.Add(close);
        return grid;
    }

    private async Task FindLogoAsync(Func<MetaDetail, Task<string?>> lookup, RemoteImage logo)
    {
        if (await lookup(_item.AsDetail()) is { Length: > 0 } url) logo.Url = url;
    }

    // ---- play ----

    private UIElement PlayButton()
    {
        var button = new Button { Style = Sty("PrimaryButton"), Height = 58, Content = new TextBlock { Text = Resume is { } left ? $"▶  Resume from {PlayerModel.Clock(left.PositionSeconds)}" : "▶  Play", FontSize = 20, FontWeight = FontWeights.Bold } };
        button.Click += (_, _) =>
        {
            _close();
            if (Resume is { } resume) _openSources(resume.ToTarget() with { ChooseSource = true }); // from where it was left, with the list (as Continue Watching does)
            else if (_item.Type == "movie") _openSources(new PlayTarget(_item.AsDetail()));
            else _openDetail(_item); // a show not started: its page knows the first episode
        };
        return button;
    }

    // ---- My List, Watched, Like, Not for me ----

    private void RefreshToggles()
    {
        var saved = _list.IsSaved(_item.Id);
        var watched = _list.IsWatched(_item.Id);
        var rating = _plus.RatingOf(_item.Id);
        var grid = new UniformGrid { Columns = 2 };
        grid.Children.Add(Toggle(saved, "", "", "In My List", "Add to My List", () => _list.ToggleSavedAsync(_item.AsDetail())));
        grid.Children.Add(Toggle(watched, "", "", "Watched", "Mark as watched", () => _list.ToggleWatchedAsync(_item.AsDetail())));
        if (_plus.Active)
        {
            grid.Children.Add(Toggle(rating == Rating.Like, "", "", "Liked", "Like", () => _plus.RateAsync(_item.Id, _item.Type, _item.Name, _item.Poster, Rating.Like)));
            grid.Children.Add(Toggle(rating == Rating.Dislike, "", "", "Not for me", "Not for me", () => _plus.RateAsync(_item.Id, _item.Type, _item.Name, _item.Poster, Rating.Dislike)));
        }

        _toggles.Content = grid;
    }

    private Button Toggle(bool on, string onGlyph, string offGlyph, string onText, string offText, Func<Task> action)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = Glyph(on ? onGlyph : offGlyph, 18);
        icon.Margin = new Thickness(0, 0, 10, 0);
        line.Children.Add(icon);
        line.Children.Add(new TextBlock { Text = on ? onText : offText, FontSize = 15, FontWeight = on ? FontWeights.Bold : FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Style = Sty("ChoicePill"), Height = 54, Margin = new Thickness(0, 0, 8, 8), Content = line, Tag = on ? "selected" : "" };
        button.Click += async (_, _) =>
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await action();
            }
            catch (Exception)
            {
                // the state shown below is what the account really has
            }
            finally
            {
                _busy = false;
            }

            RefreshToggles();
        };
        return button;
    }

    // ---- the rows ----

    private UIElement Rows()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) });
        if (_plus.Active && _fromPicked)
        {
            stack.Children.Add(Row("", "Remove from Picked for you", false, () =>
            {
                _ = _picked.RemoveAsync(_item.Id);
                _close();
            }));
        }

        stack.Children.Add(Row("", "View details", true, () =>
        {
            _close();
            _openDetail(_item);
        }));
        if (_continue.ResumeItem(_item.Id) is { } pointer)
        {
            stack.Children.Add(Row("", "Remove from Continue Watching", false, () =>
            {
                _ = _continue.RemoveAsync(pointer);
                _close();
            }, destructive: true));
        }

        stack.Children.Add(Row("", "Choose source", true, () =>
        {
            _close();
            if (Resume is { } resume) _openSources(resume.ToTarget() with { ChooseSource = true });
            else if (_item.Type == "movie") _openSources(new PlayTarget(_item.AsDetail()) with { ChooseSource = true });
            else _openDetail(_item);
        }));
        return stack;
    }

    private static Button Row(string glyph, string text, bool chevron, Action click, bool destructive = false)
    {
        var grid = new Grid { Margin = new Thickness(4, 0, 4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Glyph(glyph, 18);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);
        var label = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (chevron)
        {
            var arrow = Glyph("", 14);
            arrow.Foreground = Res("Text2Brush");
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(arrow);
        }

        var button = new Button { Style = Sty("SuggestionRow"), Height = 52, Content = grid, Focusable = false };
        if (destructive) button.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A));
        button.Click += (_, _) => click();
        return button;
    }
}
