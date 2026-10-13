using ArcTV.Core.Account;
using ArcTV.Core.Home;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Settings;

/// <summary>Where the account's settings are kept. The tests use a stand-in.</summary>
public interface IAccountSettingsStore
{
    Task<AccountSettings> GetAsync(CancellationToken cancel = default);

    /// <summary>Saves it all and returns what the account kept.</summary>
    Task<AccountSettings> SaveAsync(AccountSettings settings, CancellationToken cancel = default);
}

public sealed class AccountSettingsStore(AccountModel account) : IAccountSettingsStore
{
    public Task<AccountSettings> GetAsync(CancellationToken cancel = default) => account.AuthorizedAsync((client, token) => client.SettingsAsync(token, cancel));

    public Task<AccountSettings> SaveAsync(AccountSettings settings, CancellationToken cancel = default) => account.AuthorizedAsync((client, token) => client.SaveSettingsAsync(token, settings, cancel));
}

/// <summary>
/// The account's settings as the app uses them. A change is shown at once, saved to the account, and put back if the save fails. <see cref="Changed"/> is raised after every
/// change that sticks (and after the first read), so Home, Movies, TV Shows and Search can follow.
/// </summary>
public sealed class SettingsModel(IAccountSettingsStore store) : ObservableObject
{
    private AccountSettings? _current;
    private string? _problem;

    public event Action? Changed;

    /// <summary>The settings, or null until they have been read.</summary>
    public AccountSettings? Current { get => _current; private set => Set(ref _current, value); }

    /// <summary>Set when the account could not be reached or a change did not save; cleared by the next success.</summary>
    public string? Problem { get => _problem; private set => Set(ref _problem, value); }

    /// <summary>Asks every page showing the settings to draw itself again (for a choice kept only on this PC, like the subtitle size).</summary>
    public void Redraw() => Raise(nameof(Current));

    public IReadOnlySet<string> Blocked => BlockedGenres.Set(Current?.BlockedGenres);

    public HomeRowPreferences RowPreferences => new(Current?.HomeRowOrder ?? [], Current?.HiddenRowIds ?? []);

    public async Task LoadAsync(CancellationToken cancel = default)
    {
        try
        {
            Current = await store.GetAsync(cancel).ConfigureAwait(true);
            Problem = null;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Problem = "Couldn't read your settings just now. Check your connection and try again.";
        }
    }

    /// <summary>Changes one or more settings: shown at once, saved to the account, put back if the save fails.</summary>
    public async Task UpdateAsync(Func<AccountSettings, AccountSettings> edit, CancellationToken cancel = default)
    {
        if (Current is null) await LoadAsync(cancel).ConfigureAwait(true);
        if (Current is not { } before) return;
        var next = edit(before);
        if (next.SameChoices(before)) return;
        Current = next;
        Changed?.Invoke();
        try
        {
            Current = await store.SaveAsync(next, cancel).ConfigureAwait(true);
            Problem = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Current = before;
            Problem = "That change didn't save. Check your connection and try again.";
            Changed?.Invoke();
        }
    }

    /// <summary>Hides (or shows again) a genre everywhere.</summary>
    public Task SetGenreAsync(string genre, bool blocked) => UpdateAsync(s =>
    {
        var list = (s.BlockedGenres ?? []).Where(g => !string.Equals(g, genre, StringComparison.OrdinalIgnoreCase)).ToList();
        if (blocked) list.Add(genre);
        return s with { BlockedGenres = list };
    });

    public Task SetRowVisibleAsync(string id, bool visible) => UpdateAsync(s =>
    {
        var hidden = s.HiddenRowIds.Where(h => h != id).ToList();
        if (!visible) hidden.Add(id);
        return s with { HiddenRowIds = hidden };
    });

    /// <summary>Moves a row one place up (-1) or down (+1) among <paramref name="shownOrder"/> (every row Settings lists, in the order shown now).</summary>
    public Task MoveRowAsync(IReadOnlyList<string> shownOrder, string id, int delta) => UpdateAsync(s => s with { HomeRowOrder = HomeRowOrder.Move(shownOrder, id, delta) });

    /// <summary>Every row Home can show, in the order the person has chosen, with whether it is switched on.</summary>
    public static IReadOnlyList<(HomeSection Section, bool Visible)> RowChoices(IReadOnlyList<HomeSection> all, HomeRowPreferences preferences)
    {
        var hidden = preferences.HiddenOrEmpty.ToHashSet();
        return HomeSections.Ordered(all, preferences).Select(s => (s, !hidden.Contains(s.Id))).ToList();
    }
}
