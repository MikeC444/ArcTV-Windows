using ArcTV.Core.Account;
using ArcTV.Core.MyList;
using ArcTV.Core.Plus;
using Xunit;

namespace ArcTV.Core.Tests;

public class PlusTests
{
    private sealed class FakeStore : IPlusStore
    {
        public PlusStatus Status = new(true, "monthly", "2026-11-01T00:00:00.000Z", true, false, 7);
        public List<FeedbackItem> Feedback = [];
        public List<HistoryItem> History = [];
        public bool Fail;
        public int StatusCalls;
        public int ActiveAfter = int.MaxValue;
        public string CheckoutUrl = "https://checkout.stripe.test/pay";

        public Task<PlusStatus> StatusAsync(CancellationToken cancel = default)
        {
            StatusCalls++;
            if (Fail) throw new HttpRequestException("down");
            return Task.FromResult(StatusCalls >= ActiveAfter ? Status with { Active = true } : Status);
        }

        public Task<CheckoutLink> StartCheckoutAsync(string plan, CancellationToken cancel = default) => Task.FromResult(new CheckoutLink(CheckoutUrl));

        public Task<PlusStatus> CancelAsync(CancellationToken cancel = default) => Task.FromResult(Status with { CancelAtPeriodEnd = true });

        public Task<IReadOnlyList<FeedbackItem>> FeedbackAsync(CancellationToken cancel = default) => Task.FromResult<IReadOnlyList<FeedbackItem>>(Feedback);

        public Task SetFeedbackAsync(FeedbackItem item, CancellationToken cancel = default)
        {
            if (Fail) throw new HttpRequestException("down");
            Feedback.RemoveAll(f => f.ContentId == item.ContentId);
            Feedback.Add(item);
            return Task.CompletedTask;
        }

        public Task ClearFeedbackAsync(FeedbackItem item, CancellationToken cancel = default)
        {
            if (Fail) throw new HttpRequestException("down");
            Feedback.RemoveAll(f => f.ContentId == item.ContentId);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<HistoryItem> Items, bool Truncated)> HistoryAsync(CancellationToken cancel = default) => Task.FromResult<(IReadOnlyList<HistoryItem>, bool)>((History, false));
    }

    private static FeedbackItem Rated(string id, string feedback, string updated = "2026-10-01T00:00:00.000Z", string? deleted = null) =>
        new("main", "com.linvo.cinemeta", id, "MOVIE", "Title " + id, feedback, updated, deleted);

    [Fact]
    public void StatusIsReadWithDefaultsForWhatIsMissing()
    {
        var s = PlusStatus.Parse("""{"active":true,"plan":"yearly","validUntil":"2027-01-01T00:00:00.000Z","paywall":true,"trialDays":7}""");
        Assert.True(s.Active);
        Assert.Equal("yearly", s.Plan);
        Assert.True(s.Paywall);
        Assert.False(s.CancelAtPeriodEnd);
        Assert.Equal(7, s.TrialDays);
        Assert.False(PlusStatus.Parse("{}").Active);
        Assert.Throws<System.Text.Json.JsonException>(() => PlusStatus.Parse("[]"));
    }

    [Fact]
    public void PlansAreNamed()
    {
        Assert.Equal("Yearly", PlusPlan.LabelFor("yearly"));
        Assert.Null(PlusPlan.LabelFor("other"));
        Assert.Equal(3, PlusPlan.All.Count);
    }

    [Fact]
    public void FeedbackSaysWhatItIs()
    {
        Assert.Equal(Rating.Like, Rated("a", "like").Rating);
        Assert.Equal(Rating.Dislike, Rated("a", "DISLIKE").Rating);
        Assert.Null(Rated("a", "meh").Rating);
    }

