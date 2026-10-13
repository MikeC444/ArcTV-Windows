using System.IO;
using System.Security.Cryptography;
using MonoTorrent;
using MonoTorrent.Client;
using ArcTV.Core.Sources;
using ArcTV.Core.Storage;
using TorrentFile = ArcTV.Core.Sources.TorrentFile; // (MonoTorrent has a type of that name too)

namespace ArcTV.App.Player;

/// <summary>What the person sees while a torrent gets ready.</summary>
public sealed record TorrentProgress(string Message, string? Detail = null);

/// <summary>A torrent ready to play: the local address of its video and how to stop it (when the player is closed).</summary>
public sealed record TorrentStart(Uri Url, Action Release);

/// <summary>Something went wrong getting a torrent ready; the message says what to do.</summary>
public sealed class TorrentException(string message) : Exception(message);

/// <summary>Starts a torrent for the player. The real one is <see cref="TorrentStreamer"/>; the tests use a stand-in.</summary>
public interface ITorrentStarter
{
    Task<TorrentStart> StartAsync(SourceItem item, PlayTarget target, Action<TorrentProgress> progress, CancellationToken cancel);

    /// <summary>Gets the torrent engine's peer finding going ahead of time (a torrent about to be chosen then starts sooner).</summary>
    void Warm() { }
}

/// <summary>
/// Streams a torrent through MonoTorrent: finds the video in it, downloads it in the order it is watched and serves it to the player over a local address (a seekable HTTP stream).
/// One torrent at a time (the one being watched); its files are removed when it stops.
/// </summary>
public sealed class TorrentStreamer : ITorrentStarter
{
    private static readonly TimeSpan MetadataWait = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan BufferWait = TimeSpan.FromSeconds(180);

    public static TorrentStreamer Shared { get; } = new();

    private readonly SemaphoreSlim _one = new(1, 1);
    private ClientEngine? _engine;
    private TorrentManager? _manager;
    private object? _stream;
    private string? _folder;

    private static string Root => Path.Combine(AppPaths.DataFolder(), "torrents");

    /// <summary>
    /// The engine's defaults are careful (150 connections, 8 being opened at a time, a 5 MB disk cache) and found fewer people, more slowly, than the Mac app's engine does: this opens many more
    /// connections at once, caches more on the way to the disk, and tries to open the router's port so people can connect back.
    /// </summary>
    private ClientEngine Engine => _engine ??= new ClientEngine(new EngineSettingsBuilder
    {
        CacheDirectory = Path.Combine(Root, "cache"),
        MaximumConnections = 600,
        MaximumHalfOpenConnections = 100,
        DiskCacheBytes = 128 * 1024 * 1024,
        AllowPortForwarding = true,
        AllowLocalPeerDiscovery = true,
    }.ToSettings());

    private readonly SemaphoreSlim _keepLock = new(1, 1);
    private TorrentManager? _keep;

