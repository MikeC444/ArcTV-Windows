using System.Reflection;

namespace ArcTV.Core;

public static class AppInfo
{
    /// <summary>The version this build was published as (the release workflow passes it in); "dev" when run from Visual Studio or the command line.</summary>
    public static string Version => VersionFrom(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

    /// <summary>The backend address baked in at build time (the release workflow's API_BASE_URL secret), or empty.</summary>
    public static string BundledApiBaseUrl => MetadataFrom(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly(), "ArcTVApiBaseUrl");

    /// <summary>The Arc TV backend, used when nothing else is set (the same server the Android, web and Mac apps use). Not a secret: it is inside every APK.</summary>
    public const string DefaultApiBaseUrl = "https://mangotv-live-tv.onrender.com";

    /// <summary>The id the other apps save Cinemeta titles under.</summary>
    public const string CinemetaProviderId = "com.linvo.cinemeta";

    /// <summary>The same Cinemeta addon the other apps start with, so Home always has something to show.</summary>
    public const string CinemetaManifestUrl = "https://v3-cinemeta.strem.io/manifest.json";

    /// <summary>The address to sign in against: what the person saved, else the one baked in at build time, else the default.</summary>
    public static string ResolveApiBaseUrl(string? saved, string? bundled)
    {
        foreach (var candidate in new[] { saved, bundled })
        {
            var trimmed = (candidate ?? "").Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return DefaultApiBaseUrl;
    }

    /// <summary>"1.2.3+abc123" and "1.2.3" are 1.2.3; the development build and anything unreadable are "dev".</summary>
    public static string CleanVersion(string? informational)
    {
        var text = (informational ?? "").Split('+')[0].Trim();
        return text.Length == 0 || text.StartsWith("0.0.0-dev", StringComparison.Ordinal) ? "dev" : text;
    }

    internal static string VersionFrom(Assembly assembly) => CleanVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string MetadataFrom(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";
}
