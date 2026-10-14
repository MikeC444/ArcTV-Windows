using System.IO;
using ArcTV.Core.Sources;
using ArcTV.Core.Storage;
using ArcTV.Core.Torrents;
using TorrentFile = ArcTV.Core.Sources.TorrentFile;

namespace ArcTV.App.Player;

/// <summary>The libtorrent engine could not be loaded (the DLL is missing, or is not for this PC): the app uses MonoTorrent instead.</summary>
public sealed class LibTorrentUnavailableException(string message) : Exception(message);

/// <summary>
/// Streams a torrent through libtorrent (the engine the Mac app uses), through the native ctorrent.dll: finds the video in it, asks for its pieces in the order they are watched, and serves
/// the file to the player over a local address (<see cref="LocalVideoServer"/>) as the pieces land. One torrent at a time; its files are removed when it stops. The same promises as
/// <see cref="TorrentStreamer"/> (the MonoTorrent engine), so the rest of the app does not know which one is playing.
/// </summary>
public sealed class LibTorrentStreamer : ITorrentStarter
{
    private static readonly TimeSpan MetadataWait = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan BufferWait = TimeSpan.FromSeconds(180);

    public static LibTorrentStreamer Shared { get; } = new();

    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly object _sessionLock = new();
    private LibTorrentSession? _session;
    private LocalVideoServer? _server;
    private string? _folder;
    private DateTime _lastNote = DateTime.MinValue;

    private static string Root => Path.Combine(AppPaths.DataFolder(), "torrents-lt");

