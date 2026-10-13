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
        var manager = new DownloadManager(new HttpClient(new Stub(() => new HttpResponseMessage(HttpStatusCode.Forbidden))), folder);
        var job = await Finished(manager, new Uri("https://example.com/a.mkv"), "Film.mkv");
        Assert.Equal(DownloadState.Failed, job.State);
        Assert.Contains("403", job.Message);
        Assert.Empty(Directory.GetFiles(folder));
        manager.ClearFinished();
        Assert.Empty(manager.Jobs);
        Directory.Delete(folder, true);
    }
}
