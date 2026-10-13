using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ArcTV.App.Player;
using ArcTV.Core.Details;
using ArcTV.Core.Player;
using ArcTV.Core.Sources;

namespace ArcTV.App.Views;

/// <summary>
/// The player page: mpv draws the picture into a native window here, a transparent window over it (<see cref="PlayerOverlay"/>) holds the loading cover and the controls.
/// Full screen is the Arc TV window itself going without its frame.
/// </summary>
public partial class PlayerView : UserControl
{
    private readonly PlayingItem _item;
    private readonly Func<IVideoEngine> _makeEngine;
    private readonly Action _back;
    private IVideoEngine? _engine;
    private PlayerModel? _model;
    private readonly Action<PlayTarget, double, double>? _report;
    private readonly Action<PlayTarget>? _playNext;
    private readonly Func<bool>? _autoplayNext;
    private bool _leaving;
    private readonly Action<PlayTarget>? _chooseSource;
    private DispatcherTimer? _startWatch;
    private DispatcherTimer? _resizeDone;
    private bool _resizing;
    private readonly Func<Core.Details.MetaDetail, Task<string?>>? _logoLookup;
    private DispatcherTimer? _reportTimer;
    private PlayerOverlay? _overlay;
    private Window? _window;
    private DispatcherTimer? _waitForVideoWindow;
    private (WindowStyle Style, WindowState State, ResizeMode Resize, bool Topmost)? _beforeFullscreen;

    public PlayerView(PlayingItem item, Func<IVideoEngine> makeEngine, Action back, Action<PlayTarget, double, double>? report = null, Action<PlayTarget>? playNext = null, Func<bool>? autoplayNext = null, Func<Core.Details.MetaDetail, Task<string?>>? logoLookup = null, Action<PlayTarget>? chooseSource = null)
    {
        _logoLookup = logoLookup;
        _chooseSource = chooseSource;
        _report = report;
        _playNext = playNext;
        _autoplayNext = autoplayNext;
        InitializeComponent();
        _item = item;
        _makeEngine = makeEngine;
        _back = back;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LayoutUpdated += (_, _) => PlaceOverlay();
    }

    /// <summary>What the player is showing (for the tests).</summary>
    public PlayerModel? Model => _model;

