using System.Text.Json;
using ArcTV.Core.Account;

namespace ArcTV.Core.Plus;

/// <summary>What the account says about Arc TV Plus (<c>GET /user/plus</c>). While the paywall is off (early access) everyone is active.</summary>
public sealed record PlusStatus(bool Active, string? Plan, string? ValidUntil, bool Paywall, bool CancelAtPeriodEnd, int TrialDays)
{
    public static PlusStatus None { get; } = new(false, null, null, false, false, 0);

    /// <summary>Anything missing takes its usual default; an odd value is ignored rather than failing the whole read.</summary>
    public static PlusStatus Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) throw new JsonException("Not a Plus status.");
        bool Flag(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        string? Text(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var trial = r.TryGetProperty("trialDays", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var days) ? days : 0;
        return new PlusStatus(Flag("active"), Text("plan"), Text("validUntil"), Flag("paywall"), Flag("cancelAtPeriodEnd"), trial);
    }
}

/// <summary>The plans Arc TV Plus is sold as. The real prices live in Stripe and show on its checkout page; the cards only name the plan.</summary>
public sealed record PlusPlan(string Id, string Label, string Per, string Blurb, string? Note)
{
    public static readonly IReadOnlyList<PlusPlan> All =
    [
        new("monthly", "Monthly", "per month", "Cancel any time.", null),
        new("yearly", "Yearly", "per year", "Cancel any time.", "Best value"),
        new("lifetime", "Lifetime", "one-time payment", "Pay once, keep Plus forever. No renewals.", "Pay once"),
    ];

    public static string? LabelFor(string? id) => All.FirstOrDefault(p => p.Id == id)?.Label;
}

public sealed record PlusPerk(string Title, string Detail, bool Soon, string? Tag = null);

public static class PlusCopy
{
    public const string FreeNote = "Everything you use today stays free: browsing, playing, My List, Continue Watching, addons and Blocked Genres.";

    public const string ProceedsNote = "Every subscription goes straight back into building and running Arc TV: new features, faster servers and keeping the free app free.";

    public static readonly IReadOnlyList<PlusPerk> Perks =
    [
        new("Picked for you", "A Home row chosen from the movies and shows you like, finish and save, with the reason under each poster, plus Like and Not for me on movies and shows.", false),
        new("Profiles", "Up to 5 profiles on one account, each with its own My List, Continue Watching, settings and recommendations. Add kids profiles, and lock any profile with a PIN.", false),
        new("Parental controls", "Locks on individual genres and titles, built on kids profiles and Blocked Genres.", true),
        new("Smart source picking", "Skips Select a Source and starts the best source your device can play, once every addon has answered. Turn it on in Plus settings.", false),
        new("Your stats", "How much you watch, how this week compares, your streak, a map of your last 13 weeks and your busiest day, under Settings > Your stats.", false),
        new("Download", "A Download button on a movie or episode lists the files from your debrid service, best quality at the smallest size first, to save to your PC.", true, "Web and Mac now"),
    ];
}

/// <summary>Where checkout sends the person: Stripe's own page.</summary>
public sealed record CheckoutLink(string Url, int? AmountTotal = null, string? Currency = null, int? TrialDays = null);

public enum Rating { Like, Dislike }

/// <summary>One title's Like / Not for me (<c>GET /user/feedback</c>).</summary>
public sealed record FeedbackItem(string ProfileId, string ProviderId, string ContentId, string ContentType, string Title, string Feedback, string UpdatedAt, string? DeletedAt = null)
{
    public Rating? Rating => Feedback.ToLowerInvariant() switch { "like" => Plus.Rating.Like, "dislike" => Plus.Rating.Dislike, _ => null };

    public bool IsMovie => ContentType == "MOVIE";
}

internal sealed record FeedbackResponse(List<FeedbackItem>? Items);

