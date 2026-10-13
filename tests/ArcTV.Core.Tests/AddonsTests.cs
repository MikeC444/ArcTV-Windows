using ArcTV.Core.Account;
using ArcTV.Core.Addons;
using Xunit;

namespace ArcTV.Core.Tests;

public class AddonsTests
{
    private const string Torrentio = """{"id":"org.torrentio","name":"Torrentio","version":"0.0.15","resources":["stream",{"name":"meta","types":["movie"]}],"types":["movie","series"],"extra":{"keep":"me"}}""";

    private sealed class FakeStore : IAddonStore
    {
        public List<SyncedAddon> Items = [];
        public bool Fail;
        public List<string> Saved = [];

        public Task<IReadOnlyList<SyncedAddon>> ListAsync(CancellationToken cancel = default) =>
            Fail ? throw new HttpRequestException("down") : Task.FromResult<IReadOnlyList<SyncedAddon>>(Items.OrderBy(a => a.SortOrder).ToList());

        public Task SaveAsync(string manifestUrl, string addonId, string name, string rawManifest, bool enabled, int sortOrder, CancellationToken cancel = default)
        {
            if (Fail) throw new HttpRequestException("down");
            Saved.Add(rawManifest);
            Items.RemoveAll(a => a.ManifestUrl == manifestUrl);
            Items.Add(new SyncedAddon(manifestUrl, addonId, name, enabled, sortOrder, null) { RawManifest = rawManifest });
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string manifestUrl, CancellationToken cancel = default)
        {
            if (Fail) throw new HttpRequestException("down");
            Items.RemoveAll(a => a.ManifestUrl == manifestUrl);
            return Task.CompletedTask;
        }
    }

    private static AddonsModel Model(FakeStore store) =>
        new(store, (link, _) => Task.FromResult(AddonInstall.Parse(AddonUrl.NormalizeManifestUrl(link), link.Contains("bad") ? "{}" : Torrentio)));

    [Fact]
    public void ParseNeedsAnIdAndAName()
    {
        var ok = AddonInstall.Parse("https://t.test/manifest.json", Torrentio);
        Assert.Equal("org.torrentio", ok.AddonId);
        Assert.Equal("Torrentio", ok.Name);
        Assert.Contains("\"keep\":\"me\"", ok.RawManifest); // the manifest is kept as it came
        Assert.Throws<ApiException>(() => AddonInstall.Parse("u", "{}"));
        Assert.Throws<ApiException>(() => AddonInstall.Parse("u", "[1]"));
        Assert.Throws<ApiException>(() => AddonInstall.Parse("u", "not json"));
    }

    [Fact]
    public void SaveBodyCarriesTheManifestUntouched()
    {
        var body = AddonsApi.SaveBody("https://t.test/manifest.json", "org.torrentio", "Torrentio", Torrentio, true, 3, "2026-10-13T00:00:00.000Z");
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("https://t.test/manifest.json", root.GetProperty("manifestUrl").GetString());
        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, root.GetProperty("sortOrder").GetInt32());
        Assert.Equal("me", root.GetProperty("manifestJson").GetProperty("extra").GetProperty("keep").GetString());
    }

    [Fact]
    public async Task InstallAddsAtTheEndAndSaysSo()
    {
        var store = new FakeStore { Items = [new("https://a/manifest.json", "a", "A", true, 4, null) { RawManifest = "{}" }] };
        var model = Model(store);
        var changed = 0;
        model.Changed += () => changed++;
        await model.LoadAsync();
        await model.InstallAsync("stremio://torrentio.test");
        Assert.Equal(2, model.Items.Count);
        Assert.Equal(5, model.Items[1].SortOrder);
        Assert.Equal("https://torrentio.test/manifest.json", model.Items[1].ManifestUrl);
        Assert.Equal("Added Torrentio.", model.Message);
        Assert.False(model.MessageIsError);
        Assert.Equal(1, changed);
        Assert.False(model.Busy);
    }

    [Fact]
    public async Task InstallingTheSameLinkAgainKeepsItsPlace()
    {
        var store = new FakeStore();
        var model = Model(store);
        await model.LoadAsync();
        await model.InstallAsync("torrentio.test");
        await model.InstallAsync("torrentio.test/manifest.json");
        Assert.Single(model.Items);
    }

    [Fact]
    public async Task ABadLinkIsAnErrorAndChangesNothing()
    {
        var store = new FakeStore();
        var model = Model(store);
        var changed = 0;
        model.Changed += () => changed++;
        await model.InstallAsync("bad.test");
        Assert.Empty(model.Items);
        Assert.True(model.MessageIsError);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task SwitchingAnAddonOffSendsItsManifestBack()
    {
        var store = new FakeStore();
        var model = Model(store);
        await model.InstallAsync("torrentio.test");
        await model.SetEnabledAsync(model.Items[0], false);
        Assert.False(model.Items[0].Enabled);
        Assert.Equal(2, store.Saved.Count);
        Assert.Equal(store.Saved[0], store.Saved[1]);
    }

    [Fact]
    public async Task RemovingTakesItOff()
    {
        var store = new FakeStore();
        var model = Model(store);
        await model.InstallAsync("torrentio.test");
        await model.RemoveAsync(model.Items[0]);
        Assert.Empty(model.Items);
        Assert.Equal("Removed Torrentio.", model.Message);
    }

    [Fact]
    public async Task AFailingAccountIsReportedNotThrown()
    {
        var store = new FakeStore { Fail = true };
        var model = Model(store);
        await model.LoadAsync();
        Assert.True(model.Loaded);
        Assert.True(model.MessageIsError);
        await model.InstallAsync("torrentio.test");
        Assert.True(model.MessageIsError);
        Assert.False(model.Busy);
    }

    [Fact]
    public void ProvidesListsWhatTheManifestOffers()
    {
        var addon = new SyncedAddon("u", "x", "X", true, 0, System.Text.Json.JsonSerializer.Deserialize<AddonManifest>(Torrentio, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Assert.Equal("Provides: Streams, Metas", AddonsModel.Provides(addon));
        Assert.Equal("Provides: not stated", AddonsModel.Provides(new SyncedAddon("u", "x", "X", true, 0, null)));
    }
}
