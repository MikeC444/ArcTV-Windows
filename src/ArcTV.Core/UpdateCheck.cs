using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArcTV.Core;

/// <summary>A published version of the app on GitHub.</summary>
public sealed record ReleaseInfo(string Version, string Notes, string? PageUrl, string? DownloadUrl);

/// <summary>What a check for updates found: <see cref="Available"/> with the release, or up to date (and the newest version known, if any).</summary>
public sealed record UpdateResult(bool Available, string? Latest, ReleaseInfo? Release);

public static class UpdateCheck
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/MikeC444/ArcTV-Windows/releases/latest";

    /// <summary>True when <paramref name="candidate"/> is a higher version than <paramref name="current"/>. Accepts a leading "v" and ignores anything after a "-" (like "-beta").</summary>
    public static bool IsNewer(string candidate, string current)
    {
        var a = Parts(candidate);
        var b = Parts(current);
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : 0;
            var y = i < b.Count ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    private static List<int> Parts(string version)
    {
        var text = version.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var dash = text.IndexOf('-');
        if (dash >= 0) text = text[..dash];
        return text.Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToList();
    }

    private sealed class Release
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<Asset>? Assets { get; set; }
    }

    private sealed class Asset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    }

    /// <summary>Reads GitHub's "latest release" reply. The download is the installer (an .exe) when the release has one, else the zip.</summary>
    public static ReleaseInfo Parse(string json)
    {
        var release = JsonSerializer.Deserialize<Release>(json) ?? throw new JsonException("Not a release.");
        var version = release.TagName;
        if (version.StartsWith('v') || version.StartsWith('V')) version = version[1..];
        var assets = release.Assets ?? new List<Asset>();
        var download = assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                       ?? assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        return new ReleaseInfo(version, (release.Body ?? "").Trim(), release.HtmlUrl, download?.BrowserDownloadUrl);
    }

    /// <summary>Compares a GitHub reply with the running version. No release published yet (404) counts as up to date.</summary>
    public static UpdateResult Result(int statusCode, string json, string current)
    {
        if (statusCode == 404) return new UpdateResult(false, null, null);
        if (statusCode is < 200 or >= 300) throw new HttpRequestException($"GitHub answered {statusCode}.");
        var info = Parse(json);
        return IsNewer(info.Version, current) ? new UpdateResult(true, info.Version, info) : new UpdateResult(false, info.Version, null);
    }

    public static async Task<UpdateResult> CheckAsync(string current, HttpClient http, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("ArcTV-Windows");
        using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        return Result((int)response.StatusCode, body, current);
    }
}
