using System.Net;
using ArcTV.Core.Account;
using ArcTV.Core.Profiles;
using ArcTV.Core.Settings;
using Xunit;

namespace ArcTV.Core.Tests;

public class ProfilesTests
{
    private sealed class FakeStore : IProfileStore
    {
        public List<Profile> Items = [new("main", "Me", "fox", IsDefault: true), new("p2", "Kid", "cat", "kids", HasPin: true)];
        public PinOutcome Outcome = PinOutcome.Ok;
        public bool Fail;
        public List<string> Calls = [];
        public string? LastCurrentPin;

        public Task<IReadOnlyList<Profile>> ListAsync(CancellationToken cancel = default) => Fail ? throw new HttpRequestException("down") : Task.FromResult<IReadOnlyList<Profile>>(Items.ToList());

        public Task CreateAsync(string name, string avatar, string kind, string? pin, CancellationToken cancel = default)
        {
            Calls.Add($"create {name} {avatar} {kind} {pin}");
            Items.Add(new Profile("new", name, avatar, kind, pin is not null));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(string id, string name, string avatar, string kind, PinChange pin, string? currentPin, CancellationToken cancel = default)
        {
            Calls.Add($"update {id} {name} {avatar} {kind} {pin.Kind} {pin.Pin}");
            LastCurrentPin = currentPin;
            var i = Items.FindIndex(p => p.Id == id);
            Items[i] = Items[i] with { Name = name, Avatar = avatar, Kind = kind };
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, string? currentPin, CancellationToken cancel = default)
        {
            Calls.Add("delete " + id);
            LastCurrentPin = currentPin;
            Items.RemoveAll(p => p.Id == id);
            return Task.CompletedTask;
        }

        public Task<PinOutcome> VerifyPinAsync(string id, string pin, CancellationToken cancel = default) => Fail ? throw new HttpRequestException("down") : Task.FromResult(Outcome);
    }

    [Fact]
    public void AvatarsResolveOldIdsAndAnythingUnknownToAPicture()
    {
        Assert.Equal(16, Avatars.Ids.Length);
        Assert.Equal("lion", Avatars.Resolve("lion"));
        Assert.Equal("astronaut", Avatars.Resolve("astro")); // an old colour tile
        Assert.Equal("fox", Avatars.Resolve("nonsense"));
        Assert.Equal("fox", Avatars.Resolve(null));
    }

    [Fact]
    public void PinsAreFourDigitsAndAccountCallsAreToldApart()
    {
        Assert.True(Profile.IsValidPin("0420"));
        Assert.False(Profile.IsValidPin("042"));
        Assert.False(Profile.IsValidPin("04201"));
        Assert.False(Profile.IsValidPin("04a0"));
        Assert.False(Profile.IsValidPin(null));
        Assert.True(Profile.IsAccountLevel("/user/profiles"));
        Assert.True(Profile.IsAccountLevel("/user/profiles/p2/verify-pin"));
        Assert.True(Profile.IsAccountLevel("/user/plus"));
        Assert.False(Profile.IsAccountLevel("/user/watchlist"));
        Assert.False(Profile.IsAccountLevel("/user/plusminus"));
    }

    [Fact]
    public async Task ProfilesAreReadOnlyWithPlus()
    {
        var model = new ProfilesModel(new FakeStore());
        await model.LoadAsync(false);
        Assert.Empty(model.Profiles);
        await model.LoadAsync(true);
        Assert.Equal(2, model.Profiles.Count);
        Assert.True(model.CanAdd);
        Assert.True(model.Find("p2")!.IsKids);

        var down = new ProfilesModel(new FakeStore { Fail = true });
        await down.LoadAsync(true);
        Assert.Empty(down.Profiles); // nothing to pick: the account's own profile
    }

    [Fact]
    public async Task APinIsCheckedWithWordsForEachOutcome()
    {
        var store = new FakeStore();
        var model = new ProfilesModel(store);
        var locked = store.Items[1];
        Assert.Null(await model.CheckPinAsync(store.Items[0], null)); // no PIN on it
        Assert.Equal("Enter the 4-digit PIN.", await model.CheckPinAsync(locked, "12"));
        Assert.Null(await model.CheckPinAsync(locked, "1234"));
        store.Outcome = PinOutcome.Wrong;
        Assert.Equal("That PIN is wrong.", await model.CheckPinAsync(locked, "1234"));
        store.Outcome = PinOutcome.Locked;
        Assert.Contains("Too many wrong PINs", await model.CheckPinAsync(locked, "1234"));
        store.Fail = true;
        Assert.Contains("Couldn't check the PIN", await model.CheckPinAsync(locked, "1234"));
    }

