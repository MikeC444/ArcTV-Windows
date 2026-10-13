using System.Net;
using System.Text;
using ArcTV.Core.Account;
using ArcTV.Core.Storage;
using Xunit;

namespace ArcTV.Core.Tests;

public sealed class FakeHandler(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> answer) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request, body));
        var (status, text) = answer(request, body);
        return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}

public class CredentialsTests
{
    [Theory]
    [InlineData("", "password1", "Enter your email address.")]
    [InlineData("nope", "password1", "Enter a valid email address.")]
    [InlineData("a@b.co", "short", "Password must be at least 8 characters.")]
    public void Problems(string email, string password, string expected) => Assert.Equal(expected, Credentials.Problem(email, password));

    [Fact]
    public void Fine() => Assert.Null(Credentials.Problem("  a@b.co ", "password1"));
}

public class IsoDateTests
{
    [Fact]
    public void Formats() => Assert.Equal("2026-10-10T00:20:00.123Z", IsoDate.From(new DateTime(2026, 10, 10, 0, 20, 0, 123, DateTimeKind.Utc)));

    [Fact]
    public void ParsesWithAndWithoutFraction()
    {
        Assert.Equal(new DateTime(2026, 10, 10, 0, 20, 0, 123, DateTimeKind.Utc), IsoDate.Parse("2026-10-10T00:20:00.123Z"));
        Assert.Equal(new DateTime(2026, 10, 10, 0, 20, 0, DateTimeKind.Utc), IsoDate.Parse("2026-10-10T00:20:00Z"));
        Assert.Null(IsoDate.Parse("tomorrow-ish"));
    }
}

public class AuthClientTests
{
    private const string SessionJson = """
        {"accessToken":"a1","accessTokenExpiresAt":"2026-10-10T00:20:00.000Z","refreshToken":"r1","refreshTokenExpiresAt":"2026-11-10T00:20:00.000Z",
         "user":{"id":"u1","email":"a@b.co","displayName":"Ann"}}
        """;

    private static (AuthClient, FakeHandler) Client(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> answer, string url = "https://x.test/")
    {
        var handler = new FakeHandler(answer);
        return (new AuthClient(url, http: new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task LoginSendsPlatformAndVersionAndTrimsTheSlash()
    {
        var (client, handler) = Client((_, _) => (HttpStatusCode.OK, SessionJson));
        var session = await client.LoginAsync("a@b.co", "password1", "dev-1", "MY-PC");
        Assert.Equal("a1", session.AccessToken);
        Assert.Equal("Ann", session.User.DisplayName);
        var (request, body) = handler.Calls.Single();
        Assert.Equal("https://x.test/auth/login", request.RequestUri!.ToString());
        Assert.True(request.Headers.Contains("X-ArcTV-App-Version"));
        Assert.Contains("\"platform\":\"windows\"", body);
        Assert.Contains("\"deviceId\":\"dev-1\"", body);
    }

    [Fact]
    public async Task WrongPasswordIsAFriendlyMessage()
    {
        var (client, _) = Client((_, _) => (HttpStatusCode.Unauthorized, "{}"));
        var error = await Assert.ThrowsAsync<ApiException>(() => client.LoginAsync("a@b.co", "password1", "d", "n"));
        Assert.Equal(401, error.Status);
        Assert.Equal("Wrong email or password.", error.Message);
    }

    [Fact]
    public async Task ServerMessagesAreShownAsTheyAre()
    {
        var (client, _) = Client((_, _) => (HttpStatusCode.Conflict, """{"error":"That email is already registered."}"""));
        var error = await Assert.ThrowsAsync<ApiException>(() => client.RegisterAsync("a@b.co", "password1", null, "d", "n"));
        Assert.Equal("That email is already registered.", error.Message);
    }

    [Fact]
    public async Task RegisterSendsDisplayNameOnlyWhenGiven()
    {
        var (client, handler) = Client((_, _) => (HttpStatusCode.OK, SessionJson));
        await client.RegisterAsync("a@b.co", "password1", "  ", "d", "n");
        await client.RegisterAsync("a@b.co", "password1", " Ann ", "d", "n");
        Assert.DoesNotContain("displayName", handler.Calls[0].Body);
        Assert.Contains("\"displayName\":\"Ann\"", handler.Calls[1].Body);
    }

    [Fact]
    public async Task RefreshKeepsTheUser()
    {
        var (client, handler) = Client((_, _) => (HttpStatusCode.OK, """{"accessToken":"a2","accessTokenExpiresAt":"e1","refreshToken":"r2","refreshTokenExpiresAt":"e2"}"""));
        var old = new Session("a1", "x", "r1", "y", new AccountUser("u1", "a@b.co", "Ann"));
        var fresh = await client.RefreshAsync(old);
        Assert.Equal("a2", fresh.AccessToken);
        Assert.Equal("r2", fresh.RefreshToken);
        Assert.Equal(old.User, fresh.User);
        Assert.Contains("\"refreshToken\":\"r1\"", handler.Calls.Single().Body);
    }

    [Fact]
    public async Task UnreadableAnswerIsReported()
    {
        var (client, _) = Client((_, _) => (HttpStatusCode.OK, "<html>"));
        await Assert.ThrowsAsync<ApiException>(() => client.LoginAsync("a@b.co", "password1", "d", "n"));
    }

    [Fact]
    public async Task AuthorizedCallsCarryTokenAndProfile()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.OK, "{}"));
        var client = new AuthClient("https://x.test", "kid", new HttpClient(handler));
        await client.SendJsonAsync(HttpMethod.Get, "/library", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1 x" }, null, "tok");
        var request = handler.Calls.Single().Request;
        Assert.Equal("https://x.test/library?a=1%20x&b=2", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer tok", request.Headers.GetValues("Authorization").Single());
        Assert.Equal("kid", request.Headers.GetValues("X-ArcTV-Profile").Single());
    }

    [Fact]
    public async Task ATimeoutIsAFailureAndAnAskedForCancellationStaysACancellation()
    {
        // a server that never answers: the request is stopped by the time limit, which must look like a failed request, not like someone cancelling
        await Assert.ThrowsAsync<HttpRequestException>(() => ApiHttp.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x.test/a"), new HttpClient(new HangingHandler()), TimeSpan.FromMilliseconds(50)));

        using var stop = new CancellationTokenSource();
        var pending = ApiHttp.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x.test/a"), new HttpClient(new HangingHandler()), TimeSpan.FromSeconds(30), stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    [Fact]
    public async Task MainProfileSendsNoProfileHeader()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.OK, "{}"));
        var client = new AuthClient("https://x.test", "main", new HttpClient(handler));
        await client.SendJsonAsync(HttpMethod.Get, "/library", null, null, "tok");
        Assert.False(handler.Calls.Single().Request.Headers.Contains("X-ArcTV-Profile"));
    }

    [Fact]
    public void QrPollMapping()
    {
        Assert.IsType<QrPoll.Waiting>(QrPoll.Read("""{"status":"pending"}"""));
        Assert.IsType<QrPoll.Expired>(QrPoll.Read("""{"status":"expired"}"""));
        Assert.IsType<QrPoll.Expired>(QrPoll.Read("""{"status":"not_found"}"""));
        Assert.IsType<QrPoll.Expired>(QrPoll.Read("""{"status":"consumed"}"""));
        var done = Assert.IsType<QrPoll.SignedIn>(QrPoll.Read(SessionJson.Replace("{\"accessToken", "{\"status\":\"completed\",\"accessToken")));
        Assert.Equal("a1", done.Session.AccessToken);
    }
}

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arctv-test-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static readonly Session Sample = new("a", "e1", "r", "e2", new AccountUser("u", "a@b.co", null));

