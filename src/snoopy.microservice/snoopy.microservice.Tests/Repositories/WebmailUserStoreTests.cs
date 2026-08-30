using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class WebmailUserStoreTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private static WebmailUserStore CreateStore(string dbName, IDavAuthenticationCache? cache = null) =>
        new(new PreferencesTestDbContext(dbName), cache ?? new DavAuthenticationCache(Clock));

    [Fact]
    public async Task RegisterLogin_WhenAbsent_CreatesRowWithGuidAndStamps()
    {
        var store = CreateStore(nameof(RegisterLogin_WhenAbsent_CreatesRowWithGuidAndStamps));

        var id = (await store.RegisterLoginAsync("mick@weesky.be", CancellationToken.None)).Id;

        Assert.NotEqual(Guid.Empty, id);
        using var ctx = new PreferencesTestDbContext(nameof(RegisterLogin_WhenAbsent_CreatesRowWithGuidAndStamps));
        var row = ctx.Users.Single();
        Assert.Equal(id, row.Id);
        Assert.Equal("mick@weesky.be", row.Email);
        Assert.NotNull(row.LastLoginDate);
        Assert.Equal(row.CreationDate, row.LastLoginDate);
    }

    [Fact]
    public async Task RegisterLogin_WhenPresent_KeepsGuidAndCreationButAdvancesLastLogin()
    {
        var db = nameof(RegisterLogin_WhenPresent_KeepsGuidAndCreationButAdvancesLastLogin);
        var first = (await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None)).Id;
        DateTime creation, firstLogin;
        using (var ctx = new PreferencesTestDbContext(db))
        {
            var row = ctx.Users.Single();
            creation = row.CreationDate;
            firstLogin = row.LastLoginDate!.Value;
        }

        var second = (await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None)).Id;

        Assert.Equal(first, second);
        using var after = new PreferencesTestDbContext(db);
        var updated = after.Users.Single();
        Assert.Equal(creation, updated.CreationDate);
        Assert.True(updated.LastLoginDate >= firstLogin);
    }

    [Fact]
    public async Task RegisterLogin_CanonicalisesEmail()
    {
        var db = nameof(RegisterLogin_CanonicalisesEmail);
        await CreateStore(db).RegisterLoginAsync("  Mick@WEESKY.be ", CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Equal("mick@weesky.be", ctx.Users.Single().Email);
    }

    [Fact]
    public async Task DeleteByEmail_RemovesTheRow()
    {
        var db = nameof(DeleteByEmail_RemovesTheRow);
        await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        await CreateStore(db).DeleteByEmailAsync("mick@weesky.be", CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.Users);
    }

    [Fact]
    public async Task DeleteByEmail_WhenAbsent_IsANoOp()
    {
        var db = nameof(DeleteByEmail_WhenAbsent_IsANoOp);

        await CreateStore(db).DeleteByEmailAsync("nobody@weesky.be", CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.Users);
    }

    [Fact]
    public async Task RegisterLogin_WhenAbsent_DrawsASecurityStamp()
    {
        var db = nameof(RegisterLogin_WhenAbsent_DrawsASecurityStamp);

        var account = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        Assert.NotEqual(Guid.Empty, account.SecurityStamp);
        using var ctx = new PreferencesTestDbContext(db);
        Assert.Equal(account.SecurityStamp, ctx.Users.Single().SecurityStamp);
    }

    // Logging in must not revoke the sessions already open on other devices.
    [Fact]
    public async Task RegisterLogin_WhenPresent_KeepsTheSecurityStamp()
    {
        var db = nameof(RegisterLogin_WhenPresent_KeepsTheSecurityStamp);
        var first = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        var second = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        Assert.Equal(first.SecurityStamp, second.SecurityStamp);
    }

    [Fact]
    public async Task RotateSecurityStamp_ReplacesTheStoredValueAndReturnsIt()
    {
        var db = nameof(RotateSecurityStamp_ReplacesTheStoredValueAndReturnsIt);
        var before = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        var rotated = await CreateStore(db).RotateSecurityStampAsync("mick@weesky.be", CancellationToken.None);

        Assert.NotEqual(before.SecurityStamp, rotated);
        using var ctx = new PreferencesTestDbContext(db);
        Assert.Equal(rotated, ctx.Users.Single().SecurityStamp);
    }

    [Fact]
    public async Task RotateSecurityStamp_OnAnUnknownAccount_AnswersAValueThatMatchesNothing()
    {
        var db = nameof(RotateSecurityStamp_OnAnUnknownAccount_AnswersAValueThatMatchesNothing);

        var rotated = await CreateStore(db).RotateSecurityStampAsync("ghost@weesky.be", CancellationToken.None);

        Assert.NotEqual(Guid.Empty, rotated);
        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.Users);
    }

    [Fact]
    public async Task FindByEmail_IsCanonicalisedLikeTheRest()
    {
        var db = nameof(FindByEmail_IsCanonicalisedLikeTheRest);
        var registered = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        var found = await CreateStore(db).FindByEmailAsync("  Mick@WEESKY.be ", CancellationToken.None);

        Assert.Equal(registered, found);
    }

    [Fact]
    public async Task FindByEmail_WhenAbsent_AnswersNull()
    {
        var db = nameof(FindByEmail_WhenAbsent_AnswersNull);

        Assert.Null(await CreateStore(db).FindByEmailAsync("ghost@weesky.be", CancellationToken.None));
    }

    // Every connected-account cipher hangs off the key this salt derives: a second value would
    // leave the first login's ciphers undecryptable for good.
    [Fact]
    public async Task GetOrCreateKdfSaltAsync_GeneratesOnceAndReturnsTheSameSaltAfter()
    {
        var db = nameof(GetOrCreateKdfSaltAsync_GeneratesOnceAndReturnsTheSameSaltAfter);
        await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        var first = await CreateStore(db).GetOrCreateKdfSaltAsync("mick@weesky.be", CancellationToken.None);
        var second = await CreateStore(db).GetOrCreateKdfSaltAsync("  Mick@WEESKY.be ", CancellationToken.None);

        Assert.Equal(16, first.Length);
        Assert.Equal<byte[]>(first, second);
        using var ctx = new PreferencesTestDbContext(db);
        Assert.Equal<byte[]>(first, ctx.Users.Single().KdfSalt!);
    }

    [Fact]
    public async Task RotateSecurityStamp_DestroysTheSynchronisationSecret()
    {
        // A gesture of distrust destroys; switching off is the gesture of comfort, and it keeps.
        // Leaving the secret alive would make "sign out everywhere" leave the whole address book
        // readable and writable to whoever holds it.
        var db = nameof(RotateSecurityStamp_DestroysTheSynchronisationSecret);
        var account = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);
        using (var seed = new PreferencesTestDbContext(db))
        {
            seed.DavCredentials.Add(new DavCredential
            {
                UserId = account.Id, SecretHash = new string('a', 64),
                Salt = new byte[16], CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        await CreateStore(db).RotateSecurityStampAsync("mick@weesky.be", CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.DavCredentials);
    }

    [Fact]
    public async Task RotateSecurityStamp_ForgetsTheCachedSynchronisationIdentity()
    {
        var db = nameof(RotateSecurityStamp_ForgetsTheCachedSynchronisationIdentity);
        var cache = new DavAuthenticationCache(Clock);
        var account = await CreateStore(db, cache).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);
        cache.Store("mick@weesky.be", "fingerprint", new DavIdentity(account.Id, true),
            cache.Generation("mick@weesky.be"));

        await CreateStore(db, cache).RotateSecurityStampAsync("  Mick@WEESKY.be ", CancellationToken.None);

        Assert.False(cache.TryGet("mick@weesky.be", "fingerprint", out _));
    }

    [Fact]
    public async Task DeleteByEmail_ForgetsTheCachedSynchronisationIdentity()
    {
        // The cascade takes the credential row; without this the burst entry would keep the
        // deleted account's secret opening the address book for the rest of the window.
        var db = nameof(DeleteByEmail_ForgetsTheCachedSynchronisationIdentity);
        var cache = new DavAuthenticationCache(Clock);
        var account = await CreateStore(db, cache).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);
        cache.Store("mick@weesky.be", "fingerprint", new DavIdentity(account.Id, true),
            cache.Generation("mick@weesky.be"));

        await CreateStore(db, cache).DeleteByEmailAsync("  Mick@WEESKY.be ", CancellationToken.None);

        Assert.False(cache.TryGet("mick@weesky.be", "fingerprint", out _));
    }
}
