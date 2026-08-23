using Microsoft.EntityFrameworkCore;
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
    public async Task Enable_WhenAConcurrentFirstEnableWinsTheRace_AnswersNoSecondSecret()
    {
        // Double click, two tabs. The rival enable really runs, between this call's read and its
        // write; only the duplicate-key rejection is injected, because the InMemory provider does
        // not raise the DbUpdateException a relational store raises there. Nothing else is faked:
        // this drives the store's own recovery — detach, re-read the winner, answer null — and
        // would fail with the exception in hand if that recovery were missing.
        var db = nameof(Enable_WhenAConcurrentFirstEnableWinsTheRace_AnswersNoSecondSecret);
        string? winner = null;
        var context = new DuplicateKeyOnFirstSaveDbContext(db, async () =>
        {
            winner = await CreateStore(db).EnableAsync(User, CancellationToken.None);
        });

        var loser = await new DavCredentialStore(context).EnableAsync(User, CancellationToken.None);

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
    }

    [Fact]
    public void DavCredentialState_HasNowhereToPutASecret()
    {
        // The assertion that keeps the "reveal it again" door shut: a fourth property would have
        // to be added here first, deliberately, rather than slipped in beside a screen field.
        Assert.Equal(3, typeof(DavCredentialState).GetProperties().Length);
    }

    [Fact]
    public void DavCredentialRecord_ToString_RendersNeitherTheDigestNorTheSalt()
    {
        // The handler is this record's only reader, and a LogDebug of it while debugging is
        // exactly how a digest reaches a log file. The synthesised ToString prints all three.
        var digest = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));

        var rendered = new DavCredentialRecord(true, digest, [9, 8, 7]).ToString();

        Assert.DoesNotContain(digest, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(digest[..8], rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(DavCredentialRecord.SecretHash), rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DavCredentialRecord.Salt), rendered, StringComparison.Ordinal);
        Assert.Contains(nameof(DavCredentialRecord.CardDavEnabled), rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetState_ReportsTheLastUseTheAuthenticationPathStamped()
    {
        // The only question the screen asks before "Regenerate": is anything still syncing? Every
        // other GetState test reads that field as null, so the projection answered to nobody.
        var db = nameof(GetState_ReportsTheLastUseTheAuthenticationPathStamped);
        var used = new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);
        await CreateStore(db).EnableAsync(User, CancellationToken.None);
        await CreateStore(db).TouchAsync(User, used, CancellationToken.None);

        var state = await CreateStore(db).GetStateAsync(User, CancellationToken.None);

        Assert.Equal(used, state.LastUsedAt);
    }

    [Fact]
    public async Task GetState_StampsTheLastUseAsUtcWhateverKindTheProviderReadBack()
    {
        // Pomelo reads a MariaDB DATETIME back as Unspecified, and a serialised Unspecified has no
        // "Z" — the browser then reads it as local time. InMemory keeps the Kind it was handed, so
        // the row is written Unspecified here to stand in for what the real provider returns.
        var db = nameof(GetState_StampsTheLastUseAsUtcWhateverKindTheProviderReadBack);
        var used = new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Unspecified);
        await CreateStore(db).EnableAsync(User, CancellationToken.None);
        await CreateStore(db).TouchAsync(User, used, CancellationToken.None);

        var state = await CreateStore(db).GetStateAsync(User, CancellationToken.None);

        Assert.Equal(DateTimeKind.Utc, state.LastUsedAt!.Value.Kind);
        Assert.Equal(used, state.LastUsedAt.Value);
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
    public async Task GetState_OnASwitchedOffRow_StaysConfigured()
    {
        var db = nameof(GetState_OnASwitchedOffRow_StaysConfigured);
        await CreateStore(db).EnableAsync(User, CancellationToken.None);
        await CreateStore(db).DisableAsync(User, CancellationToken.None);

        var state = await CreateStore(db).GetStateAsync(User, CancellationToken.None);

        // Off but configured is not "never configured": the screen offers to switch back on rather
        // than to set up, and the edge answers 403 rather than 401.
        Assert.True(state.Configured);
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
    public async Task Find_OnASwitchedOffRow_AnswersItWithTheFlagOff()
    {
        var db = nameof(Find_OnASwitchedOffRow_AnswersItWithTheFlagOff);
        var secret = await CreateStore(db).EnableAsync(User, CancellationToken.None);
        await CreateStore(db).DisableAsync(User, CancellationToken.None);

        var record = await CreateStore(db).FindAsync(User, CancellationToken.None);

        // The row still answers, digest and all: the edge refuses only after a successful
        // comparison, without which the refusal would enumerate accounts.
        Assert.NotNull(record);
        Assert.False(record!.Value.CardDavEnabled);
        Assert.True(DavSecret.Matches(record.Value.Salt, record.Value.SecretHash, secret!));
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

    /// <summary>
    /// Runs a rival write between this context's read and its own — the one instant at which a
    /// duplicate key can reach SaveChanges — then rejects that first save the way MariaDB does,
    /// with a <see cref="DbUpdateException"/>. The rejection is injected because the InMemory
    /// provider models no unique constraint: it lets a raw <c>ArgumentException</c> escape from
    /// the dictionary backing its tables, which is the fake's shortcoming and not the contract.
    /// Deterministic by construction, where two genuinely parallel calls would leave the winner
    /// to chance. Test-only: the store under test is untouched.
    /// </summary>
    private sealed class DuplicateKeyOnFirstSaveDbContext(string databaseName, Func<Task> rival)
        : PreferencesDbContext(new DbContextOptionsBuilder<PreferencesDbContext>()
            .UseInMemoryDatabase(databaseName).Options)
    {
        private bool _rejected;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_rejected)
            {
                _rejected = true;
                await rival();
                // Rejected means written nowhere, exactly as the real insert would be.
                throw new DbUpdateException("Duplicate entry for key 'PRIMARY'");
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