    [Fact]
    public void SessionRoundTripsAndClears()
    {
        var store = new SessionStore(Path.Combine(_dir, "session.dat"), new PlainProtector());
        Assert.Null(store.Load());
        store.Save(Sample);
        Assert.Equal(Sample, store.Load());
        store.Clear();
        Assert.Null(store.Load());
    }

    [Fact]
    public void CorruptSessionFileMeansSignedOut()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "session.dat");
        File.WriteAllText(path, "garbage");
        Assert.Null(new SessionStore(path, new PlainProtector()).Load());
    }

    [Fact]
    public void SessionIsNotStoredAsPlainTextWithARealProtector()
    {
        var path = Path.Combine(_dir, "session.dat");
        new SessionStore(path, new ReversingProtector()).Save(Sample);
        Assert.DoesNotContain("a@b.co", File.ReadAllText(path));
        Assert.Equal(Sample, new SessionStore(path, new ReversingProtector()).Load());
    }

    private sealed class ReversingProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => Flip(data);
        public byte[] Unprotect(byte[] data) => Flip(data);

        private static byte[] Flip(byte[] data)
        {
            var copy = (byte[])data.Clone();
            Array.Reverse(copy);
            return copy;
        }
    }

    [Fact]
    public void DeviceIdIsStable()
    {
        var path = Path.Combine(_dir, "device.id");
        var first = new DeviceIdentity(path, "PC").Id();
        Assert.True(Guid.TryParse(first, out _));
        Assert.Equal(first, new DeviceIdentity(path, "PC").Id());
        Assert.Equal("PC", new DeviceIdentity(path, "PC").Name);
    }

    [Fact]
    public void ServerUrlIsRememberedAndCleared()
    {
        var settings = new LocalSettings(Path.Combine(_dir, "settings.json"));
        Assert.Null(settings.ServerUrl);
        settings.ServerUrl = " https://my.test/ ";
        Assert.Equal("https://my.test", new LocalSettings(Path.Combine(_dir, "settings.json")).ServerUrl);
        settings.ServerUrl = "";
        Assert.Null(settings.ServerUrl);
    }
}
