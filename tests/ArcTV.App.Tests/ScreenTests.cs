using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ArcTV.App.Views;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Home;
using ArcTV.Core.Storage;
using Xunit;

namespace ArcTV.App.Tests;

/// <summary>Each screen is created, shown (off to the side) and laid out. Any broken XAML, missing style or bad binding shows up here.</summary>
public class ScreenTests
{
    private sealed class Handler(Func<string, string> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer(request.RequestUri!.AbsolutePath), Encoding.UTF8, "application/json") });
    }

    private const string Manifest = """{"id":"x","name":"Cool","catalogs":[{"type":"movie","id":"all","extra":[{"name":"genre","options":["Noir","Anime"]}]}]}""";

    private static string Answer(string path) => path switch
    {
        "/user/addons" => $$"""{"items":[{"manifestUrl":"https://x.test/manifest.json","addonId":"x","name":"Cool","enabled":true,"sortOrder":0,"manifestJson":{{Manifest}}}]}""",
        _ => """{"metas":[{"id":"a","type":"movie","name":"Alpha","description":"<b>Hi</b> &amp; bye","genres":["Noir"],"releaseInfo":"2020","imdbRating":"7.1"},{"id":"b","type":"movie","name":"Beta"}]}""",
    };

    /// <summary>Runs on a thread of its own that can show windows, with the app's colours and styles loaded.</summary>
    private static void OnUiThread(Func<Task> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ArcTV;component/Theme/Theme.xaml") });
                }

                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var task = body();
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("A screen failed to open: " + failure, failure);
    }

    private static Window Host(FrameworkElement content)
    {
        var window = new Window { Width = 1280, Height = 800, Left = -3000, Top = 0, ShowActivated = false, ShowInTaskbar = false, Content = content };
        window.Show();
        return window;
    }

    private static async Task Settle() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static AccountModel SignedIn(string folder)
    {
        var store = new SessionStore(Path.Combine(folder, "s.dat"), new PlainProtector());
        store.Save(new Session("a", "e", "r", "e", new AccountUser("u1", "me@x.test", "Me")));
        return new AccountModel(store, new DeviceIdentity(Path.Combine(folder, "d.id"), "PC"), new LocalSettings(Path.Combine(folder, "set.json")), new HttpClient(new Handler(Answer)));
    }

    [Fact]
    public void ThemeStylesAllLoad() => OnUiThread(() =>
    {
        foreach (var key in new[] { "PrimaryButton", "GhostButton", "LinkButton", "Field", "PasswordField", "FieldLabel", "NavPill", "DotButton" })
            Assert.NotNull(Application.Current.TryFindResource(key));
        return Task.CompletedTask;
    });

    [Fact]
    public void SignInScreenOpens() => OnUiThread(async () =>
    {
        var view = new SignInView();
        var window = Host(view);
        await Settle();
        Assert.True(view.ActualWidth > 0);
        window.Close();
    });

    [Fact]
    public void HomeScreenShowsRowsAndBanner() => OnUiThread(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "arctv-ui-" + Guid.NewGuid());
        var account = SignedIn(folder);
        var model = new HomeModel(async cancel => await account.AuthorizedAsync((client, token) => client.AddonsAsync(token, cancel: cancel)), "u1",
            new AddonClient(new HttpClient(new Handler(Answer))));
        var view = new HomeView(model);
        var window = Host(view);
        await model.LoadAsync();
        await Settle();
        Assert.Equal(HomeState.Loaded, model.State);
        Assert.NotEmpty(model.Sections);
        Assert.NotEmpty(model.Hero);
        window.Close();
    });

    [Fact]
    public void ShellOpensWithItsTopBar() => OnUiThread(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "arctv-ui-" + Guid.NewGuid());
        var view = new ShellView(SignedIn(folder));
        var window = Host(view);
        await Settle();
        await Task.Delay(500);
        await Settle();
        Assert.True(view.ActualHeight > 0);
        window.Close();
    });
}
