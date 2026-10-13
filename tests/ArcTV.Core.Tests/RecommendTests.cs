using ArcTV.Core.Addons;
using ArcTV.Core.MyList;
using ArcTV.Core.Plus;
using ArcTV.Core.Recommend;
using Xunit;

namespace ArcTV.Core.Tests;

public class RecommendTests
{
    private static Features F(string[] genres, string[]? directors = null, string[]? cast = null) => new(genres, directors ?? [], cast ?? []);

    private static MetaPreview P(string id, string rating, params string[] genres) => new(id, "movie", id, Genres: [.. genres], ImdbRating: rating);

    private static FeedbackItem Like(string id) => new("main", "c", id, "MOVIE", "Title " + id, "like", "2026-10-01T00:00:00.000Z");

    private static FeatureLoader LoaderOf(Dictionary<string, Features> known) => (refs, _) => Task.FromResult<IReadOnlyDictionary<string, Features>>(refs.Where(r => known.ContainsKey(r.Id)).ToDictionary(r => r.Id, r => known[r.Id]));

    [Fact]
    public void TheStrongestSignalWinsAndADislikeStaysNegative()
    {
        Assert.Equal(SignalKind.Like, Interactions.Of(new InteractionInput("a", "A", Rating.Like, true, true))!.Kind);
        Assert.Equal(-5, Interactions.Of(new InteractionInput("a", "A", Rating.Dislike, true, true))!.Weight);
        Assert.Equal(SignalKind.Completed, Interactions.Of(new InteractionInput("a", "A", null, true, true))!.Kind);
        Assert.Equal(1, Interactions.Of(new InteractionInput("a", "A", null, false, true))!.Weight);
        Assert.Null(Interactions.Of(new InteractionInput("a", "A")));
    }

    [Fact]
    public void InteractionsAreCollectedStrongestFirstThenById()
    {
        var list = new[] { new WatchlistItem("p", "b", "MOVIE", "B", Watched: false), new WatchlistItem("p", "a", "MOVIE", "A", Watched: true), new WatchlistItem("p", "c", "MOVIE", "C", Watched: false) };
        var ratings = new Dictionary<string, (string, Rating)> { ["c"] = ("C", Rating.Like), ["z"] = ("Z", Rating.Dislike) };
        var all = Interactions.Collect(Interactions.Inputs(list, ratings));
        Assert.Equal(["c", "z", "a", "b"], all.Select(i => i.Id)); // like and dislike (5), finished (2), saved (1)
        Assert.Equal("a:completed|b:watchlist|c:like|z:dislike", Interactions.Signature(all));
    }

    [Fact]
    public void ExcludedTitlesAreFinishedRatedStartedOrTakenOut()
    {
        var list = new[] { new WatchlistItem("p", "done", "MOVIE", "D", Watched: true), new WatchlistItem("p", "saved", "MOVIE", "S", Watched: false) };
        var excluded = Interactions.Excluded(list, ["rated"], ["going"], ["gone"]);
        Assert.Equal(new HashSet<string> { "done", "rated", "going", "gone" }, excluded);
    }

    [Fact]
    public void FeaturesAreNormalisedAndDirectorsSplit()
    {
        var f = Features.FromNames(["  Action ", "action", "Sci-Fi"], ["Lana Wachowski, Lilly Wachowski", "A / B"], Enumerable.Range(0, 20).Select(i => "Actor " + i));
        Assert.Equal(["action", "sci-fi"], f.Genres);
        Assert.Equal(["lana wachowski", "lilly wachowski", "a", "b"], f.Directors);
        Assert.Equal(12, f.Cast.Count);
    }

    [Fact]
    public void ATitlesWeightIsSplitEquallyAmongItsFeatures()
    {
        var like = new Interaction("a", "A", SignalKind.Like, 5);
        var prefs = TastePreferences.Build([like], new Dictionary<string, Features> { ["a"] = F(["action", "thriller"], ["d"]) });
        Assert.Equal(2.5, prefs.Genre["action"]);
        Assert.Equal(5, prefs.Director["d"]);
        Assert.Equal("A", prefs.Contributions["genre|action"][0].Title);
        Assert.False(prefs.Has(TasteCategory.Cast));
    }

