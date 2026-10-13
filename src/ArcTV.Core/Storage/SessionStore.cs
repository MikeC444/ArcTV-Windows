using System.Text;
using System.Text.Json;
using ArcTV.Core.Account;

namespace ArcTV.Core.Storage;

/// <summary>Keeps the signed-in session on disk, scrambled for this Windows user, so the app opens already signed in.</summary>
public sealed class SessionStore(string path, ISecretProtector protector)
{
    public Session? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = Encoding.UTF8.GetString(protector.Unprotect(File.ReadAllBytes(path)));
            return JsonSerializer.Deserialize<Session>(json, ApiHttp.Json);
        }
        catch (Exception)
        {
            // unreadable (another Windows user, damaged file): treat as signed out
            return null;
        }
    }

    public void Save(Session session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = protector.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(session, ApiHttp.Json)));
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
