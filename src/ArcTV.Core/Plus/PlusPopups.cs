namespace ArcTV.Core.Plus;

/// <summary>When the Plus welcome card is shown (as on the Mac app): to members, once, until its content changes (a new <see cref="WelcomeId"/>).</summary>
public static class PlusPopups
{
    /// <summary>Names this version of the welcome card; changing it shows the card once more to members who have seen the old one.</summary>
    public const string WelcomeId = "2026-10-windows-1";

    public static string WelcomeKey(string accountId) => $"plus-welcome-{accountId}";

    public static bool WelcomeDue(string? seen) => !string.Equals(seen, WelcomeId, StringComparison.Ordinal);
}
