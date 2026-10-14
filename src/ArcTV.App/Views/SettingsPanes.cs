using System.Net.Http;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ArcTV.App.Controls;
using ArcTV.Core;
using ArcTV.Core.Home;
using ArcTV.Core.Settings;

namespace ArcTV.App.Views;

/// <summary>Small builders the settings panes share, so they look alike (cards, switches, a list of choices).</summary>
internal static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public static Style StyleOf(string key) => (Style)Application.Current.FindResource(key);

    public static Brush Pink => new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A));

    public static Brush Line => new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255));

    public static TextBlock Note(string text) => new() { Text = text, FontSize = 13, Foreground = Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };

    public static TextBlock Heading(string text, double top = 18) => new() { Text = text, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, top, 0, 6) };

    public static Border Card(UIElement child, double padding = 16) => new()
    {
        Background = Res("BgElevatedBrush"), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(padding),
        Margin = new Thickness(0, 0, 0, 10), Child = child,
    };

    /// <summary>A card with a title and a line under it on the left and an on/off switch on the right.</summary>
    public static Border ToggleRow(string title, string detail, bool on, Action<bool> changed, string? disabledReason = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        grid.Children.Add(text);
        var toggle = new ToggleButton { Style = (Style)Application.Current.FindResource("SwitchToggle"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), ToolTip = disabledReason };
        toggle.Click += (_, _) => changed(toggle.IsChecked == true);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return Card(grid);
    }

    /// <summary>A list of choices, one selected (a filled dot), in one card.</summary>
    public static Border Choices(IReadOnlyList<(string? Code, string Name)> choices, string? selected, Action<string?> pick)
    {
        var list = new StackPanel();
        for (var i = 0; i < choices.Count; i++)
        {
            var (code, name) = choices[i];
            if (i > 0) list.Children.Add(new Border { Height = 1, Background = Line });
            var chosen = string.Equals(code, selected, StringComparison.Ordinal);
            var row = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = chosen ? "●" : "○", FontSize = 14, Foreground = chosen ? Res("AccentBrush") : Res("Text3Brush"), VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 11, 0, 11) };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var captured = code;
            row.MouseLeftButtonUp += (_, _) => pick(captured);
            list.Children.Add(row);
        }

        return new Border { Background = Res("BgElevatedBrush"), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 0, 10), Child = list };
    }

    public static Button Act(string text, Action click, bool primary = false)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "GhostButton"), Height = 42, Margin = new Thickness(0, 0, 12, 0) };
        button.Click += (_, _) => click();
        return button;
    }
}

/// <summary>A pane that is built again from its model whenever the model changes, and asks for the account's settings the first time it is shown.</summary>
internal abstract class ModelPane : UserControl
{
    private readonly SettingsModel _settings;
    private readonly INotifyPropertyChanged[] _watched;

    protected ModelPane(SettingsModel settings, params INotifyPropertyChanged[] alsoWatch)
    {
        _settings = settings;
        _watched = [settings, .. alsoWatch];
        Loaded += async (_, _) =>
        {
            foreach (var w in _watched)
            {
                w.PropertyChanged -= OnChanged;
                w.PropertyChanged += OnChanged;
            }

            Rebuild();
            if (NeedsSettings && _settings.Current is null) await _settings.LoadAsync();
            await ShownAsync();
        };
        Unloaded += (_, _) =>
        {
            foreach (var w in _watched) w.PropertyChanged -= OnChanged;
        };
    }

    protected SettingsModel Settings => _settings;

    /// <summary>Plus pages do not need the account's settings, only Plus.</summary>
    protected virtual bool NeedsSettings => true;

    /// <summary>Called once the pane is on screen: where a pane asks for what it shows.</summary>
    protected virtual Task ShownAsync() => Task.CompletedTask;

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Rebuild);

    private void Rebuild()
    {
        var column = new StackPanel();
        if (_settings.Problem is { } problem) column.Children.Add(new TextBlock { Text = problem, FontSize = 13, Foreground = Ui.Pink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        Build(column);
        Content = column;
    }

    protected abstract void Build(StackPanel column);
}

internal sealed class AccountPane(SettingsModel settings, string email, string? name, Action refreshHome, Action signOut) : ModelPane(settings)
{
    protected override void Build(StackPanel column)
    {
        var rows = new StackPanel();
        void Row(string label, string value, bool first = false)
        {
            if (!first) rows.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 10, 0, 10) });
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = label, FontSize = 14, Foreground = Ui.Res("Text2Brush") });
            var v = new TextBlock { Text = value, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340 };
            Grid.SetColumn(v, 1);
            grid.Children.Add(v);
            rows.Children.Add(grid);
        }

        Row("Signed in as", email, true);
        Row("Name", string.IsNullOrWhiteSpace(name) ? "-" : name);
        column.Children.Add(Ui.Card(rows, 18));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Ui.Act("Refresh Home", refreshHome));
        buttons.Children.Add(Ui.Act("Sign out", signOut));
        column.Children.Add(buttons);
    }
}

