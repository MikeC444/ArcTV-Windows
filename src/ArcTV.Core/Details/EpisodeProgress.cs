using ArcTV.Core.Plus;

namespace ArcTV.Core.Details;

/// <summary>What the account's history says about one episode: finished, or how far in it got (0 to 1).</summary>
public sealed record EpisodeStatus(bool Watched, double Fraction);

/// <summary>An episode as the title page lists it: the episode and what is known about watching it.</summary>
public sealed record EpisodeEntry(EpisodeInfo Info, bool Watched, double Fraction)
{
    public bool InProgress => !Watched && Fraction > 0;
}

public static class EpisodeProgress
{
    /// <summary>How long an episode is taken to be when it is marked watched and nothing says (the history wants a length).</summary>
    public const int FallbackRuntimeMinutes = 45;

    /// <summary>The key an episode is tracked under: "season:episode".</summary>
    public static string? Key(int? season, int? episode) => season is { } s && episode is { } e ? $"{s}:{e}" : null;

    /// <summary>
    /// The finished and part-way episodes of one show, from the watch history (one row per episode). A finished episode is watched; otherwise the fraction is how far in it got,
    /// and an episode that was never started has no entry.
    /// </summary>
    public static Dictionary<string, EpisodeStatus> Statuses(IEnumerable<HistoryItem> history, string contentId)
    {
        var result = new Dictionary<string, EpisodeStatus>();
        foreach (var row in history)
        {
            if (row.ContentId != contentId || row.ContentType != "TV_SHOW" || Key(row.SeasonNumber, row.EpisodeNumber) is not { } key) continue;
            if (row.Completed) result[key] = new EpisodeStatus(true, 1);
            else if (row.DurationMs > 0 && row.PositionMs > 0) result[key] = new EpisodeStatus(false, Math.Clamp((double)row.PositionMs / row.DurationMs, 0, 1));
        }

        return result;
    }

    /// <summary>
    /// The episode after the furthest one watched (a season caught up moves on to the next season); null when nothing is watched or the last episode is the furthest, so the caller
    /// falls back to the first. Season 0 (specials) is left out, as on the title page.
    /// </summary>
    public static EpisodeInfo? NextEpisode(IReadOnlyDictionary<int, List<EpisodeInfo>> seasons, IReadOnlyDictionary<string, EpisodeStatus> statuses)
    {
        var ordered = seasons.Keys.Where(n => n > 0).Order().SelectMany(n => seasons[n]).ToList();
        var last = ordered.FindLastIndex(e => Key(e.Season, e.Episode) is { } key && statuses.TryGetValue(key, out var status) && status.Watched);
        return last >= 0 && last + 1 < ordered.Count ? ordered[last + 1] : null;
    }

    /// <summary>The page's list for one season: each episode with its status.</summary>
    public static List<EpisodeEntry> Entries(IEnumerable<EpisodeInfo> episodes, IReadOnlyDictionary<string, EpisodeStatus> statuses) =>
        episodes.Select(e => Key(e.Season, e.Episode) is { } key && statuses.TryGetValue(key, out var s) ? new EpisodeEntry(e, s.Watched, s.Fraction) : new EpisodeEntry(e, false, 0)).ToList();
}

public static class EpisodeNavigation
{
    /// <summary>The episode after this one: the next in the same season, else the first of the next season. Season 0 (specials) is skipped.</summary>
    public static EpisodeInfo? EpisodeAfter(this MetaDetail title, int season, int episode)
    {
        var numbers = title.SeasonNumbers;
        var index = numbers.ToList().IndexOf(season);
        if (index < 0) return null;
        var seasons = title.Seasons;
        if (seasons.TryGetValue(season, out var inSeason) && inSeason.FirstOrDefault(e => (e.Episode ?? 0) > episode) is { } next) return next;
        foreach (var later in numbers.Skip(index + 1))
            if (seasons.TryGetValue(later, out var list) && list.Count > 0) return list[0];
        return null;
    }
}
