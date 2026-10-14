using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArcTV.App.Controls;
using ArcTV.Core;
using ArcTV.Core.Addons;
using ArcTV.Core.Continue;
using ArcTV.Core.Details;
using ArcTV.Core.MyList;
using ArcTV.Core.Plus;
using ArcTV.Core.Sources;

namespace ArcTV.App.Views;

/// <summary>
/// A title's page, laid out like the web app's: the artwork behind it, the logo, year and length, genres, a short description, the buttons (My List, Watched)
/// with the IMDb rating at the far end, then (for a show) its seasons and episodes, and the cast.
/// </summary>
public partial class DetailView : UserControl
{
    private readonly DetailModel _model;
    private readonly MyListModel _list;
    private readonly PlusModel? _plus;
    private readonly ContinueModel? _continue;
    private readonly Action _back;
    private readonly Func<IReadOnlyList<MetaPreview>>? _similarPool;
    private readonly Action<PlayTarget>? _play;
    private readonly Action<PlayTarget>? _download;
    private bool _similarShown;
    private SmoothScroller? _smooth;
    private CancellationTokenSource? _run;
    private IReadOnlyList<int> _seasonsShown = [];
    private IReadOnlyDictionary<string, EpisodeStatus> _statuses = new Dictionary<string, EpisodeStatus>();
    private int _statusVersion;
    private (EpisodeInfo? First, int Season, int Version)? _entriesKey;

    public DetailView(DetailModel model, MyListModel list, Action back, Func<IReadOnlyList<MetaPreview>>? similarPool = null, Action<PlayTarget>? play = null, PlusModel? plus = null, ContinueModel? cont = null, Action<PlayTarget>? download = null)
    {
        _download = download;
        _plus = plus;
        _continue = cont;
        InitializeComponent();
        // the page is at least as tall as the window (less the space under it), so "You May Also Like" sits at the bottom of the window when the page is short
        PageScroll.SizeChanged += (_, e) => PageBody.MinHeight = Math.Max(0, e.NewSize.Height - 48);
        _model = model;
        _list = list;
        _back = back;
        _similarPool = similarPool;
        _play = play;
        AddHandler(Button.ClickEvent, new RoutedEventHandler(OnEpisodeClick)); // a click on an episode's card opens its sources

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
            if (_continue is not null)
            {
                _continue.PropertyChanged -= OnContinueChanged;
                _continue.PropertyChanged += OnContinueChanged;
                if (_continue.Items.Count == 0) await _continue.LoadAsync(run.Token); // so Play knows where this title was left
                await LoadStatusesAsync(run.Token); // so the episodes show their ticks, and Play knows which episode is next (read again on coming back from the player)
            }

            if (_plus is not null)
            {
                _plus.PropertyChanged -= OnListChanged;
                _plus.PropertyChanged += OnListChanged;
                await _plus.LoadStatusAsync(run.Token);
                if (_plus.Active && !_plus.RatingsLoaded) await _plus.LoadRatingsAsync(run.Token); // so Like and Not for me show what this title already has
            }
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
            if (_plus is not null) _plus.PropertyChanged -= OnListChanged;
            if (_continue is not null) _continue.PropertyChanged -= OnContinueChanged;
            _run?.Cancel();
        };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void OnListChanged(object? sender, PropertyChangedEventArgs e) => RefreshButtons();

    private void OnContinueChanged(object? sender, PropertyChangedEventArgs e) => RefreshPlay();

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
        LoadingBar.Content = _model.Loaded ? null : LoadingBar.Content ?? Skeleton.DetailRows(); // made only while loading, so its pulse runs only then

        RefreshButtons();
        RefreshSeasons();
        RefreshTrailer();
        RefreshSimilar();
        RefreshPlay();

