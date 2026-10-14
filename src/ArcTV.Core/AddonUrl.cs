namespace ArcTV.Core;

/// <summary>
/// Normalizes the many shapes an addon URL is shared in ("stremio://" links, a bare host, a URL without manifest.json) into a manifest URL,
/// the same rules as the Android, web and Mac apps.
/// </summary>
public static class AddonUrl
{
    public static string NormalizeManifestUrl(string input)
    {
        var url = input.Trim();
        if (url.StartsWith("stremio://", StringComparison.Ordinal)) url = "https://" + url["stremio://".Length..];
        if (!url.StartsWith("http://", StringComparison.Ordinal) && !url.StartsWith("https://", StringComparison.Ordinal)) url = "https://" + url;
        var last = url.Split('/')[^1];
        if (!string.Equals(last, "manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            url = url.TrimEnd('/');
            url += "/manifest.json";
        }
        return url;
    }

    /// <summary>The addon's resource base: the manifest URL with <c>manifest.json</c> taken off.</summary>
    public static string ResourceBase(string manifestUrl)
    {
        var baseUrl = manifestUrl;
        if (baseUrl.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)) baseUrl = baseUrl[..^"manifest.json".Length];
        return baseUrl.TrimEnd('/');
    }

    /// <summary>One URL path segment, percent-encoded (spaces as %20, slashes encoded too).</summary>
    internal static string Encode(string segment) => Uri.EscapeDataString(segment);

    /// <summary><c>&lt;base&gt;/catalog/&lt;type&gt;/&lt;id&gt;[/key=value&amp;key=value].json</c>, as Stremio addons expect.</summary>
    public static string CatalogUrl(string manifestUrl, string type, string catalogId, IReadOnlyDictionary<string, string>? extra = null)
    {
        var url = $"{ResourceBase(manifestUrl)}/catalog/{Encode(type)}/{Encode(catalogId)}";
        if (extra is { Count: > 0 })
        {
            var pairs = extra.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{Encode(pair.Key)}={Encode(pair.Value)}");
            url += "/" + string.Join("&", pairs);
        }
        return url + ".json";
    }

    /// <summary>A title id for a path: each ":"-separated part encoded on its own, so the colons in "tt123:1:2" stay (addons expect them literal).</summary>
    internal static string EncodeId(string id) => string.Join(":", id.Split(':').Select(Encode));

    /// <summary><c>&lt;base&gt;/meta/&lt;type&gt;/&lt;id&gt;.json</c>: one title's details.</summary>
    public static string MetaUrl(string manifestUrl, string type, string id) => $"{ResourceBase(manifestUrl)}/meta/{Encode(type)}/{EncodeId(id)}.json";

    /// <summary><c>&lt;base&gt;/stream/&lt;type&gt;/&lt;id&gt;.json</c>: the streams an addon offers for a title or episode.</summary>
    /// <summary>An addon's subtitles for a video: <c>/subtitles/{type}/{id}.json</c>, with the file name as an extra (<c>/subtitles/{type}/{id}/filename=....json</c>) when it is known, which addons use to match a release.</summary>
    public static string SubtitlesUrl(string manifestUrl, string type, string id, string? filename = null) =>
        $"{ResourceBase(manifestUrl)}/subtitles/{Encode(type)}/{EncodeId(id)}{(string.IsNullOrEmpty(filename) ? "" : $"/filename={Encode(filename)}")}.json";

    public static string StreamUrl(string manifestUrl, string type, string id) => $"{ResourceBase(manifestUrl)}/stream/{Encode(type)}/{EncodeId(id)}.json";
}
