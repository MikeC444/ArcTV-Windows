using ArcTV.Core;
using Xunit;

namespace ArcTV.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void ResolvesTheServerAddressInOrder()
    {
        Assert.Equal("https://saved.example", AppInfo.ResolveApiBaseUrl("  https://saved.example ", "https://bundled.example"));
        Assert.Equal("https://bundled.example", AppInfo.ResolveApiBaseUrl("  ", "https://bundled.example"));
        Assert.Equal(AppInfo.DefaultApiBaseUrl, AppInfo.ResolveApiBaseUrl(null, ""));
    }

    [Fact]
    public void CleansTheVersion()
    {
        Assert.Equal("1.2.3", AppInfo.CleanVersion("1.2.3+abcdef"));
        Assert.Equal("1.2.3", AppInfo.CleanVersion("1.2.3"));
        Assert.Equal("dev", AppInfo.CleanVersion("0.0.0-dev"));
        Assert.Equal("dev", AppInfo.CleanVersion("0.0.0-dev+abc"));
        Assert.Equal("dev", AppInfo.CleanVersion(null));
    }
}
