using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ArcTV.App.Player;
using ArcTV.App.Views;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Browse;
using ArcTV.Core.Details;
using ArcTV.Core.Home;
using ArcTV.Core.MyList;
using ArcTV.Core.Search;
using ArcTV.Core.Plus;
using ArcTV.Core.Settings;
using ArcTV.Core.Sources;
using ArcTV.Core.Storage;
using Xunit;

namespace ArcTV.App.Tests;

/// <summary>Each screen is created, shown (off to the side) and laid out. Any broken XAML, missing style or bad binding shows up here.</summary>
public class ScreenTests
{
    private sealed class Handler(Func<string, string> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer(request.RequestUri!.AbsolutePath), Encoding.UTF8, "application/json") });
    }

    private const string Manifest = """{"id":"x","name":"Cool","catalogs":[{"type":"movie","id":"all","extra":[{"name":"genre","options":["Noir","Anime"]}]}]}""";

    private static string Answer(string path) => path switch
    {
        "/user/addons" => $$"""{"items":[{"manifestUrl":"https://x.test/manifest.json","addonId":"x","name":"Cool","enabled":true,"sortOrder":0,"manifestJson":{{Manifest}}}]}""",
        var meta when meta.StartsWith("/meta/", StringComparison.Ordinal) =>
            """{"meta":{"id":"tt9","type":"series","name":"A Show","releaseInfo":"2020","runtime":"49 min","genres":["Drama"],"description":"<b>About</b> it","cast":["Ann","Bob"],"videos":[{"id":"tt9:1:1","season":1,"episode":1,"name":"Pilot","overview":"It starts."},{"id":"tt9:2:1","season":2,"episode":1,"name":"Again"}]}}""",
        _ => """{"metas":[{"id":"a","type":"movie","name":"Alpha","description":"<b>Hi</b> &amp; bye","genres":["Noir"],"releaseInfo":"2020","imdbRating":"7.1"},{"id":"b","type":"movie","name":"Beta"}]}""",
    };

    /// <summary>Runs on a thread of its own that can show windows, with the app's colours and styles loaded.</summary>
    private static void OnUiThread(Func<Task> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ArcTV;component/Theme/Theme.xaml") });
                }

                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var task = body();
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
                Dispatcher.CurrentDispatcher.InvokeShutdown(); // closes any window left open, so none gets a message after this thread has gone
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("A screen failed to open: " + failure, failure);
    }

    private static Window Host(FrameworkElement content)
    {
        var window = new Window { Width = 1280, Height = 800, Left = -3000, Top = 0, ShowActivated = false, ShowInTaskbar = false, Content = content };
        window.Show();
        return window;
    }

    private static async Task Settle() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static AccountModel SignedIn(string folder)
    {
        var store = new SessionStore(Path.Combine(folder, "s.dat"), new PlainProtector());
        store.Save(new Session("a", "e", "r", "e", new AccountUser("u1", "me@x.test", "Me")));
        return new AccountModel(store, new DeviceIdentity(Path.Combine(folder, "d.id"), "PC"), new LocalSettings(Path.Combine(folder, "set.json")), new HttpClient(new Handler(Answer)));
    }

    [Fact]
    public void ThemeStylesAllLoad() => OnUiThread(() =>
    {
        foreach (var key in new[] { "PrimaryButton", "GhostButton", "LinkButton", "Field", "PasswordField", "FieldLabel", "NavPill", "ChoicePill", "SuggestionRow", "PosterButton", "SeasonCircle", "DarkMenu", "DarkMenuItem", "DarkMenuSeparator", "RowArrow", "ArrowRowScroll", "DotButton" })
            Assert.NotNull(Application.Current.TryFindResource(key));
        return Task.CompletedTask;
    });

    [Fact]
    public void SignInScreenOpens() => OnUiThread(async () =>
    {
        var view = new SignInView();
        var window = Host(view);
        await Settle();
        Assert.True(view.ActualWidth > 0);
        window.Close();
    });

    [Fact]
    public void HomeScreenShowsRowsAndBanner() => OnUiThread(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "arctv-ui-" + Guid.NewGuid());
        var account = SignedIn(folder);
        var model = new HomeModel(async cancel => await account.AuthorizedAsync((client, token) => client.AddonsAsync(token, cancel: cancel)), "u1",
            new AddonClient(new HttpClient(new Handler(Answer))));
        var view = new HomeView(model);
        var window = Host(view);
        await model.LoadAsync();
        await Settle();
        Assert.Equal(HomeState.Loaded, model.State);
        Assert.NotEmpty(model.Sections);
        Assert.NotEmpty(model.Hero);
        window.Close();
    });

    [Fact]
    public void MoviesScreenShowsPostersAndGenres() => OnUiThread(async () =>
    {
        var client = new AddonClient(new HttpClient(new Handler(Answer)));
        var sources = new List<HomeSource> { new("https://x.test/manifest.json", System.Text.Json.JsonSerializer.Deserialize<AddonManifest>(Manifest, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!) };
        var model = new BrowseModel("movie", _ => Task.FromResult<IReadOnlyList<HomeSource>>(sources), client);
        var view = new BrowseView("Movies", model);
        var window = Host(view);
        await Settle();
        await Task.Delay(300);
        await Settle();
        Assert.Equal(2, model.Items.Count);
        Assert.True(view.ActualHeight > 0);
        window.Close();
    });

    [Fact]
    public void SearchScreenSearchesAndShowsResults() => OnUiThread(async () =>
    {
        var client = new AddonClient(new HttpClient(new Handler(Answer)));
        var sources = new List<HomeSource> { new("https://x.test/manifest.json", System.Text.Json.JsonSerializer.Deserialize<AddonManifest>(SearchManifest, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!) };
        var model = new SearchModel("u1", _ => Task.FromResult<IReadOnlyList<HomeSource>>(sources), null, client);
        var view = new SearchView(model);
        var window = Host(view);
        await Settle();
        await model.SearchAsync("alp");
        await Settle();
        Assert.Equal(SearchState.Results, model.State);
        Assert.Equal(2, model.Movies.Count);
        Assert.True(view.ActualHeight > 0);
        window.Close();
    });

    private const string SearchManifest = """{"id":"x","name":"Cool","catalogs":[{"type":"movie","id":"all","extra":[{"name":"search"}]}]}""";

    [Fact]
    public void MyListScreenShowsSavedTitlesAndTheirTicks() => OnUiThread(async () =>
    {
        var items = new List<WatchlistItem>
        {
            new("p", "tt1", "MOVIE", "Alpha", Year: 2020, Rating: 7.5, Watched: true, UpdatedAt: "2026-01-01T00:00:00.000Z"),
            new("p", "tt2", "TV_SHOW", "Beta", UpdatedAt: "2026-01-02T00:00:00.000Z"),
        };
        var model = new MyListModel(_ => Task.FromResult(items));
        var view = new MyListView(model);
        var window = Host(view);
        await model.LoadAsync();
        await Settle();
        Assert.Equal(2, model.Items!.Count);
        Assert.True(view.ActualHeight > 0);
        window.Close();
    });

    [Fact]
    public void TitleScreenShowsDetailsSeasonsAndCast() => OnUiThread(async () =>
    {
        var client = new AddonClient(new HttpClient(new Handler(Answer)));
        var model = new DetailModel(new MetaPreview("tt9", "series", "A Show"), client);
        var list = new MyListModel(_ => Task.FromResult(new List<ArcTV.Core.MyList.WatchlistItem>()));
        var view = new DetailView(model, list, () => { });
        var window = Host(view);
        await Settle();
        await model.LoadAsync();
        await list.LoadAsync();
        await Settle();
        Assert.Equal(new[] { 1, 2 }, model.Detail.SeasonNumbers);
        Assert.Equal(2, model.Cast.Count);
        model.ChooseSeason(2);
        await Settle();
        Assert.Single(model.Episodes);
        Assert.True(view.ActualHeight > 0);
        window.Close();
    });

    [Fact]
    public void SourcesScreenListsStreamsAndFilters() => OnUiThread(async () =>
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<AddonManifest>("""{"id":"s","name":"S","types":["movie"],"resources":["stream"]}""", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var addons = new List<SyncedAddon> { new("https://s.test/manifest.json", "s", "Streams", true, 0, manifest) };
        var client = new AddonClient(new HttpClient(new Handler(path => path.StartsWith("/stream/", StringComparison.Ordinal)
            ? """{"streams":[{"name":"D","title":"Film 1080p WEB-DL","url":"https://cdn.test/f.mkv"},{"name":"T","title":"Film 720p","infoHash":"abc"}]}"""
            : "{}")));
        var model = new SourcesModel(new PlayTarget(new MetaDetail("tt1", "movie", "Film")), _ => Task.FromResult<IReadOnlyList<SyncedAddon>>(addons), client);
        var view = new SourcesView(model, null, () => { });
        var window = Host(view);
        await model.LoadAsync();
        await Settle();
        Assert.Equal(2, model.Arranged.Count);
        Assert.Equal(model.Arranged[0].Id, model.RecommendedId);
        model.Filter = SourceFilter.Hd;
        await Settle();
        Assert.Equal(2, model.Arranged.Count); // the recommended source always stays, with the 720p one
        Assert.True(view.ActualHeight > 0);
        window.Close();
    });

    private sealed class FakeEngine : IVideoEngine
    {
        public IntPtr StartedIn;
        public Uri? Url;
        public bool Paused_;

        public event Action<double>? Position;
        public event Action<double>? Duration;
        public event Action<bool>? Paused;
        public event Action<bool>? Buffering;
        public event Action<bool>? Seeking;
        public event Action? Started;
        public event Action? Ended;
        public event Action<string>? Failed;

        public void Start(IntPtr window, Uri url, double startAt)
        {
            StartedIn = window;
            Url = url;
            Duration?.Invoke(5400);
            Position?.Invoke(12);
            Buffering?.Invoke(false);
            Started?.Invoke();
        }

        public void SetPause(bool paused) { Paused_ = paused; Paused?.Invoke(paused); }
        public void SeekTo(double seconds) => Position?.Invoke(seconds);
        public void SeekBy(double seconds) { }
        public void SetVolume(double volume) { }
        public void SetSpeed(double speed) { }
        public void SetAudioTrack(int? id) { }
        public void SetSubtitleTrack(int? id) { }
        public string? GetString(string property) => null;
        public void Shutdown() { }

        public void Unused() { _ = Seeking; _ = Ended; _ = Failed; }
    }

    [Fact]
    public void PlayerScreenOpensStartsTheEngineAndShowsItsOverlay() => OnUiThread(async () =>
    {
        var engine = new FakeEngine();
        var target = new PlayTarget(new MetaDetail("tt1", "movie", "Film"));
        var view = new PlayerView(new PlayingItem(new Uri("https://cdn.test/f.mkv"), "Film", "Addon", target), () => engine, () => { });
        var window = Host(view);
        for (var i = 0; i < 40 && engine.Url is null; i++)
        {
            await Task.Delay(50);
            await Settle();
        }

        Assert.NotNull(engine.Url);
        Assert.NotEqual(IntPtr.Zero, engine.StartedIn);
        Assert.NotNull(view.Model);
        Assert.False(view.Model!.Loading);
        window.Close();
        await Settle();
    });

    private sealed class OneAddonStore : IAddonStore
    {
        public Task<IReadOnlyList<SyncedAddon>> ListAsync(CancellationToken cancel = default) =>
            Task.FromResult<IReadOnlyList<SyncedAddon>>([new SyncedAddon("https://t.test/manifest.json", "t", "Torrentio", true, 0, new AddonManifest("t", "Torrentio", "0.1", "Streams from torrents", null, null, null, [new AddonResource("stream")])) { RawManifest = "{}" }]);

        public Task SaveAsync(string manifestUrl, string addonId, string name, string rawManifest, bool enabled, int sortOrder, CancellationToken cancel = default) => Task.CompletedTask;

        public Task RemoveAsync(string manifestUrl, CancellationToken cancel = default) => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore : IAccountSettingsStore
    {
        public Task<AccountSettings> GetAsync(CancellationToken cancel = default) => Task.FromResult(AccountSettings.Default with { BlockedGenres = ["Horror"] });

        public Task<AccountSettings> SaveAsync(AccountSettings settings, CancellationToken cancel = default) => Task.FromResult(settings);
    }

    private sealed class FakePlusStore : IPlusStore
    {
        public Task<PlusStatus> StatusAsync(CancellationToken cancel = default) => Task.FromResult(new PlusStatus(true, "yearly", "2027-01-01T00:00:00.000Z", true, false, 7));

        public Task<CheckoutLink> StartCheckoutAsync(string plan, CancellationToken cancel = default) => Task.FromResult(new CheckoutLink("https://x.test/"));

        public Task<PlusStatus> CancelAsync(CancellationToken cancel = default) => Task.FromResult(PlusStatus.None);

        public Task<IReadOnlyList<FeedbackItem>> FeedbackAsync(CancellationToken cancel = default) =>
            Task.FromResult<IReadOnlyList<FeedbackItem>>([new FeedbackItem("main", "c", "tt1", "MOVIE", "A Film", "like", "2026-10-01T00:00:00.000Z")]);

        public Task SetFeedbackAsync(FeedbackItem item, CancellationToken cancel = default) => Task.CompletedTask;

        public Task ClearFeedbackAsync(FeedbackItem item, CancellationToken cancel = default) => Task.CompletedTask;

        public Task<(IReadOnlyList<HistoryItem> Items, bool Truncated)> HistoryAsync(CancellationToken cancel = default) =>
            Task.FromResult<(IReadOnlyList<HistoryItem>, bool)>(([new HistoryItem("p", "tt1", "MOVIE", "A Film", 0, 7_200_000, true, IsoDate.Now())], false));
    }

    [Fact]
    public void SettingsScreenOpensEveryPlace() => OnUiThread(async () =>
    {
        var addons = new AddonsModel(new OneAddonStore(), (link, _) => Task.FromResult(AddonInstall.Parse(link, "{}")));
        var settings = new SettingsModel(new FakeSettingsStore());
        var home = new HomeModel(_ => Task.FromResult<IReadOnlyList<SyncedAddon>>([]), "u1");
        var folder = Path.Combine(Path.GetTempPath(), "arctv-ui-" + Guid.NewGuid());
        var local = new LocalSettings(Path.Combine(folder, "settings.json"));
        var plus = new PlusModel(new FakePlusStore());
        foreach (var place in new[] { "Account", "Addons", "Home Rows", "Blocked Genres", "Player", "Subtitles", "Audio", "Updates", "Arc TV Plus", "Plus settings", "Recommendations", "Your stats" })
        {
            var view = new SettingsView(settings, addons, home, new AccountUser("u1", "me@x.test", "Me"), local, plus, new MyListModel(_ => Task.FromResult(new List<WatchlistItem>())), (_, _) => Task.FromResult<string?>("https://img.test/p.jpg"), () => { }, () => { }, place);
            var window = Host(view);
            await Settle();
            await Task.Delay(150);
            await Settle();
            Assert.True(view.ActualHeight > 0, place);
            window.Close();
        }

        Assert.Contains("horror", settings.Blocked);
    });

    [Fact]
    public void ShellOpensWithItsTopBar() => OnUiThread(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "arctv-ui-" + Guid.NewGuid());
        var view = new ShellView(SignedIn(folder));
        var window = Host(view);
        await Settle();
        await Task.Delay(500);
        await Settle();
        Assert.True(view.ActualHeight > 0);
        window.Close();
    });

    private sealed class FakeProfiles : ArcTV.Core.Profiles.IProfileStore
    {
        public List<ArcTV.Core.Profiles.Profile> Items =
        [
            new("main", "Me", "fox", IsDefault: true),
            new("p2", "Kid", "cat", "kids", HasPin: true),
        ];

        public Task<IReadOnlyList<ArcTV.Core.Profiles.Profile>> ListAsync(CancellationToken cancel = default) => Task.FromResult<IReadOnlyList<ArcTV.Core.Profiles.Profile>>(Items.ToList());

        public Task CreateAsync(string name, string avatar, string kind, string? pin, CancellationToken cancel = default)
        {
            Items.Add(new("new", name, avatar, kind, pin is not null));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(string id, string name, string avatar, string kind, ArcTV.Core.Profiles.PinChange pin, string? currentPin, CancellationToken cancel = default) => Task.CompletedTask;

        public Task DeleteAsync(string id, string? currentPin, CancellationToken cancel = default) => Task.CompletedTask;

        public Task<ArcTV.Core.Profiles.PinOutcome> VerifyPinAsync(string id, string pin, CancellationToken cancel = default) =>
            Task.FromResult(pin == "1234" ? ArcTV.Core.Profiles.PinOutcome.Ok : ArcTV.Core.Profiles.PinOutcome.Wrong);
    }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void WhosWatchingShowsEachProfileAndAsksForAPin() => OnUiThread(async () =>
    {
        var model = new ArcTV.Core.Profiles.ProfilesModel(new FakeProfiles());
        await model.LoadAsync(true);
        ArcTV.Core.Profiles.Profile? opened = null;
        var view = new ProfilePickerView(model, "main", p => opened = p);
        var window = Host(view);
        await Settle();
        Assert.Equal("list", view.State);
        var cards = Descendants<System.Windows.Controls.Button>(view).Where(b => b.Tag is "main" or "p2").ToList();
        Assert.Equal(2, cards.Count);

        // a profile without a PIN opens at once
        cards.Single(b => b.Tag is "main").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal("main", opened?.Id);

        // one with a PIN asks first; a wrong PIN is refused with words, the right one opens it
        opened = null;
        cards.Single(b => b.Tag is "p2").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Settle();
        Assert.Equal("pin", view.State);
        Assert.Null(opened);
        var box = Descendants<System.Windows.Controls.PasswordBox>(view).Single();
        var go = Descendants<System.Windows.Controls.Button>(view).Single(b => b.Content is "Continue");
        box.Password = "0000";
        go.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(100);
        await Settle();
        Assert.Null(opened);
        Assert.Contains(Descendants<System.Windows.Controls.TextBlock>(view), t => t.Text == "That PIN is wrong.");
        box.Password = "1234";
        go.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(100);
        await Settle();
        Assert.Equal("p2", opened?.Id);
        window.Close();
    });

    [Fact]
    public void ManageProfilesOffersAddingAndEditing() => OnUiThread(async () =>
    {
        var model = new ArcTV.Core.Profiles.ProfilesModel(new FakeProfiles());
        await model.LoadAsync(true);
        var view = new ProfilePickerView(model, null, _ => { });
        var window = Host(view);
        await Settle();
        Descendants<System.Windows.Controls.Button>(view).Single(b => b.Content is "Manage Profiles").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Settle();
        Assert.Contains(Descendants<System.Windows.Controls.Button>(view), b => b.Tag is "add");
        Descendants<System.Windows.Controls.Button>(view).Single(b => b.Tag is "add").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Settle();
        Assert.Equal("add", view.State);
        var name = Descendants<System.Windows.Controls.TextBox>(view).First();
        name.Text = "Gran";
        Descendants<System.Windows.Controls.Button>(view).Single(b => b.Content is "Save").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(100);
        await Settle();
        Assert.Equal("list", view.State);
        Assert.Equal(3, model.Profiles.Count);
        window.Close();
    });

    [Fact]
    public void DownloadsPanelListsDirectFilesAndInvitesWithoutPlus() => OnUiThread(async () =>
    {
        var target = new PlayTarget(new MetaDetail("tt1", "movie", "Film"));
        SourceItem[] items =
        [
            new("Torrentio", new AddonStream(Url: "https://example.com/a.mkv", Title: "Film 1080p"), 0),
            new("Torrentio", new AddonStream(InfoHash: "abc", Title: "Film torrent"), 1),
        ];
        var manager = new ArcTV.Core.Downloads.DownloadManager(new HttpClient(), Path.Combine(Path.GetTempPath(), "arctv-dl-screen-" + Guid.NewGuid().ToString("N")));
        var listed = new DownloadPanelView(target, (_, _) => Task.FromResult(new SourceResult(items, [], [])), manager, new LocalSettings(Path.Combine(Path.GetTempPath(), "arctv-dl-settings-" + Guid.NewGuid().ToString("N") + ".json")), true, () => { }, () => { }, _ => { });
        var window = Host(listed);
        for (var i = 0; i < 100 && listed.State != "list"; i++)
        {
            await Task.Delay(20);
            await Settle();
        }

        Assert.Equal("list", listed.State);
        Assert.Contains(Descendants<System.Windows.Controls.TextBlock>(listed), t => t.Text == "1 source"); // the torrent is not offered
        window.Close();

        var asked = false;
        var invite = new DownloadPanelView(target, (_, _) => Task.FromResult(new SourceResult()), manager, new LocalSettings(Path.Combine(Path.GetTempPath(), "arctv-dl-settings-" + Guid.NewGuid().ToString("N") + ".json")), false, () => asked = true, () => { }, _ => { });
        window = Host(invite);
        await Settle();
        Assert.Equal("plus", invite.State);
        Descendants<System.Windows.Controls.Button>(invite).Single(b => b.Content is "See Arc TV Plus").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.True(asked);
        window.Close();
    });
}
