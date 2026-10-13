using System.Text.Json;
using System.Text.Json.Nodes;
using ArcTV.Core.Account;

namespace ArcTV.Core.Profiles;

/// <summary>
/// A viewing profile on an account (Arc TV Plus): its own My List, Continue Watching, settings, addons and Likes. The account's own profile is "main" and is always there.
/// </summary>
public sealed record Profile(string Id, string Name, string Avatar, string Kind = "adult", bool HasPin = false, bool IsDefault = false)
{
    public const string MainId = "main";
    public const int Limit = 5;
    public const int NameMax = 24;
    public const int PinLength = 4;

    /// <summary>Genres a kids profile never shows (Home, Movies, TV Shows, Search, "You May Also Like"), on top of whatever the profile blocks itself.</summary>
    public static readonly string[] KidsBlockedGenres = ["Horror", "Thriller", "Crime", "War", "Mystery", "Film-Noir", "Adult"];

    private static readonly string[] AccountPaths = ["/user/me", "/user/plus", "/user/profiles", "/user/trailer", "/user/release-date", "/user/cast"];

    public bool IsKids => Kind == "kids";

    public static bool IsValidPin(string? pin) => pin is { Length: PinLength } && pin.All(char.IsAsciiDigit);

    /// <summary>Calls that are about the account, not a profile's library: they are sent without the profile header.</summary>
    public static bool IsAccountLevel(string path) => AccountPaths.Any(p => path == p || path.StartsWith(p + "/", StringComparison.Ordinal));
}

public static class Avatars
{
    /// <summary>The preset pictures, in the order they are offered.</summary>
    public static readonly string[] Ids = ["fox", "cat", "dog", "panda", "frog", "owl", "ghost", "robot", "alien", "astronaut", "raccoon", "penguin", "octopus", "dragon", "retro-tv", "lion"];

    private static readonly Dictionary<string, string> Legacy = new()
    {
        ["astro"] = "astronaut", ["monster"] = "alien", ["sunrise"] = "fox", ["ocean"] = "octopus", ["forest"] = "frog",
        ["violet"] = "ghost", ["ember"] = "dragon", ["mint"] = "owl", ["wave"] = "penguin", ["bolt"] = "robot",
    };

    /// <summary>The picture a stored id stands for (an old colour-tile id becomes the nearest new picture; anything unknown becomes the first).</summary>
    public static string Resolve(string? id)
    {
        var mapped = id is not null && Legacy.TryGetValue(id, out var nearest) ? nearest : id;
        return mapped is not null && Ids.Contains(mapped) ? mapped : Ids[0];
    }
}

/// <summary>What a PIN check said.</summary>
public enum PinOutcome { Ok, Wrong, Locked }

/// <summary>What an edit does to a profile's PIN.</summary>
public enum PinChangeKind { Keep, Remove, Set }

public readonly record struct PinChange(PinChangeKind Kind, string? Pin = null)
{
    public static PinChange Keep => new(PinChangeKind.Keep);

    public static PinChange Remove => new(PinChangeKind.Remove);

    public static PinChange Set(string pin) => new(PinChangeKind.Set, pin);
}

internal sealed record ProfilesResponse(List<Profile>? Profiles);

public static class ProfilesApi
{
    private static IReadOnlyDictionary<string, string>? PinHeader(string? currentPin) => string.IsNullOrEmpty(currentPin) ? null : new Dictionary<string, string> { ["X-ArcTV-Pin"] = currentPin };

    public static async Task<List<Profile>> ProfilesAsync(this AuthClient client, string accessToken, CancellationToken cancel = default) =>
        AuthClient.Read<ProfilesResponse>(await client.SendJsonAsync(HttpMethod.Get, "/user/profiles", null, null, accessToken, cancel: cancel).ConfigureAwait(false)).Profiles ?? throw ApiException.Unreadable();

    public static async Task<Profile> CreateProfileAsync(this AuthClient client, string accessToken, string name, string avatar, string kind, string? pin, CancellationToken cancel = default)
    {
        var body = new JsonObject { ["name"] = name, ["avatar"] = avatar, ["kind"] = kind };
        if (pin is not null) body["pin"] = pin;
        return AuthClient.Read<Profile>(await client.SendJsonAsync(HttpMethod.Post, "/user/profiles", null, body.ToJsonString(), accessToken, cancel: cancel).ConfigureAwait(false));
    }

    /// <summary>Changes a profile. A locked profile needs <paramref name="currentPin"/>.</summary>
    public static async Task<Profile> UpdateProfileAsync(this AuthClient client, string accessToken, string id, string name, string avatar, string kind, PinChange pin, string? currentPin, CancellationToken cancel = default)
    {
        var body = new JsonObject { ["name"] = name, ["avatar"] = avatar, ["kind"] = kind };
        if (pin.Kind == PinChangeKind.Remove) body["pin"] = null;
        else if (pin.Kind == PinChangeKind.Set) body["pin"] = pin.Pin;
        return AuthClient.Read<Profile>(await client.SendJsonAsync(HttpMethod.Put, $"/user/profiles/{Uri.EscapeDataString(id)}", null, body.ToJsonString(), accessToken, PinHeader(currentPin), cancel).ConfigureAwait(false));
    }

    public static Task DeleteProfileAsync(this AuthClient client, string accessToken, string id, string? currentPin, CancellationToken cancel = default) =>
        client.SendJsonAsync(HttpMethod.Delete, $"/user/profiles/{Uri.EscapeDataString(id)}", null, null, accessToken, PinHeader(currentPin), cancel);

    public static async Task<PinOutcome> VerifyPinAsync(this AuthClient client, string accessToken, string id, string pin, CancellationToken cancel = default)
    {
        try
        {
            await client.SendJsonAsync(HttpMethod.Post, $"/user/profiles/{Uri.EscapeDataString(id)}/verify-pin", null, JsonSerializer.Serialize(new { pin }), accessToken, cancel: cancel).ConfigureAwait(false);
            return PinOutcome.Ok;
        }
        catch (ApiException ex) when (ex.Status == 403)
        {
            return PinOutcome.Wrong;
        }
        catch (ApiException ex) when (ex.Status == 429)
        {
            return PinOutcome.Locked;
        }
    }
}
