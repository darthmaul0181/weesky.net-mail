using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Contacts;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using Moq;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

/// <summary>
/// Décision 19: a <c>.vcf</c> brings group cards as well as contacts, and a group resolves by UID
/// and by nothing else — never by a name, never by an address, and never against the other
/// species. It enters no merge index either way round: not as a target, not as an entrant.
/// </summary>
public sealed class ContactStoreImportGroupTests
{
    private const string MemberUid = "11111111-1111-1111-1111-111111111111";

    private static ContactStore CreateStore(string db) =>
        new(new PreferencesTestDbContext(db), ContactStoreTestFactory.NewSync().Object);

    private static string GroupCard(string uid, string fn, params string[] members) =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{uid}\r\nFN:{fn}\r\nX-ADDRESSBOOKSERVER-KIND:group\r\n"
        + string.Concat(members.Select(m => $"X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:{m}\r\n"))
        + "END:VCARD\r\n";

    private static string PersonCard(string uid, string fn, string? email = null) =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{uid}\r\nFN:{fn}\r\n"
        + (email == null ? string.Empty : $"EMAIL:{email}\r\n") + "END:VCARD\r\n";

    /// <summary>The rows a <c>.vcf</c> really produces: split, then mapped, as the controller does.</summary>
    private static IReadOnlyList<ContactImportRow> Rows(params string[] cards) =>
        [.. VCardSplitter.Split(string.Concat(cards)).Select(VCardImportMapper.Map)];

