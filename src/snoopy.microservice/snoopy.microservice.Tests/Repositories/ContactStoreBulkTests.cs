using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreBulkTests
{
    private static ContactStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName), ContactStoreTestFactory.NewSync().Object);

    private static ContactWrite Write(string first, string address, bool favorite = false) =>
        new(first, null, null, null, null, null, null, null, null, null, null, null, null,
            favorite, [new ContactWriteEmail(null, address, string.Empty)], [], [], "manual");

    [Fact]
    public async Task DeleteManyAsync_RemovesEveryContactAndItsAddresses()
    {
        var db = nameof(DeleteManyAsync_RemovesEveryContactAndItsAddresses);
        var user = Guid.NewGuid();
        var first = (await CreateStore(db).CreateAsync(user, Write("Alice", "alice@x.example"), CancellationToken.None)).Value;
        var second = (await CreateStore(db).CreateAsync(user, Write("Bob", "bob@x.example"), CancellationToken.None)).Value;

        var removed = await CreateStore(db).DeleteManyAsync(user, [first, second], includeGroups: false, CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Empty(new PreferencesTestDbContext(db).ContactEmails);
    }

    // Un lot ne peut pas échouer à moitié : l'id absent est ignoré, les autres partent.
    [Fact]
    public async Task DeleteManyAsync_IgnoresAnUnknownIdAndDeletesTheRest()
    {
        var db = nameof(DeleteManyAsync_IgnoresAnUnknownIdAndDeletesTheRest);
        var user = Guid.NewGuid();
        var kept = (await CreateStore(db).CreateAsync(user, Write("Alice", "alice@x.example"), CancellationToken.None)).Value;

        var removed = await CreateStore(db).DeleteManyAsync(user, [kept, Guid.NewGuid()], includeGroups: false, CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    // Le scope par utilisateur est la seule barrière : un id d'autrui ne résout rien.
    [Fact]
    public async Task DeleteManyAsync_LeavesAnotherUsersContactAlone()
    {
        var db = nameof(DeleteManyAsync_LeavesAnotherUsersContactAlone);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var foreign = (await CreateStore(db).CreateAsync(theirs, Write("Bob", "bob@x.example"), CancellationToken.None)).Value;

        var removed = await CreateStore(db).DeleteManyAsync(mine, [foreign], includeGroups: false, CancellationToken.None);

        Assert.Equal(0, removed);
        Assert.Single(await CreateStore(db).ListAsync(theirs, CancellationToken.None));
    }

    [Fact]
    public async Task SetFavoriteManyAsync_FlagsEveryContactItFinds()
    {
        var db = nameof(SetFavoriteManyAsync_FlagsEveryContactItFinds);
        var user = Guid.NewGuid();
        var first = (await CreateStore(db).CreateAsync(user, Write("Alice", "alice@x.example"), CancellationToken.None)).Value;
        var second = (await CreateStore(db).CreateAsync(user, Write("Bob", "bob@x.example"), CancellationToken.None)).Value;

        var touched = await CreateStore(db).SetFavoriteManyAsync(user, [first, second, Guid.NewGuid()], true, CancellationToken.None);

        Assert.Equal(2, touched);
        Assert.All(await CreateStore(db).ListAsync(user, CancellationToken.None), c => Assert.True(c.IsFavorite));
    }

    [Fact]
    public async Task SetFavoriteManyAsync_ClearsTheFlagToo()
    {
        var db = nameof(SetFavoriteManyAsync_ClearsTheFlagToo);
        var user = Guid.NewGuid();
        var id = (await CreateStore(db).CreateAsync(user, Write("Alice", "alice@x.example", favorite: true), CancellationToken.None)).Value;

        await CreateStore(db).SetFavoriteManyAsync(user, [id], false, CancellationToken.None);

        Assert.False((await CreateStore(db).ListAsync(user, CancellationToken.None)).Single().IsFavorite);
    }
}
