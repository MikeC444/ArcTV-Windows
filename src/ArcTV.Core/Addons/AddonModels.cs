using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArcTV.Core.Addons;

/// <summary>The parts of a Stremio addon manifest the app uses (the same protocol the Android, web and Mac apps speak).</summary>
public sealed record AddonManifest(
    string Id,
    string Name,
    string? Version = null,
    string? Description = null,
    List<CatalogDefinition>? Catalogs = null,
    List<string>? Types = null,
    List<string>? IdPrefixes = null,
    List<AddonResource>? Resources = null)
{
    /// <summary>Whether this addon can give streams for <paramref name="titleId"/> of <paramref name="type"/>. One that says nothing about types or prefixes is asked for everything.</summary>
    public bool ProvidesStreams(string type, string titleId)
    {
        foreach (var resource in (Resources ?? []).Where(r => r.Name == "stream"))
        {
            var allowedTypes = resource.Types ?? Types;
            var prefixes = resource.IdPrefixes ?? IdPrefixes;
            if (allowedTypes is not null && !allowedTypes.Contains(type)) continue;
            if (prefixes is { Count: > 0 } && !prefixes.Any(p => titleId.StartsWith(p, StringComparison.Ordinal))) continue;
            return true;
        }
        return false;
    }

    public bool OffersCatalog(string type, string catalogId) => (Catalogs ?? []).Any(c => c.Type == type && c.Id == catalogId);
}

/// <summary>A manifest's <c>resources</c> entry: a plain name ("catalog") or {"name": "stream", "types": [...], "idPrefixes": [...]}.</summary>
[JsonConverter(typeof(AddonResourceConverter))]
public sealed record AddonResource(string Name, List<string>? Types = null, List<string>? IdPrefixes = null);

/// <summary>Never throws on an odd entry: one bad resource must not make the whole manifest unreadable.</summary>
public sealed class AddonResourceConverter : JsonConverter<AddonResource>
{
    public override AddonResource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String) return new AddonResource(root.GetString() ?? "");
        if (root.ValueKind != JsonValueKind.Object) return new AddonResource("");
        return new AddonResource(Text(root, "name") ?? "", Strings(root, "types"), Strings(root, "idPrefixes"));
    }

    private static string? Text(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string>? Strings(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : null;

    public override void Write(Utf8JsonWriter writer, AddonResource value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        if (value.Types is not null) { writer.WritePropertyName("types"); JsonSerializer.Serialize(writer, value.Types, options); }
        if (value.IdPrefixes is not null) { writer.WritePropertyName("idPrefixes"); JsonSerializer.Serialize(writer, value.IdPrefixes, options); }
        writer.WriteEndObject();
    }
}

/// <summary>A filter a catalogue accepts: "genre" (with the options it lists), "skip" (paging), "search".</summary>
public sealed record CatalogExtra(string Name, bool? IsRequired = null, List<string>? Options = null);

public sealed record CatalogDefinition(string Type, string Id, string? Name = null, List<CatalogExtra>? Extra = null)
{
    [JsonIgnore] public CatalogExtra? GenreExtra => (Extra ?? []).FirstOrDefault(e => e.Name == "genre");
    [JsonIgnore] public List<string> GenreOptions => GenreExtra?.Options ?? [];

    /// <summary>A catalogue that does not need a genre to answer.</summary>
    [JsonIgnore] public bool IsBase => GenreExtra?.IsRequired != true;

    [JsonIgnore] public bool SupportsSkip => (Extra ?? []).Any(e => e.Name == "skip");
    [JsonIgnore] public bool SupportsSearch => (Extra ?? []).Any(e => e.Name == "search");
}

/// <summary>One title in a catalogue: enough for a poster card now and a details page later.</summary>
public sealed record MetaPreview(
    string Id,
    string Type,
    string Name,
    string? Poster = null,
    string? Background = null,
    string? Logo = null,
    string? Description = null,
    string? ReleaseInfo = null,
    string? ImdbRating = null,
    List<string>? Genres = null,
    string? Runtime = null)
{
    /// <summary>The wide picture of the title for a landscape card: its landscape artwork, else its poster.</summary>
    [JsonIgnore]
    public string? WidePicture => string.IsNullOrEmpty(Background) ? Poster : Background;

    /// <summary>Set on titles from My List that have been watched (shows a tick on the poster). Not part of what addons send.</summary>
    [JsonIgnore]
    public bool Watched { get; init; }
}

internal sealed record CatalogResponse(List<MetaPreview>? Metas);

/// <summary>An addon on the account, as <c>/user/addons</c> lists it.</summary>
public sealed record SyncedAddon(string ManifestUrl, string AddonId, string Name, bool Enabled, int SortOrder, AddonManifest? ManifestJson);

internal sealed record SyncedAddonsResponse(List<SyncedAddon> Items);
