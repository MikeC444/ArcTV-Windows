using System.Globalization;
using System.Text;
using System.Text.Json;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;

namespace ArcTV.Core.Details;

/// <summary>What the account's cast lookup answers (<c>GET /user/cast</c>): TMDB's cast, with a photo address and the character where it has them.</summary>
public sealed record TmdbCast(string Name, string? Character = null, string? Photo = null);

public static class CastMerge
{
    /// <summary>A name as people match it: no accents, any letter case, punctuation as a space ("Zoë Saldaña" and "Zoe Saldana" are the same person).</summary>
    private static string Key(string name)
    {
        var folded = new string(name.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        return string.Join(" ", folded.Split(folded.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Fills in photos (and characters) the addon didn't send, matching people by name. Whatever the addon did send is kept as it is. If the addon sent no cast at all,
    /// TMDB's list is used.
    /// </summary>
    public static IReadOnlyList<CastMember> Merge(IReadOnlyList<CastMember> addon, IReadOnlyList<TmdbCast> tmdb)
    {
        if (tmdb.Count == 0) return addon;
        if (addon.Count == 0) return tmdb.Select(c => new CastMember(c.Name, c.Character, c.Photo)).ToList();

        var byName = new Dictionary<string, TmdbCast>();
        foreach (var entry in tmdb) byName.TryAdd(Key(entry.Name), entry);
        return addon.Select(member =>
        {
            if (member.PhotoUrl is not null && member.Role is not null) return member;
            return byName.TryGetValue(Key(member.Name), out var match) ? new CastMember(member.Name, member.Role ?? match.Character, member.PhotoUrl ?? match.Photo) : member;
        }).ToList();
    }
}

/// <summary>The things a title's page asks the account's server for, and that stay quiet when it has none.</summary>
public interface IDetailExtras
{
    /// <summary>The YouTube id of the title's trailer, or null.</summary>
    Task<string?> TrailerIdAsync(string title, string type, int? year, CancellationToken cancel = default);

    /// <summary>A movie's release date ("2024-03-05"), or null.</summary>
    Task<string?> ReleaseDateAsync(string title, int? year, CancellationToken cancel = default);

    /// <summary>The cast with photos from TMDB (through the account's server), for an IMDb id.</summary>
    Task<IReadOnlyList<TmdbCast>> CastPhotosAsync(string imdbId, string type, CancellationToken cancel = default);
}

public static class DetailExtrasApi
{
    private static string Kind(string type) => type == "movie" ? "MOVIE" : "TV_SHOW";

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static JsonDocument Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    /// <summary>The YouTube id of a title's trailer (<c>GET /user/trailer</c>), null when there is none.</summary>
    public static async Task<string?> TrailerIdAsync(this AuthClient client, string accessToken, string title, string type, int? year, CancellationToken cancel = default)
    {
        var query = new Dictionary<string, string> { ["title"] = title, ["type"] = Kind(type) };
        if (year is { } y) query["year"] = y.ToString(CultureInfo.InvariantCulture);
        using var doc = Parse(await client.SendJsonAsync(HttpMethod.Get, "/user/trailer", query, null, accessToken, cancel: cancel).ConfigureAwait(false));
        return Text(doc.RootElement, "youtubeVideoId");
    }

    /// <summary>A movie's release date (<c>GET /user/release-date</c>), null when the lookup has none.</summary>
    public static async Task<string?> ReleaseDateAsync(this AuthClient client, string accessToken, string title, int? year, CancellationToken cancel = default)
    {
        var query = new Dictionary<string, string> { ["title"] = title };
        if (year is { } y) query["year"] = y.ToString(CultureInfo.InvariantCulture);
        using var doc = Parse(await client.SendJsonAsync(HttpMethod.Get, "/user/release-date", query, null, accessToken, cancel: cancel).ConfigureAwait(false));
        return Text(doc.RootElement, "releaseDate");
    }

    /// <summary>The cast with photos (<c>GET /user/cast</c>), from the account's server (which holds the movie database key).</summary>
    public static async Task<IReadOnlyList<TmdbCast>> CastPhotosAsync(this AuthClient client, string accessToken, string imdbId, string type, CancellationToken cancel = default)
    {
        var query = new Dictionary<string, string> { ["imdbId"] = imdbId, ["type"] = Kind(type) };
        using var doc = Parse(await client.SendJsonAsync(HttpMethod.Get, "/user/cast", query, null, accessToken, cancel: cancel).ConfigureAwait(false));
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("cast", out var cast) || cast.ValueKind != JsonValueKind.Array) return [];
        return cast.EnumerateArray()
            .Select(entry => new TmdbCast(Text(entry, "name") ?? "", Text(entry, "character"), Text(entry, "photo")))
            .Where(entry => entry.Name.Length > 0)
            .ToList();
    }
}

/// <summary>The account's server as a source of trailers, release dates and cast photos. Trailers are remembered for the session.</summary>
public sealed class AccountDetailExtras(AccountModel account) : IDetailExtras
{
    private readonly Dictionary<string, string?> _trailers = [];

    public async Task<string?> TrailerIdAsync(string title, string type, int? year, CancellationToken cancel = default)
    {
        var key = $"{type}|{title}|{year ?? 0}";
        if (_trailers.TryGetValue(key, out var known)) return known;
        var found = await account.AuthorizedAsync((client, token) => client.TrailerIdAsync(token, title, type, year, cancel));
        _trailers[key] = found;
        return found;
    }

    public Task<string?> ReleaseDateAsync(string title, int? year, CancellationToken cancel = default) =>
        account.AuthorizedAsync((client, token) => client.ReleaseDateAsync(token, title, year, cancel));

    public Task<IReadOnlyList<TmdbCast>> CastPhotosAsync(string imdbId, string type, CancellationToken cancel = default) =>
        account.AuthorizedAsync((client, token) => client.CastPhotosAsync(token, imdbId, type, cancel));
}

public static class SimilarTitles
{
    /// <summary>Other titles from Home's rows, preferring ones that share a genre. Up to 15.</summary>
    public static IReadOnlyList<MetaPreview> For(MetaDetail title, IEnumerable<MetaPreview> pool)
    {
        var others = Home.HomeSections.DistinctById(pool).Where(i => i.Id != title.Id).ToList();
        var mine = (title.Genres ?? []).Select(g => g.ToLowerInvariant()).ToHashSet();
        var matches = mine.Count == 0 ? [] : others.Where(i => (i.Genres ?? []).Any(g => mine.Contains(g.ToLowerInvariant()))).ToList();
        return (matches.Count == 0 ? others : matches).Take(15).ToList();
    }
}
