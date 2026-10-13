using System.Diagnostics;

namespace ArcTV.App.Controls;

public static class WebLinks
{
    /// <summary>Opens a web address in the person's browser (a failure to open it is not worth stopping for).</summary>
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // no browser to open
        }
    }

    public static void OpenTrailer(string youtubeId) => Open($"https://www.youtube.com/watch?v={Uri.EscapeDataString(youtubeId)}");
}