public static class PlusApi
{
    public static async Task<PlusStatus> PlusStatusAsync(this AuthClient client, string accessToken, CancellationToken cancel = default)
    {
        var body = await client.SendJsonAsync(HttpMethod.Get, "/user/plus", null, null, accessToken, cancel: cancel).ConfigureAwait(false);
        try
        {
            return PlusStatus.Parse(body);
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    /// <summary>Starts a checkout for "monthly", "yearly" or "lifetime". The server answers 409 (already Plus for life) or 503 (checkout not available yet) as errors.</summary>
    public static async Task<CheckoutLink> StartCheckoutAsync(this AuthClient client, string accessToken, string plan, CancellationToken cancel = default)
    {
        var body = await client.SendJsonAsync(HttpMethod.Post, "/user/plus/checkout", null, JsonSerializer.Serialize(new { plan }), accessToken, cancel: cancel).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<CheckoutLink>(body, ApiHttp.Json) is { Url.Length: > 0 } link ? link : throw ApiException.Unreadable();
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    /// <summary>Cancels a monthly or yearly subscription at the end of the period already paid for (Plus stays on until then) and answers with the new status.</summary>
    public static async Task<PlusStatus> CancelSubscriptionAsync(this AuthClient client, string accessToken, CancellationToken cancel = default)
    {
        var body = await client.SendJsonAsync(HttpMethod.Post, "/user/plus/cancel", null, "{}", accessToken, cancel: cancel).ConfigureAwait(false);
        try
        {
            return PlusStatus.Parse(body);
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    public static async Task<List<FeedbackItem>> FeedbackAsync(this AuthClient client, string accessToken, CancellationToken cancel = default)
    {
        var body = await client.SendJsonAsync(HttpMethod.Get, "/user/feedback", new Dictionary<string, string> { ["profileId"] = client.ProfileId ?? "main" }, null, accessToken, cancel: cancel).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<FeedbackResponse>(body, ApiHttp.Json)?.Items ?? throw ApiException.Unreadable();
        }
        catch (JsonException)
        {
            throw ApiException.Unreadable();
        }
    }

    public static Task ClearFeedbackAsync(this AuthClient client, string accessToken, FeedbackItem item, CancellationToken cancel = default) =>
        client.SendJsonAsync(HttpMethod.Delete, "/user/feedback",
            new Dictionary<string, string>
            {
                ["profileId"] = client.ProfileId ?? item.ProfileId, ["providerId"] = item.ProviderId, ["contentId"] = item.ContentId, ["contentType"] = item.ContentType, ["updatedAt"] = IsoDate.Now(),
            },
            null, accessToken, cancel: cancel);
}

/// <summary>
/// "Picked for you": every number that shapes the recommendation, named. These are the Fire TV app's values (which come from the web app), so every Arc TV app ranks the same way.
/// The score is an internal ranking value only: it is not the IMDb rating, a star rating, or a probability that someone will enjoy a title.
/// </summary>
public static class RecommendConfig
{
    public const double WeightLike = 5;
    public const double WeightDislike = -5;
    public const double WeightCompleted = 2;
    public const double WeightWatchlist = 1;
    public const int MinInteractionsForPersonalisation = 3;
    public const int InteractionDetailFetchLimit = 60;
    public const int PickedDismissDays = 5;
}

/// <summary>
/// What Settings > Recommendations says about a profile's taste: how many titles feed it and how many points each kind of title adds. Counted the way the engine counts: a rated title
/// counts once, by its rating, and a title that is only saved or finished counts for the smaller amount. The points are weights, not a score.
/// </summary>
public sealed record RecommendationSummary(int Likes, int Dislikes, int Finished, int Saved)
{
    public int LikePoints => Likes * (int)RecommendConfig.WeightLike;

    public int DislikePoints => Dislikes * (int)RecommendConfig.WeightDislike;

    public int ListPoints => Finished * (int)RecommendConfig.WeightCompleted + Saved * (int)RecommendConfig.WeightWatchlist;

    public int Signals => Likes + Dislikes + Finished + Saved;

    /// <summary>More than the engine looks at: only its strongest signals shape the picks.</summary>
    public bool OverLimit => Signals > RecommendConfig.InteractionDetailFetchLimit;

    /// <summary>Personal picks start once there are enough signals and at least one is positive; until then Home shows popular titles.</summary>
    public bool Ready => Signals >= RecommendConfig.MinInteractionsForPersonalisation && Likes + Finished + Saved > 0;

    public static RecommendationSummary Of(IReadOnlyDictionary<string, Rating> ratings, IEnumerable<MyList.WatchlistItem> list)
    {
        var unrated = list.Where(i => !ratings.ContainsKey(i.ContentId)).ToList();
        return new RecommendationSummary(ratings.Values.Count(r => r == Rating.Like), ratings.Values.Count(r => r == Rating.Dislike), unrated.Count(i => i.Watched == true), unrated.Count(i => i.Watched != true));
    }

    /// <summary>"+20 points", "−15 points", "0 points", "+1 point".</summary>
    public static string PointsLabel(int points)
    {
        var sign = points > 0 ? "+" : points < 0 ? "−" : "";
        var n = Math.Abs(points);
        return $"{sign}{n} {(n == 1 ? "point" : "points")}";
    }
}
