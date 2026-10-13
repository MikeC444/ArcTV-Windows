using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArcTV.Core.Account;

/// <summary>The account backend (the same one the Android, web and Mac apps use). <see cref="BaseUrl"/> has no trailing slash.</summary>
public sealed class AuthClient
{
    public const string Platform = "windows";

    private readonly HttpClient? _http;

    public string BaseUrl { get; }

    /// <summary>The profile the library calls are for (sent as <c>X-ArcTV-Profile</c>); null or "main" is the account's own, which needs no header.</summary>
    public string? ProfileId { get; }

    public AuthClient(string baseUrl, string? profileId = null, HttpClient? http = null)
    {
        BaseUrl = baseUrl.Trim().TrimEnd('/');
        ProfileId = profileId;
        _http = http;
    }

    private sealed record LoginRequest(string Email, string Password, string DeviceId, string? DeviceName, string? Platform);
    private sealed record RegisterRequest(string Email, string Password, string? DisplayName, string DeviceId, string? DeviceName, string? Platform);
    private sealed record RefreshRequest(string RefreshToken);
    private sealed record TokenPair(string AccessToken, string AccessTokenExpiresAt, string RefreshToken, string RefreshTokenExpiresAt);

    public async Task<Session> LoginAsync(string email, string password, string deviceId, string deviceName, CancellationToken cancel = default) =>
        Read<Session>(await PostAsync("/auth/login", new LoginRequest(email, password, deviceId, deviceName, Platform), cancel).ConfigureAwait(false));

    /// <summary>Makes a new account and signs it in on this device.</summary>
    public async Task<Session> RegisterAsync(string email, string password, string? displayName, string deviceId, string deviceName, CancellationToken cancel = default)
    {
        var name = displayName?.Trim();
        var body = new RegisterRequest(email, password, string.IsNullOrEmpty(name) ? null : name, deviceId, deviceName, Platform);
        return Read<Session>(await PostAsync("/auth/register", body, cancel).ConfigureAwait(false));
    }

    /// <summary>Trades the refresh token for a new pair (the user stays the same).</summary>
    public async Task<Session> RefreshAsync(Session session, CancellationToken cancel = default)
    {
        var pair = Read<TokenPair>(await PostAsync("/auth/refresh", new RefreshRequest(session.RefreshToken), cancel).ConfigureAwait(false));
        return session with { AccessToken = pair.AccessToken, AccessTokenExpiresAt = pair.AccessTokenExpiresAt, RefreshToken = pair.RefreshToken, RefreshTokenExpiresAt = pair.RefreshTokenExpiresAt };
    }

    /// <summary>Best effort: the app forgets the session whether or not this succeeds.</summary>
    public async Task LogoutAsync(string accessToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/auth/logout");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            await ApiHttp.SendAsync(request, _http).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // forgotten locally either way
        }
    }

    /// <summary>A sign-in the person finishes on their phone: this PC shows <see cref="QrSession.ActivationUrl"/> as a QR code and asks the server until it is done.</summary>
    public async Task<QrSession> CreateQrSessionAsync(string deviceId, string deviceName, CancellationToken cancel = default) =>
        Read<QrSession>(await PostAsync("/auth/qr/create", new QrCreateRequest(deviceId, deviceName, Platform), cancel).ConfigureAwait(false));

    public async Task<QrPoll> PollQrSessionAsync(string token, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/auth/qr/status?token={Uri.EscapeDataString(token)}");
        return QrPoll.Read(await ApiHttp.SendAsync(request, _http, cancel: cancel).ConfigureAwait(false));
    }

    /// <summary>A call that needs the signed-in token (and the active profile, for library calls).</summary>
    public async Task<string> SendJsonAsync(HttpMethod method, string path, IReadOnlyDictionary<string, string>? query, string? jsonBody, string accessToken,
        IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancel = default)
    {
        var url = BaseUrl + path;
        if (query is { Count: > 0 }) url += "?" + string.Join("&", query.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw ApiException.BadUrl(url);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        request.Headers.TryAddWithoutValidation("X-ArcTV-App-Version", AppInfo.Version);
        if (!string.IsNullOrEmpty(ProfileId) && ProfileId != Profiles.Profile.MainId && !Profiles.Profile.IsAccountLevel(path)) request.Headers.TryAddWithoutValidation("X-ArcTV-Profile", ProfileId);
        if (headers is not null) foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        if (jsonBody is not null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await ApiHttp.SendAsync(request, _http, ApiHttp.SlowTimeout, cancel).ConfigureAwait(false);
    }

    private async Task<string> PostAsync<TBody>(string path, TBody body, CancellationToken cancel)
    {
        if (!Uri.TryCreate(BaseUrl + path, UriKind.Absolute, out _)) throw ApiException.BadUrl(BaseUrl + path);
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, ApiHttp.Json), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-ArcTV-App-Version", AppInfo.Version);
        return await ApiHttp.SendAsync(request, _http, ApiHttp.SlowTimeout, cancel).ConfigureAwait(false);
    }

    internal static T Read<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, ApiHttp.Json) ?? throw ApiException.Unreadable();
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    internal sealed record QrCreateRequest(string DeviceId, string? DeviceName, string? Platform);
}

/// <summary>A QR sign-in in progress.</summary>
public sealed record QrSession(string Token, string ActivationUrl, string ExpiresAt);

/// <summary>What asking about a QR sign-in found.</summary>
public abstract record QrPoll
{
    public sealed record Waiting : QrPoll;
    public sealed record Expired : QrPoll;
    public sealed record SignedIn(Session Session) : QrPoll;

    private sealed record Answer([property: JsonPropertyName("status")] string Status);

    /// <summary>Reads the server's answer: pending, expired, or completed with the tokens (handed out once).</summary>
    public static QrPoll Read(string json)
    {
        var answer = AuthClient.Read<Answer>(json);
        return answer.Status switch
        {
            "completed" => new SignedIn(AuthClient.Read<Session>(json)),
            "expired" or "not_found" or "consumed" => new Expired(),
            _ => new Waiting(),
        };
    }
}
