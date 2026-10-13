using ArcTV.Core.Account;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Addons;

/// <summary>Where the addons are kept (the account). The tests use a stand-in.</summary>
public interface IAddonStore
{
    /// <summary>Every addon, switched-off ones included, in their order.</summary>
    Task<IReadOnlyList<SyncedAddon>> ListAsync(CancellationToken cancel = default);

    Task SaveAsync(string manifestUrl, string addonId, string name, string rawManifest, bool enabled, int sortOrder, CancellationToken cancel = default);

    Task RemoveAsync(string manifestUrl, CancellationToken cancel = default);
}

/// <summary>The addons on the signed-in account.</summary>
public sealed class AccountAddonStore(AccountModel account) : IAddonStore
{
    public async Task<IReadOnlyList<SyncedAddon>> ListAsync(CancellationToken cancel = default) =>
        await account.AuthorizedAsync((client, token) => client.AddonsAsync(token, includeDisabled: true, cancel: cancel)).ConfigureAwait(false);

    public Task SaveAsync(string manifestUrl, string addonId, string name, string rawManifest, bool enabled, int sortOrder, CancellationToken cancel = default) =>
        account.AuthorizedAsync(async (client, token) =>
        {
            await client.SaveAddonAsync(token, manifestUrl, addonId, name, rawManifest, enabled, sortOrder, cancel).ConfigureAwait(false);
            return true;
        });

    public Task RemoveAsync(string manifestUrl, CancellationToken cancel = default) =>
        account.AuthorizedAsync(async (client, token) =>
        {
            await client.RemoveAddonAsync(token, manifestUrl, cancel).ConfigureAwait(false);
            return true;
        });
}

/// <summary>Settings > Addons: what is installed on the account, switch each on or off, remove it, or add one from a link.</summary>
public sealed class AddonsModel(IAddonStore store, Func<string, CancellationToken, Task<AddonInstall>> fetch) : ObservableObject
{
    private IReadOnlyList<SyncedAddon> _items = [];
    private bool _loaded;
    private bool _busy;
    private string? _message;
    private bool _messageIsError;

    /// <summary>Raised after a change that reaches the account (installed, switched, removed), so Home and the browse pages can look for streams and catalogues again.</summary>
    public event Action? Changed;

    public IReadOnlyList<SyncedAddon> Items { get => _items; private set => Set(ref _items, value); }

    /// <summary>False until the list has been read once (the page shows "Loading" rather than "No addons yet").</summary>
    public bool Loaded { get => _loaded; private set => Set(ref _loaded, value); }

    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    public string? Message { get => _message; private set => Set(ref _message, value); }

    public bool MessageIsError { get => _messageIsError; private set => Set(ref _messageIsError, value); }

    public async Task LoadAsync(CancellationToken cancel = default)
    {
        try
        {
            Items = await store.ListAsync(cancel).ConfigureAwait(true);
            Loaded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Say("Couldn't read your addons just now. Check your connection and open this page again.", true);
            Loaded = true;
        }
    }

    public async Task InstallAsync(string link, CancellationToken cancel = default)
    {
        link = link.Trim();
        if (link.Length == 0 || Busy) return;
        Busy = true;
        Message = null;
        try
        {
            var install = await fetch(link, cancel).ConfigureAwait(true);
            var existing = Items.FirstOrDefault(a => a.ManifestUrl == install.ManifestUrl);
            var order = existing?.SortOrder ?? ((Items.Count == 0 ? -1 : Items.Max(a => a.SortOrder)) + 1);
            await store.SaveAsync(install.ManifestUrl, install.AddonId, install.Name, install.RawManifest, true, order, cancel).ConfigureAwait(true);
            await Refresh(cancel).ConfigureAwait(true);
            Say($"Added {install.Name}.", false);
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Say("That link didn't give an addon. Check it and try again.", true);
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task SetEnabledAsync(SyncedAddon addon, bool enabled, CancellationToken cancel = default)
    {
        if (addon.RawManifest is null || addon.Enabled == enabled) return;
        try
        {
            await store.SaveAsync(addon.ManifestUrl, addon.AddonId, addon.Name, addon.RawManifest, enabled, addon.SortOrder, cancel).ConfigureAwait(true);
            await Refresh(cancel).ConfigureAwait(true);
            Message = null;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Say($"Couldn't change {addon.Name}. Try again.", true);
            Raise(nameof(Items)); // so a switch that was flipped on screen goes back to what the account has
        }
    }

    public async Task RemoveAsync(SyncedAddon addon, CancellationToken cancel = default)
    {
        try
        {
            await store.RemoveAsync(addon.ManifestUrl, cancel).ConfigureAwait(true);
            await Refresh(cancel).ConfigureAwait(true);
            Say($"Removed {addon.Name}.", false);
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Say($"Couldn't remove {addon.Name}. Try again.", true);
        }
    }

    private async Task Refresh(CancellationToken cancel) => Items = await store.ListAsync(cancel).ConfigureAwait(true);

    private void Say(string text, bool isError)
    {
        MessageIsError = isError;
        Message = text;
    }

    /// <summary>"Catalogues, Streams" from the addon's manifest.</summary>
    public static string Provides(SyncedAddon addon)
    {
        var names = (addon.ManifestJson?.Resources ?? []).Select(r => r.Name).Where(n => n.Length > 0).Distinct()
            .Select(n => char.ToUpperInvariant(n[0]) + n[1..] + (n is "stream" or "catalog" or "meta" or "subtitles" ? "s" : ""))
            .Select(n => n == "Catalogs" ? "Catalogues" : n)
            .ToList();
        return names.Count == 0 ? "Provides: not stated" : "Provides: " + string.Join(", ", names);
    }
}
