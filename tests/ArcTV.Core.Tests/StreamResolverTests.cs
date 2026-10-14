using System.Net;
using ArcTV.Core.Sources;
using Xunit;

namespace ArcTV.Core.Tests;

public class StreamResolverTests
{
    private sealed class Fake(Func<HttpRequestMessage, int, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => Task.FromResult(answer(request, ++Calls));
    }

    private static HttpResponseMessage Redirect(string to) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };

    [Fact]
    public async Task ARedirectIsFollowedToTheRealFile()
    {
        var fake = new Fake((r, _) => r.RequestUri!.Host == "resolve.test" ? Redirect("https://cdn.test/video.mkv") : new HttpResponseMessage(HttpStatusCode.PartialContent));
        var url = await StreamResolver.ResolveAsync(new Uri("https://resolve.test/a"), handler: fake, attemptTimeout: TimeSpan.FromSeconds(5));
        Assert.Equal("https://cdn.test/video.mkv", url.ToString());
    }

    [Fact]
    public async Task AnErrorFirstIsTriedAgain()
    {
        var fake = new Fake((r, n) => n < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Redirect("https://cdn.test/v.mkv"));
        var url = await StreamResolver.ResolveAsync(new Uri("https://resolve.test/a"), handler: fake, attemptTimeout: TimeSpan.FromSeconds(5));
        Assert.Equal("https://cdn.test/v.mkv", url.ToString());
        Assert.True(fake.Calls >= 3);
    }

    [Fact]
    public async Task ADirectFileIsLeftAlone()
    {
        var fake = new Fake((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var original = new Uri("https://cdn.test/v.mkv");
        Assert.Equal(original, await StreamResolver.ResolveAsync(original, handler: fake));
    }

    [Fact]
    public async Task WhenItNeverAnswersTheOriginalAddressIsReturned()
    {
        var fake = new Fake((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var original = new Uri("https://resolve.test/a");
        Assert.Equal(original, await StreamResolver.ResolveAsync(original, handler: fake, attemptTimeout: TimeSpan.FromSeconds(5)));
    }
}
