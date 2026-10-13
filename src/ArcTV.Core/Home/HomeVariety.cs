using System.Globalization;

namespace ArcTV.Core.Home;

/// <summary>A small repeatable random stream (mulberry32): the same seed always gives the same numbers, as in the web and Mac apps.</summary>
public sealed class SeededRandom(int seed)
{
    private uint _a = unchecked((uint)seed);

    public double Next()
    {
        unchecked
        {
            _a += 0x6d2b79f5;
            var t = _a;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
}

/// <summary>
/// Keeping Home fresh without making it irrelevant: each row draws from a few pages deep into the same ranking, and the order inside it is a
/// gentle seeded shuffle favouring higher ranks. The seed is the account plus the day, so a row is steady while you use the app and different tomorrow.
/// </summary>
public static class HomeVariety
{
    public static uint HashSeed(string text)
    {
        unchecked
        {
            var hash = 0x811c9dc5u;
            foreach (var unit in text)
            {
                hash ^= unit;
                hash *= 0x01000193;
            }
            return hash;
        }
    }

    public static string DayStamp(DateTime? now = null) => (now ?? DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Which catalogue page (0 = the top 100) a row reads today; the first weight is the likeliest.</summary>
    public static int PickPage(string seed, string rowKey, IReadOnlyList<double> weights)
    {
        var random = new SeededRandom(unchecked((int)HashSeed($"{seed}|{rowKey}|page"))).Next();
        var total = weights.Sum();
        var acc = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            acc += weights[i] / total;
            if (random < acc) return i;
        }
        return weights.Count - 1;
    }

    /// <summary>A weighted random draw (Efraimidis-Spirakis keys): weight falls with rank as 1 / (1 + rank / 25).</summary>
    public static List<T> VaryOrder<T>(IReadOnlyList<T> items, string seed, string rowKey)
    {
        var random = new SeededRandom(unchecked((int)HashSeed($"{seed}|{rowKey}|order")));
        return items
            .Select((item, rank) => (item, key: Math.Pow(random.Next(), 1 + rank / 25.0)))
            .OrderByDescending(pair => pair.key)
            .Select(pair => pair.item)
            .ToList();
    }

    /// <summary>The genres Home shows as rows, in this order. Every other genre stays on the Movies and TV Shows pages.</summary>
    public static readonly string[] WideAppealGenres = ["Action", "Comedy", "Drama", "Thriller", "Horror", "Sci-Fi", "Crime", "Animation", "Documentary"];

    public static List<string> HomeGenres(IEnumerable<string> declared)
    {
        var byLower = new Dictionary<string, string>();
        foreach (var genre in declared) byLower.TryAdd(genre.ToLowerInvariant(), genre);
        return WideAppealGenres.Where(g => byLower.ContainsKey(g.ToLowerInvariant())).Select(g => byLower[g.ToLowerInvariant()]).ToList();
    }
}

/// <summary>The genres Cinemeta's catalogues can be filtered by (the Movies and TV Shows drop-downs).</summary>
public static class Genres
{
    public static readonly string[] Movies = ["Action", "Adventure", "Animation", "Biography", "Comedy", "Crime", "Documentary", "Drama", "Family", "Fantasy", "History", "Horror", "Mystery", "Romance", "Sci-Fi", "Sport", "Thriller", "War", "Western"];

    public static readonly string[] Shows = [.. Movies, "Reality-TV", "Talk-Show", "Game-Show"];
}
