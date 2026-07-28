using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreTests
{
    private static ContactStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    private static ContactWrite Write(
        string? first = "Bruno", string? last = "Mertens", string? nick = null,
        bool favorite = false, params string[] addresses) =>
        new(first, last, nick, favorite, addresses, "manual");

    [Fact]
    public async Task Create_ThenList_ReturnsTheContact()
    {
        var db = nameof(Create_ThenList_ReturnsTheContact);
        var user = Guid.NewGuid();

        var created = await CreateStore(db)
            .CreateAsync(user, Write(addresses: "bruno@example.com"), CancellationToken.None);

        Assert.True(created.IsSuccess);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(created.Value, stored.Id);
        Assert.Equal("Bruno", stored.FirstName);
        Assert.Equal("bruno@example.com", Assert.Single(stored.Addresses));
    }

    // The table collates binary, so folding on the way in is the only thing stopping one address
    // from becoming two rows the client can never reconcile.
    [Fact]
    public async Task Create_FoldsAddressCaseAndSpace()
    {
        var db = nameof(Create_FoldsAddressCaseAndSpace);
        var user = Guid.NewGuid();

        await CreateStore(db).CreateAsync(user, Write(addresses: " Bruno@Example.COM "), CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("bruno@example.com", Assert.Single(stored.Addresses));
    }

    [Fact]
    public async Task Create_KeepsAddressOrder_PositionZeroIsPrimary()
    {
        var db = nameof(Create_KeepsAddressOrder_PositionZeroIsPrimary);
        var user = Guid.NewGuid();

        await CreateStore(db).CreateAsync(
            user, Write(addresses: ["second@example.com", "first@example.com"]), CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(["second@example.com", "first@example.com"], stored.Addresses);
    }

    // Two rows differing only by case fold onto one address. Left as two, the composite key would
    // throw; resequencing after the fold is what keeps position 0 unambiguous.
    [Fact]
    public async Task Create_DedupesAddressesThatFoldTogether()
    {
        var db = nameof(Create_DedupesAddressesThatFoldTogether);
        var user = Guid.NewGuid();

        var created = await CreateStore(db).CreateAsync(
            user, Write(addresses: ["Bruno@example.com", "bruno@example.com", "other@example.com"]),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(["bruno@example.com", "other@example.com"], stored.Addresses);
        Assert.Equal([0, 1], new PreferencesTestDbContext(db).ContactEmails
            .Where(e => e.ContactId == created.Value)
            .OrderBy(e => e.Position)
            .Select(e => e.Position));
    }

    [Fact]
    public async Task Create_SetsUidToTheGeneratedId()
    {
        var db = nameof(Create_SetsUidToTheGeneratedId);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);

        var created = await new ContactStore(context).CreateAsync(user, Write(), CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal(created.Value.ToString(), row.Uid);
    }

    [Fact]
    public async Task Create_LeavesVCardRawNull()
    {
        var db = nameof(Create_LeavesVCardRawNull);

        await CreateStore(db).CreateAsync(Guid.NewGuid(), Write(), CancellationToken.None);

        Assert.Null(Assert.Single(new PreferencesTestDbContext(db).Contacts).VCardRaw);
    }

    // Same address on two contacts is allowed by decision: shared mailboxes are real. Nothing in
    // the schema or the store may refuse it.
    [Fact]
    public async Task Create_AllowsTheSameAddressOnTwoContacts()
    {
        var db = nameof(Create_AllowsTheSameAddressOnTwoContacts);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "Alice", addresses: "info@example.com"),
            CancellationToken.None);

        var second = await CreateStore(db).CreateAsync(
            user, Write(first: "Compta", addresses: "info@example.com"), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(2, (await CreateStore(db).ListAsync(user, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task List_IsScopedToItsUser()
    {
        var db = nameof(List_IsScopedToItsUser);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await CreateStore(db).CreateAsync(mine, Write(first: "Mine"), CancellationToken.None);
        await CreateStore(db).CreateAsync(theirs, Write(first: "Theirs"), CancellationToken.None);

        var listed = await CreateStore(db).ListAsync(mine, CancellationToken.None);

        Assert.Equal("Mine", Assert.Single(listed).FirstName);
    }

    // The grouping, not merely the join. Handing every contact the whole user's address list is
    // invisible to every single-contact fixture above, and this is the module's hottest read: the
    // tile, the card and the composer's autocomplete all take their addresses from here. The
    // shared address rides along on purpose — each holder must see it without inheriting the
    // other's private ones.
    [Fact]
    public async Task List_GivesEachContactItsOwnAddressesOnly()
    {
        var db = nameof(List_GivesEachContactItsOwnAddressesOnly);
        var user = Guid.NewGuid();
        var alice = await CreateStore(db).CreateAsync(
            user, Write(first: "Alice", addresses: ["alice@example.com", "info@example.com"]),
            CancellationToken.None);
        var compta = await CreateStore(db).CreateAsync(
            user, Write(first: "Compta", addresses: ["compta@example.com", "info@example.com"]),
            CancellationToken.None);

        var listed = await CreateStore(db).ListAsync(user, CancellationToken.None);

        Assert.Equal(2, listed.Count);
        Assert.Equal(["alice@example.com", "info@example.com"],
            Assert.Single(listed, c => c.Id == alice.Value).Addresses);
        Assert.Equal(["compta@example.com", "info@example.com"],
            Assert.Single(listed, c => c.Id == compta.Value).Addresses);
    }

    [Fact]
    public async Task List_WithNoContacts_IsEmptyNotNull()
    {
        var listed = await CreateStore(nameof(List_WithNoContacts_IsEmptyNotNull))
            .ListAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(listed);
    }

    // Counted only on the branch that adds a row, and it is what bounds the payload the whole
    // book becomes in the browser.
    [Fact]
    public async Task Create_AtTheCap_IsRefused()
    {
        var db = nameof(Create_AtTheCap_IsRefused);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        for (var i = 0; i < ContactStore.MaxPerUser; i++)
        {
            var id = Guid.NewGuid();
            context.Contacts.Add(new Contact
            {
                Id = id, UserId = user, Uid = id.ToString(), FirstName = $"C{i}",
                UpdatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync(CancellationToken.None);

        var result = await new ContactStore(new PreferencesTestDbContext(db))
            .CreateAsync(user, Write(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContactStore.CapReached, result.Error);
    }

    private static async Task<Guid> Seed(string db, Guid user, params string[] addresses) =>
        (await CreateStore(db).CreateAsync(user, Write(addresses: addresses), CancellationToken.None)).Value;

    [Fact]
    public async Task Update_ReplacesNamesAndAddresses()
    {
        var db = nameof(Update_ReplacesNamesAndAddresses);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "old@example.com");

        var result = await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Chloé", "Vermeulen", "chlo", true, ["new@example.com"], "manual"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("Chloé", stored.FirstName);
        Assert.True(stored.IsFavorite);
        Assert.Equal("new@example.com", Assert.Single(stored.Addresses));
    }

    // Replace, not merge: the editor sends the list it shows, so an address the user removed has
    // to disappear. Merging would make removal impossible from the only screen that offers it.
    [Fact]
    public async Task Update_DropsAddressesNoLongerListed()
    {
        var db = nameof(Update_DropsAddressesNoLongerListed);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "a@example.com", "b@example.com");

        await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Bruno", null, null, false, ["b@example.com"], "manual"), CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("b@example.com", Assert.Single(stored.Addresses));
    }

    [Fact]
    public async Task Update_ReorderingChangesThePrimary()
    {
        var db = nameof(Update_ReorderingChangesThePrimary);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "a@example.com", "b@example.com");

        await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Bruno", null, null, false, ["b@example.com", "a@example.com"], "manual"),
            CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(["b@example.com", "a@example.com"], stored.Addresses);
        Assert.Equal([0, 1], new PreferencesTestDbContext(db).ContactEmails
            .Where(e => e.ContactId == id)
            .OrderBy(e => e.Position)
            .Select(e => e.Position));
    }

    [Fact]
    public async Task Update_TouchesUpdatedAt()
    {
        var db = nameof(Update_TouchesUpdatedAt);
        var user = Guid.NewGuid();
        var id = await Seed(db, user);

        var seedContext = new PreferencesTestDbContext(db);
        var seeded = Assert.Single(seedContext.Contacts);
        seeded.UpdatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await seedContext.SaveChangesAsync(CancellationToken.None);
        var before = seeded.UpdatedAt;

        await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Bruno", null, null, false, [], "manual"), CancellationToken.None);

        Assert.True(Assert.Single(new PreferencesTestDbContext(db).Contacts).UpdatedAt > before);
    }

    // The uid must survive an edit: it is the identity a CardDAV client syncs on, and rewriting
    // it would duplicate the card on that client's next pass.
    [Fact]
    public async Task Update_LeavesUidAlone()
    {
        var db = nameof(Update_LeavesUidAlone);
        var user = Guid.NewGuid();
        var id = await Seed(db, user);
        var before = Assert.Single(new PreferencesTestDbContext(db).Contacts).Uid;

        await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Bruno", null, null, false, [], "manual"), CancellationToken.None);

        Assert.Equal(before, Assert.Single(new PreferencesTestDbContext(db).Contacts).Uid);
    }

    [Fact]
    public async Task Update_AnotherUsersContact_IsNotFound()
    {
        var db = nameof(Update_AnotherUsersContact_IsNotFound);
        var id = await Seed(db, Guid.NewGuid());

        var result = await CreateStore(db).UpdateAsync(Guid.NewGuid(), id,
            new ContactWrite("Hijack", null, null, false, [], "manual"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContactStore.NotFound, result.Error);
    }

    [Fact]
    public async Task Delete_RemovesTheContactAndItsAddresses()
    {
        var db = nameof(Delete_RemovesTheContactAndItsAddresses);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "a@example.com", "b@example.com");

        var result = await CreateStore(db).DeleteAsync(user, id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Empty(new PreferencesTestDbContext(db).ContactEmails);
    }

    [Fact]
    public async Task Delete_AnUnknownId_IsNotFound()
    {
        var result = await CreateStore(nameof(Delete_AnUnknownId_IsNotFound))
            .DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContactStore.NotFound, result.Error);
    }

    [Fact]
    public async Task SetFavorite_FlipsTheFlagAndNothingElse()
    {
        var db = nameof(SetFavorite_FlipsTheFlagAndNothingElse);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "a@example.com");

        var result = await CreateStore(db).SetFavoriteAsync(user, id, true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.True(stored.IsFavorite);
        Assert.Equal("Bruno", stored.FirstName);
        Assert.Equal("a@example.com", Assert.Single(stored.Addresses));
    }

    [Fact]
    public async Task SetFavorite_AnotherUsersContact_IsNotFound()
    {
        var db = nameof(SetFavorite_AnotherUsersContact_IsNotFound);
        var id = await Seed(db, Guid.NewGuid());

        var result = await CreateStore(db)
            .SetFavoriteAsync(Guid.NewGuid(), id, true, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContactStore.NotFound, result.Error);
    }

    [Fact]
    public async Task Create_StoresTheSource()
    {
        var db = nameof(Create_StoresTheSource);
        var user = Guid.NewGuid();

        var created = await CreateStore(db).CreateAsync(
            user, new ContactWrite("Alice", null, null, false, ["alice@x.be"], "captured"),
            CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal("captured", row.Source);
    }

    // The whole point of the column: editing a captured card must not make it pass for a manual one.
    [Fact]
    public async Task Update_LeavesTheSourceIntact()
    {
        var db = nameof(Update_LeavesTheSourceIntact);
        var user = Guid.NewGuid();
        var created = await CreateStore(db).CreateAsync(
            user, new ContactWrite("Alice", null, null, false, ["alice@x.be"], "captured"),
            CancellationToken.None);

        await CreateStore(db).UpdateAsync(
            user, created.Value,
            new ContactWrite("Alice", "Dupont", null, false, ["alice@x.be"], "manual"),
            CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal("captured", row.Source);
        Assert.Equal("Dupont", row.LastName);
    }
}
