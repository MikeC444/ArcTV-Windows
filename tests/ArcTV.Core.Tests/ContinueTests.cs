using ArcTV.Core.Continue;
using ArcTV.Core.Details;
using ArcTV.Core.Sources;
using Xunit;

namespace ArcTV.Core.Tests;

public class ContinueTests
{
    private sealed class FakeStore : IContinueStore
    {
        public List<ContinueItem> Items = [];
        public List<string> Reports = [];
        public bool Fail;
        public List<ArcTV.Core.Plus.HistoryItem> History = [];

        public Task<IReadOnlyList<ArcTV.Core.Plus.HistoryItem>> HistoryAsync(CancellationToken cancel = default) => Fail ? throw new HttpRequestException("down") : Task.FromResult<IReadOnlyList<ArcTV.Core.Plus.HistoryItem>>(History.ToList());

        public Task<IReadOnlyList<ContinueItem>> ListAsync(CancellationToken cancel = default) => Fail ? throw new HttpRequestException("down") : Task.FromResult<IReadOnlyList<ContinueItem>>(Items.ToList());

        public Task ReportAsync(string body, CancellationToken cancel = default)
        {
            if (Fail) throw new HttpRequestException("down");
            Reports.Add(body);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(ContinueItem item, CancellationToken cancel = default)
        {
            if (Fail) throw new HttpRequestException("down");
            Items.RemoveAll(i => i.ContentId == item.ContentId);
            return Task.CompletedTask;
        }
    }

    private static ContinueItem Movie(string id, long position, long duration = 7_200_000) => new("p", id, "MOVIE", "Film " + id, position, duration, "2026-10-01T00:00:00.000Z");

    private static ContinueItem Episode(string id, int season, int episode, long position, long duration = 2_700_000) =>
        new("p", id, "TV_SHOW", "Show " + id, position, duration, "2026-10-01T00:00:00.000Z", season, episode, "Pilot");

    [Fact]
    public void TheListIsReadAndAnItemKnowsHowFarInItIs()
    {
        var items = ContinueApi.ParseItems("""{"items":[{"providerId":"p","contentId":"tt1","contentType":"TV_SHOW","seasonNumber":2,"episodeNumber":5,"episodeTitle":"Five","title":"Show","positionMs":900000,"durationMs":3600000,"lastWatchedAt":"2026-10-01T00:00:00.000Z"}]}""");
        var item = Assert.Single(items);
        Assert.Equal(0.25, item.Progress);
        Assert.Equal(900, item.PositionSeconds);
        Assert.Equal("S2 E5", item.EpisodeLabel);
        Assert.False(item.IsMovie);
        Assert.False(item.NearlyDone);
        Assert.Throws<Account.ApiException>(() => ContinueApi.ParseItems("not json"));
    }

    [Fact]
    public void ARowStartsFromWhereItWasLeftUnlessItIsNearlyFinished()
    {
        var target = Episode("tt1", 2, 5, 900_000).ToTarget();
        Assert.Equal("tt1", target.Title.Id);
        Assert.Equal((2, 5), (target.Season, target.Episode));
        Assert.Equal(900, target.StartAt);
        Assert.Equal("S2 E5 Pilot", target.EpisodeTitle);
        Assert.Equal(0, Movie("tt2", 7_000_000).ToTarget().StartAt); // 97 percent: from the start
        Assert.Equal("movie", Movie("tt2", 1000).ToTarget().Title.Type);
        Assert.Null(Movie("tt2", 1000).ToTarget().EpisodeTitle);
    }

