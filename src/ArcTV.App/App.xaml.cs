using System.IO;
using System.Windows;
using ArcTV.Core.Account;
using ArcTV.Core.Storage;

namespace ArcTV.App;

public partial class App : Application
{
    /// <summary>Who is signed in on this PC (kept scrambled for this Windows user, in %LOCALAPPDATA%\ArcTV).</summary>
    public static AccountModel Account { get; } = CreateAccount();

    private static AccountModel CreateAccount()
    {
        var folder = AppPaths.DataFolder();
        return new AccountModel(
            new SessionStore(Path.Combine(folder, "session.dat"), new DpapiProtector()),
            new DeviceIdentity(Path.Combine(folder, "device.id")),
            new LocalSettings(Path.Combine(folder, "settings.json")));
    }
}
