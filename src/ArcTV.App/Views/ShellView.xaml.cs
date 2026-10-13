using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Browse;
using ArcTV.Core.Home;

namespace ArcTV.App.Views;

/// <summary>The signed-in app: the floating top bar and whichever place is open.</summary>
public partial class ShellView : UserControl
{
    private static readonly string[] Places = ["Home", "Movies", "TV Shows", "Search", "My List", "Settings"];

    private readonly AccountModel _account;
    private readonly HomeModel _home;
    private readonly Dictionary<string, BrowseModel> _browse = [];
    private readonly AddonClient _addonClient = new();
    private IReadOnlyList<HomeSource> _sources = [];
    private string _place = "";
    private readonly Dictionary<string, Button> _pills = [];

    public ShellView(AccountModel account)
    {
        InitializeComponent();
        _account = account;
        var user = account.Session!.User;
        _home = new HomeModel(AccountAddonsAsync, user.Id);

        foreach (var place in Places)
        {
            var pill = new Button { Content = place, Tag = place, Style = (Style)FindResource("NavPill") };
            pill.Click += (_, _) => Open(place);
            _pills[place] = pill;
            Pills.Children.Add(pill);
        }

        BuildAccountButton(user);
        Open("Home");
        Loaded += async (_, _) => await _home.LoadAsync();
    }

    private void BuildAccountButton(AccountUser user)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!;
        var initial = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(4, 0, 10, 0),
            Background = (Brush)FindResource("BrandGradient"),
            Child = new TextBlock { Text = name[..1].ToUpperInvariant(), FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var label = new TextBlock { Text = name, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), FontWeight = FontWeights.SemiBold };
        AccountButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { initial, label } };

        var email = new MenuItem { Header = user.Email, IsEnabled = false };
        var signOut = new MenuItem { Header = "Sign out" };
        signOut.Click += async (_, _) => await _account.SignOutAsync();
        AccountMenu.Items.Add(email);
        AccountMenu.Items.Add(new Separator());
        AccountMenu.Items.Add(signOut);
    }

    private void OnAccount(object sender, RoutedEventArgs e)
    {
        AccountMenu.PlacementTarget = AccountButton;
        AccountMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        AccountMenu.IsOpen = true;
    }

    private void OnLogo(object sender, MouseButtonEventArgs e) => Open("Home");

    private async Task<IReadOnlyList<SyncedAddon>> AccountAddonsAsync(CancellationToken cancel) =>
        await _account.AuthorizedAsync((client, token) => client.AddonsAsync(token, cancel: cancel));

    /// <summary>The addons Movies and TV Shows browse. Kept once found, so each visit does not ask the account again.</summary>
    private async Task<IReadOnlyList<HomeSource>> SourcesAsync(CancellationToken cancel)
    {
        if (_sources.Count > 0) return _sources;
        var found = await AddonSources.LoadAsync(AccountAddonsAsync, _addonClient, cancel);
        if (found.Count > 0) _sources = found;
        return found;
    }

    /// <summary>One model per kind, kept for the session, so coming back to Movies finds the same titles in the same order.</summary>
    private BrowseModel BrowseFor(string type)
    {
        if (!_browse.TryGetValue(type, out var model)) _browse[type] = model = new BrowseModel(type, SourcesAsync, _addonClient);
        return model;
    }

    private void Open(string place)
    {
        if (place == _place) return;
        _place = place;
        foreach (var (name, pill) in _pills) pill.Tag = name == place ? "selected" : name;
        Page.Content = place switch
        {
            "Home" => new HomeView(_home),
            "Movies" => new BrowseView("Movies", BrowseFor("movie")),
            "TV Shows" => new BrowseView("TV Shows", BrowseFor("series")),
            _ => new ComingSoonView(place),
        };
    }
}
