using System.Text.RegularExpressions;

namespace ArcTV.Core.Account;

/// <summary>The same loose check the web app makes before asking the server (which stays the authority).</summary>
public static partial class Credentials
{
    public const int MinPasswordLength = 8;

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailShape();

    public static string? Problem(string email, string password)
    {
        var trimmed = email.Trim();
        if (trimmed.Length == 0) return "Enter your email address.";
        if (!EmailShape().IsMatch(trimmed)) return "Enter a valid email address.";
        if (password.Length < MinPasswordLength) return $"Password must be at least {MinPasswordLength} characters.";
        return null;
    }
}
