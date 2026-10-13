using System.Globalization;
using System.Text.Json;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Player;

/// <summary>An audio or subtitle track in the file being played.</summary>
public sealed record MediaTrack(int Id, string Type, string Title, string? Language, bool Selected)
{
    public string Label
    {
        get
        {
            var name = string.Join(" · ", new[] { string.IsNullOrEmpty(Title) ? null : Title, Language?.ToUpperInvariant() }.Where(part => !string.IsNullOrEmpty(part)));
            return name.Length == 0 ? $"Track {Id}" : name;
        }
    }
}

/// <summary>What the player engine can be told to do (mpv on Windows; a stand-in in tests).</summary>
public interface IPlayerCommands
{
    void SetPause(bool paused);

    void SeekTo(double seconds);

    void SeekBy(double seconds);

    /// <summary>0 to 100 (more is louder than the file).</summary>
    void SetVolume(double volume);

    void SetSpeed(double speed);

    /// <summary>The audio track to use, or null for none.</summary>
    void SetAudioTrack(int? id);

    /// <summary>The subtitle track to show, or null for none.</summary>
    void SetSubtitleTrack(int? id);

    /// <summary>A property of what is playing as text (the track list is JSON), or null.</summary>
    string? GetString(string property);

    void Shutdown();
}

/// <summary>
/// A "not ready yet" slate: some debrid addons answer a title that isn't cached yet with a short video that says so, and it plays like any other video. Recognised by being
/// far shorter than the title.
/// </summary>
public static class PlaceholderVideo
{
    /// <summary>A "video" shorter than this, for a title that runs much longer, is not the title.</summary>
    public const double MaxSeconds = 120;

    public const string Message = "This source isn't ready yet. Its debrid service may still be preparing the file. Try again in a few minutes, or choose another source.";

    /// <summary>
    /// A film or episode with a known runtime of at least 10 minutes that comes back as under two minutes, and under a tenth of that runtime, is a slate. Without a known runtime
    /// nothing is assumed, so a genuinely short film is never turned away.
    /// </summary>
    public static bool IsPlaceholder(double durationSeconds, int? runtimeMinutes) =>
        runtimeMinutes is >= 10 && double.IsFinite(durationSeconds) && durationSeconds > 0 && durationSeconds < MaxSeconds && durationSeconds < runtimeMinutes.Value * 60 / 10.0;
}

/// <summary>
/// What the player screen shows and does: the playing position, pause, volume, tracks. The engine reports in (the On... methods), the controls send commands out.
/// Call it from the UI thread.
/// </summary>
public sealed class PlayerModel(IPlayerCommands commands) : ObservableObject
{
    private double _position;
    private double _duration;
    private bool _paused;
    private bool _buffering = true;
    private bool _hasStarted;
    private bool _seeking;
    private string? _failure;
    private bool _ended;
    private bool _placeholder;
    private double _volume = 100;
    private double _speed = 1;
    private int? _runtimeMinutes;
    private IReadOnlyList<MediaTrack> _audioTracks = [];
    private IReadOnlyList<MediaTrack> _subtitleTracks = [];
    private DateTime _seekDeadline = DateTime.MaxValue;

    public double Position { get => _position; private set => Set(ref _position, value); }

    public double Duration { get => _duration; private set => Set(ref _duration, value); }

    public bool Paused { get => _paused; private set => Set(ref _paused, value); }

    public bool Buffering { get => _buffering; private set => Set(ref _buffering, value); }

    /// <summary>False until the first picture is playing: the screen shows the title's artwork and logo until then.</summary>
    public bool HasStarted { get => _hasStarted; private set => Set(ref _hasStarted, value); }

    /// <summary>True from the moment a jump is asked for until the player has landed on the new spot; shows the spinner while the stream loads.</summary>
    public bool Seeking { get => _seeking; private set => Set(ref _seeking, value); }

    public string? Failure { get => _failure; private set => Set(ref _failure, value); }

    public bool Ended { get => _ended; private set => Set(ref _ended, value); }

    /// <summary>True when the source turned out to be a "not ready yet" slate: nothing it plays is saved, and its end is not "finished".</summary>
    public bool Placeholder { get => _placeholder; private set => Set(ref _placeholder, value); }

    public double Volume { get => _volume; private set => Set(ref _volume, value); }

    public double Speed { get => _speed; private set => Set(ref _speed, value); }

    public IReadOnlyList<MediaTrack> AudioTracks { get => _audioTracks; private set => Set(ref _audioTracks, value); }

    public IReadOnlyList<MediaTrack> SubtitleTracks { get => _subtitleTracks; private set => Set(ref _subtitleTracks, value); }

