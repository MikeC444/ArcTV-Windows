using ArcTV.Core;
using Xunit;

namespace ArcTV.Core.Tests;

public class UpdateCheckTests
{
    [Fact]
    public void VersionCompare()
    {
        Assert.True(UpdateCheck.IsNewer("0.1.3", "0.1.2"));
        Assert.True(UpdateCheck.IsNewer("v0.2.0", "0.1.9"));
        Assert.True(UpdateCheck.IsNewer("1.0", "0.9.9"));
        Assert.False(UpdateCheck.IsNewer("0.1.2", "0.1.2"));
        Assert.False(UpdateCheck.IsNewer("0.1.2", "0.1.10"));
        Assert.False(UpdateCheck.IsNewer("1.0.0", "1.0"));
        Assert.False(UpdateCheck.IsNewer("0.2.0-beta", "0.2.0"));
    }

    private const string Json = """
        {"tag_name":"v0.2.0","body":"- Faster Home\n- New look\n","html_url":"https://github.com/x/y/releases/tag/v0.2.0",
         "assets":[{"name":"ArcTV-0.2.0-win-x64.zip","browser_download_url":"https://github.com/x/y/releases/download/v0.2.0/ArcTV-0.2.0-win-x64.zip"},
                   {"name":"ArcTV-Setup-0.2.0.exe","browser_download_url":"https://github.com/x/y/releases/download/v0.2.0/ArcTV-Setup-0.2.0.exe"}]}
        """;

    [Fact]
    public void ParseRelease_PrefersTheInstaller()
    {
        var info = UpdateCheck.Parse(Json);
        Assert.Equal("0.2.0", info.Version);
        Assert.Equal("- Faster Home\n- New look", info.Notes);
        Assert.EndsWith("ArcTV-Setup-0.2.0.exe", info.DownloadUrl);
        Assert.Equal("https://github.com/x/y/releases/tag/v0.2.0", info.PageUrl);
    }

    [Fact]
    public void ParseRelease_FallsBackToTheZip()
    {
        var info = UpdateCheck.Parse("""{"tag_name":"v1.0.0","assets":[{"name":"a.zip","browser_download_url":"https://x/a.zip"}]}""");
        Assert.Equal("https://x/a.zip", info.DownloadUrl);
        Assert.Null(UpdateCheck.Parse("""{"tag_name":"v1.0.0"}""").DownloadUrl);
    }

    [Fact]
    public void ResultComparesWithTheRunningVersion()
    {
        var available = UpdateCheck.Result(200, Json, "0.1.0");
        Assert.True(available.Available);
        Assert.Equal("0.2.0", available.Release?.Version);
        var current = UpdateCheck.Result(200, Json, "0.2.0");
        Assert.False(current.Available);
        Assert.Equal("0.2.0", current.Latest);
        Assert.False(UpdateCheck.Result(404, "", "0.1.0").Available); // nothing published yet is up to date
        Assert.Null(UpdateCheck.Result(404, "", "0.1.0").Latest);
        Assert.Throws<HttpRequestException>(() => UpdateCheck.Result(500, "", "0.1.0"));
    }
}
