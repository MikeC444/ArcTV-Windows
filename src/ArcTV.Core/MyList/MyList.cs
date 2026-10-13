using System.Globalization;
using System.Text.Json;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.MyList;

/// <summary>One saved title on the account's My List (GET /user/watchlist).</summary>
public sealed record WatchlistItem(
    string ProviderId,
    string ContentId,
    string ContentType,
    string Title,
    string? PosterUrl = null,
    string? BackdropUrl = null,
    int? Year = null,
    double? Rating = null,
    bool? Watched = null,
    string UpdatedAt = "")
{
    /// <summary>As a poster-card title: the backend's MOVIE / TV_SHOW become the addon protocol's movie / series. A watched title carries a tick.</summary>
    public MetaPreview AsPreview() => new(
        ContentId, ContentType == "MOVIE" ? "movie" : "series", Title, PosterUrl, BackdropUrl,
        ReleaseInfo: Year?.ToString(CultureInfo.InvariantCulture), ImdbRating: Rating?.ToString(CultureInfo.InvariantCulture)) { Watched = Watched == true };
}

internal sealed record WatchlistResponse(List<WatchlistItem>? Items);

public static class WatchlistApi
{
    /// <summary>The account's My List, oldest added first (the order the backend sends).</summary>
    public static async Task<List<WatchlistItem>> WatchlistAsync(this AuthClient client, string accessToken, CancellationToken cancel = default)
    {
        var body = await client.SendJsonAsync(HttpMethod.Get, "/user/watchlist", null, null, accessToken, cancel: cancel).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<WatchlistResponse>(body, ApiHttp.Json)?.Items ?? throw ApiException.Unreadable();
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    /// <summary>Adds a title to My List, or updates it (its watched flag).</summary>
    public static Task SaveToWatchlistAsync(this AuthClient client, string accessToken, WatchlistItem item, CancellationToken cancel = default) =>
        client.SendJsonAsync(HttpMethod.Post, "/user/watchlist", null, JsonSerializer.Serialize(item, ApiHttp.Json), accessToken, cancel: cancel);

    public static Task RemoveFromWatchlistAsync(this AuthClient client, string accessToken, WatchlistItem item, CancellationToken cancel = default) =>
        client.SendJsonAsync(HttpMethod.Delete, "/user/watchlist",
            new Dictionary<string, string> { ["providerId"] = item.ProviderId, ["contentId"] = item.ContentId, ["contentType"] = item.ContentType, ["updatedAt"] = IsoDate.Now() },
            null, accessToken, cancel: cancel);
}

/// <summary>How My List is ordered (the web app's sort drop-down).</summary>
public enum ListSort { Recent, Title, HighestRated, Newest }

public static class ListSorts
{
    public static IReadOnlyList<ListSort> All { get; } = [ListSort.Recent, ListSort.Title, ListSort.HighestRated, ListSort.Newest];

    public static string Label(this ListSort sort) => sort switch
    {
        ListSort.Title => "A–Z",
        ListSort.HighestRated => "Highest Rated",
        ListSort.Newest => "Newest",
        _ => "Recently Added",
    };

    /// <summary>
    /// <paramref name="items"/> come oldest-added first, as the account sends them. Every order starts from newest-added first, so titles that tie (same rating,
    /// same year) stay in recently-added order, as on the other apps.
    /// </summary>
    public static List<WatchlistItem> Apply(this ListSort sort, IEnumerable<WatchlistItem> items)
    {
        var recent = items.Reverse().ToList();
        return sort switch
        {
            ListSort.Title => recent.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
            ListSort.HighestRated => recent.OrderByDescending(i => i.Rating ?? -1).ToList(),
            ListSort.Newest => recent.OrderByDescending(i => i.Year ?? -1).ToList(),
            _ => recent,
        };
    }
}

/// <summary>What the My List page shows: nothing until the list has arrived, then the saved titles; or a reason it could not be loaded.</summary>
public sealed class MyListModel(Func<CancellationToken, Task<List<WatchlistItem>>> load, Func<WatchlistItem, Task>? save = null, Func<WatchlistItem, Task>? remove = null) : ObservableObject
{
    private IReadOnlyList<WatchlistItem>? _items;
    private string? _error;
    private int _generation;

    /// <summary>Null until the first load succeeds; kept while a later load is under way.</summary>
    public IReadOnlyList<WatchlistItem>? Items { get => _items; private set => Set(ref _items, value); }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public async Task LoadAsync(CancellationToken cancel = default)
    {
        var generation = ++_generation;
        Error = null;
        try
        {
            var found = await load(cancel);
            if (generation == _generation) Items = found;
        }
        catch (OperationCanceledException)
        {
            // left the page
        }
        catch (Exception ex)
        {
            if (generation == _generation) Error = ex is ApiException ? ex.Message : "Couldn't load your list. Check your connection.";
        }
    }

    public bool IsSaved(string id) => Items?.Any(i => i.ContentId == id) == true;

    public bool IsWatched(string id) => Items?.FirstOrDefault(i => i.ContentId == id)?.Watched == true;

    /// <summary>Adds the title to My List, or takes it off. Shown at once; put back if the account could not be reached.</summary>
    public async Task ToggleSavedAsync(Details.MetaDetail title)
    {
        var before = Items ?? [];
        var existing = before.FirstOrDefault(i => i.ContentId == title.Id);
        try
        {
            if (existing is not null)
            {
                Items = before.Where(i => i.ContentId != title.Id).ToList();
                if (remove is not null) await remove(existing);
            }
            else
            {
                var item = title.ToWatchlistItem(false);
                Items = [.. before, item];
                if (save is not null) await save(item);
            }
        }
        catch (Exception)
        {
            Items = before;
        }
    }

    /// <summary>Marks the title watched, or not watched again (adding it to My List if it was not there).</summary>
    public async Task ToggleWatchedAsync(Details.MetaDetail title)
    {
        var before = Items ?? [];
        var nowWatched = !IsWatched(title.Id);
        var item = (before.FirstOrDefault(i => i.ContentId == title.Id) ?? title.ToWatchlistItem(nowWatched)) with { Watched = nowWatched, UpdatedAt = IsoDate.Now() };
        Items = [.. before.Where(i => i.ContentId != title.Id), item];
        try
        {
            if (save is not null) await save(item);
        }
        catch (Exception)
        {
            Items = before;
        }
    }
}
