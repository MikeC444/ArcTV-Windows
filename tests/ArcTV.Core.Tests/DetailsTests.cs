using System.Net;
using System.Text.Json;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Details;
using ArcTV.Core.MyList;
using Xunit;

namespace ArcTV.Core.Tests;

public class DetailsTests
{
    private static MetaDetail Parse(string json) => JsonSerializer.Deserialize<MetaDetail>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void FormatsLikeTheMacApp()
    {
        Assert.Equal("2h 17m", Format.Runtime(137));
        Assert.Equal("49m", Format.Runtime(49));
        Assert.Equal("1h 0m", Format.Runtime(60));
        Assert.Equal(142, Format.RuntimeMinutes("142 min"));
        Assert.Equal(142, Format.RuntimeMinutes("2h 22min"));
        Assert.Null(Format.RuntimeMinutes(null));
        Assert.Equal("Mar 5, 2024", Format.ReleaseDate("2024-03-05"));
        Assert.Equal("Mar 5, 2024", Format.ReleaseDate("2024-03-05T00:00:00.000Z"));
        Assert.Null(Format.ReleaseDate("soon"));
    }

    [Fact]
    public void CinemetaCastWithPhotosComesFirstThenTheRemainingNames()
    {
        var meta = Parse("""{"id":"tt1","type":"movie","name":"One","cast":["A","B","C"],"app_extras":{"cast":[{"name":"B","character":"Bee","photo":"b.jpg"}]}}""");
        Assert.Equal(new[] { new CastMember("B", "Bee", "b.jpg"), new CastMember("A"), new CastMember("C") }, meta.CastMembers);
    }

    [Fact]
    public void EpisodesAreGroupedBySeasonInOrderAndSpecialsAreLeftOut()
    {
        var meta = Parse("""
            {"id":"tt2","type":"series","name":"Show","releaseInfo":"2019-2024","videos":[
              {"id":"tt2:1:2","season":1,"episode":2,"name":"Two"},
              {"id":"tt2:1:1","season":1,"episode":1,"title":"One"},
              {"id":"tt2:2:1","season":2,"episode":1},
              {"id":"tt2:0:1","season":0,"episode":1,"name":"Special"},
              {"id":"tt2:x","name":"No numbers"}]}
            """);
        Assert.Equal(new[] { 1, 2 }, meta.SeasonNumbers);
        Assert.Equal(new[] { "One", "Two" }, meta.Seasons[1].Select(e => e.DisplayTitle));
        Assert.Equal("Episode 1", meta.Seasons[2][0].DisplayTitle);
        Assert.Equal(2019, meta.Year);
    }

    [Fact]
    public void StreamIdsAndMetaAddressesKeepTheirColons()
    {
        Assert.Equal("tt1:2:3", StreamIds.For("tt1", 2, 3));
        Assert.Equal("tt1", StreamIds.For("tt1", null, null));
        Assert.Equal("https://x.test/meta/series/tt1:2:3.json", AddonUrl.MetaUrl("https://x.test/manifest.json", "series", "tt1:2:3"));
        Assert.Equal("https://x.test/meta/movie/a%20b.json", AddonUrl.MetaUrl("https://x.test/manifest.json", "movie", "a b"));
    }

    private static AddonClient Client(string body) => new(new HttpClient(new FakeHandler((_, _) => (HttpStatusCode.OK, body))));

    [Fact]
    public async Task ModelStartsWithTheCardThenAddsTheFullDetailsAndKeepsWhatTheyLeaveOut()
    {
        var card = new MetaPreview("tt1", "movie", "One", Poster: "p.jpg", Description: "from the card", ImdbRating: "7.0");
        var model = new DetailModel(card, Client("""{"meta":{"id":"tt1","type":"movie","name":"One","cast":["A"],"imdbRating":"8.1"}}"""));
        Assert.False(model.Loaded);
        Assert.Equal("from the card", model.Detail.Description);

        await model.LoadAsync();
        Assert.True(model.Loaded);
        Assert.Equal("8.1", model.Detail.ImdbRating); // the full answer wins
        Assert.Equal("from the card", model.Detail.Description); // what it lacks comes from the card
        Assert.Equal("p.jpg", model.Detail.Poster);
        Assert.Equal(new[] { "A" }, model.Cast.Select(c => c.Name));
    }

    [Fact]
    public async Task ModelKeepsTheCardWhenTheAddonHasNothing()
    {
        var model = new DetailModel(new MetaPreview("tt1", "movie", "One"), Client("not json"));
        await model.LoadAsync();
        Assert.True(model.Loaded);
        Assert.Equal("One", model.Detail.Name);
    }

    [Fact]
    public async Task ChoosingASeasonChangesTheEpisodes()
    {
        var model = new DetailModel(new MetaPreview("tt2", "series", "Show"), Client("""{"meta":{"id":"tt2","type":"series","name":"Show","videos":[{"id":"a","season":1,"episode":1},{"id":"b","season":2,"episode":1},{"id":"c","season":2,"episode":2}]}}"""));
        await model.LoadAsync();
        Assert.Equal(1, model.CurrentSeason);
        Assert.Single(model.Episodes);
        model.ChooseSeason(2);
        Assert.Equal(2, model.Episodes.Count);
        model.ChooseSeason(9); // no such season: back to the first
        Assert.Equal(1, model.CurrentSeason);
    }

