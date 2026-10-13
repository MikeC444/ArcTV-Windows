using System.Windows;
using System.Windows.Controls;
using ArcTV.App.Controls;
using ArcTV.App.Views;
using ArcTV.Core.Plus;
using ArcTV.Core.Profiles;

namespace ArcTV.App;

public partial class MainWindow : Window
{
    private int _generation;
    private PlusModel? _plus;
    private ProfilesModel? _profiles;
    private bool _signedIn;

    public MainWindow()
    {
        InitializeComponent();
        App.Account.PropertyChanged += (_, args) =>
        {
            // the account announces this every time its token is renewed too: only signing in or out changes what is shown
            if (args.PropertyName == nameof(Core.Account.AccountModel.IsSignedIn)) Dispatcher.Invoke(() =>
            {
                if (App.Account.IsSignedIn != _signedIn) ShowCurrent();
            });
        };
        ShowCurrent();
    }

    /// <summary>Sign-in when nobody is signed in; otherwise the profiles are read (with Plus) and "Who's watching?" comes first when there is more than one.</summary>
    private void ShowCurrent()
    {
        _signedIn = App.Account.IsSignedIn;
        if (!_signedIn)
        {
            _generation++;
            Host.Content = new SignInView();
            return;
        }

        _ = EnterAsync();
    }

    private static Grid Loading() => new() { Background = (System.Windows.Media.Brush)Application.Current.FindResource("BgBrush"), Children = { new Spinner { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };

    private async Task EnterAsync()
    {
        var generation = ++_generation;
        var account = App.Account;
        Host.Content = Loading();
        var plus = _plus = new PlusModel(new AccountPlusStore(account));
        var profiles = _profiles = new ProfilesModel(new AccountProfileStore(account));
        await plus.LoadStatusAsync();
        await profiles.LoadAsync(plus.Active);
        if (generation != _generation || !account.IsSignedIn) return;
        if (profiles.Profiles.Count > 1) ShowPicker(false);
        else Start(profiles.Find(Profile.MainId));
    }

    /// <summary>The app on one profile's library: nothing of the last profile stays in memory, because the whole app is made again.</summary>
    private void Start(Profile? profile)
    {
        var account = App.Account;
        account.UseProfile(profile?.Id);
        Host.Content = new ShellView(account, _plus!, profile, _plus!.Active ? () => ShowPicker(true) : null);
    }

    /// <summary>"Who's watching?", from the menu (<paramref name="canGoBack"/>: Back returns to the profile in use) or at the start.</summary>
    private async void ShowPicker(bool canGoBack)
    {
        var account = App.Account;
        var generation = ++_generation;
        var current = account.ProfileId;
        if (canGoBack) await _profiles!.LoadAsync(_plus!.Active); // another device may have changed them
        if (generation != _generation || !account.IsSignedIn) return;
        Host.Content = new ProfilePickerView(_profiles!, canGoBack ? current : null, Start, canGoBack ? () => Start(_profiles!.Find(current) ?? _profiles.Find(Profile.MainId)) : null);
    }
}
