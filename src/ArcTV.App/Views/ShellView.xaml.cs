using System.IO;
using System.Net.Http;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArcTV.App.Controls;
using ArcTV.Core;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Player;
using ArcTV.Core.Plus;
using ArcTV.Core.Profiles;
using ArcTV.Core.Settings;
using ArcTV.Core.Browse;
using ArcTV.Core.Continue;
using ArcTV.Core.Details;
using ArcTV.Core.Downloads;
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
    private readonly Action? _switchProfile;
    private readonly Dictionary<string, string?> _logos = [];

    public ShellView(AccountModel account) : this(account, new PlusModel(new AccountPlusStore(account)))
    {
    }

    /// <summary>
    /// <paramref name="plus"/> is the account's Plus state (already read when profiles were); <paramref name="profile"/> is the profile in use, if the account has profiles, and
    /// <paramref name="switchProfile"/> goes back to "Who's watching?" (null when there is nothing to switch to).
    /// </summary>
    public ShellView(AccountModel account, PlusModel plus, Profile? profile = null, Action? switchProfile = null)
    {
        InitializeComponent();
        _account = account;
        _switchProfile = switchProfile;
        _extras = new AccountDetailExtras(account);
        var user = account.Session!.User;
        _user = user;
        var scope = account.Scope ?? user.Id; // what this PC's local copies are filed under: the account, and the profile when it is not the account's own
        _home = new HomeModel(AccountAddonsAsync, scope, cache: new HomeCache(AppPaths.DataFolder())); // the last rows show at once while the fresh ones load
        _kids = profile is { IsKids: true };
        _settingsModel = new SettingsModel(new AccountSettingsStore(account)) { AlsoBlocked = profile is { IsKids: true } ? Profile.KidsBlockedGenres : [] };
        _settingsModel.Changed += ApplySettings;
        _plus = plus;
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
            new AccountPickedStore(account), Path.Combine(AppPaths.DataFolder(), account.ProfileId == Profile.MainId ? "picked.json" : $"picked-{account.ProfileId}.json"));

        foreach (var place in Places)
        {
            var pill = new Button { Content = place, Tag = place, Style = (Style)FindResource("NavPill") };
            pill.Click += (_, _) => Open(place);
            _pills[place] = pill;
            Pills.Children.Add(pill);
        }

        BuildAccountButton(user, profile);
        SizeChanged += (_, _) =>
        {
            PosterMetrics.Instance.Update(ActualWidth); // the posters are drawn bigger or smaller with the window
            FitBar();
        };

        // a click on any poster (or suggestion) that stands for a title opens that title; right-clicking one opens its menu; the mouse's Back button and Escape come back
        AddHandler(Button.ClickEvent, new RoutedEventHandler(OnTitleClick));
        AddHandler(MouseRightButtonUpEvent, new MouseButtonEventHandler(OnCardRightClick));
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnPageScrolled));
        Bar.RenderTransform = new TranslateTransform();
        MouseMove += (_, e) =>
        {
            if (!_barShown && e.GetPosition(this).Y < 24) ShowBar(true); // the pointer at the top edge brings the bar back
        };
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1) GoBack();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && MenuLayer.Content is not null)
            {
                CloseCardMenu();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _history.Count > 0 && Keyboard.FocusedElement is not TextBox && Page.Content is not PlayerView) GoBack(); // the player has its own Esc (it leaves full screen first)
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
            if (ShowOffers) await CheckForOffersAsync(); // a newer version of the app, and the Plus welcome, once Home is up
        };
        Unloaded += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source) source.RemoveHook(OnWindowMessage);
        };
    }

    // ---- popups that offer something: a newer version, and the Plus welcome (as on the Mac) ----

    /// <summary>False in the tests, so a shell that opens does not go and ask GitHub.</summary>
    public static bool ShowOffers { get; set; } = true;

    private readonly bool _kids;
    private ReleaseInfo? _pendingUpdate;
    private bool _checkedForUpdate;
    private bool _welcomeShown;

    /// <summary>Asks GitHub once per launch whether a newer version is out (quiet when offline, or when that version was skipped), then shows whatever is due.</summary>
    private async Task CheckForOffersAsync()
    {
        if (!_checkedForUpdate)
        {
            _checkedForUpdate = true;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var result = await UpdateCheck.CheckAsync(AppInfo.Version, http);
                if (UpdateCheck.Due(result, _local.SkippedUpdate)) _pendingUpdate = result.Release;
            }
            catch (Exception)
            {
                // offline, or no release yet: nothing to offer
            }
        }

        ShowDueOffers();
    }

    /// <summary>One card at a time, only over Home with nothing else open: the update first, then (for members, once) the Plus welcome. Called again whenever one is closed and whenever Home is shown.</summary>
    private void ShowDueOffers()
    {
        if (MenuLayer.Content is not null || Page.Content is not HomeView) return;
        if (_pendingUpdate is { } release)
        {
            _pendingUpdate = null;
            MenuLayer.Content = new UpdateOfferView(release, AppInfo.Version, skip =>
            {
                if (skip) _local.SkippedUpdate = release.Version;
                CloseCardMenu();
                ShowDueOffers();
            }, found =>
            {
                var address = found.DownloadUrl ?? found.PageUrl;
                if (address is not null) WebLinks.Open(address);
            });
            return;
        }

        if (_welcomeShown || _kids || !_plus.StatusKnown || !_plus.Active) return;
        var key = PlusPopups.WelcomeKey(_user.Id);
        if (!PlusPopups.WelcomeDue(_local.PopupSeen(key))) return;
        _welcomeShown = true; // once a launch
        void Done()
        {
            _local.MarkPopupSeen(key, PlusPopups.WelcomeId); // seen for good, whichever way it was closed
            CloseCardMenu();
        }

        MenuLayer.Content = new PlusWelcomeView(Done, () =>
        {
            Done();
            OpenSettingsPane("Plus settings");
        });
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

    private void BuildAccountButton(AccountUser user, Profile? profile)
    {
        var name = profile?.Name ?? (string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!);
        FrameworkElement initial = profile is not null ? ProfilePickerView.Avatar(profile.Avatar, 46) : new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Margin = new Thickness(4, 0, 10, 0),
            Background = (Brush)FindResource("BrandGradient"),
            Child = new TextBlock { Text = name[..1].ToUpperInvariant(), FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        initial.Margin = new Thickness(4, 0, 10, 0);
        var label = new TextBlock { Text = name, MaxWidth = 180, FontSize = 18, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), FontWeight = FontWeights.SemiBold };
        AccountButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { initial, label } };

        var email = new MenuItem { Header = new TextBlock { Text = user.Email, Foreground = (Brush)FindResource("Text2Brush") }, IsEnabled = false, Style = (Style)FindResource("DarkMenuItem") };
        var signOut = new MenuItem { Header = "Sign out", Style = (Style)FindResource("DarkMenuItem") };
        signOut.Click += async (_, _) => await _account.SignOutAsync();
        AccountMenu.Items.Add(email);
        var downloads = new MenuItem { Header = "Downloads", Style = (Style)FindResource("DarkMenuItem") };
        downloads.Click += (_, _) => OpenDownloadsPage();
        AccountMenu.Items.Add(downloads);
        if (_switchProfile is not null)
        {
            var switchItem = new MenuItem { Header = "Switch profile", Style = (Style)FindResource("DarkMenuItem") };
            switchItem.Click += (_, _) => _switchProfile();
            AccountMenu.Items.Add(switchItem);
        }

        AccountMenu.Items.Add(new Separator { Style = (Style)FindResource("DarkMenuSeparator") });
        AccountMenu.Items.Add(signOut);
    }

    /// <summary>
    /// Keeps the top bar inside the window: the logo, the places and the profile button need a certain width, and in a narrower window the whole bar shrinks to fit (down to half size)
    /// instead of pushing the profile button out of sight.
    /// </summary>
    private void FitBar()
    {
        if (ActualWidth <= 0) return;
        var natural = Logo.DesiredSize.Width + PillsBox.DesiredSize.Width + PillsBox.Margin.Left + PillsBox.Margin.Right + AccountButton.DesiredSize.Width;
        if (natural <= 0) return;
        BarScale.ScaleX = BarScale.ScaleY = Math.Clamp((ActualWidth - 64) / natural, 0.5, 1);
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
    /// <summary>The subtitle files the account's addons offer for the video being played (OpenSubtitles and the like), the preferred language first.</summary>
    private async Task<IReadOnlyList<ArcTV.Core.Addons.AddonSubtitle>> FindAddonSubtitlesAsync(PlayingItem item, CancellationToken cancel) =>
        await ArcTV.Core.Addons.SubtitleFinder.FindAsync(await AccountAddonsAsync(cancel), item.Target.Title.Type, item.Target.StreamId, item.Filename, _settingsModel.Current?.DefaultSubtitleLanguage, _addonClient, cancel);

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

    /// <summary>
    /// A title's logo for the sources page and the player when what started them has none (Continue Watching and some rows carry only the poster and backdrop): Continue Watching's own
    /// lookup if it has it, else Cinemeta, remembered for the session. Null when there is none.
    /// </summary>
    private async Task<string?> LogoOfAsync(MetaDetail title)
    {
        if (!string.IsNullOrEmpty(title.Logo)) return title.Logo;
        if (_continue.Logos.TryGetValue(title.Id, out var known)) return known;
        if (_logos.TryGetValue(title.Id, out var cached)) return cached;
        string? found = null;
        try
        {
            found = (await _addonClient.FetchMetaAsync(ArcTV.Core.AppInfo.CinemetaManifestUrl, title.Type, title.Id))?.Logo;
        }
        catch (Exception)
        {
            // the words stand in for the logo
        }

        _logos[title.Id] = found;
        return found;
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

    // ---- the top bar steps out of the way while a page is scrolled down ----

    private bool _barShown = true;

    /// <summary>Scrolling a page down takes the top bar away, scrolling up (or being near the top of the page, or putting the pointer at the window's top edge) brings it back.</summary>
    private void OnPageScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scroller || e.VerticalChange == 0 || scroller.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) return;
        if (scroller.VerticalOffset < 40 || e.VerticalChange < -4) ShowBar(true);
        else if (e.VerticalChange > 4) ShowBar(false);
    }

    private void ShowBar(bool shown)
    {
        if (_barShown == shown || Bar.Visibility != Visibility.Visible || Bar.RenderTransform is not TranslateTransform move) return;
        _barShown = shown;
        Bar.IsHitTestVisible = shown;
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(shown ? 0 : -Bar.ActualHeight, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        Bar.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(shown ? 1 : 0, TimeSpan.FromMilliseconds(220)));
    }

    // ---- the card menu ----

    private async void OnCardRightClick(object sender, MouseButtonEventArgs e)
    {
        if (MenuLayer.Content is not null || Page.Content is PlayerView || e.OriginalSource is not DependencyObject source || CardMenu.Find(source) is not { } card) return;
        e.Handled = true;
        try
        {
            var list = MyListFor();
            if (list.Items is null) await list.LoadAsync();
            if (!_plus.StatusKnown) await _plus.LoadStatusAsync();
            if (_plus.Active && !_plus.RatingsLoaded) await _plus.LoadRatingsAsync();
        }
        catch (Exception)
        {
            // the menu opens with what is known
        }

        if (MenuLayer.Content is not null) return;
        MenuLayer.Content = new CardMenuView(card.Item, card.FromPicked, MyListFor(), _plus, _continue, _picked, CloseCardMenu, OpenSources, OpenTitle, LogoOfAsync);
    }

    private void CloseCardMenu() => MenuLayer.Content = null;

    private void OnTitleClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { DataContext: MetaPreview item }) OpenTitle(item);
    }

    /// <summary>Shows a title's page over the one that is open; Back puts that page back exactly as it was (where it was scrolled to, what was typed).</summary>
    private void OpenTitle(MetaPreview item)
    {
        ShowBar(true);
        if (Page.Content is UIElement current) _history.Push((current, _place));
        Page.Content = new DetailView(new DetailModel(item, _addonClient, _extras), MyListFor(), GoBack, () => _home.AllRows.SelectMany(row => row.Items).ToList(), OpenSources, _plus, _continue, OpenDownloads);
    }

    private static readonly DownloadManager Downloads = new(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, DownloadManager.DefaultFolder(), Path.Combine(AppPaths.DataFolder(), "downloads.json")); // the list is kept, so a stopped download can be resumed

    /// <summary>The Downloads panel for a movie or episode, over the page (Arc TV Plus; without it the panel says so and offers the Plus page).</summary>
    private void OpenDownloads(PlayTarget target)
    {
        MenuLayer.Content = new DownloadPanelView(target, async (t, cancel) => await SourceFinder.FindAsync(await AccountAddonsAsync(cancel), t.Title.Type, t.StreamId, _addonClient, cancel),
            Downloads, _local, _plus.Active, () =>
            {
                _settingsPane = "Arc TV Plus";
                Open("Settings");
            }, CloseCardMenu, _ => { }, OpenDownloadsPage);
    }

    /// <summary>The "How to set up your sources" guide, over the page that is open (Back puts that page back).</summary>
    private void OpenSetupGuide()
    {
        ShowBar(true);
        if (Page.Content is UIElement current && current is not SetupGuideView) _history.Push((current, _place));
        Page.Content = new SetupGuideView(GoBack, () => OpenSettingsPane("Addons"));
    }

    /// <summary>Settings, opened on one of its pages.</summary>
    private void OpenSettingsPane(string pane)
    {
        _settingsPane = pane;
        Open("Settings");
    }

    /// <summary>The Downloads page, over the one that is open (Back puts that page back).</summary>
    private void OpenDownloadsPage()
    {
        ShowBar(true);
        if (Page.Content is UIElement current && current is not DownloadsView) _history.Push((current, _place));
        Page.Content = new DownloadsView(Downloads, _local);
    }

    private string _settingsPane = "Account";

    private string TakeSettingsPane()
    {
        var pane = _settingsPane;
        _settingsPane = "Account";
        return pane;
    }

    /// <summary>The source list for a movie or an episode, over the page that asked for it; Back returns to that page.</summary>
    private void OpenSources(PlayTarget target)
    {
        ShowBar(true);
        target = _continue.Resume(target); // a title in Continue Watching starts from where it was left
        if (Page.Content is UIElement current) _history.Push((current, _place));
        Page.Content = SourcesPage(target);
    }

    /// <summary>
    /// The source page for a target. It skips itself (and the player starts) only when a source can be picked without asking: the next episode of a binge, or (with Plus) the best one
    /// when smart source picking is on. Playing something watched before shows the list, with the source last used marked.
    /// </summary>
    private SourcesView SourcesPage(PlayTarget target)
    {
        var store = new LastSourceStore(Path.Combine(AppPaths.DataFolder(), "last-sources.json"), _account.Scope!);
        var smart = _plus.Active && _local.SmartPicking;
        var model = new SourcesModel(target, AccountAddonsAsync, _addonClient, items => AutoPick.Choose(target, items, smart));
        return new SourcesView(model, store, GoBack, OpenPlayer, LogoOfAsync, TorrentEngines.Shared, OpenSetupGuide, () => OpenSettingsPane("Addons"));
    }

    /// <summary>The player, over the sources page; the top bar is out of the way while it plays, and Back returns to the sources (or, when the source was picked without asking, to the page before).</summary>
    private void OpenPlayer(PlayingItem item)
    {
        if (Page.Content is UIElement current && current is not SourcesView { AutoChosen: true }) _history.Push((current, _place));
        Bar.Visibility = Visibility.Collapsed;
        Page.Content = new PlayerView(item, () => new MpvEngine(PlaybackPrefs()), GoBack, (target, position, duration) => _ = _continue.ReportAsync(target, position, duration), PlayNext, () => _settingsModel.Current?.AutoplayNextEpisode ?? true, LogoOfAsync, ChooseAnotherSource, FindAddonSubtitlesAsync, PlaybackPrefs);
    }

    /// <summary>The player gives way to this title's source list (a source that hangs, or is not wanted, can be swapped); Back from the list goes where Back from the player would have.</summary>
    private void ChooseAnotherSource(PlayTarget target)
    {
        if (_history.Count > 0 && _history.Peek().Item1 is SourcesView) _history.Pop();
        Bar.Visibility = Visibility.Visible;
        ShowBar(true); // (it may have been put away by scrolling before the video)
        Page.Content = SourcesPage(target);
    }

    /// <summary>The next episode takes the player's place (Back from it goes where Back from this one would have).</summary>
    private void PlayNext(PlayTarget target)
    {
        if (_history.Count > 0 && _history.Peek().Item1 is SourcesView) _history.Pop(); // the list for the episode that just ended is out of date
        Page.Content = SourcesPage(target);
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;
        ShowBar(true);
        Bar.Visibility = Visibility.Visible;
        ShowBar(true); // (it may have been put away by scrolling before the video)
        var (page, place) = _history.Pop();
        _place = place;
        foreach (var (name, pill) in _pills) pill.Tag = name == place ? "selected" : name;
        Page.Content = page;
    }

    private void Open(string place)
    {
        ShowBar(true);
        if (place == _place && _history.Count == 0) return;
        _history.Clear();
        Bar.Visibility = Visibility.Visible;
        ShowBar(true); // (it may have been put away by scrolling before the video)
        _place = place;
        foreach (var (name, pill) in _pills) pill.Tag = name == place ? "selected" : name;
        Page.Content = place switch
        {
            "Home" => new HomeView(_home, MyListFor(), _extras, OpenSources, _continue, _picked),
            "Movies" => new BrowseView("Movies", BrowseFor("movie")),
            "TV Shows" => new BrowseView("TV Shows", BrowseFor("series")),
            "My List" => new MyListView(MyListFor()),
            "Settings" => new SettingsView(_settingsModel, AddonsFor(), _home, _user, _local, _plus, MyListFor(), PosterFor, () => { _picked.Reload(); _ = _home.LoadAsync(); }, () => _ = _account.SignOutAsync(), TakeSettingsPane()),
            "Search" => new SearchView(new SearchModel(_account.Scope!, SourcesAsync, new RecentSearchStore(Path.Combine(AppPaths.DataFolder(), "recent-searches.json")), _addonClient) { Blocked = _settingsModel.Blocked }),
            _ => new ComingSoonView(place),
        };
        if (place == "Home") Dispatcher.BeginInvoke(ShowDueOffers); // a waiting offer shows when Home does
    }
}
