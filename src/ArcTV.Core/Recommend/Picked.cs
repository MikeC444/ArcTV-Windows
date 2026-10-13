using System.Text.Json;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.MyList;
using ArcTV.Core.Mvvm;
using ArcTV.Core.Plus;

namespace ArcTV.Core.Recommend;

/// <summary>One pick on the Home row: the title and, in a personalised row, why it is there.</summary>
public sealed record PickedEntry(MetaPreview Item, string? Reason)
{
    public string Id => Item.Id;
}

/// <summary>The "Picked for you" row, ready to place on Home.</summary>
public sealed record PickedRow(string Title, bool Personal, IReadOnlyList<PickedEntry> Entries)
{
    /// <summary>
    /// The row from an engine result: null when there is nothing to show. <paramref name="hiddenIds"/> are titles that must not show now (rated Like or Not for me, finished, or in
    /// Continue Watching) and <paramref name="dismissed"/> those removed by hand; they are dropped at once, before any recompute, so they disappear the moment they are marked.
    /// </summary>
    public static PickedRow? From(EngineResult? result, IEnumerable<MetaPreview> pool, ISet<string> hiddenIds, ISet<string> dismissed)
    {
        if (result is null || result.Items.Count == 0) return null;
        var byId = new Dictionary<string, MetaPreview>();
        foreach (var item in pool) byId.TryAdd(item.Id, item);
        var entries = result.Items
            .Where(p => byId.ContainsKey(p.Id) && !hiddenIds.Contains(p.Id) && !dismissed.Contains(p.Id))
            .Select(p => new PickedEntry(byId[p.Id], result.IsPersonal ? p.Reason : null)).ToList();
        if (entries.Count == 0) return null;
        return new PickedRow(result.IsPersonal ? RecommendTuning.PickedRowTitle : RecommendTuning.PopularRowTitle, result.IsPersonal, entries.Take(RecommendTuning.MaxResults).ToList());
    }
}

/// <summary>A title taken out of "Picked for you" (<c>GET /user/picked-dismissals</c>).</summary>
public sealed record PickedDismissal(string ProfileId, string ContentId, string DismissedAt);

internal sealed record PickedDismissalsResponse(List<PickedDismissal>? Items);

/// <summary>Titles taken out of the row by hand: each stays out for five days, then the algorithm decides again.</summary>
public static class PickedDismissals
{
    public static TimeSpan Window => TimeSpan.FromDays(RecommendConfig.PickedDismissDays);

    /// <summary>The ids still out of the row at <paramref name="now"/>.</summary>
    public static HashSet<string> Active(IReadOnlyDictionary<string, DateTime> removed, DateTime now) => removed.Where(p => now - p.Value < Window).Select(p => p.Key).ToHashSet();

    /// <summary>Only the removals that still count; the newest wins when the same title appears twice.</summary>
    public static Dictionary<string, DateTime> Merged(IReadOnlyDictionary<string, DateTime> local, IEnumerable<PickedDismissal> server, DateTime now)
    {
        var output = local.Where(p => now - p.Value < Window).ToDictionary(p => p.Key, p => p.Value);
        foreach (var item in server)
        {
            if (IsoDate.Parse(item.DismissedAt) is not { } at || now - at >= Window) continue;
            if (!output.TryGetValue(item.ContentId, out var existing) || existing < at) output[item.ContentId] = at;
        }

        return output;
    }
}

public static class PickedApi
{
    public static async Task<List<PickedDismissal>> PickedDismissalsAsync(this AuthClient client, string accessToken, CancellationToken cancel = default)
    {
        var body = await client.SendJsonAsync(HttpMethod.Get, "/user/picked-dismissals", new Dictionary<string, string> { ["profileId"] = client.ProfileId ?? "main" }, null, accessToken, cancel: cancel).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<PickedDismissalsResponse>(body, ApiHttp.Json)?.Items ?? throw ApiException.Unreadable();
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    public static Task DismissPickedAsync(this AuthClient client, string accessToken, string contentId, DateTime at, CancellationToken cancel = default) =>
        client.SendJsonAsync(HttpMethod.Post, "/user/picked-dismissals", null, JsonSerializer.Serialize(new { profileId = client.ProfileId ?? "main", contentId, dismissedAt = IsoDate.From(at) }), accessToken, cancel: cancel);
}

/// <summary>Where the dismissals are kept on the account. The tests use a stand-in.</summary>
public interface IPickedStore
{
    Task<IReadOnlyList<PickedDismissal>> DismissalsAsync(CancellationToken cancel = default);

