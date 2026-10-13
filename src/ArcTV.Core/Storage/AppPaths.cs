namespace ArcTV.Core.Storage;

public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\ArcTV (created on first use).</summary>
    public static string DataFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcTV");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
