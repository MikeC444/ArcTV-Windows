using System.Net;
using ArcTV.Core.Account;
using ArcTV.Core.Storage;
using Xunit;

namespace ArcTV.Core.Tests;

public class AccountModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arctv-model-" + Guid.NewGuid());
    private DateTime _clock = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private const string Login = """{"accessToken":"a1","accessTokenExpiresAt":"e","refreshToken":"r1","refreshTokenExpiresAt":"e","user":{"id":"u","email":"a@b.co"}}""";
    private const string Refreshed = """{"accessToken":"a2","accessTokenExpiresAt":"e","refreshToken":"r2","refreshTokenExpiresAt":"e"}""";

    private (AccountModel, FakeHandler) Make(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> answer)
    {
        var handler = new FakeHandler(answer);
        var model = new AccountModel(new SessionStore(Path.Combine(_dir, "s.dat"), new PlainProtector()), new DeviceIdentity(Path.Combine(_dir, "d.id"), "PC"),
            new LocalSettings(Path.Combine(_dir, "set.json")), new HttpClient(handler), () => _clock);
        return (model, handler);
    }

    [Fact]
    public async Task SignInKeepsTheSessionAcrossRestarts()
    {
        var (model, _) = Make((_, _) => (HttpStatusCode.OK, Login));
        Assert.True(await model.SignInAsync(" a@b.co ", "password1"));
        Assert.True(model.IsSignedIn);
        var (again, _) = Make((_, _) => (HttpStatusCode.OK, Login));
        Assert.Equal("a1", again.Session?.AccessToken);
    }

    [Fact]
    public async Task TheProfileInUseDecidesTheHeaderAndWhatLocalCopiesAreFiledUnder()
    {
        var (model, handler) = Make((_, _) => (HttpStatusCode.OK, Login));
        Assert.Null(model.Scope); // nobody signed in
        await model.SignInAsync("a@b.co", "password1");
        Assert.Equal("u", model.Scope); // the account's own profile keeps the plain name
        model.UseProfile("kid");
        Assert.Equal("u-kid", model.Scope);
        Assert.Equal("kid", model.Client().ProfileId);
        await model.AuthorizedAsync((c, t) => c.SendJsonAsync(HttpMethod.Get, "/user/watchlist", null, null, t));
        Assert.Equal("kid", handler.Calls[^1].Request.Headers.GetValues("X-ArcTV-Profile").Single());
        model.UseProfile(null);
        Assert.Equal("u", model.Scope);
        model.UseProfile("kid");
        await model.SignOutAsync();
        Assert.Equal("main", model.ProfileId); // the next person starts on their own profile
    }

    [Fact]
    public async Task BadInputNeverReachesTheServer()
    {
        var (model, handler) = Make((_, _) => (HttpStatusCode.OK, Login));
        Assert.False(await model.SignInAsync("a@b.co", "short"));
        Assert.Equal("Password must be at least 8 characters.", model.Error);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task TooManyAttemptsPausesSignIn()
    {
        var (model, handler) = Make((_, _) => (HttpStatusCode.TooManyRequests, "{}"));
        Assert.False(await model.SignInAsync("a@b.co", "password1"));
        Assert.StartsWith("Too many sign-in attempts", model.Error);
        Assert.True(model.Waiting);
        Assert.False(await model.SignInAsync("a@b.co", "password1"));
        Assert.Single(handler.Calls);
        _clock += TimeSpan.FromSeconds(31);
        Assert.False(model.Waiting);
    }

    [Fact]
    public async Task WrongPasswordShowsTheServersWords()
    {
        var (model, _) = Make((_, _) => (HttpStatusCode.Unauthorized, "{}"));
        Assert.False(await model.SignInAsync("a@b.co", "password1"));
        Assert.Equal("Wrong email or password.", model.Error);
        Assert.False(model.IsSignedIn);
    }

    [Fact]
    public async Task NoNetworkSaysSo()
    {
        var (model, _) = Make((_, _) => throw new HttpRequestException("down"));
        await model.SignInAsync("a@b.co", "password1");
        Assert.Equal("Couldn't reach the server. Check the address and your connection.", model.Error);
    }

    [Fact]
    public async Task ExpiredTokenIsRefreshedOnceThenRetried()
    {
        var calls = 0;
        var (model, handler) = Make((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/auth/login") return (HttpStatusCode.OK, Login);
            if (path == "/auth/refresh") return (HttpStatusCode.OK, Refreshed);
            return ++calls == 1 ? (HttpStatusCode.Unauthorized, "{}") : (HttpStatusCode.OK, "[]");
        });
        await model.SignInAsync("a@b.co", "password1");
        var answer = await model.AuthorizedAsync((client, token) => client.SendJsonAsync(HttpMethod.Get, "/library", null, null, token));
        Assert.Equal("[]", answer);
        Assert.Equal("a2", model.Session!.AccessToken);
        Assert.Equal("Bearer a2", handler.Calls.Last().Request.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task RefreshRefusedSignsOut()
    {
        var (model, _) = Make((request, _) => request.RequestUri!.AbsolutePath == "/auth/login" ? (HttpStatusCode.OK, Login) : (HttpStatusCode.Unauthorized, "{}"));
        await model.SignInAsync("a@b.co", "password1");
        await Assert.ThrowsAsync<ApiException>(() => model.AuthorizedAsync((client, token) => client.SendJsonAsync(HttpMethod.Get, "/library", null, null, token)));
        Assert.False(model.IsSignedIn);
    }

    [Fact]
    public async Task SignOutForgetsEvenIfTheServerIsDown()
    {
        var (model, _) = Make((request, _) => request.RequestUri!.AbsolutePath == "/auth/login" ? (HttpStatusCode.OK, Login) : throw new HttpRequestException("down"));
        await model.SignInAsync("a@b.co", "password1");
        await model.SignOutAsync();
        Assert.False(model.IsSignedIn);
        Assert.Null(new SessionStore(Path.Combine(_dir, "s.dat"), new PlainProtector()).Load());
    }

    [Fact]
    public async Task QrSignInShowsTheCodeThenFinishes()
    {
        var polls = 0;
        var (model, _) = Make((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/auth/qr/create" => (HttpStatusCode.OK, """{"token":"t","activationUrl":"https://x.test/activate?c=1","expiresAt":"e"}"""),
            _ => ++polls < 2 ? (HttpStatusCode.OK, """{"status":"pending"}""") : (HttpStatusCode.OK, Login.Replace("{\"accessToken", "{\"status\":\"completed\",\"accessToken")),
        });
        string? shown = null;
        var outcome = await model.SignInWithQrAsync(url => shown = url, TimeSpan.FromMilliseconds(5), CancellationToken.None);
        Assert.Equal(QrOutcome.SignedIn, outcome);
        Assert.Equal("https://x.test/activate?c=1", shown);
        Assert.True(model.IsSignedIn);
    }

    [Fact]
    public async Task QrCodeThatRanOutIsReported()
    {
        var (model, _) = Make((request, _) => request.RequestUri!.AbsolutePath == "/auth/qr/create"
            ? (HttpStatusCode.OK, """{"token":"t","activationUrl":"u","expiresAt":"e"}""")
            : (HttpStatusCode.OK, """{"status":"expired"}"""));
        Assert.Equal(QrOutcome.Expired, await model.SignInWithQrAsync(_ => { }, TimeSpan.FromMilliseconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task QrCancelStopsQuietly()
    {
        var (model, _) = Make((request, _) => request.RequestUri!.AbsolutePath == "/auth/qr/create"
            ? (HttpStatusCode.OK, """{"token":"t","activationUrl":"u","expiresAt":"e"}""")
            : (HttpStatusCode.OK, """{"status":"pending"}"""));
        using var cts = new CancellationTokenSource(50);
        Assert.Equal(QrOutcome.Cancelled, await model.SignInWithQrAsync(_ => { }, TimeSpan.FromMilliseconds(10), cts.Token));
        Assert.Null(model.Error);
    }
}
