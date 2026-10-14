using System.Net;
using ArcTV.Core.Torrents;
using Xunit;

namespace ArcTV.Core.Tests;

public class LocalVideoServerTests
{
    private sealed class Pieces : IPieceSource
    {
        public long Arrived;
        public List<long> Requests = [];
        public bool Have(long offset, long length) => offset + length <= Arrived;
        public void Request(long offset) => Requests.Add(offset);
    }

    private static HttpClient Client() => new(new HttpClientHandler { UseProxy = false });

    private static (string Path, byte[] Bytes) Video(int size = 1_000_000)
    {
        var bytes = new byte[size];
        new Random(5).NextBytes(bytes);
        var path = Path.Combine(Path.GetTempPath(), "arctv-video-" + Guid.NewGuid().ToString("N") + ".mkv");
        File.WriteAllBytes(path, bytes);
        return (path, bytes);
    }

    [Theory]
    [InlineData("bytes=0-99", 1000L, 0L, 99L)]
    [InlineData("bytes=500-", 1000L, 500L, 999L)]
    [InlineData("bytes=-200", 1000L, 800L, 999L)]
    [InlineData("bytes=900-5000", 1000L, 900L, 999L)]
    [InlineData("BYTES=10-20, 30-40", 1000L, 10L, 20L)]
    public void RangesAreRead(string header, long size, long start, long end)
    {
        var range = ByteRange.Parse(header, size, out var unsatisfiable);
        Assert.False(unsatisfiable);
        Assert.Equal(new ByteRange(start, end), range);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("items=0-5")]
    [InlineData("bytes=abc-def")]
    [InlineData("bytes=50-10")]
    public void AHeaderThatIsNotARangeGivesTheWholeFile(string? header) => Assert.Null(ByteRange.Parse(header, 1000, out _));

    [Fact]
    public void ARangeStartingPastTheEndIsUnsatisfiable()
    {
        Assert.Null(ByteRange.Parse("bytes=1000-", 1000, out var unsatisfiable));
        Assert.True(unsatisfiable);
    }

    [Fact]
    public async Task APartOfTheFileIsServedWithTheRightHeadersAndAskedForFirst()
    {
        var (path, bytes) = Video();
        var pieces = new Pieces { Arrived = bytes.Length };
        using var server = new LocalVideoServer(path, bytes.Length, pieces);
        var url = server.Start();
        Assert.EndsWith(".mkv", url.AbsolutePath);

        using var http = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(300_000, 399_999);
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("video/x-matroska", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(300_000, response.Content.Headers.ContentRange?.From);
        Assert.Equal(bytes.Length, response.Content.Headers.ContentRange?.Length);
        Assert.Equal(bytes[300_000..400_000], await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(300_000, pieces.Requests[0]); // the player jumped there: those pieces first
        File.Delete(path);
    }

    [Fact]
    public async Task TheWholeFileIsServedWithoutARangeAndHeadSaysHowBigItIs()
    {
        var (path, bytes) = Video(700_000);
        using var server = new LocalVideoServer(path, bytes.Length, new Pieces { Arrived = bytes.Length });
        var url = server.Start();
        using var http = Client();
        Assert.Equal(bytes, await http.GetByteArrayAsync(url));
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        Assert.Equal(bytes.Length, head.Content.Headers.ContentLength);
        Assert.Equal("bytes", head.Headers.AcceptRanges.Single());
        File.Delete(path);
    }

    [Fact]
    public async Task AnAnswerWaitsForPiecesThatHaveNotArrivedYet()
    {
        var (path, bytes) = Video(600_000);
        var pieces = new Pieces { Arrived = 0 };
        using var server = new LocalVideoServer(path, bytes.Length, pieces);
        var url = server.Start();
        using var http = Client();
        var download = http.GetByteArrayAsync(url);
        await Task.Delay(400);
        Assert.False(download.IsCompleted); // nothing has arrived: the answer is held
        pieces.Arrived = 250_000;
        await Task.Delay(400);
        Assert.False(download.IsCompleted); // some has: still waiting for the rest
        pieces.Arrived = bytes.Length;
        Assert.Equal(bytes, await download.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains(0L, pieces.Requests);
        File.Delete(path);
    }

    [Fact]
    public async Task ARangePastTheEndIsRefusedAndAStoppedServerAnswersNoOne()
    {
        var (path, bytes) = Video(1000);
        var server = new LocalVideoServer(path, bytes.Length, new Pieces { Arrived = bytes.Length });
        var url = server.Start();
        using var http = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(5000, null);
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        server.Dispose();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetByteArrayAsync(url));
        File.Delete(path);
    }
}