    public bool IsFullscreen => _beforeFullscreen is not null;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_engine is not null) return;
        _window = Window.GetWindow(this);

        var engine = _engine = _makeEngine();
        var model = _model = new PlayerModel(engine) { RuntimeMinutes = Core.Details.Format.RuntimeMinutes(_item.Target.Title.Runtime) };
        engine.Position += model.OnPosition;
        engine.Duration += model.OnDuration;
        engine.Paused += model.OnPaused;
        engine.Buffering += model.OnBuffering;
        engine.Seeking += model.OnSeeking;
        engine.Started += () => model.OnBuffering(false);
        engine.Ended += () =>
        {
            if (model.Placeholder || _leaving) return;
            // the video finished: carry on with the next episode when Auto Play Next Episode is on, else go back
            if (NextEpisode() is not null && _autoplayNext?.Invoke() != false) PlayNext();
            else _back();
        };
        engine.Failed += model.OnFailed;

        _overlay = new PlayerOverlay(model, _item, _back, ToggleFullscreen, () => IsFullscreen, HandleKey, NextEpisode(), _playNext is null ? null : (Action)PlayNext, _logoLookup, _chooseSource is null ? null : (Action)ChooseAnotherSource) { Owner = _window };
        _overlay.Show();
        PlaceOverlay();
        if (_window is not null)
        {
            _window.PreviewKeyDown += OnWindowKey;
            _window.SizeChanged += OnWindowSized;
            _window.LocationChanged += OnWindowMoved;
            _window.Closed += OnWindowClosed; // the page is not always told it is unloaded when its whole window closes, and the overlay is a window of its own
        }

        // the video window is made when this page is first laid out; start as soon as it exists
        _waitForVideoWindow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        var tries = 0;
        _waitForVideoWindow.Tick += (_, _) =>
        {
            if (Video.WindowHandle == IntPtr.Zero && ++tries < 100) return;
            _waitForVideoWindow?.Stop();
            if (Video.WindowHandle == IntPtr.Zero) model.OnFailed("The video window could not be made.");
            else engine.Start(Video.WindowHandle, _item.Url, _item.Target.StartAt);
        };
        _waitForVideoWindow.Start();

        // tell the account where playback is every half minute (and once more on leaving), so Continue Watching and Resume know
        _reportTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _reportTimer.Tick += (_, _) => ReportProgress();
        _reportTimer.Start();

        // a source that never starts must not leave the loading symbol turning for ever: after a while the player says so and offers another source
        _startWatch = new DispatcherTimer { Interval = StartTimeout };
        _startWatch.Tick += (_, _) =>
        {
            _startWatch?.Stop();
            if (_model is { HasStarted: false, Failure: null } waiting) waiting.OnFailed("This source isn't starting. It may be slow, or the link may have stopped working. Try another source.");
        };
        _startWatch.Start();
    }

    /// <summary>How long a source gets to show its first picture.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Leaves the player for this title's source list (from where it is now), so a source that hangs or is not wanted can be swapped.</summary>
    private void ChooseAnotherSource()
    {
        if (_leaving || _chooseSource is null) return;
        _leaving = true;
        var here = _model is { HasStarted: true, Position: > 10 } model ? model.Position : _item.Target.StartAt;
        _chooseSource(_item.Target with { StartAt = here, ChooseSource = true, AutoPick = false });
    }

    /// <summary>The episode after this one (a show's episode only; null for a movie or the last episode).</summary>
    private EpisodeInfo? NextEpisode() =>
        _playNext is not null && _item.Target is { Season: { } season, Episode: { } episode } ? _item.Target.Title.EpisodeAfter(season, episode) : null;

    /// <summary>Starts the next episode: its source is picked without asking, and this player gives way to it.</summary>
    private void PlayNext()
    {
        if (_leaving || NextEpisode() is not { } next) return;
        _leaving = true;
        var title = _item.Target.Title;
        _playNext?.Invoke(new PlayTarget(title, next.Season, next.Episode, $"S{next.Season ?? 0} E{next.Episode ?? 0} {next.DisplayTitle}", AutoPick: true));
    }

    private void ReportProgress()
    {
        if (_report is not null && _model is { Duration: > 0 } model) _report(_item.Target, model.Position, model.Duration);
    }

    private void OnWindowClosed(object? sender, EventArgs e) => OnUnloaded(this, new RoutedEventArgs());

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _waitForVideoWindow?.Stop();
        _resizeDone?.Stop();
        _startWatch?.Stop();
        _reportTimer?.Stop();
        ReportProgress();
        if (_window is not null)
        {
            _window.PreviewKeyDown -= OnWindowKey;
            _window.SizeChanged -= OnWindowSized;
            _window.LocationChanged -= OnWindowMoved;
            _window.Closed -= OnWindowClosed;
        }

        if (IsFullscreen) ToggleFullscreen();
        _overlay?.Close();
        _overlay = null;
        _model?.Stop();
        _engine = null;
        _model = null;
    }

    // ---- the overlay follows the picture ----

    /// <summary>
    /// While the window is being dragged to a new size the controls step aside: a separate transparent window chasing the picture through every size it passes is what made the whole
    /// player stutter and shake. They fade back in, laid over the picture once, a moment after the resizing stops.
    /// </summary>
    private void OnWindowSized(object? sender, SizeChangedEventArgs e)
    {
        if (_overlay is null) return;
        _resizing = true;
        _overlay.Opacity = 0;
        if (_resizeDone is null)
        {
            _resizeDone = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _resizeDone.Tick += (_, _) =>
            {
                _resizeDone?.Stop();
                _resizing = false;
                PlaceOverlay();
                if (_overlay is not null) _overlay.Opacity = 1;
            };
        }

        _resizeDone.Stop();
        _resizeDone.Start();
    }

    /// <summary>Moving the window (without changing its size) moves the controls with it.</summary>
    private void OnWindowMoved(object? sender, EventArgs e) => PlaceOverlay();

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    /// <summary>
    /// Lays the overlay exactly over the picture. It is done in screen pixels, from where the video's own window really is: working it out in device-independent units went wrong
    /// when the window and the overlay were on screens of different scaling, and left the controls part of the way up the picture instead of at its bottom.
    /// </summary>
    private void PlaceOverlay()
    {
        if (_resizing || _overlay is null || !_overlay.IsVisible || ActualWidth <= 0 || ActualHeight <= 0) return;
        var overlay = new WindowInteropHelper(_overlay).Handle;
        if (overlay == IntPtr.Zero) return;

        int left, top, width, height;
        if (Video.WindowHandle != IntPtr.Zero && GetWindowRect(Video.WindowHandle, out var video) && video.Right > video.Left && video.Bottom > video.Top)
        {
            (left, top, width, height) = (video.Left, video.Top, video.Right - video.Left, video.Bottom - video.Top);
        }
        else
        {
            var from = PointToScreen(new Point(0, 0));
            var to = PointToScreen(new Point(ActualWidth, ActualHeight));
            (left, top, width, height) = ((int)Math.Round(from.X), (int)Math.Round(from.Y), (int)Math.Round(to.X - from.X), (int)Math.Round(to.Y - from.Y));
        }

        if (GetWindowRect(overlay, out var now) && now.Left == left && now.Top == top && now.Right - now.Left == width && now.Bottom - now.Top == height) return;
        SetWindowPos(overlay, IntPtr.Zero, left, top, width, height, NoZOrder | NoActivate);
    }

    // ---- full screen ----

    public void ToggleFullscreen()
    {
        if (_window is null) return;
        if (_beforeFullscreen is { } before)
        {
            _window.Topmost = before.Topmost;
            _window.ResizeMode = before.Resize;
            _window.WindowStyle = before.Style;
            _window.WindowState = before.State;
            _beforeFullscreen = null;
        }
        else
        {
            _beforeFullscreen = (_window.WindowStyle, _window.WindowState, _window.ResizeMode, _window.Topmost);
            // no frame and no resizing, then maximised: the window fills the whole screen, the task bar included
            _window.WindowState = WindowState.Normal;
            _window.WindowStyle = WindowStyle.None;
            _window.ResizeMode = ResizeMode.NoResize;
            _window.Topmost = true;
            _window.WindowState = WindowState.Maximized;
        }

        _overlay?.Refresh();
    }

    // ---- the keyboard ----

    private void OnWindowKey(object sender, KeyEventArgs e)
    {
        if (HandleKey(e.Key)) e.Handled = true;
    }

    /// <summary>Space pauses; Left and Right jump 10 seconds; Up and Down change the volume; M mutes; F goes full screen; Esc leaves full screen, then the player.</summary>
    public bool HandleKey(Key key)
    {
        if (_model is not { } model) return false;
        switch (key)
        {
            case Key.Space:
                model.TogglePause();
                return true;
            case Key.Left:
                model.Skip(-10);
                return true;
            case Key.Right:
                model.Skip(10);
                return true;
            case Key.Up:
                model.SetVolume(model.Volume + 5);
                _overlay?.ShowVolume();
                return true;
            case Key.Down:
                model.SetVolume(model.Volume - 5);
                _overlay?.ShowVolume();
                return true;
            case Key.M:
                model.SetVolume(model.Volume > 0 ? 0 : 100);
                _overlay?.ShowVolume();
                return true;
            case Key.F:
                ToggleFullscreen();
                return true;
            case Key.Escape:
                if (IsFullscreen) ToggleFullscreen();
                else _back();
                return true;
            default:
                return false;
        }
    }
}
