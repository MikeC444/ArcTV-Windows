using System.Net;
using System.Text.RegularExpressions;

namespace ArcTV.Core;

public static partial class Text
{
    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    /// <summary>Descriptions from addons sometimes carry HTML tags and entities (&amp;amp;, &amp;#39;); show them as plain text.</summary>
    public static string HtmlDecoded(string? text) => string.IsNullOrEmpty(text) ? "" : WebUtility.HtmlDecode(Tags().Replace(text, " ")).Replace("  ", " ").Trim();
}
