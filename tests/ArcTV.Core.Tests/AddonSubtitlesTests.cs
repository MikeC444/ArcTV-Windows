using System.Net;
using System.Text;
using ArcTV.Core.Addons;
using Xunit;

namespace ArcTV.Core.Tests;

public class AddonSubtitlesTests
{
    private sealed class Handler(Func<string, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var path = request.RequestUri!.AbsoluteUri;
            lock (Asked) Asked.Add(path);
            var (status, body) = answer(path);
            if (body == "hang") await Task.Delay(Timeout.Infinite, cancel);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static SyncedAddon Addon(string name, bool enabled = true, bool offersSubtitles = true, List<string>? types = null) =>
        new($"https://{name}.test/manifest.json", name, name, enabled, 0,
            new AddonManifest(name, name, Resources: [new AddonResource(offersSubtitles ? "subtitles" : "stream", types, ["tt"])]));

    [Fact]
    public void LanguagesAreReadWhateverWayTheAddonWroteThem()
    {
        Assert.Equal("en", SubtitleLanguage.Key("eng"));
        Assert.Equal("en", SubtitleLanguage.Key("en"));
        Assert.Equal("pt", SubtitleLanguage.Key("pt-BR"));
        Assert.Equal("fr", SubtitleLanguage.Key("fre"));
        Assert.Equal("de", SubtitleLanguage.Key("German"));
        Assert.Null(SubtitleLanguage.Key("klingon"));
        Assert.Null(SubtitleLanguage.Key(""));
        Assert.Equal("English", SubtitleLanguage.DisplayName("eng"));
        Assert.Equal("Serbian", SubtitleLanguage.DisplayName("scc"));
        Assert.Equal("KLINGON", SubtitleLanguage.DisplayName("klingon")); // never a blank
        Assert.Equal("Unknown", SubtitleLanguage.DisplayName(null));
    }

    [Fact]
    public void AnAnswerGivesOnlyWebAddressesOncePerAddressAndNeverThrows()
    {
        const string json = """{"subtitles":[{"id":"1","url":"https://s.test/a.srt","lang":"eng"},{"id":"2","url":"https://s.test/a.srt","lang":"eng"},{"id":"3","url":"ftp://s.test/b.srt","lang":"fre"},{"id":"4","lang":"spa"},{"url":" http://s.test/c.srt ","lang":"ger"},7,{"url":"https://s.test/d.srt"}]}""";
        var files = AddonSubtitles.Parse(json, "OpenSubtitles");
        Assert.Equal(["https://s.test/a.srt", "http://s.test/c.srt", "https://s.test/d.srt"], files.Select(f => f.Url));
        Assert.Equal("English · OpenSubtitles", files[0].MenuTitle);
        Assert.Equal("German", files[1].LanguageName);
        Assert.Equal("Unknown", files[2].LanguageName);
        Assert.Empty(AddonSubtitles.Parse("not json", "x"));
        Assert.Empty(AddonSubtitles.Parse("""{"subtitles":"no"}""", "x"));
        Assert.Empty(AddonSubtitles.Parse("{}", "x"));
    }

    [Fact]
    public void ThePreferredLanguageComesFirstThenAToZAndTheListIsKeptShort()
    {
        AddonSubtitle File(string lang, string url, string addon = "A") => new(addon, lang, SubtitleLanguage.DisplayName(lang), url);
        var found = new[] { File("spa", "s1"), File("eng", "e1"), File("fre", "f1"), File("eng", "e2", "B"), File("ara", "a1"), File("eng", "e3"), File("eng", "e4"), File("eng", "e5"), File("eng", "e6"), File("eng", "e7") };
        var ranked = AddonSubtitles.Rank(found, "fr");
        Assert.Equal("f1", ranked[0].Url); // preferred first
        Assert.Equal(["Arabic", "English", "Spanish"], ranked.Skip(1).Select(f => f.LanguageName).Distinct()); // then A to Z
        Assert.Equal(5, ranked.Count(f => f.LanguageName == "English")); // at most five a language
        Assert.Equal(["e1", "e2", "e3", "e4", "e5"], ranked.Where(f => f.LanguageName == "English").Select(f => f.Url)); // each addon's own order kept
        Assert.Equal(3, AddonSubtitles.Rank(found, null, total: 3).Count);
    }

    [Fact]
    public void OnlyAnAddonThatListsSubtitlesForThatKindOfTitleIsAsked()
    {
        var movie = new AddonManifest("a", "a", Resources: [new AddonResource("subtitles", ["movie"], ["tt"])]);
        Assert.True(movie.OffersSubtitles("movie", "tt123"));
        Assert.False(movie.OffersSubtitles("series", "tt123")); // not for shows
        Assert.False(movie.OffersSubtitles("movie", "kitsu:1")); // not for that id
        Assert.False(new AddonManifest("b", "b", Resources: [new AddonResource("stream")]).OffersSubtitles("movie", "tt1")); // a streams addon is not asked
        Assert.False(new AddonManifest("c", "c").OffersSubtitles("movie", "tt1"));
        Assert.True(new AddonManifest("d", "d", Types: ["movie", "series"], Resources: [new AddonResource("subtitles")]).OffersSubtitles("series", "tt1")); // the manifest's own types apply
        Assert.True(new AddonManifest("e", "e", Resources: [new AddonResource("subtitles", [], [])]).OffersSubtitles("movie", "anything")); // none listed: anything
    }

    [Fact]
    public void TheAddressesCarryTheEpisodeIdAndTheFileName()
    {
        Assert.Equal("https://os.test/subtitles/series/tt9:2:3.json", AddonUrl.SubtitlesUrl("https://os.test/manifest.json", "series", "tt9:2:3"));
        Assert.Equal("https://os.test/subtitles/movie/tt1/filename=Film%202019%201080p.mkv.json", AddonUrl.SubtitlesUrl("https://os.test/manifest.json", "movie", "tt1", "Film 2019 1080p.mkv"));
    }

    [Fact]
    public async Task TheFinderAsksOnlyEnabledSubtitleAddonsAndSurvivesASlowOrBrokenOne()
    {
        var handler = new Handler(url => url switch
        {
            var u when u.StartsWith("https://good.test/", StringComparison.Ordinal) => (HttpStatusCode.OK, """{"subtitles":[{"url":"https://f.test/en.srt","lang":"eng"},{"url":"https://f.test/fr.srt","lang":"fre"}]}"""),
            var u when u.StartsWith("https://broken.test/", StringComparison.Ordinal) => (HttpStatusCode.InternalServerError, "oops"),
            var u when u.StartsWith("https://slow.test/", StringComparison.Ordinal) => (HttpStatusCode.OK, "hang"),
            _ => (HttpStatusCode.OK, """{"subtitles":[{"url":"https://f.test/never.srt","lang":"eng"}]}"""),
        });
        var client = new AddonClient(new HttpClient(handler));
        var addons = new[] { Addon("good"), Addon("broken"), Addon("off", enabled: false), Addon("streams", offersSubtitles: false) };
        var found = await SubtitleFinder.FindAsync(addons, "movie", "tt1", "Film.mkv", "fr", client);
        Assert.Equal(["https://f.test/fr.srt", "https://f.test/en.srt"], found.Select(f => f.Url)); // French first, as asked for
        Assert.All(found, f => Assert.Equal("good", f.AddonName));
        Assert.DoesNotContain(handler.Asked, url => url.Contains("off.test") || url.Contains("streams.test"));
        Assert.Contains(handler.Asked, url => url.EndsWith("/subtitles/movie/tt1/filename=Film.mkv.json", StringComparison.Ordinal));

        // a cancelled search is cancelled, not "no files"
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SubtitleFinder.FindAsync([Addon("slow")], "movie", "tt1", null, null, client, cancelled.Token));
        Assert.Empty(await SubtitleFinder.FindAsync([], "movie", "tt1", null, null, client));
    }
}
