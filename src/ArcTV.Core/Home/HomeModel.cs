using ArcTV.Core.Addons;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Home;

public enum HomeState { Idle, Loading, Loaded, Empty }

/// <summary>
/// What Home shows: rows built from the account's addons (every enabled one that offers catalogues; Cinemeta when there is none), the titles the banner rotates
/// through, and the state of loading. Rows appear as each batch arrives.
/// </summary>
public sealed class HomeModel(Func<CancellationToken, Task<IReadOnlyList<SyncedAddon>>> addons, string userId, AddonClient? client = null, Random? random = null, HomeCache? cache = null) : ObservableObject
{
    public const int HeroSize = 10;

    private readonly AddonClient _client = client ?? new AddonClient();
    private readonly Random _random = random ?? new Random();
    private readonly List<HomeSection> _raw = [];
    private readonly object _lock = new();
    private CancellationTokenSource? _run;
    private HomeState _state;
    private IReadOnlyList<HomeSection> _sections = [];
    private IReadOnlyList<MetaPreview> _hero = [];
    private HomeRowPreferences _preferences = new();
    private IReadOnlySet<string> _blocked = new HashSet<string>();
    private SynchronizationContext? _context;
    private bool _fromCache;

    public HomeState State { get => _state; private set => Set(ref _state, value); }

    /// <summary>The rows as shown: hidden ones left out, the person's order, and a title in only one row.</summary>
    public IReadOnlyList<HomeSection> Sections { get => _sections; private set => Set(ref _sections, value); }

    public IReadOnlyList<MetaPreview> Hero { get => _hero; private set => Set(ref _hero, value); }

    public bool Complete { get; private set; }

    public HomeRowPreferences Preferences
    {
        get => _preferences;
        set
        {
            _preferences = value;
            Rebuild();
        }
    }

    /// <summary>Genres hidden everywhere (Settings > Blocked Genres): titles in them are left out of every row and the banner.</summary>
    public IReadOnlySet<string> BlockedGenres
    {
        get => _blocked;
        set
        {
            _blocked = value;
            Rebuild();
        }
    }

    /// <summary>Every row as it arrived (before hiding and ordering), for Settings > Home Rows.</summary>
    public IReadOnlyList<HomeSection> AllRows
    {
        get
        {
            lock (_lock) return _raw.ToList();
        }
    }

    /// <summary>Starts (or restarts) loading. Changes are announced on the caller's thread when it has a synchronization context (the UI thread).</summary>
    public async Task LoadAsync()
    {
        _run?.Cancel();
        var run = _run = new CancellationTokenSource();
        var cancel = run.Token;
        _context = SynchronizationContext.Current;
        lock (_lock) _raw.Clear();
        Complete = false;
        // the rows of the last time are on screen at once; the fresh ones replace them as soon as the first arrive
        var saved = cache?.Load(userId);
        _fromCache = saved is not null;
        if (saved is not null) lock (_lock) _raw.AddRange(saved);
        Post(() =>
        {
            Sections = [];
            Hero = [];
            State = HomeState.Loading;
            if (saved is not null) Rebuild();
        });

        try
        {
            var sources = await AddonSources.LoadAsync(addons, _client, cancel).ConfigureAwait(false);

            var seed = $"{userId}|{HomeVariety.DayStamp()}";
            await Task.WhenAll(sources.Select(source => HomeSections.LoadAsync(source, seed, batch =>
            {
                if (cancel.IsCancellationRequested) return Task.CompletedTask;
                lock (_lock)
                {
                    if (_fromCache)
                    {
                        _fromCache = false;
                        _raw.Clear();
                    }

                    _raw.AddRange(batch);
                }

                Post(() => { if (!cancel.IsCancellationRequested) Rebuild(); });
                return Task.CompletedTask;
            }, client: _client, cancel: cancel))).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancel.IsCancellationRequested) return;
        Complete = true;
        if (!_fromCache) cache?.Save(AllRows, userId); // (when nothing fresh arrived, what was saved stays)
        await PostAsync(() =>
        {
            if (cancel.IsCancellationRequested) return;
            Rebuild(final: true);
            State = Sections.Count == 0 ? HomeState.Empty : HomeState.Loaded;
        }).ConfigureAwait(false);
    }

    private void Rebuild(bool final = false)
    {
        List<HomeSection> raw;
        lock (_lock) raw = _raw.ToList();
        if (_blocked.Count > 0) raw = raw.Select(s => s with { Items = Settings.BlockedGenres.Without(s.Items, _blocked) }).Where(s => s.Items.Count > 0).ToList();
        var hidden = _preferences.HiddenOrEmpty.ToHashSet();
        var shown = HomeSections.Deduped(HomeSections.Ordered(raw.Where(s => !hidden.Contains(s.Id)).ToList(), _preferences));
        Sections = shown;
        if (shown.Count > 0 && State == HomeState.Loading) State = HomeState.Loaded;
        // a banner title in a genre blocked since it was chosen (the settings arrive after the rows on a start from the saved rows) is chosen again
        if (_blocked.Count > 0 && Settings.BlockedGenres.Without(Hero, _blocked).Count < Hero.Count) Hero = [];
        if (Hero.Count == 0 || (final && Hero.Count < HeroSize))
        {
            var backup = raw.Where(s => hidden.Contains(s.Id)).SelectMany(s => s.Items);
            Hero = HeroPool.PickTitles(shown.SelectMany(s => s.Items), backup, HeroSize, _random);
        }
    }

    /// <summary>Like <see cref="Post"/> but finishes once the change has been made, so a caller that awaits <see cref="LoadAsync"/> sees the final state.</summary>
    private Task PostAsync(Action action)
    {
        if (_context is null)
        {
            action();
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource();
        _context.Post(_ =>
        {
            try { action(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        }, null);
        return done.Task;
    }

    private void Post(Action action)
    {
        if (_context is null) action();
        else _context.Post(_ => action(), null);
    }
}
