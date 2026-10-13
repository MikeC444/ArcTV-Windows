using System.Net;
using System.Text.Json;
using ArcTV.Core.Addons;
using ArcTV.Core.Browse;
using ArcTV.Core.Home;
using Xunit;

namespace ArcTV.Core.Tests;

public class BrowseTests
{
    private static MetaPreview Meta(string id, string? rating = null, string? year = null) => new(id, "movie", id, ReleaseInfo: year, ImdbRating: rating);

    [Fact]
    public void SortsLikeTheWebApp()
    {
        var items = new[] { Meta("a", "6.0", "2020"), Meta("b", "8.5", "2018"), Meta("c", null, "2024") };
        Assert.Equal(new[] { "a", "b", "c" }, BrowseSort.Featured.Apply(items).Select(i => i.Id));
        Assert.Equal(new[] { "b", "a", "c" }, BrowseSort.HighestRated.Apply(items).Select(i => i.Id));
        Assert.Equal(new[] { "c", "a", "b" }, BrowseSort.Newest.Apply(items).Select(i => i.Id));
    }

    [Fact]
    public void YearComesFromTheStartOfReleaseInfo()
    {
        Assert.Equal(2019, Meta("a", null, "2019-2024").Year());
        Assert.Null(Meta("a", null, "TBA").Year());
        Assert.Null(Meta("a", null, null).Year());
    }

    [Fact]
    public void InterleaveAndDistinct()
    {
        var movies = new List<MetaPreview> { Meta("m1"), Meta("m2"), Meta("dup") };
        var shows = new List<MetaPreview> { Meta("s1"), Meta("dup") };
        var merged = HomeSections.Interleaved([movies, shows]);
        Assert.Equal(new[] { "m1", "s1", "m2", "dup", "dup" }, merged.Select(i => i.Id));
        Assert.Equal(new[] { "m1", "s1", "m2", "dup" }, HomeSections.DistinctById(merged).Select(i => i.Id));
    }

    [Fact]
    public void ShowsHaveThreeMoreGenresThanMovies()
    {
        Assert.Equal(Genres.Movies.Count + 3, Genres.Shows.Count);
        Assert.Contains("Talk-Show", Genres.Shows);
        Assert.DoesNotContain("Talk-Show", Genres.Movies);
    }

    private const string Manifest = """
        {"id":"cine","name":"Cine","catalogs":[
          {"type":"movie","id":"top"},
          {"type":"movie","id":"byGenre","extra":[{"name":"genre","isRequired":true,"options":["Drama","Comedy"]},{"name":"skip"}]},
          {"type":"series","id":"top"}]}
        """;

    private static HomeSource Source() => new("https://x.test/manifest.json", JsonSerializer.Deserialize<AddonManifest>(Manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web))!);

    private static (AddonClient Client, List<string> Calls) Client(Func<string, string>? answer = null)
    {
        var calls = new List<string>();
        var handler = new FakeHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            calls.Add(path);
            return (HttpStatusCode.OK, (answer ?? (_ => """{"metas":[{"id":"a","type":"movie","name":"A"}]}"""))(path));
        });
        return (new AddonClient(new HttpClient(handler)), calls);
    }

    [Fact]
    public async Task WithoutAGenreOnlyCataloguesThatNeedNoGenreAreAsked()
    {
        var (client, calls) = Client();
        var found = await Source().BrowsePageAsync("movie", null, 0, client);
        Assert.Equal(new[] { "a" }, found.Select(i => i.Id));
        Assert.Equal(new[] { "/catalog/movie/top.json" }, calls);
    }

    [Fact]
    public async Task AGenreAsksOnlyCataloguesThatList_ItAndLaterPagesSkip()
    {
        var (client, calls) = Client();
        await Source().BrowsePageAsync("movie", "Drama", 2, client);
        Assert.Equal(new[] { "/catalog/movie/byGenre/genre=Drama&skip=200.json" }, calls);
        calls.Clear();
        Assert.Empty(await Source().BrowsePageAsync("movie", "Horror", 0, client));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task ModelLoadsAPageThenMoreAndStopsWhenThereIsNothingNew()
    {
        // each page has the same two titles the first time, then a new one, then nothing new
        var page = 0;
        var (client, _) = Client(_ =>
        {
            page++;
            return page switch
            {
                1 => """{"metas":[{"id":"a","type":"movie","name":"A"},{"id":"b","type":"movie","name":"B"}]}""",
                2 => """{"metas":[{"id":"b","type":"movie","name":"B"},{"id":"c","type":"movie","name":"C"}]}""",
                _ => """{"metas":[{"id":"c","type":"movie","name":"C"}]}""",
            };
        });
        var model = new BrowseModel("movie", _ => Task.FromResult<IReadOnlyList<HomeSource>>([Source()]), client, new Random(1));

        await model.ReloadAsync(null);
        Assert.False(model.Loading);
        Assert.Equal(new[] { "a", "b" }, model.Items.Select(i => i.Id).Order());

        await model.LoadMoreAsync();
        Assert.Equal(new[] { "a", "b", "c" }, model.Items.Select(i => i.Id).Order());

        await model.LoadMoreAsync();
        var settled = model.Items.Count;
        await model.LoadMoreAsync();
        Assert.Equal(3, settled);
        Assert.Equal(3, model.Items.Count);
    }

    [Fact]
    public async Task ComingBackToAGenreFindsWhatWasLoadedWithoutAskingAgain()
    {
        var (client, calls) = Client();
        var model = new BrowseModel("movie", _ => Task.FromResult<IReadOnlyList<HomeSource>>([Source()]), client);
        await model.ReloadAsync(null);
        await model.ReloadAsync("Drama");
        var asked = calls.Count;
        await model.ReloadAsync(null);
        Assert.Equal(asked, calls.Count);
        Assert.Equal(new[] { "a" }, model.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task NoAddonsIsSaidPlainly()
    {
        var model = new BrowseModel("movie", _ => Task.FromResult<IReadOnlyList<HomeSource>>([]));
        await model.ReloadAsync(null);
        Assert.True(model.NoAddons);
        Assert.False(model.Loading);
        Assert.Empty(model.Items);
    }
}
