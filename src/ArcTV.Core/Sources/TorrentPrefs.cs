namespace ArcTV.Core.Sources;

/// <summary>How much of a torrent's start must have arrived before the video starts (Settings &gt; Player), as on the Mac app.</summary>
public enum TorrentBuffer
{
    Small,
    Normal,
    Large,
}

public static class TorrentBuffers
{
    public static TorrentBuffer From(string? text) => Enum.TryParse<TorrentBuffer>(text, true, out var value) ? value : TorrentBuffer.Normal;

    /// <summary>What must have arrived before the video starts.</summary>
    public static long StartBytes(this TorrentBuffer buffer) => buffer switch
    {
        TorrentBuffer.Small => 3L * 1_048_576,
        TorrentBuffer.Large => 16L * 1_048_576,
        _ => 6L * 1_048_576,
    };

    /// <summary>How many pieces after the playing spot the libtorrent engine asks for first (the Mac app's numbers).</summary>
    public static int PiecesAhead(this TorrentBuffer buffer) => buffer switch
    {
        TorrentBuffer.Small => 12,
        TorrentBuffer.Large => 64,
        _ => 24,
    };

    public static string Detail(this TorrentBuffer buffer) => buffer switch
    {
        TorrentBuffer.Small => "Starts soonest; may stall on slow torrents.",
        TorrentBuffer.Large => "Rides out slow patches; takes longer to start and uses more data.",
        _ => "A good balance.",
    };
}

/// <summary>The most disk space a torrent may use (the file being watched is kept until you stop).</summary>
public static class TorrentStorage
{
    public const int DefaultGigabytes = 50;

    /// <summary>Gigabytes; 0 is no limit.</summary>
    public static readonly int[] Options = [5, 10, 20, 50, 0];

    public static string Label(int gigabytes) => gigabytes == 0 ? "No limit" : $"{gigabytes} GB";

    /// <summary>True when a file of <paramref name="size"/> bytes is allowed under <paramref name="gigabytes"/> (0 means anything).</summary>
    public static bool Allows(long size, int gigabytes) => gigabytes == 0 || size <= gigabytes * 1_073_741_824L;
}

/// <summary>Which torrent engine plays torrents: libtorrent (the default, through the native ctorrent.dll) or MonoTorrent (built into the app, and what is used when libtorrent can't load).</summary>
public static class TorrentEngineChoice
{
    public const string LibTorrent = "libtorrent";
    public const string MonoTorrent = "monotorrent";

    public static bool PrefersLibTorrent(string? setting) => !string.Equals(setting, MonoTorrent, StringComparison.OrdinalIgnoreCase);
}
