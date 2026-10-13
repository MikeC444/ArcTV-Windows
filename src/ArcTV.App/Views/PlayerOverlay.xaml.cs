using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ArcTV.App.Controls;
using ArcTV.Core.Details;
using ArcTV.Core.Player;
using ArcTV.Core.Sources;

namespace ArcTV.App.Views;

/// <summary>
/// The player's face: a transparent window laid exactly over the video (WPF cannot draw over a video window inside its own window). It holds the loading cover, the title's logo at
/// the top right, the controls (laid out like the Mac app's: back and the 10-second jumps top left, play / pause, the timeline and the time along the bottom, and a row of pills), and the lists.
/// </summary>
public partial class PlayerOverlay : Window
{
    private static readonly double[] Speeds = [0.5, 0.75, 1, 1.25, 1.5, 2];

    private readonly PlayerModel _model;
    private readonly Action _back;
    private readonly Action _toggleFullscreen;
    private readonly Func<bool> _isFullscreen;
    private readonly Func<Key, bool> _handleKey;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _controlsShown = true;
    private bool _menuOpen;
    private double _volumeBeforeMute = 100;
    private readonly EpisodeInfo? _next;
    private bool? _flashedPaused;
    private readonly Action? _playNext;

    public PlayerOverlay(PlayerModel model, PlayingItem item, Action back, Action toggleFullscreen, Func<bool> isFullscreen, Func<Key, bool> handleKey, EpisodeInfo? next = null, Action? playNext = null)
    {
        InitializeComponent();
        _next = next;
        _playNext = playNext;
        if (next is not null) NextButton.Tag = $"S{next.Season ?? 0} E{next.Episode ?? 0} \u2022 {next.DisplayTitle}";
        _model = model;
        _back = back;
        _toggleFullscreen = toggleFullscreen;
        _isFullscreen = isFullscreen;
        _handleKey = handleKey;

        var title = item.Target.Title;
        CoverPicture.Url = Artwork.Bigger(title.Background ?? title.Poster);
        CoverPicture.FallbackUrl = title.Background ?? title.Poster;
        CoverLogo.Url = title.Logo;
        CoverName.Text = title.Name;
        TitleLogo.Url = title.Logo;
        TitleName.Text = title.Name;
        if (item.Target.EpisodeTitle is { } episode)
        {
            EpisodeText.Text = episode;
            EpisodeText.Visibility = Visibility.Visible;
        }

        Bar.Scrubbed += seconds => _model.Seek(seconds);
        VolumeBar.Total = 100;
        VolumeBar.Value = _model.Volume;
        VolumeBar.Scrubbing += volume => _model.SetVolume(volume);
        VolumeBar.Scrubbed += volume => _model.SetVolume(volume);

        PreviewKeyDown += (_, e) =>
        {
            if (_handleKey(e.Key)) e.Handled = true;
            Activity();
        };
        _model.PropertyChanged += OnModelChanged;
        _timer.Tick += (_, _) =>
        {
            _model.Tick(DateTime.UtcNow);
            if (_controlsShown && !_menuOpen && !_model.Paused && DateTime.UtcNow - _lastActivity > TimeSpan.FromSeconds(3)) SetControls(false);
        };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _model.PropertyChanged -= OnModelChanged;
        };
        Refresh();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    public void Refresh()
    {
        var started = _model.HasStarted;
        if (started && Cover.Visibility == Visibility.Visible)
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350));
            fade.Completed += (_, _) => Cover.Visibility = Visibility.Collapsed;
            Cover.BeginAnimation(OpacityProperty, fade);
        }

        TitleEnd.Visibility = started ? Visibility.Visible : Visibility.Collapsed;
        Busy.Visibility = started && _model.Loading ? Visibility.Visible : Visibility.Collapsed;

        // pausing or playing from now on (not the start of the video) shows its symbol in the middle for a couple of seconds
        if (started)
        {
            if (_flashedPaused is { } before && before != _model.Paused) ShowFlash(_model.Paused);
            _flashedPaused = _model.Paused;
        }

        PlayGlyph.Text = _model.Paused ? "" : "";
        PlayedText.Text = PlayerModel.Clock(_model.Position);
        LeftText.Text = _model.Duration > 0 ? "−" + PlayerModel.Clock(Math.Max(0, _model.Duration - _model.Position)) : "";
        Bar.Total = _model.Duration;
        Bar.Value = _model.Position;
        MuteGlyph.Text = _model.Volume <= 0 ? "" : "";
        FullscreenGlyph.Text = _isFullscreen() ? "" : "";
        SpeedButton.Content = $"Speed {_model.Speed:0.##}x";

        NextButton.Visibility = _next is not null && _playNext is not null && _model.Duration > 120 && _model.Duration - _model.Position <= 60 ? Visibility.Visible : Visibility.Collapsed;

        FailurePanel.Visibility = _model.Failure is null ? Visibility.Collapsed : Visibility.Visible;
        FailureText.Text = _model.Failure ?? "";
        if (_model.Failure is not null) SetControls(true);
    }

    /// <summary>The pause symbol after pausing, the play symbol after playing: it shows at once, stays about two seconds and fades.</summary>
    private void ShowFlash(bool paused)
    {
        FlashGlyph.Text = paused ? "\uE769" : "\uE768";
        var animation = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(80)));
        animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(2000)));
        animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(2500)));
        Flash.BeginAnimation(OpacityProperty, animation);
    }

    // ---- showing and hiding the controls ----

    private void Activity()
    {
        _lastActivity = DateTime.UtcNow;
        if (!_controlsShown) SetControls(true);
    }

    private void SetControls(bool shown)
    {
        _controlsShown = shown;
        ControlLayer.IsHitTestVisible = shown;
        ControlLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(shown ? 1 : 0, TimeSpan.FromMilliseconds(shown ? 120 : 300)));
        Root.Cursor = shown ? Cursors.Arrow : Cursors.None;
    }

    private void OnMouseMove(object sender, MouseEventArgs e) => Activity();

    /// <summary>A click on the picture itself pauses or plays; a double click goes full screen.</summary>
    private void OnVideoClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) _toggleFullscreen();
        else if (_model.Failure is null) _model.TogglePause();
    }

    // ---- the buttons ----

    private void OnBack(object sender, RoutedEventArgs e) => _back();

    private void OnNextEpisode(object sender, RoutedEventArgs e) => _playNext?.Invoke();

    private void OnBack10(object sender, RoutedEventArgs e) => _model.Skip(-10);

    private void OnForward10(object sender, RoutedEventArgs e) => _model.Skip(10);

    private void OnPlayPause(object sender, RoutedEventArgs e) => _model.TogglePause();

    private void OnFullscreen(object sender, RoutedEventArgs e)
    {
        _toggleFullscreen();
        Refresh();
    }

    private void OnMute(object sender, RoutedEventArgs e)
    {
        if (_model.Volume > 0)
        {
            _volumeBeforeMute = _model.Volume;
            _model.SetVolume(0);
        }
        else
        {
            _model.SetVolume(_volumeBeforeMute > 0 ? _volumeBeforeMute : 100);
        }

        VolumeBar.Value = _model.Volume;
    }

    /// <summary>Called by the player for the arrow keys: volume moves on the bar too.</summary>
    public void ShowVolume() => VolumeBar.Value = _model.Volume;

    // ---- the lists ----

    private void OnAudio(object sender, RoutedEventArgs e)
    {
        _model.RefreshTracks();
        Open(AudioButton, _model.AudioTracks.Select(track => (track.Label, track.Selected, (Action)(() => _model.SelectAudio(track)))).ToList(), "No other audio tracks");
    }

    private void OnSubtitles(object sender, RoutedEventArgs e)
    {
        _model.RefreshTracks();
        var rows = new List<(string, bool, Action)> { ("Off", !_model.SubtitleTracks.Any(t => t.Selected), () => _model.SelectSubtitle(null)) };
        rows.AddRange(_model.SubtitleTracks.Select(track => (track.Label, track.Selected, (Action)(() => _model.SelectSubtitle(track)))));
        Open(SubtitlesButton, rows, "No subtitles in this video");
    }

    private void OnSpeed(object sender, RoutedEventArgs e) =>
        Open(SpeedButton, Speeds.Select(speed => ($"{speed:0.##}x", Math.Abs(speed - _model.Speed) < 0.001, (Action)(() => _model.SetSpeed(speed)))).ToList(), "");

    private void Open(Button anchor, IReadOnlyList<(string Text, bool Selected, Action Pick)> rows, string emptyText)
    {
        MenuRows.Children.Clear();
        if (rows.Count == 0 || (rows.Count == 1 && rows[0].Text == "Off" && emptyText.Length > 0 && !_model.SubtitleTracks.Any()))
        {
            MenuRows.Children.Add(new TextBlock { Text = emptyText.Length > 0 ? emptyText : "Nothing to choose", Margin = new Thickness(12, 8, 12, 8), Foreground = (Brush)FindResource("Text2Brush") });
        }
        else
        {
            foreach (var row in rows)
            {
                var line = new Grid();
                line.Children.Add(new TextBlock { Text = row.Text, FontSize = 14, FontWeight = row.Selected ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 28, 0) });
                if (row.Selected) line.Children.Add(new TextBlock { Text = "✓", FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
                var button = new Button { Style = (Style)FindResource("SuggestionRow"), Content = line, MinWidth = 220, Focusable = false };
                button.Click += (_, _) =>
                {
                    MenuPopup.IsOpen = false;
                    row.Pick();
                };
                MenuRows.Children.Add(button);
            }
        }

        MenuPopup.PlacementTarget = anchor;
        _menuOpen = true;
        MenuPopup.IsOpen = true;
    }

    private void OnMenuClosed(object? sender, EventArgs e)
    {
        _menuOpen = false;
        Activity();
    }

    // ---- the cover and the logo ----

    /// <summary>The logo shows instead of the name (a title with no logo, or one that never loads, keeps its words).</summary>
    private void OnCoverLogoShown(object? sender, EventArgs e) => CoverName.Visibility = Visibility.Collapsed;

    private void OnTitleLogoShown(object? sender, EventArgs e) => TitleName.Visibility = Visibility.Collapsed;
}
