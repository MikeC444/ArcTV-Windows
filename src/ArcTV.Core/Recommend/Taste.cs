using ArcTV.Core.Addons;
using ArcTV.Core.Details;
using ArcTV.Core.MyList;
using ArcTV.Core.Plus;

namespace ArcTV.Core.Recommend;

public enum SignalKind { Like, Dislike, Completed, Watchlist }

public enum TasteCategory { Genre, Director, Cast }

public static class TasteCategories
{
    public static readonly TasteCategory[] All = [TasteCategory.Genre, TasteCategory.Director, TasteCategory.Cast];

    public static string Key(this TasteCategory category) => category switch { TasteCategory.Genre => "genre", TasteCategory.Director => "director", _ => "cast" };

    public static double Weight(this TasteCategory category) => category switch
    {
        TasteCategory.Genre => RecommendTuning.CategoryWeightGenre,
        TasteCategory.Director => RecommendTuning.CategoryWeightDirector,
        _ => RecommendTuning.CategoryWeightCast,
    };
}

/// <summary>The numbers of "Picked for you" that <see cref="RecommendConfig"/> (in Plus) does not already name. They are the Fire TV app's values (which come from the web app).</summary>
public static class RecommendTuning
{
    /// <summary>How the three similarities combine. A category with no usable data is left out and the rest are renormalised.</summary>
    public const double CategoryWeightGenre = 0.6;
    public const double CategoryWeightDirector = 0.25;
    public const double CategoryWeightCast = 0.15;

    public const int MaxResults = 20;

    /// <summary>Composition: the best picks always stay; the other places follow the profile's genre split, drawn from each genre's other scored candidates that reach the floor of that genre's best. A pick shown last time counts this times as much.</summary>
    public const int RotationAnchors = 5;
    public const double RotationFloor = 0.6;
    public const double RotationRepeatWeight = 0.15;

    /// <summary>Shortlist: every genre the profile likes gets at least this many candidates, out of a budget shared by taste share.</summary>
    public const int ShortlistGenreBudget = 36;
    public const int ShortlistGenreMin = 4;

    /// <summary>Diversity: one title of the profile's can be the stated reason for at most this many picks.</summary>
    public const int MaxPicksPerSource = 3;

    /// <summary>Shortlist: this many by overall genre match, the rest round-robin from the best matches of each of the profile's own titles.</summary>
    public const int ShortlistByScore = 20;

    public const int ShortlistSourceTitles = 12;

    /// <summary>Billing-order cast beyond this adds noise, not taste.</summary>
    public const int CastFeatureLimit = 12;

    public const int CandidateDetailFetchLimit = 60;
    public const int DetailFetchConcurrency = 4;
    public const int FeatureCacheMaxEntries = 800;
    public static readonly TimeSpan FeatureCacheTtl = TimeSpan.FromDays(30);

    public const string PickedRowTitle = "Picked for you · ArcTV Plus";
    public const string PopularRowTitle = "Popular movies, not personalised yet · ArcTV Plus";
}

/// <summary>What the addon tells us about a title, normalised: lower-cased, trimmed, unique. Any list may be empty (metadata is often partial).</summary>
public sealed record Features(IReadOnlyList<string> Genres, IReadOnlyList<string> Directors, IReadOnlyList<string> Cast)
{
    public static Features Empty { get; } = new([], [], []);

    public IReadOnlyList<string> List(TasteCategory category) => category switch { TasteCategory.Genre => Genres, TasteCategory.Director => Directors, _ => Cast };

    public bool HasAny => Genres.Count > 0 || Directors.Count > 0 || Cast.Count > 0;

    public static string NormaliseName(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static List<string> Unique(IEnumerable<string> names)
    {
        var seen = new HashSet<string>();
        var output = new List<string>();
        foreach (var name in names)
        {
            var normal = NormaliseName(name);
            if (normal.Length > 0 && seen.Add(normal)) output.Add(normal);
        }

        return output;
    }

    /// <summary>Features from raw lists (the shape an addon's meta uses), with a name list cut to a sensible length and a director entry like "A, B" split in two.</summary>
    public static Features FromNames(IEnumerable<string> genres, IEnumerable<string> directors, IEnumerable<string> cast) =>
        new(Unique(genres), Unique(directors.SelectMany(d => d.Split(',', '/', '&'))), Unique(cast).Take(RecommendTuning.CastFeatureLimit).ToList());

    public static Features FromMeta(MetaDetail meta) => FromNames(meta.Genres ?? [], meta.Director ?? [], meta.Cast ?? []);

    public static List<string> NormaliseGenres(IEnumerable<string> genres)
    {
        var seen = new HashSet<string>();
        return genres.Select(g => g.Trim().ToLowerInvariant()).Where(g => g.Length > 0 && seen.Add(g)).ToList();
    }
}

/// <summary>What is currently stored about one title for one profile. Built from the stores each time, never accumulated: edits and removals just work.</summary>
public sealed record InteractionInput(string Id, string Title, Rating? Rating = null, bool Completed = false, bool InWatchlist = false);

public sealed record Interaction(string Id, string Title, SignalKind Kind, double Weight);

public static class Interactions
{
    /// <summary>
    /// The single strongest applicable signal for a title, or null when it carries none. Explicit feedback comes first, so a disliked title stays negative even if it was finished or
    /// saved. Opening a page, starting playback and failures are not inputs at all.
    /// </summary>
    public static Interaction? Of(InteractionInput input)
    {
        if (input.Rating == Rating.Like) return new Interaction(input.Id, input.Title, SignalKind.Like, RecommendConfig.WeightLike);
        if (input.Rating == Rating.Dislike) return new Interaction(input.Id, input.Title, SignalKind.Dislike, RecommendConfig.WeightDislike);
        if (input.Completed) return new Interaction(input.Id, input.Title, SignalKind.Completed, RecommendConfig.WeightCompleted);
        if (input.InWatchlist) return new Interaction(input.Id, input.Title, SignalKind.Watchlist, RecommendConfig.WeightWatchlist);
        return null;
    }

