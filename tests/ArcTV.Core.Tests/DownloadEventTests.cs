using System.Text.Json;
using ArcTV.Core.Downloads;
using Xunit;

namespace ArcTV.Core.Tests;

public class DownloadEventTests
{
    private static JsonElement Body(DownloadEvent entry) => JsonDocument.Parse(entry.ToJson()).RootElement;

    [Fact]
    public void APressedFileCarriesItsQualityAndWhetherItWasCached()
    {
        var body = Body(new DownloadEvent(DownloadEvent.File, Movie: false, Episode: true, "1080p", true));
        Assert.Equal("file", body.GetProperty("kind").GetString());
        Assert.Equal("TV_SHOW", body.GetProperty("contentType").GetString());
        Assert.True(body.GetProperty("episode").GetBoolean());
        Assert.Equal("1080p", body.GetProperty("resolution").GetString());
        Assert.True(body.GetProperty("cached").GetBoolean());
    }

    [Theory]
    [InlineData("opened")]
    [InlineData("popup")]
    public void OpeningThePanelOrShowingThePlusPopupCarriesNoQuality(string kind)
    {
        var body = Body(new DownloadEvent(kind, Movie: true, Episode: false, "4K", true));
        Assert.Equal("MOVIE", body.GetProperty("contentType").GetString());
        Assert.False(body.GetProperty("episode").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("resolution").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cached").ValueKind);
    }
}