internal sealed class HomeRowsPane : ModelPane
{
    private readonly HomeModel _home;

    public HomeRowsPane(SettingsModel settings, HomeModel home) : base(settings, home) => _home = home;

    protected override void Build(StackPanel column)
    {
        var choices = SettingsModel.RowChoices(_home.AllRows, Settings.RowPreferences);
        if (choices.Count == 0)
        {
            column.Children.Add(Ui.Note(_home.State == HomeState.Loading ? "Loading your rows…" : "Home has no rows yet. Install an addon first (Settings > Addons), then come back to arrange them."));
            return;
        }

        column.Children.Add(Ui.Note("Choose which rows appear on Home and the order they come in. Changes are saved automatically."));
        var order = choices.Select(c => c.Section.Id).ToList();
        for (var i = 0; i < choices.Count; i++)
        {
            var (section, visible) = choices[i];
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var id = section.Id;
            var toggle = new ToggleButton { Style = (Style)Application.Current.FindResource("SwitchToggle"), IsChecked = visible, VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += async (_, _) => await Settings.SetRowVisibleAsync(id, toggle.IsChecked == true);
            grid.Children.Add(toggle);
            var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Opacity = visible ? 1 : 0.5 };
            text.Children.Add(new TextBlock { Text = section.Title, FontSize = 15, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = $"{section.Items.Count} titles", FontSize = 12, Foreground = Ui.Res("Text3Brush") });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Button Arrow(string glyph, int delta, bool enabled, int column)
            {
                // the ghost button's own padding left no room for the arrow in a 32-pixel square (it showed as an empty square), so this is the round button the season numbers use
                var b = new Button
                {
                    Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    Width = 40, Height = 40, Margin = new Thickness(8, 0, 0, 0), IsEnabled = enabled, Opacity = enabled ? 1 : 0.3, ToolTip = delta < 0 ? "Move up" : "Move down",
                    Style = (Style)Application.Current.FindResource("SeasonCircle"), Foreground = Ui.Res("TextBrush"),
                };
                b.Click += async (_, _) => await Settings.MoveRowAsync(order, id, delta);
                Grid.SetColumn(b, column);
                return b;
            }

            grid.Children.Add(Arrow("\uE70E", -1, i > 0, 2));
            grid.Children.Add(Arrow("\uE70D", 1, i < choices.Count - 1, 3));
            column.Children.Add(Ui.Card(grid, 12));
        }
    }
}

internal sealed class BlockedGenresPane(SettingsModel settings) : ModelPane(settings)
{
    protected override void Build(StackPanel column)
    {
        column.Children.Add(Ui.Note("Pick the genres you never want to see. Titles in them are hidden on every device."));
        var wrap = new WrapPanel();
        var blocked = Settings.Blocked;
        foreach (var genre in Genres.Shows)
        {
            var on = blocked.Contains(genre);
            var button = new Button
            {
                Content = (on ? "⊘  " : "") + genre,
                Style = (Style)Application.Current.FindResource("ChoicePill"),
                Height = 38, Margin = new Thickness(0, 0, 10, 10), Tag = on ? "selected" : "",
            };
            var g = genre;
            button.Click += async (_, _) => await Settings.SetGenreAsync(g, !on);
            wrap.Children.Add(button);
        }

        column.Children.Add(wrap);
        if (blocked.Count == 0) column.Children.Add(new TextBlock { Text = "Nothing is blocked.", FontSize = 13, Foreground = Ui.Res("Text3Brush") });
    }
}

internal sealed class PlayerPane(SettingsModel settings, ArcTV.Core.Storage.LocalSettings local) : ModelPane(settings)
{
    private ArcTV.Core.Sources.TorrentBuffer _buffer = ArcTV.Core.Sources.TorrentBuffers.From(local.TorrentBuffer);
    private int _storage = local.TorrentStorageGb;

