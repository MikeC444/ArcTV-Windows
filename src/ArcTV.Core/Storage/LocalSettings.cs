using System.Text.Json;

namespace ArcTV.Core.Storage;

/// <summary>Small per-PC choices kept in settings.json (not secret).</summary>
public sealed class LocalSettings(string path)
{
    private sealed record Data(string? ServerUrl);

    public string? ServerUrl
    {
        get => Read().ServerUrl;
        set => Write(new Data(string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/')));
    }

    private Data Read()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? new Data(null) : new Data(null);
        }
        catch (Exception)
        {
            return new Data(null);
        }
    }

    private void Write(Data data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(data));
    }
}