    Task DismissAsync(string contentId, DateTime at, CancellationToken cancel = default);
}

public sealed class AccountPickedStore(AccountModel account) : IPickedStore
{
    public async Task<IReadOnlyList<PickedDismissal>> DismissalsAsync(CancellationToken cancel = default) => await account.AuthorizedAsync((c, t) => c.PickedDismissalsAsync(t, cancel)).ConfigureAwait(false);

    public Task DismissAsync(string contentId, DateTime at, CancellationToken cancel = default) =>
        account.AuthorizedAsync(async (c, t) =>
        {
            await c.DismissPickedAsync(t, contentId, at, cancel).ConfigureAwait(false);
            return true;
        });
}

/// <summary>What the "Picked for you" row is made from, read each time something that feeds it may have changed.</summary>
public sealed record PickedInputs(bool Active, bool Ready, IReadOnlyList<MetaPreview> Pool, IReadOnlyList<WatchlistItem> List, IReadOnlyDictionary<string, FeedbackItem> Ratings, IReadOnlyCollection<string> ContinueIds);

/// <summary>
/// "Picked for you" (Arc TV Plus): works the row out again when what feeds it has changed (ratings, My List, Continue Watching, what is on offer). The picks are chosen with this launch's seed,
/// so they stay put until Home is refreshed. A title rated, finished or started disappears from the row at once, before any recompute.
/// </summary>
public sealed class PickedModel(Func<PickedInputs> inputs, FeatureLoader loader, IPickedStore store, string? stateFile = null, Func<DateTime>? now = null, Random? random = null) : ObservableObject
{
    private sealed record State(Dictionary<string, string>? Removed, List<string>? Shown);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly Random _random = random ?? new Random();
    private readonly Dictionary<string, DateTime> _removed = [];
    private HashSet<string> _previousShown = [];
    private int _seed;
    private bool _seeded;
    private bool _loadedState;
    private bool _rememberedShown;
    private string _key = "";
    private EngineResult? _result;
    private IReadOnlyList<MetaPreview> _resultPool = [];
    private PickedRow? _row;
    private CancellationTokenSource? _run;

    /// <summary>The row for Home, or null when there is none (no Plus, nothing picked yet).</summary>
    public PickedRow? Row { get => _row; private set => Set(ref _row, value); }

    private int Seed
    {
        get
        {
            if (!_seeded)
            {
                _seed = _random.Next();
                _seeded = true;
            }

            return _seed;
        }
    }

    /// <summary>Works the row out again if what feeds it changed, and refreshes what shows (hiding anything newly rated, finished or started).</summary>
    public void Refresh()
    {
        LoadState();
        var x = inputs();
        if (!x.Active)
        {
            _result = null;
            _key = "";
            Row = null;
            return;
        }

        if (!x.Ready)
        {
            Row = Build(x);
            return;
        }

        var pool = Pool(x);
        var types = x.List.GroupBy(i => i.ContentId).ToDictionary(g => g.Key, g => g.First().ContentType == "MOVIE" ? "movie" : "series");
        var refs = new Dictionary<string, TitleRef>();
        var rated = new Dictionary<string, (string Title, Rating Rating)>();
        foreach (var (id, item) in x.Ratings)
        {
            if (item.Rating is not { } rating) continue;
            rated[id] = (item.Title, rating);
            refs[id] = new TitleRef(id, item.ProviderId, item.IsMovie ? "movie" : "series");
        }

        foreach (var item in x.List) refs.TryAdd(item.ContentId, new TitleRef(item.ContentId, item.ProviderId, types.GetValueOrDefault(item.ContentId)));
        var interactions = Interactions.Collect(Interactions.Inputs(x.List, rated));
        var key = Interactions.Signature(interactions) + "#" + string.Join(",", pool.Select(p => p.Id));
        if (key != _key)
        {
            _key = key;
            var excluded = Interactions.Excluded(x.List, x.Ratings.Keys, x.ContinueIds, PickedDismissals.Active(_removed, _now()));
            var input = new EngineInput(interactions, excluded, pool.Select(Candidate.From).ToList(), refs, loader, Seed, _previousShown);
            _run?.Cancel();
            var run = _run = new CancellationTokenSource();
            _ = RunAsync(input, pool, run);
        }

        Row = Build(x);
    }

