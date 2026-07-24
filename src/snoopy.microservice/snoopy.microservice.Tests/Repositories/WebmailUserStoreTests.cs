using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class WebmailUserStoreTests
{
    private static WebmailUserStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task RegisterLogin_WhenAbsent_CreatesRowWithGuidAndStamps()
    {
        var store = CreateStore(nameof(RegisterLogin_WhenAbsent_CreatesRowWithGuidAndStamps));

        var id = await store.RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

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
        var first = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);
        DateTime creation, firstLogin;
        using (var ctx = new PreferencesTestDbContext(db))
        {
            var row = ctx.Users.Single();
            creation = row.CreationDate;
            firstLogin = row.LastLoginDate!.Value;
        }

        var second = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

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
}