    /// <summary>Stored facts to one interaction per title, in a stable order (strongest first, then id).</summary>
    public static List<Interaction> Collect(IEnumerable<InteractionInput> inputs)
    {
        var byId = new Dictionary<string, InteractionInput>();
        foreach (var input in inputs)
        {
            byId[input.Id] = byId.TryGetValue(input.Id, out var merged)
                ? merged with { Title = input.Title, Rating = input.Rating ?? merged.Rating, Completed = input.Completed || merged.Completed, InWatchlist = input.InWatchlist || merged.InWatchlist }
                : input;
        }

        return byId.Values.Select(Of).OfType<Interaction>()
            .OrderByDescending(i => Math.Abs(i.Weight)).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>What the profile's stored data says, as engine inputs: My List entries (finished or just saved), then the ratings on top.</summary>
    public static List<InteractionInput> Inputs(IEnumerable<WatchlistItem> list, IReadOnlyDictionary<string, (string Title, Rating Rating)> ratings)
    {
        var inputs = new Dictionary<string, InteractionInput>();
        foreach (var item in list) inputs[item.ContentId] = new InteractionInput(item.ContentId, item.Title, null, item.Watched == true, true);
        foreach (var (id, entry) in ratings)
        {
            var baseInput = inputs.TryGetValue(id, out var existing) ? existing : new InteractionInput(id, entry.Title);
            inputs[id] = baseInput with { Rating = entry.Rating };
        }

        return inputs.Values.ToList();
    }

    /// <summary>
    /// Never recommended: finished titles, anything rated Like or Not for me (a liked title informs the picks but is never picked itself), anything in Continue Watching, and titles taken out
    /// of the row by hand (kept out, but not a taste signal).
    /// </summary>
    public static HashSet<string> Excluded(IEnumerable<WatchlistItem> list, IEnumerable<string> ratedIds, IEnumerable<string> continueIds, IEnumerable<string>? dismissed = null)
    {
        var ids = new HashSet<string>(list.Where(i => i.Watched == true).Select(i => i.ContentId));
        ids.UnionWith(ratedIds);
        ids.UnionWith(continueIds);
        if (dismissed is not null) ids.UnionWith(dismissed);
        return ids;
    }

    /// <summary>A short fingerprint of the preference data: it changes exactly when a recommendation could.</summary>
    public static string Signature(IEnumerable<Interaction> interactions) => string.Join("|", interactions.Select(i => $"{i.Id}:{i.Kind.ToString().ToLowerInvariant()}").Order(StringComparer.Ordinal));
}

/// <summary>A title of the profile's that pushed a feature, for honest explanations.</summary>
public sealed record Contribution(string Id, string Title, SignalKind Kind, double Amount);

public sealed class TastePreferences
{
    public Dictionary<string, double> Genre { get; } = [];
    public Dictionary<string, double> Director { get; } = [];
    public Dictionary<string, double> Cast { get; } = [];

    /// <summary>Which titles pushed each feature ("genre|drama" to a list).</summary>
    public Dictionary<string, List<Contribution>> Contributions { get; } = [];

    public IReadOnlyDictionary<string, double> Vector(TasteCategory category) => category switch { TasteCategory.Genre => Genre, TasteCategory.Director => Director, _ => Cast };

    /// <summary>True when the profile has any nonzero preference in this category.</summary>
    public bool Has(TasteCategory category) => Vector(category).Values.Any(v => v != 0);

    internal static string KeyOf(TasteCategory category, string feature) => $"{category.Key()}|{feature}";

    internal void Add(TasteCategory category, string feature, double amount, Interaction from)
    {
        var map = category switch { TasteCategory.Genre => Genre, TasteCategory.Director => Director, _ => Cast };
        map[feature] = map.GetValueOrDefault(feature) + amount;
        var key = KeyOf(category, feature);
        if (!Contributions.TryGetValue(key, out var list)) Contributions[key] = list = [];
        list.Add(new Contribution(from.Id, from.Title, from.Kind, amount));
    }

    /// <summary>
    /// One preference map per category. A title's signal weight is split equally among its unique features within each category, so a film with a long cast list doesn't count for more
    /// than one with a short one. Titles without metadata for a category add nothing there.
    /// </summary>
    public static TastePreferences Build(IEnumerable<Interaction> interactions, IReadOnlyDictionary<string, Features> featuresById)
    {
        var prefs = new TastePreferences();
        foreach (var interaction in interactions)
        {
            if (!featuresById.TryGetValue(interaction.Id, out var features)) continue;
            foreach (var category in TasteCategories.All)
            {
                var list = features.List(category);
                if (list.Count == 0) continue;
                var share = interaction.Weight / list.Count;
                foreach (var feature in list) prefs.Add(category, feature, share, interaction);
            }
        }

        return prefs;
    }
}
