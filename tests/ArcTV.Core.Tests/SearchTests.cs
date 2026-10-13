using System.Net;
using System.Text.Json;
using ArcTV.Core.Addons;
using ArcTV.Core.Home;
using ArcTV.Core.Search;
using ArcTV.Core.Storage;
using Xunit;

namespace ArcTV.Core.Tests;

public class SearchTests
{
    private static HomeSource Source(string manifest) => new("https://x.test/manifest.json", JsonSerializer.Deserialize<AddonManifest>(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web))!);

    private const string Searchable = """
        {"id":"s","name":"S","catalogs":[
          {"type":"movie","id":"top","extra":[{"name":"search"}]},
          {"type":"series","id":"top","extra":[{"name":"search"}]}]}
        """;

    private const string BaseOnly = """{"id":"b","name":"B","catalogs":[{"type":"movie","id":"all"}]}""";

    private static AddonClient Client(Func<string, string> answer, List<string>? calls = null) =>
        new(new HttpClient(new FakeHandler((request, _) =>
        {
            calls?.Add(request.RequestUri!.AbsolutePath);
            return (HttpStatusCode.OK, answer(request.RequestUri!.AbsolutePath));
        })));

    private static string Metas(params (string Id, string Type, string Name)[] items) =>
        "{\"metas\":[" + string.Join(",", items.Select(i => $"{{\"id\":\"{i.Id}\",\"type\":\"{i.Type}\",\"name\":\"{i.Name}\"}}")) + "]}";

    [Fact]
    public void RecentSearchesPutTheNewestFirstAndDoNotDouble()
    {
        var list = new List<string>();
        foreach (var term in new[] { "alien", "  sinners ", "ALIEN", "it" }) list = RecentSearches.Adding(term, list);
        Assert.Equal(new[] { "it", "ALIEN", "sinners" }, list);
        Assert.Equal(list, RecentSearches.Adding("   ", list));
        var many = new List<string>();
        for (var n = 1; n <= 12; n++) many = RecentSearches.Adding($"t{n}", many);
        Assert.Equal(RecentSearches.Limit, many.Count);
        Assert.Equal("t12", many[0]);
    }

    [Fact]
    public async Task AnAddonWithASearchCatalogueIsAskedForRealAndMoviesAndShowsAreSplit()
    {
        var calls = new List<string>();
        var client = Client(path => path.Contains("/movie/") ? Metas(("m1", "movie", "Alien")) : Metas(("s1", "series", "Alien: Earth")), calls);
        var result = await CatalogSearch.SearchAsync([Source(Searchable)], "alien", client);
        Assert.Equal(new[] { "m1" }, result.Movies.Select(i => i.Id));
        Assert.Equal(new[] { "s1" }, result.TvShows.Select(i => i.Id));
        Assert.Equal(0, result.Pending);
        Assert.Equal(0, result.Failed);
        Assert.All(calls, path => Assert.Contains("search=alien", path));
    }

    [Fact]
    public async Task AnAddonWithoutSearchHasItsBaseCataloguesFilteredByTitle()
    {
        var client = Client(_ => Metas(("a", "movie", "Alien"), ("b", "movie", "Heat"), ("c", "movie", "Aliens")));
        var result = await CatalogSearch.SearchAsync([Source(BaseOnly)], "ALIEN", client);
        Assert.Equal(new[] { "a", "c" }, result.Movies.Select(i => i.Id));
    }

    [Fact]
    public async Task ASlowAddonIsLeftBehindAndCounted()
    {
        var client = new AddonClient(new HttpClient(new HangHandler()));
        var result = await CatalogSearch.SearchAsync([Source(Searchable)], "alien", client, timeout: TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Pending);
        Assert.Empty(result.Movies);
    }

    private sealed class HangHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static SearchModel Model(IReadOnlyList<HomeSource> sources, AddonClient client, RecentSearchStore? store = null) =>
        new("u1", _ => Task.FromResult(sources), store, client);

    [Fact]
    public async Task ModelShowsResultsThenNothingFound()
    {
        var model = Model([Source(Searchable)], Client(path => path.Contains("alien") ? Metas(("m1", "movie", "Alien")) : """{"metas":[]}"""));
        await model.SearchAsync("alien");
        Assert.Equal(SearchState.Results, model.State);
        Assert.Single(model.Movies);

        await model.SearchAsync("zzz");
        Assert.Equal(SearchState.None, model.State);
        Assert.Equal("zzz", model.Query);
    }

    [Fact]
    public async Task ModelWithNoAddonsFindsNothing()
    {
        var model = Model([], Client(_ => ""));
        await model.SearchAsync("alien");
        Assert.Equal(SearchState.None, model.State);
    }

    [Fact]
    public void ResetGoesBackToTheEmptyPage()
    {
        var model = Model([], Client(_ => ""));
        model.Reset();
        Assert.Equal(SearchState.Idle, model.State);
        Assert.Empty(model.Movies);
    }

    [Fact]
    public void RecentSearchesAreKeptPerAccountOnDisk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "arctv-recents-" + Guid.NewGuid());
        var store = new RecentSearchStore(Path.Combine(folder, "recent-searches.json"));
        var model = new SearchModel("u1", _ => Task.FromResult<IReadOnlyList<HomeSource>>([]), store);
        model.Remember("alien");
        model.Remember("heat");
        Assert.Equal(new[] { "heat", "alien" }, new SearchModel("u1", _ => Task.FromResult<IReadOnlyList<HomeSource>>([]), store).Recents);
        Assert.Empty(new SearchModel("someone-else", _ => Task.FromResult<IReadOnlyList<HomeSource>>([]), store).Recents);

        model.Forget("heat");
        Assert.Equal(new[] { "alien" }, new SearchModel("u1", _ => Task.FromResult<IReadOnlyList<HomeSource>>([]), store).Recents);
        model.ClearRecents();
        Assert.Empty(new SearchModel("u1", _ => Task.FromResult<IReadOnlyList<HomeSource>>([]), store).Recents);
        Directory.Delete(folder, true);
    }
}
