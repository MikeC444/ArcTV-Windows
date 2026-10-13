using ArcTV.Core.Account;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Plus;

/// <summary>Where Plus things are kept (the account). The tests use a stand-in.</summary>
public interface IPlusStore
{
    Task<PlusStatus> StatusAsync(CancellationToken cancel = default);

    Task<CheckoutLink> StartCheckoutAsync(string plan, CancellationToken cancel = default);

    Task<PlusStatus> CancelAsync(CancellationToken cancel = default);

    Task<IReadOnlyList<FeedbackItem>> FeedbackAsync(CancellationToken cancel = default);

    Task ClearFeedbackAsync(FeedbackItem item, CancellationToken cancel = default);

    Task<(IReadOnlyList<HistoryItem> Items, bool Truncated)> HistoryAsync(CancellationToken cancel = default);
}

public sealed class AccountPlusStore(AccountModel account) : IPlusStore
{
    public Task<PlusStatus> StatusAsync(CancellationToken cancel = default) => account.AuthorizedAsync((c, t) => c.PlusStatusAsync(t, cancel));

    public Task<CheckoutLink> StartCheckoutAsync(string plan, CancellationToken cancel = default) => account.AuthorizedAsync((c, t) => c.StartCheckoutAsync(t, plan, cancel));

    public Task<PlusStatus> CancelAsync(CancellationToken cancel = default) => account.AuthorizedAsync((c, t) => c.CancelSubscriptionAsync(t, cancel));

    public async Task<IReadOnlyList<FeedbackItem>> FeedbackAsync(CancellationToken cancel = default) => await account.AuthorizedAsync((c, t) => c.FeedbackAsync(t, cancel)).ConfigureAwait(false);

    public Task ClearFeedbackAsync(FeedbackItem item, CancellationToken cancel = default) =>
        account.AuthorizedAsync(async (c, t) =>
        {
            await c.ClearFeedbackAsync(t, item, cancel).ConfigureAwait(false);
            return true;
        });

    public async Task<(IReadOnlyList<HistoryItem> Items, bool Truncated)> HistoryAsync(CancellationToken cancel = default)
    {
        var (items, truncated) = await account.AuthorizedAsync((c, t) => c.HistoryAsync(t, cancel)).ConfigureAwait(false);
        return (items, truncated);
    }
}

/// <summary>Arc TV Plus as the Settings pages use it: whether the account has it, what it has liked and marked Not for me, and its watch statistics.</summary>
public sealed class PlusModel(IPlusStore store, Func<DateTime>? now = null, TimeSpan? pollEvery = null) : ObservableObject
{
    private PlusStatus _status = PlusStatus.None;
    private bool _statusKnown;
    private IReadOnlyDictionary<string, FeedbackItem> _ratings = new Dictionary<string, FeedbackItem>();
    private bool _ratingsLoaded;
    private WatchStats? _stats;
    private IReadOnlyDictionary<string, string> _posters = new Dictionary<string, string>();
    private bool _statsTruncated;
    private bool _statsFailed;
    private CancellationTokenSource? _watch;

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly TimeSpan _pollEvery = pollEvery ?? TimeSpan.FromSeconds(4);

    public PlusStatus Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>False until the account has answered once (a Plus-only page waits rather than showing the lock).</summary>
    public bool StatusKnown { get => _statusKnown; private set => Set(ref _statusKnown, value); }

    public bool Active => Status.Active;

    public IReadOnlyDictionary<string, FeedbackItem> Ratings { get => _ratings; private set => Set(ref _ratings, value); }

    public bool RatingsLoaded { get => _ratingsLoaded; private set => Set(ref _ratingsLoaded, value); }

    /// <summary>The poster of each rated title (by id), looked up when the Recommendations page opens: the account keeps the rating, not the picture.</summary>
    public IReadOnlyDictionary<string, string> Posters { get => _posters; private set => Set(ref _posters, value); }

    public WatchStats? Stats { get => _stats; private set => Set(ref _stats, value); }

    public bool StatsTruncated { get => _statsTruncated; private set => Set(ref _statsTruncated, value); }

    public bool StatsFailed { get => _statsFailed; private set => Set(ref _statsFailed, value); }

    public async Task LoadStatusAsync(CancellationToken cancel = default)
    {
        try
        {
            Status = await store.StatusAsync(cancel).ConfigureAwait(true);
            StatusKnown = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // keep what was known
        }
    }

