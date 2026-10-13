using System.Text.Json;

namespace ArcTV.Core.Home;

/// <summary>The last Home rows, kept in a file so the next start has something to show at once while the fresh rows load. One file per account profile.</summary>
public sealed class HomeCache(string directory, Func<DateTime>? now = null)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    public const int ItemsPerRow = 30;

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);

    private sealed record Payload(DateTime At, List<HomeSection> Sections);

    private string FileFor(string scope) => Path.Combine(directory, $"home-{new string(scope.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray())}.json");

    /// <summary>The saved rows, or null when there are none, they are over a week old, or the file cannot be read.</summary>
    public IReadOnlyList<HomeSection>? Load(string scope)
    {
        try
        {
            var path = FileFor(scope);
            if (!File.Exists(path)) return null;
            var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(path));
            if (payload is null || payload.Sections is not { Count: > 0 } sections || _now() - payload.At >= MaxAge) return null;
            return sections;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(IEnumerable<HomeSection> sections, string scope)
    {
        try
        {
            var trimmed = sections.Select(s => s with { Items = s.Items.Take(ItemsPerRow).ToList() }).ToList();
            if (trimmed.Count == 0) return;
            Directory.CreateDirectory(directory);
            var path = FileFor(scope);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Payload(_now(), trimmed)));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception)
        {
            // a saved copy is a convenience: never let it stop Home
        }
    }
}
