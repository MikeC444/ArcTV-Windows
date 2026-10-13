using ArcTV.Core.Addons;

namespace ArcTV.Core.Recommend;

/// <summary>A catalogue entry that could be recommended. <see cref="Genres"/> come from the catalogue listing itself (no extra request).</summary>
public sealed record Candidate(string Id, string Title, string? ProviderId = null, IReadOnlyList<string>? Genres = null, double? Rating = null, string? Type = null)
{
    public static Candidate From(MetaPreview item) =>
        new(item.Id, item.Name, AppInfo.CinemetaProviderId, item.Genres ?? [], double.TryParse(item.ImdbRating, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : null, item.Type);
}

/// <summary>A title (movie or show) whose features are looked up.</summary>
public sealed record TitleRef(string Id, string? ProviderId = null, string? Type = null);

/// <summary>Fetches (or reads from cache) the features of up to <paramref name="limit"/> titles not yet known. Unknown or failed lookups are left out. Must never throw.</summary>
public delegate Task<IReadOnlyDictionary<string, Features>> FeatureLoader(IReadOnlyList<TitleRef> refs, int limit);

public sealed record EngineInput(
    IReadOnlyList<Interaction> Interactions, ISet<string> ExcludeIds, IReadOnlyList<Candidate> Pool, IReadOnlyDictionary<string, TitleRef> InteractionRefs, FeatureLoader LoadFeatures,
    int? Seed = null, ISet<string>? PreviousShown = null);

public sealed record Picked(string Id, double? Score, string? Reason);

public sealed record EngineResult(bool IsPersonal, IReadOnlyList<Picked> Items);

public static class Engine
{
    private static int ByRatingThenId(Candidate a, Candidate b)
    {
        var ra = a.Rating ?? -1;
        var rb = b.Rating ?? -1;
        return ra != rb ? rb.CompareTo(ra) : string.CompareOrdinal(a.Id, b.Id);
    }

    /// <summary>Distinct, eligible candidates, in their given order.</summary>
    private static List<Candidate> Eligible(IEnumerable<Candidate> pool, ISet<string> excludeIds)
    {
        var seen = new HashSet<string>();
        return pool.Where(c => !excludeIds.Contains(c.Id) && seen.Add(c.Id)).ToList();
    }

    /// <summary>Cold start, or nothing scorable: the best-rated eligible titles, plainly labelled as popular, never as personalised.</summary>
    public static EngineResult PopularFallback(IEnumerable<Candidate> pool, ISet<string> excludeIds)
    {
        var list = Eligible(pool, excludeIds);
        list.Sort(ByRatingThenId);
        return new EngineResult(false, list.Take(RecommendTuning.MaxResults).Select(c => new Picked(c.Id, null, null)).ToList());
    }

    private sealed record ScoredCandidate(string Id, Candidate Candidate, double Score, IReadOnlyList<Source> Sources, string? Genre) : IScoredPick, IComposePick;

    private sealed record PreScored(Candidate Candidate, double Pre);

