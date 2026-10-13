using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Details;
using ArcTV.Core.Mvvm;
using ArcTV.Core.Plus;
using ArcTV.Core.Sources;

namespace ArcTV.Core.Continue;

/// <summary>A title the account can pick up again (<c>GET /user/continue-watching</c>).</summary>
public sealed record ContinueItem(
    string ProviderId, string ContentId, string ContentType, string Title, long PositionMs, long DurationMs, string LastWatchedAt,
    int? SeasonNumber = null, int? EpisodeNumber = null, string? EpisodeTitle = null, string? PosterUrl = null, string? BackdropUrl = null)
{
    /// <summary>A film or episode this far through is finished for all practical purposes: Continue Watching does not start it again from the end.</summary>
    public const double FinishedAt = 0.92;

    public bool IsMovie => ContentType == "MOVIE";

    /// <summary>0 to 1: how far through it is.</summary>
    public double Progress => DurationMs > 0 ? Math.Clamp((double)PositionMs / DurationMs, 0, 1) : 0;

    public double PositionSeconds => PositionMs / 1000.0;

    public bool NearlyDone => Progress >= FinishedAt;

    /// <summary>"S1 E3" for an episode, null for a movie.</summary>
    public string? EpisodeLabel => SeasonNumber is { } s && EpisodeNumber is { } e ? $"S{s} E{e}" : null;

    public MetaPreview AsPreview() => new(ContentId, IsMovie ? "movie" : "series", Title, PosterUrl, BackdropUrl);

    /// <summary>What the sources page and the player need to start it: from where it was left.</summary>
    public PlayTarget ToTarget() => new(new MetaDetail(ContentId, IsMovie ? "movie" : "series", Title, PosterUrl, BackdropUrl), SeasonNumber, EpisodeNumber,
        EpisodeHeading, NearlyDone ? 0 : PositionSeconds);

    /// <summary>"S1 E3 Name" for an episode. What was reported can already be that whole line (the player reports it so), so the label is only added in front of a bare name.</summary>
    private string? EpisodeHeading
    {
        get
        {
            if (EpisodeLabel is not { } label) return null;
            // what the account holds can already start with the number, even twice or more (an earlier version put it in front of a line that already had it, and that was sent back
            // and kept): every number at the front is dropped and the one true label is put on
            var name = string.IsNullOrWhiteSpace(EpisodeTitle) ? "" : LeadingNumbers.Replace(EpisodeTitle, "").Trim();
            return name.Length == 0 ? label : $"{label} {name}";
        }
    }

    private static readonly Regex LeadingNumbers = new(@"^(\s*S\d+\s*E\d+\b\s*)+", RegexOptions.IgnoreCase);
}

internal sealed record ContinueResponse(List<ContinueItem>? Items);

/// <summary>A playback position report (<c>POST /user/watch-progress</c>): feeds Continue Watching and the watch history.</summary>
internal sealed record ProgressReport(
    string ProviderId, string ContentId, string ContentType, string Title, long PositionMs, long DurationMs, bool Completed, string WatchedAt,
    int? SeasonNumber = null, int? EpisodeNumber = null, string? EpisodeTitle = null, string? PosterUrl = null, string? BackdropUrl = null);

public static class ContinueApi
{
    private static readonly JsonSerializerOptions SendOptions = new(ApiHttp.Json) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static List<ContinueItem> ParseItems(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ContinueResponse>(body, ApiHttp.Json)?.Items ?? throw ApiException.Unreadable();
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    /// <summary>The JSON for a position report. A key with nothing to say (a movie's season) is left out.</summary>
    public static string ProgressBody(MetaDetail title, int? season, int? episode, string? episodeTitle, double positionSeconds, double durationSeconds, bool completed, string watchedAt) =>
        JsonSerializer.Serialize(new ProgressReport(AppInfo.CinemetaProviderId, title.Id, title.Type == "movie" ? "MOVIE" : "TV_SHOW", title.Name,
            (long)(Math.Max(0, positionSeconds) * 1000), (long)(Math.Max(0, durationSeconds) * 1000), completed, watchedAt, season, episode, episodeTitle, title.Poster, title.Background), SendOptions);

    public static async Task<List<ContinueItem>> ContinueWatchingAsync(this AuthClient client, string accessToken, CancellationToken cancel = default) =>
        ParseItems(await client.SendJsonAsync(HttpMethod.Get, "/user/continue-watching", null, null, accessToken, cancel: cancel).ConfigureAwait(false));

    public static Task ReportProgressAsync(this AuthClient client, string accessToken, string body, CancellationToken cancel = default) =>
        client.SendJsonAsync(HttpMethod.Post, "/user/watch-progress", null, body, accessToken, cancel: cancel);

    /// <summary>Takes a title out of Continue Watching (without marking it watched).</summary>
    public static Task RemoveContinueWatchingAsync(this AuthClient client, string accessToken, ContinueItem item, CancellationToken cancel = default) =>
        client.SendJsonAsync(HttpMethod.Delete, "/user/continue-watching",
            new Dictionary<string, string> { ["providerId"] = item.ProviderId, ["contentId"] = item.ContentId, ["contentType"] = item.ContentType, ["updatedAt"] = IsoDate.Now() },
            null, accessToken, cancel: cancel);
}

/// <summary>Where Continue Watching is kept (the account). The tests use a stand-in.</summary>
public interface IContinueStore
{
    Task<IReadOnlyList<ContinueItem>> ListAsync(CancellationToken cancel = default);

