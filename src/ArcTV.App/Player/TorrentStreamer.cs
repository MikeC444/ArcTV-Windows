using System.IO;
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

    private ClientEngine Engine => _engine ??= new ClientEngine(new EngineSettingsBuilder { CacheDirectory = Path.Combine(Root, "cache") }.ToSettings());

    public async Task<TorrentStart> StartAsync(SourceItem item, PlayTarget target, Action<TorrentProgress> progress, CancellationToken cancel)
    {
        await _one.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            await StopAsync().ConfigureAwait(false);
            var text = TorrentLink.Magnet(item.Stream) ?? throw new TorrentException("This source has no torrent address. Try another source.");
            var magnet = MagnetLink.Parse(text);
            _folder = Path.Combine(Root, "data", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            progress(new TorrentProgress("Finding people who share this…"));
            var manager = _manager = await Engine.AddStreamingAsync(magnet, _folder).ConfigureAwait(false);
            await manager.StartAsync().ConfigureAwait(false);

            // 1. the file list
            var metadata = manager.WaitForMetadataAsync(cancel);
            var started = DateTime.UtcNow;
            while (!metadata.IsCompleted)
            {
                cancel.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - started > MetadataWait) throw new TorrentException("No one is sharing this torrent right now (it never gave its file list). Try another source.");
                var peers = manager.OpenConnections;
                progress(new TorrentProgress(peers > 0 ? "Getting the file list…" : "Finding people who share this…", peers > 0 ? $"{peers} connected" : null));
                await Task.WhenAny(metadata, Task.Delay(500, cancel)).ConfigureAwait(false);
            }

            await metadata.ConfigureAwait(false);

            // 2. the video to play
            var files = manager.Files.Select((f, i) => new TorrentFile(i, f.Path, f.Length)).ToList();
            var pick = TorrentFileChoice.Pick(files, item.Stream.FileIdx, target.Season, target.Episode) ?? throw new TorrentException("This torrent has no video file in it.");
            var file = manager.Files[pick];

            // 3. the start of it (the player is given its address once the first parts have arrived)
            progress(new TorrentProgress("Getting the start of the video…"));
            var opening = manager.StreamProvider.CreateHttpStreamAsync(file, true);
            started = DateTime.UtcNow;
            while (!opening.IsCompleted)
            {
                cancel.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - started > BufferWait) throw new TorrentException("The video did not start downloading in time. It may have too few people sharing it. Try another source.");
                progress(new TorrentProgress("Getting the start of the video…", $"{manager.OpenConnections} connected, {manager.Monitor.DownloadRate / 1_000_000.0:0.0} MB/s"));
                await Task.WhenAny(opening, Task.Delay(500, cancel)).ConfigureAwait(false);
            }

            var stream = await opening.ConfigureAwait(false);
            _stream = stream;
            return new TorrentStart(new Uri(stream.FullUri), () => _ = StopAsync());
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _one.Release();
        }
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
