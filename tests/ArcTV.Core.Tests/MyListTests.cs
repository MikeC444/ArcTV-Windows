using System.Net;
using System.Text.Json;
using ArcTV.Core.Account;
using ArcTV.Core.MyList;
using Xunit;

namespace ArcTV.Core.Tests;

public class MyListTests
{
    private static WatchlistItem Item(string title, double? rating = null, int? year = null, bool? watched = null) =>
        new("p", title, "MOVIE", title, Year: year, Rating: rating, Watched: watched, UpdatedAt: "2026-01-01T00:00:00.000Z");

    [Fact]
    public void ListSortsOrderTitlesTheWayTheirNamesSay()
    {
        // the account sends the oldest first: "beta" was added first and "gamma" last
        var items = new[] { Item("beta", 7, 2020), Item("Alpha", 9, 1999), Item("gamma", null, 2024) };
        Assert.Equal(new[] { "gamma", "Alpha", "beta" }, ListSort.Recent.Apply(items).Select(i => i.Title));
        Assert.Equal(new[] { "Alpha", "beta", "gamma" }, ListSort.Title.Apply(items).Select(i => i.Title));
        Assert.Equal(new[] { "Alpha", "beta", "gamma" }, ListSort.HighestRated.Apply(items).Select(i => i.Title));
        Assert.Equal(new[] { "gamma", "beta", "Alpha" }, ListSort.Newest.Apply(items).Select(i => i.Title));

        var tied = new[] { Item("old", 8, 2000), Item("mid", 8, 2000), Item("new", 8, 2000) };
        Assert.Equal(new[] { "new", "mid", "old" }, ListSort.HighestRated.Apply(tied).Select(i => i.Title));
    }

    [Fact]
    public void WatchlistDecodesAndBecomesPosterCards()
    {
        const string json = """
            {"items":[{"providerId":"com.linvo.cinemeta","contentId":"tt0816692","contentType":"MOVIE","title":"Interstellar","posterUrl":"https://example.com/p.jpg","year":2014,"rating":8.7,"watched":true,"updatedAt":"2026-10-01T00:00:00.000Z"},
                      {"providerId":"p","contentId":"tt2","contentType":"TV_SHOW","title":"A Show","updatedAt":"2026-10-02T00:00:00.000Z"}]}
            """;
        var items = JsonSerializer.Deserialize<WatchlistResponse>(json, ApiHttp.Json)!.Items!;
        Assert.Equal(2, items.Count);
        var movie = items[0].AsPreview();
        Assert.Equal("movie", movie.Type);
        Assert.Equal("2014", movie.ReleaseInfo);
        Assert.Equal("8.7", movie.ImdbRating);
        Assert.True(movie.Watched);
        var show = items[1].AsPreview();
        Assert.Equal("series", show.Type);
        Assert.False(show.Watched);
    }

    [Fact]
    public async Task ModelKeepsTheListWhileItLoadsAgainAndSaysWhyItFailed()
    {
        var fail = false;
        var model = new MyListModel(_ => fail ? throw ApiException.Server(500, "The server is having a moment.") : Task.FromResult(new List<WatchlistItem> { Item("a") }));
        Assert.Null(model.Items);

        await model.LoadAsync();
        Assert.Single(model.Items!);
        Assert.Null(model.Error);

        fail = true;
        await model.LoadAsync();
        Assert.Single(model.Items!); // the earlier list stays
        Assert.Equal("The server is having a moment.", model.Error);
    }

    [Fact]
    public async Task ANetworkFailureGetsAPlainMessage()
    {
        var model = new MyListModel(_ => throw new HttpRequestException("no network"));
        await model.LoadAsync();
        Assert.Null(model.Items);
        Assert.Equal("Couldn't load your list. Check your connection.", model.Error);
    }
}
