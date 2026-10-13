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
}
