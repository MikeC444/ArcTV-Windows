using System.Text.Json;

namespace ArcTV.Core.Sources;

/// <summary>
/// What a title (or episode) was last watched on, kept on this PC only (not part of the synced account data), so "Play" and "Resume" can carry on with the same
/// release instead of going back to the source list. It keeps what the source was (addon, release name, torrent hash) because an addon can hand out new links
/// between two requests.
/// </summary>
public sealed record LastSource(string AddonName, string ReleaseTitle, string? InfoHash = null)
{
    public static LastSource From(SourceItem item) => new(item.AddonName, item.Headline, item.Stream.InfoHash);

    /// <summary>
    /// Finds the source in a fresh list: the same torrent (info hash), else the same release from the same addon, else the same release name. Only a source that
    /// can play counts. Nothing is guessed beyond that.
    /// </summary>
    public SourceItem? Match(IReadOnlyList<SourceItem> items)
    {
        var playable = items.Where(i => i.CanStream).ToList();
        if (!string.IsNullOrEmpty(InfoHash) && playable.FirstOrDefault(i => string.Equals(i.Stream.InfoHash, InfoHash, StringComparison.OrdinalIgnoreCase)) is { } sameTorrent) return sameTorrent;
        return playable.FirstOrDefault(i => i.Headline == ReleaseTitle && i.AddonName == AddonName) ?? playable.FirstOrDefault(i => i.Headline == ReleaseTitle);
    }
}

/// <summary>The last source of each title, per account, in last-sources.json next to the other app data.</summary>
public sealed class LastSourceStore(string path, string userId)
{
    public static string Key(string titleId, int? season, int? episode) => $"{titleId}|{season ?? -1}|{episode ?? -1}";

    private Dictionary<string, Dictionary<string, LastSource>> ReadAll()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, LastSource>>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public LastSource? Get(string titleId, int? season, int? episode) =>
        ReadAll().TryGetValue(userId, out var mine) && mine.TryGetValue(Key(titleId, season, episode), out var found) ? found : null;

    public void Set(LastSource source, string titleId, int? season, int? episode)
    {
        try
        {
            var all = ReadAll();
            if (!all.TryGetValue(userId, out var mine)) all[userId] = mine = [];
            mine[Key(titleId, season, episode)] = source;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(all));
        }
        catch (Exception)
        {
            // remembering the last source is a convenience: never let it stop anything
        }
    }
}
