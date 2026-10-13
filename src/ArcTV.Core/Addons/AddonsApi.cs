using System.Text;
using System.Text.Json;
using ArcTV.Core.Account;

namespace ArcTV.Core.Addons;

public static class AddonsApi
{
    /// <summary>The account's installed addons, in their order. Switched-off ones are left out unless asked for.</summary>
    public static async Task<List<SyncedAddon>> AddonsAsync(this AuthClient client, string accessToken, bool includeDisabled = false, CancellationToken cancel = default)
    {
        var body = await client.SendJsonAsync(HttpMethod.Get, "/user/addons", null, null, accessToken, cancel: cancel).ConfigureAwait(false);
        SyncedAddonsResponse? parsed;
        var raw = new Dictionary<string, string>();
        try
        {
            parsed = JsonSerializer.Deserialize<SyncedAddonsResponse>(body, ApiHttp.Json);
            // a second, loose read keeps each manifest's own JSON as it was sent, to send back unchanged
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("manifestUrl", out var url) && url.ValueKind == JsonValueKind.String
                        && item.TryGetProperty("manifestJson", out var manifest) && manifest.ValueKind == JsonValueKind.Object)
                        raw[url.GetString()!] = manifest.GetRawText();
                }
            }
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }

        return (parsed?.Items ?? [])
            .Select(a => raw.TryGetValue(a.ManifestUrl, out var text) ? a with { RawManifest = text } : a)
            .Where(a => includeDisabled || a.Enabled)
            .OrderBy(a => a.SortOrder)
            .ToList();
    }

    /// <summary>The JSON the server takes to install or update an addon (the manifest goes in exactly as given).</summary>
    public static string SaveBody(string manifestUrl, string addonId, string name, string rawManifest, bool enabled, int sortOrder, string updatedAt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("manifestUrl", manifestUrl);
            writer.WriteString("addonId", addonId);
            writer.WriteString("name", name);
            writer.WritePropertyName("manifestJson");
            writer.WriteRawValue(rawManifest);
            writer.WriteBoolean("enabled", enabled);
            writer.WriteNumber("sortOrder", sortOrder);
            writer.WriteString("updatedAt", updatedAt);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Installs an addon on the account, or updates it (turn it on or off, move it).</summary>
    public static async Task SaveAddonAsync(this AuthClient client, string accessToken, string manifestUrl, string addonId, string name, string rawManifest, bool enabled, int sortOrder, CancellationToken cancel = default) =>
        await client.SendJsonAsync(HttpMethod.Post, "/user/addons", null, SaveBody(manifestUrl, addonId, name, rawManifest, enabled, sortOrder, IsoDate.Now()), accessToken, cancel: cancel).ConfigureAwait(false);

    public static async Task RemoveAddonAsync(this AuthClient client, string accessToken, string manifestUrl, CancellationToken cancel = default) =>
        await client.SendJsonAsync(HttpMethod.Delete, "/user/addons", new Dictionary<string, string> { ["manifestUrl"] = manifestUrl, ["updatedAt"] = IsoDate.Now() }, null, accessToken, cancel: cancel).ConfigureAwait(false);
}
