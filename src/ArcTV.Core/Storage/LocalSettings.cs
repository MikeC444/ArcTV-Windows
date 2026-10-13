using System.Text.Json;

namespace ArcTV.Core.Storage;

/// <summary>Small per-PC choices kept in settings.json (not secret).</summary>
public sealed class LocalSettings(string path)
{
    private sealed record Data(string? ServerUrl, string? SubtitleSize = null, string? SubtitleColor = null, bool SmartPicking = false, string? TorrentBuffer = null, int TorrentStorageGb = 50, string? DownloadFolder = null);

    public string? ServerUrl
    {
        get => Read().ServerUrl;
        set => Write(Read() with { ServerUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/') });
    }

    /// <summary>How big subtitles are drawn, kept on this PC (a PC and a TV want different sizes). Null until chosen.</summary>
    public string? SubtitleSize
    {
        get => Read().SubtitleSize;
        set => Write(Read() with { SubtitleSize = value });
    }

    public string? SubtitleColor
    {
        get => Read().SubtitleColor;
        set => Write(Read() with { SubtitleColor = value });
    }

    /// <summary>Plus: start the best source by itself instead of showing the list (kept on this PC).</summary>
    public bool SmartPicking
    {
        get => Read().SmartPicking;
        set => Write(Read() with { SmartPicking = value });
    }

    /// <summary>How much of a torrent's start must arrive before it plays: Small, Normal (the default) or Large.</summary>
    public string? TorrentBuffer
    {
        get => Read().TorrentBuffer;
        set => Write(Read() with { TorrentBuffer = value });
    }

    /// <summary>The most disk space (in gigabytes) a torrent may use while it plays; 0 is no limit.</summary>
    public int TorrentStorageGb
    {
        get => Read().TorrentStorageGb;
        set => Write(Read() with { TorrentStorageGb = value });
    }

    /// <summary>Where downloads are saved; null until chosen (then the Windows Downloads folder).</summary>
    public string? DownloadFolder
    {
        get => Read().DownloadFolder;
        set => Write(Read() with { DownloadFolder = string.IsNullOrWhiteSpace(value) ? null : value });
    }

    private Data Read()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? new Data(null) : new Data(null);
        }
        catch (Exception)
        {
            return new Data(null);
        }
    }

    private void Write(Data data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(data));
    }
}