    [Fact]
    public void TheReportBodyLeavesOutWhatAMovieDoesNotHave()
    {
        var movie = ContinueApi.ProgressBody(new MetaDetail("tt1", "movie", "Film", "https://p.test/a.jpg"), null, null, null, 120.5, 7200, false, "2026-10-13T00:00:00.000Z");
        using var doc = System.Text.Json.JsonDocument.Parse(movie);
        var root = doc.RootElement;
        Assert.Equal("MOVIE", root.GetProperty("contentType").GetString());
        Assert.Equal(120500, root.GetProperty("positionMs").GetInt64());
        Assert.Equal(7_200_000, root.GetProperty("durationMs").GetInt64());
        Assert.False(root.GetProperty("completed").GetBoolean());
        Assert.False(root.TryGetProperty("seasonNumber", out _));
        Assert.False(root.TryGetProperty("backdropUrl", out _));
        Assert.Equal("https://p.test/a.jpg", root.GetProperty("posterUrl").GetString());

        var show = ContinueApi.ProgressBody(new MetaDetail("tt2", "series", "Show"), 1, 3, "S1 E3 Three", 10, 100, true, "2026-10-13T00:00:00.000Z");
        using var d2 = System.Text.Json.JsonDocument.Parse(show);
        Assert.Equal("TV_SHOW", d2.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(1, d2.RootElement.GetProperty("seasonNumber").GetInt32());
        Assert.Equal(3, d2.RootElement.GetProperty("episodeNumber").GetInt32());
    }

    [Fact]
    public async Task TooLittleToSayIsNotReportedAndNinetyTwoPercentIsFinished()
    {
        var store = new FakeStore();
        var model = new ContinueModel(store);
        var target = new PlayTarget(new MetaDetail("tt1", "movie", "Film"));
        await model.ReportAsync(target, 3, 7200); // under 5 seconds in
        await model.ReportAsync(target, 20, 25); // a clip
        Assert.Empty(store.Reports);
        await model.ReportAsync(target, 600, 7200);
        Assert.Contains("\"completed\":false", store.Reports[0]);
        await model.ReportAsync(target, 6700, 7200); // 93 percent
        Assert.Contains("\"completed\":true", store.Reports[1]);
    }

    [Fact]
    public async Task AMoviePlayedToTheEndIsMarkedWatchedAndAShowIsNot()
    {
        var finished = new List<string>();
        var model = new ContinueModel(new FakeStore(), title =>
        {
            finished.Add(title.Id);
            return Task.CompletedTask;
        });
        await model.ReportAsync(new PlayTarget(new MetaDetail("tt1", "movie", "Film")), 7100, 7200);
        await model.ReportAsync(new PlayTarget(new MetaDetail("tt2", "series", "Show"), 1, 1), 2600, 2700);
        await model.ReportAsync(new PlayTarget(new MetaDetail("tt3", "movie", "Other")), 100, 7200);
        Assert.Equal(["tt1"], finished);
    }

    [Fact]
    public async Task EachItemGetsItsTitleLogoLookedUpOnceAndAFailureLeavesOneOut()
    {
        var store = new FakeStore { Items = [Movie("tt1", 600_000), Movie("tt2", 600_000), Episode("tt3", 1, 1, 100_000)] };
        var asked = new List<string>();
        var model = new ContinueModel(store)
        {
            LogoLookup = (item, _) =>
            {
                lock (asked) asked.Add(item.ContentId);
                return item.ContentId == "tt2" ? throw new HttpRequestException("no") : Task.FromResult<string?>("https://img.test/" + item.ContentId + ".png");
            },
        };
        await model.LoadAsync();
        Assert.Equal(["tt1", "tt3"], model.Logos.Keys.Order());
        Assert.Equal("https://img.test/tt1.png", model.Logos["tt1"]);
        asked.Clear();
        await model.LoadAsync();
        Assert.Equal(["tt2"], asked); // only the one still without a logo is tried again
    }

    private static MetaDetail ThreeEpisodes() => new("tt5", "series", "Show tt5", Runtime: "40 min", Videos:
        [new("a", Season: 1, Episode: 1, Title: "One"), new("b", Season: 1, Episode: 2, Title: "Two"), new("c", Season: 1, Episode: 3, Title: "Three")]);

    [Fact]
    public async Task MarkingEpisodesWatchedReportsEachAsFinishedAndPutsBackWhereThePersonIs()
    {
        var show = ThreeEpisodes();
        var store = new FakeStore { Items = [Episode("tt5", 1, 3, 600_000)] }; // partway through episode 3
        var model = new ContinueModel(store);
        await model.LoadAsync();

        Assert.True(await model.SetEpisodesWatchedAsync(show.Seasons[1].Take(2).ToList(), true, show));
        Assert.Equal(3, store.Reports.Count); // two finished episodes, then episode 3 put back as the pointer
        Assert.Contains("\"completed\":true", store.Reports[0]);
        Assert.Contains("\"episodeNumber\":1", store.Reports[0]);
        Assert.Contains("\"positionMs\":2400000", store.Reports[0]); // the show's own 40 minutes
        Assert.Contains("\"episodeNumber\":2", store.Reports[1]);
        Assert.Contains("\"episodeNumber\":3", store.Reports[2]);
        Assert.Contains("\"completed\":false", store.Reports[2]);
        Assert.Contains("\"positionMs\":600000", store.Reports[2]);
    }

    [Fact]
    public async Task MarkingNotWatchedWritesTheStartBackAndClearsThePointerTheReportMade()
    {
        var show = ThreeEpisodes();
        var store = new FakeStore { Items = [Episode("tt5", 1, 1, 2_400_000)] }; // the pointer is the very episode being un-marked
        var model = new ContinueModel(store);
        await model.LoadAsync();

        Assert.True(await model.SetEpisodesWatchedAsync([show.Seasons[1][0]], false, show));
        Assert.Single(store.Reports);
        Assert.Contains("\"completed\":false", store.Reports[0]);
        Assert.Contains("\"positionMs\":0", store.Reports[0]);
        Assert.Empty(store.Items); // nothing else to put back: the pointer is cleared
    }

    [Fact]
    public async Task AFailedMarkingSaysSoAndEpisodeStatusesComeFromTheHistory()
    {
        var show = ThreeEpisodes();
        var store = new FakeStore { Fail = true };
        var model = new ContinueModel(store);
        Assert.False(await model.SetEpisodesWatchedAsync([show.Seasons[1][0]], true, show));
        Assert.Null(await model.EpisodeStatusesAsync("tt5")); // the history cannot be read

        store.Fail = false;
        store.History = [new("p", "tt5", "TV_SHOW", "Show", 2_400_000, 2_400_000, true, "2026-10-01T00:00:00.000Z", 1, 1)];
        var statuses = await model.EpisodeStatusesAsync("tt5");
        Assert.True(statuses!["1:1"].Watched);
    }

    [Fact]
    public async Task AReportRefreshesTheRow()
    {
        var store = new FakeStore { Items = [Movie("tt1", 600_000)] };
        var model = new ContinueModel(store);
        await model.ReportAsync(new PlayTarget(new MetaDetail("tt1", "movie", "Film")), 600, 7200);
        Assert.Single(model.Items);
    }

    [Fact]
    public async Task RemovingIsShownAtOnceAndPutBackOnFailure()
    {
        var store = new FakeStore { Items = [Movie("tt1", 600_000), Movie("tt2", 600_000)] };
        var model = new ContinueModel(store);
        await model.LoadAsync();
        store.Fail = true;
        await model.RemoveAsync(model.Items[0]);
        Assert.Equal(2, model.Items.Count);
        store.Fail = false;
        await model.RemoveAsync(model.Items[0]);
        Assert.Equal(["tt2"], model.Items.Select(i => i.ContentId));
    }

    [Fact]
    public async Task ResumeFindsTheTitleOrTheEpisodeAndLeavesAnExplicitPlaceAlone()
    {
        var store = new FakeStore { Items = [Movie("tt1", 900_000), Episode("tt2", 1, 4, 600_000), Movie("tt3", 7_100_000)] };
        var model = new ContinueModel(store);
        await model.LoadAsync();

        Assert.Equal(900, model.Resume(new PlayTarget(new MetaDetail("tt1", "movie", "Film"))).StartAt);
        Assert.Equal(600, model.Resume(new PlayTarget(new MetaDetail("tt2", "series", "Show"), 1, 4)).StartAt);
        Assert.Equal(0, model.Resume(new PlayTarget(new MetaDetail("tt2", "series", "Show"), 1, 5)).StartAt); // another episode: not this one's place
        Assert.Equal(0, model.Resume(new PlayTarget(new MetaDetail("tt3", "movie", "Done"))).StartAt); // nearly finished
        Assert.Equal(0, model.Resume(new PlayTarget(new MetaDetail("tt9", "movie", "Unknown"))).StartAt);
        Assert.Equal(42, model.Resume(new PlayTarget(new MetaDetail("tt1", "movie", "Film"), StartAt: 42)).StartAt);
        Assert.Equal("tt2", model.ResumeItem("tt2")!.ContentId);
        Assert.Null(model.ResumeItem("tt2", 2, 1));
    }
}
