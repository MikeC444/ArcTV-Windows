namespace ArcTV.Core.Account;

/// <summary>What went wrong talking to a server, in words a person can read.</summary>
public sealed class ApiException : Exception
{
    public enum ErrorKind { Server, BadUrl, Unreadable }

    public ErrorKind Kind { get; }

    /// <summary>The HTTP status for a server error, else 0.</summary>
    public int Status { get; }

    public string? ServerMessage { get; }

    private ApiException(ErrorKind kind, int status, string? serverMessage, string message) : base(message)
    {
        Kind = kind;
        Status = status;
        ServerMessage = serverMessage;
    }

    public static ApiException Server(int status, string? serverMessage) =>
        new(ErrorKind.Server, status, serverMessage,
            !string.IsNullOrEmpty(serverMessage) ? serverMessage : status == 401 ? "Wrong email or password." : $"The server answered with an error (HTTP {status}).");

    public static ApiException BadUrl(string text) => new(ErrorKind.BadUrl, 0, null, $"That address doesn't look right: {text}");

    public static ApiException Unreadable() => new(ErrorKind.Unreadable, 0, null, "The server's answer couldn't be read.");
}
