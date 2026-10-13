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
        try
        {
            parsed = JsonSerializer.Deserialize<SyncedAddonsResponse>(body, ApiHttp.Json);
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }

        return (parsed?.Items ?? []).Where(a => includeDisabled || a.Enabled).OrderBy(a => a.SortOrder).ToList();
    }
}