    [Fact]
    public async Task MyListTogglesShowAtOnceAndAreUndoneWhenTheAccountCannotBeReached()
    {
        var saved = new List<WatchlistItem>();
        var fail = false;
        var list = new MyListModel(_ => Task.FromResult(new List<WatchlistItem>()),
            item => fail ? throw new HttpRequestException("offline") : Task.Run(() => saved.Add(item)),
            _ => fail ? throw new HttpRequestException("offline") : Task.CompletedTask);
        await list.LoadAsync();
        var title = new MetaDetail("tt1", "movie", "One", ReleaseInfo: "2014", ImdbRating: "8.7");

        await list.ToggleSavedAsync(title);
        Assert.True(list.IsSaved("tt1"));
        Assert.False(list.IsWatched("tt1"));
        Assert.Equal("MOVIE", saved.Single().ContentType);
        Assert.Equal(2014, saved.Single().Year);

        await list.ToggleWatchedAsync(title);
        Assert.True(list.IsWatched("tt1"));
        Assert.Single(list.Items!); // still one entry, now watched

        fail = true;
        await list.ToggleSavedAsync(title); // would remove it, but the account is unreachable
        Assert.True(list.IsSaved("tt1"));
        await list.ToggleWatchedAsync(title);
        Assert.True(list.IsWatched("tt1")); // put back
    }

    private sealed class FakeExtras(string? trailer = "yt123", string? date = "2024-03-05", params TmdbCast[] cast) : IDetailExtras
    {
        public Task<string?> TrailerIdAsync(string title, string type, int? year, CancellationToken cancel = default) => Task.FromResult(trailer);

        public Task<string?> ReleaseDateAsync(string title, int? year, CancellationToken cancel = default) => Task.FromResult(date);

        public Task<IReadOnlyList<TmdbCast>> CastPhotosAsync(string imdbId, string type, CancellationToken cancel = default) => Task.FromResult<IReadOnlyList<TmdbCast>>(cast);
    }

    [Fact]
    public void CastKeepsWhatTheAddonSentAndFillsInPhotosByName()
    {
        var addon = new[] { new CastMember("Zo\u00EB Salda\u00F1a"), new CastMember("A B", "Hero", "keep.jpg") };
        var tmdb = new[] { new TmdbCast("Zoe Saldana", "Neytiri", "zoe.jpg"), new TmdbCast("A B", "Other", "other.jpg") };
        var merged = CastMerge.Merge(addon, tmdb);
        Assert.Equal(new CastMember("Zo\u00EB Salda\u00F1a", "Neytiri", "zoe.jpg"), merged[0]);
        Assert.Equal(new CastMember("A B", "Hero", "keep.jpg"), merged[1]);
        Assert.Equal(new[] { "Zoe Saldana", "A B" }, CastMerge.Merge([], tmdb).Select(m => m.Name));
        Assert.Equal(addon, CastMerge.Merge(addon, []));
    }

    [Fact]
    public async Task ModelAddsTheTrailerReleaseDateAndCastPhotosFromTheAccount()
    {
        var card = new MetaPreview("tt0816692", "movie", "Interstellar", ReleaseInfo: "2014", Runtime: "169 min");
        var model = new DetailModel(card, Client("""{"meta":{"id":"tt0816692","type":"movie","name":"Interstellar","releaseInfo":"2014","runtime":"169 min","cast":["Matthew McConaughey"]}}"""),
            new FakeExtras(cast: new TmdbCast("Matthew McConaughey", "Cooper", "m.jpg")));
        Assert.Equal("2014   2h 49m", model.Facts);

        await model.LoadAsync();
        Assert.Equal("yt123", model.TrailerId);
        Assert.Equal("Mar 5, 2024   2h 49m", model.Facts);
        Assert.Equal(new CastMember("Matthew McConaughey", "Cooper", "m.jpg"), model.Cast.Single());
    }

    [Fact]
    public async Task ASeriesKeepsItsYearAndNeverAsksForAReleaseDate()
    {
        var model = new DetailModel(new MetaPreview("tt2", "series", "Show", ReleaseInfo: "2019-2024"), Client("""{"meta":{"id":"tt2","type":"series","name":"Show","releaseInfo":"2019-2024"}}"""), new FakeExtras());
        await model.LoadAsync();
        Assert.Equal("2019", model.Facts);
    }

    [Fact]
    public async Task ModelStaysQuietWhenTheAccountHasNothing()
    {
        var model = new DetailModel(new MetaPreview("tt1", "movie", "One", ReleaseInfo: "2014"), Client("""{"meta":{"id":"tt1","type":"movie","name":"One","releaseInfo":"2014"}}"""), new FakeExtras(null, null));
        await model.LoadAsync();
        Assert.Null(model.TrailerId);
        Assert.Equal("2014", model.Facts);
    }

    [Fact]
    public void SimilarTitlesPreferTheSameGenreAndLeaveOutTheTitleItself()
    {
        var title = new MetaDetail("a", "movie", "A", Genres: ["Drama"]);
        var pool = new[]
        {
            new MetaPreview("a", "movie", "A", Genres: ["Drama"]), new MetaPreview("b", "movie", "B", Genres: ["drama"]),
            new MetaPreview("c", "movie", "C", Genres: ["Comedy"]), new MetaPreview("b", "movie", "B again", Genres: ["Drama"]),
        };
        Assert.Equal(new[] { "b" }, SimilarTitles.For(title, pool).Select(i => i.Id));
        Assert.Equal(new[] { "b", "c" }, SimilarTitles.For(new MetaDetail("a", "movie", "A", Genres: ["Horror"]), pool).Select(i => i.Id)); // none share one: anything else
    }
}
