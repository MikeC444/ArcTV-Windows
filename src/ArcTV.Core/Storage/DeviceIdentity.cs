namespace ArcTV.Core.Storage;

/// <summary>A random id made once per install, so the backend can tell this PC apart from the user's other devices.</summary>
public sealed class DeviceIdentity(string path, string? name = null)
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? Environment.MachineName : name;

    public string Id()
    {
        try
        {
            if (File.Exists(path))
            {
                var saved = File.ReadAllText(path).Trim();
                if (Guid.TryParse(saved, out _)) return saved;
            }
        }
        catch (IOException)
        {
        }

        var fresh = Guid.NewGuid().ToString();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, fresh);
        }
        catch (IOException)
        {
        }

        return fresh;
    }
}
