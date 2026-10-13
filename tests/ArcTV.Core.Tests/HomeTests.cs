using System.Net;
using System.Text.Json;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Home;
using Xunit;

namespace ArcTV.Core.Tests;

public class VarietyTests
{
    [Fact]
    public void SeededRandomMatchesTheWebAppsNumbers()
    {
        var random = new SeededRandom(12345);
        Assert.Equal(0.9797282677609473, random.Next(), 12);
        Assert.Equal(0.3067522644996643, random.Next(), 12);
        Assert.Equal(0.484205421525985, random.Next(), 12);
    }

    [Fact]
    public void HashMatchesTheWebApp() => Assert.Equal(2263898998u, HomeVariety.HashSeed("abc|2026-10-13"));

    [Fact]
    public void PickPageIsSteadyAndInRange()
    {
        var first = HomeVariety.PickPage("u|2026-10-13", "x_base", [5, 3, 2]);
        Assert.Equal(first, HomeVariety.PickPage("u|2026-10-13", "x_base", [5, 3, 2]));
        Assert.InRange(first, 0, 2);
    }

    [Fact]
    public void VaryOrderKeepsEveryItemOnce()
    {
        var items = Enumerable.Range(0, 50).ToList();
        var varied = HomeVariety.VaryOrder(items, "seed", "row");
        Assert.Equal(items, varied.OrderBy(x => x).ToList());
        Assert.Equal(varied, HomeVariety.VaryOrder(items, "seed", "row"));
    }

    [Fact]
    public void HomeGenresAreCuratedAndKeepTheAddonsSpelling()
    {
        Assert.Equal(["Action", "sci-fi"], HomeVariety.HomeGenres(["sci-fi", "Western", "Action"]));
        Assert.Equal(["sci-fi"], HomeVariety.HomeGenres(["Western", "sci-fi"]));
    }

    [Fact]
    public void DayStampIsTheLocalDate() => Assert.Equal("2026-01-05", HomeVariety.DayStamp(new DateTime(2026, 1, 5, 23, 59, 0)));
}

public class ManifestTests
{
    private static AddonManifest Parse(string json) => JsonSerializer.Deserialize<AddonManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void ResourcesMayBeNamesOrObjectsAndOddOnesDontBreakIt()
    {
        var manifest = Parse("""{"id":"a","name":"A","resources":["catalog",{"name":"stream","types":["movie"],"idPrefixes":["tt"]},42,{"nope":1}]}""");
        Assert.Equal(4, manifest.Resources!.Count);
        Assert.True(manifest.ProvidesStreams("movie", "tt1"));
        Assert.False(manifest.ProvidesStreams("series", "tt1"));
        Assert.False(manifest.ProvidesStreams("movie", "kitsu:1"));
    }

    [Fact]
    public void AnAddonThatSaysNothingIsAskedForEverything() =>
        Assert.True(Parse("""{"id":"a","name":"A","resources":[{"name":"stream"}]}""").ProvidesStreams("movie", "anything"));

    [Fact]
    public void CatalogHelpers()
    {
        var manifest = Parse("""{"id":"a","name":"A","catalogs":[{"type":"movie","id":"top","extra":[{"name":"genre","isRequired":true,"options":["Action"]},{"name":"skip"},{"name":"search"}]},{"type":"series","id":"top"}]}""");
        var movie = manifest.Catalogs![0];
        Assert.False(movie.IsBase);
        Assert.True(movie.SupportsSkip);
        Assert.True(movie.SupportsSearch);
        Assert.Equal(["Action"], movie.GenreOptions);
        Assert.True(manifest.Catalogs[1].IsBase);
        Assert.True(manifest.OffersCatalog("series", "top"));
    }
}

public class HomeLoadTests
{
    private static MetaPreview M(string id, string type = "movie") => new(id, type, id, $"https://img/{id}.jpg");

    private static string Metas(params string[] ids) => "{\"metas\":[" + string.Join(",", ids.Select(i => $"{{\"id\":\"{i}\",\"type\":\"movie\",\"name\":\"{i}\"}}")) + "]}";