    Task ReportAsync(string body, CancellationToken cancel = default);

    Task RemoveAsync(ContinueItem item, CancellationToken cancel = default);

    /// <summary>The profile's watch history (one row per movie or episode), for the episode ticks.</summary>
    Task<IReadOnlyList<HistoryItem>> HistoryAsync(CancellationToken cancel = default);
}

public sealed class AccountContinueStore(AccountModel account) : IContinueStore
{
    public async Task<IReadOnlyList<ContinueItem>> ListAsync(CancellationToken cancel = default) => await account.AuthorizedAsync((c, t) => c.ContinueWatchingAsync(t, cancel)).ConfigureAwait(false);

    public Task ReportAsync(string body, CancellationToken cancel = default) =>
        account.AuthorizedAsync(async (c, t) =>
        {
            await c.ReportProgressAsync(t, body, cancel).ConfigureAwait(false);
            return true;
        });

    public async Task<IReadOnlyList<HistoryItem>> HistoryAsync(CancellationToken cancel = default) => (await account.AuthorizedAsync((c, t) => c.HistoryAsync(t, cancel)).ConfigureAwait(false)).Items;

    public Task RemoveAsync(ContinueItem item, CancellationToken cancel = default) =>
        account.AuthorizedAsync(async (c, t) =>
        {
            await c.RemoveContinueWatchingAsync(t, item, cancel).ConfigureAwait(false);
            return true;
        });
}

/// <summary>
/// Continue Watching: what the account says can be picked up again, where playback reports go, and where a title was left so it starts from there. A movie played to the end is
/// marked watched (through <paramref name="movieFinished"/>).
/// </summary>
public sealed class ContinueModel(IContinueStore store, Func<MetaDetail, Task>? movieFinished = null, Func<DateTime>? now = null) : ObservableObject
{
    private IReadOnlyList<ContinueItem> _items = [];
    private IReadOnlyDictionary<string, string> _logos = new Dictionary<string, string>();
    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);

    public IReadOnlyList<ContinueItem> Items { get => _items; private set => Set(ref _items, value); }

    /// <summary>The title logo of each item (by title id), looked up after the list is read: the account keeps the poster and backdrop of a title but not its logo.</summary>
    public IReadOnlyDictionary<string, string> Logos { get => _logos; private set => Set(ref _logos, value); }

    /// <summary>How to find an item's logo (the shell gives it a Cinemeta lookup). Null means logos are not looked up.</summary>
    public Func<ContinueItem, CancellationToken, Task<string?>>? LogoLookup { get; set; }

    public async Task LoadAsync(CancellationToken cancel = default)
    {
        try
        {
            Items = await store.ListAsync(cancel).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }

        await LoadLogosAsync(cancel).ConfigureAwait(true);
    }