    protected override void Build(StackPanel column)
    {
        column.Children.Add(Ui.Card(new TextBlock
        {
            Text = "Video plays in Arc TV's built-in player (mpv).", FontSize = 14, TextWrapping = TextWrapping.Wrap,
        }));
        column.Children.Add(Ui.ToggleRow("Auto play next episode",
            "When an episode ends, the next one starts by itself with the best source that plays at once. Saved to your account; Arc TV for Windows follows it once next-episode playback is built.",
            Settings.Current?.AutoplayNextEpisode ?? true,
            on => _ = Settings.UpdateAsync(s => s with { AutoplayNextEpisode = on })));

        // torrents (kept on this PC, as on the Mac)
        column.Children.Add(Ui.Heading("Torrent engine"));
        var problem = ArcTV.App.Player.TorrentEngines.Shared.LibTorrentProblem;
        column.Children.Add(Ui.Note(problem is null
            ? "libtorrent is the engine the Mac app uses and is meant to find people and start faster. MonoTorrent is built into the app: switch to it if torrents misbehave. Applies to the next torrent you play."
            : $"libtorrent could not be loaded on this PC, so MonoTorrent is being used. ({problem})"));
        column.Children.Add(Ui.Choices([((string?)ArcTV.Core.Sources.TorrentEngineChoice.LibTorrent, "libtorrent (recommended)"), ((string?)ArcTV.Core.Sources.TorrentEngineChoice.MonoTorrent, "MonoTorrent")],
            ArcTV.Core.Sources.TorrentEngineChoice.PrefersLibTorrent(local.TorrentEngine) ? ArcTV.Core.Sources.TorrentEngineChoice.LibTorrent : ArcTV.Core.Sources.TorrentEngineChoice.MonoTorrent,
            code =>
            {
                local.TorrentEngine = code;
                Settings.Redraw();
            }));
        column.Children.Add(Ui.Heading("Torrent buffer"));
        column.Children.Add(Ui.Note("How much of the start of a torrent must arrive before the video starts. " + ArcTV.Core.Sources.TorrentBuffers.Detail(_buffer)));
        column.Children.Add(Ui.Choices([.. Enum.GetValues<ArcTV.Core.Sources.TorrentBuffer>().Select(b => ((string?)b.ToString(), b.ToString()))], _buffer.ToString(), code =>
        {
            _buffer = ArcTV.Core.Sources.TorrentBuffers.From(code);
            local.TorrentBuffer = _buffer.ToString();
            Settings.Redraw();
        }));
        column.Children.Add(Ui.Heading("Torrent storage limit"));
        column.Children.Add(Ui.Note("The most disk space a torrent may use while you watch. Its files are deleted when you stop. A bigger file than this won't start."));
        column.Children.Add(Ui.Choices([.. ArcTV.Core.Sources.TorrentStorage.Options.Select(g => ((string?)g.ToString(), ArcTV.Core.Sources.TorrentStorage.Label(g)))], _storage.ToString(), code =>
        {
            _storage = int.TryParse(code, out var gigabytes) ? gigabytes : ArcTV.Core.Sources.TorrentStorage.DefaultGigabytes;
            local.TorrentStorageGb = _storage;
        }));
    }
}

internal sealed class AudioPane(SettingsModel settings) : ModelPane(settings)
{
    protected override void Build(StackPanel column)
    {
        column.Children.Add(Ui.Heading("Preferred audio language", 0));
        column.Children.Add(Ui.Note("Used when a video has more than one audio track. Automatic leaves it to the video."));
        column.Children.Add(Ui.Choices([.. LanguageChoices.Audio().Select(l => (l.Code, l.Name))], Settings.Current?.DefaultAudioLanguage, code => _ = Settings.UpdateAsync(s => s with { DefaultAudioLanguage = code })));
    }
}

internal sealed class SubtitlesPane(SettingsModel settings, ArcTV.Core.Storage.LocalSettings local) : ModelPane(settings)
{
    private SubtitleSize _size = SubtitleStyle.SizeFrom(local.SubtitleSize);
    private SubtitleColor _color = SubtitleStyle.ColorFrom(local.SubtitleColor);

    protected override void Build(StackPanel column)
    {
        column.Children.Add(Ui.ToggleRow("Subtitles", "Start playback with subtitles on. You can still switch them in the player.",
            Settings.Current?.SubtitlesEnabled ?? false, on => _ = Settings.UpdateAsync(s => s with { SubtitlesEnabled = on })));

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        left.Children.Add(Ui.Heading("Size", 8));
        left.Children.Add(Ui.Choices([.. Enum.GetValues<SubtitleSize>().Select(s => ((string?)s.ToString(), s.Label()))], _size.ToString(), code =>
        {
            _size = SubtitleStyle.SizeFrom(code);
            local.SubtitleSize = _size.ToString();
            Redraw();
        }));
        left.Children.Add(Ui.Heading("Colour", 8));
        left.Children.Add(new TextBlock { Text = "Applies from the next video you start, on this PC.", FontSize = 12, Foreground = Ui.Res("Text2Brush"), Margin = new Thickness(0, 0, 0, 8) });
        left.Children.Add(Ui.Choices([.. Enum.GetValues<SubtitleColor>().Select(c => ((string?)c.ToString(), c.Label()))], _color.ToString(), code =>
        {
            _color = SubtitleStyle.ColorFrom(code);
            local.SubtitleColor = _color.ToString();
            Redraw();
        }));
        row.Children.Add(left);
        var preview = Preview();
        Grid.SetColumn(preview, 1);
        row.Children.Add(preview);
        column.Children.Add(row);

        column.Children.Add(Ui.Heading("Default language", 8));
        column.Children.Add(Ui.Choices([.. LanguageChoices.Subtitles().Select(l => (l.Code, l.Name))], Settings.Current?.DefaultSubtitleLanguage, code => _ = Settings.UpdateAsync(s => s with { DefaultSubtitleLanguage = code })));
    }