    private static (AddonClient, List<string>) Client(Func<string, string?> answer)
    {
        var calls = new List<string>();
        var handler = new FakeHandler((request, _) =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            lock (calls) calls.Add(url);
            var body = answer(url);
            return body is null ? (HttpStatusCode.InternalServerError, "{}") : (HttpStatusCode.OK, body);
        });
        return (new AddonClient(new HttpClient(handler)), calls);
    }

    private static HomeSource Source(string json) =>
        new("https://x.test/manifest.json", JsonSerializer.Deserialize<AddonManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!);

    [Fact]
    public async Task CinemetaShapedAddonGivesPopularNewTopRatedAndWideAppealGenres()
    {
        const string manifest = """
            {"id":"cm","name":"Cinemeta","catalogs":[
              {"type":"movie","id":"top","name":"Popular","extra":[{"name":"genre","options":["Action","Western","Comedy"]},{"name":"skip"}]},
              {"type":"series","id":"top","name":"Popular","extra":[{"name":"genre","options":["Action"]},{"name":"skip"}]},
              {"type":"movie","id":"year","extra":[{"name":"genre","isRequired":true,"options":["2025","2026"]}]},
              {"type":"movie","id":"imdbRating","extra":[{"name":"genre","options":["Action"]}]}]}
            """;
        var (client, calls) = Client(url => Metas("a", "b", "c"));
        var rows = new List<HomeSection>();
        await HomeSections.LoadAsync(Source(manifest), "seed", batch => { rows.AddRange(batch); return Task.CompletedTask; }, new DateTime(2026, 6, 1), client);
        Assert.Equal(["Popular", "New", "Top rated", "Action", "Comedy"], rows.Select(r => r.Title).ToList());
        Assert.Contains(calls, c => c.Contains("/catalog/movie/year/genre=2026.json") || c.Contains("/catalog/movie/year/genre=2026.json?"));
        Assert.DoesNotContain(calls, c => c.Contains("Western"));
    }

    [Fact]
    public async Task OtherAddonsGetTheirBaseRowAndEveryGenre()
    {
        const string manifest = """{"id":"x","name":"Cool","catalogs":[{"type":"movie","id":"all","extra":[{"name":"genre","options":["Noir","Anime"]}]}]}""";
        var (client, _) = Client(url => url.Contains("genre=Noir") ? Metas("n1") : Metas("p1", "p2"));
        var rows = new List<HomeSection>();
        await HomeSections.LoadAsync(Source(manifest), "s", batch => { rows.AddRange(batch); return Task.CompletedTask; }, client: client);
        Assert.Equal(["Cool", "Noir", "Anime"], rows.Select(r => r.Title).ToList());
        Assert.Equal("x_base", rows[0].Id);
    }

    [Fact]
    public async Task AFailingCatalogJustLeavesNoRow()
    {
        const string manifest = """{"id":"x","name":"Cool","catalogs":[{"type":"movie","id":"all","extra":[{"name":"genre","options":["Noir"]}]}]}""";
        var (client, _) = Client(url => url.Contains("genre=Noir") ? null : Metas("p1"));
        var rows = new List<HomeSection>();
        await HomeSections.LoadAsync(Source(manifest), "s", batch => { rows.AddRange(batch); return Task.CompletedTask; }, client: client);
        Assert.Equal(["Cool"], rows.Select(r => r.Title).ToList());
    }

    [Fact]
    public async Task RowsArriveInBatchesOfFour()
    {
        var genres = string.Join(",", Enumerable.Range(0, 9).Select(i => $"\"G{i}\""));
        var manifest = $$"""{"id":"x","name":"Cool","catalogs":[{"type":"movie","id":"all","extra":[{"name":"genre","options":[{{genres}}]}]}]}""";
        var (client, _) = Client(_ => Metas("p1"));
        var sizes = new List<int>();
        await HomeSections.LoadAsync(Source(manifest), "s", batch => { sizes.Add(batch.Count); return Task.CompletedTask; }, client: client);
        Assert.Equal([4, 4, 2], sizes);
    }

    [Fact]
    public void InterleavedAndDistinct()
    {
        var mixed = HomeSections.Interleaved([[M("m1"), M("m2"), M("m3")], [M("s1", "series")]]);
        Assert.Equal(["m1", "s1", "m2", "m3"], mixed.Select(i => i.Id).ToList());
        Assert.Equal(["a", "b"], HomeSections.DistinctById([M("a"), M("b"), M("a")]).Select(i => i.Id).ToList());
    }

    private static HomeSection Row(string id, string title, params string[] ids) => new(id, title, ids.Select(i => M(i)).ToList());

    [Fact]
    public void DefaultOrderPutsLeadRowsFirstThenKnownNames()
    {
        var ordered = HomeSections.Ordered(
            [Row("x_Western", "Western", "w"), Row("x_Comedy", "Comedy", "c"), Row("x_toprated", "Top rated", "t"), Row("x_base", "Popular", "p"), Row("x_new", "New", "n")],
            new HomeRowPreferences());
        Assert.Equal(["Popular", "New", "Top rated", "Comedy", "Western"], ordered.Select(r => r.Title).ToList());
    }

    [Fact]
    public void ThePersonsOrderComesFirst()
    {
        var ordered = HomeSections.Ordered(
            [Row("x_base", "Popular", "p"), Row("x_Comedy", "Comedy", "c"), Row("x_Action", "Action", "a")],
            new HomeRowPreferences(["x_Comedy"]));
        Assert.Equal(["Comedy", "Popular", "Action"], ordered.Select(r => r.Title).ToList());
    }

    [Fact]
    public void NewRowsAreNotBuriedBelowRowsThePersonChose()
    {
        var ordered = HomeSections.Ordered(
            [Row("x_Comedy", "Comedy", "c"), Row("x_new", "New", "n"), Row("x_Action", "Action", "a")],
            new HomeRowPreferences(["x_Comedy", "x_Action"]));
        Assert.Equal(["New", "Comedy", "Action"], ordered.Select(r => r.Title).ToList());
    }

    [Fact]
    public void ATitleShowsInOneRowOnly()
    {
        var deduped = HomeSections.Deduped([Row("a", "A", "1", "2"), Row("b", "B", "2"), Row("c", "C", "2", "3")]);
        Assert.Equal(["A", "C"], deduped.Select(r => r.Title).ToList());
        Assert.Equal(["3"], deduped[1].Items.Select(i => i.Id).ToList());
    }

    [Fact]
    public void HeroPrefersWideBackdrops()
    {
        var wide = new MetaPreview("w", "movie", "w", "p.jpg", "bg.jpg");
        var poster = new MetaPreview("p", "movie", "p", "p.jpg", "p.jpg");
        var picked = HeroPool.PickTitles([poster, wide], [], 1, new Random(1));
        Assert.Equal("w", picked.Single().Id);
        Assert.Equal(2, HeroPool.PickTitles([wide], [poster], 2, new Random(1)).Count);
    }

    [Fact]
    public async Task AccountAddonsAreSortedAndDisabledOnesHidden()
    {
        const string json = """
            {"items":[{"manifestUrl":"u2","addonId":"b","name":"B","enabled":true,"sortOrder":2,"manifestJson":{"id":"b","name":"B"}},
                      {"manifestUrl":"u1","addonId":"a","name":"A","enabled":true,"sortOrder":1,"manifestJson":null},
                      {"manifestUrl":"u3","addonId":"c","name":"C","enabled":false,"sortOrder":0}]}
            """;
        var client = new AuthClient("https://x.test", http: new HttpClient(new FakeHandler((_, _) => (HttpStatusCode.OK, json))));
        Assert.Equal(["A", "B"], (await client.AddonsAsync("tok")).Select(a => a.Name).ToList());
        Assert.Equal(["C", "A", "B"], (await client.AddonsAsync("tok", includeDisabled: true)).Select(a => a.Name).ToList());
    }
}