    /// <summary>The title's runtime, set by the screen, to recognise such a slate once the length is known.</summary>
    public int? RuntimeMinutes
    {
        get => _runtimeMinutes;
        set
        {
            _runtimeMinutes = value;
            CheckPlaceholder();
        }
    }

    /// <summary>True while something is loading and the spinner should turn: buffering or jumping, and nothing has gone wrong.</summary>
    public bool Loading => (Buffering || Seeking) && Failure is null;

    // ---- what the engine reports ----

    public void OnPosition(double seconds)
    {
        // the engine reports the position with every frame; redrawing the controls that often is what makes a player feel slow
        if (!HasStarted || Math.Abs(seconds - Position) >= 0.25) Position = seconds;
        if (HasStarted) return;
        HasStarted = true;
        Buffering = false;
        commands.SetVolume(Volume);
        if (Math.Abs(Speed - 1) > 0.001) commands.SetSpeed(Speed);
        RefreshTracks();
        Raise(nameof(Loading));
    }

    public void OnDuration(double seconds)
    {
        Duration = seconds;
        CheckPlaceholder();
    }

    public void OnPaused(bool paused) => Paused = paused;

    public void OnBuffering(bool buffering)
    {
        Buffering = buffering;
        Raise(nameof(Loading));
    }

    public void OnSeeking(bool seeking)
    {
        Seeking = seeking;
        _seekDeadline = DateTime.MaxValue;
        Raise(nameof(Loading));
    }

    public void OnEnded() => Ended = true;

    public void OnFailed(string message)
    {
        Failure = message;
        Raise(nameof(Loading));
    }

    /// <summary>Call now and then: a jump the engine never reports as finished stops showing the spinner after a few seconds.</summary>
    public void Tick(DateTime now)
    {
        if (Seeking && now >= _seekDeadline) OnSeeking(false);
    }

    /// <summary>A source far shorter than its title is a slate: stop it and say the source isn't ready, instead of playing it as the film.</summary>
    private void CheckPlaceholder()
    {
        if (Placeholder || !PlaceholderVideo.IsPlaceholder(Duration, RuntimeMinutes)) return;
        Placeholder = true;
        commands.SetPause(true);
        OnFailed(PlaceholderVideo.Message);
    }

    // ---- what the controls do ----

    public void TogglePause() => commands.SetPause(!Paused);

    private void MarkSeeking()
    {
        Seeking = true;
        _seekDeadline = DateTime.UtcNow.AddSeconds(8);
        Raise(nameof(Loading));
    }

    public void Seek(double seconds)
    {
        MarkSeeking();
        commands.SeekTo(Math.Max(0, Duration > 0 ? Math.Min(seconds, Duration) : seconds));
    }

    public void Skip(double seconds)
    {
        MarkSeeking();
        commands.SeekBy(seconds);
    }

    public void SetVolume(double volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        commands.SetVolume(Volume);
    }

    public void SetSpeed(double speed)
    {
        Speed = speed;
        commands.SetSpeed(speed);
    }

    public void SelectAudio(MediaTrack? track)
    {
        commands.SetAudioTrack(track?.Id);
        RefreshTracks();
    }

    public void SelectSubtitle(MediaTrack? track)
    {
        commands.SetSubtitleTrack(track?.Id);
        RefreshTracks();
    }

    /// <summary>The audio and subtitle tracks in the player's track list (a JSON array).</summary>
    public static (List<MediaTrack> Audio, List<MediaTrack> Subtitles) ParseTracks(string? json)
    {
        var audio = new List<MediaTrack>();
        var subtitles = new List<MediaTrack>();
        if (string.IsNullOrEmpty(json)) return (audio, subtitles);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (audio, subtitles);
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !entry.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) continue;
                string? Text(string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                var track = new MediaTrack(id.GetInt32(), type.GetString()!, Text("title") ?? "", Text("lang"), entry.TryGetProperty("selected", out var selected) && selected.ValueKind == JsonValueKind.True);
                if (track.Type == "audio") audio.Add(track);
                else if (track.Type == "sub") subtitles.Add(track);
            }
        }
        catch (JsonException)
        {
            // an unreadable list is no tracks
        }

        return (audio, subtitles);
    }

    public void RefreshTracks()
    {
        var (audio, subtitles) = ParseTracks(commands.GetString("track-list"));
        AudioTracks = audio;
        SubtitleTracks = subtitles;
    }

    public void Stop() => commands.Shutdown();

    /// <summary>"1:05:09" or "5:09" (and "--:--" when the time is not known).</summary>
    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "--:--";
        var total = (int)seconds;
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;
        return hours > 0 ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{secs:00}") : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{secs:00}");
    }
}
