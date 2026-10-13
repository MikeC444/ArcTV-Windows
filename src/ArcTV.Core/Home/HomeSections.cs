using ArcTV.Core.Addons;

namespace ArcTV.Core.Home;

/// <summary>One row on Home.</summary>
public sealed record HomeSection(string Id, string Title, IReadOnlyList<MetaPreview> Items);

/// <summary>What the person chose in Settings > Home Rows: the order of rows and the ones switched off.</summary>
public sealed record HomeRowPreferences(IReadOnlyList<string>? Order = null, IReadOnlyList<string>? HiddenRowIds = null)
{
    public IReadOnlyList<string> OrderOrEmpty => Order ?? [];
    public IReadOnlyList<string> HiddenOrEmpty => HiddenRowIds ?? [];
}

/// <summary>An addon that can fill Home: where it is and what its manifest declares.</summary>
public sealed record HomeSource(string ManifestUrl, AddonManifest Manifest)
{
    public List<CatalogDefinition> Catalogs => (Manifest.Catalogs ?? []).Where(c => c.Type is "movie" or "series").ToList();
}

/// <summary>
/// Home's rows, built from the account's addons the way the web, Fire TV and Mac apps build them. An addon shaped like Cinemeta (catalogues "top", "year"
/// and "imdbRating") gets Popular, New, Top rated and then the genres with wide appeal; any other addon gets its base catalogue and a row per genre.
/// </summary>
public static class HomeSections
{
    public static readonly double[] PageWeights = [5, 3, 2];
    public static readonly double[] NewPageWeights = [6, 4];
    public const int PageSize = 100;
    public const int MaxGenreRows = 30;

    /// <summary>Rows are fetched in small batches so the first rows appear fast and shared free addon servers aren't hammered.</summary>
    public const int BatchSize = 4;

    public static bool IsRanked(IReadOnlyCollection<CatalogDefinition> catalogs)
    {
        var ids = catalogs.Select(c => c.Id).ToHashSet();
        return ids.Contains("top") && ids.Contains("year") && ids.Contains("imdbRating");
    }

    public static bool IsYear(string text) => text.All(char.IsAsciiDigit) && int.TryParse(text, out var year) && year is >= 1900 and <= 2100;

    public static List<string> DeclaredGenres(IEnumerable<CatalogDefinition> catalogs) =>
        catalogs.SelectMany(c => c.GenreOptions).Distinct().Take(MaxGenreRows).ToList();

    /// <summary>movie[0], series[0], movie[1], ... so a merged row reads as mixed content.</summary>
    public static List<MetaPreview> Interleaved(IReadOnlyList<List<MetaPreview>> lists)
    {
        if (lists.Count == 1) return lists[0];
        var output = new List<MetaPreview>();
        var longest = lists.Count == 0 ? 0 : lists.Max(l => l.Count);
        for (var i = 0; i < longest; i++)
            foreach (var list in lists)
                if (i < list.Count) output.Add(list[i]);
        return output;
    }

    /// <summary>A title once: the first of each id.</summary>
    public static List<MetaPreview> DistinctById(IEnumerable<MetaPreview> items)
    {
        var seen = new HashSet<string>();
        return items.Where(i => seen.Add(i.Id)).ToList();
    }