    private static async Task<Guid> GivenARow(
        string db, Guid user, string uid, string kind, string? name = null, string? email = null)
    {
        await using var context = new PreferencesTestDbContext(db);
        var id = Guid.NewGuid();
        var card = kind == ContactKinds.Group
            ? GroupCard(uid, name ?? "Amis") : PersonCard(uid, name ?? "Bruno", email);
        context.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = uid, Kind = kind, DisplayName = name ?? "Amis",
            VCardRaw = card, CardHash = ContactStore.CardHashOf(card), DavName = $"{id}.vcf",
            UpdatedAt = DateTime.UtcNow, SyncSequence = 1
        });
        if (email != null) context.ContactEmails.Add(new ContactEmail { ContactId = id, Address = email });
        await context.SaveChangesAsync(CancellationToken.None);
        return id;
    }

    // The resolution of a MEMBER is a join, not a state the reader carries: whichever card the
    // file puts first, the group ends up naming the same contact.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Import_AGroupCard_LandsAsAGroupNamingItsMember(bool groupFirst)
    {
        var db = nameof(Import_AGroupCard_LandsAsAGroupNamingItsMember) + groupFirst;
        var user = Guid.NewGuid();
        var group = GroupCard("g-1", "Amis", MemberUid);
        var person = PersonCard($"urn:uuid:{MemberUid}", "Bruno", "bruno@example.com");

        var outcome = await CreateStore(db).ImportAsync(
            user, groupFirst ? Rows(group, person) : Rows(person, group), CancellationToken.None);

        Assert.Equal(2, outcome.Created);
        var after = new PreferencesTestDbContext(db);
        var stored = after.Contacts.Single(c => c.Uid == "g-1");
        Assert.Equal(ContactKinds.Group, stored.Kind);
        Assert.Equal("Amis", stored.DisplayName);
        Assert.Equal(group, stored.VCardRaw);
        Assert.Equal(MemberUid, after.ContactGroupMembers.Single(m => m.GroupId == stored.Id).MemberUid);

        var listed = await new ContactGroupStore(
                after, new ContactStore(after, ContactStoreTestFactory.NewSync().Object),
                ContactStoreTestFactory.NewSync().Object)
            .ListAsync(user, CancellationToken.None);
        Assert.Equal(
            after.Contacts.Single(c => c.Kind == ContactKinds.Individual).Id,
            Assert.Single(Assert.Single(listed).MemberIds));
    }

    // The index kept as the file is read never takes a group in: the name of a group is not the
    // name of a contact, whichever order the two cards arrive in.
    [Fact]
    public async Task Import_AnAddresslessCardNamedAfterAGroupOfTheSameFile_CreatesAContact()
    {
        var db = nameof(Import_AnAddresslessCardNamedAfterAGroupOfTheSameFile_CreatesAContact);
        var user = Guid.NewGuid();

        var outcome = await CreateStore(db).ImportAsync(
            user, Rows(GroupCard("g-1", "Amis"), PersonCard("p-1", "Amis")), CancellationToken.None);

        Assert.Equal(2, outcome.Created);
        Assert.Equal(0, outcome.Merged);
        var after = new PreferencesTestDbContext(db);
        Assert.Equal(ContactKinds.Group, after.Contacts.Single(c => c.Uid == "g-1").Kind);
        Assert.Equal(ContactKinds.Individual, after.Contacts.Single(c => c.Uid == "p-1").Kind);
    }

    [Fact]
    public async Task Import_AGroupOnAContactsUid_Fails()
    {
        var db = nameof(Import_AGroupOnAContactsUid_Fails);
        var user = Guid.NewGuid();
        await GivenARow(db, user, "u-1", ContactKinds.Individual);

        var outcome = await CreateStore(db).ImportAsync(
            user, Rows(GroupCard("u-1", "Amis")), CancellationToken.None);

        Assert.Equal(1, outcome.Failed);
        Assert.Equal(0, outcome.Merged);
        Assert.Equal(ContactStore.CrossSpeciesUid, Assert.Single(outcome.Errors).Reason);
        var stored = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal(ContactKinds.Individual, stored.Kind);
    }

    [Fact]
    public async Task Import_AContactOnAGroupsUid_Fails()
    {
        var db = nameof(Import_AContactOnAGroupsUid_Fails);
        var user = Guid.NewGuid();
        await GivenARow(db, user, "g-1", ContactKinds.Group);

        var outcome = await CreateStore(db).ImportAsync(
            user, Rows(PersonCard("g-1", "Bruno", "bruno@example.com")), CancellationToken.None);

        Assert.Equal(1, outcome.Failed);
        Assert.Equal(0, outcome.Merged);
        Assert.Equal(ContactStore.CrossSpeciesUid, Assert.Single(outcome.Errors).Reason);
        var stored = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal(ContactKinds.Group, stored.Kind);
    }

    // A group at an unknown UID is created, always: the stored group of the same name is not what
    // this card describes, and only the UID could ever have said so.
    [Fact]
    public async Task Import_AGroupAtAnUnknownUid_IsCreated_NeverMatchedByName()
    {
        var db = nameof(Import_AGroupAtAnUnknownUid_IsCreated_NeverMatchedByName);
        var user = Guid.NewGuid();
        await GivenARow(db, user, "g-1", ContactKinds.Group, name: "Amis");

        var outcome = await CreateStore(db).ImportAsync(
            user, Rows(GroupCard("g-2", "Amis")), CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Equal(0, outcome.Merged);
        Assert.Equal(2, new PreferencesTestDbContext(db).Contacts.Count(c => c.Kind == ContactKinds.Group));
    }

    // The address index excludes groups too. A group card carrying an EMAIL is exotic and stored
    // all the same, and it must never become the target a CSV row folds into.
    [Fact]
    public async Task Import_ARowAddressedLikeAGroup_CreatesAContact()
    {
        var db = nameof(Import_ARowAddressedLikeAGroup_CreatesAContact);
        var user = Guid.NewGuid();
        await GivenARow(db, user, "g-1", ContactKinds.Group, name: "Amis", email: "amis@example.com");

        var outcome = await CreateStore(db).ImportAsync(user, [new ContactImportRow(
            Line: 2, FirstName: "Bruno", LastName: null, Nickname: null, IsFavorite: false,
            Addresses: ["amis@example.com"], VCard: null, Uid: null)], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Equal(0, outcome.Merged);
        var after = new PreferencesTestDbContext(db);
        Assert.Equal("Bruno", after.Contacts.Single(c => c.Kind == ContactKinds.Individual).FirstName);
    }

    // Replaying the same file changes nothing: the group is counted as merged, and the card it
    // came back with is the one already stored — no archive, no rank, no client woken.
    [Fact]
    public async Task Import_ReplayingTheSameGroup_StoresNothingAnew()
    {
        var db = nameof(Import_ReplayingTheSameGroup_StoresNothingAnew);
        var user = Guid.NewGuid();
        var rows = Rows(GroupCard("g-1", "Amis", MemberUid));
        await CreateStore(db).ImportAsync(user, rows, CancellationToken.None);
        var before = new PreferencesTestDbContext(db).Contacts.Single();

        var sync = ContactStoreTestFactory.NewSync(rank: 9);
        var outcome = await new ContactStore(new PreferencesTestDbContext(db), sync.Object)
            .ImportAsync(user, rows, CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Equal(0, outcome.Created);
        var after = new PreferencesTestDbContext(db).Contacts.Single();
        Assert.Equal(before.CardHash, after.CardHash);
        Assert.Equal(before.SyncSequence, after.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // The known group is REPLACED by the incoming card, projection included: a member the new
    // card drops is a member the book must stop naming.
    [Fact]
    public async Task Import_AKnownGroup_TakesTheIncomingCardWhole()
    {
        var db = nameof(Import_AKnownGroup_TakesTheIncomingCardWhole);
        var user = Guid.NewGuid();
        const string second = "22222222-2222-2222-2222-222222222222";
        await CreateStore(db).ImportAsync(
            user, Rows(GroupCard("g-1", "Amis", MemberUid)), CancellationToken.None);

        var replacement = GroupCard("g-1", "Collègues", second);
        var outcome = await CreateStore(db).ImportAsync(user, Rows(replacement), CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Equal(0, outcome.Created);
        var after = new PreferencesTestDbContext(db);
        var stored = after.Contacts.Single();
        Assert.Equal(replacement, stored.VCardRaw);
        Assert.Equal("Collègues", stored.DisplayName);
        Assert.Equal(second, after.ContactGroupMembers.Single(m => m.GroupId == stored.Id).MemberUid);
    }
}
