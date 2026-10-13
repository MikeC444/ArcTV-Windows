using ArcTV.Core.Account;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Profiles;

/// <summary>Where profiles are kept (the account). The tests use a stand-in.</summary>
public interface IProfileStore
{
    Task<IReadOnlyList<Profile>> ListAsync(CancellationToken cancel = default);

    Task CreateAsync(string name, string avatar, string kind, string? pin, CancellationToken cancel = default);

    Task UpdateAsync(string id, string name, string avatar, string kind, PinChange pin, string? currentPin, CancellationToken cancel = default);

    Task DeleteAsync(string id, string? currentPin, CancellationToken cancel = default);

    Task<PinOutcome> VerifyPinAsync(string id, string pin, CancellationToken cancel = default);
}

public sealed class AccountProfileStore(AccountModel account) : IProfileStore
{
    public async Task<IReadOnlyList<Profile>> ListAsync(CancellationToken cancel = default) => await account.AuthorizedAsync((c, t) => c.ProfilesAsync(t, cancel)).ConfigureAwait(false);

    public Task CreateAsync(string name, string avatar, string kind, string? pin, CancellationToken cancel = default) =>
        account.AuthorizedAsync(async (c, t) =>
        {
            await c.CreateProfileAsync(t, name, avatar, kind, pin, cancel).ConfigureAwait(false);
            return true;
        });

    public Task UpdateAsync(string id, string name, string avatar, string kind, PinChange pin, string? currentPin, CancellationToken cancel = default) =>
        account.AuthorizedAsync(async (c, t) =>
        {
            await c.UpdateProfileAsync(t, id, name, avatar, kind, pin, currentPin, cancel).ConfigureAwait(false);
            return true;
        });

    public Task DeleteAsync(string id, string? currentPin, CancellationToken cancel = default) =>
        account.AuthorizedAsync(async (c, t) =>
        {
            await c.DeleteProfileAsync(t, id, currentPin, cancel).ConfigureAwait(false);
            return true;
        });

    public Task<PinOutcome> VerifyPinAsync(string id, string pin, CancellationToken cancel = default) => account.AuthorizedAsync((c, t) => c.VerifyPinAsync(t, id, pin, cancel));
}

/// <summary>
/// "Who's watching?": the account's profiles. Without Plus (or on a server with no profiles) there is only the account's own profile and nothing to pick. Call it from the UI thread.
/// </summary>
public sealed class ProfilesModel(IProfileStore store) : ObservableObject
{
    private IReadOnlyList<Profile> _profiles = [];

    public IReadOnlyList<Profile> Profiles { get => _profiles; private set { if (Set(ref _profiles, value)) Raise(nameof(CanAdd)); } }

    public bool CanAdd => Profiles.Count < Profile.Limit;

    public Profile? Find(string? id) => Profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>Reads the profile list (only when the account has Plus). A list that cannot be read leaves the account with just its own profile.</summary>
    public async Task LoadAsync(bool plusActive, CancellationToken cancel = default)
    {
        if (!plusActive)
        {
            Profiles = [];
            return;
        }

        try
        {
            Profiles = await store.ListAsync(cancel).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Profiles = [];
        }
    }

    /// <summary>Checks the PIN of a profile that has one. Returns null when it is fine (or the profile has no PIN), else what to tell the person.</summary>
    public async Task<string?> CheckPinAsync(Profile profile, string? pin, CancellationToken cancel = default)
    {
        if (!profile.HasPin) return null;
        if (!Profile.IsValidPin(pin)) return "Enter the 4-digit PIN.";
        PinOutcome outcome;
        try
        {
            outcome = await store.VerifyPinAsync(profile.Id, pin!, cancel).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "Couldn't check the PIN. Check your connection and try again.";
        }

        return outcome switch
        {
            PinOutcome.Ok => null,
            PinOutcome.Wrong => "That PIN is wrong.",
            _ => "Too many wrong PINs. Try again in a few minutes.",
        };
    }

    /// <summary>What is wrong with a name, or null.</summary>
    public static string? NameProblem(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Give the profile a name." : name.Trim().Length > Profile.NameMax ? $"A name can be at most {Profile.NameMax} letters." : null;

    public async Task CreateAsync(string name, string avatar, bool kids, string? pin, CancellationToken cancel = default)
    {
        await store.CreateAsync(name.Trim(), avatar, kids ? "kids" : "adult", pin, cancel).ConfigureAwait(true);
        Profiles = await store.ListAsync(cancel).ConfigureAwait(true);
    }

    public async Task UpdateAsync(Profile profile, string name, string avatar, bool kids, PinChange pin, string? currentPin, CancellationToken cancel = default)
    {
        await store.UpdateAsync(profile.Id, name.Trim(), avatar, kids && !profile.IsDefault ? "kids" : "adult", pin, currentPin, cancel).ConfigureAwait(true);
        Profiles = await store.ListAsync(cancel).ConfigureAwait(true);
    }

    public async Task DeleteAsync(Profile profile, string? currentPin, CancellationToken cancel = default)
    {
        await store.DeleteAsync(profile.Id, currentPin, cancel).ConfigureAwait(true);
        Profiles = await store.ListAsync(cancel).ConfigureAwait(true);
    }
}
