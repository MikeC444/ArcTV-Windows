using ArcTV.Core;
using Xunit;

namespace ArcTV.Core.Tests;

public class AddonUrlTests
{
    [Fact]
    public void NormalizesStremioLinksAndBareHosts()
    {
        Assert.Equal("https://v3-cinemeta.strem.io/manifest.json", AddonUrl.NormalizeManifestUrl("stremio://v3-cinemeta.strem.io"));
        Assert.Equal("https://example.com/addon/manifest.json", AddonUrl.NormalizeManifestUrl("  example.com/addon/  "));
        Assert.Equal("https://example.com/Manifest.json", AddonUrl.NormalizeManifestUrl("https://example.com/Manifest.json"));
    }

    [Fact]
    public void ResourceBaseDropsTheManifest()
    {
        Assert.Equal("https://v3-cinemeta.strem.io", AddonUrl.ResourceBase("https://v3-cinemeta.strem.io/manifest.json"));
        Assert.Equal("https://example.com/a/b", AddonUrl.ResourceBase("https://example.com/a/b/manifest.json"));
    }

    [Fact]
    public void CatalogUrlWithAndWithoutExtras()
    {
        const string manifest = "https://v3-cinemeta.strem.io/manifest.json";
        Assert.Equal("https://v3-cinemeta.strem.io/catalog/movie/top.json", AddonUrl.CatalogUrl(manifest, "movie", "top"));
        Assert.Equal("https://v3-cinemeta.strem.io/catalog/movie/year/genre=2026.json", AddonUrl.CatalogUrl(manifest, "movie", "year", new Dictionary<string, string> { ["genre"] = "2026" }));
        Assert.Equal("https://v3-cinemeta.strem.io/catalog/series/top/genre=Sci%20Fi.json", AddonUrl.CatalogUrl(manifest, "series", "top", new Dictionary<string, string> { ["genre"] = "Sci Fi" }));
    }

    [Fact]
    public void ExtrasAreInAlphabeticalOrderSoTheUrlIsStable()
    {
        var url = AddonUrl.CatalogUrl("https://a/manifest.json", "tv", "all", new Dictionary<string, string> { ["skip"] = "100", ["genre"] = "News" });
        Assert.Equal("https://a/catalog/tv/all/genre=News&skip=100.json", url);
    }
}
