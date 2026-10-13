using System.IO;
using ArcTV.Core.Storage;

namespace ArcTV.App.Player;

/// <summary>
/// A short log of what the player did (player.log next to the other app data, and mpv's own mpv.log): when a video will not start, these say where it stopped. Links are never written down,
/// only the address of the server they point at.
/// </summary>
internal static class PlayerLog
{
    private static readonly object Gate = new();

    public static string LogFile => Path.Combine(AppPaths.DataFolder(), "player.log");

    public static string MpvFile => Path.Combine(AppPaths.DataFolder(), "mpv.log");

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                var path = LogFile;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 200_000) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // a log must never get in the way
        }
    }
}
