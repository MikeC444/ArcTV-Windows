using System.Globalization;
using System.Text.RegularExpressions;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;

namespace ArcTV.Core.Sources;

public sealed record StreamHints(string? Filename = null, long? VideoSize = null);

/// <summary>A stream an addon offered (<c>/stream/&lt;type&gt;/&lt;id&gt;.json</c>).</summary>
public sealed record AddonStream(
    string? Url = null,
    string? YtId = null,
    string? InfoHash = null,
    int? FileIdx = null,
    string? Title = null,
    string? Name = null,
    string? Description = null,
    StreamHints? BehaviorHints = null,
    List<string>? Sources = null);

internal sealed record StreamResponse(List<AddonStream>? Streams);

public enum Quality { Unknown = 0, Sd, P720, P1080, P2160 }

public static class Qualities
{
    public static string Label(this Quality quality) => quality switch
    {
        Quality.Sd => "SD",
        Quality.P720 => "720p",
        Quality.P1080 => "1080p",
        Quality.P2160 => "4K",
        _ => "Auto",
    };

    /// <summary>Read from the words addons put in a stream's name and title ("2160p", "4K", "1080p", ...).</summary>
    public static Quality Detect(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("2160") || lower.Contains("4k") || lower.Contains("uhd")) return Quality.P2160;
        if (lower.Contains("1080")) return Quality.P1080;
        if (lower.Contains("720")) return Quality.P720;
        if (lower.Contains("480") || lower.Contains("360") || lower.Contains("dvdrip")) return Quality.Sd;
        return Quality.Unknown;
    }
}

/// <summary>One row in the source list: a stream plus the addon it came from and what can be read off it.</summary>
public sealed partial record SourceItem(string AddonName, AddonStream Stream, int Index)
{
    public string Id => $"{AddonName}#{Index}";

    private string SearchText => string.Join(" ", new[] { Stream.Name, Stream.Title, Stream.Description, Stream.BehaviorHints?.Filename }.Where(t => t is not null));

    public Quality Quality => Qualities.Detect(SearchText);

    /// <summary>The address to play, when the addon gave a direct web link (a debrid service does). Torrent-only streams have none.</summary>
    public Uri? PlayUrl => Stream.Url is { } text && text.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;

    public bool IsPlayable => PlayUrl is not null;

    /// <summary>A torrent (no web link, but a torrent hash): a torrent engine can stream it.</summary>
    public bool IsTorrent => PlayUrl is null && !string.IsNullOrEmpty(Stream.InfoHash);

    /// <summary>Something that can be started: a web link, or a torrent.</summary>
    public bool CanStream => IsPlayable || IsTorrent;

    /// <summary>The first line addons use for the release name (their title, else the file name).</summary>
    public string Headline =>
        (Stream.Title ?? Stream.Description ?? Stream.BehaviorHints?.Filename ?? Stream.Name ?? "Stream")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Stream";

    /// <summary>The rest of the lines (size, seeds, language) joined on one line.</summary>
    public string Details => string.Join("  \u00B7  ", (Stream.Title ?? Stream.Description ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1));

