using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArcTV.App.Controls;
using ArcTV.Core;
using ArcTV.Core.Addons;
using ArcTV.Core.Details;
using ArcTV.Core.MyList;

namespace ArcTV.App.Views;

/// <summary>
/// A title's page, laid out like the web app's: the artwork behind it, the logo, year and length, genres, a short description, the buttons (My List, Watched)
/// with the IMDb rating at the far end, then (for a show) its seasons and episodes, and the cast.
/// </summary>
public partial class DetailView : UserControl
{
    private readonly DetailModel _model;
    private readonly MyListModel _list;
    private readonly Action _back;
    private readonly Func<IReadOnlyList<MetaPreview>>? _similarPool;
    private bool _similarShown;
    private SmoothScroller? _smooth;
    private CancellationTokenSource? _run;
    private IReadOnlyList<int> _seasonsShown = [];

    public DetailView(DetailModel model, MyListModel list, Action back, Func<IReadOnlyList<MetaPreview>>? similarPool = null)
    {
        InitializeComponent();
        _model = model;
        _list = list;
        _back = back;
        _similarPool = similarPool;

        Loaded += async (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.PropertyChanged += OnModelChanged;
            _list.PropertyChanged -= OnListChanged;
            _list.PropertyChanged += OnListChanged;
            Refresh();
            var run = _run = new CancellationTokenSource();
            if (!_model.Loaded) await _model.LoadAsync(run.Token);
            if (_list.Items is null) await _list.LoadAsync(run.Token); // so the My List button knows whether this title is already on it
        };
        PageScroll.PreviewMouseWheel += (_, e) =>
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) (_smooth ??= new SmoothScroller(PageScroll, 1.1)).Wheel(e); // Shift-wheel moves a row sideways
        };
        Unloaded += (_, _) =>
        {
            _smooth?.Stop();
            _model.PropertyChanged -= OnModelChanged;
            _list.PropertyChanged -= OnListChanged;
            _run?.Cancel();
        };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void OnListChanged(object? sender, PropertyChangedEventArgs e) => RefreshButtons();

    private void Refresh()
    {
        var detail = _model.Detail;
        TitleText.Text = detail.Name;
        Backdrop.Url = Artwork.Bigger(detail.Background);
        Backdrop.FallbackUrl = detail.Background;
        if (LogoImage.Url != detail.Logo)
        {
            TitleText.Visibility = Visibility.Visible;
            LogoImage.Url = detail.Logo;
        }


        var facts = _model.Facts;
        FactsText.Text = facts;
        FactsText.Visibility = facts.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var genres = string.Join("  ·  ", detail.Genres ?? []);
        GenresText.Text = genres;
        GenresText.Visibility = genres.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var description = Text.HtmlDecoded(detail.Description);
        DescriptionText.Text = description;
        DescriptionText.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        RatingText.Text = detail.ImdbRating ?? "";
        RatingBox.Visibility = string.IsNullOrEmpty(detail.ImdbRating) ? Visibility.Collapsed : Visibility.Visible;
        LoadingBar.Visibility = _model.Loaded ? Visibility.Collapsed : Visibility.Visible;

        RefreshButtons();
        RefreshSeasons();
        RefreshTrailer();
        RefreshSimilar();

        var cast = _model.Cast;
        CastPanel.Visibility = cast.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!ReferenceEquals(CastList.ItemsSource, cast) && !SameCast(CastList.ItemsSource as IReadOnlyList<CastMember>, cast)) CastList.ItemsSource = cast;
    }

    private static bool SameCast(IReadOnlyList<CastMember>? shown, IReadOnlyList<CastMember> cast) => shown is not null && shown.SequenceEqual(cast);

    private void RefreshTrailer()
    {
        var found = _model.TrailerId is not null;
        TrailerButton.IsEnabled = found;
        TrailerButton.ToolTip = found ? "Watch the trailer on YouTube" : _model.Loaded ? "No trailer found for this title" : "Looking for a trailer…";
    }

    /// <summary>A movie's "You May Also Like": other titles from Home's rows, once the page has its details.</summary>
    private void RefreshSimilar()
    {
        if (_similarShown || _similarPool is null || _model.IsShow || !_model.Loaded) return;
        _similarShown = true;
        var similar = SimilarTitles.For(_model.Detail, _similarPool());
        SimilarPanel.Visibility = similar.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SimilarList.ItemsSource = similar;
    }

    private void RefreshButtons()
    {
        var id = _model.Detail.Id;
        var saved = _list.IsSaved(id);
        var watched = _list.IsWatched(id);
        ListButton.Content = saved ? "✓  In My List" : "+  My List";
        ListButton.Tag = saved ? "selected" : "";
        WatchedButton.Content = watched ? "✓  Watched" : "Mark as watched";
        WatchedButton.Tag = watched ? "selected" : "";
    }

    private void RefreshSeasons()
    {
        var numbers = _model.Detail.SeasonNumbers;
        SeasonsPanel.Visibility = _model.IsShow && numbers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (numbers.Count == 0) return;

        if (!numbers.SequenceEqual(_seasonsShown))
        {
            _seasonsShown = numbers;
            SeasonButtons.Children.Clear();
            foreach (var number in numbers)
            {
                var button = new Button { Content = number.ToString(), Style = (Style)FindResource("SeasonCircle"), ToolTip = $"Season {number}" };
                button.Click += (_, _) => _model.ChooseSeason(number);
                SeasonButtons.Children.Add(button);
            }
        }

        for (var i = 0; i < SeasonButtons.Children.Count; i++) ((Button)SeasonButtons.Children[i]).Tag = numbers[i] == _model.CurrentSeason ? "selected" : "";
        var episodes = _model.Episodes;
        SeasonTitle.Text = $"Season {_model.CurrentSeason}";
        SeasonCount.Text = $"{episodes.Count} {(episodes.Count == 1 ? "Episode" : "Episodes")}";
        if (!ReferenceEquals(EpisodeList.ItemsSource, episodes)) EpisodeList.ItemsSource = episodes;
    }

    // ---- buttons ----

    private void OnBack(object sender, RoutedEventArgs e) => _back();

    private void OnTrailer(object sender, RoutedEventArgs e)
    {
        if (_model.TrailerId is { } id) WebLinks.OpenTrailer(id);
    }

    private async void OnList(object sender, RoutedEventArgs e) => await _list.ToggleSavedAsync(_model.Detail);

    private async void OnWatched(object sender, RoutedEventArgs e) => await _list.ToggleWatchedAsync(_model.Detail);

    /// <summary>The title shows instead of the logo until the logo has loaded (a title with no logo, or one that never loads, keeps its words).</summary>
    private void OnLogoShown(object? sender, EventArgs e) => TitleText.Visibility = Visibility.Hidden;
}
