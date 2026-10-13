namespace ArcTV.Core.Recommend;

public sealed record ScoreResult(double Score, IReadOnlyDictionary<TasteCategory, double> Categories);

/// <summary>One of the profile's titles and how much of a candidate's score it accounts for.</summary>
public sealed class Source(string id, string title, SignalKind kind)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public SignalKind Kind { get; } = kind;

    /// <summary>Weighted sum over the candidate's genres, directors and cast of what this title added to each feature.</summary>
    public double Total { get; set; }

    /// <summary>The part of <see cref="Total"/> that came through directors.</summary>
    public double Director { get; set; }
}

public interface IScoredPick
{
    string Id { get; }

    double Score { get; }

    /// <summary>Titles of the profile's that contributed to this pick, best first (see <see cref="Scoring.RankSources"/>).</summary>
    IReadOnlyList<Source> Sources { get; }
}

public sealed record DiversifiedPick<T>(T Pick, Source? Source);

public static class Scoring
{
    /// <summary>Cosine similarity of two sparse vectors; negative preferences stay negative. Null when either vector has no magnitude.</summary>
    public static double? Cosine(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b)
    {
        double dot = 0, na = 0, nb = 0;
        foreach (var key in a.Keys.Order(StringComparer.Ordinal))
        {
            var value = a[key];
            na += value * value;
            if (b.TryGetValue(key, out var other)) dot += value * other;
        }

        foreach (var key in b.Keys.Order(StringComparer.Ordinal)) nb += b[key] * b[key];
        if (na == 0 || nb == 0) return null;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>
    /// Weighted mean of the category similarities. A category with no candidate metadata, or where the profile has no nonzero preference, is omitted and the remaining weights
    /// renormalised; if nothing can be compared the candidate is unscored (null).
    /// </summary>
    public static ScoreResult? ScoreCandidate(Features? features, TastePreferences prefs)
    {
        if (features is null) return null;
        var categories = new Dictionary<TasteCategory, double>();
        double weighted = 0, total = 0;
        foreach (var category in TasteCategories.All)
        {
            var list = features.List(category);
            if (list.Count == 0 || !prefs.Has(category)) continue;
            var vector = new Dictionary<string, double>();
            foreach (var name in list) vector.TryAdd(name, 1.0);
            if (Cosine(vector, prefs.Vector(category)) is not { } similarity) continue;
            categories[category] = similarity;
            weighted += category.Weight() * similarity;
            total += category.Weight();
        }

        return total == 0 ? null : new ScoreResult(weighted / total, categories);
    }

    private static int Strength(SignalKind kind) => kind switch { SignalKind.Like => 3, SignalKind.Completed => 2, SignalKind.Watchlist => 1, _ => 0 };

    private static string Verb(SignalKind kind) => kind switch { SignalKind.Like => "liked", SignalKind.Completed => "watched", SignalKind.Watchlist => "saved", _ => "" };

    /// <summary>
    /// Every title the profile has a positive signal for, credited with the part of this candidate's score it contributed, biggest first; ties go to the stronger signal (like,
    /// finished, saved), then id. Disliked titles are never sources.
    /// </summary>
    public static List<Source> RankSources(Features? features, TastePreferences prefs)
    {
        if (features is null) return [];
        var credit = new Dictionary<string, Source>();
        foreach (var category in TasteCategories.All)
        {
            foreach (var feature in features.List(category))
            {
                if (!prefs.Contributions.TryGetValue(TastePreferences.KeyOf(category, feature), out var contributions)) continue;
                foreach (var c in contributions)
                {
                    if (c.Amount <= 0 || c.Kind == SignalKind.Dislike) continue;
                    if (!credit.TryGetValue(c.Id, out var entry)) credit[c.Id] = entry = new Source(c.Id, c.Title, c.Kind);
                    var part = category.Weight() * c.Amount;
                    entry.Total += part;
                    if (category == TasteCategory.Director) entry.Director += part;
                }
            }
        }

        return credit.Values.OrderByDescending(s => s.Total).ThenByDescending(s => Strength(s.Kind)).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The reason line for a source: "Because you liked X", or "More from directors you enjoy" when it mostly matches through a shared director.</summary>
    public static string ReasonFor(Source source) => source.Director > source.Total / 2 ? "More from directors you enjoy" : $"Because you {Verb(source.Kind)} {source.Title}";

    /// <summary>
    /// Diversity step, separate from scoring and applied after it. Walks the picks best score first and gives each the strongest of its contributing titles that is not yet used up (one
    /// title can explain at most <paramref name="maxPerSource"/> picks); a pick whose contributors are all used up waits and only fills spare places at the end.
    /// </summary>
    public static List<DiversifiedPick<T>> Diversify<T>(IReadOnlyList<T> sortedByScore, int limit = RecommendTuning.MaxResults, int maxPerSource = RecommendTuning.MaxPicksPerSource) where T : IScoredPick
    {
        var used = new Dictionary<string, int>();
        var chosen = new List<DiversifiedPick<T>>();
        var waiting = new List<T>();
        foreach (var pick in sortedByScore)
        {
            if (chosen.Count >= limit) break;
            if (pick.Sources.Count == 0)
            {
                chosen.Add(new DiversifiedPick<T>(pick, null));
                continue;
            }

            var source = pick.Sources.FirstOrDefault(s => used.GetValueOrDefault(s.Id) < maxPerSource);
            if (source is null)
            {
                waiting.Add(pick);
                continue;
            }

            used[source.Id] = used.GetValueOrDefault(source.Id) + 1;
            chosen.Add(new DiversifiedPick<T>(pick, source));
        }

        foreach (var pick in waiting)
        {
            if (chosen.Count >= limit) break;
            chosen.Add(new DiversifiedPick<T>(pick, pick.Sources.FirstOrDefault()));
        }

        return chosen;
    }

    /// <summary>The reason for the single best source, or null when nothing positive matched (no reason is ever invented).</summary>
    public static string? Explain(Features? features, TastePreferences prefs) => RankSources(features, prefs).FirstOrDefault() is { } source ? ReasonFor(source) : null;
}
