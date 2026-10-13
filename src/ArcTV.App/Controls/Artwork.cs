using System.Text.RegularExpressions;

namespace ArcTV.App.Controls;

public static partial class Artwork
{
    [GeneratedRegex(@"(image\.tmdb\.org/t/p/)w\d+(/)", RegexOptions.IgnoreCase)]
    private static partial Regex TmdbSize();

    /// <summary>
    /// Addons hand out small copies of a title's wide picture, which go soft or blocky when stretched across a window. The two artwork servers they mostly use keep
    /// bigger ones under a different size in the address: Cinemeta's (medium, large) and TMDB's (w300, w780, ... w1280). Ask for the big one (a picture falls back to
    /// the address the addon gave when there is none). Any other address is left alone.
    /// </summary>
    public static string? Bigger(string? url)
    {
        if (url is null) return null;
        if (url.Contains("/background/medium/", StringComparison.Ordinal)) return url.Replace("/background/medium/", "/background/large/", StringComparison.Ordinal);
        return TmdbSize().Replace(url, "${1}w1280${2}");
    }
}
