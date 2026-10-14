using ArcTV.Core.Addons;
using ArcTV.Core.Home;
using ArcTV.Core.Settings;
using ArcTV.Core.Storage;
using Xunit;

namespace ArcTV.Core.Tests;

public class SettingsTests
{
    private sealed class FakeStore(AccountSettings start) : IAccountSettingsStore
    {
        public AccountSettings Stored = start;
        public bool Fail;
        public int Saves;

        public Task<AccountSettings> GetAsync(CancellationToken cancel = default) => Fail ? throw new HttpRequestException("down") : Task.FromResult(Stored);

        public Task<AccountSettings> SaveAsync(AccountSettings settings, CancellationToken cancel = default)
        {
            if (Fail) throw new HttpRequestException("down");
            Saves++;
            Stored = settings;
            return Task.FromResult(settings);
        }
    }

    [Fact]
    public void SettingsAreReadWithDefaultsForWhatIsMissing()
    {
        var s = AccountSettings.Parse("""{"homeRowOrder":["a","b"],"hiddenRowIds":["c"],"subtitlesEnabled":true,"defaultSubtitleLanguage":"fr","blockedGenres":["Horror"]}""");
        Assert.Equal(["a", "b"], s.HomeRowOrder);
        Assert.Equal(["c"], s.HiddenRowIds);
        Assert.True(s.AutoplayNextEpisode); // missing: on
        Assert.True(s.SkipIntroEnabled);
        Assert.True(s.SubtitlesEnabled);
        Assert.Equal("fr", s.DefaultSubtitleLanguage);
        Assert.Null(s.DefaultAudioLanguage);
        Assert.Equal(["Horror"], s.BlockedGenres);
        var empty = AccountSettings.Parse("{}");
        Assert.Empty(empty.HomeRowOrder);
        Assert.False(empty.SubtitlesEnabled);
        Assert.Throws<System.Text.Json.JsonException>(() => AccountSettings.Parse("[1]"));
    }

