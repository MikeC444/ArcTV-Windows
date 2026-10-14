using System.Diagnostics;
using ArcTV.Core.Sources;
using ArcTV.Core.Torrents;
using Xunit;

namespace ArcTV.Core.Tests;

/// <summary>
/// The libtorrent engine end to end, with no internet: the native test helper (native/seeder.cpp) seeds a video on the loopback address, and the C# wrapper, the local video server and an HTTP
/// range read take it from there. They run only when ARCTV_CTORRENT_PATH (the built library) and ARCTV_CT_SEEDER (the helper) are set, as they are for the native build job; otherwise they
/// pass without doing anything (the library is not part of the test project).
/// </summary>
public class LibTorrentTests
{
    private static string? Library => Environment.GetEnvironmentVariable("ARCTV_CTORRENT_PATH") is { Length: > 0 } path && File.Exists(path) ? path : null;

    private static string? Seeder => Environment.GetEnvironmentVariable("ARCTV_CT_SEEDER") is { Length: > 0 } path && File.Exists(path) ? path : null;

    [Fact]
    public void TheNativeLibraryLoadsAndAMadeUpTorrentCanBeAddedAskedAboutAndStopped()
    {
        if (Library is null) return;
        Assert.True(LibTorrentSession.Available(out var why), why);
        using var session = new LibTorrentSession();
        Assert.Throws<InvalidOperationException>(() => session.Start("not a magnet", Path.GetTempPath()));
        var folder = Path.Combine(Path.GetTempPath(), "arctv-lt-" + Guid.NewGuid().ToString("N"));
        session.Start("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=x", folder);
        var status = session.Status();
        Assert.False(status.HasMetadata);
        Assert.Equal(0, status.FileCount);
        Assert.Empty(session.Files());
        Assert.Equal(0, session.PieceLength());
        Assert.False(session.Have(0, 0, 10));
        session.Stop(true);
    }

    [Fact]
    public async Task AVideoIsTakenFromASeederAndServedToAPlayerByteForByte()
    {
        if (Library is null || Seeder is null) return;
        var work = Path.Combine(Path.GetTempPath(), "arctv-lt-e2e-" + Guid.NewGuid().ToString("N"));
        using var seeder = Process.Start(new ProcessStartInfo(Seeder, $"\"{Path.Combine(work, "seeder")}\"") { RedirectStandardOutput = true, RedirectStandardInput = true, UseShellExecute = false })!;
        try
        {
            string? magnet = null, original = null;
            while (magnet is null || original is null)
            {
                var line = await seeder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
                Assert.NotNull(line);
                if (line.StartsWith("MAGNET ", StringComparison.Ordinal)) magnet = line[7..];
                if (line.StartsWith("FILE ", StringComparison.Ordinal)) original = line[5..];
            }

            var bytes = await File.ReadAllBytesAsync(original);
            using var session = new LibTorrentSession();
            session.Start(magnet, Path.Combine(work, "client"));
            for (var i = 0; i < 600 && !session.Status().HasMetadata; i++) await Task.Delay(50);
            Assert.True(session.Status().HasMetadata, "the file list never came from the seeder");

            // the app's own file choice picks the video out of the torrent's files (one of them is libtorrent's padding)
            var files = session.Files().Select(f => new TorrentFile(f.Index, f.Path, f.Size)).ToList();
            var pick = TorrentFileChoice.Pick(files, null, 1, 2);
            Assert.NotNull(pick);
            Assert.EndsWith("show.s01e02.mkv", files[pick.Value].Name);
            Assert.Equal(bytes.Length, files[pick.Value].Size);
            Assert.Equal(256 * 1024, session.PieceLength());
            session.SelectFile(pick.Value);
            var source = session.Source(pick.Value, 24);
            source.Request(0);

            using var server = new LocalVideoServer(session.FilePath(pick.Value), bytes.Length, source);
            var url = server.Start();
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(60) };

            // the start, then a jump into the middle, as a player does
            using (var request = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new System.Net.Http.Headers.RangeHeaderValue(0, 2_999_999) } })
            using (var response = await http.SendAsync(request))
            {
                Assert.Equal(System.Net.HttpStatusCode.PartialContent, response.StatusCode);
                Assert.Equal(bytes[..3_000_000], await response.Content.ReadAsByteArrayAsync());
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new System.Net.Http.Headers.RangeHeaderValue(8_000_000, 8_999_999) } })
            using (var response = await http.SendAsync(request))
                Assert.Equal(bytes[8_000_000..9_000_000], await response.Content.ReadAsByteArrayAsync());

            Assert.True(session.Status().Progress > 0); // (connections are not checked: two seeds drop each other once the file is whole)
            session.Stop(true);
        }
        finally
        {
            try
            {
                seeder.StandardInput.Close();
                if (!seeder.WaitForExit(5000)) seeder.Kill();
            }
            catch (Exception)
            {
                // already gone
            }

            try
            {
                Directory.Delete(work, true);
            }
            catch (IOException)
            {
                // left for the temp folder clean-up
            }
        }
    }
}
