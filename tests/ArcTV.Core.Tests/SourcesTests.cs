using System.Net;
using System.Text.Json;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Details;
using ArcTV.Core.Sources;
using Xunit;

namespace ArcTV.Core.Tests;

public class SourcesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static SourceItem Item(string addon, AddonStream stream, int index = 0) => new(addon, stream, index);

    [Fact]
    public void ManifestResourcesAcceptNamesAndObjects()
    {
        var manifest = JsonSerializer.Deserialize<AddonManifest>("""
            {"id":"org.torrentio","name":"Torrentio","types":["movie","series"],
             "resources":["catalog",{"name":"stream","types":["movie","series"],"idPrefixes":["tt"]}]}
            """, Web)!;
        Assert.True(manifest.ProvidesStreams("movie", "tt0816692"));
        Assert.True(manifest.ProvidesStreams("series", "tt1:1:2"));
        Assert.False(manifest.ProvidesStreams("movie", "kitsu:1"));
        Assert.False(manifest.ProvidesStreams("tv", "tt1"));
    }

    [Fact]
    public void ReadsQualitySizeAndPlayability()
    {
        var stream = JsonSerializer.Deserialize<AddonStream>("""{"name":"TorBox 2160p","title":"Movie.2025.2160p.WEB-DL.mkv\n👤 12  💾 14.2 GB  ⚙️ Site","url":"https://cdn.example/x.mkv"}""", Web)!;
        var item = Item("Torrentio", stream);
        Assert.Equal(Quality.P2160, item.Quality);
        Assert.Equal("14.2 GB", item.SizeText);
        Assert.Equal(12, item.Seeders);
        Assert.Equal("WEB-DL", item.SourceTag);
        Assert.True(item.IsPlayable);
        Assert.Equal("Movie.2025.2160p.WEB-DL.mkv", item.Headline);

        var torrent = Item("T", new AddonStream(InfoHash: "abc", Title: "Show 720p"), 1);
        Assert.False(torrent.IsPlayable);
        Assert.True(torrent.IsTorrent);
        Assert.True(torrent.CanStream);
        Assert.Equal(Quality.P720, torrent.Quality);
        Assert.False(Item("X", new AddonStream(Title: "nothing to play")).CanStream);
    }

    [Fact]
    public void ReadsCodecAudioAndSizeFromTheName()
    {
        var item = Item("A", new AddonStream(Title: "Film.2160p.BluRay.REMUX.HEVC.HDR10+.DV.Atmos 21.3 GB"));
        Assert.Equal("HEVC  ·  HDR10+  ·  Dolby Vision", item.CodecText);
        Assert.Equal("Atmos", item.AudioTag);
        Assert.Equal("REMUX", item.SourceTag);
        Assert.Equal((long)(21.3 * 1_073_741_824), item.SizeBytes);
        Assert.Equal(5_368_709_120, Item("A", new AddonStream(BehaviorHints: new StreamHints(VideoSize: 5_368_709_120))).SizeBytes);
    }

    [Fact]
    public void AddonErrorsReadAsSentences()
    {
        Assert.Equal("refused the request (HTTP 403)", SourceFinder.DescribeError(ApiException.Server(403, null)));
        Assert.Equal("had a problem answering (HTTP 503)", SourceFinder.DescribeError(ApiException.Server(503, null)));
        Assert.Equal("is limiting requests right now, try again in a moment", SourceFinder.DescribeError(ApiException.Server(429, null)));
        Assert.Equal("took too long to answer", SourceFinder.DescribeError(new TaskCanceledException()));
        Assert.Equal("sent an answer that could not be read", SourceFinder.DescribeError(ApiException.Unreadable()));
        Assert.Equal("couldn't be reached", SourceFinder.DescribeError(new HttpRequestException("offline")));
    }

    private static SyncedAddon Addon(string name, string manifest) =>
        new($"https://{name}.test/manifest.json", name, name, true, 0, JsonSerializer.Deserialize<AddonManifest>(manifest, Web));

    private const string StreamManifest = """{"id":"x","name":"X","types":["movie","series"],"resources":["stream"]}""";
    private const string CatalogOnly = """{"id":"c","name":"C","resources":["catalog"]}""";

    [Fact]
    public async Task FinderAsksOnlyAddonsThatStreamPutsDirectLinksFirstAndReportsFailures()
    {
        var calls = new List<string>();
        var client = new AddonClient(new HttpClient(new FakeHandler((request, _) =>
        {
            calls.Add(request.RequestUri!.AbsoluteUri);
            return request.RequestUri.Host switch
            {
                "good.test" => (HttpStatusCode.OK, """{"streams":[{"name":"T","title":"A 1080p","infoHash":"h1"},{"name":"D","title":"B 720p","url":"https://cdn/b.mkv"},{"name":"D","title":"C 2160p","url":"https://cdn/c.mkv"}]}"""),
                _ => (HttpStatusCode.ServiceUnavailable, "{}"),
            };
        })));
        var result = await SourceFinder.FindAsync([Addon("good", StreamManifest), Addon("bad", StreamManifest), Addon("catalogs", CatalogOnly)], "movie", "tt1", client);

        Assert.Equal(new[] { "C 2160p", "B 720p", "A 1080p" }, result.Items.Select(i => i.Headline)); // direct links first (best quality first), then the torrent
        Assert.Equal(new[] { "catalogs" }, result.Skipped);
        Assert.Equal("had a problem answering (HTTP 503)", Assert.Single(result.Failures).Reason);
        Assert.DoesNotContain(calls, c => c.Contains("catalogs.test"));
        Assert.Contains("https://good.test/stream/movie/tt1.json", calls);
    }

    private static List<SourceItem> Mixed() =>
    [
        Item("A", new AddonStream(Title: "1080 direct", Url: "https://x/1")),
        Item("A", new AddonStream(Title: "4K torrent 👤 50 40 GB", InfoHash: "t4")),
        Item("A", new AddonStream(Title: "720 torrent 👤 900 2 GB", InfoHash: "t7")),
        Item("A", new AddonStream(Title: "480 torrent", InfoHash: "ts")),
    ];

    [Fact]
    public void ListingFiltersByQualityButKeepsTheRecommendedSourceOnTop()
    {
        var items = Mixed().Select((item, index) => item with { Index = index }).ToList();
        Assert.Equal(items[0].Id, SourceListing.RecommendedId(items));

        var uhd = SourceListing.Arrange(items, SourceFilter.Uhd, SourceSort.Quality);
        Assert.Equal(new[] { "1080 direct", "4K torrent 👤 50 40 GB" }, uhd.Select(i => i.Headline));

        var other = SourceListing.Arrange(items, SourceFilter.Other, SourceSort.Quality);
        Assert.Equal(new[] { "1080 direct", "480 torrent" }, other.Select(i => i.Headline));

        Assert.Equal(new[] { "1080 direct", "720 torrent 👤 900 2 GB", "4K torrent 👤 50 40 GB", "480 torrent" }, SourceListing.Arrange(items, SourceFilter.All, SourceSort.Seeders).Select(i => i.Headline));
        Assert.Equal(new[] { "1080 direct", "4K torrent 👤 50 40 GB", "720 torrent 👤 900 2 GB", "480 torrent" }, SourceListing.Arrange(items, SourceFilter.All, SourceSort.Size).Select(i => i.Headline));
        Assert.Equal(SourceSort.Seeders, SourceSort.Quality.Next());
        Assert.Equal(SourceSort.Quality, SourceSort.Size.Next());
    }

    [Fact]
    public void LastSourceFindsTheSameReleaseInAFreshList()
    {
        var items = new List<SourceItem>
        {
            Item("A", new AddonStream(Title: "Other", Url: "https://x/1")),
            Item("A", new AddonStream(Title: "Movie 1080p", InfoHash: "ABC")),
            Item("B", new AddonStream(Title: "Movie 1080p", Url: "https://y/2")),
        };
        Assert.Equal("ABC", new LastSource("Z", "gone", "abc").Match(items)!.Stream.InfoHash); // same torrent, any letter case
        Assert.Equal("B", new LastSource("B", "Movie 1080p").Match(items)!.AddonName); // same release from the same addon
        Assert.Null(new LastSource("C", "Movie 1080p").Match(items)); // two addons list that name: not sure which, so not guessed
        Assert.Equal("A", new LastSource("C", "Other").Match(items)!.AddonName); // a name only one stream has
        Assert.Null(new LastSource("A", "Nothing like it").Match(items));
        Assert.Null(new LastSource("A", "x").Match([Item("A", new AddonStream(Title: "x"))])); // a source that cannot play does not count
    }

    [Fact]
    public void LastSourceDoesNotGuessBetweenStreamsThatShareAReleaseName()
    {
        // an addon often gives the same first line to every quality of a film; what tells them apart is the rest of what it wrote
        var items = new List<SourceItem>
        {
            Item("A", new AddonStream(Name: "Debrid 2160p", Title: "Movie\n💾 58 GB", Url: "https://x/4k"), 0),
            Item("A", new AddonStream(Name: "Debrid 1080p", Title: "Movie\n💾 6 GB", Url: "https://x/1080"), 1),
            Item("A", new AddonStream(Name: "Debrid 720p", Title: "Movie\n💾 2 GB", Url: "https://x/720"), 2),
        };
        var chosen = items[1];
        Assert.Equal("Movie", chosen.Headline);
        // remembered with its full write-up: that very stream, even when the list comes in another order and with other links
        var last = LastSource.From(chosen);
        var fresh = new List<SourceItem>
        {
            Item("A", new AddonStream(Name: "Debrid 720p", Title: "Movie\n💾 2 GB", Url: "https://x/new720"), 0),
            Item("A", new AddonStream(Name: "Debrid 1080p", Title: "Movie\n💾 6 GB", Url: "https://x/new1080"), 1),
            Item("A", new AddonStream(Name: "Debrid 2160p", Title: "Movie\n💾 58 GB", Url: "https://x/new4k"), 2),
        };
        Assert.Equal("https://x/new1080", last.Match(fresh)!.PlayUrl!.ToString());
        // remembered only by name (an older entry): three streams fit, so none is picked
        Assert.Null(new LastSource("A", "Movie").Match(items));
        // gone from the list: nothing
        Assert.Null(last.Match([fresh[0], fresh[2]]));
    }

    [Fact]
    public void LastSourceIsKeptPerTitleEpisodeAndAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), "arctv-last-" + Guid.NewGuid(), "last-sources.json");
        var mine = new LastSourceStore(path, "u1");
        mine.Set(new LastSource("A", "Rel 1"), "tt1", null, null);
        mine.Set(new LastSource("A", "Rel 2"), "tt2", 1, 3);
        Assert.Equal("Rel 1", mine.Get("tt1", null, null)!.ReleaseTitle);
        Assert.Equal("Rel 2", mine.Get("tt2", 1, 3)!.ReleaseTitle);
        Assert.Null(mine.Get("tt2", 1, 4));
        Assert.Null(new LastSourceStore(path, "someone-else").Get("tt1", null, null));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public async Task ModelFiltersAndExplainsAnEmptyList()
    {
        var detail = new MetaDetail("tt1", "movie", "One");
        var empty = new SourcesModel(new PlayTarget(detail), _ => Task.FromResult<IReadOnlyList<SyncedAddon>>([]));
        await empty.LoadAsync();
        Assert.False(empty.Loading);
        Assert.Contains("no addons yet", empty.EmptyMessage);

        var skipped = new SourcesModel(new PlayTarget(detail), _ => Task.FromResult<IReadOnlyList<SyncedAddon>>([Addon("catalogs", CatalogOnly)]));
        await skipped.LoadAsync();
        Assert.Contains("None of your addons give streams", skipped.EmptyMessage);
    }

    [Fact]
    public void PlayTargetsNameTheirStreamAndHeading()
    {
        var show = new MetaDetail("tt9", "series", "A Show");
        Assert.Equal("tt9:2:5", new PlayTarget(show, 2, 5, "S2 E5 Name").StreamId);
        Assert.Equal("A Show · S2 E5 Name", new PlayTarget(show, 2, 5, "S2 E5 Name").Heading);
        Assert.Equal("tt9", new PlayTarget(show).StreamId);
        Assert.Equal("A Show", new PlayTarget(show).Heading);
    }
}