    [GeneratedRegex(@"\d+(\.\d+)?\s?(GB|MB)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeInText();

    [GeneratedRegex("\U0001F464\\s*(\\d+)")]
    private static partial Regex SeedersInText();

    [GeneratedRegex(@"\bdv\b", RegexOptions.IgnoreCase)]
    private static partial Regex DolbyVisionWord();

    public string? SizeText
    {
        get
        {
            if (Stream.BehaviorHints?.VideoSize is > 0 and var bytes) return (bytes / 1_073_741_824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            var match = SizeInText().Match(SearchText);
            return match.Success ? match.Value : null;
        }
    }

    /// <summary>Seeders, from the "👤 12" line torrent addons write.</summary>
    public int? Seeders
    {
        get
        {
            var match = SeedersInText().Match(SearchText);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }
    }

    /// <summary>REMUX, BluRay, WEB-DL, WEBRip, HDTV or DVDRip when the name says so.</summary>
    public string? SourceTag
    {
        get
        {
            var lower = SearchText.ToLowerInvariant();
            (string, string)[] pairs = [("remux", "REMUX"), ("blu-ray", "BluRay"), ("bluray", "BluRay"), ("bdrip", "BluRay"), ("web-dl", "WEB-DL"), ("webdl", "WEB-DL"), ("webrip", "WEBRip"), ("hdtv", "HDTV"), ("dvdrip", "DVDRip")];
            return pairs.Where(p => lower.Contains(p.Item1)).Select(p => p.Item2).FirstOrDefault();
        }
    }

    /// <summary>The video format and any HDR: "HEVC  \u00B7  HDR10+  \u00B7  Dolby Vision".</summary>
    public string? CodecText
    {
        get
        {
            var lower = SearchText.ToLowerInvariant();
            var parts = new List<string>();
            if (lower.Contains("av1")) parts.Add("AV1");
            else if (lower.Contains("hevc") || lower.Contains("x265") || lower.Contains("h265") || lower.Contains("h.265")) parts.Add("HEVC");
            else if (lower.Contains("x264") || lower.Contains("h264") || lower.Contains("h.264") || lower.Contains("avc")) parts.Add("H.264");
            if (lower.Contains("hdr10+") || lower.Contains("hdr10plus")) parts.Add("HDR10+");
            else if (lower.Contains("hdr")) parts.Add("HDR");
            if (lower.Contains("dolby vision") || DolbyVisionWord().IsMatch(lower)) parts.Add("Dolby Vision");
            return parts.Count == 0 ? null : string.Join("  \u00B7  ", parts);
        }
    }

    /// <summary>The sound: Atmos, TrueHD, DTS, DD+ and so on, when the name says so.</summary>
    public string? AudioTag
    {
        get
        {
            var lower = SearchText.ToLowerInvariant();
            (string, string)[] pairs = [("atmos", "Atmos"), ("truehd", "TrueHD"), ("dts-hd", "DTS-HD"), ("dts", "DTS"), ("ddp", "DD+"), ("eac3", "DD+"), ("dd+", "DD+"), ("ac3", "Dolby Digital"), ("aac", "AAC")];
            return pairs.Where(p => lower.Contains(p.Item1)).Select(p => p.Item2).FirstOrDefault();
        }
    }

    /// <summary>File size in bytes, from the addon's hint or the "21.3 GB" in the name.</summary>
    public long? SizeBytes
    {
        get
        {
            if (Stream.BehaviorHints?.VideoSize is > 0 and var bytes) return bytes;
            var match = SizeInText().Match(SearchText);
            if (!match.Success) return null;
            var number = double.Parse(new string(match.Value.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray()), CultureInfo.InvariantCulture);
            return (long)(number * (match.Value.Contains("gb", StringComparison.OrdinalIgnoreCase) ? 1_073_741_824 : 1_048_576));
        }
    }
}

/// <summary>An addon that was asked for streams and could not answer, with why (never the address, which can hold an account key).</summary>
public sealed record AddonFailure(string AddonName, string Reason);

/// <summary>Everything one look for sources found: the streams, which addons failed, and which cannot give streams for this title at all.</summary>
public sealed record SourceResult(IReadOnlyList<SourceItem> Items, IReadOnlyList<AddonFailure> Failures, IReadOnlyList<string> Skipped)
{
    public SourceResult() : this([], [], [])
    {
    }
}

public static class SourceFinder
{
    /// <summary>Why an addon did not answer, as the end of a sentence about it ("&lt;addon&gt; took too long to answer").</summary>
    public static string DescribeError(Exception error) => error switch
    {
        ApiException { Kind: ApiException.ErrorKind.Server, Status: 429 } => "is limiting requests right now, try again in a moment",
        ApiException { Kind: ApiException.ErrorKind.Server, Status: >= 500 and var status } => $"had a problem answering (HTTP {status})",
        ApiException { Kind: ApiException.ErrorKind.Server, Status: var status } => $"refused the request (HTTP {status})",
        ApiException => "sent an answer that could not be read",
        OperationCanceledException => "took too long to answer",
        _ => "couldn't be reached",
    };

    /// <summary>Every enabled addon that can stream <paramref name="id"/> is asked at once; the playable streams come first, best quality first.</summary>
    public static async Task<SourceResult> FindAsync(IReadOnlyList<SyncedAddon> addons, string type, string id, AddonClient? client = null, CancellationToken cancel = default)
    {
        var addonClient = client ?? new AddonClient();
        var askable = addons.Where(a => a.ManifestJson?.ProvidesStreams(type, id) ?? false).ToList();
        var skipped = addons.Where(a => !(a.ManifestJson?.ProvidesStreams(type, id) ?? false)).Select(a => a.Name).ToList();

        var answers = await Task.WhenAll(askable.Select(async addon =>
        {
            try
            {
                var streams = await addonClient.FetchStreamsAsync(addon.ManifestUrl, type, id, cancel).ConfigureAwait(false);
                return (Items: streams.Select((stream, index) => new SourceItem(addon.Name, stream, index)).ToList(), Failure: (AddonFailure?)null);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancel.IsCancellationRequested))
            {
                return (Items: new List<SourceItem>(), Failure: new AddonFailure(addon.Name, DescribeError(ex)));
            }
        })).ConfigureAwait(false);

        // direct links first, then the best quality, then the addon's own order
        var sorted = answers.SelectMany(a => a.Items).OrderByDescending(i => i.IsPlayable).ThenByDescending(i => i.Quality).ToList();
        return new SourceResult(sorted, answers.Select(a => a.Failure).OfType<AddonFailure>().ToList(), skipped);
    }
}

/// <summary>The quality pills on the source page.</summary>
public enum SourceFilter { All, Uhd, Fhd, Hd, Other }

public enum SourceSort { Quality, Seeders, Size }

public static class SourceListing
{
    public static string Label(this SourceFilter filter) => filter switch
    {
        SourceFilter.Uhd => "4K",
        SourceFilter.Fhd => "1080p",
        SourceFilter.Hd => "720p",
        SourceFilter.Other => "Other",
        _ => "All Sources",
    };

    public static string Label(this SourceSort sort) => sort switch
    {
        SourceSort.Seeders => "Seeders",
        SourceSort.Size => "Size",
        _ => "Quality",
    };

    public static SourceSort Next(this SourceSort sort) => (SourceSort)(((int)sort + 1) % 3);

    public static IReadOnlyList<SourceFilter> Filters { get; } = [SourceFilter.All, SourceFilter.Uhd, SourceFilter.Fhd, SourceFilter.Hd, SourceFilter.Other];

    private static bool Matches(SourceFilter filter, Quality quality) => filter switch
    {
        SourceFilter.Uhd => quality == Quality.P2160,
        SourceFilter.Fhd => quality == Quality.P1080,
        SourceFilter.Hd => quality == Quality.P720,
        SourceFilter.Other => quality < Quality.P720,
        _ => true,
    };

    /// <summary>The best source that plays straight away (the list is already best first), marked "Recommended".</summary>
    public static string? RecommendedId(IEnumerable<SourceItem> items) => items.FirstOrDefault(i => i.IsPlayable)?.Id;

    /// <summary>
    /// The sources after filtering and sorting. Quality keeps the list's own order (direct links first, best quality first); the recommended source always stays on top.
    /// </summary>
    public static List<SourceItem> Arrange(IReadOnlyList<SourceItem> items, SourceFilter filter, SourceSort sort)
    {
        var recommended = RecommendedId(items);
        var kept = items.Where(i => Matches(filter, i.Quality) || i.Id == recommended).ToList();
        var sorted = sort switch
        {
            SourceSort.Seeders => kept.OrderByDescending(i => i.Seeders ?? -1).ToList(),
            SourceSort.Size => kept.OrderByDescending(i => i.SizeBytes ?? -1).ToList(),
            _ => kept,
        };
        var top = sorted.FirstOrDefault(i => i.Id == recommended);
        return recommended is null || top is null ? sorted : [top, .. sorted.Where(i => i.Id != recommended)];
    }
}

/// <summary>What to play: a movie, or one episode of a show.</summary>
public sealed record PlayTarget(Details.MetaDetail Title, int? Season = null, int? Episode = null, string? EpisodeTitle = null, double StartAt = 0, bool ChooseSource = false, bool AutoPick = false)
{
    public string StreamId => Details.StreamIds.For(Title.Id, Season, Episode);

    public string Heading => EpisodeTitle is { } episode ? $"{Title.Name} \u00B7 {episode}" : Title.Name;
}

/// <summary>A source that was picked: what to play and what to call it.</summary>
public sealed record PlayingItem(Uri Url, string Heading, string SourceName, PlayTarget Target, string? Filename = null);
