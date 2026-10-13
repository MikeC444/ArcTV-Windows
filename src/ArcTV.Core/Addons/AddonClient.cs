using System.Text.Json;
using ArcTV.Core.Account;

namespace ArcTV.Core.Addons;

/// <summary>Talks the Stremio addon protocol: fetch a manifest, then use it to pull catalogues.</summary>
public sealed class AddonClient(HttpClient? http = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };

    private async Task<T> GetAsync<T>(string url, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var body = await ApiHttp.SendAsync(request, http, cancel: cancel).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(body, Options) ?? throw ApiException.Unreadable();
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    public Task<AddonManifest> FetchManifestAsync(string manifestUrl, CancellationToken cancel = default) => GetAsync<AddonManifest>(manifestUrl, cancel);

    /// <summary>One title's details from an addon, or null when the addon has none for it.</summary>
    public async Task<Details.MetaDetail?> FetchMetaAsync(string manifestUrl, string type, string id, CancellationToken cancel = default) =>
        (await GetAsync<Details.MetaResponse>(AddonUrl.MetaUrl(manifestUrl, type, id), cancel).ConfigureAwait(false)).Meta;

    /// <summary>The streams an addon offers for a title (or "tt:season:episode").</summary>
    public async Task<List<Sources.AddonStream>> FetchStreamsAsync(string manifestUrl, string type, string id, CancellationToken cancel = default) =>
        (await GetAsync<Sources.StreamResponse>(AddonUrl.StreamUrl(manifestUrl, type, id), cancel).ConfigureAwait(false)).Streams ?? [];

    public async Task<List<MetaPreview>> FetchCatalogAsync(string manifestUrl, string type, string catalogId, IReadOnlyDictionary<string, string>? extra = null, CancellationToken cancel = default) =>
        (await GetAsync<CatalogResponse>(AddonUrl.CatalogUrl(manifestUrl, type, catalogId, extra), cancel).ConfigureAwait(false)).Metas ?? [];

    /// <summary>Like <see cref="FetchCatalogAsync"/> but a failing addon is just an empty list.</summary>
    public async Task<List<MetaPreview>> TryFetchCatalogAsync(string manifestUrl, string type, string catalogId, IReadOnlyDictionary<string, string>? extra = null, CancellationToken cancel = default)
    {
        try
        {
            return await FetchCatalogAsync(manifestUrl, type, catalogId, extra, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            return [];
        }
    }
}