    /// <summary>
    /// The engine shuts its peer finding (the DHT) and its router port mapping down whenever its last torrent stops, so every torrent started cold and took many seconds to find anyone. This keeps one idle
    /// torrent (a made-up one, asked about nowhere but the DHT) running, so the engine stays up between the torrents that are watched.
    /// </summary>
    private async Task KeepEngineRunningAsync()
    {
        await _keepLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_keep is not null) return;
            var folder = Path.Combine(Root, "keep");
            Directory.CreateDirectory(folder);
            var magnet = MagnetLink.Parse("magnet:?xt=urn:btih:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)));
            var manager = await Engine.AddAsync(magnet, folder).ConfigureAwait(false);
            await manager.StartAsync().ConfigureAwait(false);
            _keep = manager;
            PlayerLog.Write("torrent: engine kept running");
        }
        catch (Exception ex)
        {
            PlayerLog.Write($"torrent: could not keep the engine running ({ex.Message})");
        }
        finally
        {
            _keepLock.Release();
        }
    }

    public void Warm() => _ = KeepEngineRunningAsync();

    public async Task<TorrentStart> StartAsync(SourceItem item, PlayTarget target, Action<TorrentProgress> progress, CancellationToken cancel)
    {
        await _one.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            await StopAsync().ConfigureAwait(false);
            await KeepEngineRunningAsync().ConfigureAwait(false);
            var text = TorrentLink.Magnet(item.Stream) ?? throw new TorrentException("This source has no torrent address. Try another source.");
            var magnet = MagnetLink.Parse(text);
            _folder = Path.Combine(Root, "data", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            // leaving the player stops the torrent at once, wherever it has got to, rather than at the next look
            using var stopOnLeave = cancel.Register(() => _ = StopAsync());
            progress(new TorrentProgress("Finding people who share this…"));
            var settings = new TorrentSettingsBuilder { MaximumConnections = 250 }.ToSettings(); // (60 by default)
            var manager = _manager = await Engine.AddStreamingAsync(magnet, _folder, settings).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            await manager.StartAsync().ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();

            // 1. the file list
            var metadata = manager.WaitForMetadataAsync(cancel);
            var started = DateTime.UtcNow;
            while (!metadata.IsCompleted)
            {
                cancel.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - started > MetadataWait) throw new TorrentException("No one is sharing this torrent right now (it never gave its file list). Try another source.");
                var peers = manager.OpenConnections;
                Note(manager, "metadata");
                progress(new TorrentProgress(peers > 0 ? "Getting the file list…" : "Finding people who share this…", peers > 0 ? $"{peers} connected" : null));
                await Task.WhenAny(metadata, Task.Delay(500, cancel)).ConfigureAwait(false);
            }

            await metadata.ConfigureAwait(false);

            // 2. the video to play
            var files = manager.Files.Select((f, i) => new TorrentFile(i, f.Path, f.Length)).ToList();
            var pick = TorrentFileChoice.Pick(files, item.Stream.FileIdx, target.Season, target.Episode) ?? throw new TorrentException("This torrent has no video file in it.");
            var file = manager.Files[pick];
            var local = new LocalSettings(Path.Combine(AppPaths.DataFolder(), "settings.json"));
            var limit = local.TorrentStorageGb;
            if (!TorrentStorage.Allows(file.Length, limit))
                throw new TorrentException($"This video is {file.Length / 1_073_741_824.0:0.#} GB, bigger than your torrent storage limit of {TorrentStorage.Label(limit)}. Raise the limit in Settings > Player, or try another source.");

            // 3. the start of it (the player is given its address once the first parts have arrived)
            progress(new TorrentProgress("Getting the start of the video…"));
            // a torrent whose file list was kept from an earlier time is checked on disk first: reading from it during the check fails ("Could not read the requested data")
            while (manager.State is TorrentState.Hashing or TorrentState.Starting or TorrentState.Metadata)
            {
                cancel.ThrowIfCancellationRequested();
                Note(manager, "getting ready");
                await Task.Delay(100, cancel).ConfigureAwait(false);
            }

            var provider = manager.StreamProvider ?? throw new TorrentException("The torrent could not be streamed. Try another source.");
            // not the library's own wait for the first and last parts of the file (the last part of a big file can be the rarest, and holds up the start for nothing). The stream is made without it,
            // and the start is read through the stream's own address: reading is what makes the torrent fetch those parts first (left alone it fetches parts from all over the file), and the read
            // finishes when as much of the start as the Torrent buffer setting says has arrived, the way the Mac app does
            var stream = await provider.CreateHttpStreamAsync(file, false, cancel).ConfigureAwait(false);
            _stream = stream;
            var wanted = Math.Min(file.Length, TorrentBuffers.From(local.TorrentBuffer).StartBytes());
            long got = 0;
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
            var reading = Task.Run(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, stream.FullUri);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, wanted - 1);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
                var buffer = new byte[81920];
                int count;
                while ((count = await body.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0) Interlocked.Add(ref got, count);
            }, cancel);
            started = DateTime.UtcNow;
            while (!reading.IsCompleted)
            {
                cancel.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - started > BufferWait) throw new TorrentException("The video did not start downloading in time. It may have too few people sharing it. Try another source.");
                Note(manager, "start of video");
                progress(new TorrentProgress("Getting the start of the video…", $"{manager.OpenConnections} connected, {manager.Monitor.DownloadRate / 1_000_000.0:0.0} MB/s, {Interlocked.Read(ref got) * 100 / Math.Max(1, wanted)}% of the start"));
                await Task.WhenAny(reading, Task.Delay(500, cancel)).ConfigureAwait(false);
            }

            await reading.ConfigureAwait(false);
            PlayerLog.Write($"torrent: the first {wanted / 1_048_576.0:0.#} MB arrived after {(DateTime.UtcNow - started).TotalSeconds:0} s");

            return new TorrentStart(new Uri(stream.FullUri), () => _ = StopAsync());
        }
        catch (Exception ex)
        {
            PlayerLog.Write($"torrent: stopped before playing ({ex.GetType().Name}: {ex.Message}{(cancel.IsCancellationRequested ? ", the player was left" : "")})");
            await StopAsync().ConfigureAwait(false);
            // leaving the player stops the torrent under the read that is still waiting, which then fails with its own error: that is the person leaving, not a failure
            if (cancel.IsCancellationRequested && ex is not OperationCanceledException) throw new OperationCanceledException(cancel);
            throw;
        }
        finally
        {
            _one.Release();
        }
    }

    private DateTime _lastNote = DateTime.MinValue;

    /// <summary>Writes how the torrent is doing to the player log every few seconds, so a start that never happens can be understood afterwards.</summary>
    private void Note(TorrentManager manager, string stage)
    {
        if (DateTime.UtcNow - _lastNote < TimeSpan.FromSeconds(5)) return;
        _lastNote = DateTime.UtcNow;
        PlayerLog.Write($"torrent ({stage}): state {manager.State}, dht {_engine?.Dht.State} with {_engine?.Dht.NodeCount} nodes, {manager.OpenConnections} connected, {manager.Peers.Available} known and waiting, {manager.Monitor.DownloadRate / 1000} kB/s down, {manager.Progress:0.0}% of it done");
    }

    /// <summary>Stops the torrent being watched (if any) and removes what was downloaded for it.</summary>
    public async Task StopAsync()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        var manager = Interlocked.Exchange(ref _manager, null);
        var folder = Interlocked.Exchange(ref _folder, null);
        try
        {
            (stream as IDisposable)?.Dispose();
            if (manager is not null)
            {
                await manager.StopAsync().ConfigureAwait(false);
                if (_engine is not null) await _engine.RemoveAsync(manager).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // stopping is best effort: the files are removed below either way
        }

        try
        {
            if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch (Exception)
        {
            // a file still in use goes with the next clean-up
        }
    }
}