    [Fact]
    public void ScoringIsACosineOverTheCategoriesThatCanBeCompared()
    {
        var prefs = TastePreferences.Build([new Interaction("a", "A", SignalKind.Like, 5)], new Dictionary<string, Features> { ["a"] = F(["action"]) });
        Assert.Equal(1, Scoring.ScoreCandidate(F(["action"]), prefs)!.Score, 6);
        Assert.Equal(0, Scoring.ScoreCandidate(F(["drama"]), prefs)!.Score, 6);
        Assert.Null(Scoring.ScoreCandidate(F([]), prefs)); // nothing to compare
        Assert.Null(Scoring.ScoreCandidate(null, prefs));
        var disliked = TastePreferences.Build([new Interaction("a", "A", SignalKind.Dislike, -5)], new Dictionary<string, Features> { ["a"] = F(["horror"]) });
        Assert.Equal(-1, Scoring.ScoreCandidate(F(["horror"]), disliked)!.Score, 6);
    }

    [Fact]
    public void AReasonNamesTheTitleThatContributedMostAndNeverAnInventedOne()
    {
        var interactions = new[] { new Interaction("a", "Heat", SignalKind.Like, 5), new Interaction("b", "Se7en", SignalKind.Completed, 2) };
        var prefs = TastePreferences.Build(interactions, new Dictionary<string, Features> { ["a"] = F(["crime"]), ["b"] = F(["crime"]) });
        Assert.Equal("Because you liked Heat", Scoring.Explain(F(["crime"]), prefs));
        Assert.Null(Scoring.Explain(F(["comedy"]), prefs));
        Assert.Equal("More from directors you enjoy", Scoring.Explain(F(["comedy"], ["mann"]), TastePreferences.Build([new Interaction("a", "Heat", SignalKind.Like, 5)], new Dictionary<string, Features> { ["a"] = F(["crime"], ["mann"]) })));
    }

    private sealed record Pick(string Id, double Score, IReadOnlyList<Source> Sources, string? Genre = null) : IScoredPick, IComposePick;

    [Fact]
    public void OneTitleCanExplainOnlyAFewPicksAndTheRestWait()
    {
        var shared = new Source("s", "S", SignalKind.Like) { Total = 1 };
        var other = new Source("o", "O", SignalKind.Like) { Total = 1 };
        var picks = Enumerable.Range(0, 5).Select(i => new Pick("p" + i, 1 - i * 0.1, [shared])).Append(new Pick("q", 0.2, [other])).ToList();
        var chosen = Scoring.Diversify(picks, 20, 3);
        Assert.Equal(["p0", "p1", "p2", "q", "p3", "p4"], chosen.Select(c => c.Pick.Id)); // p3 and p4 wait: S already explains three
        Assert.All(chosen.Take(3), c => Assert.Equal("s", c.Source!.Id));
    }

