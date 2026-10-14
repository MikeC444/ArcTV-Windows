using System.Text.Json;
using ArcTV.Core.Account;

namespace ArcTV.Core.Downloads;

/// <summary>
/// What Download use reports to the web admin dashboard (the same three events as the web app): "popup" (no Plus: the Plus popup was shown), "opened" (the Downloads panel opened) and "file" (a file in
/// it was pressed, with its short quality label and whether the debrid service already had it). Never a title, a content id, a release name or a file address.
/// </summary>
public sealed record DownloadEvent(string Kind, bool Movie, bool Episode, string? Resolution = null, bool? Cached = null)
{
    public const string Popup = "popup", Opened = "opened", File = "file";

    /// <summary>The request body the server expects (the quality and cached flag only go with a pressed file).</summary>
    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["kind"] = Kind,
        ["contentType"] = Movie ? "MOVIE" : "TV_SHOW",
        ["episode"] = Episode,
        ["resolution"] = Kind == File ? Resolution : null,
        ["cached"] = Kind == File ? Cached : null,
    }, ApiHttp.Json);
}

public static class DownloadEventsApi
{
    /// <summary>Tells the dashboard (fire and forget: the caller ignores failures).</summary>
    public static Task ReportDownloadAsync(this AuthClient client, string accessToken, DownloadEvent entry, CancellationToken cancel = default) =>
        client.SendJsonAsync(HttpMethod.Post, "/user/player-events/download", null, entry.ToJson(), accessToken, cancel: cancel);
}
