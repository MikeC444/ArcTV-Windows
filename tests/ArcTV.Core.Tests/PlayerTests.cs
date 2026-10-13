using ArcTV.Core.Player;
using Xunit;

namespace ArcTV.Core.Tests;

public class PlayerTests
{
    private sealed class FakeCommands : IPlayerCommands
    {
        public List<string> Calls { get; } = [];

        public string? TrackList { get; set; }

        public void SetPause(bool paused) => Calls.Add($"pause {paused}");

        public void SeekTo(double seconds) => Calls.Add($"seekto {seconds}");

        public void SeekBy(double seconds) => Calls.Add($"seekby {seconds}");

        public void SetVolume(double volume) => Calls.Add($"volume {volume}");

        public void SetSpeed(double speed) => Calls.Add($"speed {speed}");

        public void SetAudioTrack(int? id) => Calls.Add($"audio {id?.ToString() ?? "none"}");

        public void SetSubtitleTrack(int? id) => Calls.Add($"sub {id?.ToString() ?? "none"}");

        public string? GetString(string property) => property == "track-list" ? TrackList : null;

        public void Shutdown() => Calls.Add("shutdown");
    }

    [Fact]
    public void ClockReadsLikeAPlayer()
    {
        Assert.Equal("0:00", PlayerModel.Clock(0));
        Assert.Equal("5:09", PlayerModel.Clock(309));
        Assert.Equal("1:05:09", PlayerModel.Clock(3909));
        Assert.Equal("--:--", PlayerModel.Clock(double.NaN));
        Assert.Equal("--:--", PlayerModel.Clock(-1));
    }

    [Fact]
    public void ASlateIsFarShorterThanItsTitle()
    {
        Assert.True(PlaceholderVideo.IsPlaceholder(9, 47));
        Assert.True(PlaceholderVideo.IsPlaceholder(90, 120));
        Assert.False(PlaceholderVideo.IsPlaceholder(47 * 60, 47));
        Assert.False(PlaceholderVideo.IsPlaceholder(40 * 60, 47));
        Assert.False(PlaceholderVideo.IsPlaceholder(90, null)); // no known runtime: never assumed
        Assert.False(PlaceholderVideo.IsPlaceholder(90, 5));    // a short title is not turned away
    }

    [Fact]
    public void TracksAreReadFromTheTrackList()
    {
        var (audio, subs) = PlayerModel.ParseTracks("""
            [{"id":1,"type":"video"},{"id":1,"type":"audio","lang":"eng","title":"Surround","selected":true},{"id":2,"type":"audio","lang":"fra"},
             {"id":1,"type":"sub","lang":"eng","selected":false},{"id":2,"type":"sub","title":"Forced"}]
            """);
        Assert.Equal(2, audio.Count);
        Assert.Equal("Surround · ENG", audio[0].Label);
        Assert.True(audio[0].Selected);
        Assert.Equal("FRA", audio[1].Label);
        Assert.Equal(2, subs.Count);
        Assert.Equal("Forced", subs[1].Label);
        Assert.Equal("Track 7", new MediaTrack(7, "sub", "", null, false).Label);
        Assert.Empty(PlayerModel.ParseTracks("not json").Audio);
        Assert.Empty(PlayerModel.ParseTracks(null).Subtitles);
    }

    [Fact]
    public void TheFirstPositionStartsPlayingAndSetsTheVolume()
    {
        var commands = new FakeCommands { TrackList = """[{"id":1,"type":"audio","lang":"eng"}]""" };
        var model = new PlayerModel(commands);
        Assert.False(model.HasStarted);
        Assert.True(model.Loading);

        model.OnPosition(0.1);
        Assert.True(model.HasStarted);
        Assert.False(model.Loading);
        Assert.Contains("volume 100", commands.Calls);
        Assert.Single(model.AudioTracks);

        model.OnPosition(0.2); // under a quarter of a second on: not worth redrawing
        Assert.Equal(0.1, model.Position);
        model.OnPosition(1);
        Assert.Equal(1, model.Position);
    }

    [Fact]
    public void JumpingShowsTheSpinnerUntilTheEngineLandsOrAFewSecondsPass()
    {
        var commands = new FakeCommands();
        var model = new PlayerModel(commands);
        model.OnPosition(1);
        model.OnDuration(3600);

        model.Skip(10);
        Assert.True(model.Seeking);
        Assert.True(model.Loading);
        Assert.Contains("seekby 10", commands.Calls);

        model.OnSeeking(false);
        Assert.False(model.Loading);

        model.Seek(99999); // past the end: kept inside the video
        Assert.Contains("seekto 3600", commands.Calls);
        model.Tick(DateTime.UtcNow.AddSeconds(30)); // the engine never said it landed
        Assert.False(model.Seeking);
    }

    [Fact]
    public void ASlateIsStoppedAndExplained()
    {
        var commands = new FakeCommands();
        var model = new PlayerModel(commands) { RuntimeMinutes = 47 };
        model.OnDuration(9);
        Assert.True(model.Placeholder);
        Assert.Equal(PlaceholderVideo.Message, model.Failure);
        Assert.Contains("pause True", commands.Calls);
        Assert.False(model.Loading);
    }

    [Fact]
    public void ControlsSendCommands()
    {
        var commands = new FakeCommands();
        var model = new PlayerModel(commands);
        model.OnPaused(true);
        model.TogglePause();
        model.SetVolume(150);
        model.SetSpeed(1.5);
        model.SelectAudio(new MediaTrack(3, "audio", "", null, false));
        model.SelectSubtitle(null);
        model.Stop();
        Assert.Equal(new[] { "pause False", "volume 100", "speed 1.5", "audio 3", "sub none", "shutdown" }, commands.Calls);
        Assert.Equal(100, model.Volume); // never past 100
    }
}
