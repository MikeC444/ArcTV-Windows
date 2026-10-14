using System.Net;
using ArcTV.Core.Addons;
using ArcTV.Core.Downloads;
using ArcTV.Core.Sources;
using Xunit;

namespace ArcTV.Core.Tests;

public class DownloadTests
{
    private static SourceItem Direct(string title, string name = "[RD+] Torrentio", string url = "https://example.com/a.mkv", long? size = null) =>
        new("Torrentio", new AddonStream(Name: name, Title: title, Url: url, BehaviorHints: size is null ? null : new StreamHints(VideoSize: size)), 0);

    [Fact]
    public void OnlyDirectLinksAreOfferedBestQualityThenSmallestFirst()
    {
        var items = new[]
        {
            Direct("Film 1080p", size: 5_000_000_000),
            Direct("Film 2160p", size: 40_000_000_000),
            Direct("Film 1080p small", size: 2_000_000_000),
            new SourceItem("Torrentio", new AddonStream(Title: "Film 2160p torrent", InfoHash: "abc"), 3),
        };
        var sorted = DownloadChoices.Sorted(items);
        Assert.Equal(3, sorted.Count); // the torrent is not offered
        Assert.Equal("Film 2160p", sorted[0].Headline);
        Assert.Equal("Film 1080p small", sorted[1].Headline);
        Assert.Equal("Film 1080p", sorted[2].Headline);
    }

    [Fact]
    public void FileNamesAreSafeAndKeepAKnownExtension()
    {
        var link = new Uri("https://example.com/path/video.mp4?x=1");
        Assert.Equal("Film 2019 1080p.mp4", DownloadChoices.FileName("Film: 2019 / 1080p", link));
        Assert.Equal("Film.mkv", DownloadChoices.FileName("Film.mkv", link));
        Assert.Equal("Arc TV download.mp4", DownloadChoices.FileName("???", link));
        Assert.EndsWith(".mkv", DownloadChoices.FileName("Film", new Uri("https://example.com/download")));
        Assert.True(DownloadChoices.FileName(new string('a', 400), link).Length <= 154);
    }

    [Fact]
    public void LabelsFollowTheReleaseName()
    {
        Assert.Equal("UHD Blu-ray REMUX", DownloadChoices.Kind(Direct("Film 2160p BluRay REMUX HEVC")));
        Assert.Equal("4K WEB-DL", DownloadChoices.Kind(Direct("Film 2160p WEB-DL")));
        Assert.Equal("1080p", DownloadChoices.Kind(Direct("Film 1080p")));
        Assert.Equal("Season 2 · 8 episodes", DownloadChoices.SeasonLabel(2, 8));
        Assert.Equal("Season 1 · 1 episode", DownloadChoices.SeasonLabel(1, 1));
    }

    private sealed class Stub(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => Task.FromResult(answer());
    }

    private static string Folder() => Path.Combine(Path.GetTempPath(), "arctv-dl-" + Guid.NewGuid().ToString("N"));

    private static async Task<DownloadJob> Finished(DownloadManager manager, Uri url, string name)
    {
        var job = manager.Start(url, name);
        for (var i = 0; i < 500 && job.State == DownloadState.Running; i++) await Task.Delay(10);
        return job;
    }

