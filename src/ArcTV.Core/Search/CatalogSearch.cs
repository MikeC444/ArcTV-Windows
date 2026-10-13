using ArcTV.Core.Addons;
using ArcTV.Core.Home;

namespace ArcTV.Core.Search;

/// <summary>What a search has found so far (the web app's SearchResults).</summary>
public sealed record SearchProgress(IReadOnlyList<MetaPreview> Movies, IReadOnlyList<MetaPreview> TvShows, int Pending, int Failed);

/// <summary>
/// Searching every addon at once. Results are handed over the moment each addon answers, merged in the same addon order so rows don't jump about.
/// An addon with a catalogue that can search is asked for real; otherwise its base catalogues are filtered by title. A slow addon is left behind.
/// </summary>
public static class CatalogSearch
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private sealed class Collector(int count)
    {
        private readonly object _lock = new();
        private readonly List<MetaPreview>?[] _lists = new List<MetaPreview>?[count];
        private int _pending = count;
        private int _failed;

        public void Set(int index, List<MetaPreview> items)
        {
            lock (_lock) _lists[index] = items;
        }

        public void Finish(bool failed)
        {
            lock (_lock)
            {
                _pending--;
                if (failed) _failed++;
            }
        }

        public SearchProgress Snapshot()
        {
            lock (_lock)
            {
                var merged = HomeSections.DistinctById(HomeSections.Interleaved(_lists.Where(l => l is not null).Select(l => l!).ToList()));
                return new SearchProgress(merged.Where(i => i.Type == "movie").ToList(), merged.Where(i => i.Type == "series").ToList(), _pending, _failed);
            }
        }
    }

    /// <param name="onUpdate">Called (from any thread) each time something new has arrived.</param>
    public static async Task<SearchProgress> SearchAsync(IReadOnlyList<HomeSource> sources, string query, AddonClient? client = null, Action<SearchProgress>? onUpdate = null,
        TimeSpan? timeout = null, CancellationToken cancel = default)
    {
        var addons = client ?? new AddonClient();
        var collector = new Collector(sources.Count);
        await Task.WhenAll(sources.Select(async (source, index) =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            var work = SearchOneAsync(source, query, addons, partial =>
            {
                collector.Set(index, partial);
                onUpdate?.Invoke(collector.Snapshot());
            }, stop.Token);

            List<MetaPreview>? answered = null;
            var finished = await Task.WhenAny(work, Task.Delay(timeout ?? Timeout, cancel)).ConfigureAwait(false);
            if (finished == work)
            {
                answered = await work.ConfigureAwait(false);
            }
            else
            {
                stop.Cancel();
                _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // the abandoned search ends in a cancellation nobody awaits
            }

            if (answered is not null) collector.Set(index, answered);
            collector.Finish(failed: answered is null);
            onUpdate?.Invoke(collector.Snapshot());
        })).ConfigureAwait(false);
        return collector.Snapshot();
    }

    internal static async Task<List<MetaPreview>> SearchOneAsync(HomeSource source, string query, AddonClient client, Action<List<MetaPreview>> partial, CancellationToken cancel)
    {
        var catalogs = source.Catalogs;
        var searchable = catalogs.Where(c => c.SupportsSearch).ToList();
        if (searchable.Count > 0)
        {
            var perCatalog = Enumerable.Range(0, searchable.Count).Select(_ => new List<MetaPreview>()).ToList();
            var request = new Dictionary<string, string> { ["search"] = query };
            List<MetaPreview> Merged()
            {
                lock (perCatalog) return HomeSections.DistinctById(HomeSections.Interleaved(perCatalog.ToList()));
            }

            await Task.WhenAll(searchable.Select(async (catalog, index) =>
            {
                var items = await client.TryFetchCatalogAsync(source.ManifestUrl, catalog.Type, catalog.Id, request, cancel).ConfigureAwait(false);
                lock (perCatalog) perCatalog[index] = items;
                if (items.Count > 0) partial(Merged());
            })).ConfigureAwait(false);
            var found = Merged();
            if (found.Count > 0) return found;
        }

        var baseCatalogs = catalogs.Where(c => c.IsBase).ToList();
        var lists = await Task.WhenAll(baseCatalogs.Select(c => client.TryFetchCatalogAsync(source.ManifestUrl, c.Type, c.Id, null, cancel))).ConfigureAwait(false);
        return HomeSections.DistinctById(HomeSections.Interleaved(lists)).Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

/// <summary>Recent searches: newest first, a repeat (any letter case) moves up instead of doubling, and only the newest few are kept.</summary>
public static class RecentSearches
{
    public const int Limit = 8;

    public static List<string> Adding(string query, IReadOnlyList<string> list)
    {
        var q = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (q.Length == 0) return list.ToList();
        return list.Where(t => !string.Equals(t, q, StringComparison.OrdinalIgnoreCase)).Prepend(q).Take(Limit).ToList();
    }
}
