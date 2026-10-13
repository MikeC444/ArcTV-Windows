using System.IO;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArcTV.App.Controls;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Player;
using ArcTV.Core.Plus;
using ArcTV.Core.Settings;
using ArcTV.Core.Browse;
using ArcTV.Core.Continue;
using ArcTV.Core.Details;
using ArcTV.Core.Home;
using ArcTV.Core.MyList;
using ArcTV.Core.Recommend;
using ArcTV.Core.Search;
using ArcTV.App.Player;
using ArcTV.Core.Sources;
using ArcTV.Core.Storage;

namespace ArcTV.App.Views;

/// <summary>The signed-in app: the floating top bar and whichever place is open.</summary>
public partial class ShellView : UserControl
{
    private static readonly string[] Places = ["Home", "Movies", "TV Shows", "Search", "My List", "Settings"];

    private readonly AccountModel _account;
    private readonly HomeModel _home;
    private readonly Dictionary<string, BrowseModel> _browse = [];
    private MyListModel? _myList;
    private AddonsModel? _addonsModel;
    private readonly SettingsModel _settingsModel;
    private readonly PlusModel _plus;
    private readonly ContinueModel _continue;
    private readonly PickedModel _picked;
    private readonly LocalSettings _local = new(Path.Combine(AppPaths.DataFolder(), "settings.json"));
    private readonly AccountUser _user;
    private string _blockedKey = "";
    private readonly IDetailExtras _extras;
    private readonly Stack<(UIElement Page, string Place)> _history = [];
    private readonly AddonClient _addonClient = new();
    private IReadOnlyList<HomeSource> _sources = [];
    private string _place = "";
    private readonly Dictionary<string, Button> _pills = [];

