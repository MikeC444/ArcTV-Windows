using ArcTV.Core.Addons;
using ArcTV.Core.Home;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Browse;

/// <summary>
/// Loads Movies or TV Shows a page at a time, as you scroll, from every addon the account has (the web app's pager). What a genre has loaded is kept for
/// as long as this model lives, so coming back from a title finds the same titles in the same order. Call it from the UI thread: it announces changes
/// where it was called from.
/// </summary>
public sealed class BrowseModel(string type, Func<CancellationToken, Task<IReadOnlyList<HomeSource>>> sources, AddonClient? client = null, Random? random = null) : ObservableObject
{
    private sealed record Snapshot(List<MetaPreview> Items, int Page, bool HasMore);

    private readonly AddonClient _client = client ?? new AddonClient();
    private readonly Random _random = random ?? new Random();
    private readonly Dictionary<string, Snapshot> _cache = [];
    private readonly HashSet<string> _seen = [];
    private IReadOnlyList<MetaPreview> _items = [];
    private IReadOnlyList<HomeSource> _sources = [];
    private bool _loading = true;
    private bool _noAddons;
    private string? _genre;
    private int _page = 1;
    private bool _hasMore = true;
    private bool _fetching;
    private int _generation;

    public string Type { get; } = type;

    public IReadOnlyList<MetaPreview> Items { get => _items; private set => Set(ref _items, value); }

    /// <summary>True until the first page of what is chosen has arrived.</summary>
    public bool Loading { get => _loading; private set => Set(ref _loading, value); }

    /// <summary>True when no addon offers anything to browse (not even Cinemeta).</summary>
    public bool NoAddons { get => _noAddons; private set => Set(ref _noAddons, value); }

    private string CacheKey => $"{Type}|{_genre}|{string.Join(",", _sources.Select(s => s.Manifest.Id))}";

    /// <summary>The first page of the genre chosen (all genres when null), or what was already loaded for it.</summary>
    public async Task ReloadAsync(string? genre, CancellationToken cancel = default)
    {
        var generation = ++_generation;
        _genre = genre;
        _fetching = false;
        Loading = true;

        var found = await sources(cancel);
        if (generation != _generation) return;
        _sources = found;
        NoAddons = found.Count == 0;

        if (_cache.TryGetValue(CacheKey, out var hit))
        {
            Restore(hit);
            Loading = false;
            return;
        }

        _seen.Clear();
        _page = 1;
        _hasMore = true;
        Items = [];
        if (found.Count == 0)
        {
            Loading = false;
            return;
        }

        var lists = await Task.WhenAll(found.Select(source => source.BrowsePageAsync(Type, genre, 0, _client, cancel)));
        if (generation != _generation) return;

        // Movies and shows are a shuffled browse page, so the same titles are not first every time.
        var merged = HomeSections.DistinctById(Shuffled(lists.SelectMany(l => l)));
        foreach (var item in merged) _seen.Add(item.Id);
        Items = merged;
        if (merged.Count > 0) _cache[CacheKey] = new Snapshot(merged, _page, _hasMore);
        Loading = false;
    }

    /// <summary>The next page, added to the end. Does nothing while one is on its way, once the addons have no more, or before the first page is in.</summary>
    public async Task LoadMoreAsync(CancellationToken cancel = default)
    {
        if (!_hasMore || _fetching || _sources.Count == 0 || Items.Count == 0) return;
        _fetching = true;
        var generation = _generation;
        var wanted = _page;
        var genre = _genre;

        IEnumerable<MetaPreview>[] lists;
        try
        {
            lists = await Task.WhenAll(_sources.Select(source => source.BrowsePageAsync(Type, genre, wanted, _client, cancel)));
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation) _fetching = false;
            return;
        }

        if (generation != _generation) return;

        var fresh = HomeSections.DistinctById(Shuffled(lists.SelectMany(l => l))).Where(item => !_seen.Contains(item.Id)).ToList();
        if (fresh.Count == 0)
        {
            _hasMore = false;
        }
        else
        {
            _page++;
            foreach (var item in fresh) _seen.Add(item.Id);
            Items = [.. Items, .. fresh];
        }

        _cache[CacheKey] = new Snapshot(Items.ToList(), _page, _hasMore);
        _fetching = false;
    }

    private void Restore(Snapshot hit)
    {
        _page = hit.Page;
        _hasMore = hit.HasMore;
        _seen.Clear();
        foreach (var item in hit.Items) _seen.Add(item.Id);
        Items = hit.Items.ToList();
    }

    private List<MetaPreview> Shuffled(IEnumerable<MetaPreview> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
