using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArcTV.App.Controls;
using ArcTV.Core;
using ArcTV.Core.Details;
using ArcTV.Core.Sources;

namespace ArcTV.App.Views;

/// <summary>
/// "Select a source": every stream the account's addons offer for this title, the ones that can play here first, best quality first, with the quality pills and a sort.
/// Until the built-in player exists, choosing a direct link copies it (to open in another player); a torrent cannot be started yet.
/// </summary>
public partial class SourcesView : UserControl
{
    private readonly SourcesModel _model;
    private readonly LastSourceStore? _store;
    private readonly Action _back;
    private CancellationTokenSource? _run;
    private SmoothScroller? _smooth;
    private SmoothScroller? _smoothInfo;

    public SourcesView(SourcesModel model, LastSourceStore? store, Action back)
    {
        InitializeComponent();
        _model = model;
        _store = store;
        _back = back;
        ShowTitle();
        ListScroll.PreviewMouseWheel += (_, e) => (_smooth ??= new SmoothScroller(ListScroll, 1.1)).Wheel(e);
        InfoScroll.PreviewMouseWheel += (_, e) => (_smoothInfo ??= new SmoothScroller(InfoScroll, 1.1)).Wheel(e); // the title's column has no scroll bar; the wheel moves it when the window is short

        Loaded += async (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.PropertyChanged += OnModelChanged;
            Refresh();
            await LoadAsync();
        };
        Unloaded += (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _run?.Cancel();
            _smooth?.Stop();
            _smoothInfo?.Stop();
        };
    }

