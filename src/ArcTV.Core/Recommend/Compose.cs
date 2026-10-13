namespace ArcTV.Core.Recommend;

/// <summary>A small seeded generator (mulberry32): the same seed always gives the same sequence, so one launch is stable and the step is testable.</summary>
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

public interface IComposePick
{
    string Id { get; }

    double Score { get; }

    /// <summary>The genre the pick stands for in the profile (see <see cref="Compose.PrimaryGenre"/>); null when it matches none of the profile's genres.</summary>
    string? Genre { get; }
}

public static class Compose
{
    /// <summary>
    /// The genre a title "stands for" in this profile: of its own genres, the one the profile likes most overall (ties: alphabetical). Null when none of its genres has a positive weight.
    /// Used on both the profile's own titles and candidates, so the two are bucketed the same way.
    /// </summary>
    public static string? PrimaryGenre(IEnumerable<string> genres, TastePreferences prefs)
    {
        string? best = null;
        var bestWeight = 0.0;
        foreach (var genre in genres.Distinct().Order(StringComparer.Ordinal))
        {
            var weight = prefs.Genre.GetValueOrDefault(genre);
            if (weight > bestWeight)
            {
                best = genre;
                bestWeight = weight;
            }
        }

        return best;
    }

    /// <summary>How the profile's taste splits across genres: each of its own titles with a positive signal counts, by its signal weight, towards that title's primary genre. Shares sum to 1.</summary>
    public static Dictionary<string, double> TasteShares(IEnumerable<Interaction> signalled, IReadOnlyDictionary<string, Features> ownFeatures, TastePreferences prefs)
    {
        var raw = new Dictionary<string, double>();
        var total = 0.0;
        foreach (var interaction in signalled)
        {
            if (interaction.Weight <= 0) continue;
            if (PrimaryGenre(ownFeatures.TryGetValue(interaction.Id, out var f) ? f.Genres : [], prefs) is not { } genre) continue;
            raw[genre] = raw.GetValueOrDefault(genre) + interaction.Weight;
            total += interaction.Weight;
        }

        return total <= 0 ? [] : raw.ToDictionary(p => p.Key, p => p.Value / total);
    }

    /// <summary>
    /// Composition step, applied after scoring and before the diversity step; it only chooses and orders picks that were genuinely scored. The best <paramref name="anchors"/> always stay;
    /// every other place is given to a genre by the profile's own taste split (Sainte-Lague); within a genre the pick is drawn with a seeded weighted draw over that genre's other scored
    /// picks (score above 0 and at least <paramref name="floor"/> of that genre's best), and with no seed the genre's best is taken. If a genre runs out, its place goes to the next.
    /// </summary>
    public static List<T> Of<T>(IReadOnlyList<T> sortedByScore, int limit, IReadOnlyDictionary<string, double>? shares = null, int anchors = RecommendTuning.RotationAnchors,
        double floor = RecommendTuning.RotationFloor, double repeatWeight = RecommendTuning.RotationRepeatWeight, ISet<string>? previous = null, int? seed = null) where T : IComposePick
    {
        shares ??= new Dictionary<string, double>();
        previous ??= new HashSet<string>();
        if (sortedByScore.Count <= Math.Min(anchors, limit)) return sortedByScore.Take(limit).ToList();
        var random = seed is { } s ? new SeededRandom(s) : null;

        var row = sortedByScore.Take(Math.Min(anchors, limit)).ToList();
        var used = new HashSet<string>(row.Select(p => p.Id));
        var count = new Dictionary<string, int>();
        foreach (var p in row)
            if (p.Genre is { } g) count[g] = count.GetValueOrDefault(g) + 1;

        var buckets = new Dictionary<string, List<T>>();
        foreach (var p in sortedByScore)
        {
            if (p.Genre is not { } genre || used.Contains(p.Id) || p.Score <= 0) continue;
            if (!buckets.TryGetValue(genre, out var list)) buckets[genre] = list = [];
            list.Add(p);
        }

        // the floor is measured against each genre's best scored pick overall, not against whatever is left of it
        var genreBest = new Dictionary<string, double>();
        foreach (var p in sortedByScore)
            if (p.Genre is { } genre && p.Score > 0 && !genreBest.ContainsKey(genre)) genreBest[genre] = p.Score;

        T? DrawFrom(List<T> bucket)
        {
            if (bucket.Count == 0) return default;
            var best = genreBest.TryGetValue(bucket[0].Genre ?? "", out var b) ? b : bucket[0].Score;
            var eligible = bucket.Where(p => p.Score >= best * floor).ToList();
            if (eligible.Count == 0) return default;
            var winner = eligible[0];
            if (random is null) return winner;
            // weighted draw of one (Efraimidis-Spirakis key = u^(1/weight)): the largest wins
            var winnerKey = -1.0;
            foreach (var p in eligible)
            {
                var weight = Math.Max(1e-6, Math.Pow(p.Score / best, 2) * (previous.Contains(p.Id) ? repeatWeight : 1.0));
                var key = Math.Pow(random.Next(), 1.0 / weight);
                if (key > winnerKey)
                {
                    winner = p;
                    winnerKey = key;
                }
            }

            return winner;
        }

        while (row.Count < limit)
        {
            // genres in order of Sainte-Lague priority: a proportional split of the places that also mixes the genres through the row
            var wanted = buckets.Where(p => p.Value.Count > 0)
                .Select(p => (Genre: p.Key, Priority: shares.GetValueOrDefault(p.Key) / (2 * count.GetValueOrDefault(p.Key) + 1)))
                .OrderByDescending(x => x.Priority).ThenByDescending(x => shares.GetValueOrDefault(x.Genre)).ThenBy(x => x.Genre, StringComparer.Ordinal)
                .ToList();
            T? found = default;
            var any = false;
            foreach (var entry in wanted)
            {
                var drawn = DrawFrom(buckets[entry.Genre]);
                if (drawn is null) continue;
                found = drawn;
                any = true;
                break;
            }

            if (!any) break;
            row.Add(found!);
            used.Add(found!.Id);
            if (found.Genre is { } genre)
            {
                count[genre] = count.GetValueOrDefault(genre) + 1;
                buckets[genre] = buckets[genre].Where(p => p.Id != found.Id).ToList();
            }
        }

        // nothing left that clears the bar: the best remaining scores fill the row
        foreach (var p in sortedByScore)
        {
            if (row.Count >= limit) break;
            if (used.Add(p.Id)) row.Add(p);
        }

        return row;
    }
}
