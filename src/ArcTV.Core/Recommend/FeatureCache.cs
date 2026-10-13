using System.Text.Json;

namespace ArcTV.Core.Recommend;

/// <summary>Remembers each title's genres, directors and cast so a refresh rarely touches the network. Kept in a file so it survives launches.</summary>
public sealed class FeatureCache(string? file)
{
    private sealed record Entry(Features Features, DateTime At);

    private readonly object _lock = new();
    private Dictionary<string, Entry> _store = [];
    private bool _loaded;

    public Features? Get(string id, DateTime? now = null)
    {
        lock (_lock)
        {
            LoadIfNeeded();
            return _store.TryGetValue(id, out var entry) && (now ?? DateTime.UtcNow) - entry.At <= RecommendTuning.FeatureCacheTtl ? entry.Features : null;
        }
    }

    public void Put(IReadOnlyDictionary<string, Features> features, DateTime? now = null)
    {
        lock (_lock)
        {
            LoadIfNeeded();
            foreach (var (id, value) in features) _store[id] = new Entry(value, now ?? DateTime.UtcNow);
            if (_store.Count > RecommendTuning.FeatureCacheMaxEntries)
                foreach (var key in _store.OrderBy(p => p.Value.At).Take(_store.Count - RecommendTuning.FeatureCacheMaxEntries).Select(p => p.Key).ToList()) _store.Remove(key);
            Save();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                LoadIfNeeded();
                return _store.Count;
            }
        }
    }

    private void LoadIfNeeded()
    {
        if (_loaded) return;
        _loaded = true;
        if (file is null || !File.Exists(file)) return;
        try
        {
            _store = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(file)) ?? [];
        }
        catch (Exception)
        {
            _store = [];
        }
    }

    private void Save()
    {
        if (file is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(_store));
        }
        catch (Exception)
        {
            // the cache is an optimisation
        }
    }
}

public static class FeatureLoading
{
    /// <summary>A loader that reads the cache first and looks up at most <c>limit</c> unknown titles through <paramref name="fetch"/>, a few at a time. A failed lookup is just left out; it never throws.</summary>
    public static FeatureLoader Loader(FeatureCache cache, Func<TitleRef, Task<Features?>> fetch, int concurrency = RecommendTuning.DetailFetchConcurrency) => async (refs, limit) =>
    {
        var output = new Dictionary<string, Features>();
        var missing = new List<TitleRef>();
        var seen = new HashSet<string>();
        foreach (var r in refs)
        {
            if (!seen.Add(r.Id)) continue;
            if (cache.Get(r.Id) is { } cached) output[r.Id] = cached; else missing.Add(r);
        }

        var fetched = new System.Collections.Concurrent.ConcurrentDictionary<string, Features>();
        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        await Task.WhenAll(missing.Take(limit).Select(async r =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (await fetch(r).ConfigureAwait(false) is { HasAny: true } features) fetched[r.Id] = features;
            }
            catch (Exception)
            {
                // left out
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);
        if (!fetched.IsEmpty) cache.Put(fetched);
        foreach (var (id, features) in fetched) output.TryAdd(id, features);
        return output;
    };
}