    private async Task RunAsync(EngineInput input, IReadOnlyList<MetaPreview> pool, CancellationTokenSource run)
    {
        EngineResult result;
        try
        {
            result = await Engine.RecommendAsync(input);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }

        if (run.IsCancellationRequested) return;
        _result = result;
        _resultPool = pool;
        if (!_rememberedShown && result.IsPersonal)
        {
            _rememberedShown = true;
            SaveState(result.Items.Select(i => i.Id).ToList());
        }

        Row = Build(inputs());
    }

    private PickedRow? Build(PickedInputs x)
    {
        if (!x.Active || _result is null) return null;
        var hidden = new HashSet<string>(x.Ratings.Keys);
        hidden.UnionWith(x.ContinueIds);
        foreach (var item in x.List)
            if (item.Watched == true) hidden.Add(item.ContentId);
        return PickedRow.From(_result, _resultPool, hidden, PickedDismissals.Active(_removed, _now()));
    }

    private static List<MetaPreview> Pool(PickedInputs x)
    {
        var seen = new HashSet<string>();
        return x.Pool.Where(p => seen.Add(p.Id)).ToList();
    }

    /// <summary>A refresh swaps most of the row: what is showing now becomes "shown last time" and a new draw starts.</summary>
    public void Reload()
    {
        if (Row is { } shown) _previousShown = shown.Entries.Select(e => e.Id).ToHashSet();
        _seed = _random.Next();
        _seeded = true;
        _key = "";
        _rememberedShown = false;
    }

    /// <summary>Takes a title out of the row for five days (no taste signal), on every device.</summary>
    public async Task RemoveAsync(string id, CancellationToken cancel = default)
    {
        LoadState();
        var at = _now();
        _removed[id] = at;
        SaveState(null);
        Row = Build(inputs());
        try
        {
            await store.DismissAsync(id, at, cancel).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // kept on this PC all the same
        }
    }

    /// <summary>Reads the removals the account has (made on another device too).</summary>
    public async Task LoadRemovalsAsync(CancellationToken cancel = default)
    {
        LoadState();
        try
        {
            var items = await store.DismissalsAsync(cancel).ConfigureAwait(true);
            var merged = PickedDismissals.Merged(_removed, items, _now());
            _removed.Clear();
            foreach (var (id, at) in merged) _removed[id] = at;
            SaveState(null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // keep what is known
        }
    }

    private void LoadState()
    {
        if (_loadedState) return;
        _loadedState = true;
        if (stateFile is null || !File.Exists(stateFile)) return;
        try
        {
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(stateFile));
            foreach (var (id, text) in state?.Removed ?? [])
                if (IsoDate.Parse(text) is { } at && _now() - at < PickedDismissals.Window) _removed[id] = at;
            _previousShown = (state?.Shown ?? []).ToHashSet();
        }
        catch (Exception)
        {
            // start clean
        }
    }

    private void SaveState(List<string>? shown)
    {
        if (stateFile is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            var live = _removed.Where(p => _now() - p.Value < PickedDismissals.Window).ToDictionary(p => p.Key, p => IsoDate.From(p.Value));
            File.WriteAllText(stateFile, JsonSerializer.Serialize(new State(live, shown ?? _previousShown.ToList())));
        }
        catch (Exception)
        {
            // a removal is still held in memory
        }
    }
}