    private async Task LoadAsync()
    {
        _run?.Cancel();
        var run = _run = new CancellationTokenSource();
        await _model.LoadAsync(run.Token);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    // ---- the title on the left ----

    private void ShowTitle()
    {
        var detail = _model.Target.Title;
        Backdrop.Url = Artwork.Bigger(detail.Background ?? detail.Poster);
        Backdrop.FallbackUrl = detail.Background ?? detail.Poster;
        PosterImage.Url = detail.Poster;
        YearText.Text = detail.Year?.ToString() ?? "";
        YearText.Visibility = detail.Year is null ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = detail.Name;
        LogoImage.Url = detail.Logo;
        if (_model.Target.EpisodeTitle is { } episode)
        {
            EpisodeText.Text = episode;
            EpisodeText.Visibility = Visibility.Visible;
        }

        var runtime = Format.RuntimeMinutes(detail.Runtime) is { } minutes ? Format.Runtime(minutes) : "";
        var facts = string.Join("   ", new[] { string.Join("  \u2022  ", (detail.Genres ?? []).Take(3)), runtime }.Where(text => text.Length > 0));
        FactsText.Text = facts;
        FactsText.Visibility = facts.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var about = Text.HtmlDecoded(detail.Description);
        DescriptionText.Text = about;
        DescriptionText.Visibility = about.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLogoShown(object? sender, EventArgs e) => TitleText.Visibility = Visibility.Hidden;

    // ---- the list ----

    private void Refresh()
    {
        var result = _model.Result;
        var loading = _model.Loading;
        var arranged = _model.Arranged;
        var any = result.Items.Count > 0;

        LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ListPanel.Visibility = !loading && any ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = !loading && !any ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _model.EmptyMessage;
        RetryButton.Visibility = result.Failures.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        FilterPills.Children.Clear();
        foreach (var option in SourceListing.Filters)
        {
            var pill = new Button { Content = option.Label(), Style = (Style)FindResource("ChoicePill"), Margin = new Thickness(0, 0, 8, 6), Tag = option == _model.Filter ? "selected" : "" };
            pill.Click += (_, _) => _model.Filter = option;
            FilterPills.Children.Add(pill);
        }

        SortButton.Content = $"Sort by: {_model.Sort.Label()}  \u25BE";

        SourceList.Children.Clear();
        foreach (var item in arranged) SourceList.Children.Add(BuildRow(item, item.Id == _model.RecommendedId));
        ShowAllLink.Visibility = arranged.Count <= 1 && _model.Filter != SourceFilter.All ? Visibility.Visible : Visibility.Collapsed;

        StatusLines.Children.Clear();
        if (!loading)
        {
            foreach (var name in result.Skipped)
                StatusLines.Children.Add(new TextBlock { Text = $"{name}: doesn't provide streams", FontSize = 13, Foreground = (Brush)FindResource("Text3Brush"), Margin = new Thickness(0, 0, 0, 4) });
            foreach (var failure in result.Failures)
                StatusLines.Children.Add(new TextBlock { Text = $"{failure.AddonName} {failure.Reason}", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)), Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
        }
    }

    private static Brush Tier(Quality quality) => quality switch
    {
        Quality.P2160 => (Brush)Application.Current.FindResource("AccentBrush"),
        Quality.P1080 => new SolidColorBrush((Color)Application.Current.FindResource("BlueColor")),
        Quality.P720 => new SolidColorBrush(Color.FromRgb(0x1D, 0xB8, 0xA6)),
        _ => (Brush)Application.Current.FindResource("Text3Brush"),
    };

    private static TextBlock Line(string text, double size, Brush brush, FontWeight? weight = null) =>
        new() { Text = text, FontSize = size, Foreground = brush, FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };

    private Button BuildRow(SourceItem item, bool recommended)
    {
        var dim = (Brush)FindResource("Text2Brush");
        var faint = (Brush)FindResource("Text3Brush");
        var tier = Tier(item.Quality);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // the quality box
        var quality = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        quality.Children.Add(new TextBlock { Text = item.Quality.Label(), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = tier, HorizontalAlignment = HorizontalAlignment.Center });
        if (item.SourceTag is { } tag) quality.Children.Add(new TextBlock { Text = tag, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = tier, HorizontalAlignment = HorizontalAlignment.Center });
        var box = new Border { Width = 70, Height = 46, CornerRadius = new CornerRadius(10), BorderBrush = tier, BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 16, 0), Child = quality };
        grid.Children.Add(box);

        // the release name and what is known about it
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(new TextBlock { Text = item.Headline, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = item.CanStream ? (Brush)FindResource("TextBrush") : faint, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 });
        if (recommended)
            heading.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = (Brush)FindResource("AccentBrush"), Padding = new Thickness(9, 1, 9, 1), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "\u2605 Recommended", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.Black } });
        middle.Children.Add(heading);
        if (item.CodecText is { } codec) middle.Children.Add(Line(codec, 12, dim));
        var facts = new List<string>();
        if (item.Seeders is { } seeders) facts.Add($"{seeders} seeders");
        if (item.AudioTag is { } audio) facts.Add(audio);
        if (item.IsTorrent) facts.Add("Torrent");
        if (facts.Count > 0) middle.Children.Add(Line(string.Join("   \u00B7   ", facts), 11, faint));
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        // size and where it came from
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
        if (item.SizeText is { } size) right.Children.Add(new TextBlock { Text = size, FontSize = 13, FontWeight = FontWeights.Medium, Foreground = dim, HorizontalAlignment = HorizontalAlignment.Right });
        right.Children.Add(new TextBlock { Text = item.AddonName, FontSize = 11, Foreground = faint, HorizontalAlignment = HorizontalAlignment.Right });
        right.Children.Add(new TextBlock { Text = item.IsPlayable ? "Click to copy the link" : item.IsTorrent ? "Needs the torrent player (coming soon)" : "Cannot be played", FontSize = 11, Foreground = item.IsPlayable ? (Brush)FindResource("AccentBrush") : faint, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var row = new Button { Style = (Style)FindResource("SourceRow"), Content = grid, IsEnabled = item.IsPlayable, Tag = recommended ? "recommended" : "" };
        row.Click += (_, _) => Choose(item);
        return row;
    }

    /// <summary>For now: remember the choice and copy the direct link, so it can be opened in another player until the built-in one arrives.</summary>
    private void Choose(SourceItem item)
    {
        if (item.PlayUrl is not { } url) return;
        var target = _model.Target;
        _store?.Set(LastSource.From(item), target.Title.Id, target.Season, target.Episode);
        try
        {
            Clipboard.SetText(url.ToString());
            NoticeText.Text = "Link copied. Playing inside Arc TV is coming soon; until then, paste it into a video player (such as VLC) to watch.";
        }
        catch (Exception)
        {
            NoticeText.Text = "Playing inside Arc TV is coming soon. The link could not be copied just now; try again.";
        }

        NoticeBox.Visibility = Visibility.Visible;
        ListScroll.ScrollToTop();
    }

    // ---- buttons ----

    private void OnBack(object sender, RoutedEventArgs e) => _back();

    private void OnSort(object sender, RoutedEventArgs e) => _model.Sort = _model.Sort.Next();

    private void OnShowAll(object sender, RoutedEventArgs e) => _model.Filter = SourceFilter.All;

    private async void OnRetry(object sender, RoutedEventArgs e) => await LoadAsync();
}
