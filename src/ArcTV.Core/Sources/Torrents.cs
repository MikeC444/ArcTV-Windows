using System.Text;
using System.Text.RegularExpressions;

namespace ArcTV.Core.Sources;

/// <summary>A file inside a torrent.</summary>
public sealed record TorrentFile(int Index, string Name, long Size);

public static class TorrentLink
{
    /// <summary>Trackers added to the ones the addon names (the torrent is also looked for through the distributed hash table).</summary>
    public static readonly string[] FallbackTrackers =
    [
        "udp://tracker.opentrackr.org:1337/announce", "udp://open.tracker.cl:1337/announce", "udp://tracker.torrent.eu.org:451/announce", "udp://exodus.desync.com:6969/announce",
    ];

    /// <summary>The magnet link for a torrent stream: its hash, a name, and the trackers the addon listed. Null when the stream has no hash.</summary>
    public static string? Magnet(AddonStream stream)
    {
        var hash = stream.InfoHash?.Trim();
        if (string.IsNullOrEmpty(hash)) return null;
        var text = new StringBuilder("magnet:?xt=urn:btih:").Append(hash);
        var name = stream.BehaviorHints?.Filename ?? stream.Title?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrEmpty(name)) text.Append("&dn=").Append(Uri.EscapeDataString(name));
        var seen = new HashSet<string>();
        foreach (var entry in stream.Sources ?? [])
        {
            if (!entry.StartsWith("tracker:", StringComparison.OrdinalIgnoreCase)) continue;
            var tracker = entry["tracker:".Length..];
            if (tracker.Length > 0 && seen.Add(tracker)) text.Append("&tr=").Append(Uri.EscapeDataString(tracker));
        }

        // a few well known trackers alongside the addon's own: more places to ask for people, and a torrent whose own trackers are dead can still be found
        foreach (var tracker in FallbackTrackers)
            if (seen.Add(tracker)) text.Append("&tr=").Append(Uri.EscapeDataString(tracker));
        return text.ToString();
    }
}

/// <summary>Which file of a torrent to play.</summary>
public static partial class TorrentFileChoice
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { "mkv", "mp4", "m4v", "mov", "avi", "webm", "ts", "m2ts", "wmv", "flv", "mpg", "mpeg" };

    public static bool IsVideo(string name) => Path.GetExtension(name).TrimStart('.') is { Length: > 0 } extension && VideoExtensions.Contains(extension);

    /// <summary>The addon's own pick when it is a video; else, for an episode, the video that names it (S01E02 or 1x02); else the biggest video. Null when there is no video.</summary>
    public static int? Pick(IReadOnlyList<TorrentFile> files, int? hint, int? season, int? episode)
    {
        var videos = files.Where(f => IsVideo(f.Name)).ToList();
        if (hint is { } wanted && files.FirstOrDefault(f => f.Index == wanted) is { } hinted && IsVideo(hinted.Name)) return hinted.Index;
        if (season is { } s && episode is { } e)
        {
            string[] patterns = [$"s{s:00}e{e:00}", $"s{s}e{e}", $"{s}x{e:00}"];
            var named = videos.Where(f => patterns.Any(p => f.Name.Contains(p, StringComparison.OrdinalIgnoreCase))).OrderByDescending(f => f.Size).FirstOrDefault();
            if (named is not null) return named.Index;
        }

        return videos.OrderByDescending(f => f.Size).FirstOrDefault()?.Index;
    }
}
