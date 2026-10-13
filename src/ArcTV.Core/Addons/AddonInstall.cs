using System.Text.Json;
using ArcTV.Core.Account;

namespace ArcTV.Core.Addons;

/// <summary>What an addon's own manifest says it is, read from a link the person pasted.</summary>
public sealed record AddonInstall(string ManifestUrl, string AddonId, string Name, string RawManifest)
{
    /// <summary>Fetches the manifest at <paramref name="input"/> (a link in any of the usual shapes) and checks it looks like an addon.</summary>
    public static async Task<AddonInstall> FetchAsync(string input, HttpClient? http = null, CancellationToken cancel = default)
    {
        var url = AddonUrl.NormalizeManifestUrl(input);
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw ApiException.BadUrl(input);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var body = await ApiHttp.SendAsync(request, http, cancel: cancel).ConfigureAwait(false);
        return Parse(url, body);
    }

    /// <summary>An addon needs an id and a name; anything else is not an addon.</summary>
    public static AddonInstall Parse(string manifestUrl, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw ApiException.Unreadable();
            var id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) throw ApiException.Unreadable();
            return new AddonInstall(manifestUrl, id, name, root.GetRawText());
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }
}
