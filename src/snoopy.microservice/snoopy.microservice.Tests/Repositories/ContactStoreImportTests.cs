using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreImportTests
{
    private static ContactStore CreateStore(string dbName) => new(new PreferencesTestDbContext(dbName));

    private static ContactImportRow Row(
        int line = 2, string? first = null, string? last = null, string? nick = null,
        bool favorite = false, string? vcard = null, params string[] addresses) =>
        new(line, first, last, nick, favorite, addresses, vcard);

    private static ContactWrite Write(
        string? first = null, string? last = null, string? nick = null,
        bool favorite = false, params string[] addresses) =>
        new(first, last, nick, favorite, addresses, "manual");

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

        await new ContactStore(context).ImportAsync(
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

    [Fact]
    public async Task Import_StopsCreatingAtTheUserCap()
    {
        var db = nameof(Import_StopsCreatingAtTheUserCap);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        FillTheBook(context, user);
        await context.SaveChangesAsync();

        var outcome = await new ContactStore(new PreferencesTestDbContext(db)).ImportAsync(
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

        var outcome = await new ContactStore(new PreferencesTestDbContext(db)).ImportAsync(
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
}