    public async Task LoadRatingsAsync(CancellationToken cancel = default)
    {
        try
        {
            var items = await store.FeedbackAsync(cancel).ConfigureAwait(true);
            Ratings = items.Where(i => i.DeletedAt is null && i.Rating is not null).GroupBy(i => i.ContentId).ToDictionary(g => g.Key, g => g.First());
            RatingsLoaded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RatingsLoaded = true;
        }
    }

    public async Task LoadStatsAsync(CancellationToken cancel = default)
    {
        StatsFailed = false;
        try
        {
            var (items, truncated) = await store.HistoryAsync(cancel).ConfigureAwait(true);
            StatsTruncated = truncated;
            Stats = WatchStats.Compute(items, _now());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatsFailed = true;
        }
    }

    /// <summary>Finds a poster for every rated title that has none yet (four lookups at a time) and announces them together. A title whose lookup fails just stays without one.</summary>
    public async Task LoadPostersAsync(Func<FeedbackItem, CancellationToken, Task<string?>> lookup, CancellationToken cancel = default)
    {
        var need = Ratings.Values.Where(i => !Posters.ContainsKey(i.ContentId)).ToList();
        if (need.Count == 0) return;
        var found = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(need.Select(async item =>
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                if (await lookup(item, cancel).ConfigureAwait(false) is { Length: > 0 } url) found[item.ContentId] = url;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // no poster for this one
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(true);
        if (found.IsEmpty) return;
        var all = Posters.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (id, url) in found) all[id] = url;
        Posters = all;
    }

    /// <summary>Takes the rating off one title. Shown at once; put back if the account could not be reached.</summary>
    public async Task RemoveRatingAsync(FeedbackItem item, CancellationToken cancel = default)
    {
        var before = Ratings;
        Ratings = before.Where(p => p.Key != item.ContentId).ToDictionary(p => p.Key, p => p.Value);
        try
        {
            await store.ClearFeedbackAsync(item, cancel).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Ratings = before;
        }
    }

    /// <summary>Clears every Like and Not for me. Anything the account could not clear stays.</summary>
    public async Task ResetRatingsAsync(CancellationToken cancel = default)
    {
        foreach (var item in Ratings.Values.ToList()) await RemoveRatingAsync(item, cancel).ConfigureAwait(true);
    }

    /// <summary>Starts checkout for a plan and gives back Stripe's address to open in the browser; then asks every few seconds until Plus is on (or ten minutes pass).</summary>
    public async Task<string> StartCheckoutAsync(string plan, CancellationToken cancel = default)
    {
        var link = await store.StartCheckoutAsync(plan, cancel).ConfigureAwait(true);
        if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw ApiException.Unreadable();
        _ = WatchForPurchaseAsync();
        return link.Url;
    }

    public async Task WatchForPurchaseAsync()
    {
        _watch?.Cancel();
        var run = _watch = new CancellationTokenSource();
        var until = _now() + TimeSpan.FromMinutes(10);
        try
        {
            while (!run.IsCancellationRequested && _now() < until)
            {
                await Task.Delay(_pollEvery, run.Token).ConfigureAwait(true);
                await LoadStatusAsync(run.Token).ConfigureAwait(true);
                if (Active) return;
            }
        }
        catch (OperationCanceledException)
        {
            // a newer watch took over
        }
    }

    public void StopWatching() => _watch?.Cancel();

    public async Task CancelSubscriptionAsync(CancellationToken cancel = default) => Status = await store.CancelAsync(cancel).ConfigureAwait(true);

    /// <summary>What a failed checkout or cancel says, in words a person can read.</summary>
    public static string CheckoutProblem(Exception ex) => ex is ApiException { Status: 409 } ? "You already have Plus for life."
        : ex is ApiException { Status: 503 } ? "Plus checkout isn't available yet." : "Couldn't start checkout. Try again in a moment.";

    public static string CancelProblem(Exception ex) => ex is ApiException { Status: 409 } ? "Lifetime Plus has no subscription to cancel."
        : ex is ApiException { Status: 404 } ? "We couldn't find a subscription to cancel on this account."
        : ex is ApiException { Status: 429 } e ? e.ServerMessage ?? "Too many tries. Try again in a moment." : "Couldn't cancel your subscription. Try again in a moment.";
}
