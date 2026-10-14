using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.WhatsNew;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Repositories;

public sealed class WhatsNewStoreTests
{
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly Guid _user = Guid.NewGuid();

    private PreferencesTestDbContext Context() => new(_db);

    private UserPreference Baseline(string version) => new()
    {
        UserId = _user, PreferenceKey = UserPreferences.WhatsNewBaseline, PreferenceValue = version, UpdatedAt = DateTime.UtcNow
    };

    private async Task<WhatsNewStore> StoreWithUserAsync(string? baseline = null)
    {
        await using (var seed = Context())
        {
            seed.Users.Add(new WebmailUser { Id = _user, Email = "a@x", SecurityStamp = Guid.NewGuid(), CreationDate = DateTime.UtcNow });
            if (baseline is not null) seed.UserPreferences.Add(Baseline(baseline));
            await seed.SaveChangesAsync();
        }
        return new WhatsNewStore(Context());
    }

    [Fact]
    public async Task Get_AnswersTheBaselineAndOnlyThisAccountsRows()
    {
        var store = await StoreWithUserAsync("2.4.0");
        await using (var other = Context())
        {
            other.WhatsNewItems.Add(new WhatsNewItem { UserId = Guid.NewGuid(), ItemId = "daily-image" });
            await other.SaveChangesAsync();
        }
        await store.MarkAsync(_user, ["daily-image"], WhatsNewMark.Shown, CancellationToken.None);

        var state = await store.GetAsync(_user, CancellationToken.None);

        Assert.Equal("2.4.0", state.Baseline);
        Assert.Equal("daily-image", Assert.Single(state.Items).ItemId);
    }

    [Fact]
    public async Task Get_AnswersNoBaselineForANewAccount()
    {
        var store = await StoreWithUserAsync();

        Assert.Null((await store.GetAsync(_user, CancellationToken.None)).Baseline);
    }

    [Fact]
    public async Task Mark_FillsOnlyEmptyColumnsAndAlwaysTheFirstShown()
    {
        // Stamps far in the past: a store overwriting them would fail however fast the clock runs.
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var store = await StoreWithUserAsync();
        await using (var seed = Context())
        {
            seed.WhatsNewItems.Add(new WhatsNewItem { UserId = _user, ItemId = "a", SeenAt = old });
            seed.WhatsNewItems.Add(new WhatsNewItem { UserId = _user, ItemId = "b", FirstShownAt = old, SeenAt = old });
            await seed.SaveChangesAsync();
        }

        await store.MarkAsync(_user, ["a", "b"], WhatsNewMark.Seen, CancellationToken.None);

        var items = (await store.GetAsync(_user, CancellationToken.None)).Items;
        var a = items.Single(i => i.ItemId == "a");
        var b = items.Single(i => i.ItemId == "b");
        Assert.True(a.FirstShownAt > old);
        Assert.Null(a.ListedAt);
        Assert.Equal(old, a.SeenAt);
        Assert.Equal(old, b.FirstShownAt);
        Assert.Equal(old, b.SeenAt);
    }

    [Fact]
    public async Task Mark_StampsEveryIdOfTheBatch()
    {
        var store = await StoreWithUserAsync();
        await store.MarkAsync(_user, ["a"], WhatsNewMark.Shown, CancellationToken.None);

        await store.MarkAsync(_user, ["a", "b"], WhatsNewMark.Listed, CancellationToken.None);

        var items = (await store.GetAsync(_user, CancellationToken.None)).Items;
        Assert.Equal(["a", "b"], items.Select(i => i.ItemId));
        Assert.All(items, i => Assert.NotNull(i.ListedAt));
    }

    [Fact]
    public async Task Mark_SurvivesARowCreatedMeanwhile()
    {
        await StoreWithUserAsync();
        var winner = DateTime.UtcNow.AddMinutes(-1);
        var racing = new RacingPreferencesDbContext(_db, async () =>
        {
            await using var other = Context();
            other.WhatsNewItems.Add(new WhatsNewItem { UserId = _user, ItemId = "a", FirstShownAt = winner });
            await other.SaveChangesAsync();
        });

        await new WhatsNewStore(racing).MarkAsync(_user, ["a"], WhatsNewMark.Seen, CancellationToken.None);

        var row = Assert.Single((await new WhatsNewStore(Context()).GetAsync(_user, CancellationToken.None)).Items);
        Assert.Equal(winner, row.FirstShownAt);
        Assert.NotNull(row.SeenAt);
    }

    [Fact]
    public async Task SetBaseline_WritesOnceThenRefuses()
    {
        var store = await StoreWithUserAsync();

        Assert.True(await store.SetBaselineAsync(_user, "2.4.0", CancellationToken.None));
        Assert.False(await store.SetBaselineAsync(_user, "9.9.9", CancellationToken.None));
        Assert.Equal("2.4.0", (await store.GetAsync(_user, CancellationToken.None)).Baseline);
    }

    [Fact]
    public async Task SetBaseline_LosesQuietlyToAnotherTab()
    {
        await StoreWithUserAsync();
        var racing = new RacingPreferencesDbContext(_db, async () =>
        {
            await using var other = Context();
            other.UserPreferences.Add(Baseline("2.4.0"));
            await other.SaveChangesAsync();
        });

        Assert.False(await new WhatsNewStore(racing).SetBaselineAsync(_user, "2.4.0-dev", CancellationToken.None));
        Assert.Equal("2.4.0", (await new WhatsNewStore(Context()).GetAsync(_user, CancellationToken.None)).Baseline);
    }
}
