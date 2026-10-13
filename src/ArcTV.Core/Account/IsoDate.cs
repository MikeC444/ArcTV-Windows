using System.Globalization;

namespace ArcTV.Core.Account;

/// <summary>Timestamps the backend expects: ISO-8601 in UTC with milliseconds ("2026-10-10T00:20:00.123Z").</summary>
public static class IsoDate
{
    public static string Now() => From(DateTime.UtcNow);

    public static string From(DateTime time) => time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Reads a backend timestamp, with or without fractional seconds.</summary>
    public static DateTime? Parse(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) ? time : null;
}
