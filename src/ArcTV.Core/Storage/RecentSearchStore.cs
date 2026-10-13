using System.Text.Json;

namespace ArcTV.Core.Storage;

/// <summary>The searches each person made, kept on this PC (not secret) in recent-searches.json, filed under their account id.</summary>
public sealed class RecentSearchStore(string path)
{
    public List<string> Load(string userId)
    {
        try
        {
            return ReadAll().TryGetValue(userId, out var list) ? list : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public void Save(string userId, IReadOnlyList<string> searches)
    {
        try
        {
            var all = ReadAll();
            all[userId] = searches.ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(all));
        }
        catch (Exception)
        {
            // recent searches are a convenience: never let a full disk or a locked file stop a search
        }
    }

    private Dictionary<string, List<string>> ReadAll() =>
        File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path)) ?? [] : [];
}
