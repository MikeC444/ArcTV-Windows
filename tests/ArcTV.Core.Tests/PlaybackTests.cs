using ArcTV.Core.Addons;
using ArcTV.Core.Details;
using ArcTV.Core.Plus;
using ArcTV.Core.Sources;
using Xunit;

namespace ArcTV.Core.Tests;

public class PlaybackTests
{
    private static MetaDetail Show() => new("tt1", "series", "Show", Videos:
    [
        new("a", Season: 0, Episode: 1, Title: "Special"),
        new("b", Season: 1, Episode: 1), new("c", Season: 1, Episode: 2), new("d", Season: 1, Episode: 3),
        new("e", Season: 2, Episode: 1), new("f", Season: 2, Episode: 2),
    ]);

    private static HistoryItem Row(string id, int? season, int? episode, long position, long duration, bool completed) =>
        new("p", id, season is null ? "MOVIE" : "TV_SHOW", "T", position, duration, completed, "2026-10-01T00:00:00.000Z", season, episode);

    [Fact]
    public void TheEpisodeAfterIsTheNextOneThenTheNextSeasonAndSpecialsAreSkipped()
    {
        var show = Show();
        Assert.Equal("c", show.EpisodeAfter(1, 1)!.Id);
        Assert.Equal("e", show.EpisodeAfter(1, 3)!.Id);
        Assert.Null(show.EpisodeAfter(2, 2));
        Assert.Null(show.EpisodeAfter(0, 1)); // specials are not a season of the show
        Assert.Null(show.EpisodeAfter(9, 1));
        Assert.Null(new MetaDetail("tt2", "movie", "Film").EpisodeAfter(1, 1));
    }

    [Fact]
    public void StatusesReadFinishedAndPartWayEpisodesOfOneShowFromTheHistory()
    {
        var statuses = EpisodeProgress.Statuses(
        [
            Row("tt1", 1, 1, 2_700_000, 2_700_000, true),
            Row("tt1", 1, 2, 900_000, 3_600_000, false),
            Row("tt1", 1, 3, 0, 3_600_000, false),       // never started: no entry
            Row("tt9", 1, 1, 1000, 2000, true),          // another show
            Row("tt1", null, null, 5000, 9000, true),    // a movie row with the same id is not an episode
        ], "tt1");
        Assert.Equal(2, statuses.Count);
        Assert.Equal(new EpisodeStatus(true, 1), statuses["1:1"]);
        Assert.Equal(new EpisodeStatus(false, 0.25), statuses["1:2"]);
    }

    [Fact]
    public void NextEpisodeIsTheOneAfterTheFurthestWatchedAndNullWhenThereIsNone()
    {
        var show = Show();
        Dictionary<string, EpisodeStatus> Watched(params string[] keys) => keys.ToDictionary(k => k, _ => new EpisodeStatus(true, 1));
        Assert.Null(EpisodeProgress.NextEpisode(show.Seasons, Watched())); // nothing watched: the caller starts at the first
        Assert.Equal("c", EpisodeProgress.NextEpisode(show.Seasons, Watched("1:1"))!.Id);
        Assert.Equal("e", EpisodeProgress.NextEpisode(show.Seasons, Watched("1:1", "1:2", "1:3"))!.Id); // a season caught up moves on
        Assert.Equal("d", EpisodeProgress.NextEpisode(show.Seasons, Watched("1:1", "1:2"))!.Id);
        Assert.Null(EpisodeProgress.NextEpisode(show.Seasons, Watched("2:2"))); // the last episode is the furthest
        Assert.Equal("c", EpisodeProgress.NextEpisode(show.Seasons, new Dictionary<string, EpisodeStatus> { ["1:1"] = new(true, 1), ["1:2"] = new(false, 0.4) })!.Id);
    }

    [Fact]
    public void EntriesCarryEachEpisodesStatus()
    {
        var show = Show();
        var entries = EpisodeProgress.Entries(show.Seasons[1], new Dictionary<string, EpisodeStatus> { ["1:1"] = new(true, 1), ["1:2"] = new(false, 0.5) });
        Assert.True(entries[0].Watched);
        Assert.False(entries[0].InProgress);
        Assert.True(entries[1].InProgress);
        Assert.Equal(0.5, entries[1].Fraction);
        Assert.False(entries[2].Watched || entries[2].InProgress);
    }

    private static SourceItem Direct(string addon, string title, int index = 0) => new(addon, new AddonStream(Title: title, Url: "https://cdn.example/" + index + ".mkv"), index);

    private static SourceItem Item(string addon, string title) => new(addon, new AddonStream(Title: title), 9);

    private static SourceItem Torrent(string addon, string title, string hash, int index = 0) => new(addon, new AddonStream(Title: title, InfoHash: hash), index);

    [Fact]
    public void ASourceIsPickedWithoutAskingOnlyWhenItIsSafeToGuess()
    {
        var show = new MetaDetail("tt1", "series", "Show");
        var best = Direct("A", "Best 2160p", 0);
        var other = Direct("B", "Other 720p", 1);
        var torrent = Torrent("T", "Torrent 1080p", "abc", 2);
        IReadOnlyList<SourceItem> items = [best, other, torrent];
        // the next episode of a binge: the best that plays
        Assert.Same(best, AutoPick.Choose(new PlayTarget(show, 1, 2, AutoPick: true), items, false));
        // playing something watched before still shows the list: nothing is picked from what was used last time
        Assert.Null(AutoPick.Choose(new PlayTarget(show, 1, 2), items, false));
        // smart picking (a setting the person turns on): the best that plays
        Assert.Same(best, AutoPick.Choose(new PlayTarget(show, 1, 2), items, true));
        // asking for the list always gets it
        Assert.Null(AutoPick.Choose(new PlayTarget(show, 1, 2, ChooseSource: true), items, true));
        // a torrent is the best when it is all there is
        Assert.Same(torrent, AutoPick.Choose(new PlayTarget(show, 1, 2, AutoPick: true), [torrent], true));
        // nothing can play (no link and no torrent): show the list
        Assert.Null(AutoPick.Choose(new PlayTarget(show, 1, 2, AutoPick: true), [Item("N", "nothing")], true));
    }

    [Fact]
    public async Task TheSourcesModelStaysLoadingWhileItHandsOverToThePlayer()
    {
        var detail = new MetaDetail("tt1", "movie", "One");
        var picked = Direct("A", "One 1080p");
        SourceItem? heard = null;
        var model = new SourcesModel(new PlayTarget(detail), _ => Task.FromResult<IReadOnlyList<SyncedAddon>>([]), null, _ => picked);
        model.AutoChose += item => heard = item;
        await model.LoadAsync();
        Assert.Same(picked, heard);
        Assert.Same(picked, model.AutoChosen);
        Assert.True(model.Loading); // the list never flashes up before the video

        var asking = new SourcesModel(new PlayTarget(detail), _ => Task.FromResult<IReadOnlyList<SyncedAddon>>([]), null, _ => null);
        await asking.LoadAsync();
        Assert.Null(asking.AutoChosen);
        Assert.False(asking.Loading);
    }
}
