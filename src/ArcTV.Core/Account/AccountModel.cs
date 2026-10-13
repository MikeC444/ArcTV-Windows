using ArcTV.Core.Mvvm;
using ArcTV.Core.Storage;

namespace ArcTV.Core.Account;

/// <summary>Who is signed in on this PC and what the sign-in screen should say. Everything the sign-in view binds to lives here so it can be tested without a window.</summary>
public sealed class AccountModel : ObservableObject
{
    public static readonly TimeSpan RateLimitWait = TimeSpan.FromSeconds(30);

    private readonly SessionStore _store;
    private readonly DeviceIdentity _device;
    private readonly LocalSettings _settings;
    private readonly HttpClient? _http;
    private readonly Func<DateTime> _now;
    private Session? _session;
    private string _profileId = Profiles.Profile.MainId;
    private bool _busy;
    private string? _error;
    private DateTime? _waitUntil;

    public AccountModel(SessionStore store, DeviceIdentity device, LocalSettings settings, HttpClient? http = null, Func<DateTime>? now = null)
    {
        _store = store;
        _device = device;
        _settings = settings;
        _http = http;
        _now = now ?? (() => DateTime.UtcNow);
        _session = store.Load();
    }

    public Session? Session { get => _session; private set { if (Set(ref _session, value)) Raise(nameof(IsSignedIn)); } }

    public bool IsSignedIn => _session is not null;

    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    /// <summary>Set after the server says "too many attempts": sign-in stays off until then.</summary>
    public DateTime? WaitUntil { get => _waitUntil; private set => Set(ref _waitUntil, value); }

    public bool Waiting => _waitUntil is { } until && until > _now();

    public string ServerUrl
    {
        get => AppInfo.ResolveApiBaseUrl(_settings.ServerUrl, AppInfo.BundledApiBaseUrl);
        set { _settings.ServerUrl = value; Raise(); }
    }

    /// <summary>The profile in use (Arc TV Plus). Library calls carry it; "main" is the account's own profile.</summary>
    public string ProfileId { get => _profileId; private set { if (Set(ref _profileId, value)) Raise(nameof(Scope)); } }

    public void UseProfile(string? id) => ProfileId = string.IsNullOrEmpty(id) ? Profiles.Profile.MainId : id;

    /// <summary>What this PC's local copies (recent searches, remembered sources, picked rows) are filed under: the account, and the profile when it is not the account's own, so the account's own profile keeps the names it always had.</summary>
    public string? Scope => Session is { } s ? (ProfileId == Profiles.Profile.MainId ? s.User.Id : $"{s.User.Id}-{ProfileId}") : null;

    public AuthClient Client(string? profileId = null) => new(ServerUrl, profileId ?? ProfileId, _http);

    public Task<bool> SignInAsync(string email, string password, CancellationToken cancel = default) =>
        EnterAsync(email, password, () => Client().LoginAsync(email.Trim(), password, _device.Id(), _device.Name, cancel));

    public Task<bool> RegisterAsync(string email, string password, string? displayName, CancellationToken cancel = default) =>
        EnterAsync(email, password, () => Client().RegisterAsync(email.Trim(), password, displayName, _device.Id(), _device.Name, cancel));

    private async Task<bool> EnterAsync(string email, string password, Func<Task<Session>> call)
    {
        if (Busy || Waiting) return false;
        if (Credentials.Problem(email, password) is { } problem)
        {
            Error = problem;
            return false;
        }

        Busy = true;
        Error = null;
        try
        {
            Accept(await call().ConfigureAwait(true));
            return true;
        }
        catch (Exception ex)
        {
            Error = Describe(ex);
            if (ex is ApiException { Status: 429 }) WaitUntil = _now() + RateLimitWait;
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    private void Accept(Session session)
    {
        _store.Save(session);
        Session = session;
        Error = null;
        WaitUntil = null;
    }

    public static string Describe(Exception ex) => ex switch
    {
        ApiException { Status: 429 } => "Too many sign-in attempts right now. Wait a moment and try again.",
        ApiException api => api.Message,
        HttpRequestException or TaskCanceledException or OperationCanceledException => "Couldn't reach the server. Check the address and your connection.",
        _ => ex.Message,
    };

    public async Task SignOutAsync()
    {
        var session = Session;
        Forget();
        if (session is not null) await Client().LogoutAsync(session.AccessToken).ConfigureAwait(true);
    }

    private void Forget()
    {
        _store.Clear();
        ProfileId = Profiles.Profile.MainId;
        Session = null;
    }

    /// <summary>Runs a call that needs the token. If the server says the token ran out, trades the refresh token and tries once more; if that fails too the person is signed out.</summary>
    public async Task<T> AuthorizedAsync<T>(Func<AuthClient, string, Task<T>> call, string? profileId = null)
    {
        var session = Session ?? throw ApiException.Server(401, "Sign in to continue.");
        var client = Client(profileId);
        try
        {
            return await call(client, session.AccessToken).ConfigureAwait(false);
        }
        catch (ApiException expired) when (expired.Status == 401)
        {
            Session fresh;
            try
            {
                fresh = await client.RefreshAsync(session).ConfigureAwait(false);
            }
            catch (ApiException refresh) when (refresh.Status is 400 or 401 or 403)
            {
                Forget();
                throw;
            }

            Accept(fresh);
            return await call(client, fresh.AccessToken).ConfigureAwait(false);
        }
    }

    /// <summary>Asks the server for a phone sign-in and waits for it (checking every <paramref name="every"/>). Returns false if the code ran out or the wait was cancelled.</summary>
    public async Task<QrOutcome> SignInWithQrAsync(Action<string> showCode, TimeSpan every, CancellationToken cancel)
    {
        try
        {
            var client = Client();
            var qr = await client.CreateQrSessionAsync(_device.Id(), _device.Name, cancel).ConfigureAwait(true);
            showCode(qr.ActivationUrl);
            while (!cancel.IsCancellationRequested)
            {
                await Task.Delay(every, cancel).ConfigureAwait(true);
                QrPoll poll;
                try
                {
                    poll = await client.PollQrSessionAsync(qr.Token, cancel).ConfigureAwait(true);
                }
                catch (Exception ex) when (!cancel.IsCancellationRequested && ex is ApiException or HttpRequestException or OperationCanceledException)
                {
                    continue; // a hiccup: ask again
                }

                switch (poll)
                {
                    case QrPoll.SignedIn done:
                        Accept(done.Session);
                        return QrOutcome.SignedIn;
                    case QrPoll.Expired:
                        return QrOutcome.Expired;
                }
            }

            return QrOutcome.Cancelled;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return QrOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            Error = Describe(ex);
            return QrOutcome.Failed;
        }
    }
}

public enum QrOutcome { SignedIn, Expired, Cancelled, Failed }