    /// <summary>The rows an addon gives, a few at a time as they arrive. <paramref name="onBatch"/> is called with each batch (in order).</summary>
    public static async Task LoadAsync(HomeSource source, string seed, Func<IReadOnlyList<HomeSection>, Task> onBatch, DateTime? now = null, AddonClient? client = null, CancellationToken cancel = default)
    {
        var fetcher = new Fetcher(source, seed, client ?? new AddonClient(), cancel);
        var jobs = new List<Func<Task<HomeSection?>>>();
        var catalogs = source.Catalogs;
        if (catalogs.Count == 0) return;

        if (IsRanked(catalogs))
        {
            var top = catalogs.Where(c => c.Id == "top").ToList();
            var title = top.Select(c => c.Name).FirstOrDefault(n => n is not null) ?? "Popular";
            jobs.Add(() => fetcher.Varied(top, title, new Dictionary<string, string>(), "base", PageWeights));

            var yearCatalogs = catalogs.Where(c => c.Id == "year").ToList();
            var years = yearCatalogs.SelectMany(c => c.GenreOptions).Where(IsYear).Distinct().ToList();
            var thisYear = (now ?? DateTime.Now).Year.ToString();
            var year = years.Contains(thisYear) ? thisYear : years.FirstOrDefault();
            if (year is not null) jobs.Add(() => fetcher.Varied(yearCatalogs, "New", new Dictionary<string, string> { ["genre"] = year }, "new", NewPageWeights));

            var rated = catalogs.Where(c => c.Id == "imdbRating").ToList();
            jobs.Add(() => fetcher.Varied(rated, "Top rated", new Dictionary<string, string>(), "toprated", PageWeights));

            foreach (var genre in HomeVariety.HomeGenres(DeclaredGenres(catalogs).Where(g => !IsYear(g))))
            {
                var matching = catalogs.Where(c => c.GenreOptions.Contains(genre)).ToList();
                jobs.Add(() => fetcher.Varied(matching, genre, new Dictionary<string, string> { ["genre"] = genre }, genre, PageWeights));
            }
        }
        else
        {
            var baseCatalogs = catalogs.Where(c => c.IsBase).ToList();
            if (baseCatalogs.Count > 0)
            {
                var title = baseCatalogs.Select(c => c.Name).FirstOrDefault(n => n is not null) ?? source.Manifest.Name;
                jobs.Add(() => fetcher.Merged(baseCatalogs, title, new Dictionary<string, string>(), "base"));
            }
            foreach (var genre in DeclaredGenres(catalogs))
            {
                var matching = catalogs.Where(c => c.GenreOptions.Contains(genre)).ToList();
                jobs.Add(() => fetcher.Merged(matching, genre, new Dictionary<string, string> { ["genre"] = genre }, genre));
            }
        }

        for (var index = 0; index < jobs.Count; index += BatchSize)
        {
            cancel.ThrowIfCancellationRequested();
            var slice = jobs.Skip(index).Take(BatchSize).Select(job => job()).ToList();
            var ready = (await Task.WhenAll(slice).ConfigureAwait(false)).Where(s => s is not null).Select(s => s!).ToList();
            if (ready.Count > 0) await onBatch(ready).ConfigureAwait(false);
        }
    }

    private sealed class Fetcher(HomeSource source, string seed, AddonClient client, CancellationToken cancel)
    {
        private Task<List<MetaPreview>> Safe(CatalogDefinition catalog, IReadOnlyDictionary<string, string> extra) =>
            client.TryFetchCatalogAsync(source.ManifestUrl, catalog.Type, catalog.Id, extra, cancel);

        private string RowId(string rowKey) => $"{source.Manifest.Id}_{rowKey}";

        /// <summary>A row that reads today's page of its ranking (not always the first) and shuffles gently within it; falls back to page one if empty.</summary>
        public async Task<HomeSection?> Varied(List<CatalogDefinition> catalogs, string title, Dictionary<string, string> extra, string rowKey, double[] weights)
        {
            if (catalogs.Count == 0) return null;

            async Task<List<MetaPreview>> Read(int page)
            {
                var tasks = catalogs.Select(catalog =>
                {
                    var request = page > 0 && catalog.SupportsSkip ? new Dictionary<string, string>(extra) { ["skip"] = (page * PageSize).ToString() } : extra;
                    return Safe(catalog, request);
                });
                return DistinctById(Interleaved((await Task.WhenAll(tasks).ConfigureAwait(false)).ToList()));
            }

            var id = RowId(rowKey);
            var page = HomeVariety.PickPage(seed, id, weights);
            var items = await Read(page).ConfigureAwait(false);
            if (items.Count == 0 && page > 0) items = await Read(0).ConfigureAwait(false);
            return items.Count == 0 ? null : new HomeSection(id, title, HomeVariety.VaryOrder(items, seed, id));
        }

        /// <summary>Every matched catalogue in parallel, interleaved.</summary>
        public async Task<HomeSection?> Merged(List<CatalogDefinition> catalogs, string title, Dictionary<string, string> extra, string rowKey)
        {
            if (catalogs.Count == 0) return null;
            var lists = (await Task.WhenAll(catalogs.Select(c => Safe(c, extra))).ConfigureAwait(false)).ToList();
            var items = DistinctById(Interleaved(lists));
            return items.Count == 0 ? null : new HomeSection(RowId(rowKey), title, items);
        }
    }

    // --- ordering ---

