using ArcTV.Core.Sources;
using ArcTV.Core.Storage;
using System.IO;

namespace ArcTV.App.Player;

/// <summary>
/// Picks the engine that plays a torrent: libtorrent (the default) or MonoTorrent (Settings &gt; Player), and falls back to MonoTorrent for good (until the app restarts) the first time libtorrent
/// cannot be loaded, so a missing or broken ctorrent.dll never stops torrents from playing.
/// </summary>
public sealed class TorrentEngines(LocalSettings settings) : ITorrentStarter
{
    public static TorrentEngines Shared { get; } = new(new LocalSettings(Path.Combine(AppPaths.DataFolder(), "settings.json")));

    private volatile string? _libTorrentProblem;

    /// <summary>Why libtorrent could not be loaded (null while nothing has gone wrong): Settings shows it.</summary>
    public string? LibTorrentProblem => _libTorrentProblem;

    private bool UseLibTorrent => TorrentEngineChoice.PrefersLibTorrent(settings.TorrentEngine) && _libTorrentProblem is null;

    public void Warm()
    {
        if (UseLibTorrent) LibTorrentStreamer.Shared.Warm();
        else TorrentStreamer.Shared.Warm();
    }

    public async Task<TorrentStart> StartAsync(SourceItem item, PlayTarget target, Action<TorrentProgress> progress, CancellationToken cancel)
    {
        if (UseLibTorrent)
        {
            try
            {
                return await LibTorrentStreamer.Shared.StartAsync(item, target, progress, cancel).ConfigureAwait(false);
            }
            catch (LibTorrentUnavailableException ex)
            {
                _libTorrentProblem = ex.Message;
                PlayerLog.Write($"torrent: libtorrent could not be loaded ({ex.Message}); using MonoTorrent instead");
            }
        }

        PlayerLog.Write("torrent: starting with MonoTorrent");
        return await TorrentStreamer.Shared.StartAsync(item, target, progress, cancel).ConfigureAwait(false);
    }
}