    /// <summary>Finds a logo for every item that has none yet (four lookups at a time) and announces them together. An item whose lookup fails just shows without one.</summary>
    public async Task LoadLogosAsync(CancellationToken cancel = default)
    {
        if (LogoLookup is not { } lookup) return;
        var need = Items.Where(i => !Logos.ContainsKey(i.ContentId)).ToList();
        if (need.Count == 0) return;
        var found = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(need.Select(async item =>
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                if (await lookup(item, cancel).ConfigureAwait(false) is { Length: > 0 } url) found[item.ContentId] = url;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // no logo for this one
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(true);
        if (found.IsEmpty) return;
        var all = Logos.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (id, url) in found) all[id] = url;
        Logos = all;
    }

    /// <summary>
    /// Tells the account where playback is (called now and then while playing, and on leaving the player). Too little to say (a length under 30 seconds, or under 5 seconds in) is
    /// not reported. At 92 percent it is a finished report.
    /// </summary>
    public async Task ReportAsync(PlayTarget target, double position, double duration, CancellationToken cancel = default)
    {
        if (duration <= 30 || position <= 5) return;
        var completed = position / duration >= ContinueItem.FinishedAt;
        try
        {
            await store.ReportAsync(ContinueApi.ProgressBody(target.Title, target.Season, target.Episode, target.EpisodeTitle, position, duration, completed, IsoDate.From(_now())), cancel).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }

        if (completed && target.Title.Type == "movie" && movieFinished is not null)
        {
            try
            {
                await movieFinished(target.Title).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // the tick on My List can wait
            }
        }

        await LoadAsync(cancel).ConfigureAwait(true);
    }

    /// <summary>Takes a title off the row. Shown at once; put back if the account could not be reached.</summary>
    public async Task RemoveAsync(ContinueItem item, CancellationToken cancel = default)
    {
        var before = Items;
        Items = before.Where(i => i.ContentId != item.ContentId).ToList();
        try
        {
            await store.RemoveAsync(item, cancel).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Items = before;
        }
    }

    /// <summary>Where this movie or episode was left, if Continue Watching has it (a show keeps one place: its latest episode).</summary>
    public ContinueItem? ResumeItem(string titleId, int? season = null, int? episode = null)
    {
        var item = Items.FirstOrDefault(i => i.ContentId == titleId);
        if (item is null) return null;
        if (season is null && episode is null) return item;
        return item.SeasonNumber == season && item.EpisodeNumber == episode ? item : null;
    }

    /// <summary>The same target, starting from where it was left (when it is in Continue Watching and not nearly finished). A target that already has a place is left alone.</summary>
    public PlayTarget Resume(PlayTarget target)
    {
        if (target.StartAt > 0) return target;
        var item = ResumeItem(target.Title.Id, target.Season, target.Episode);
        return item is { NearlyDone: false, PositionSeconds: >= 10 } ? target with { StartAt = item.PositionSeconds } : target;
    }

    /// <summary>Which episodes of a show are finished or part-way, from the watch history; null when the history cannot be read.</summary>
    public async Task<IReadOnlyDictionary<string, EpisodeStatus>?> EpisodeStatusesAsync(string contentId, CancellationToken cancel = default)
    {
        try
        {
            return EpisodeProgress.Statuses(await store.HistoryAsync(cancel).ConfigureAwait(true), contentId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Marks episodes watched or not watched from the episode list. Watched is a finished report for each episode (it lands in the watch history as completed); not watched writes it back
    /// to the start. The account keeps one Continue Watching pointer per show and a finished report clears it, so the episode the person is actually partway through (when it is not one of
    /// these) is put back afterwards; un-marking with nothing to put back clears the pointer the report just made. The steps run one after another, each stamped later than the last.
    /// Returns false when any step failed (the caller then reads the real state again).
    /// </summary>
    public async Task<bool> SetEpisodesWatchedAsync(IReadOnlyList<EpisodeInfo> episodes, bool watched, MetaDetail title, CancellationToken cancel = default)
    {
        var duration = (Format.RuntimeMinutes(title.Runtime) ?? EpisodeProgress.FallbackRuntimeMinutes) * 60.0;
        var keys = episodes.Select(e => EpisodeProgress.Key(e.Season, e.Episode)).OfType<string>().ToHashSet();
        var restore = Items.FirstOrDefault(i => i.ContentId == title.Id && i.SeasonNumber is not null && i.EpisodeNumber is not null && !keys.Contains(EpisodeProgress.Key(i.SeasonNumber, i.EpisodeNumber)!));
        var stamp = _now();
        string At() => IsoDate.From(stamp = stamp.AddMilliseconds(5));
        try
        {
            foreach (var episode in episodes)
            {
                var heading = $"S{episode.Season ?? 0} E{episode.Episode ?? 0} {episode.DisplayTitle}";
                await store.ReportAsync(ContinueApi.ProgressBody(title, episode.Season, episode.Episode, heading, watched ? duration : 0, duration, watched, At()), cancel).ConfigureAwait(true);
            }

            if (restore is not null)
            {
                await store.ReportAsync(ContinueApi.ProgressBody(title, restore.SeasonNumber, restore.EpisodeNumber, restore.EpisodeTitle, restore.PositionSeconds, Math.Max(restore.DurationMs / 1000.0, 1), false, At()), cancel).ConfigureAwait(true);
            }
            else if (!watched && (await store.ListAsync(cancel).ConfigureAwait(true)).FirstOrDefault(i => i.ContentId == title.Id) is { } pointer)
            {
                await store.RemoveAsync(pointer, cancel).ConfigureAwait(true);
            }

            await LoadAsync(cancel).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await LoadAsync(cancel).ConfigureAwait(true);
            return false;
        }
    }
}
