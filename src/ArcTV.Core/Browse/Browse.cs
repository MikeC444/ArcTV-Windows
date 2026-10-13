using System.Globalization;
using ArcTV.Core.Addons;
using ArcTV.Core.Home;

namespace ArcTV.Core.Browse;

public static class MetaPreviewYear
{
    /// <summary>The release year from <c>ReleaseInfo</c> ("2026", "2019-2024"), if it starts with one.</summary>
    public static int? Year(this MetaPreview item)
    {
        var info = item.ReleaseInfo;
        return info is { Length: >= 4 } && int.TryParse(info.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : null;
    }
}

/// <summary>How Movies and TV Shows are ordered (the web app's three pills).</summary>
public enum BrowseSort { Featured, HighestRated, Newest }

public static class BrowseSorts
{
    public static IReadOnlyList<BrowseSort> All { get; } = [BrowseSort.Featured, BrowseSort.HighestRated, BrowseSort.Newest];

    public static string Label(this BrowseSort sort) => sort switch
    {
        BrowseSort.HighestRated => "Highest Rated",
        BrowseSort.Newest => "Newest",
        _ => "Featured",
    };

    /// <summary>Featured keeps the order given; the others sort high to low and keep ties (and titles without a rating or year, last) in the order given.</summary>
    public static List<MetaPreview> Apply(this BrowseSort sort, IEnumerable<MetaPreview> items) => sort switch
    {
        BrowseSort.HighestRated => items.OrderByDescending(Rating).ToList(),
        BrowseSort.Newest => items.OrderByDescending(item => item.Year() ?? -1).ToList(),
        _ => items.ToList(),
    };

    private static double Rating(MetaPreview item) =>
        double.TryParse(item.ImdbRating, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) ? rating : -1;
}

/// <summary>The genres Cinemeta's catalogues can be filtered by (the web app's drop-down).</summary>
public static class Genres
{
    /// <summary>The Movies drop-down: exactly the genres Cinemeta's catalogue lists, in its order.</summary>
    public static readonly IReadOnlyList<string> Movies =
    [
        "Action", "Adventure", "Animation", "Biography", "Comedy", "Crime", "Documentary", "Drama", "Family", "Fantasy",
        "History", "Horror", "Mystery", "Romance", "Sci-Fi", "Sport", "Thriller", "War", "Western",
    ];

    /// <summary>The TV Shows drop-down: the same, and three more.</summary>
    public static readonly IReadOnlyList<string> Shows = [.. Movies, "Reality-TV", "Talk-Show", "Game-Show"];

    public static readonly IReadOnlyList<string> All =
    [
        "Action", "Adventure", "Animation", "Biography", "Comedy", "Crime", "Documentary", "Drama", "Family", "Fantasy",
        "History", "Horror", "Music", "Mystery", "Romance", "Sci-Fi", "Sport", "Thriller", "War", "Western",
    ];
}

/// <summary>Movies and TV Shows pages: pages of a type from every addon, optionally for one genre. A port of the web app's getSectionsByType / getMoreItemsByType.</summary>
public static class BrowsePaging
{
    /// <summary>One page (100 titles per catalogue; page 0 is the first). Catalogues that need a genre are used only when a genre is chosen.</summary>
    public static async Task<List<MetaPreview>> BrowsePageAsync(this HomeSource source, string type, string? genre, int page, AddonClient? client = null, CancellationToken cancel = default)
    {
        var wanted = source.Catalogs
            .Where(c => c.Type == type)
            .Where(c => genre is null
                ? c.IsBase
                : HomeSections.IsYear(genre) ? c.GenreOptions.Any(HomeSections.IsYear) : c.GenreOptions.Contains(genre))
            .ToList();
        if (wanted.Count == 0) return [];

        var extra = new Dictionary<string, string>();
        if (genre is not null) extra["genre"] = genre;
        if (page > 0) extra["skip"] = (page * HomeSections.PageSize).ToString(CultureInfo.InvariantCulture);

        var addons = client ?? new AddonClient();
        var lists = await Task.WhenAll(wanted.Select(catalog => addons.TryFetchCatalogAsync(source.ManifestUrl, catalog.Type, catalog.Id, extra, cancel))).ConfigureAwait(false);
        return HomeSections.DistinctById(HomeSections.Interleaved(lists));
    }
}
