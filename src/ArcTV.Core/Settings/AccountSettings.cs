using System.Text;
using System.Text.Json;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;

namespace ArcTV.Core.Settings;

/// <summary>
/// The account's settings (<c>/user/settings</c>), one row shared by every app. Changing one thing means sending all of it back, so the whole thing is kept and only the changed
/// field is edited.
/// </summary>
public sealed record AccountSettings(
    IReadOnlyList<string> HomeRowOrder,
    IReadOnlyList<string> HiddenRowIds,
    bool AutoplayNextEpisode = true,
    bool SkipIntroEnabled = true,
    bool SubtitlesEnabled = false,
    string? DefaultSubtitleLanguage = null,
    string? DefaultAudioLanguage = null,
    IReadOnlyList<string>? BlockedGenres = null,
    string? UpdatedAt = null)
{
    public static AccountSettings Default { get; } = new([], []);

    /// <summary>Reads the server's reply. Anything missing takes its usual default, and an odd value is ignored rather than failing the whole read.</summary>
    public static AccountSettings Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Not settings.");
        return new AccountSettings(
            Strings(root, "homeRowOrder") ?? [],
            Strings(root, "hiddenRowIds") ?? [],
            Bool(root, "autoplayNextEpisode", true),
            Bool(root, "skipIntroEnabled", true),
            Bool(root, "subtitlesEnabled", false),
            Text(root, "defaultSubtitleLanguage"),
            Text(root, "defaultAudioLanguage"),
            Strings(root, "blockedGenres"),
            Text(root, "updatedAt"));
    }

    /// <summary>The JSON the server takes. An empty language is sent as null (that clears the account's choice, as the backend expects) rather than left out.</summary>
    public string ToJson(string updatedAt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            WriteStrings(writer, "homeRowOrder", HomeRowOrder);
            WriteStrings(writer, "hiddenRowIds", HiddenRowIds);
            writer.WriteBoolean("autoplayNextEpisode", AutoplayNextEpisode);
            writer.WriteBoolean("skipIntroEnabled", SkipIntroEnabled);
            writer.WriteBoolean("subtitlesEnabled", SubtitlesEnabled);
            writer.WriteString("defaultSubtitleLanguage", string.IsNullOrEmpty(DefaultSubtitleLanguage) ? null : DefaultSubtitleLanguage);
            writer.WriteString("defaultAudioLanguage", string.IsNullOrEmpty(DefaultAudioLanguage) ? null : DefaultAudioLanguage);
            WriteStrings(writer, "blockedGenres", BlockedGenres ?? []);
            writer.WriteString("updatedAt", updatedAt);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Whether two settings hold the same choices (the lists are compared by what is in them).</summary>
    public bool SameChoices(AccountSettings other) => ToJson("") == other.ToJson("");

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static string? Text(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement o, string name, bool fallback) =>
        o.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    private static List<string>? Strings(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList() : null;
}

public static class SettingsApi
{
    public static async Task<AccountSettings> SettingsAsync(this AuthClient client, string accessToken, CancellationToken cancel = default)
    {
        var body = await client.SendJsonAsync(HttpMethod.Get, "/user/settings", null, null, accessToken, cancel: cancel).ConfigureAwait(false);
        try
        {
            return AccountSettings.Parse(body);
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    /// <summary>Saves the whole settings row (last write wins, stamped now) and returns what the backend kept.</summary>
    public static async Task<AccountSettings> SaveSettingsAsync(this AuthClient client, string accessToken, AccountSettings settings, CancellationToken cancel = default)
    {
        var stamp = IsoDate.Now();
        var body = await client.SendJsonAsync(HttpMethod.Put, "/user/settings", null, settings.ToJson(stamp), accessToken, cancel: cancel).ConfigureAwait(false);
        try
        {
            return AccountSettings.Parse(body);
        }
        catch (JsonException)
        {
            return settings with { UpdatedAt = stamp };
        }
    }
}

public static class BlockedGenres
{
    /// <summary>Titles with none of the blocked genres (the other apps hide them everywhere).</summary>
    public static IReadOnlyList<MetaPreview> Without(IReadOnlyList<MetaPreview> items, IReadOnlySet<string> blocked)
    {
        if (blocked.Count == 0) return items;
        return items.Where(item => !(item.Genres ?? []).Any(g => blocked.Contains(g))).ToList();
    }

    /// <summary>The blocked set, compared without regard to case.</summary>
    public static IReadOnlySet<string> Set(IEnumerable<string>? genres) => new HashSet<string>(genres ?? [], StringComparer.OrdinalIgnoreCase);
}
