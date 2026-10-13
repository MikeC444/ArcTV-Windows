using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using ArcTV.Core.Home;
using ArcTV.Core.Settings;
using ArcTV.Core.Storage;

namespace ArcTV.App.Views;

/// <summary>Settings laid out like the web app's: grouped places down the left, the chosen one on the right.</summary>
public sealed class SettingsView : UserControl
{
    private sealed record Place(string Name, string Subtitle, string Glyph, bool Soon = false);

    private static readonly (string Group, Place[] Places)[] Groups =
    [
        ("You", [new("Account", "Manage your Arc TV account", "\uE77B"), new("Arc TV Plus", "Extra features for supporters", "\uE734", true), new("Plus settings", "Switches for your Plus features", "\uE713", true),
            new("Recommendations", "What shapes your Picked for you row", "\uE735", true), new("Your stats", "How much you watch, at a glance", "\uE9D2", true)]),
        ("Content", [new("Addons", "Manage installed content providers", "\uEA86"), new("Home Rows", "Choose which rows show up on Home", "\uF0E2"), new("Blocked Genres", "Hide genres you don't want to see", "\uE711")]),
        ("Playback", [new("Player", "Choose how video plays", "\uE714"), new("Subtitles", "Look and language", "\uED1E"), new("Audio", "Preferred audio language", "\uE767")]),
        ("App", [new("Updates", "Version and updates", "\uE896")]),
    ];

    private readonly SettingsModel _settings;
    private readonly AddonsModel _addons;
    private readonly HomeModel _home;
    private readonly AccountUser _user;
    private readonly Action _refreshHome;
    private readonly Action _signOut;
    private readonly LocalSettings _local;
    private readonly ContentControl _pane = new();
    private readonly TextBlock _title = new() { FontSize = 28, FontWeight = FontWeights.ExtraBold, Margin = new Thickness(0, 0, 0, 18) };
    private readonly Dictionary<string, Border> _rows = [];
    private string _current = "Account";

    public SettingsView(SettingsModel settings, AddonsModel addons, HomeModel home, AccountUser user, LocalSettings local, Action refreshHome, Action signOut, string open = "Account")
    {
        _settings = settings;
        _addons = addons;
        _home = home;
        _user = user;
        _local = local;
        _refreshHome = refreshHome;
        _signOut = signOut;
        _current = open;
        Background = Ui.Res("BgBrush");

        var grid = new Grid { Margin = new Thickness(24, 96, 24, 24) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var side = new StackPanel { Margin = new Thickness(12) };
        foreach (var (group, places) in Groups)
        {
            side.Children.Add(new TextBlock { Text = group.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Ui.Res("Text3Brush"), Margin = new Thickness(12, group == "You" ? 0 : 16, 0, 4) });
            foreach (var place in places) side.Children.Add(Row(place));
        }

        var sidebar = new Border
        {
            Background = Ui.Res("BgElevatedBrush"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 24, 0),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = side },
        };
        grid.Children.Add(sidebar);

        var right = new StackPanel { MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 40) };
        right.Children.Add(_title);
        right.Children.Add(_pane);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = right };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;
        Show(_current);
    }

    private Border Row(Place place)
    {
        var icon = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
            Child = new TextBlock { Text = place.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = Ui.Res("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = place.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = place.Subtitle, FontSize = 11, Foreground = Ui.Res("Text3Brush"), TextTrimming = TextTrimming.CharacterEllipsis });
        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        if (place.Soon)
        {
            var tag = new TextBlock { Text = "Soon", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Ui.Res("AccentBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(tag, Dock.Right);
            line.Children.Add(tag);
        }

        line.Children.Add(text);
        var row = new Border { Child = line, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Opacity = place.Soon ? 0.7 : 1 };
        row.MouseEnter += (_, _) => { if (_current != place.Name) row.Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255)); };
        row.MouseLeave += (_, _) => { if (_current != place.Name) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) => Show(place.Name);
        _rows[place.Name] = row;
        return row;
    }

    private void Show(string name)
    {
        _current = name;
        foreach (var (place, row) in _rows) row.Background = place == name ? new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)) : Brushes.Transparent;
        _title.Text = name;
        _pane.Content = name switch
        {
            "Account" => new AccountPane(_settings, _user.Email, _user.DisplayName, _refreshHome, _signOut),
            "Addons" => new AddonsPane(_addons),
            "Home Rows" => new HomeRowsPane(_settings, _home),
            "Blocked Genres" => new BlockedGenresPane(_settings),
            "Player" => new PlayerPane(_settings),
            "Subtitles" => new SubtitlesPane(_settings, _local),
            "Audio" => new AudioPane(_settings),
            "Updates" => new UpdatesPane(_settings),
            _ => new ComingSoonPane(name),
        };
    }
}