    [Fact]
    public void TheSummaryCountsTheWayTheEngineDoes()
    {
        var ratings = new Dictionary<string, Rating> { ["a"] = Rating.Like, ["b"] = Rating.Dislike };
        WatchlistItem Item(string id, bool? watched) => new("p", id, "MOVIE", id, Watched: watched);
        var list = new[] { Item("a", true), Item("c", true), Item("d", false), Item("e", null) }; // "a" is rated, so counts only as a like
        var s = RecommendationSummary.Of(ratings, list);
        Assert.Equal((1, 1, 1, 2), (s.Likes, s.Dislikes, s.Finished, s.Saved));
        Assert.Equal(5, s.LikePoints);
        Assert.Equal(-5, s.DislikePoints);
        Assert.Equal(2 + 2, s.ListPoints);
        Assert.True(s.Ready);
        Assert.False(new RecommendationSummary(0, 2, 0, 0).Ready); // nothing positive
        Assert.False(new RecommendationSummary(1, 0, 0, 0).Ready); // too few
        Assert.True(new RecommendationSummary(30, 30, 1, 0).OverLimit);
    }

    [Fact]
    public void PointsAreWrittenWithASign()
    {
        Assert.Equal("+20 points", RecommendationSummary.PointsLabel(20));
        Assert.Equal("−15 points", RecommendationSummary.PointsLabel(-15));
        Assert.Equal("0 points", RecommendationSummary.PointsLabel(0));
        Assert.Equal("+1 point", RecommendationSummary.PointsLabel(1));
    }

    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static HistoryItem Movie(string id, bool done, long position, long duration, string at) => new("p", id, "MOVIE", id, position, duration, done, at);

    [Fact]
    public void StatsAreCountedFromTheHistory()
    {
        var stats = WatchStats.Compute(
        [
            Movie("a", true, 0, 7_200_000, "2026-10-13T12:00:00.000Z"),
            new HistoryItem("p", "s1", "TV_SHOW", "Show", 0, 3_000_000, true, "2026-10-14T11:00:00.000Z", 1, 1),
            Movie("c", false, 1_800_000, 6_000_000, "2026-10-04T12:00:00.000Z"),
        ], Now, TimeZoneInfo.Utc);
        Assert.False(stats.IsEmpty);
        Assert.Equal(12_000_000, stats.TotalMs);
        Assert.Equal(10_200_000, stats.Last7DaysMs);
        Assert.Equal(1_800_000, stats.Prev7DaysMs);
        Assert.Equal(12_000_000, stats.Last30DaysMs);
        Assert.Equal(9_000_000, stats.MovieMs);
        Assert.Equal(3_000_000, stats.ShowMs);
        Assert.Equal(3, stats.ActiveDays);
        Assert.Equal(4_000_000, stats.AvgPerActiveDayMs);
        Assert.Equal((1, 1, 1, 3), (stats.MoviesFinished, stats.EpisodesWatched, stats.ShowsWatched, stats.TitlesWatched));
        Assert.Equal(2, stats.StreakDays);
        Assert.Equal(2, stats.LongestStreakDays);
        Assert.Equal(1, stats.BusiestWeekday); // Tuesday
        Assert.Equal(3_000_000, stats.DailyMs[^1]);
        Assert.Equal(7_200_000, stats.DailyMs[^2]);
        Assert.Equal(1_800_000, stats.DailyMs[WatchStats.HeatDays - 1 - 10]);
    }

    [Fact]
    public void NoHistoryIsEmpty()
    {
        Assert.True(WatchStats.Compute([], Now, TimeZoneInfo.Utc).IsEmpty);
        Assert.True(WatchStats.Compute([Movie("a", true, 0, 1, "not a date")], Now, TimeZoneInfo.Utc).IsEmpty);
    }

    [Fact]
    public void AStreakStillCountsWhenTodayIsNotWatchedYet()
    {
        var stats = WatchStats.Compute([Movie("a", true, 0, 1000, "2026-10-13T12:00:00.000Z"), Movie("b", true, 0, 1000, "2026-10-12T12:00:00.000Z")], Now, TimeZoneInfo.Utc);
        Assert.Equal(2, stats.StreakDays);
    }

    [Fact]
    public void BigNumbersAreWrittenForPeople()
    {
        Assert.Equal("0 minutes", WatchStats.Duration(0));
        Assert.Equal("under a minute", WatchStats.Duration(30_000));
        Assert.Equal("40 minutes", WatchStats.Duration(40 * 60_000L));
        Assert.Equal("1 hour 5 minutes", WatchStats.Duration(65 * 60_000L));
        Assert.Equal("25 hours", WatchStats.Duration((25 * 60 + 30) * 60_000L));
        Assert.Equal("", WatchStats.Short(0));
        Assert.Equal("45m", WatchStats.Short(45 * 60_000L));
        Assert.Equal("3h", WatchStats.Short(3 * 3_600_000L));
        Assert.Equal("2.5h", WatchStats.Short((long)(2.5 * 3_600_000)));
        Assert.Equal("13h", WatchStats.Short((long)(12.5 * 3_600_000)));
    }