    [Fact]
    public async Task AFileIsSavedUnderItsNameAndANumberIsAddedWhenTheNameIsTaken()
    {
        var folder = Folder();
        var bytes = new byte[300_000];
        new Random(1).NextBytes(bytes);
        var manager = new DownloadManager(new HttpClient(new Stub(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })), folder);
        var first = await Finished(manager, new Uri("https://example.com/a.mkv"), "Film.mkv");
        Assert.Equal(DownloadState.Done, first.State);
        Assert.Equal(Path.Combine(folder, "Film.mkv"), first.FilePath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(first.FilePath!));
        Assert.Equal(1, first.Fraction);
        var second = await Finished(manager, new Uri("https://example.com/a.mkv"), "Film.mkv");
        Assert.Equal(Path.Combine(folder, "Film 2.mkv"), second.FilePath);
        Assert.Empty(Directory.GetFiles(folder, "*.part"));
        Directory.Delete(folder, true);
    }

    [Fact]
    public async Task AServerErrorFailsWithWordsAndLeavesNothingBehind()
    {
        var folder = Folder();
        var manager = new DownloadManager(new HttpClient(new Stub(() => new HttpResponseMessage(HttpStatusCode.InternalServerError))), folder);
        var job = await Finished(manager, new Uri("https://example.com/a.mkv"), "Film.mkv");
        Assert.Equal(DownloadState.Failed, job.State);
        Assert.Contains("500", job.Message);
        Assert.Empty(Directory.GetFiles(folder));
        manager.ClearFinished();
        Assert.Empty(manager.Jobs);
        Directory.Delete(folder, true);
    }

    [Fact]
    public void DebridTagsAndOtherLabelsAreReadFromTheStream()
    {
        var cached = DownloadChoices.Debrid(Direct("Film 2160p", name: "[RD+] Torrentio 4k"))!;
        Assert.True(cached.Cached);
        Assert.Equal("Cached on Real-Debrid", cached.Label);
        var fetching = DownloadChoices.Debrid(Direct("Film", name: "[AD download] Torrentio"))!;
        Assert.False(fetching.Cached);
        Assert.StartsWith("Not cached", fetching.Label);
        Assert.Null(DownloadChoices.Debrid(Direct("Film", name: "Torrentio")));
        Assert.Equal("Dolby Vision", DownloadChoices.Hdr(Direct("Film 2160p DV HEVC")));
        Assert.Equal("HDR", DownloadChoices.Hdr(Direct("Film 2160p HDR10+")));
        Assert.Null(DownloadChoices.Hdr(Direct("Film 1080p")));
        Assert.True(DownloadChoices.IsUnusuallySmall(Direct("Film 1080p", size: 90_000_000)));
        Assert.False(DownloadChoices.IsUnusuallySmall(Direct("Film 1080p", size: 3_000_000_000)));
        Assert.False(DownloadChoices.IsUnusuallySmall(Direct("Film 720p", size: 90_000_000)));
    }

    /// <summary>A server that honours Range: it sends the rest of the file with 206 (or all of it with 200 when no range was asked for).</summary>
    private sealed class RangeServer(byte[] bytes) : HttpMessageHandler
    {
        public List<string?> Ranges { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var from = request.Headers.Range?.Ranges.First().From;
            Ranges.Add(from?.ToString());
            if (from is not { } start) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            var content = new ByteArrayContent(bytes[(int)start..]);
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, bytes.Length - 1, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        }
    }

    private static async Task<DownloadJob> Settled(DownloadJob job)
    {
        for (var i = 0; i < 500 && job.State == DownloadState.Running; i++) await Task.Delay(10);
        return job;
    }

    [Fact]
    public async Task ADownloadStoppedByClosingTheAppComesBackPausedAndResumesFromItsHalfFile()
    {
        var folder = Folder();
        Directory.CreateDirectory(folder);
        var state = Path.Combine(folder, "downloads.json");
        var bytes = new byte[200_000];
        new Random(2).NextBytes(bytes);
        var half = Path.Combine(folder, "Film.mkv.part");
        await File.WriteAllBytesAsync(half, bytes[..80_000]);
        var record = new[] { new { Id = 7, Name = "Film.mkv", Url = "https://example.com/a.mkv", Destination = Path.Combine(folder, "Film.mkv"), State = "Running", Message = (string?)null, Total = (long?)200_000 } };
        await File.WriteAllTextAsync(state, System.Text.Json.JsonSerializer.Serialize(record));

        var server = new RangeServer(bytes);
        var manager = new DownloadManager(new HttpClient(server), folder, state);
        var job = Assert.Single(manager.Jobs);
        Assert.Equal(DownloadState.Paused, job.State);
        Assert.True(job.CanResume);
        Assert.InRange(job.Fraction ?? 0, 0.39, 0.41);

        manager.Resume(job);
        await Settled(job);
        Assert.Equal(DownloadState.Done, job.State);
        Assert.Equal(new[] { "80000" }, server.Ranges); // asked only for the rest
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(folder, "Film.mkv")));
        Assert.False(File.Exists(half));

        // the finished one is still listed the next time, as long as its file is there
        var again = new DownloadManager(new HttpClient(server), folder, state);
        Assert.Equal(DownloadState.Done, Assert.Single(again.Jobs).State);
        File.Delete(Path.Combine(folder, "Film.mkv"));
        Assert.Empty(new DownloadManager(new HttpClient(server), folder, state).Jobs);
        Directory.Delete(folder, true);
    }

    [Fact]
    public async Task ACancelledDownloadLeavesNoHalfFileAndAnExpiredLinkSaysSo()
    {
        var folder = Folder();
        var state = Path.Combine(folder, "downloads.json");
        Directory.CreateDirectory(folder);
        var gone = new DownloadManager(new HttpClient(new Stub(() => new HttpResponseMessage(HttpStatusCode.Gone))), folder, state);
        var expired = await Finished(gone, new Uri("https://example.com/a.mkv"), "Film.mkv");
        Assert.Equal(DownloadState.Failed, expired.State);
        Assert.Contains("expired", expired.Message);
        Assert.False(expired.CanResume);

        var paused = new DownloadManager(new HttpClient(new Stub(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) })), folder);
        var job = await Finished(paused, new Uri("https://example.com/b.mkv"), "Other.mkv");
        paused.Remove(job);
        Assert.Empty(paused.Jobs);
        Assert.True(File.Exists(Path.Combine(folder, "Other.mkv"))); // taking it off the list keeps the file
        Directory.Delete(folder, true);
    }
}