    [Fact]
    public void TheSeededGeneratorRepeatsItselfAndStaysInRange()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(42);
        var first = Enumerable.Range(0, 10).Select(_ => a.Next()).ToList();
        Assert.Equal(first, Enumerable.Range(0, 10).Select(_ => b.Next()));
        Assert.All(first, v => Assert.InRange(v, 0, 1));
        Assert.NotEqual(first, Enumerable.Range(0, 10).Select(_ => new SeededRandom(43).Next()));
    }

    [Fact]
    public void ARowKeepsTheBestAndWithoutASeedIsFullyDeterministic()
    {
        var picks = Enumerable.Range(0, 12).Select(i => new Pick("p" + i, 1 - i * 0.05, [], i % 2 == 0 ? "action" : "drama")).ToList();
        var shares = new Dictionary<string, double> { ["action"] = 0.5, ["drama"] = 0.5 };
        var one = Compose.Of(picks, 8, shares);
        var two = Compose.Of(picks, 8, shares);
        Assert.Equal(one.Select(p => p.Id), two.Select(p => p.Id));
        Assert.Equal(8, one.Count);
        Assert.Equal(["p0", "p1", "p2", "p3", "p4"], one.Take(5).Select(p => p.Id)); // the anchors
        Assert.Equal(8, one.Select(p => p.Id).Distinct().Count());
        var seeded = Compose.Of(picks, 8, shares, seed: 7);
        Assert.Equal(seeded.Select(p => p.Id), Compose.Of(picks, 8, shares, seed: 7).Select(p => p.Id));
    }

    [Fact]
    public void TheMainGenreOfATitleIsTheOneTheProfileLikesMost()
    {
        var prefs = new TastePreferences();
        prefs.Genre["action"] = 5;
        prefs.Genre["drama"] = 2;
        Assert.Equal("action", Compose.PrimaryGenre(["drama", "action"], prefs));
        Assert.Null(Compose.PrimaryGenre(["comedy"], prefs));
        var shares = Compose.TasteShares([new Interaction("a", "A", SignalKind.Like, 5), new Interaction("b", "B", SignalKind.Completed, 2)],
            new Dictionary<string, Features> { ["a"] = F(["action"]), ["b"] = F(["drama"]) }, prefs);
        Assert.Equal(5 / 7.0, shares["action"], 6);
        Assert.Equal(1.0, shares.Values.Sum(), 6);
    }

    [Fact]
    public async Task WithTooFewSignalsTheRowIsThePopularFallback()
    {
        var pool = new[] { P("a", "7.0", "action"), P("b", "9.0", "drama"), P("c", "8.0", "comedy") }.Select(Candidate.From).ToList();
        var result = await Engine.RecommendAsync(new EngineInput([new Interaction("x", "X", SignalKind.Like, 5)], new HashSet<string>(), pool, new Dictionary<string, TitleRef>(), LoaderOf([])));
        Assert.False(result.IsPersonal);
        Assert.Equal(["b", "c", "a"], result.Items.Select(i => i.Id)); // best rated first
        Assert.All(result.Items, i => Assert.Null(i.Reason));
    }

    [Fact]
    public async Task APersonalRowPutsWhatMatchesFirstExplainsItAndLeavesOutWhatIsExcluded()
    {
        var known = new Dictionary<string, Features>
        {
            ["l1"] = F(["action", "thriller"]), ["l2"] = F(["action"]), ["l3"] = F(["action", "sci-fi"]),
            ["a1"] = F(["action"]), ["a2"] = F(["action", "thriller"]), ["d1"] = F(["drama"]), ["c1"] = F(["comedy"]), ["x"] = F(["action"]),
        };
        var interactions = new[] { new Interaction("l1", "Heat", SignalKind.Like, 5), new Interaction("l2", "Die Hard", SignalKind.Like, 5), new Interaction("l3", "Aliens", SignalKind.Like, 5) };
        var pool = new[] { P("a1", "8.0", "action"), P("a2", "7.0", "action", "thriller"), P("d1", "9.0", "drama"), P("c1", "9.5", "comedy"), P("x", "9.9", "action") }.Select(Candidate.From).ToList();
        var result = await Engine.RecommendAsync(new EngineInput(interactions, new HashSet<string> { "x" }, pool, new Dictionary<string, TitleRef>(), LoaderOf(known)));
        Assert.True(result.IsPersonal);
        Assert.DoesNotContain(result.Items, i => i.Id == "x");
        Assert.Equal(new HashSet<string> { "a1", "a2" }, result.Items.Take(2).Select(i => i.Id).ToHashSet());
        Assert.StartsWith("Because you liked", result.Items[0].Reason);
        Assert.Equal(4, result.Items.Count);
        Assert.All(result.Items.Where(i => i.Id is "d1" or "c1"), i => Assert.Null(i.Reason)); // no real match, no invented reason
    }

    [Fact]
    public void TheRowDropsWhatMustNotShowAndIsLabelledAsWhatItIs()
    {
        var pool = new[] { P("a", "8", "x"), P("b", "8", "x"), P("c", "8", "x") };
        var personal = new EngineResult(true, [new Picked("a", 1, "Because you liked Z"), new Picked("b", 0.5, null), new Picked("c", 0.2, "Because you saved Y"), new Picked("gone", 0.1, null)]);
        var row = PickedRow.From(personal, pool, new HashSet<string> { "b" }, new HashSet<string> { "c" })!;
        Assert.Equal(["a"], row.Entries.Select(e => e.Id));
        Assert.Equal("Because you liked Z", row.Entries[0].Reason);
        Assert.True(row.Personal);
        Assert.Contains("Picked for you", row.Title);
        var popular = PickedRow.From(new EngineResult(false, [new Picked("a", null, "ignored")]), pool, new HashSet<string>(), new HashSet<string>())!;
        Assert.Null(popular.Entries[0].Reason);
        Assert.Contains("not personalised", popular.Title);
        Assert.Null(PickedRow.From(personal, pool, new HashSet<string> { "a", "b", "c" }, new HashSet<string>()));
        Assert.Null(PickedRow.From(null, pool, new HashSet<string>(), new HashSet<string>()));
    }

    [Fact]
    public void ARemovalCountsForFiveDays()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var local = new Dictionary<string, DateTime> { ["old"] = now.AddDays(-6), ["new"] = now.AddDays(-1) };
        Assert.Equal(new HashSet<string> { "new" }, PickedDismissals.Active(local, now));
        var merged = PickedDismissals.Merged(local, [new PickedDismissal("main", "new", IsoDateText(now.AddHours(-2))), new PickedDismissal("main", "other", IsoDateText(now.AddDays(-2))), new PickedDismissal("main", "stale", IsoDateText(now.AddDays(-9)))], now);
        Assert.Equal(["new", "other"], merged.Keys.Order());
        Assert.Equal(now.AddHours(-2), merged["new"]);
    }

    private static string IsoDateText(DateTime time) => Account.IsoDate.From(time);

    [Fact]
    public void TheFeatureCacheKeepsTitlesForAMonthAndNoMoreThanItsLimit()
    {
        var cache = new FeatureCache(null);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        cache.Put(new Dictionary<string, Features> { ["a"] = F(["x"]) }, start);
        Assert.NotNull(cache.Get("a", start.AddDays(29)));
        Assert.Null(cache.Get("a", start.AddDays(31)));
        var many = Enumerable.Range(0, RecommendTuning.FeatureCacheMaxEntries + 10).ToDictionary(i => "t" + i, _ => F(["x"]));
        cache.Put(many, start.AddDays(1));
        Assert.Equal(RecommendTuning.FeatureCacheMaxEntries, cache.Count);
    }

    [Fact]
    public async Task TheLoaderReadsTheCacheFirstAndLooksUpOnlyWhatIsMissingWithinItsLimit()
    {
        var cache = new FeatureCache(null);
        cache.Put(new Dictionary<string, Features> { ["cached"] = F(["x"]) });
        var asked = new List<string>();
        var loader = FeatureLoading.Loader(cache, r =>
        {
            lock (asked) asked.Add(r.Id);
            return Task.FromResult<Features?>(r.Id == "empty" ? Features.Empty : r.Id == "boom" ? throw new HttpRequestException() : F(["y"]));
        });
        var got = await loader([new TitleRef("cached"), new TitleRef("a"), new TitleRef("b"), new TitleRef("c"), new TitleRef("empty"), new TitleRef("boom"), new TitleRef("a")], 3);
        Assert.Equal(3, asked.Count); // only three of the unknown ones, never the cached one
        Assert.DoesNotContain("cached", asked);
        Assert.True(got.ContainsKey("cached"));
        Assert.Equal(["a", "b", "c"], asked.Order());
        var again = await loader([new TitleRef("a")], 3);
        Assert.True(again.ContainsKey("a"));
        Assert.Equal(3, asked.Count); // a is cached now
    }

    private sealed class FakePickedStore : IPickedStore
    {
        public List<string> Dismissed = [];

        public Task<IReadOnlyList<PickedDismissal>> DismissalsAsync(CancellationToken cancel = default) => Task.FromResult<IReadOnlyList<PickedDismissal>>([]);

        public Task DismissAsync(string contentId, DateTime at, CancellationToken cancel = default)
        {
            Dismissed.Add(contentId);
            return Task.CompletedTask;
        }
    }

    private static PickedInputs Inputs(bool active = true, bool ready = true, params FeedbackItem[] ratings)
    {
        var pool = new[] { P("a1", "8.0", "action"), P("a2", "7.0", "action"), P("a3", "6.0", "action"), P("d1", "9.0", "drama") };
        return new PickedInputs(active, ready, pool, [new WatchlistItem("p", "l1", "MOVIE", "Heat", Watched: false), new WatchlistItem("p", "l2", "MOVIE", "Die Hard", Watched: false), new WatchlistItem("p", "l3", "MOVIE", "Aliens", Watched: false)],
            ratings.ToDictionary(r => r.ContentId), []);
    }

    private static FeatureLoader ActionLoader() => LoaderOf(new Dictionary<string, Features>
    {
        ["l1"] = F(["action"]), ["l2"] = F(["action"]), ["l3"] = F(["action"]), ["a1"] = F(["action"]), ["a2"] = F(["action"]), ["a3"] = F(["action"]), ["d1"] = F(["drama"]),
    });

    private static async Task<PickedRow?> WaitForRow(PickedModel model)
    {
        for (var i = 0; i < 200 && model.Row is null; i++) await Task.Delay(10);
        return model.Row;
    }

    [Fact]
    public async Task WithoutPlusThereIsNoRowAndWithPlusItAppearsOnceHomeIsReady()
    {
        var current = Inputs(active: false);
        var model = new PickedModel(() => current, ActionLoader(), new FakePickedStore());
        model.Refresh();
        Assert.Null(model.Row);
        current = Inputs(ready: false);
        model.Refresh();
        Assert.Null(model.Row); // Home is not loaded yet
        current = Inputs();
        model.Refresh();
        var row = await WaitForRow(model);
        Assert.NotNull(row);
        Assert.True(row!.Personal);
        Assert.Contains(row.Entries, e => e.Id == "a1");
    }

    [Fact]
    public async Task ATitleRatedAfterwardsDisappearsFromTheRowAtOnce()
    {
        var current = Inputs();
        var model = new PickedModel(() => current, ActionLoader(), new FakePickedStore());
        model.Refresh();
        Assert.NotNull(await WaitForRow(model));
        Assert.Contains(model.Row!.Entries, e => e.Id == "a1");
        current = Inputs(true, true, Like("a1"));
        model.Refresh();
        Assert.DoesNotContain(model.Row!.Entries, e => e.Id == "a1");
    }

    [Fact]
    public async Task ARemovedTitleStaysOutOnThisPcAndIsToldToTheAccount()
    {
        var file = Path.Combine(Path.GetTempPath(), "arctv-picked-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new FakePickedStore();
            var model = new PickedModel(() => Inputs(), ActionLoader(), store, file);
            model.Refresh();
            Assert.NotNull(await WaitForRow(model));
            var first = model.Row!.Entries[0].Id;
            await model.RemoveAsync(first);
            Assert.DoesNotContain(model.Row!.Entries, e => e.Id == first);
            Assert.Equal([first], store.Dismissed);

            var next = new PickedModel(() => Inputs(), ActionLoader(), new FakePickedStore(), file);
            next.Refresh();
            Assert.NotNull(await WaitForRow(next));
            Assert.DoesNotContain(next.Row!.Entries, e => e.Id == first); // remembered across launches
        }
        finally
        {
            File.Delete(file);
        }
    }
}