    private static readonly string[] DefaultRowPriority =
    [
        "popular", "new", "top rated", "featured", "trending", "trending now", "new releases", "top 10 movies", "top 10 tv shows", "recently added",
        "action", "comedy", "horror", "romance", "thriller", "drama", "sci-fi", "science fiction", "fantasy", "mystery", "crime",
        "family", "kids", "children", "anime", "animation", "documentary", "music", "musical", "adventure",
    ];

    private static int DefaultRank(HomeSection section)
    {
        if (section.Id.EndsWith("_base", StringComparison.Ordinal)) return -1;
        var index = Array.IndexOf(DefaultRowPriority, section.Title.Trim().ToLowerInvariant());
        return index < 0 ? DefaultRowPriority.Length : index;
    }

    private static bool IsLeadRow(HomeSection s) => new[] { "_base", "_new", "_toprated" }.Any(suffix => s.Id.EndsWith(suffix, StringComparison.Ordinal));

    private static bool IsNewLeadRow(HomeSection s) => new[] { "_new", "_toprated" }.Any(suffix => s.Id.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>
    /// The person's explicit order first, then everything else by default rank (stable). One exception: New or Top rated when the person has not placed it
    /// goes at the top, right after any lead rows they did place.
    /// </summary>
    public static List<HomeSection> Ordered(IReadOnlyList<HomeSection> sections, HomeRowPreferences preferences)
    {
        var byId = new Dictionary<string, HomeSection>();
        foreach (var s in sections) byId.TryAdd(s.Id, s);
        var placed = preferences.OrderOrEmpty.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var explicitIds = preferences.OrderOrEmpty.ToHashSet();
        var remaining = sections
            .Select((section, index) => (section, index, rank: DefaultRank(section)))
            .Where(x => !explicitIds.Contains(x.section.Id))
            .OrderBy(x => x.rank).ThenBy(x => x.index)
            .Select(x => x.section)
            .ToList();
        var leadRows = remaining.Where(IsNewLeadRow).ToList();
        if (leadRows.Count == 0 || placed.Count == 0) return [.. placed, .. remaining];
        var at = 0;
        while (at < placed.Count && IsLeadRow(placed[at])) at++;
        var rest = remaining.Where(s => !IsNewLeadRow(s));
        return [.. placed.Take(at), .. leadRows, .. placed.Skip(at), .. rest];
    }

    /// <summary>A title appears in only one row: the first one (in the order shown) that holds it. A row left with nothing is dropped.</summary>
    public static List<HomeSection> Deduped(IEnumerable<HomeSection> sections)
    {
        var seen = new HashSet<string>();
        var output = new List<HomeSection>();
        foreach (var section in sections)
        {
            var items = section.Items.Where(i => seen.Add(i.Id)).ToList();
            if (items.Count > 0) output.Add(section with { Items = items });
        }
        return output;
    }
}

/// <summary>The titles the Home hero rotates through: prefer ones with a real landscape background.</summary>
public static class HeroPool
{
    public static bool HasWideBackdrop(MetaPreview item) => !string.IsNullOrEmpty(item.Background) && item.Background != item.Poster;

    public static List<MetaPreview> PickPool(IEnumerable<MetaPreview> pool, int size, Random random)
    {
        var all = pool.ToList();
        var wide = all.Where(HasWideBackdrop).OrderBy(_ => random.NextDouble()).ToList();
        if (wide.Count >= size) return wide.Take(size).ToList();
        var rest = all.Where(i => !HasWideBackdrop(i)).ToList();
        var withImage = rest.Where(i => i.Background is not null || i.Poster is not null).OrderBy(_ => random.NextDouble());
        var blank = rest.Where(i => i.Background is null && i.Poster is null).OrderBy(_ => random.NextDouble());
        return wide.Concat(withImage).Concat(blank).Take(size).ToList();
    }

    /// <summary><paramref name="size"/> random titles from the rows the person has enabled; only when those hold fewer is the rest made up from <paramref name="backup"/>.</summary>
    public static List<MetaPreview> PickTitles(IEnumerable<MetaPreview> enabled, IEnumerable<MetaPreview> backup, int size, Random random)
    {
        var own = PickPool(enabled, size, random);
        if (own.Count >= size) return own;
        var taken = own.Select(i => i.Id).ToHashSet();
        var extra = PickPool(backup.Where(i => !taken.Contains(i.Id)), size - own.Count, random);
        return own.Concat(extra).OrderBy(_ => random.NextDouble()).ToList();
    }
}