    [Fact]
    public async Task CreatingEditingAndDeletingReloadTheList()
    {
        var store = new FakeStore();
        var model = new ProfilesModel(store);
        await model.LoadAsync(true);

        await model.CreateAsync(" Gran ", "owl", true, "4321");
        Assert.Equal("create Gran owl kids 4321", store.Calls[0]);
        Assert.Equal(3, model.Profiles.Count);

        await model.UpdateAsync(model.Find("p2")!, "Junior", "dog", false, PinChange.Remove, "1111");
        Assert.Equal("update p2 Junior dog adult Remove ", store.Calls[1]);
        Assert.Equal("1111", store.LastCurrentPin);
        Assert.Equal("Junior", model.Find("p2")!.Name);

        // the account's own profile is never a kids profile
        await model.UpdateAsync(model.Find("main")!, "Me", "fox", true, PinChange.Keep, null);
        Assert.Equal("update main Me fox adult Keep ", store.Calls[2]);

        await model.DeleteAsync(model.Find("new")!, null);
        Assert.Null(model.Find("new"));
    }

    [Fact]
    public void NamesNeedToBeThereAndShortEnough()
    {
        Assert.NotNull(ProfilesModel.NameProblem(""));
        Assert.NotNull(ProfilesModel.NameProblem("   "));
        Assert.Null(ProfilesModel.NameProblem("Sam"));
        Assert.NotNull(ProfilesModel.NameProblem(new string('x', 25)));
        Assert.Null(ProfilesModel.NameProblem(new string('x', 24)));
    }

    [Fact]
    public async Task ProfileCallsGoToTheAccountWithoutTheProfileHeaderAndLibraryCallsCarryIt()
    {
        var handler = new FakeHandler((request, _) => (HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("/verify-pin") ? "{}" : """{"profiles":[{"id":"main","name":"Me","avatar":"fox","kind":"adult","hasPin":false,"isDefault":true},{"id":"p2","name":"Kid","avatar":"cat","kind":"kids","hasPin":true,"isDefault":false}]}"""));
        var client = new AuthClient("https://x.test", "p2", new HttpClient(handler));

        var list = await client.ProfilesAsync("tok");
        Assert.Equal(2, list.Count);
        Assert.True(list[1].IsKids);
        Assert.True(list[1].HasPin);
        Assert.True(list[0].IsDefault);
        Assert.False(handler.Calls[0].Request.Headers.Contains("X-ArcTV-Profile")); // the list is about the account

        await client.SendJsonAsync(HttpMethod.Get, "/user/watchlist", null, null, "tok");
        Assert.Equal("p2", handler.Calls[1].Request.Headers.GetValues("X-ArcTV-Profile").Single());

        Assert.Equal(PinOutcome.Ok, await client.VerifyPinAsync("tok", "p2", "1234"));
        Assert.Equal(HttpMethod.Post, handler.Calls[2].Request.Method);
        Assert.Contains("/user/profiles/p2/verify-pin", handler.Calls[2].Request.RequestUri!.AbsolutePath);
        Assert.Contains("\"pin\":\"1234\"", handler.Calls[2].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, PinOutcome.Wrong)]
    [InlineData((HttpStatusCode)429, PinOutcome.Locked)]
    public async Task APinTheServerRefusesIsWrongOrLocked(HttpStatusCode status, PinOutcome expected)
    {
        var client = new AuthClient("https://x.test", null, new HttpClient(new FakeHandler((_, _) => (status, """{"error":"no"}"""))));
        Assert.Equal(expected, await client.VerifyPinAsync("tok", "p2", "1234"));
    }

    [Fact]
    public async Task EditsSendThePinAsKeptRemovedOrSetAndTheCurrentPinInAHeader()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.OK, """{"id":"p2","name":"Kid","avatar":"cat","kind":"kids","hasPin":false,"isDefault":false}"""));
        var client = new AuthClient("https://x.test", null, new HttpClient(handler));

        await client.UpdateProfileAsync("tok", "p2", "Kid", "cat", "kids", PinChange.Keep, null);
        Assert.DoesNotContain("pin", handler.Calls[0].Body);
        Assert.False(handler.Calls[0].Request.Headers.Contains("X-ArcTV-Pin"));

        await client.UpdateProfileAsync("tok", "p2", "Kid", "cat", "kids", PinChange.Remove, "1234");
        Assert.Contains("\"pin\":null", handler.Calls[1].Body);
        Assert.Equal("1234", handler.Calls[1].Request.Headers.GetValues("X-ArcTV-Pin").Single());

        await client.UpdateProfileAsync("tok", "p2", "Kid", "cat", "kids", PinChange.Set("9876"), "1234");
        Assert.Contains("\"pin\":\"9876\"", handler.Calls[2].Body);
        Assert.Equal(HttpMethod.Put, handler.Calls[2].Request.Method);

        await client.DeleteProfileAsync("tok", "p2", "1234");
        Assert.Equal(HttpMethod.Delete, handler.Calls[3].Request.Method);
        Assert.Equal("1234", handler.Calls[3].Request.Headers.GetValues("X-ArcTV-Pin").Single());
    }
}