    /// <summary>
    /// Ranking in four plain steps: (1) look up the profile's own titles' features and turn the stored signals into genre, director and cast preference vectors; (2) shortlist the eligible
    /// candidates from their catalogue genres alone, within a bounded size; (3) fetch the shortlist's directors and cast (cached, bounded concurrency) and score each with the full
    /// weighted cosine; (4) sort by score, compose the row (strongest stay, the rest follow the profile's genre split and, with a seed, are drawn so a refresh changes most of them),
    /// then the separate diversity step keeps the top 20 while stopping any one of the profile's titles from explaining more than three of them.
    /// </summary>
    public static async Task<EngineResult> RecommendAsync(EngineInput input)
    {
        var pool = Eligible(input.Pool, input.ExcludeIds);
        var signalled = input.Interactions.Where(i => i.Weight != 0).ToList();
        if (signalled.Count < Plus.RecommendConfig.MinInteractionsForPersonalisation || !signalled.Any(i => i.Weight > 0)) return PopularFallback(pool, input.ExcludeIds);

        var own = signalled.Take(Plus.RecommendConfig.InteractionDetailFetchLimit).Select(i => input.InteractionRefs.TryGetValue(i.Id, out var r) ? r : new TitleRef(i.Id)).ToList();
        var ownFeatures = await input.LoadFeatures(own, Plus.RecommendConfig.InteractionDetailFetchLimit).ConfigureAwait(false);
        var prefs = TastePreferences.Build(signalled, ownFeatures);

        // step 2: shortlist from the catalogue listing alone (its genres)
        var preScored = pool.Select(c => new PreScored(c, Scoring.ScoreCandidate(new Features(Features.NormaliseGenres(c.Genres ?? []), [], []), prefs)?.Score ?? double.NegativeInfinity)).ToList();
        preScored.Sort((a, b) => a.Pre != b.Pre ? b.Pre.CompareTo(a.Pre) : ByRatingThenId(a.Candidate, b.Candidate));
        var picked = new HashSet<string>();
        var shortlist = new List<Candidate>();
        void Take(Candidate candidate)
        {
            if (picked.Contains(candidate.Id) || shortlist.Count >= RecommendTuning.CandidateDetailFetchLimit) return;
            picked.Add(candidate.Id);
            shortlist.Add(candidate);
        }

        foreach (var p in preScored.Take(RecommendTuning.ShortlistByScore)) Take(p.Candidate);

        // every genre the profile likes gets candidates to score, in proportion to its share of the profile's taste
        var shares = Compose.TasteShares(signalled, ownFeatures, prefs);
        foreach (var (genre, share) in shares.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var quota = Math.Max(RecommendTuning.ShortlistGenreMin, (int)Math.Ceiling(share * RecommendTuning.ShortlistGenreBudget));
            foreach (var p in preScored.Where(p => Compose.PrimaryGenre(Features.NormaliseGenres(p.Candidate.Genres ?? []), prefs) == genre).Take(quota)) Take(p.Candidate);
        }

        var owners = signalled.Where(i => i.Weight > 0).Take(RecommendTuning.ShortlistSourceTitles);
        var perOwner = owners.Select(owner =>
        {
            var ownGenres = new HashSet<string>(ownFeatures.TryGetValue(owner.Id, out var f) ? f.Genres : []);
            double Overlap(Candidate c)
            {
                var genres = Features.NormaliseGenres(c.Genres ?? []);
                var shared = genres.Count(ownGenres.Contains);
                return shared == 0 ? 0 : (double)shared / (ownGenres.Count + genres.Count - shared);
            }

            var list = pool.Select(c => (Candidate: c, Overlap: Overlap(c))).Where(x => x.Overlap > 0).ToList();
            list.Sort((a, b) => a.Overlap != b.Overlap ? b.Overlap.CompareTo(a.Overlap) : ByRatingThenId(a.Candidate, b.Candidate));
            return list.Select(x => x.Candidate).ToList();
        }).ToList();
        var round = 0;
        while (shortlist.Count < RecommendTuning.CandidateDetailFetchLimit && round < pool.Count)
        {
            var progressed = false;
            foreach (var list in perOwner)
            {
                var next = list.FirstOrDefault(c => !picked.Contains(c.Id));
                if (next is null) continue;
                Take(next);
                progressed = true;
            }

            if (!progressed) break;
            round++;
        }

        // anything still free is filled by overall match
        foreach (var p in preScored) Take(p.Candidate);

        // step 3: full features for the shortlist
        var details = await input.LoadFeatures(shortlist.Select(c => new TitleRef(c.Id, c.ProviderId, c.Type)).ToList(), RecommendTuning.CandidateDetailFetchLimit).ConfigureAwait(false);

        // a candidate that is itself one of the profile's titles (saved or liked but not finished) is judged against the profile without that title, so it can neither boost its own
        // score nor be named as its own reason
        var ownIds = new HashSet<string>(signalled.Select(i => i.Id));
        var withoutSelf = new Dictionary<string, TastePreferences>();
        TastePreferences PrefsFor(string id)
        {
            if (!ownIds.Contains(id)) return prefs;
            if (withoutSelf.TryGetValue(id, out var cached)) return cached;
            return withoutSelf[id] = TastePreferences.Build(signalled.Where(i => i.Id != id), ownFeatures);
        }

        var scored = new List<ScoredCandidate>();
        foreach (var candidate in shortlist)
        {
            details.TryGetValue(candidate.Id, out var fetched);
            var features = new Features(fetched is { Genres.Count: > 0 } ? fetched.Genres : Features.NormaliseGenres(candidate.Genres ?? []), fetched?.Directors ?? [], fetched?.Cast ?? []);
            var candidatePrefs = PrefsFor(candidate.Id);
            if (Scoring.ScoreCandidate(features, candidatePrefs) is not { } result) continue;
            scored.Add(new ScoredCandidate(candidate.Id, candidate, result.Score, result.Score > 0 ? Scoring.RankSources(features, candidatePrefs) : [], Compose.PrimaryGenre(features.Genres, prefs)));
        }

        if (scored.Count == 0) return PopularFallback(pool, input.ExcludeIds);

        // step 4: what is not in the composed row follows in score order, so the source cap can still pull in a next-best pick when it has to skip one
        var sorted = scored.ToList();
        sorted.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : ByRatingThenId(a.Candidate, b.Candidate));
        var composed = Compose.Of(sorted, RecommendTuning.MaxResults, shares, previous: input.PreviousShown, seed: input.Seed);
        var composedIds = new HashSet<string>(composed.Select(c => c.Id));
        var ordered = composed.Concat(sorted.Where(c => !composedIds.Contains(c.Id))).ToList();
        var items = Scoring.Diversify(ordered, RecommendTuning.MaxResults).Select(d => new Picked(d.Pick.Id, d.Pick.Score, d.Source is { } s ? Scoring.ReasonFor(s) : null)).ToList();
        return new EngineResult(true, items);
    }
}