    /// <summary>The page is built again from the model; a size or colour is local, so the page is told to redraw itself.</summary>
    private void Redraw() => Settings.Redraw();

    /// <summary>A 16:9 frame with a sample subtitle at the chosen size and colour. The text is the same share of the frame's height the player draws it at.</summary>
    private Border Preview()
    {
        const double width = 320;
        const double height = width * 9 / 16;
        var (r, g, b) = _color.Rgb();
        var frame = new Grid { Width = width, Height = height, ClipToBounds = true };
        frame.Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x2A, 0x3F), Color.FromRgb(0x0A, 0x0C, 0x14), 90);
        frame.Children.Add(new TextBlock
        {
            Text = "I told you we'd make it\nbefore the sun goes down.", FontSize = height * _size.Fraction(), FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(r, g, b)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, ShadowDepth = 1, BlurRadius = 2, Opacity = 1 },
        });
        return new Border { Child = frame, CornerRadius = new CornerRadius(10), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
    }
}

internal sealed class UpdatesPane(SettingsModel settings) : ModelPane(settings)
{
    private enum Check { Idle, Checking, Failed, UpToDate, Available }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private Check _state = Check.Idle;
    private ReleaseInfo? _release;
    private bool _started;

    protected override void Build(StackPanel column)
    {
        var rows = new StackPanel();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = "Version", FontSize = 14, Foreground = Ui.Res("Text2Brush") });
        var version = new TextBlock { Text = AppInfo.Version, FontSize = 14 };
        Grid.SetColumn(version, 1);
        grid.Children.Add(version);
        rows.Children.Add(grid);
        rows.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 10, 0, 10) });
        rows.Children.Add(new TextBlock
        {
            FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Foreground = _state == Check.Available ? Ui.Res("AccentBrush") : Ui.Res("Text2Brush"),
            FontWeight = _state == Check.Available ? FontWeights.SemiBold : FontWeights.Normal,
            Text = _state switch
            {
                Check.Idle or Check.Checking => "Checking for updates…",
                Check.Failed => "Couldn't check for updates. Check your connection and try again.",
                Check.UpToDate => "You're up to date.",
                _ => $"Version {_release?.Version} is available.",
            },
        });
        column.Children.Add(Ui.Card(rows, 18));

        if (_state == Check.Available && _release is { } info)
        {
            var notes = new StackPanel();
            notes.Children.Add(new TextBlock { Text = $"What's new in {info.Version}", FontSize = 15, FontWeight = FontWeights.Bold });
            notes.Children.Add(new TextBlock { Text = info.Notes.Length == 0 ? "No notes for this version." : info.Notes, FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
            var links = new StackPanel { Orientation = Orientation.Horizontal };
            if (info.DownloadUrl is { } download) links.Children.Add(Ui.Act("Download", () => WebLinks.Open(download), true));
            if (info.PageUrl is { } page) links.Children.Add(Ui.Act("Release page", () => WebLinks.Open(page)));
            notes.Children.Add(links);
            column.Children.Add(Ui.Card(notes, 18));
        }

        var check = Ui.Act("Check for updates", () => _ = CheckAsync());
        check.IsEnabled = _state != Check.Checking;
        column.Children.Add(check);
        if (!_started)
        {
            _started = true;
            _ = CheckAsync();
        }
    }

    private async Task CheckAsync()
    {
        _state = Check.Checking;
        Settings.Redraw();
        try
        {
            var result = await UpdateCheck.CheckAsync(AppInfo.Version, Http);
            _release = result.Release;
            _state = result.Available ? Check.Available : Check.UpToDate;
        }
        catch (Exception)
        {
            _state = Check.Failed;
        }

        Settings.Redraw();
    }
}

internal sealed class ComingSoonPane : UserControl
{
    public ComingSoonPane(string name)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(new TextBlock { Text = $"{name} is coming soon to Arc TV for Windows.", FontSize = 15, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap });
        Content = panel;
    }
}