        var cast = _model.Cast;
        CastPanel.Visibility = cast.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!ReferenceEquals(CastList.ItemsSource, cast) && !SameCast(CastList.ItemsSource as IReadOnlyList<CastMember>, cast)) CastList.ItemsSource = cast;
    }

    private static bool SameCast(IReadOnlyList<CastMember>? shown, IReadOnlyList<CastMember> cast) => shown is not null && shown.SequenceEqual(cast);

    /// <summary>
    /// What Play starts: a movie (from where it was left, if Continue Watching has it), or a show's episode: the one Continue Watching holds, from where it was left, else the first
    /// episode.
    /// </summary>
    private PlayTarget? MainTarget()
    {
        var left = _continue?.ResumeItem(_model.Detail.Id) is { NearlyDone: false, PositionSeconds: >= 10 } item ? item : null;
        if (!_model.IsShow) return left is null ? new PlayTarget(_model.Detail) : new PlayTarget(_model.Detail) with { StartAt = left.PositionSeconds };
        if (left is { SeasonNumber: { } s, EpisodeNumber: { } e } && _model.Detail.Seasons.GetValueOrDefault(s)?.FirstOrDefault(x => x.Episode == e) is { } episode)
            return EpisodeTarget(episode) with { StartAt = left.PositionSeconds };
        // nothing to resume: the episode after the furthest one watched, else the first
        var first = EpisodeProgress.NextEpisode(_model.Detail.Seasons, _statuses) ?? (_model.Detail.SeasonNumbers.Count > 0 ? _model.Detail.Seasons[_model.Detail.SeasonNumbers[0]].FirstOrDefault() : null);
        return first is null ? null : EpisodeTarget(first);
    }

    private PlayTarget EpisodeTarget(EpisodeInfo episode) =>
        new(_model.Detail, episode.Season, episode.Episode, $"S{episode.Season ?? 0} E{episode.Episode ?? 0} {episode.DisplayTitle}");

    private void RefreshPlay()
    {
        var target = MainTarget();
        PlayButton.IsEnabled = _play is not null && target is not null;
        DownloadButton.Visibility = _download is null ? Visibility.Collapsed : Visibility.Visible;
        DownloadButton.IsEnabled = target is not null;
        var resuming = target is { StartAt: > 0 };
        PlayButton.Content = target is { Season: { } season, Episode: { } episode } ? $"\u25B6  {(resuming ? "Resume" : "Play")} S{season}E{episode}" : resuming ? "\u25B6  Resume" : "\u25B6  Play";
        PlayButton.ToolTip = _play is null ? "Playing is coming soon to Arc TV for Windows." : target is null ? "Looking for episodes…" : null;
    }

    /// <summary>Download opens the panel of files for what Play would start (a movie, or a show's next episode; the panel lets a show's episode be changed).</summary>
    private void OnDownload(object sender, RoutedEventArgs e)
    {
        if (_download is not null && MainTarget() is { } target) _download(target with { StartAt = 0 });
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_play is not null && MainTarget() is { } target) _play(target);
    }

    /// <summary>A click on an episode's card plays it; a click on the tick on its picture marks it watched or not watched.</summary>
    private void OnEpisodeClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: EpisodeEntry entry } button) return;
        if (button.Tag is "tick")
        {
            SetWatched([entry.Info], !entry.Watched);
            e.Handled = true;
        }
        else if (_play is not null)
        {
            _play(EpisodeTarget(entry.Info));
        }
    }

    // ---- which episodes have been watched ----

    private async Task LoadStatusesAsync(CancellationToken cancel = default)
    {
        if (_continue is null || !_model.IsShow) return;
        if (await _continue.EpisodeStatusesAsync(_model.Detail.Id, cancel) is { } found) SetStatuses(found);
    }

    private void SetStatuses(IReadOnlyDictionary<string, EpisodeStatus> statuses)
    {
        _statuses = statuses;
        _statusVersion++;
        RefreshSeasons();
        RefreshPlay();
    }

    /// <summary>Marks episodes watched or not watched: the tick changes at once, the account is told, and if that fails the real state is read again.</summary>
    private async void SetWatched(IReadOnlyList<EpisodeInfo> episodes, bool watched)
    {
        if (_continue is null) return;
        var next = _statuses.ToDictionary(p => p.Key, p => p.Value);
        foreach (var episode in episodes)
        {
            if (EpisodeProgress.Key(episode.Season, episode.Episode) is not { } key) continue;
            if (watched) next[key] = new EpisodeStatus(true, 1);
            else next.Remove(key);
        }

        SetStatuses(next);
        if (!await _continue.SetEpisodesWatchedAsync(episodes, watched, _model.Detail)) await LoadStatusesAsync();
    }

    private void OnSeasonWatched(object sender, RoutedEventArgs e)
    {
        var episodes = _model.Episodes;
        if (episodes.Count == 0) return;
        SetWatched(episodes, !AllWatched(episodes));
    }

    private bool AllWatched(IReadOnlyList<EpisodeInfo> episodes) =>
        episodes.Count > 0 && episodes.All(ep => EpisodeProgress.Key(ep.Season, ep.Episode) is { } key && _statuses.TryGetValue(key, out var status) && status.Watched);

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

        // Like and Not for me belong to Arc TV Plus: they show once the account has it
        var rating = _plus?.RatingOf(id);
        var visible = _plus is { Active: true } ? Visibility.Visible : Visibility.Collapsed;
        LikeButton.Visibility = DislikeButton.Visibility = visible;
        LikeButton.Content = ThumbIcon("\uE8E1");
        LikeButton.ToolTip = rating == Rating.Like ? "Liked: click to take it back" : "Like";
        System.Windows.Automation.AutomationProperties.SetName(LikeButton, "Like");
        LikeButton.Tag = rating == Rating.Like ? "selected" : "";
        DislikeButton.Content = ThumbIcon("\uE8E0");
        DislikeButton.ToolTip = rating == Rating.Dislike ? "Marked not for me: click to take it back" : "Not for me";
        System.Windows.Automation.AutomationProperties.SetName(DislikeButton, "Not for me");
        DislikeButton.Tag = rating == Rating.Dislike ? "selected" : "";
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
        var allWatched = AllWatched(episodes);
        SeasonWatchedButton.Content = allWatched ? "\u2713  Season watched" : "Mark season watched";
        SeasonWatchedButton.Tag = allWatched ? "selected" : "";
        SeasonWatchedButton.ToolTip = allWatched ? "Mark this season as not watched" : "Mark every episode of this season as watched";
        SeasonWatchedButton.Visibility = _continue is null ? Visibility.Collapsed : Visibility.Visible;

        var key = (episodes.FirstOrDefault(), _model.CurrentSeason, _statusVersion);
        if (_entriesKey != key || EpisodeList.ItemsSource is null)
        {
            _entriesKey = key;
            EpisodeList.ItemsSource = EpisodeProgress.Entries(episodes, _statuses);
        }
    }

    // ---- buttons ----

    private void OnBack(object sender, RoutedEventArgs e) => _back();

    private void OnTrailer(object sender, RoutedEventArgs e)
    {
        if (_model.TrailerId is { } id) WebLinks.OpenTrailer(id);
    }

    private async void OnList(object sender, RoutedEventArgs e) => await _list.ToggleSavedAsync(_model.Detail);

    private async void OnWatched(object sender, RoutedEventArgs e) => await _list.ToggleWatchedAsync(_model.Detail);

    /// <summary>A thumbs up or down for the Like and Not for me buttons (the pill turns to the selected look when it is the title's rating).</summary>
    private static TextBlock ThumbIcon(string glyph) => new() { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private async void OnLike(object sender, RoutedEventArgs e) => await Rate(Rating.Like);

    private async void OnDislike(object sender, RoutedEventArgs e) => await Rate(Rating.Dislike);

    private async Task Rate(Rating rating)
    {
        if (_plus is null) return;
        var d = _model.Detail;
        await _plus.RateAsync(d.Id, d.Type, d.Name, d.Poster, rating);
    }

    /// <summary>The title shows instead of the logo until the logo has loaded (a title with no logo, or one that never loads, keeps its words).</summary>
    private void OnLogoShown(object? sender, EventArgs e) => TitleText.Visibility = Visibility.Hidden;
}
