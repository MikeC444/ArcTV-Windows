using ArcTV.Core.Addons;

namespace ArcTV.Core.Home;

public static class AddonSources
{
    /// <summary>
    /// The addons that offer catalogues: every enabled one the account has, or Cinemeta when there is none (or the account cannot be reached).
    /// Empty only when even Cinemeta cannot be reached.
    /// </summary>
    public static async Task<IReadOnlyList<HomeSource>> LoadAsync(Func<CancellationToken, Task<IReadOnlyList<SyncedAddon>>> addons, AddonClient client, CancellationToken cancel = default)
    {
        IReadOnlyList<SyncedAddon> found;
        try
        {
            found = await addons(cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            found = [];
        }

        var sources = found.Where(a => a.ManifestJson is not null).Select(a => new HomeSource(a.ManifestUrl, a.ManifestJson!)).Where(s => s.Catalogs.Count > 0).ToList();
        if (sources.Count > 0) return sources;

        try
        {
            return [new HomeSource(AppInfo.CinemetaManifestUrl, await client.FetchManifestAsync(AppInfo.CinemetaManifestUrl, cancel).ConfigureAwait(false))];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }
}
