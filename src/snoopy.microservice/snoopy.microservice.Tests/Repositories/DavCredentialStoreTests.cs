using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class DavCredentialStoreTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static DavCredentialStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task Enable_WhenAbsent_CreatesTheRowAndAnswersTheSecret()
    {
        var db = nameof(Enable_WhenAbsent_CreatesTheRowAndAnswersTheSecret);

        var secret = await CreateStore(db).EnableAsync(User, CancellationToken.None);

        Assert.NotNull(secret);
        Assert.Equal(DavSecret.Length, secret!.Length);
        using var ctx = new PreferencesTestDbContext(db);
        var row = Assert.Single(ctx.DavCredentials);
        Assert.True(row.CardDavEnabled);
        Assert.Equal(DavSecret.SaltLength, row.Salt.Length);
        // Stored as a digest and nothing else: the table is never a keyring to steal.
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, secret));
        Assert.DoesNotContain(secret, row.SecretHash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enable_WhenAlreadyConfigured_TurnsItBackOnWithoutANewSecret()
    {
        var db = nameof(Enable_WhenAlreadyConfigured_TurnsItBackOnWithoutANewSecret);
        var first = await CreateStore(db).EnableAsync(User, CancellationToken.None);
        await CreateStore(db).DisableAsync(User, CancellationToken.None);

        var again = await CreateStore(db).EnableAsync(User, CancellationToken.None);

        Assert.Null(again);
        using var ctx = new PreferencesTestDbContext(db);
        var row = Assert.Single(ctx.DavCredentials);
        Assert.True(row.CardDavEnabled);
        // Turning off destroys nothing, turning back on reconfigures no device.
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, first!));
    }

    [Fact]
    public async Task TwoFirstEnablesAtOnce_LeaveOneRowAndOneSecret()
    {
        // Double click, two tabs. The InMemory provider does enforce the primary key on
        // SaveChanges, so this exercises the real DbUpdateException path rather than a mock of it.
        var db = nameof(TwoFirstEnablesAtOnce_LeaveOneRowAndOneSecret);
        var first = CreateStore(db);
        var second = CreateStore(db);

        var winner = await first.EnableAsync(User, CancellationToken.None);
        var loser = await second.EnableAsync(User, CancellationToken.None);

        Assert.NotNull(winner);
        // The loser answers as a re-enable does: the state, and no second secret.
        Assert.Null(loser);
        using var ctx = new PreferencesTestDbContext(db);
        var row = Assert.Single(ctx.DavCredentials);
        Assert.True(row.CardDavEnabled);
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, winner!));
    }

    [Fact]
    public async Task Disable_KeepsTheSecret()
    {
        var db = nameof(Disable_KeepsTheSecret);
        var secret = await CreateStore(db).EnableAsync(User, CancellationToken.None);

        await CreateStore(db).DisableAsync(User, CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        var row = Assert.Single(ctx.DavCredentials);
        Assert.False(row.CardDavEnabled);
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, secret!));
    }

    [Fact]
    public async Task Disable_OnAnAccountThatNeverEnabled_DoesNothing()
    {
        var db = nameof(Disable_OnAnAccountThatNeverEnabled_DoesNothing);

        await CreateStore(db).DisableAsync(User, CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.DavCredentials);
    }

    [Fact]
    public async Task Regenerate_ReplacesTheSecretAndTheSaltOnTheSameRow()
    {
        var db = nameof(Regenerate_ReplacesTheSecretAndTheSaltOnTheSameRow);
        var first = await CreateStore(db).EnableAsync(User, CancellationToken.None);
        byte[] firstSalt;
        using (var before = new PreferencesTestDbContext(db)) firstSalt = before.DavCredentials.Single().Salt;

        var second = await CreateStore(db).RegenerateAsync(User, CancellationToken.None);

        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        using var ctx = new PreferencesTestDbContext(db);
        // One row, never a second: user_id is the primary key and the shape is the guarantee.
        var row = Assert.Single(ctx.DavCredentials);
        Assert.NotEqual(firstSalt, row.Salt);
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, second!));
        Assert.False(DavSecret.Matches(row.Salt, row.SecretHash, first!));
    }

    [Fact]
    public async Task Regenerate_OnAnAccountThatNeverEnabled_CreatesNothing()
    {
        var db = nameof(Regenerate_OnAnAccountThatNeverEnabled_CreatesNothing);

        var secret = await CreateStore(db).RegenerateAsync(User, CancellationToken.None);

        Assert.Null(secret);
        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.DavCredentials);
    }

    [Fact]
    public async Task GetState_ReportsAConfiguredAccountAndCarriesNoSecret()
    {
        var db = nameof(GetState_ReportsAConfiguredAccountAndCarriesNoSecret);
        await CreateStore(db).EnableAsync(User, CancellationToken.None);

        var state = await CreateStore(db).GetStateAsync(User, CancellationToken.None);

        Assert.True(state.Configured);
        Assert.True(state.CardDavEnabled);
        Assert.Null(state.LastUsedAt);
        // The assertion that keeps the "reveal" door shut: the shape has nowhere to put a secret.
        Assert.Equal(3, typeof(DavCredentialState).GetProperties().Length);
    }

    [Fact]
    public async Task GetState_OnAnAccountThatNeverEnabled_IsNotConfigured()
    {
        var db = nameof(GetState_OnAnAccountThatNeverEnabled_IsNotConfigured);

        var state = await CreateStore(db).GetStateAsync(User, CancellationToken.None);

        Assert.False(state.Configured);
        Assert.False(state.CardDavEnabled);
    }

    [Fact]
    public async Task Find_AnswersTheRowTheHandlerCompares()
    {
        var db = nameof(Find_AnswersTheRowTheHandlerCompares);
        var secret = await CreateStore(db).EnableAsync(User, CancellationToken.None);

        var record = await CreateStore(db).FindAsync(User, CancellationToken.None);

        Assert.NotNull(record);
        Assert.True(record!.Value.CardDavEnabled);
        Assert.True(DavSecret.Matches(record.Value.Salt, record.Value.SecretHash, secret!));
    }

    [Fact]
    public async Task Find_OnAnAccountThatNeverEnabled_IsNull()
    {
        var db = nameof(Find_OnAnAccountThatNeverEnabled_IsNull);

        Assert.Null(await CreateStore(db).FindAsync(User, CancellationToken.None));
    }

    [Fact]
    public async Task Touch_WritesTheDateAndCreatesNothingWhenThereIsNoRow()
    {
        var db = nameof(Touch_WritesTheDateAndCreatesNothingWhenThereIsNoRow);
        var used = new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);

        await CreateStore(db).TouchAsync(User, used, CancellationToken.None);
        using (var empty = new PreferencesTestDbContext(db)) Assert.Empty(empty.DavCredentials);

        await CreateStore(db).EnableAsync(User, CancellationToken.None);
        await CreateStore(db).TouchAsync(User, used, CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Equal(used, ctx.DavCredentials.Single().LastUsedAt);
    }

    [Fact]
    public async Task Delete_RemovesTheRowAndIsSilentOnAnAbsentOne()
    {
        var db = nameof(Delete_RemovesTheRowAndIsSilentOnAnAbsentOne);
        await CreateStore(db).EnableAsync(User, CancellationToken.None);

        await CreateStore(db).DeleteAsync(User, CancellationToken.None);
        await CreateStore(db).DeleteAsync(User, CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.DavCredentials);
    }
}