    /// <summary>The session, made on first use. Making it loads the native library and opens the engine's ports, so it is always done off the screen's thread.</summary>
    private LibTorrentSession Session()
    {
        lock (_sessionLock)
        {
            if (_session is not null) return _session;
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                _session = new LibTorrentSession();
                PlayerLog.Write($"torrent: libtorrent session created in {timer.ElapsedMilliseconds} ms");
                return _session;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException)
            {
                throw new LibTorrentUnavailableException($"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Starts the engine ahead of time, so its peer finding (the DHT) is already going when a torrent is chosen. libtorrent keeps it going while the session lives.</summary>
    public void Warm() => _ = Task.Run(() =>
    {
        try
        {
            Session();
        }
        catch (LibTorrentUnavailableException ex)
        {
            PlayerLog.Write($"torrent: libtorrent is not available ({ex.Message})");
        }
    });

    public async Task<TorrentStart> StartAsync(SourceItem item, PlayTarget target, Action<TorrentProgress> progress, CancellationToken cancel)
    {
        await _one.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            // off the screen's thread from here on (every wait below says ConfigureAwait(false))
            var session = await Task.Run(Session, CancellationToken.None).ConfigureAwait(false);
            await StopAsync().ConfigureAwait(false);
            var text = TorrentLink.Magnet(item.Stream) ?? throw new TorrentException("This source has no torrent address. Try another source.");
            _folder = Path.Combine(Root, "data", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            // leaving the player stops the torrent at once, wherever it has got to, rather than at the next look
            using var stopOnLeave = cancel.Register(() => _ = StopAsync());
            progress(new TorrentProgress("Finding people who share this…"));
            PlayerLog.Write("torrent: starting with libtorrent");
            try
            {
                session.Start(text, _folder);
            }
            catch (InvalidOperationException ex)
            {
                throw new TorrentException(ex.Message);
            }

            // 1. the file list
            var started = DateTime.UtcNow;
            LibTorrentStatus status;
            while (!(status = session.Status()).HasMetadata)
            {
                cancel.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - started > MetadataWait) throw new TorrentException("No one is sharing this torrent right now (it never gave its file list). Try another source.");
                Note(session, "metadata");
                progress(new TorrentProgress(status.Peers > 0 ? "Getting the file list…" : "Finding people who share this…", status.Peers > 0 ? $"{status.Peers} connected" : null));
                await Task.Delay(500, cancel).ConfigureAwait(false);
            }

            // 2. the video to play
            var files = session.Files().Select(f => new TorrentFile(f.Index, f.Path, f.Size)).ToList();
            var pick = TorrentFileChoice.Pick(files, item.Stream.FileIdx, target.Season, target.Episode) ?? throw new TorrentException("This torrent has no video file in it.");
            var file = files.First(f => f.Index == pick);
            var local = new LocalSettings(Path.Combine(AppPaths.DataFolder(), "settings.json"));
            var limit = local.TorrentStorageGb;
            if (!TorrentStorage.Allows(file.Size, limit))
                throw new TorrentException($"This video is {file.Size / 1_073_741_824.0:0.#} GB, bigger than your torrent storage limit of {TorrentStorage.Label(limit)}. Raise the limit in Settings > Player, or try another source.");
            session.SelectFile(pick);

            // 3. a local address for the player, and the start of the video: the first pieces are asked for first, and the player is given the address once as much of the start as the
            // Torrent buffer setting says has arrived (the way the Mac app does)
            var buffer = TorrentBuffers.From(local.TorrentBuffer);
            var source = session.Source(pick, buffer.PiecesAhead());
            source.Request(0);
            var server = _server = new LocalVideoServer(session.FilePath(pick), file.Size, source);
            var url = server.Start();

            var wanted = Math.Min(file.Size, buffer.StartBytes());
            started = DateTime.UtcNow;
            progress(new TorrentProgress("Getting the start of the video…"));
            while (!session.Have(pick, 0, wanted))
            {
                cancel.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - started > BufferWait) throw new TorrentException("The video did not start downloading in time. It may have too few people sharing it. Try another source.");
                status = session.Status();
                Note(session, "start of video");
                progress(new TorrentProgress("Getting the start of the video…", $"{status.Peers} connected, {status.DownloadRate / 1_000_000.0:0.0} MB/s"));
                await Task.Delay(250, cancel).ConfigureAwait(false);
            }

            PlayerLog.Write($"torrent: the first {wanted / 1_048_576.0:0.#} MB arrived after {(DateTime.UtcNow - started).TotalSeconds:0} s");
            return new TorrentStart(url, () => _ = StopAsync());
        }
        catch (Exception ex) when (ex is not LibTorrentUnavailableException)
        {
            PlayerLog.Write($"torrent: stopped before playing ({ex.GetType().Name}: {ex.Message}{(cancel.IsCancellationRequested ? ", the player was left" : "")})");
            await StopAsync().ConfigureAwait(false);
            if (cancel.IsCancellationRequested && ex is not OperationCanceledException) throw new OperationCanceledException(cancel);
            throw;
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>Writes how the torrent is doing to the player log every few seconds, so a start that never happens can be understood afterwards.</summary>
    private void Note(LibTorrentSession session, string stage)
    {
        if (DateTime.UtcNow - _lastNote < TimeSpan.FromSeconds(5)) return;
        _lastNote = DateTime.UtcNow;
        var s = session.Status();
        PlayerLog.Write($"torrent ({stage}, libtorrent): {s.StateName}, dht {s.DhtNodes} nodes, {s.Connections} connected ({s.Seeds} seeds), {s.KnownPeers} known and waiting, {s.DownloadRate / 1000} kB/s down, {s.Progress * 100:0.0}% of it done");
    }

    /// <summary>Stops the torrent being watched (if any), closes the local address and removes what was downloaded for it.</summary>
    public async Task StopAsync()
    {
        var server = Interlocked.Exchange(ref _server, null);
        var folder = Interlocked.Exchange(ref _folder, null);
        try
        {
            server?.Dispose();
            _session?.Stop(true);
        }
        catch (Exception)
        {
            // stopping is best effort: the files are removed below either way
        }

        if (folder is null) return;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                return;
            }
            catch (Exception)
            {
                await Task.Delay(300).ConfigureAwait(false); // libtorrent lets go of its files a moment after the stop
            }
        }
    }
}
