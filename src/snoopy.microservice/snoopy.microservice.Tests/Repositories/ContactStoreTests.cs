using System.Security.Cryptography;
using System.Text;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreTests
{
    private static ContactStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName), ContactStoreTestFactory.NewSync().Object);

    private static ContactWrite Write(
        string? first = "Bruno", string? last = "Mertens", string? nick = null,
        bool favorite = false, string source = "manual", string? notes = null,
        IReadOnlyList<ContactWriteEmail>? emails = null,
        IReadOnlyList<ContactWritePhone>? phones = null,
        IReadOnlyList<ContactWriteAddress>? postal = null,
        params string[] addresses) =>
        new(first, last, nick, null, null, null, null, null, null, null, null, null, notes,
            favorite, emails ?? [.. addresses.Select(a => new ContactWriteEmail(null, a, string.Empty))],
            phones, postal, source);

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

    // Nothing to name the contact but its address: the 3.0 writer fills the mandatory N with a
    // question mark, and a total projection would store it as the surname.
    [Fact]
    public async Task Create_ContactWithNoName_LeavesTheNameColumnsNull()
    {
        var db = nameof(Create_ContactWithNoName_LeavesTheNameColumnsNull);
        var user = Guid.NewGuid();

        var created = await CreateStore(db).CreateAsync(
            user, Write(first: null, last: null, addresses: "bruno@example.com"), CancellationToken.None);

        Assert.True(created.IsSuccess);
        var row = new PreferencesTestDbContext(db).Contacts.Single(c => c.Id == created.Value);
        Assert.Null(row.LastName);
        Assert.Null(row.FirstName);
        // The FN of a nameless card is that card's own address, which is the writer computing a
        // display name rather than the user choosing one: the card keeps it, the column does not.
        Assert.Contains("FN:bruno@example.com", row.VCardRaw!);
        Assert.Null(row.DisplayName);
        Assert.DoesNotContain('?', row.VCardRaw!);
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

    // Two rows differing only by case fold onto one address in the list. They stay two rows in
    // the table: position is the rank of the EMAIL on the card now, and dropping one would make a
    // bulk edit destroy the second property — the exception decision 4 forbids.
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
        Assert.Equal([0, 1, 2], new PreferencesTestDbContext(db).ContactEmails
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

        var created = await new ContactStore(context, ContactStoreTestFactory.NewSync().Object)
            .CreateAsync(user, Write(), CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal(created.Value.ToString(), row.Uid);
    }

    // Was Create_LeavesVCardRawNull until decision 1 made the card sovereign: every contact has
    // one, a name-only one included, or the first import of the next day breaks the invariant.
    [Fact]
    public async Task Create_GivesEvenANameOnlyContactACard()
    {
        var db = nameof(Create_GivesEvenANameOnlyContactACard);

        await CreateStore(db).CreateAsync(Guid.NewGuid(), Write(), CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Contains("BEGIN:VCARD", row.VCardRaw!);
        Assert.Contains($"UID:{row.Uid}", row.VCardRaw!);
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

        var result = await new ContactStore(new PreferencesTestDbContext(db), ContactStoreTestFactory.NewSync().Object)
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
            Write(first: "Chloé", last: "Vermeulen", nick: "chlo", favorite: true, addresses: "new@example.com"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("Chloé", stored.FirstName);
        Assert.True(stored.IsFavorite);
        Assert.Equal("new@example.com", Assert.Single(stored.Addresses));
    }

    // Absent n'est pas vide : un PUT qui ne nomme ni les téléphones ni l'anniversaire les conserve.
    // C'est le seul écran qui écrit, et il n'en montre aucun — les effacer serait détruire ce que
    // l'utilisateur n'a jamais vu.
    [Fact]
    public async Task Update_WithoutPhonesOrBirthday_KeepsThem()
    {
        var db = nameof(Update_WithoutPhonesOrBirthday_KeepsThem);
        var user = Guid.NewGuid();
        var seeded = await CreateStore(db).CreateAsync(user, Write(
            phones: [new ContactWritePhone(null, "+32470000000", "CELL")],
            addresses: "bruno@example.com") with { Birthday = "1993-06-21" }, CancellationToken.None);

        var result = await CreateStore(db).UpdateAsync(user, seeded.Value,
            Write(first: "Bruno", last: "Mertens", phones: null, addresses: "bruno@example.com"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var detail = await CreateStore(db).GetAsync(user, seeded.Value, CancellationToken.None);
        Assert.Equal("+32470000000", Assert.Single(detail!.Phones).Number);
        Assert.Equal("1993-06-21", detail.Birthday);
    }

    // L'autre moitié de la règle, sans laquelle « conserver » deviendrait « impossible à effacer ».
    [Fact]
    public async Task Update_WithAnEmptyPhoneList_ClearsThePhones()
    {
        var db = nameof(Update_WithAnEmptyPhoneList_ClearsThePhones);
        var user = Guid.NewGuid();
        var seeded = await CreateStore(db).CreateAsync(user, Write(
            phones: [new ContactWritePhone(null, "+32470000000", "CELL")],
            addresses: "bruno@example.com"), CancellationToken.None);

        await CreateStore(db).UpdateAsync(user, seeded.Value,
            Write(phones: [], addresses: "bruno@example.com"), CancellationToken.None);

        var detail = await CreateStore(db).GetAsync(user, seeded.Value, CancellationToken.None);
        Assert.Empty(detail!.Phones);
    }

    // ORG est la seule propriété dont deux champs partagent une ligne de carte : nommer une moitié
    // sans relire l'autre la détruirait, la destruction même que la règle « absent = conservé » ferme.
    [Fact]
    public async Task Update_NamingOneHalfOfTheOrganization_KeepsTheOther()
    {
        var db = nameof(Update_NamingOneHalfOfTheOrganization_KeepsTheOther);
        var user = Guid.NewGuid();
        var seeded = await CreateStore(db).CreateAsync(user,
            Write(addresses: "bruno@example.com") with { Organization = "Acme", Department = "R&D" },
            CancellationToken.None);

        await CreateStore(db).UpdateAsync(user, seeded.Value,
            Write(addresses: "bruno@example.com") with { Organization = "Globex" }, CancellationToken.None);

        var detail = await CreateStore(db).GetAsync(user, seeded.Value, CancellationToken.None);
        Assert.Equal("Globex", detail!.Organization);
        Assert.Equal("R&D", detail.Department);

        await CreateStore(db).UpdateAsync(user, seeded.Value,
            Write(addresses: "bruno@example.com") with { Department = "Legal" }, CancellationToken.None);

        detail = await CreateStore(db).GetAsync(user, seeded.Value, CancellationToken.None);
        Assert.Equal("Globex", detail!.Organization);
        Assert.Equal("Legal", detail.Department);
    }

    // L'autre moitié de la règle pour les scalaires : la chaîne vide efface. Elle n'atteignait
    // aucun de ces trois chemins avant que « absent = conservé » ne remplace le pliage de Blank.
    [Fact]
    public async Task Update_WithEmptyScalars_ClearsThem()
    {
        var db = nameof(Update_WithEmptyScalars_ClearsThem);
        var user = Guid.NewGuid();
        var seeded = await CreateStore(db).CreateAsync(user,
            Write(notes: "à rappeler", addresses: "bruno@example.com")
                with { Organization = "Acme", Department = "R&D", Birthday = "1993-06-21" },
            CancellationToken.None);

        await CreateStore(db).UpdateAsync(user, seeded.Value,
            Write(addresses: "bruno@example.com")
                with { Organization = string.Empty, Department = string.Empty,
                       Notes = string.Empty, Birthday = string.Empty },
            CancellationToken.None);

        var detail = await CreateStore(db).GetAsync(user, seeded.Value, CancellationToken.None);
        Assert.Null(detail!.Organization);
        Assert.Null(detail.Department);
        Assert.Null(detail.Notes);
        Assert.Null(detail.Birthday);
        Assert.DoesNotContain("ORG", new PreferencesTestDbContext(db).Contacts
            .Single(c => c.Id == seeded.Value).VCardRaw!, StringComparison.Ordinal);
    }

    // Effacer une seule moitié laisse la ligne, avec l'autre moitié intacte.
    [Fact]
    public async Task Update_ClearingOneHalfOfTheOrganization_KeepsTheLine()
    {
        var db = nameof(Update_ClearingOneHalfOfTheOrganization_KeepsTheLine);
        var user = Guid.NewGuid();
        var seeded = await CreateStore(db).CreateAsync(user,
            Write(addresses: "bruno@example.com") with { Organization = "Acme", Department = "R&D" },
            CancellationToken.None);

        await CreateStore(db).UpdateAsync(user, seeded.Value,
            Write(addresses: "bruno@example.com") with { Department = string.Empty },
            CancellationToken.None);

        var detail = await CreateStore(db).GetAsync(user, seeded.Value, CancellationToken.None);
        Assert.Equal("Acme", detail!.Organization);
        Assert.Null(detail.Department);
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
            Write(first: "Bruno", last: null, nick: null, addresses: "b@example.com"), CancellationToken.None);

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
            Write(first: "Bruno", last: null, nick: null, addresses: ["b@example.com", "a@example.com"]),
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
            Write(first: "Bruno", last: null, nick: null), CancellationToken.None);

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
            Write(first: "Bruno", last: null, nick: null), CancellationToken.None);

        Assert.Equal(before, Assert.Single(new PreferencesTestDbContext(db).Contacts).Uid);
    }

    [Fact]
    public async Task Update_AnotherUsersContact_IsNotFound()
    {
        var db = nameof(Update_AnotherUsersContact_IsNotFound);
        var id = await Seed(db, Guid.NewGuid());

        var result = await CreateStore(db).UpdateAsync(Guid.NewGuid(), id,
            Write(first: "Hijack", last: null, nick: null), CancellationToken.None);

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
            user, Write(first: "Alice", last: null, nick: null, source: "captured", addresses: "alice@x.be"),
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
            user, Write(first: "Alice", last: null, nick: null, source: "captured", addresses: "alice@x.be"),
            CancellationToken.None);

        await CreateStore(db).UpdateAsync(
            user, created.Value,
            Write(first: "Alice", last: "Dupont", nick: null, addresses: "alice@x.be"),
            CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal("captured", row.Source);
        Assert.Equal("Dupont", row.LastName);
    }

    // ---- the cycle: compose, hash, project (décisions 1, 2, 3, 9) --------------------------------

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0x04];

    [Fact] // the whole cycle: create -> the card exists, so does the hash, the columns are its projection
    public async Task Create_ComposesHashesAndProjects()
    {
        var db = nameof(Create_ComposesHashesAndProjects);
        var user = Guid.NewGuid();

        var id = await CreateStore(db).CreateAsync(
            user,
            Write(first: "Ana", last: null, nick: null, phones: [new(null, "+321", "CELL")],
                addresses: "ana@example.com"),
            CancellationToken.None);

        var context = new PreferencesTestDbContext(db);
        var row = context.Contacts.Single(c => c.Id == id.Value);
        Assert.Contains("BEGIN:VCARD", row.VCardRaw!);
        Assert.Equal(64, row.CardHash.Length);
        var phone = Assert.Single(context.ContactPhones.Where(p => p.ContactId == id.Value));
        Assert.Equal("+321", phone.Number);
        Assert.Equal("CELL", phone.Type);
        // FN as the card carries it, projected back — not copied across from the write. It is
        // the first name alone here, so the projection has nothing to keep: `display_name` holds
        // a display name the user chose, and this one the writer computed.
        Assert.Contains("FN:Ana", row.VCardRaw!);
        Assert.Null(row.DisplayName);
        Assert.Equal("Ana", row.FirstName);
        Assert.Equal("ana@example.com", Assert.Single(context.ContactEmails.Where(e => e.ContactId == id.Value)).Address);
    }

    [Fact] // décision 3: the projection is total — an update wipes the child rows and rewrites them
    public async Task Update_RewritesChildrenFromTheCard()
    {
        var db = nameof(Update_RewritesChildrenFromTheCard);
        var user = Guid.NewGuid();
        var id = (await CreateStore(db).CreateAsync(
            user,
            Write(phones: [new(null, "+1", "CELL"), new(null, "+2", "WORK")],
                postal: [new(null, "HOME", null, null, "Rue Haute 1", "Bruxelles", null, "1000", "BE")]),
            CancellationToken.None)).Value;

        var saved = await CreateStore(db).UpdateAsync(
            user, id, Write(phones: [new(0, "+9", "HOME")]), CancellationToken.None);

        Assert.True(saved.IsSuccess);
        var context = new PreferencesTestDbContext(db);
        var phone = Assert.Single(context.ContactPhones.Where(p => p.ContactId == id));
        Assert.Equal("+9", phone.Number);
        Assert.Equal("HOME", phone.Type);
        Assert.Equal(0, phone.Position);
        // Les téléphones sont remplacés en bloc parce que l'écriture les nomme ; l'adresse postale,
        // qu'elle ne nomme pas, survit. La projection reste totale : elle relit la carte gardée.
        Assert.Single(context.ContactAddresses.Where(a => a.ContactId == id));
        Assert.DoesNotContain("+2", context.Contacts.Single(c => c.Id == id).VCardRaw!);
    }

    private const string StaleRev = "REV:2020-01-01T00:00:00Z";

    // Ages the stored REV. The library stamps it to the second, so a card re-composed within the
    // same second is byte-equal by accident and would pass whether or not the store ignores REV.
    private static string WithStaleRev(string card) =>
        string.Join("\r\n", card.Split("\r\n").Select(l => l.StartsWith("REV:") ? StaleRev : l));

    [Theory] // décision 9: a write that changes nothing changes neither the card nor the ETag
    [InlineData(false)] // lines carrying their position — what 4b's editor will post
    [InlineData(true)]  // lines carrying none — what the live frontend still posts, so the
                        // composer drops the properties and re-appends them: the path most
                        // likely to break byte-equality
    public async Task Update_SameContentKeepsTheHash(bool withoutPositions)
    {
        var db = nameof(Update_SameContentKeepsTheHash) + withoutPositions;
        var user = Guid.NewGuid();
        var write = Write(phones: [new(null, "+1", "CELL")], addresses: "bruno@example.com");
        var id = (await CreateStore(db).CreateAsync(user, write, CancellationToken.None)).Value;

        var seed = new PreferencesTestDbContext(db);
        var seeded = seed.Contacts.Single(c => c.Id == id);
        Assert.Contains("REV:", seeded.VCardRaw!); // or ageing it proves nothing
        seeded.VCardRaw = WithStaleRev(seeded.VCardRaw!);
        seeded.CardHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seeded.VCardRaw)));
        await seed.SaveChangesAsync(CancellationToken.None);
        var before = seeded.VCardRaw;
        var hash = seeded.CardHash;

        var saved = await CreateStore(db).UpdateAsync(
            user, id,
            withoutPositions
                ? write
                : Write(emails: [new(0, "bruno@example.com", string.Empty)], phones: [new(0, "+1", "CELL")]),
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        var after = new PreferencesTestDbContext(db).Contacts.Single(c => c.Id == id);
        // The 2020 stamp survives, so the candidate — carrying today's REV — was recognised as
        // saying nothing new. Keeping the stored bytes is the whole of what keeps the ETag stable.
        Assert.Contains(StaleRev, after.VCardRaw!);
        Assert.Equal(before, after.VCardRaw);
        Assert.Equal(hash, after.CardHash);
    }

    [Fact] // the 1 MB ceiling is measured at every vcard_raw write, import or not (spec, § Limites)
    public async Task Update_RefusesACardOverOneMegabyte()
    {
        var db = nameof(Update_RefusesACardOverOneMegabyte);
        var user = Guid.NewGuid();
        var id = (await CreateStore(db).CreateAsync(user, Write(), CancellationToken.None)).Value;
        var before = new PreferencesTestDbContext(db).Contacts.Single(c => c.Id == id).VCardRaw;

        var result = await CreateStore(db).UpdateAsync(
            user, id, Write(notes: new string('x', 1_100_000)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContactStore.CardTooLarge, result.Error);
        Assert.Equal(before, new PreferencesTestDbContext(db).Contacts.Single(c => c.Id == id).VCardRaw);
    }

    [Fact] // the list: displayName + hasPhoto, addresses deduplicated, ordered on (pref, position)
    public async Task List_OrdersByPrefAndDeduplicates()
    {
        var db = nameof(List_OrdersByPrefAndDeduplicates);
        var user = Guid.NewGuid();
        var id = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        context.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), FirstName = "Ana",
            DisplayName = "Dr. Ana Ruiz", UpdatedAt = DateTime.UtcNow
        });
        context.ContactEmails.Add(new ContactEmail { ContactId = id, Position = 0, Address = "home@example.com" });
        context.ContactEmails.Add(new ContactEmail { ContactId = id, Position = 1, Address = "work@example.com", Pref = 1 });
        context.ContactEmails.Add(new ContactEmail { ContactId = id, Position = 2, Address = "work@example.com", Pref = 2 });
        context.ContactPhotos.Add(new ContactPhoto { ContactId = id, MediaType = "image/jpeg", Bytes = JpegBytes });
        await context.SaveChangesAsync(CancellationToken.None);

        var listed = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));

        Assert.Equal(["work@example.com", "home@example.com"], listed.Addresses);
        Assert.Equal("Dr. Ana Ruiz", listed.DisplayName);
        Assert.True(listed.HasPhoto);
    }

    [Fact] // the card: positions, type, pref, params, group; another user's id answers null
    public async Task Get_IsScopedByUser()
    {
        var db = nameof(Get_IsScopedByUser);
        var user = Guid.NewGuid();
        var id = (await CreateStore(db).CreateAsync(
            user,
            Write(first: "Ana", last: null, nick: null, phones: [new(null, "+321", "CELL")],
                postal: [new(null, "HOME", null, null, "Rue Haute 1", "Bruxelles", null, "1000", "BE")],
                addresses: "ana@example.com"),
            CancellationToken.None)).Value;

        var detail = await CreateStore(db).GetAsync(user, id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("Ana", detail.FirstName);
        Assert.False(detail.HasPhoto);
        Assert.Equal("ana@example.com", Assert.Single(detail.Addresses).Address);
        var phone = Assert.Single(detail.Phones);
        Assert.Equal(0, phone.Position);
        Assert.Equal("CELL", phone.Type);
        Assert.Equal(101, phone.Pref);
        Assert.Contains("CELL", phone.Params);
        Assert.Equal(string.Empty, phone.GroupName);
        Assert.Equal("Bruxelles", Assert.Single(detail.PostalAddresses).Locality);
        Assert.Null(await CreateStore(db).GetAsync(Guid.NewGuid(), id, CancellationToken.None));
    }

    [Fact] // the photo: Bytes + MediaType + CardHash, projected from the card; absent -> null
    public async Task GetPhoto_AnswersTheProjection()
    {
        var db = nameof(GetPhoto_AnswersTheProjection);
        var user = Guid.NewGuid();
        var id = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        context.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), FirstName = "Ana", UpdatedAt = DateTime.UtcNow,
            VCardRaw = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Ana\r\nN:;Ana;;;\r\nPHOTO;ENCODING=b;TYPE=JPEG:"
                + Convert.ToBase64String(JpegBytes) + "\r\nUID:" + id + "\r\nEND:VCARD\r\n"
        });
        var bare = Guid.NewGuid();
        context.Contacts.Add(new Contact { Id = bare, UserId = user, Uid = bare.ToString(), UpdatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync(CancellationToken.None);
        // The projection is what fills contact_photos, so the card goes through a write first.
        await CreateStore(db).UpdateAsync(user, id, Write(first: "Ana", last: null, nick: null), CancellationToken.None);

        var photo = await CreateStore(db).GetPhotoAsync(user, id, CancellationToken.None);

        Assert.NotNull(photo);
        Assert.Equal("image/jpeg", photo.Value.MediaType);
        Assert.Equal(JpegBytes, photo.Value.Bytes);
        Assert.Equal(new PreferencesTestDbContext(db).Contacts.Single(c => c.Id == id).CardHash, photo.Value.CardHash);
        Assert.Null(await CreateStore(db).GetPhotoAsync(user, bare, CancellationToken.None));
        Assert.Null(await CreateStore(db).GetPhotoAsync(Guid.NewGuid(), id, CancellationToken.None));
    }

    [Fact] // the export reads the same projected shape the card route does
    public async Task Export_AnswersEveryContactOfTheUserOnly()
    {
        var db = nameof(Export_AnswersEveryContactOfTheUserOnly);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Ana", last: null, nick: null, phones: [new(null, "+321", "CELL")],
                addresses: "ana@example.com"), CancellationToken.None);
        await CreateStore(db).CreateAsync(Guid.NewGuid(), Write(first: "Theirs"), CancellationToken.None);

        var exported = await CreateStore(db).ExportAsync(user, CancellationToken.None);

        var only = Assert.Single(exported);
        Assert.Equal("Ana", only.FirstName);
        Assert.Equal("+321", Assert.Single(only.Phones).Number);
        Assert.Equal("ana@example.com", Assert.Single(only.Addresses).Address);
    }

    // Every stored card carries a UID equal to its column, and the verbatim door is the one that
    // brings cards declaring none. Insertion is textual, so what it does not insert must not move.
    private const string CardWithoutUid =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Bruno\r\nX-ABUID:ABC-DEF\r\nEND:VCARD\r\n";

    [Fact]
    public void WithUid_InsertsTheColumnRightAfterVersion()
    {
        Assert.Equal(
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:card-9\r\nFN:Bruno\r\nX-ABUID:ABC-DEF\r\nEND:VCARD\r\n",
            ContactStore.WithUid(CardWithoutUid, "card-9"));
    }

    [Fact]
    public void WithUid_LeavesACardDeclaringAUidByteForByte()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:theirs\r\nFN:Bruno\r\nEND:VCARD\r\n";

        Assert.Equal(card, ContactStore.WithUid(card, "ours"));
    }

    [Fact]
    public void WithUid_ReadsAGroupPrefixedUidAsAUid()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nitem1.UID:theirs\r\nEND:VCARD\r\n";

        Assert.Equal(card, ContactStore.WithUid(card, "ours"));
    }

    [Fact]
    public void WithUid_DoesNotReadAUidOutOfAFoldedValue()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nNOTE:sent by\r\n UID:not-a-property\r\nEND:VCARD\r\n";

        Assert.Equal(
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:ours\r\nNOTE:sent by\r\n UID:not-a-property\r\nEND:VCARD\r\n",
            ContactStore.WithUid(card, "ours"));
    }

    // The other direction of the same rule: unfolding is what recognises a name a fold split, and
    // without it this card would take a second UID.
    [Fact]
    public void WithUid_ReadsAUidWhoseNameAFoldSplits()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nU\r\n ID:theirs\r\nEND:VCARD\r\n";

        Assert.Equal(card, ContactStore.WithUid(card, "ours"));
    }

    [Fact]
    public void WithUid_StopsAtTheFirstEndOfCard()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Bruno\r\nEND:VCARD\r\nUID:elsewhere\r\n";

        Assert.Equal(
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:ours\r\nFN:Bruno\r\nEND:VCARD\r\nUID:elsewhere\r\n",
            ContactStore.WithUid(card, "ours"));
    }

    [Fact]
    public void WithUid_KeepsTheCardsOwnLineEnding()
    {
        var inserted = ContactStore.WithUid("BEGIN:VCARD\nVERSION:3.0\nFN:Bruno\nEND:VCARD\n", "ours");

        Assert.Equal("BEGIN:VCARD\nVERSION:3.0\nUID:ours\nFN:Bruno\nEND:VCARD\n", inserted);
        Assert.DoesNotContain("\r", inserted);
        // A lone CR breaks a line for the parser too; an anchor ending the text has none to lend,
        // so the card's first break serves — CRLF only when the card carries no break at all.
        Assert.Equal("BEGIN:VCARD\rVERSION:3.0\rUID:ours\rEND:VCARD\r",
            ContactStore.WithUid("BEGIN:VCARD\rVERSION:3.0\rEND:VCARD\r", "ours"));
        Assert.Equal("BEGIN:VCARD\nVERSION:3.0\nUID:ours",
            ContactStore.WithUid("BEGIN:VCARD\nVERSION:3.0", "ours"));
        Assert.Equal("VERSION:3.0\r\nUID:ours", ContactStore.WithUid("VERSION:3.0", "ours"));
    }

    [Fact]
    public void WithUid_FallsBackToBeginWhenTheCardHasNoVersion()
    {
        Assert.Equal(
            "BEGIN:VCARD\r\nUID:ours\r\nFN:Bruno\r\nEND:VCARD\r\n",
            ContactStore.WithUid("BEGIN:VCARD\r\nFN:Bruno\r\nEND:VCARD\r\n", "ours"));
    }

    [Fact]
    public void WithUid_LeavesTextThatIsNoCardIntact()
    {
        Assert.Equal("FIRST", ContactStore.WithUid("FIRST", "ours"));
        Assert.Equal("NOTE:x\r\n", ContactStore.WithUid("NOTE:x\r\n", "ours"));
    }

    [Fact] // impossible by construction, guaranteed rather than assumed: one line, one property
    public void WithUid_StripsLineBreaksOutOfTheColumn()
    {
        Assert.Equal(
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:oursEND:VCARD\r\nFN:Bruno\r\nX-ABUID:ABC-DEF\r\nEND:VCARD\r\n",
            ContactStore.WithUid(CardWithoutUid, "ours\r\nEND:VCARD"));
    }

    [Fact] // the ETag rests on the hash: writing the same content twice must insert only once
    public void WithUid_IsIdempotent()
    {
        var once = ContactStore.WithUid(CardWithoutUid, "ours");

        Assert.Equal(once, ContactStore.WithUid(once, "ours"));
    }
}