public class HomeModelTests
{
    private static string Metas(params string[] ids) => "{\"metas\":[" + string.Join(",", ids.Select(i => $"{{\"id\":\"{i}\",\"type\":\"movie\",\"name\":\"{i}\",\"poster\":\"p\",\"background\":\"b{i}\"}}")) + "]}";

    private static AddonClient Client(Func<string, string?> answer) =>
        new(new HttpClient(new FakeHandler((request, _) => answer(request.RequestUri!.AbsoluteUri) is { } body ? (HttpStatusCode.OK, body) : (HttpStatusCode.NotFound, "{}"))));

    private static SyncedAddon Addon(string json) =>
        new("https://x.test/manifest.json", "x", "X", true, 0, JsonSerializer.Deserialize<AddonManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private const string Cool = """{"id":"x","name":"Cool","catalogs":[{"type":"movie","id":"all","extra":[{"name":"genre","options":["Noir","Anime"]}]}]}""";

    [Fact]
    public async Task LoadsRowsAndPicksTheBanner()
    {
        var model = new HomeModel(_ => Task.FromResult<IReadOnlyList<SyncedAddon>>([Addon(Cool)]), "u", Client(url => url.Contains("Noir") ? Metas("a", "b") : Metas("c", "d", "e")), new Random(1));
        await model.LoadAsync();
        Assert.Equal(HomeState.Loaded, model.State);
        Assert.True(model.Complete);
        Assert.Equal("Cool", model.Sections[0].Title);
        Assert.NotEmpty(model.Hero);
        var all = model.Sections.SelectMany(s => s.Items).Select(i => i.Id).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public async Task HiddenRowsAreLeftOutButCanFillTheBanner()
    {
        var model = new HomeModel(_ => Task.FromResult<IReadOnlyList<SyncedAddon>>([Addon(Cool)]), "u", Client(_ => Metas("c")), new Random(1));
        await model.LoadAsync();
        model.Preferences = new HomeRowPreferences(HiddenRowIds: ["x_base", "x_Noir", "x_Anime"]);
        Assert.Empty(model.Sections);
    }

    [Fact]
    public async Task NoAddonsFallsBackToCinemeta()
    {
        const string cinemeta = """{"id":"com.linvo.cinemeta","name":"Cinemeta","catalogs":[{"type":"movie","id":"top","name":"Popular"}]}""";
        var model = new HomeModel(_ => Task.FromResult<IReadOnlyList<SyncedAddon>>([]), "u",
            Client(url => url.EndsWith("manifest.json") ? cinemeta : Metas("m1")), new Random(1));
        await model.LoadAsync();
        Assert.Equal("Popular", model.Sections.Single().Title);
    }

    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "arctv-home-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TheLastRowsShowAtOnceAndAreReplacedByTheFreshOnes()
    {
        var folder = TempFolder();
        var cache = new HomeCache(folder);
        cache.Save([new HomeSection("old", "Old row", [new MetaPreview("o1", "movie", "Old one")])], "u");
        var release = new TaskCompletionSource<IReadOnlyList<SyncedAddon>>();
        var model = new HomeModel(_ => release.Task, "u", Client(_ => Metas("n1", "n2")), new Random(1), cache);

        var load = model.LoadAsync();
        Assert.Equal(HomeState.Loaded, model.State); // before any addon has answered
        Assert.Equal("Old row", model.Sections.Single().Title);
        Assert.NotEmpty(model.Hero);

        release.SetResult([Addon(Cool)]);
        await load;
        Assert.DoesNotContain(model.Sections, s => s.Title == "Old row");
        Assert.Contains(model.Sections.SelectMany(s => s.Items), i => i.Id == "n1");
        Assert.DoesNotContain(new HomeCache(folder).Load("u")!, s => s.Id == "old"); // the fresh rows are what is saved now
        Directory.Delete(folder, true);
    }

    [Fact]
    public async Task WhenNothingFreshArrivesTheSavedRowsStayAndAreKept()
    {
        var folder = TempFolder();
        var cache = new HomeCache(folder);
        cache.Save([new HomeSection("old", "Old row", [new MetaPreview("o1", "movie", "Old one")])], "u");
        var model = new HomeModel(_ => Task.FromResult<IReadOnlyList<SyncedAddon>>([]), "u", Client(_ => null), new Random(1), cache);
        await model.LoadAsync();
        Assert.Equal(HomeState.Loaded, model.State);
        Assert.Equal("Old row", model.Sections.Single().Title);
        Assert.Equal("Old row", new HomeCache(folder).Load("u")!.Single().Title);
        Directory.Delete(folder, true);
    }

    [Fact]
    public async Task NothingAtAllIsEmpty()
    {
        var model = new HomeModel(_ => throw new HttpRequestException("down"), "u", Client(_ => null));
        await model.LoadAsync();
        Assert.Equal(HomeState.Empty, model.State);
    }
}

public class TextTests
{
    [Fact]
    public void HtmlBecomesPlainText()
    {
        Assert.Equal("Tom & Jerry's <3", Text.HtmlDecoded("Tom &amp; Jerry&#39;s &lt;3"));
        Assert.Equal("a b", Text.HtmlDecoded("a<br/>b"));
        Assert.Equal("", Text.HtmlDecoded(null));
    }
}

public class HomeCacheTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static HomeSection Row(string id, int items) => new(id, "Row " + id, Enumerable.Range(0, items).Select(i => new MetaPreview($"{id}{i}", "movie", $"Title {i}", "p", "b")).ToList());

    [Fact]
    public void RowsAreKeptPerAccountTrimmedAndForgottenAfterAWeek()
    {
        var folder = Path.Combine(Path.GetTempPath(), "arctv-cache-" + Guid.NewGuid().ToString("N"));
        var clock = Start;
        var cache = new HomeCache(folder, () => clock);
        Assert.Null(cache.Load("u1"));
        cache.Save([Row("a", 40), Row("b", 3)], "u1");
        var back = cache.Load("u1")!;
        Assert.Equal(["Row a", "Row b"], back.Select(s => s.Title));
        Assert.Equal(HomeCache.ItemsPerRow, back[0].Items.Count);
        Assert.Equal("Title 0", back[0].Items[0].Name);
        Assert.Equal("b", back[0].Items[0].Background);
        Assert.Null(cache.Load("u2")); // another account or profile has its own

        clock = Start.AddDays(6);
        Assert.NotNull(cache.Load("u1"));
        clock = Start.AddDays(8);
        Assert.Null(cache.Load("u1")); // too old to show
        Directory.Delete(folder, true);
    }

    [Fact]
    public void ABadFileOrNothingToSaveIsHarmless()
    {
        var folder = Path.Combine(Path.GetTempPath(), "arctv-cache-" + Guid.NewGuid().ToString("N"));
        var cache = new HomeCache(folder);
        cache.Save([], "u");
        Assert.False(Directory.Exists(folder)); // nothing was written
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "home-u.json"), "not json");
        Assert.Null(cache.Load("u"));
        Directory.Delete(folder, true);
    }
}