    [Fact]
    public void ThisWeekIsComparedWithTheLast()
    {
        Assert.Null(WatchStats.WeekChange(100, 0));
        Assert.Equal(("up", 467), WatchStats.WeekChange(10_200_000, 1_800_000));
        Assert.Equal(("down", 50), WatchStats.WeekChange(50, 100));
        Assert.Equal(("same", 0), WatchStats.WeekChange(100, 100));
    }

    [Fact]
    public void TheActivityGridStartsOnAMondayAndLevelsByTheBusiestDay()
    {
        var daily = new long[WatchStats.HeatDays];
        daily[^1] = 100;
        daily[^2] = 25;
        var cells = WatchStats.HeatCells(daily, new DateTime(2026, 10, 14));
        Assert.Equal(3, cells.Count(c => c.Ms is null)); // 16 July 2026 is a Thursday: three blanks before it
        Assert.Equal(WatchStats.HeatDays + 3, cells.Count);
        Assert.Equal(4, cells[^1].Level);
        Assert.Equal(1, cells[^2].Level);
        Assert.Equal(0, cells[^3].Level);
    }

    [Fact]
    public async Task RatingsKeepTheLikedAndNotForMeAndLeaveOutDeletedOnes()
    {
        var store = new FakeStore { Feedback = [Rated("a", "like"), Rated("b", "dislike"), Rated("c", "like", deleted: "2026-10-02T00:00:00.000Z"), Rated("d", "bogus")] };
        var model = new PlusModel(store);
        await model.LoadRatingsAsync();
        Assert.Equal(["a", "b"], model.Ratings.Keys.OrderBy(k => k));
        Assert.True(model.RatingsLoaded);
    }

    [Fact]
    public async Task PostersAreLookedUpOnceForEveryRatedTitleAndAFailureLeavesOneOut()
    {
        var store = new FakeStore { Feedback = [Rated("a", "like"), Rated("b", "dislike"), Rated("c", "like")] };
        var model = new PlusModel(store);
        await model.LoadRatingsAsync();
        var asked = new List<string>();
        Task<string?> Lookup(FeedbackItem item, CancellationToken _)
        {
            lock (asked) asked.Add(item.ContentId);
            return item.ContentId == "b" ? throw new HttpRequestException("no") : Task.FromResult<string?>("https://img.test/" + item.ContentId);
        }

        await model.LoadPostersAsync(Lookup);
        Assert.Equal(["a", "c"], model.Posters.Keys.OrderBy(k => k));
        Assert.Equal("https://img.test/a", model.Posters["a"]);
        asked.Clear();
        await model.LoadPostersAsync(Lookup);
        Assert.Equal(["b"], asked); // only the one still without a poster is tried again
    }

    [Fact]
    public async Task RemovingARatingIsShownAtOnceAndPutBackOnFailure()
    {
        var store = new FakeStore { Feedback = [Rated("a", "like")] };
        var model = new PlusModel(store);
        await model.LoadRatingsAsync();
        store.Fail = true;
        await model.RemoveRatingAsync(model.Ratings["a"]);
        Assert.Contains("a", model.Ratings.Keys);
        store.Fail = false;
        await model.RemoveRatingAsync(model.Ratings["a"]);
        Assert.Empty(model.Ratings);
        Assert.Empty(store.Feedback);
    }

    [Fact]
    public async Task LikingATitleSavesItAndTheSameAgainTakesItOff()
    {
        var store = new FakeStore();
        var model = new PlusModel(store);
        await model.LoadStatusAsync();
        await model.RateAsync("tt1", "movie", "A Film", "https://img.test/a.jpg", Rating.Like);
        Assert.Equal(Rating.Like, model.RatingOf("tt1"));
        Assert.Equal("MOVIE", store.Feedback.Single().ContentType);
        Assert.Equal("like", store.Feedback.Single().Feedback);
        Assert.Equal("https://img.test/a.jpg", model.Posters["tt1"]);
        await model.RateAsync("tt1", "movie", "A Film", null, Rating.Like);
        Assert.Null(model.RatingOf("tt1"));
        Assert.Empty(store.Feedback);
    }

