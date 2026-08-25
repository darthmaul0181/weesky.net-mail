using System.Security.Cryptography;
using System.Text;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreImportTests
{
    private static ContactStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName), ContactStoreTestFactory.NewSync().Object);

    private static ContactImportRow Row(
        int line = 2, string? first = null, string? last = null, string? nick = null,
        bool favorite = false, string? vcard = null, string? uid = null,
        ContactWrite? write = null, params string[] addresses) =>
        new(line, first, last, nick, favorite, addresses, vcard, uid, write);

    private static ContactWrite Write(
        string? first = null, string? last = null, string? nick = null,
        bool favorite = false, params string[] addresses) =>
        new(first, last, nick, null, null, null, null, null, null, null, null, null, null,
            favorite, [.. addresses.Select(a => new ContactWriteEmail(null, a, string.Empty))], [], [], "manual");

    private static ContactWrite Offer(
        string? organization = null, string? department = null,
        IReadOnlyList<ContactWritePhone>? phones = null,
        IReadOnlyList<ContactWriteAddress>? postalAddresses = null) =>
        new(null, null, null, null, null, null, null, organization, department, null, null, null,
            null, false, [], phones, postalAddresses, "imported");

    private static string Card(params string[] lines) =>
        "BEGIN:VCARD\r\nVERSION:3.0\r\n" + string.Concat(lines.Select(l => l + "\r\n")) + "END:VCARD\r\n";

    /// <summary>Fills the book to the cap and answers the first contact's id.</summary>
    private static Guid FillTheBook(PreferencesTestDbContext context, Guid user)
    {
        var first = Guid.NewGuid();
        for (var i = 0; i < ContactStore.MaxPerUser; i++)
            context.Contacts.Add(new Contact
            {
                Id = i == 0 ? first : Guid.NewGuid(), UserId = user,
                Uid = Guid.NewGuid().ToString(), FirstName = $"C{i}",
            });
        return first;
    }

    [Fact]
    public async Task Import_CreatesAnUnknownContact()
    {
        var db = nameof(Import_CreatesAnUnknownContact);
        var user = Guid.NewGuid();

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(first: "Bruno", addresses: "bruno@example.com")], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("Bruno", stored.FirstName);
        Assert.Equal("bruno@example.com", Assert.Single(stored.Addresses));
    }

    [Fact]
    public async Task Import_FilesACreatedContactAsImported()
    {
        var db = nameof(Import_FilesACreatedContactAsImported);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);

        await new ContactStore(context, ContactStoreTestFactory.NewSync().Object).ImportAsync(
            user, [Row(first: "Bruno", addresses: "bruno@example.com")], CancellationToken.None);

        Assert.Equal("imported", Assert.Single(new PreferencesTestDbContext(db).Contacts).Source);
    }

    // Nothing is ever overwritten: only the empty fields are filled in.
    [Fact]
    public async Task Import_MergesIntoTheContactHoldingTheAddress_WithoutOverwriting()
    {
        var db = nameof(Import_MergesIntoTheContactHoldingTheAddress_WithoutOverwriting);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Bruno", addresses: "bruno@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(user, [Row(
            first: "Brunon", last: "Mertens", addresses: ["bruno@example.com", "second@example.com"])],
            CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Equal(0, outcome.Created);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("Bruno", stored.FirstName);
        Assert.Equal("Mertens", stored.LastName);
        Assert.Equal(["bruno@example.com", "second@example.com"], stored.Addresses);
    }

    [Fact]
    public async Task Import_RaisesTheFavouriteButNeverLowersIt()
    {
        var db = nameof(Import_RaisesTheFavouriteButNeverLowersIt);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Bruno", favorite: true, addresses: "bruno@example.com"), CancellationToken.None);

        await CreateStore(db).ImportAsync(
            user, [Row(addresses: "bruno@example.com")], CancellationToken.None);

        Assert.True(Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)).IsFavorite);
    }

    // The open question slice 3a left here: an address on two cards names neither of them.
    [Fact]
    public async Task Import_SkipsARowWhoseAddressBelongsToTwoContacts()
    {
        var db = nameof(Import_SkipsARowWhoseAddressBelongsToTwoContacts);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "A", addresses: "shared@example.com"), CancellationToken.None);
        await CreateStore(db).CreateAsync(user, Write(first: "B", addresses: "shared@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(line: 7, first: "C", addresses: "shared@example.com")], CancellationToken.None);

        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(0, outcome.Created);
        var error = Assert.Single(outcome.Errors);
        Assert.Equal(7, error.Line);
        Assert.Equal(ContactStore.AmbiguousAddress, error.Reason);
    }

    [Fact]
    public async Task Import_SkipsARowReachingTwoDifferentContacts()
    {
        var db = nameof(Import_SkipsARowReachingTwoDifferentContacts);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "A", addresses: "a@example.com"), CancellationToken.None);
        await CreateStore(db).CreateAsync(user, Write(first: "B", addresses: "b@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(addresses: ["a@example.com", "b@example.com"])], CancellationToken.None);

        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(ContactStore.AmbiguousAddress, Assert.Single(outcome.Errors).Reason);
    }

    [Fact]
    public async Task Import_FailsARowWithNeitherNameNorAddress()
    {
        var db = nameof(Import_FailsARowWithNeitherNameNorAddress);

        var outcome = await CreateStore(db).ImportAsync(
            Guid.NewGuid(), [Row(line: 5, vcard: "BEGIN:VCARD")], CancellationToken.None);

        Assert.Equal(1, outcome.Failed);
        Assert.Equal(ContactStore.NoNameOrAddress, Assert.Single(outcome.Errors).Reason);
    }

    // A file listing one person twice must not leave two cards behind.
    [Fact]
    public async Task Import_MergesASecondRowIntoTheContactTheFirstOneCreated()
    {
        var db = nameof(Import_MergesASecondRowIntoTheContactTheFirstOneCreated);
        var user = Guid.NewGuid();

        var outcome = await CreateStore(db).ImportAsync(user,
        [
            Row(line: 2, first: "Bruno", addresses: "bruno@example.com"),
            Row(line: 3, last: "Mertens", addresses: ["bruno@example.com", "second@example.com"]),
        ], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Equal(1, outcome.Merged);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("Mertens", stored.LastName);
        Assert.Equal(["bruno@example.com", "second@example.com"], stored.Addresses);
    }

    // The round trip's other half: a contact with no address is invisible to the address index, so
    // re-importing our own export would create one duplicate per address-less contact, every time.
    [Fact]
    public async Task Import_MergesANameOnlyRowIntoTheAddressLessContactHoldingThatName()
    {
        var db = nameof(Import_MergesANameOnlyRowIntoTheAddressLessContactHoldingThatName);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "Bruno", last: "Mertens"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(first: "Bruno", last: "Mertens", favorite: true)], CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Equal(0, outcome.Created);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.True(stored.IsFavorite);
    }

    // The name index holds only the address-less: the exporter always writes the addresses a contact
    // has, so a row naming one without them is describing somebody else.
    [Fact]
    public async Task Import_CreatesForANameOnlyRowWhenTheContactOfThatNameHasAddresses()
    {
        var db = nameof(Import_CreatesForANameOnlyRowWhenTheContactOfThatNameHasAddresses);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Bruno", last: "Mertens", addresses: "bruno@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(first: "Bruno", last: "Mertens")], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Equal(0, outcome.Merged);
        Assert.Equal(2, (await CreateStore(db).ListAsync(user, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Import_SkipsANameOnlyRowWhoseNameIsOnTwoAddressLessContacts()
    {
        var db = nameof(Import_SkipsANameOnlyRowWhoseNameIsOnTwoAddressLessContacts);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "Bruno"), CancellationToken.None);
        await CreateStore(db).CreateAsync(user, Write(first: "Bruno"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(line: 9, first: "Bruno")], CancellationToken.None);

        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(0, outcome.Created);
        var error = Assert.Single(outcome.Errors);
        Assert.Equal(9, error.Line);
        Assert.Equal(ContactStore.AmbiguousName, error.Reason);
    }

    // The name index is kept current for the reason the address one is: a file listing one
    // address-less person twice must not leave two cards behind.
    [Fact]
    public async Task Import_CreatesOneContactForANameOnlyRowListedTwice()
    {
        var db = nameof(Import_CreatesOneContactForANameOnlyRowListedTwice);
        var user = Guid.NewGuid();

        var outcome = await CreateStore(db).ImportAsync(user,
        [
            Row(line: 2, first: "Bruno", last: "Mertens"),
            Row(line: 3, first: "Bruno", last: "Mertens"),
        ], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Equal(1, outcome.Merged);
        Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    // "Exactly the same name" is what a user means by it: an ordinal match would file Bruno and
    // bruno as two people.
    [Fact]
    public async Task Import_MatchesTheNameIgnoringCaseAndSurroundingSpace()
    {
        var db = nameof(Import_MatchesTheNameIgnoringCaseAndSurroundingSpace);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "Bruno", nick: "Nono"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(first: "  bruno ", nick: "NONO")], CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task Import_KeepsTheVCardOnlyWhenThereWasNone()
    {
        var db = nameof(Import_KeepsTheVCardOnlyWhenThereWasNone);
        var user = Guid.NewGuid();

        await CreateStore(db).ImportAsync(user,
        [
            Row(line: 2, first: "Bruno", vcard: "FIRST", addresses: "bruno@example.com"),
            Row(line: 3, vcard: "SECOND", addresses: "bruno@example.com"),
        ], CancellationToken.None);

        Assert.Equal("FIRST", Assert.Single(new PreferencesTestDbContext(db).Contacts).VCardRaw);
    }

    // The card's own key decides before the address does, and replaying the file changes nothing.
    [Fact]
    public async Task Import_MergesOnUidFirst()
    {
        var db = nameof(Import_MergesOnUidFirst);
        var user = Guid.NewGuid();
        await CreateStore(db).ImportAsync(user,
        [
            Row(line: 2, nick: "Ana", vcard: Card("UID:card-1", "FN:Ana", "EMAIL:a@example.com"),
                uid: "card-1", addresses: "a@example.com"),
            Row(line: 8, nick: "Bo", vcard: Card("UID:card-2", "FN:Bo", "EMAIL:b@example.com"),
                uid: "card-2", addresses: "b@example.com"),
        ], CancellationToken.None);

        // The row's only address belongs to the other contact: the UID is what files it right.
        var moved = Row(line: 3, nick: "Ana",
            vcard: Card("UID:card-1", "FN:Ana", "EMAIL:b@example.com"), uid: "card-1",
            addresses: "b@example.com");
        var outcome = await CreateStore(db).ImportAsync(user, [moved], CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Equal(0, outcome.Created);
        var after = new PreferencesTestDbContext(db);
        var ana = after.Contacts.Single(c => c.Uid == "card-1");
        var bo = after.Contacts.Single(c => c.Uid == "card-2");
        Assert.Equal(2, after.ContactEmails.Count(e => e.ContactId == ana.Id));
        Assert.Single(after.ContactEmails.Where(e => e.ContactId == bo.Id));

        var hash = ana.CardHash;
        await CreateStore(db).ImportAsync(user, [moved], CancellationToken.None);
        var replayed = new PreferencesTestDbContext(db);
        Assert.Equal(hash, replayed.Contacts.Single(c => c.Uid == "card-1").CardHash);
        Assert.Equal(2, replayed.ContactEmails.Count(e => e.ContactId == ana.Id));
    }

    // Two new cards of one UID would violate uq_contacts_user_uid and fail the whole file.
    [Fact]
    public async Task Import_KeepsTheUidIndexCurrent()
    {
        var db = nameof(Import_KeepsTheUidIndexCurrent);
        var user = Guid.NewGuid();

        var outcome = await CreateStore(db).ImportAsync(user,
        [
            Row(line: 2, nick: "Ana", vcard: Card("UID:same", "FN:Ana"), uid: "same"),
            Row(line: 8, nick: "Ana", vcard: Card("UID:same", "FN:Ana", "EMAIL:a@example.com"),
                uid: "same", addresses: "a@example.com"),
        ], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Equal(1, outcome.Merged);
        var stored = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal("same", stored.Uid);
    }

    // The third door of décision 1: the incoming card is the only one there is, so it is filed
    // as it arrived — the sole path that keeps a foreign card's X- properties.
    [Fact]
    public async Task Import_StoresTheIncomingCardVerbatimWhenTheTargetHasNone()
    {
        var db = nameof(Import_StoresTheIncomingCardVerbatimWhenTheTargetHasNone);
        var user = Guid.NewGuid();
        var id = Guid.NewGuid();
        var seed = new PreferencesTestDbContext(db);
        seed.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), FirstName = "Bruno", VCardRaw = null,
        });
        seed.ContactEmails.Add(new ContactEmail { ContactId = id, Address = "bruno@example.com" });
        await seed.SaveChangesAsync();

        var card = Card("UID:card-9", "N:Mertens;Bruno;;;", "FN:Bruno Mertens",
            "EMAIL:bruno@example.com", "X-ABUID:ABC-DEF");
        await CreateStore(db).ImportAsync(user, [Row(
            first: "Bruno", last: "Mertens", vcard: card, uid: "card-9", addresses: "bruno@example.com")],
            CancellationToken.None);

        var stored = new PreferencesTestDbContext(db).Contacts.Single();
        Assert.Equal(card, stored.VCardRaw);
        Assert.Equal("Mertens", stored.LastName);
        // The column takes the card's UID: a card stored as it arrived and a contact answering to
        // another identity is a duplicate at the first CardDAV pass.
        Assert.Equal("card-9", stored.Uid);
    }

    // The card is posed as it arrived only when it repeats what the contact already holds: the
    // projection is total, so a card matched on one shared address would erase all the rest.
    [Fact]
    public async Task Import_NeverPosesAnIncomingCardOverWhatTheTargetWouldLose()
    {
        var db = nameof(Import_NeverPosesAnIncomingCardOverWhatTheTargetWouldLose);
        var user = Guid.NewGuid();
        var id = Guid.NewGuid();
        var seed = new PreferencesTestDbContext(db);
        seed.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), FirstName = "Bruno", LastName = "Mertens",
            VCardRaw = null,
        });
        seed.ContactEmails.Add(new ContactEmail { ContactId = id, Address = "bruno@example.com" });
        seed.ContactEmails.Add(new ContactEmail
        {
            ContactId = id, Address = "second@example.com", Position = 1,
        });
        await seed.SaveChangesAsync();

        // Neither the surname nor the second address is on the incoming card.
        var card = Card("UID:card-7", "N:;Bruno;;;", "FN:Bruno", "NICKNAME:bru",
            "EMAIL:bruno@example.com", "EMAIL:third@example.com");
        await CreateStore(db).ImportAsync(user, [Row(
            first: "Bruno", nick: "bru", vcard: card, uid: "card-7",
            addresses: ["bruno@example.com", "third@example.com"])], CancellationToken.None);

        var after = new PreferencesTestDbContext(db);
        var stored = after.Contacts.Single();
        Assert.NotEqual(card, stored.VCardRaw);
        Assert.Equal("Mertens", stored.LastName);
        Assert.Equal("bru", stored.Nickname);
        Assert.Equal(id.ToString(), stored.Uid); // not adopted: the card was not posed as it arrived
        Assert.Equal(
            ["bruno@example.com", "second@example.com", "third@example.com"],
            after.ContactEmails.Where(e => e.ContactId == stored.Id)
                .OrderBy(e => e.Position).Select(e => e.Address));
    }

    // A CSV row brings no identity of its own, so a merge into an existing contact must not
    // rename the key a CardDAV client synchronises on.
    [Fact]
    public async Task Import_NeverRenamesAnExistingContactsUid()
    {
        var db = nameof(Import_NeverRenamesAnExistingContactsUid);
        var user = Guid.NewGuid();
        var id = Guid.NewGuid();
        var seed = new PreferencesTestDbContext(db);
        seed.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), FirstName = "Bruno", VCardRaw = null,
        });
        seed.ContactEmails.Add(new ContactEmail { ContactId = id, Address = "bruno@example.com" });
        await seed.SaveChangesAsync();

        // The shape the CSV path hands the store: columns to compose from, no card, no UID.
        await CreateStore(db).ImportAsync(user, [Row(
            first: "Bruno", last: "Mertens", write: Write(first: "Bruno", last: "Mertens"),
            addresses: "bruno@example.com")], CancellationToken.None);

        var stored = new PreferencesTestDbContext(db).Contacts.Single();
        Assert.Equal(id.ToString(), stored.Uid);
        Assert.Contains($"UID:{id}", stored.VCardRaw!);
        Assert.Equal("Mertens", stored.LastName);
    }

    // A card with no UID cannot lend one, so the composer stamps the column's on it rather than
    // storing a card whose identity nothing in the book claims.
    [Fact]
    public async Task Import_StampsTheColumnsUidOnAnIncomingCardCarryingNone()
    {
        var db = nameof(Import_StampsTheColumnsUidOnAnIncomingCardCarryingNone);
        var user = Guid.NewGuid();
        var id = Guid.NewGuid();
        var seed = new PreferencesTestDbContext(db);
        seed.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), FirstName = "Bruno", VCardRaw = null,
        });
        seed.ContactEmails.Add(new ContactEmail { ContactId = id, Address = "bruno@example.com" });
        await seed.SaveChangesAsync();

        await CreateStore(db).ImportAsync(user, [Row(
            first: "Bruno", last: "Mertens",
            vcard: Card("N:Mertens;Bruno;;;", "FN:Bruno Mertens", "EMAIL:bruno@example.com"),
            addresses: "bruno@example.com")], CancellationToken.None);

        var stored = new PreferencesTestDbContext(db).Contacts.Single();
        Assert.Equal(id.ToString(), stored.Uid);
        Assert.Contains($"UID:{id}", stored.VCardRaw!);
        Assert.Equal("Mertens", stored.LastName);
    }

    // A target that has a card keeps it and has the merge folded in: what only the card carries
    // survives, and the columns cannot drift from it.
    [Fact]
    public async Task Import_RecomposesTheTargetsCard()
    {
        var db = nameof(Import_RecomposesTheTargetsCard);
        var user = Guid.NewGuid();
        var existing = Card("UID:card-3", "N:;Ana;;;", "FN:Ana", "EMAIL:ana@example.com",
            "X-ABLabel:Perso");
        await CreateStore(db).ImportAsync(
            user, [Row(first: "Ana", vcard: existing, uid: "card-3", addresses: "ana@example.com")],
            CancellationToken.None);

        await CreateStore(db).ImportAsync(user, [Row(line: 3, last: "Solo",
            vcard: Card("UID:card-3", "FN:Ana Solo", "EMAIL:new@example.com"), uid: "card-3",
            addresses: ["ana@example.com", "new@example.com"])], CancellationToken.None);

        var after = new PreferencesTestDbContext(db);
        var stored = after.Contacts.Single();
        Assert.NotEqual(existing, stored.VCardRaw);
        Assert.Contains("X-ABLabel:Perso", stored.VCardRaw!);
        Assert.Contains("new@example.com", stored.VCardRaw!);
        Assert.Equal("Solo", stored.LastName);
        Assert.Equal(2, after.ContactEmails.Count(e => e.ContactId == stored.Id));
    }

    // The seam the CSV path rests on: the reader carries columns, the store composes the card and
    // the card is projected back. The addresses are the store's own — the reader sends none — so
    // this is also what proves they reach the card at all rather than being lost between the two.
    [Fact]
    public async Task Import_ComposesACreatedContactsCardFromTheRowsWrite()
    {
        var db = nameof(Import_ComposesACreatedContactsCardFromTheRowsWrite);
        var user = Guid.NewGuid();
        var write = new ContactWrite(
            "Bruno", "Mertens", null, null, "J", "Mr", null, "Weesky", "Support", "Engineer",
            "1980-01-15", "https://x.be", "a note", false,
            [], // the reader sends none: the store fills them from its own capped list
            [new ContactWritePhone(null, "+32470000000", "CELL")],
            [new ContactWriteAddress(null, "HOME", null, null, "Rue X 1", "Namur", null, "5000", "Belgium")],
            "imported");

        await CreateStore(db).ImportAsync(user, [Row(
            first: "Bruno", last: "Mertens", write: write, addresses: "bruno@example.com")],
            CancellationToken.None);

        var after = new PreferencesTestDbContext(db);
        var stored = after.Contacts.Single();
        Assert.Contains("EMAIL;TYPE=INTERNET:bruno@example.com", stored.VCardRaw!);
        Assert.Contains("TEL;TYPE=CELL:+32470000000", stored.VCardRaw!);
        Assert.Contains("ORG:Weesky;Support", stored.VCardRaw!);
        Assert.Contains("ADR;TYPE=HOME:;;Rue X 1;Namur;;5000;Belgium", stored.VCardRaw!);
        Assert.Contains("N:Mertens;Bruno;J;Mr;", stored.VCardRaw!);
        Assert.Equal("bruno@example.com",
            after.ContactEmails.Single(e => e.ContactId == stored.Id).Address);
        Assert.Single(after.ContactPhones.Where(p => p.ContactId == stored.Id));
        Assert.Single(after.ContactAddresses.Where(a => a.ContactId == stored.Id));
        Assert.Equal("Weesky", stored.Organization);
    }

    // ContactVCardWriter's economy is dead: under décision 1 a contact that is only a name has a
    // card too, or the invariant breaks on the first CSV import after the backfill.
    [Fact]
    public async Task Import_EveryCreatedContactHasACard()
    {
        var db = nameof(Import_EveryCreatedContactHasACard);
        var user = Guid.NewGuid();

        await CreateStore(db).ImportAsync(
            user, [Row(first: "Bruno", addresses: "bruno@example.com")], CancellationToken.None);

        var stored = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Contains("FN:Bruno", stored.VCardRaw!);
        Assert.Contains("EMAIL", stored.VCardRaw!);
        Assert.Contains($"UID:{stored.Uid}", stored.VCardRaw!);
        Assert.NotEmpty(stored.CardHash);
    }

    // The verbatim door is the one that brings cards declaring no UID at all; the store stamps the
    // column's on them without touching a byte of the rest, and the hash still describes the whole.
    [Fact]
    public async Task Import_StampsTheColumnsUidOnAVerbatimCardThatDeclaresNone()
    {
        var db = nameof(Import_StampsTheColumnsUidOnAVerbatimCardThatDeclaresNone);
        var user = Guid.NewGuid();
        var card = Card("N:Mertens;Bruno;;;", "FN:Bruno Mertens", "EMAIL:bruno@example.com",
            "X-ABUID:ABC-DEF");

        await CreateStore(db).ImportAsync(user, [Row(
            first: "Bruno", last: "Mertens", vcard: card, addresses: "bruno@example.com")],
            CancellationToken.None);

        var stored = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal(
            card.Replace("VERSION:3.0\r\n", $"VERSION:3.0\r\nUID:{stored.Uid}\r\n"), stored.VCardRaw);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stored.VCardRaw!))),
            stored.CardHash);
    }

    [Fact]
    public async Task Import_StopsCreatingAtTheUserCap()
    {
        var db = nameof(Import_StopsCreatingAtTheUserCap);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        FillTheBook(context, user);
        await context.SaveChangesAsync();

        var outcome = await new ContactStore(new PreferencesTestDbContext(db), ContactStoreTestFactory.NewSync().Object)
            .ImportAsync(
                user, [Row(line: 2, first: "Over", addresses: "over@example.com")], CancellationToken.None);

        Assert.Equal(0, outcome.Created);
        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(ContactStore.CapReached, Assert.Single(outcome.Errors).Reason);
    }

    // The cap counts creations only: a full book still accepts what merges into it.
    [Fact]
    public async Task Import_MergesIntoAFullBookWithoutSpendingQuota()
    {
        var db = nameof(Import_MergesIntoAFullBookWithoutSpendingQuota);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        var first = FillTheBook(context, user);
        context.ContactEmails.Add(new ContactEmail { ContactId = first, Address = "full@example.com" });
        await context.SaveChangesAsync();

        var outcome = await new ContactStore(new PreferencesTestDbContext(db), ContactStoreTestFactory.NewSync().Object)
            .ImportAsync(
                user, [Row(last: "Mertens", addresses: "full@example.com")], CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Equal(0, outcome.Skipped);
        Assert.Empty(outcome.Errors);
    }

    [Fact]
    public async Task Import_CapsTheAddressesOfOneContact()
    {
        var db = nameof(Import_CapsTheAddressesOfOneContact);
        var user = Guid.NewGuid();
        var many = Enumerable.Range(0, 60).Select(i => $"a{i}@example.com").ToArray();

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(first: "Bruno", addresses: many)], CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(50, stored.Addresses.Count);
        Assert.Equal(ContactStore.AddressCapReached, Assert.Single(outcome.Errors).Reason);
    }

    [Fact]
    public async Task Import_NeverReachesAnotherUsersBook()
    {
        var db = nameof(Import_NeverReachesAnotherUsersBook);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await CreateStore(db).CreateAsync(theirs, Write(first: "Theirs", addresses: "shared@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            mine, [Row(first: "Mine", addresses: "shared@example.com")], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Single(await CreateStore(db).ListAsync(mine, CancellationToken.None));
        Assert.Single(await CreateStore(db).ListAsync(theirs, CancellationToken.None));
    }

    [Fact]
    public async Task Import_FoldsTheAddressBeforeMatching()
    {
        var db = nameof(Import_FoldsTheAddressBeforeMatching);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "Bruno", addresses: "bruno@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(last: "Mertens", addresses: " BRUNO@Example.COM ")], CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Single(Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)).Addresses);
    }

    // The merge branch has its own ceiling: what the target already holds leaves the room.
    [Fact]
    public async Task Import_CapsWhatAMergeAppends()
    {
        var db = nameof(Import_CapsWhatAMergeAppends);
        var user = Guid.NewGuid();
        var already = Enumerable.Range(0, 48).Select(i => $"h{i}@example.com").ToArray();
        await CreateStore(db).CreateAsync(user, Write(first: "Bruno", addresses: already), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(user, [Row(addresses:
            [already[0], "n1@example.com", "n2@example.com", "n3@example.com", "n4@example.com", "n5@example.com"])],
            CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(ContactValidator.MaxAddressesPerContact, stored.Addresses.Count);
        Assert.Equal(ContactStore.AddressCapReached, Assert.Single(outcome.Errors).Reason);
    }

    [Fact]
    public async Task Import_MovesUpdatedAtOnlyWhenTheMergeChangedSomething()
    {
        var db = nameof(Import_MovesUpdatedAtOnlyWhenTheMergeChangedSomething);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Bruno", addresses: "bruno@example.com"), CancellationToken.None);

        // Backdated rather than compared against the clock: DateTime.UtcNow can tick twice inside
        // one test, and a replayed import must be shown not to move the stamp at all.
        var backdate = new PreferencesTestDbContext(db);
        var before = backdate.Contacts.Single().UpdatedAt = DateTime.UtcNow.AddDays(-1);
        await backdate.SaveChangesAsync();

        await CreateStore(db).ImportAsync(
            user, [Row(addresses: "bruno@example.com")], CancellationToken.None);
        Assert.Equal(before, new PreferencesTestDbContext(db).Contacts.Single().UpdatedAt);

        await CreateStore(db).ImportAsync(
            user, [Row(addresses: ["bruno@example.com", "second@example.com"])], CancellationToken.None);
        Assert.True(new PreferencesTestDbContext(db).Contacts.Single().UpdatedAt > before);
    }

    // Positions are not keys and a deleted address leaves a hole: appending must clear the last
    // one, not the count, or two rows claim the same place.
    [Fact]
    public async Task Import_AppendsPastTheLastPositionWhenTheBookHasAGap()
    {
        var db = nameof(Import_AppendsPastTheLastPositionWhenTheBookHasAGap);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user,
            Write(first: "Bruno", addresses: ["a@example.com", "b@example.com", "c@example.com"]),
            CancellationToken.None);

        var gap = new PreferencesTestDbContext(db);
        gap.ContactEmails.Remove(gap.ContactEmails.Single(e => e.Position == 1));
        await gap.SaveChangesAsync();

        await CreateStore(db).ImportAsync(
            user, [Row(addresses: ["a@example.com", "d@example.com"])], CancellationToken.None);

        var contactId = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)).Id;
        var rows = new PreferencesTestDbContext(db).ContactEmails.Where(e => e.ContactId == contactId).ToList();
        Assert.Equal(3, rows.Single(e => e.Address == "d@example.com").Position);
        Assert.Equal(rows.Count, rows.Select(e => e.Position).Distinct().Count());
    }

    // Le trou que ce carnet a paye : une carte iPhone fusionnee dans une fiche deja connue
    // n'apportait que noms et e-mails, et son ADR disparaissait en silence.
    [Fact]
    public async Task Import_FillsAnEmptyTargetsPhonesAndPostalAddressesFromTheCard()
    {
        var db = nameof(Import_FillsAnEmptyTargetsPhonesAndPostalAddressesFromTheCard);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Bruno", addresses: "bruno@example.com"), CancellationToken.None);

        await CreateStore(db).ImportAsync(user, [Row(
            first: "Bruno", write: Offer(
                phones: [new ContactWritePhone(null, "+32470000000", "CELL")],
                postalAddresses: [new ContactWriteAddress(
                    null, "HOME", null, null, "Rue X 1", "Namur", null, "5000", "Belgium")],
                organization: "Weesky"),
            addresses: "bruno@example.com")], CancellationToken.None);

        var after = new PreferencesTestDbContext(db);
        var stored = after.Contacts.Single();
        Assert.Contains("ADR;TYPE=HOME:;;Rue X 1;Namur;;5000;Belgium", stored.VCardRaw!);
        Assert.Equal("Weesky", stored.Organization);
        Assert.Equal("+32470000000", after.ContactPhones.Single(p => p.ContactId == stored.Id).Number);
        Assert.Equal("Namur", after.ContactAddresses.Single(a => a.ContactId == stored.Id).Locality);
    }

    // Tout ou rien par famille : deux orthographes du meme numero sont indiscernables sans
    // normalisation, donc une cible qui en tient deja un garde exactement les siens.
    [Fact]
    public async Task Import_LeavesTheFamiliesOfATargetThatAlreadyHoldsThemAlone()
    {
        var db = nameof(Import_LeavesTheFamiliesOfATargetThatAlreadyHoldsThemAlone);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "Bruno", addresses: "bruno@example.com")
            with { Phones = [new ContactWritePhone(null, "+3221234567", "HOME")] },
            CancellationToken.None);

        await CreateStore(db).ImportAsync(user, [Row(
            first: "Bruno", write: Offer(phones: [new ContactWritePhone(null, "+32470000000", "CELL")]),
            addresses: "bruno@example.com")], CancellationToken.None);

        var after = new PreferencesTestDbContext(db);
        var stored = after.Contacts.Single();
        Assert.Equal("+3221234567", after.ContactPhones.Single(p => p.ContactId == stored.Id).Number);
    }

    // Les colonnes ne sont ecrites qu'a la fin du fichier : une seconde ligne visant la meme cible
    // la verrait encore vide et doublerait la famille que la premiere vient de poser.
    [Fact]
    public async Task Import_DoesNotDoubleAFamilyWhenTwoRowsMergeIntoTheSameTarget()
    {
        var db = nameof(Import_DoesNotDoubleAFamilyWhenTwoRowsMergeIntoTheSameTarget);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Bruno", addresses: "bruno@example.com"), CancellationToken.None);

        await CreateStore(db).ImportAsync(user, [
            Row(line: 2, first: "Bruno",
                write: Offer(phones: [new ContactWritePhone(null, "+32470000000", "CELL")]),
                addresses: "bruno@example.com"),
            Row(line: 3, first: "Bruno",
                write: Offer(phones: [new ContactWritePhone(null, "+32480000000", "CELL")]),
                addresses: "bruno@example.com")], CancellationToken.None);

        var after = new PreferencesTestDbContext(db);
        var stored = after.Contacts.Single();
        Assert.Equal("+32470000000",
            Assert.Single(after.ContactPhones.Where(p => p.ContactId == stored.Id)).Number);
    }

    // Une cible nee dans le meme fichier n'a pas encore de colonnes : ce qu'elle tient est ce que
    // sa carte verbatim dit, et une ligne suivante ne doit pas l'ecraser.
    [Fact]
    public async Task Import_LeavesABornTargetsOwnOrganizationAlone()
    {
        var db = nameof(Import_LeavesABornTargetsOwnOrganizationAlone);
        var user = Guid.NewGuid();

        await CreateStore(db).ImportAsync(user, [
            Row(line: 2, first: "Ana",
                vcard: Card("UID:card-1", "N:;Ana;;;", "FN:Ana", "ORG:Acme;Ventes",
                    "EMAIL:ana@example.com"),
                uid: "card-1", write: Offer(organization: "Acme", department: "Ventes"),
                addresses: "ana@example.com"),
            Row(line: 3, first: "Ana", write: Offer(organization: "Autre"),
                addresses: "ana@example.com")], CancellationToken.None);

        var stored = new PreferencesTestDbContext(db).Contacts.Single();
        Assert.Equal("Acme", stored.Organization);
        Assert.Contains("ORG:Acme;Ventes", stored.VCardRaw!);
    }

    // Rejouer le meme fichier ne doit rien remuer : updated_at est ce sur quoi un ETag CardDAV
    // reposera, et le faire bouger pour rien resynchronise tous les clients.
    [Fact]
    public async Task Import_ReplayedAfterFillingTheFamiliesMovesNothing()
    {
        var db = nameof(Import_ReplayedAfterFillingTheFamiliesMovesNothing);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Bruno", addresses: "bruno@example.com"), CancellationToken.None);
        ContactImportRow[] file = [Row(first: "Bruno", write: Offer(
            phones: [new ContactWritePhone(null, "+32470000000", "CELL")], organization: "Weesky"),
            addresses: "bruno@example.com")];
        await CreateStore(db).ImportAsync(user, file, CancellationToken.None);

        var backdate = new PreferencesTestDbContext(db);
        var before = backdate.Contacts.Single().UpdatedAt = DateTime.UtcNow.AddDays(-1);
        await backdate.SaveChangesAsync();

        await CreateStore(db).ImportAsync(user, file, CancellationToken.None);

        var after = new PreferencesTestDbContext(db);
        Assert.Equal(before, after.Contacts.Single().UpdatedAt);
        Assert.Single(after.ContactPhones);
    }
}
