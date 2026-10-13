using System.Globalization;
using System.Text.Json;
using ArcTV.Core.Account;

namespace ArcTV.Core.Plus;

/// <summary>One row of the watch history (<c>GET /user/history</c>): a movie or an episode, the furthest point reached and when it was last watched.</summary>
public sealed record HistoryItem(string ProviderId, string ContentId, string ContentType, string Title, long PositionMs, long DurationMs, bool Completed, string WatchedAt, int? SeasonNumber = null, int? EpisodeNumber = null)
{
    /// <summary>How much of this row counts as watched: all of it when finished, otherwise how far in it got (never more than its length).</summary>
    public long WatchedMs
    {
        get
        {
            var position = Math.Max(0, PositionMs);
            if (DurationMs > 0) return Completed ? DurationMs : Math.Min(position, DurationMs);
            return position;
        }
    }
}

internal sealed record HistoryPage(List<HistoryItem>? Items);

public sealed record HeatCell(long? Ms, int Level, string Label);

/// <summary>
/// "Your stats": what the account's watch history says about how it is used. The history holds one row per movie or episode, so a title watched twice counts once, on the day it was last
/// watched. A port of the web app's <c>domain/stats.ts</c>.
/// </summary>
public sealed class WatchStats
{
    public const int HeatDays = 91;

    public static readonly string[] Weekdays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public bool IsEmpty { get; private set; } = true;
    public long TotalMs { get; private set; }
    public long Last7DaysMs { get; private set; }
    public long Last30DaysMs { get; private set; }

    /// <summary>The 7 days before the last 7 (to say whether this week was busier or quieter).</summary>
    public long Prev7DaysMs { get; private set; }
    public long MovieMs { get; private set; }
    public long ShowMs { get; private set; }
    public int ActiveDays { get; private set; }
    public long AvgPerActiveDayMs { get; private set; }

    /// <summary>Time watched per local day for the last <see cref="HeatDays"/> days, oldest first (the last entry is today).</summary>
    public long[] DailyMs { get; } = new long[HeatDays];
    public int TitlesWatched { get; private set; }
    public int MoviesFinished { get; private set; }
    public int EpisodesWatched { get; private set; }
    public int ShowsWatched { get; private set; }

    /// <summary>Milliseconds watched per weekday, Monday first.</summary>
    public long[] ByWeekdayMs { get; } = new long[7];

    /// <summary>Index into <see cref="ByWeekdayMs"/> (Monday = 0) of the busiest day, or null with no history.</summary>
    public int? BusiestWeekday { get; private set; }
    public int StreakDays { get; private set; }
    public int LongestStreakDays { get; private set; }
    public DateTime? FirstWatchedAt { get; private set; }

    private static int WeekdayIndex(DateTime local) => ((int)local.DayOfWeek + 6) % 7;

    public static WatchStats Compute(IEnumerable<HistoryItem> items, DateTime nowUtc, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var stats = new WatchStats();
        var valid = items.Select(item => (item, at: IsoDate.Parse(item.WatchedAt))).Where(x => x.at is not null).Select(x => (x.item, at: x.at!.Value)).ToList();
        if (valid.Count == 0) return stats;
        stats.IsEmpty = false;
        DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        int DayNumber(DateTime utc) => DateOnly.FromDateTime(Local(utc)).DayNumber;
        var today = DayNumber(nowUtc);
        var days = new HashSet<int>();
        var titles = new HashSet<string>();
        var shows = new HashSet<string>();
        var first = DateTime.MaxValue;
        foreach (var (item, at) in valid)
        {
            var ms = item.WatchedMs;
            var age = nowUtc - at;
            stats.TotalMs += ms;
            if (age <= TimeSpan.FromDays(7)) stats.Last7DaysMs += ms;
            if (age <= TimeSpan.FromDays(30)) stats.Last30DaysMs += ms;
            if (age > TimeSpan.FromDays(7) && age <= TimeSpan.FromDays(14)) stats.Prev7DaysMs += ms;
            if (item.ContentType == "MOVIE") stats.MovieMs += ms; else stats.ShowMs += ms;
            var ago = today - DayNumber(at);
            if (ago >= 0 && ago < HeatDays) stats.DailyMs[HeatDays - 1 - ago] += ms;
            if (at < first) first = at;
            stats.ByWeekdayMs[WeekdayIndex(Local(at))] += ms;
            if (ms > 0) days.Add(DayNumber(at));
            var key = $"{item.ProviderId}|{item.ContentId}|{item.ContentType}";
            titles.Add(key);
            if (item.ContentType == "MOVIE")
            {
                if (item.Completed) stats.MoviesFinished++;
            }
            else
            {
                shows.Add(key);
                if (item.Completed) stats.EpisodesWatched++;
            }
        }

        stats.TitlesWatched = titles.Count;
        stats.ShowsWatched = shows.Count;
        stats.ActiveDays = days.Count;
        stats.AvgPerActiveDayMs = days.Count == 0 ? 0 : stats.TotalMs / days.Count;
        var busiest = stats.ByWeekdayMs.Max();
        stats.BusiestWeekday = busiest > 0 ? Array.IndexOf(stats.ByWeekdayMs, busiest) : null;

        var ordered = days.OrderBy(d => d).ToList();
        var run = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            run = i > 0 && ordered[i] == ordered[i - 1] + 1 ? run + 1 : 1;
            stats.LongestStreakDays = Math.Max(stats.LongestStreakDays, run);
        }