    [Fact]
    public async Task ChoosingTheOtherRatingChangesIt()
    {
        var store = new FakeStore();
        var model = new PlusModel(store);
        await model.LoadStatusAsync();
        await model.RateAsync("tt2", "series", "A Show", null, Rating.Like);
        await model.RateAsync("tt2", "series", "A Show", null, Rating.Dislike);
        Assert.Equal(Rating.Dislike, model.RatingOf("tt2"));
        Assert.Equal("TV_SHOW", store.Feedback.Single().ContentType);
        Assert.Equal("dislike", store.Feedback.Single().Feedback);
    }

    [Fact]
    public async Task ARatingThatDoesNotSaveIsPutBackAndWithoutPlusNothingHappens()
    {
        var store = new FakeStore { Fail = false };
        var model = new PlusModel(store);
        await model.RateAsync("tt1", "movie", "A Film", null, Rating.Like); // status not read yet: no Plus
        Assert.Null(model.RatingOf("tt1"));
        await model.LoadStatusAsync();
        store.Fail = true;
        await model.RateAsync("tt1", "movie", "A Film", "https://img.test/a.jpg", Rating.Like);
        Assert.Null(model.RatingOf("tt1"));
        Assert.Empty(model.Posters);
    }

    [Fact]
    public async Task ResetClearsEveryRating()
    {
        var store = new FakeStore { Feedback = [Rated("a", "like"), Rated("b", "dislike")] };
        var model = new PlusModel(store);
        await model.LoadRatingsAsync();
        await model.ResetRatingsAsync();
        Assert.Empty(model.Ratings);
        Assert.Empty(store.Feedback);
    }

    [Fact]
    public async Task StatusIsKnownOnlyOnceTheAccountHasAnswered()
    {
        var store = new FakeStore { Fail = true };
        var model = new PlusModel(store);
        await model.LoadStatusAsync();
        Assert.False(model.StatusKnown);
        store.Fail = false;
        await model.LoadStatusAsync();
        Assert.True(model.StatusKnown);
        Assert.True(model.Active);
    }

    [Fact]
    public async Task StatsComeFromTheHistoryAndAFailureIsReported()
    {
        var store = new FakeStore { History = [Movie("a", true, 0, 60_000, "2026-10-14T11:00:00.000Z")] };
        var model = new PlusModel(store, () => Now);
        await model.LoadStatsAsync();
        Assert.False(model.StatsFailed);
        Assert.Equal(60_000, model.Stats!.TotalMs);
    }

    [Fact]
    public async Task CheckoutOnlyOpensAnHttpsAddressAndThenWatchesForThePayment()
    {
        var store = new FakeStore { Status = PlusStatus.None with { Paywall = true }, ActiveAfter = 3 };
        var model = new PlusModel(store, pollEvery: TimeSpan.FromMilliseconds(5));
        Assert.Equal("https://checkout.stripe.test/pay", await model.StartCheckoutAsync("monthly"));
        for (var i = 0; i < 200 && !model.Active; i++) await Task.Delay(10);
        Assert.True(model.Active);

        store.CheckoutUrl = "http://not-secure.test/";
        await Assert.ThrowsAsync<ApiException>(() => model.StartCheckoutAsync("monthly"));
        model.StopWatching();
    }

    [Fact]
    public async Task CancellingKeepsPlusUntilThePeriodEnds()
    {
        var model = new PlusModel(new FakeStore());
        await model.CancelSubscriptionAsync();
        Assert.True(model.Status.CancelAtPeriodEnd);
        Assert.True(model.Active);
    }

    [Fact]
    public void ProblemsAreExplained()
    {
        Assert.Equal("You already have Plus for life.", PlusModel.CheckoutProblem(ApiException.Server(409, null)));
        Assert.Equal("Plus checkout isn't available yet.", PlusModel.CheckoutProblem(ApiException.Server(503, null)));
        Assert.Equal("Lifetime Plus has no subscription to cancel.", PlusModel.CancelProblem(ApiException.Server(409, null)));
        Assert.StartsWith("Couldn't cancel", PlusModel.CancelProblem(new HttpRequestException()));
    }
}
