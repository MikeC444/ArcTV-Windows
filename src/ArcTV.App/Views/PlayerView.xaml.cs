using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArcTV.App.Player;
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
    private PlayerOverlay? _overlay;
    private Window? _window;
    private DispatcherTimer? _waitForVideoWindow;
    private (WindowStyle Style, WindowState State, ResizeMode Resize, bool Topmost)? _beforeFullscreen;

    public PlayerView(PlayingItem item, Func<IVideoEngine> makeEngine, Action back)
    {
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
            if (!model.Placeholder) _back();
        };
        engine.Failed += model.OnFailed;

        _overlay = new PlayerOverlay(model, _item, _back, ToggleFullscreen, () => IsFullscreen, HandleKey) { Owner = _window };
        _overlay.Show();
        PlaceOverlay();
        if (_window is not null)
        {
            _window.PreviewKeyDown += OnWindowKey;
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
    }

    private void OnWindowClosed(object? sender, EventArgs e) => OnUnloaded(this, new RoutedEventArgs());

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _waitForVideoWindow?.Stop();
        if (_window is not null)
        {
            _window.PreviewKeyDown -= OnWindowKey;
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

    private void PlaceOverlay()
    {
        if (_overlay is null || !_overlay.IsVisible || ActualWidth <= 0 || ActualHeight <= 0 || PresentationSource.FromVisual(this) is not { } source) return;
        var topLeft = source.CompositionTarget.TransformFromDevice.Transform(PointToScreen(new Point(0, 0)));
        _overlay.Left = topLeft.X;
        _overlay.Top = topLeft.Y;
        _overlay.Width = ActualWidth;
        _overlay.Height = ActualHeight;
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