        var cursor = days.Contains(today) ? today : today - 1;
        while (days.Contains(cursor))
        {
            stats.StreakDays++;
            cursor--;
        }

        stats.FirstWatchedAt = first;
        return stats;
    }

    /// <summary>"126 hours" / "1 hour 5 minutes" / "40 minutes" / "under a minute", for the big numbers.</summary>
    public static string Duration(long ms)
    {
        var totalMinutes = (int)(Math.Max(0, ms) / 60_000);
        if (totalMinutes < 1) return ms > 0 ? "under a minute" : "0 minutes";
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        var h = $"{hours} {(hours == 1 ? "hour" : "hours")}";
        var m = $"{minutes} {(minutes == 1 ? "minute" : "minutes")}";
        if (hours == 0) return m;
        // past a day's worth the minutes are noise
        return hours >= 24 || minutes == 0 ? h : $"{h} {m}";
    }

    /// <summary>"45m" / "3h" / "12.5h" for a bar label; empty for nothing.</summary>
    public static string Short(long ms)
    {
        if (ms <= 0) return "";
        var minutes = (int)Math.Round(ms / 60_000.0, MidpointRounding.AwayFromZero);
        if (minutes < 60) return $"{Math.Max(1, minutes)}m";
        var hours = ms / 3_600_000.0;
        if (hours < 10)
        {
            var tenth = Math.Round(hours * 10, MidpointRounding.AwayFromZero) / 10;
            return tenth == Math.Round(tenth) ? $"{(int)tenth}h" : $"{tenth.ToString(CultureInfo.InvariantCulture)}h";
        }

        return $"{(int)Math.Round(hours, MidpointRounding.AwayFromZero)}h";
    }

    /// <summary>This week against the one before: null when there is nothing to compare.</summary>
    public static (string Direction, int Percent)? WeekChange(long last7, long prev7)
    {
        if (prev7 <= 0) return null;
        var percent = (int)Math.Round((double)(last7 - prev7) / prev7 * 100, MidpointRounding.AwayFromZero);
        return (percent > 0 ? "up" : percent < 0 ? "down" : "same", Math.Abs(percent));
    }

    /// <summary>The activity grid's cells, column by column (Monday first), padded to a Monday: a null amount for the blank cells that start the first week.</summary>
    public static List<HeatCell> HeatCells(IReadOnlyList<long> dailyMs, DateTime nowLocal)
    {
        var max = dailyMs.Count == 0 ? 0 : dailyMs.Max();
        var start = nowLocal.Date.AddDays(-(dailyMs.Count - 1));
        var cells = Enumerable.Range(0, WeekdayIndex(start)).Select(_ => new HeatCell(null, 0, "")).ToList();
        for (var i = 0; i < dailyMs.Count; i++)
        {
            var ms = dailyMs[i];
            var level = ms <= 0 || max <= 0 ? 0 : Math.Min(4, Math.Max(1, (int)Math.Ceiling(ms / (double)max * 4)));
            cells.Add(new HeatCell(ms, level, start.AddDays(i).ToString("ddd d MMM", CultureInfo.CurrentCulture)));
        }

        return cells;
    }
}

public static class HistoryApi
{
    /// <summary>The profile's whole watch history, newest first, read a page at a time (a very long history is read up to 5,000 rows).</summary>
    public static async Task<(List<HistoryItem> Items, bool Truncated)> HistoryAsync(this AuthClient client, string accessToken, CancellationToken cancel = default)
    {
        const int pageSize = 200;
        var items = new List<HistoryItem>();
        string? before = null;
        for (var page = 0; page < 25; page++)
        {
            var query = new Dictionary<string, string> { ["limit"] = pageSize.ToString(CultureInfo.InvariantCulture) };
            if (before is not null) query["before"] = before;
            var body = await client.SendJsonAsync(HttpMethod.Get, "/user/history", query, null, accessToken, cancel: cancel).ConfigureAwait(false);
            List<HistoryItem> rows;
            try
            {
                rows = JsonSerializer.Deserialize<HistoryPage>(body, ApiHttp.Json)?.Items ?? throw ApiException.Unreadable();
            }
            catch (JsonException)
            {
                throw ApiException.Unreadable();
            }

            items.AddRange(rows.Where(r => r.ContentType is "MOVIE" or "TV_SHOW"));
            if (rows.Count < pageSize) return (items, false);
            before = rows[^1].WatchedAt;
        }

        return (items, true);
    }
}