    public ShellView(AccountModel account)
    {
        InitializeComponent();
        _account = account;
        _extras = new AccountDetailExtras(account);
        var user = account.Session!.User;
        _user = user;
        _home = new HomeModel(AccountAddonsAsync, user.Id);
        _settingsModel = new SettingsModel(new AccountSettingsStore(account));
        _settingsModel.Changed += ApplySettings;
        _plus = new PlusModel(new AccountPlusStore(account));
        _continue = new ContinueModel(new AccountContinueStore(account), async title =>
        {
            // a movie played to the end is marked watched on My List (once)
            var list = MyListFor();
            if (list.Items is null) await list.LoadAsync();
            if (!list.IsWatched(title.Id)) await list.ToggleWatchedAsync(title);
        });
        _continue.LogoLookup = async (item, cancel) => (await _addonClient.FetchMetaAsync(ArcTV.Core.AppInfo.CinemetaManifestUrl, item.IsMovie ? "movie" : "series", item.ContentId, cancel))?.Logo;
        _picked = new PickedModel(
            () => new PickedInputs(_plus.Active, _plus.StatusKnown && _plus.RatingsLoaded && _home.State == HomeState.Loaded && MyListFor().Items is not null,
                BlockedGenres.Without(_home.AllRows.SelectMany(row => row.Items).GroupBy(i => i.Id).Select(g => g.First()).ToList(), _settingsModel.Blocked),
                MyListFor().Items ?? [], _plus.Ratings, _continue.Items.Select(i => i.ContentId).ToList()),
            FeatureLoading.Loader(new FeatureCache(Path.Combine(AppPaths.DataFolder(), "features.json")), FeaturesOfAsync),
            new AccountPickedStore(account), Path.Combine(AppPaths.DataFolder(), "picked.json"));

        foreach (var place in Places)
        {
            var pill = new Button { Content = place, Tag = place, Style = (Style)FindResource("NavPill") };
            pill.Click += (_, _) => Open(place);
            _pills[place] = pill;
            Pills.Children.Add(pill);
        }

        BuildAccountButton(user);

        // a click on any poster (or suggestion) that stands for a title opens that title; the mouse's Back button and Escape come back
        AddHandler(Button.ClickEvent, new RoutedEventHandler(OnTitleClick));
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1) GoBack();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _history.Count > 0 && Keyboard.FocusedElement is not TextBox && Page.Content is not PlayerView) GoBack(); // the player has its own Esc (it leaves full screen first)
        };
        Open("Home");
        Loaded += async (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(OnWindowMessage);
            var settings = _settingsModel.LoadAsync(); // at the same time as Home: its rows are reordered and filtered when the settings arrive
            var plus = LoadPlusDataAsync(); // and Plus, which Picked for you needs
            await _home.LoadAsync();
            await settings;
            await plus;
            _picked.Refresh();
        };
        Unloaded += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source) source.RemoveHook(OnWindowMessage);
        };
    }

    private const int WmMouseHWheel = 0x020E;

    /// <summary>
    /// WPF has no event for a sideways swipe on a touchpad (or a tilt wheel), so the window's own message is listened for: it moves the row the pointer is over.
    /// </summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmMouseHWheel) return IntPtr.Zero;
        var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        DependencyObject? hit = InputHitTest(PointFromScreen(new Point(x, y))) as DependencyObject;
        while (hit is not null and not ArrowScrollViewer) hit = hit is Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
        if (hit is ArrowScrollViewer row)
        {
            row.Swipe(delta);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void BuildAccountButton(AccountUser user)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!;
        var initial = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(4, 0, 10, 0),
            Background = (Brush)FindResource("BrandGradient"),
            Child = new TextBlock { Text = name[..1].ToUpperInvariant(), FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var label = new TextBlock { Text = name, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), FontWeight = FontWeights.SemiBold };
        AccountButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { initial, label } };

        var email = new MenuItem { Header = new TextBlock { Text = user.Email, Foreground = (Brush)FindResource("Text2Brush") }, IsEnabled = false, Style = (Style)FindResource("DarkMenuItem") };
        var signOut = new MenuItem { Header = "Sign out", Style = (Style)FindResource("DarkMenuItem") };
        signOut.Click += async (_, _) => await _account.SignOutAsync();
        AccountMenu.Items.Add(email);
        AccountMenu.Items.Add(new Separator { Style = (Style)FindResource("DarkMenuSeparator") });
        AccountMenu.Items.Add(signOut);
    }

    private void OnAccount(object sender, RoutedEventArgs e)
    {
        AccountMenu.PlacementTarget = AccountButton;
        AccountMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        AccountMenu.IsOpen = true;
    }

    private void OnLogo(object sender, MouseButtonEventArgs e) => Open("Home");

    private async Task<IReadOnlyList<SyncedAddon>> AccountAddonsAsync(CancellationToken cancel) =>
        await _account.AuthorizedAsync((client, token) => client.AddonsAsync(token, cancel: cancel));

    /// <summary>The addons Movies and TV Shows browse. Kept once found, so each visit does not ask the account again.</summary>
    private async Task<IReadOnlyList<HomeSource>> SourcesAsync(CancellationToken cancel)
    {
        if (_sources.Count > 0) return _sources;
        var found = await AddonSources.LoadAsync(AccountAddonsAsync, _addonClient, cancel);
        if (found.Count > 0) _sources = found;
        return found;
    }

    /// <summary>One model per kind, kept for the session, so coming back to Movies finds the same titles in the same order.</summary>
    private BrowseModel BrowseFor(string type)
    {
        if (!_browse.TryGetValue(type, out var model)) _browse[type] = model = new BrowseModel(type, SourcesAsync, _addonClient) { Blocked = _settingsModel.Blocked };
        return model;
    }

    /// <summary>One model for the session: coming back to My List shows what was there while it is fetched again.</summary>
    private MyListModel MyListFor() => _myList ??= new MyListModel(
        cancel => _account.AuthorizedAsync((client, token) => client.WatchlistAsync(token, cancel)),
        item => _account.AuthorizedAsync(async (client, token) =>
        {
            await client.SaveToWatchlistAsync(token, item);
            return true;
        }),
        item => _account.AuthorizedAsync(async (client, token) =>
        {
            await client.RemoveFromWatchlistAsync(token, item);
            return true;
        }));

    /// <summary>The account's settings reached Home, Movies, TV Shows and Search: row order and visibility, and blocked genres (a change in them makes Movies and TV Shows start over).</summary>
    private void ApplySettings()
    {
        _home.Preferences = _settingsModel.RowPreferences;
        var key = string.Join("|", _settingsModel.Blocked.OrderBy(g => g, StringComparer.OrdinalIgnoreCase));
        if (key == _blockedKey) return;
        _blockedKey = key;
        _home.BlockedGenres = _settingsModel.Blocked;
        _browse.Clear();
    }

    /// <summary>What the player follows from Settings: subtitles on at the start, the languages, and how subtitles look.</summary>
    private PlaybackPreferences PlaybackPrefs()
    {
        var s = _settingsModel.Current;
        return new PlaybackPreferences(s?.SubtitlesEnabled ?? false, s?.DefaultSubtitleLanguage, s?.DefaultAudioLanguage, SubtitleStyle.SizeFrom(_local.SubtitleSize), SubtitleStyle.ColorFrom(_local.SubtitleColor));
    }

    /// <summary>What Picked for you needs from the account: whether it has Plus, what is rated, what was taken out of the row, and My List and Continue Watching (which Home asks for too).</summary>
    private async Task LoadPlusDataAsync()
    {
        _plus.PropertyChanged += (_, _) => _picked.Refresh();
        MyListFor().PropertyChanged += (_, _) => _picked.Refresh();
        _continue.PropertyChanged += (_, _) => _picked.Refresh();
        _home.PropertyChanged += (_, _) => _picked.Refresh();
        await _plus.LoadStatusAsync();
        if (!_plus.Active) return;
        var list = MyListFor();
        await Task.WhenAll(_plus.LoadRatingsAsync(), _picked.LoadRemovalsAsync(), list.Items is null ? list.LoadAsync() : Task.CompletedTask, _continue.LoadAsync());
        _picked.Refresh();
    }

    /// <summary>A title's genres, directors and cast, from Cinemeta (for Picked for you); null when it cannot be had.</summary>
    private async Task<Features?> FeaturesOfAsync(TitleRef title)
    {
        try
        {
            var meta = await _addonClient.FetchMetaAsync(ArcTV.Core.AppInfo.CinemetaManifestUrl, title.Type ?? "movie", title.Id);
            return meta is null ? null : Features.FromMeta(meta);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A rated title's poster, from Cinemeta (the account keeps the rating, not the picture).</summary>
    private async Task<string?> PosterFor(FeedbackItem item, CancellationToken cancel)
    {
        try
        {
            return (await _addonClient.FetchMetaAsync(ArcTV.Core.AppInfo.CinemetaManifestUrl, item.IsMovie ? "movie" : "series", item.ContentId, cancel))?.Poster;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>One model for the session. When addons change, what Home, Movies and TV Shows read from them is fetched again.</summary>
    private AddonsModel AddonsFor()
    {
        if (_addonsModel is not null) return _addonsModel;
        _addonsModel = new AddonsModel(new AccountAddonStore(_account), (link, cancel) => AddonInstall.FetchAsync(link, null, cancel));
        _addonsModel.Changed += () =>
        {
            _sources = [];
            _browse.Clear();
            _picked.Reload();
            _ = _home.LoadAsync();
        };
        return _addonsModel;
    }

    private void OnTitleClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { DataContext: MetaPreview item }) OpenTitle(item);
    }

    /// <summary>Shows a title's page over the one that is open; Back puts that page back exactly as it was (where it was scrolled to, what was typed).</summary>
    private void OpenTitle(MetaPreview item)
    {
        if (Page.Content is UIElement current) _history.Push((current, _place));
        Page.Content = new DetailView(new DetailModel(item, _addonClient, _extras), MyListFor(), GoBack, () => _home.AllRows.SelectMany(row => row.Items).ToList(), OpenSources, _plus, _continue);
    }

    /// <summary>The source list for a movie or an episode, over the page that asked for it; Back returns to that page.</summary>
    private void OpenSources(PlayTarget target)
    {
        target = _continue.Resume(target); // a title in Continue Watching starts from where it was left
        if (Page.Content is UIElement current) _history.Push((current, _place));
        var store = new LastSourceStore(Path.Combine(AppPaths.DataFolder(), "last-sources.json"), _account.Session!.User.Id);
        Page.Content = new SourcesView(new SourcesModel(target, AccountAddonsAsync, _addonClient), store, GoBack, OpenPlayer);
    }

    /// <summary>The player, over the sources page; the top bar is out of the way while it plays, and Back returns to the sources.</summary>
    private void OpenPlayer(PlayingItem item)
    {
        if (Page.Content is UIElement current) _history.Push((current, _place));
        Bar.Visibility = Visibility.Collapsed;
        Page.Content = new PlayerView(item, () => new MpvEngine(PlaybackPrefs()), GoBack, (target, position, duration) => _ = _continue.ReportAsync(target, position, duration));
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;
        Bar.Visibility = Visibility.Visible;
        var (page, place) = _history.Pop();
        _place = place;
        foreach (var (name, pill) in _pills) pill.Tag = name == place ? "selected" : name;
        Page.Content = page;
    }

    private void Open(string place)
    {
        if (place == _place && _history.Count == 0) return;
        _history.Clear();
        Bar.Visibility = Visibility.Visible;
        _place = place;
        foreach (var (name, pill) in _pills) pill.Tag = name == place ? "selected" : name;
        Page.Content = place switch
        {
            "Home" => new HomeView(_home, MyListFor(), _extras, OpenSources, _continue, _picked),
            "Movies" => new BrowseView("Movies", BrowseFor("movie")),
            "TV Shows" => new BrowseView("TV Shows", BrowseFor("series")),
            "My List" => new MyListView(MyListFor()),
            "Settings" => new SettingsView(_settingsModel, AddonsFor(), _home, _user, _local, _plus, MyListFor(), PosterFor, () => { _picked.Reload(); _ = _home.LoadAsync(); }, () => _ = _account.SignOutAsync()),
            "Search" => new SearchView(new SearchModel(_account.Session!.User.Id, SourcesAsync, new RecentSearchStore(Path.Combine(AppPaths.DataFolder(), "recent-searches.json")), _addonClient) { Blocked = _settingsModel.Blocked }),
            _ => new ComingSoonView(place),
        };
    }
}
