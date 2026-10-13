using System.IO;
using System.Net.Http;
using System.Windows;
using ArcTV.Core.Account;
using ArcTV.Core.Storage;

namespace ArcTV.App;

public partial class App : Application
{
    /// <summary>Who is signed in on this PC (kept scrambled for this Windows user, in %LOCALAPPDATA%\ArcTV).</summary>
    public static AccountModel Account { get; } = CreateAccount();

    public App()
    {
        // a request that fails in the background (no connection, a server that does not answer) must never close the app: whatever was asked for just does not happen
        DispatcherUnhandledException += (_, e) =>
        {
            if (e.Exception is HttpRequestException or OperationCanceledException or ApiException) e.Handled = true;
        };
    }

    private static AccountModel CreateAccount()
    {
        var folder = AppPaths.DataFolder();
        return new AccountModel(
            new SessionStore(Path.Combine(folder, "session.dat"), new DpapiProtector()),
            new DeviceIdentity(Path.Combine(folder, "device.id")),
            new LocalSettings(Path.Combine(folder, "settings.json")));
    }
}