    [Fact]
    public void AnEmptyLanguageIsSentAsNullSoTheAccountClearsIt()
    {
        var json = AccountSettings.Default.ToJson("2026-10-13T00:00:00.000Z");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, doc.RootElement.GetProperty("defaultSubtitleLanguage").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.GetProperty("blockedGenres").ValueKind);
        Assert.True(doc.RootElement.GetProperty("autoplayNextEpisode").GetBoolean());
        Assert.Equal("2026-10-13T00:00:00.000Z", doc.RootElement.GetProperty("updatedAt").GetString());
    }

    [Fact]
    public async Task AChangeIsShownAtOnceAndSaved()
    {
        var store = new FakeStore(AccountSettings.Default);
        var model = new SettingsModel(store);
        var changes = 0;
        model.Changed += () => changes++;
        await model.LoadAsync();
        await model.UpdateAsync(s => s with { SubtitlesEnabled = true });
        Assert.True(model.Current!.SubtitlesEnabled);
        Assert.True(store.Stored.SubtitlesEnabled);
        Assert.Equal(1, store.Saves);
        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task ANoChangeIsNotSaved()
    {
        var store = new FakeStore(AccountSettings.Default);
        var model = new SettingsModel(store);
        await model.LoadAsync();
        await model.UpdateAsync(s => s with { AutoplayNextEpisode = true });
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task AFailedSaveIsPutBackWithAMessage()
    {
        var store = new FakeStore(AccountSettings.Default);
        var model = new SettingsModel(store);
        await model.LoadAsync();
        store.Fail = true;
        await model.UpdateAsync(s => s with { SubtitlesEnabled = true });
        Assert.False(model.Current!.SubtitlesEnabled);
        Assert.NotNull(model.Problem);
        store.Fail = false;
        await model.UpdateAsync(s => s with { SubtitlesEnabled = true });
        Assert.Null(model.Problem);
    }

    [Fact]
    public async Task BlockingAGenreIsCaseInsensitiveAndReversible()
    {
        var model = new SettingsModel(new FakeStore(AccountSettings.Default));
        await model.SetGenreAsync("Horror", true);
        Assert.Contains("horror", model.Blocked);
        await model.SetGenreAsync("horror", true); // no duplicate
        Assert.Single(model.Current!.BlockedGenres!);
        await model.SetGenreAsync("HORROR", false);
        Assert.Empty(model.Blocked);
    }

    [Fact]
    public async Task AKidsListIsBlockedOnTopOfTheAccountsOwn()
    {
        var model = new SettingsModel(new FakeStore(AccountSettings.Default with { BlockedGenres = ["Horror"] }));
        await model.LoadAsync();
        model.AlsoBlocked = ["Crime", "War"];
        Assert.True(model.Blocked.SetEquals(["horror", "crime", "war"]));
        model.AlsoBlocked = [];
        Assert.Single(model.Blocked);
    }

    [Fact]
    public void BlockedGenresLeaveTitlesOut()
    {
        MetaPreview Item(string id, params string[] genres) => new(id, "movie", id) { Genres = [.. genres] };
        var items = new List<MetaPreview> { Item("a", "Action"), Item("b", "Horror", "Thriller"), Item("c") };
        var shown = BlockedGenres.Without(items, BlockedGenres.Set(["horror"]));
        Assert.Equal(["a", "c"], shown.Select(i => i.Id));
        Assert.Same(items, BlockedGenres.Without(items, BlockedGenres.Set([])));
    }

    [Fact]
    public void MovingARowSwapsItWithItsNeighbour()
    {
        string[] order = ["a", "b", "c"];
        Assert.Equal(["b", "a", "c"], HomeRowOrder.Move(order, "b", -1));
        Assert.Equal(["a", "c", "b"], HomeRowOrder.Move(order, "b", 1));
        Assert.Equal(order, HomeRowOrder.Move(order, "a", -1)); // cannot go above the top
        Assert.Equal(order, HomeRowOrder.Move(order, "c", 1));
        Assert.Equal(order, HomeRowOrder.Move(order, "zzz", 1));
    }

    [Fact]
    public async Task RowsCanBeSwitchedOffAndMoved()
    {
        var store = new FakeStore(AccountSettings.Default);
        var model = new SettingsModel(store);
        await model.SetRowVisibleAsync("b", false);
        Assert.Equal(["b"], model.Current!.HiddenRowIds);
        await model.SetRowVisibleAsync("b", true);
        Assert.Empty(model.Current!.HiddenRowIds);
        await model.MoveRowAsync(["a", "b", "c"], "c", -1);
        Assert.Equal(["a", "c", "b"], model.Current!.HomeRowOrder);
        Assert.Equal(["a", "c", "b"], model.RowPreferences.OrderOrEmpty);
    }

    [Fact]
    public void RowChoicesFollowTheOrderAndMarkHiddenOnes()
    {
        var rows = new List<HomeSection> { new("a", "A", []), new("b", "B", []), new("c", "C", []) };
        var choices = SettingsModel.RowChoices(rows, new HomeRowPreferences(["c", "a", "b"], ["a"]));
        Assert.Equal(["c", "a", "b"], choices.Select(c => c.Section.Id));
        Assert.Equal([true, false, true], choices.Select(c => c.Visible));
    }

    [Fact]
    public void LanguageListsStartWithTheSystemChoiceThenEnglish()
    {
        var subtitles = LanguageChoices.Subtitles();
        Assert.Null(subtitles[0].Code);
        Assert.Equal("en", subtitles[1].Code);
        Assert.Equal(subtitles.Skip(2).Select(l => l.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), subtitles.Skip(2).Select(l => l.Name));
        Assert.Equal("Automatic", LanguageChoices.Audio()[0].Name);
        Assert.Equal(LanguageChoices.All.Count + 1, LanguageChoices.Audio().Count);
    }

    [Fact]
    public void SubtitleStyleMapsToMpvAndFallsBackToTheDefault()
    {
        Assert.Equal(55, SubtitleSize.Medium.MpvFontSize());
        Assert.Equal(55 / 720.0, SubtitleSize.Medium.Fraction());
        Assert.Equal("#FFFF00", SubtitleColor.Yellow.Hex());
        Assert.Equal(SubtitleSize.Large, SubtitleStyle.SizeFrom("large"));
        Assert.Equal(SubtitleSize.Medium, SubtitleStyle.SizeFrom("nonsense"));
        Assert.Equal(SubtitleColor.White, SubtitleStyle.ColorFrom(null));
    }

    [Fact]
    public void LocalChoicesAreKeptTogether()
    {
        var path = Path.Combine(Path.GetTempPath(), "arctv-local-" + Guid.NewGuid() + ".json");
        try
        {
            var settings = new LocalSettings(path);
            settings.ServerUrl = "https://x.test/";
            settings.SubtitleSize = "Large";
            settings.SubtitleColor = "Cyan";
            var again = new LocalSettings(path);
            Assert.Equal("https://x.test", again.ServerUrl);
            Assert.Equal("Large", again.SubtitleSize);
            Assert.Equal("Cyan", again.SubtitleColor);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OncePopupsAndASkippedUpdateAreRememberedOnThisPc()
    {
        var path = Path.Combine(Path.GetTempPath(), "arctv-popups-" + Guid.NewGuid().ToString("N") + ".json");
        var settings = new ArcTV.Core.Storage.LocalSettings(path);
        Assert.Null(settings.PopupSeen("plus-welcome-u1"));
        Assert.Null(settings.SkippedUpdate);
        settings.MarkPopupSeen("plus-welcome-u1", "id-1");
        settings.MarkPopupSeen("plus-welcome-u2", "id-2");
        settings.SkippedUpdate = "0.2.0";
        var again = new ArcTV.Core.Storage.LocalSettings(path);
        Assert.Equal("id-1", again.PopupSeen("plus-welcome-u1"));
        Assert.Equal("id-2", again.PopupSeen("plus-welcome-u2")); // each account has its own
        Assert.Equal("0.2.0", again.SkippedUpdate);
        again.ServerUrl = "https://x.test"; // another setting does not lose them
        Assert.Equal("id-1", new ArcTV.Core.Storage.LocalSettings(path).PopupSeen("plus-welcome-u1"));
        File.Delete(path);
    }

    [Fact]
    public void TheWelcomeShowsOnceUntilItsContentChangesAndAnUpdateIsOfferedUnlessSkipped()
    {
        Assert.True(ArcTV.Core.Plus.PlusPopups.WelcomeDue(null));
        Assert.True(ArcTV.Core.Plus.PlusPopups.WelcomeDue("an-older-welcome"));
        Assert.False(ArcTV.Core.Plus.PlusPopups.WelcomeDue(ArcTV.Core.Plus.PlusPopups.WelcomeId));
        Assert.NotEqual(ArcTV.Core.Plus.PlusPopups.WelcomeKey("a"), ArcTV.Core.Plus.PlusPopups.WelcomeKey("b"));

        var release = new ArcTV.Core.ReleaseInfo("0.3.0", "Notes", "https://page", "https://download");
        Assert.True(ArcTV.Core.UpdateCheck.Due(new ArcTV.Core.UpdateResult(true, "0.3.0", release), null));
        Assert.True(ArcTV.Core.UpdateCheck.Due(new ArcTV.Core.UpdateResult(true, "0.3.0", release), "0.2.0")); // an older skip does not hide a newer version
        Assert.False(ArcTV.Core.UpdateCheck.Due(new ArcTV.Core.UpdateResult(true, "0.3.0", release), "0.3.0"));
        Assert.False(ArcTV.Core.UpdateCheck.Due(new ArcTV.Core.UpdateResult(false, "0.1.0", null), null));
    }
}
