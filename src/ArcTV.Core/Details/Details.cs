using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.MyList;

namespace ArcTV.Core.Details;

/// <summary>An episode of a show (<c>videos</c> in an addon's meta answer).</summary>
public sealed record EpisodeInfo(string Id, string? Title = null, string? Name = null, int? Season = null, int? Episode = null, string? Released = null, string? Thumbnail = null, string? Overview = null)
{
    public string DisplayTitle => Title ?? Name ?? $"Episode {Episode ?? 0}";
}

/// <summary>A person in the cast: a name, the character and a photo when the addon has them.</summary>
public sealed record CastMember(string Name, string? Role = null, string? PhotoUrl = null);

public sealed record CastExtra(string? Name = null, string? Character = null, string? Photo = null);

public sealed record AppExtras(List<CastExtra>? Cast = null);

/// <summary>What a title's details page shows (an addon's <c>/meta/&lt;type&gt;/&lt;id&gt;.json</c>).</summary>
public sealed record MetaDetail(
    string Id,
    string Type,
    string Name,
    string? Poster = null,
    string? Background = null,
    string? Logo = null,
    string? Description = null,
    string? ReleaseInfo = null,
    string? ImdbRating = null,
    List<string>? Genres = null,
    string? Runtime = null,
    List<string>? Director = null,
    List<string>? Cast = null,
    List<EpisodeInfo>? Videos = null,
    [property: JsonPropertyName("app_extras")] AppExtras? AppExtras = null)
{
    /// <summary>The release year from <c>ReleaseInfo</c>.</summary>
    [JsonIgnore]
    public int? Year => ReleaseInfo is { Length: >= 4 } info && int.TryParse(info.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : null;

    /// <summary>
    /// The cast with photos and characters when the addon sends them (Cinemeta's <c>app_extras.cast</c>); names the extended list lacks follow it, so nobody in
    /// <c>Cast</c> disappears.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<CastMember> CastMembers
    {
        get
        {
            var photographed = (AppExtras?.Cast ?? [])
                .Where(extra => !string.IsNullOrWhiteSpace(extra.Name))
                .Select(extra => new CastMember(Text.HtmlDecoded(extra.Name), string.IsNullOrEmpty(extra.Character) ? null : Text.HtmlDecoded(extra.Character), string.IsNullOrEmpty(extra.Photo) ? null : extra.Photo))
                .ToList();
            var known = photographed.Select(m => m.Name).ToHashSet();
            return [.. photographed, .. (Cast ?? []).Select(Text.HtmlDecoded).Where(name => name.Length > 0 && !known.Contains(name)).Select(name => new CastMember(name))];
        }
    }

    /// <summary>Episodes grouped by season, episodes in order. Empty for a movie.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<int, List<EpisodeInfo>> Seasons =>
        (Videos ?? []).Where(v => v.Season is not null && v.Episode is not null)
            .GroupBy(v => v.Season!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Episode ?? 0).ToList());

    /// <summary>The seasons in order. Season 0 ("Specials") is left out, as on the web app.</summary>
    [JsonIgnore]
    public IReadOnlyList<int> SeasonNumbers => Seasons.Keys.Where(n => n > 0).Order().ToList();

    /// <summary>The preview card fields this detail came from, for places that show a poster.</summary>
    public MetaPreview Preview() => new(Id, Type, Name, Poster, Background, Logo, Description, ReleaseInfo, ImdbRating, Genres, Runtime);

    /// <summary>This title as a My List entry.</summary>
    public WatchlistItem ToWatchlistItem(bool watched) => new(
        AppInfo.CinemetaProviderId, Id, Type == "movie" ? "MOVIE" : "TV_SHOW", Name, Poster, Background, Year,
        double.TryParse(ImdbRating, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) ? rating : null, watched, IsoDate.Now());
}

internal sealed record MetaResponse(MetaDetail? Meta);

public static class MetaPreviewDetail
{
    /// <summary>The details page's starting point while the full answer loads (and what it shows if the addon has nothing more).</summary>
    public static MetaDetail AsDetail(this MetaPreview item) =>
        new(item.Id, item.Type, item.Name, item.Poster, item.Background, item.Logo, item.Description, item.ReleaseInfo, item.ImdbRating, item.Genres, item.Runtime);
}

public static class StreamIds
{
    /// <summary>The id addons expect for a stream request: the title's id, plus ":season:episode" for an episode.</summary>
    public static string For(string titleId, int? season, int? episode) => season is { } s && episode is { } e ? $"{titleId}:{s}:{e}" : titleId;
}

/// <summary>Words for a title's facts.</summary>
public static partial class Format
{
    /// <summary>"2h 17m" ("49m" under an hour: the web app writes "0h 49m", which reads like a mistake).</summary>
    public static string Runtime(int minutes) => minutes < 60 ? $"{minutes}m" : $"{minutes / 60}h {minutes % 60}m";

    [GeneratedRegex(@"(\d+)\s*h(?:\D*?(\d+)\s*m)?", RegexOptions.IgnoreCase)]
    private static partial Regex HoursAndMinutes();

    /// <summary>"142 min" gives 142, "2h 22min" gives 142.</summary>
    public static int? RuntimeMinutes(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = HoursAndMinutes().Match(text);
        if (match.Success)
        {
            var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            return hours * 60 + minutes;
        }

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var total) ? total : null;
    }

    /// <summary>"yyyy-MM-dd" (or a longer timestamp starting with it) gives "Mar 5, 2024".</summary>
    public static string? ReleaseDate(string iso) =>
        iso.Length >= 10 && DateTime.TryParseExact(iso[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
            : null;
}
