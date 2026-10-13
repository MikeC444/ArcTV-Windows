namespace ArcTV.Core.Account;

public sealed record AccountUser(string Id, string Email, string? DisplayName);

/// <summary>A signed-in session: the token pair the backend hands out plus who it is for. Matches the backend's /auth/login answer.</summary>
public sealed record Session(string AccessToken, string AccessTokenExpiresAt, string RefreshToken, string RefreshTokenExpiresAt, AccountUser User);
