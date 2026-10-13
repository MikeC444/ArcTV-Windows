using System.Text.Json;

namespace ArcTV.Core.Account;

/// <summary>The one place requests are sent from: a time limit on each, and server errors turned into <see cref="ApiException"/>.</summary>
internal static class ApiHttp
{
    private static readonly HttpClient Shared = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>A server on a free hosting plan can take a minute to wake up on the first request after a quiet spell.</summary>
    public static readonly TimeSpan SlowTimeout = TimeSpan.FromSeconds(60);

    public static async Task<string> SendAsync(HttpRequestMessage request, HttpClient? client = null, TimeSpan? timeout = null, CancellationToken cancel = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(timeout ?? DefaultTimeout);
        using var response = await (client ?? Shared).SendAsync(request, limit.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw ApiException.Server((int)response.StatusCode, ErrorMessage(body));
        return body;
    }

    /// <summary>The backend answers errors as {"error": "..."} (a few places use {"error": {"message": "..."}}).</summary>
    public static string? ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("error", out var error)) return null;
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String) return message.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
