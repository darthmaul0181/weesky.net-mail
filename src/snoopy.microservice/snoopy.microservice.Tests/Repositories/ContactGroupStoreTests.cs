using Microsoft.EntityFrameworkCore;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

/// <summary>
/// The group store (tranche 4e). Real <see cref="ContactStore"/> underneath — the write gate, the
/// projection and the transaction wrapper are its, not a second copy — and a doubled sync store,
/// which is the only observable a test has for "this write took a rank and archived a revision".
/// </summary>
public sealed class ContactGroupStoreTests : IDisposable
{
    private readonly PreferencesTestDbContext Context = ContactStoreTestFactory.NewContext();
    // Handing out ranks well above the 1 the posed fixtures carry, so "this write took a rank" is
    // an observable rather than a coincidence between the double's first answer and the fixture.
    private readonly Mock<IContactSyncStore> Sync = ContactStoreTestFactory.NewSyncCounting(first: 10);
    private readonly Guid User = Guid.NewGuid();

    private ContactGroupStore Store => new(Context, new ContactStore(Context, Sync.Object), Sync.Object);

    public void Dispose() => Context.Dispose();

    /// <summary>A real contact, made through the real store so its UID is the one the book gives.</summary>
    private async Task<Guid> GivenAContact(string first, Guid? owner = null)
    {
        var created = await new ContactStore(Context, Sync.Object).CreateAsync(
            owner ?? User, ContactStoreTestFactory.Write(first, "Mertens"), CancellationToken.None);
        return created.Value;
    }

    /// <summary>
    /// A stored group, posed as a row rather than through the store: the List tests need members
    /// no write door would ever accept — a dangling UID, another book's, a group's.
    /// </summary>
    private async Task<Guid> GivenAGroup(string name, Guid? owner = null, params string[] memberUids)
    {
        var id = Guid.NewGuid();
        var card = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{id}\r\nFN:{name}\r\n"
            + "X-ADDRESSBOOKSERVER-KIND:group\r\n"
            + string.Concat(memberUids.Select(u => $"X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:{u}\r\n"))
            + "END:VCARD\r\n";
        Context.Contacts.Add(new Contact
        {
            Id = id, UserId = owner ?? User, Uid = id.ToString(), Kind = ContactKinds.Group,
            DisplayName = name, VCardRaw = card, CardHash = ContactStore.CardHashOf(card),
            DavName = $"{id}.vcf", UpdatedAt = DateTime.UtcNow, SyncSequence = 1
        });
        for (var rank = 0; rank < memberUids.Length; rank++)
            Context.ContactGroupMembers.Add(new ContactGroupMember
            {
                GroupId = id, MemberUid = memberUids[rank], Position = rank
            });
        await Context.SaveChangesAsync(CancellationToken.None);
        return id;
    }

    private Task<Contact> RowOf(Guid id) =>
        Context.Contacts.SingleAsync(c => c.Id == id, CancellationToken.None);

    private Task<List<string>> MemberUidsOf(Guid group) =>
        Context.ContactGroupMembers.Where(m => m.GroupId == group)
            .OrderBy(m => m.Position).Select(m => m.MemberUid).ToListAsync(CancellationToken.None);

    // ---- CreateAsync ----------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_AnswersTheGroupWithNoMembers()
    {
        var created = await Store.CreateAsync(User, "  Amis  ", CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal("Amis", created.Value.Name);
        Assert.Empty(created.Value.MemberIds);
        Assert.NotEqual(Guid.Empty, created.Value.Id);
    }

    [Fact]
    public async Task CreateAsync_StoresAGroupCardNamedAsAResource()
    {
        var created = await Store.CreateAsync(User, "Amis", CancellationToken.None);

        var row = await RowOf(created.Value.Id);
        Assert.Equal(ContactKinds.Group, row.Kind);
        Assert.Contains("X-ADDRESSBOOKSERVER-KIND:group", row.VCardRaw);
        Assert.Equal($"{created.Value.Id}.vcf", row.DavName);
        Assert.True(row.SyncSequence > 0);
        // The projection is what the screen reads: FN lands in display_name like any other card.
        Assert.Equal("Amis", row.DisplayName);
    }

    [Fact]
    public async Task CreateAsync_RefusesAnEmptyName()
    {
        var created = await Store.CreateAsync(User, "   ", CancellationToken.None);

        Assert.True(created.IsFailure);
    }

    // Décision 18: groups are counted, and this is the fourth gate that says so.
    [Fact]
    public async Task CreateAsync_AtTheCap_Refuses()
    {
        for (var i = 0; i < ContactStore.MaxPerUser; i++)
        {
            var id = Guid.NewGuid();
            Context.Contacts.Add(new Contact
            {
                Id = id, UserId = User, Uid = id.ToString(), FirstName = $"C{i}",
                UpdatedAt = DateTime.UtcNow
            });
        }
        await Context.SaveChangesAsync(CancellationToken.None);

        var created = await Store.CreateAsync(User, "Amis", CancellationToken.None);

        Assert.True(created.IsFailure);
        Assert.Equal(ContactStore.CapReached, created.Error);
    }

    // ---- ListAsync ------------------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_AnswersTheResolvedMembersInCardOrder()
    {
        var first = await GivenAContact("Ada");
        var second = await GivenAContact("Grace");
        var group = await GivenAGroup("Amis", null, second.ToString(), first.ToString());

        var listed = await Store.ListAsync(User, CancellationToken.None);

        var view = Assert.Single(listed);
        Assert.Equal(group, view.Id);
        Assert.Equal("Amis", view.Name);
        Assert.Equal([second, first], view.MemberIds);
    }

    // Décision 2: a client may PUT the group before its members, so the reference is allowed to
    // dangle — but a dangling reference is no member of anything the screen can show.
    [Fact]
    public async Task ListAsync_LeavesADanglingMemberOut()
    {
        await GivenAGroup("Amis", null, Guid.NewGuid().ToString());

        var listed = await Store.ListAsync(User, CancellationToken.None);

        Assert.Empty(Assert.Single(listed).MemberIds);
    }

    [Fact]
    public async Task ListAsync_LeavesAnotherBooksMemberOut()
    {
        var foreign = await GivenAContact("Ada", Guid.NewGuid());
        await GivenAGroup("Amis", null, foreign.ToString());

        var listed = await Store.ListAsync(User, CancellationToken.None);

        Assert.Empty(Assert.Single(listed).MemberIds);
    }

    // Décision 9: a group nested in a group is not a contact, so it resolves to nothing.
    [Fact]
    public async Task ListAsync_LeavesAGroupMemberOut()
    {
        var nested = await GivenAGroup("Collègues");
        var outer = await GivenAGroup("Amis", null, (await RowOf(nested)).Uid);

        var listed = await Store.ListAsync(User, CancellationToken.None);

        Assert.Empty(Assert.Single(listed, g => g.Id == outer).MemberIds);
    }

    // A card imported with UID:urn:uuid:… stores that whole string; the MEMBER value stores it
    // stripped. Both forms are tried, or such a member never resolves.
    [Fact]
    public async Task ListAsync_ResolvesAMemberWhoseStoredUidCarriesTheUrnPrefix()
    {
        var bare = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();
        Context.Contacts.Add(new Contact
        {
            Id = id, UserId = User, Uid = "urn:uuid:" + bare, FirstName = "Ada",
            UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync(CancellationToken.None);
        await GivenAGroup("Amis", null, bare);

        var listed = await Store.ListAsync(User, CancellationToken.None);

        Assert.Equal(id, Assert.Single(Assert.Single(listed).MemberIds));
    }

    // Décision 7: the prefix is recognised whatever its case, and the retrait already is — a
    // contact "non-member" on reading and "member" on deletion is a card the book contradicts.
    [Fact]
    public async Task ListAsync_ResolvesAMemberWhoseStoredUidCarriesThePrefixInAnotherCase()
    {
        var bare = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();
        Context.Contacts.Add(new Contact
        {
            Id = id, UserId = User, Uid = "URN:UUID:" + bare, FirstName = "Ada",
            UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync(CancellationToken.None);
        await GivenAGroup("Amis", null, bare);

        var listed = await Store.ListAsync(User, CancellationToken.None);

        Assert.Equal(id, Assert.Single(Assert.Single(listed).MemberIds));
    }

    // The prefix alone is case-blind; the UID it carries is not, and the column collates binary.
    [Fact]
    public async Task ListAsync_LeavesAMemberWhoseUidDiffersOnlyByCaseOut()
    {
        var id = Guid.NewGuid();
        Context.Contacts.Add(new Contact
        {
            Id = id, UserId = User, Uid = "urn:uuid:ABC", FirstName = "Ada",
            UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync(CancellationToken.None);
        await GivenAGroup("Amis", null, "abc");

        var listed = await Store.ListAsync(User, CancellationToken.None);

        Assert.Empty(Assert.Single(listed).MemberIds);
    }

    // Nothing downstream sorts — the screen shows what it receives, and suggestionsFor cuts at
    // GROUP_LIMIT before any sort of its own. The order is therefore this method's contract.
    [Fact]
    public async Task ListAsync_OrdersTheGroupsByName()
    {
        await GivenAGroup("Work");
        await GivenAGroup("clients");
        await GivenAGroup("Émile");
        await GivenAGroup("Family");

        var listed = await Store.ListAsync(User, CancellationToken.None);

        Assert.Equal(["clients", "Émile", "Family", "Work"], listed.Select(g => g.Name));
    }

    [Fact]
    public async Task ListAsync_OnTwoGroupsOfOneName_OrdersThemByIdSoTheAnswerIsStable()
    {
        var first = await GivenAGroup("Amis");
        var second = await GivenAGroup("amis");
        Guid[] expected = [first, second];
        Array.Sort(expected);

        var listed = await Store.ListAsync(User, CancellationToken.None);

        Assert.Equal(expected, listed.Select(g => g.Id));
    }

    [Fact]
    public async Task ListAsync_LeavesAnotherBooksGroupOut()
    {
        await GivenAGroup("Amis", Guid.NewGuid());

        Assert.Empty(await Store.ListAsync(User, CancellationToken.None));
    }

    // ---- RenameAsync ----------------------------------------------------------------------------

    [Fact]
    public async Task RenameAsync_MovesOnlyTheFn()
    {
        var member = await GivenAContact("Ada");
        var group = await GivenAGroup("Amis", null, member.ToString());
        var before = (await RowOf(group)).VCardRaw!;

        var renamed = await Store.RenameAsync(User, group, "  Collègues  ", CancellationToken.None);

        Assert.True(renamed.IsSuccess);
        var after = (await RowOf(group)).VCardRaw!;
        Assert.Equal(before.Replace("FN:Amis", "FN:Collègues"), after);
        Assert.Equal("Collègues", (await RowOf(group)).DisplayName);
    }

    [Fact]
    public async Task RenameAsync_TakesARankAndArchivesWhatItReplaced()
    {
        var group = await GivenAGroup("Amis");
        Sync.Invocations.Clear();

        await Store.RenameAsync(User, group, "Collègues", CancellationToken.None);

        Assert.True((await RowOf(group)).SyncSequence > 1);
        Sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r =>
                r.Cause == RevisionCause.Webmail && r.ContactId == group && r.VCardRaw.Contains("FN:Amis")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameAsync_RefusesAnEmptyName()
    {
        var group = await GivenAGroup("Amis");

        Assert.True((await Store.RenameAsync(User, group, " ", CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task RenameAsync_OnAContact_AnswersNotFound()
    {
        var contact = await GivenAContact("Ada");

        var renamed = await Store.RenameAsync(User, contact, "Amis", CancellationToken.None);

        Assert.True(renamed.IsFailure);
        Assert.Equal(ContactStore.NotFound, renamed.Error);
    }

    [Fact]
    public async Task RenameAsync_OnAnotherBooksGroup_AnswersNotFound()
    {
        var group = await GivenAGroup("Amis", Guid.NewGuid());

        var renamed = await Store.RenameAsync(User, group, "Collègues", CancellationToken.None);

        Assert.True(renamed.IsFailure);
        Assert.Equal(ContactStore.NotFound, renamed.Error);
    }

    // ---- AddMembersAsync ------------------------------------------------------------------------

    [Fact]
    public async Task AddMembersAsync_WritesTheLineAndProjectsIt()
    {
        var member = await GivenAContact("Ada");
        var group = await GivenAGroup("Amis");

        var added = await Store.AddMembersAsync(User, group, [member], CancellationToken.None);

        Assert.True(added.IsSuccess);
        Assert.Contains($"urn:uuid:{member}", (await RowOf(group)).VCardRaw);
        Assert.Equal([member.ToString()], await MemberUidsOf(group));
    }

    [Fact]
    public async Task AddMembersAsync_WritesEveryMemberOfTheBatchInOneCall()
    {
        var first = await GivenAContact("Ada");
        var second = await GivenAContact("Grace");
        var group = await GivenAGroup("Amis");

        var added = await Store.AddMembersAsync(User, group, [first, second], CancellationToken.None);

        Assert.True(added.IsSuccess);
        var card = (await RowOf(group)).VCardRaw!;
        Assert.Contains($"urn:uuid:{first}", card);
        Assert.Contains($"urn:uuid:{second}", card);
        Assert.Equal([first.ToString(), second.ToString()], await MemberUidsOf(group));
    }

    // A batch straddling both states: the already-held id must fall out of the delta, or the card
    // grows a second MEMBER line for it — and the whole batch must still cost exactly one rank.
    [Fact]
    public async Task AddMembersAsync_OnAMixedBatch_WritesOnlyTheDeltaAndTakesOneRank()
    {
        var held = await GivenAContact("Ada");
        var newcomer = await GivenAContact("Grace");
        var group = await GivenAGroup("Amis", null, held.ToString());
        Sync.Invocations.Clear();

        var added = await Store.AddMembersAsync(User, group, [held, newcomer], CancellationToken.None);

        Assert.True(added.IsSuccess);
        var card = (await RowOf(group)).VCardRaw!;
        Assert.Equal(1, Occurrences(card, $"urn:uuid:{held}"));
        Assert.Equal(1, Occurrences(card, $"urn:uuid:{newcomer}"));
        Assert.Equal([held.ToString(), newcomer.ToString()], await MemberUidsOf(group));
        Sync.Verify(s => s.NextSequenceAsync(User, It.IsAny<CancellationToken>()), Times.Once);
    }

    // AddGroupMember prefixes urn:uuid: itself, so a stored UID already carrying it must be
    // stripped before it is written — or the line names urn:uuid:urn:uuid:… and resolves to nobody.
    [Fact]
    public async Task AddMembersAsync_OnAContactWhoseUidCarriesThePrefix_WritesItOnlyOnce()
    {
        var bare = Guid.NewGuid().ToString();
        var member = Guid.NewGuid();
        Context.Contacts.Add(new Contact
        {
            Id = member, UserId = User, Uid = "urn:uuid:" + bare, FirstName = "Ada",
            UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync(CancellationToken.None);
        var group = await GivenAGroup("Amis");

        var added = await Store.AddMembersAsync(User, group, [member], CancellationToken.None);

        Assert.True(added.IsSuccess);
        var card = (await RowOf(group)).VCardRaw!;
        Assert.Contains($"X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:{bare}", card);
        Assert.DoesNotContain("urn:uuid:urn:uuid:", card);
        Assert.Equal([bare], await MemberUidsOf(group));
        // Both forms resolve, so the member the screen shows is the contact that was added.
        Assert.Equal(member, Assert.Single(Assert.Single(await Store.ListAsync(User, CancellationToken.None)).MemberIds));
    }

    private static int Occurrences(string card, string value) =>
        card.Split(value).Length - 1;

    [Fact]
    public async Task AddMembersAsync_TakesARankAndArchives()
    {
        var member = await GivenAContact("Ada");
        var group = await GivenAGroup("Amis");
        Sync.Invocations.Clear();

        await Store.AddMembersAsync(User, group, [member], CancellationToken.None);

        Sync.Verify(s => s.NextSequenceAsync(User, It.IsAny<CancellationToken>()), Times.Once);
        Sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Webmail),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // An unknown id, another book's, and a group's — all three resolve to nothing, and a batch that
    // resolves to nothing is a silent no-op rather than a 404 confirming what exists.
    [Fact]
    public async Task AddMembersAsync_SkipsAnUnknownAForeignAndAGroupId()
    {
        var foreign = await GivenAContact("Ada", Guid.NewGuid());
        var nested = await GivenAGroup("Collègues");
        var group = await GivenAGroup("Amis");
        Sync.Invocations.Clear();

        var added = await Store.AddMembersAsync(
            User, group, [Guid.NewGuid(), foreign, nested], CancellationToken.None);

        Assert.True(added.IsSuccess);
        Assert.Empty(await MemberUidsOf(group));
        Sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddMembersAsync_OnAnAlreadyHeldMember_TakesNeitherRankNorRevision()
    {
        var member = await GivenAContact("Ada");
        var group = await GivenAGroup("Amis", null, member.ToString());
        Sync.Invocations.Clear();

        var added = await Store.AddMembersAsync(User, group, [member], CancellationToken.None);

        Assert.True(added.IsSuccess);
        Assert.Equal([member.ToString()], await MemberUidsOf(group));
        Sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddMembersAsync_OnAnotherBooksGroup_AnswersNotFound()
    {
        var member = await GivenAContact("Ada");
        var group = await GivenAGroup("Amis", Guid.NewGuid());

        var added = await Store.AddMembersAsync(User, group, [member], CancellationToken.None);

        Assert.True(added.IsFailure);
        Assert.Equal(ContactStore.NotFound, added.Error);
    }

    [Fact]
    public async Task AddMembersAsync_OnAContact_AnswersNotFound()
    {
        var contact = await GivenAContact("Ada");

        var added = await Store.AddMembersAsync(User, contact, [contact], CancellationToken.None);

        Assert.True(added.IsFailure);
        Assert.Equal(ContactStore.NotFound, added.Error);
    }

    // ---- RemoveMembersAsync ---------------------------------------------------------------------

    [Fact]
    public async Task RemoveMembersAsync_DropsTheLineAndTheProjection()
    {
        var stays = await GivenAContact("Ada");
        var goes = await GivenAContact("Grace");
        var group = await GivenAGroup("Amis", null, stays.ToString(), goes.ToString());

        var removed = await Store.RemoveMembersAsync(User, group, [goes], CancellationToken.None);

        Assert.True(removed.IsSuccess);
        Assert.DoesNotContain($"urn:uuid:{goes}", (await RowOf(group)).VCardRaw);
        Assert.Equal([stays.ToString()], await MemberUidsOf(group));
        // The contact itself survives its group (décision 7).
        Assert.True(await Context.Contacts.AnyAsync(c => c.Id == goes, CancellationToken.None));
    }

    // Régression : retirer le PREMIER des deux membres renumérote le survivant de 1 à 0. Sous une
    // clé (group_id, position) ce survivant changeait de clé primaire, EF le suivait comme une
    // paire Deleted+Added et émettait l'INSERT avant le DELETE — MySQL rendait « Duplicate entry
    // for key 'uq_group_member' ». InMemory ne prouve pas l'ordre du SQL ; ce qu'il épingle, c'est
    // que la clé ne bouge plus, donc que la paire fusionne en un seul UPDATE de position.
    [Fact]
    public async Task RemoveMembersAsync_OfTheFirstMember_RenumbersTheSurvivorInPlace()
    {
        var goes = await GivenAContact("Ada");
        var stays = await GivenAContact("Grace");
        var group = await GivenAGroup("Amis", null, goes.ToString(), stays.ToString());

        var removed = await Store.RemoveMembersAsync(User, group, [goes], CancellationToken.None);

        Assert.True(removed.IsSuccess);
        var survivor = Assert.Single(Context.ContactGroupMembers.Where(m => m.GroupId == group));
        Assert.Equal(stays.ToString(), survivor.MemberUid);
        Assert.Equal(0, survivor.Position);
    }

    [Fact]
    public async Task RemoveMembersAsync_OfSomeoneWhoIsNotAMember_TakesNeitherRankNorRevision()
    {
        var stranger = await GivenAContact("Ada");
        var group = await GivenAGroup("Amis");
        Sync.Invocations.Clear();

        var removed = await Store.RemoveMembersAsync(User, group, [stranger], CancellationToken.None);

        Assert.True(removed.IsSuccess);
        Sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveMembersAsync_OnAnotherBooksGroup_AnswersNotFound()
    {
        var member = await GivenAContact("Ada");
        var group = await GivenAGroup("Amis", Guid.NewGuid(), member.ToString());

        var removed = await Store.RemoveMembersAsync(User, group, [member], CancellationToken.None);

        Assert.True(removed.IsFailure);
        Assert.Equal(ContactStore.NotFound, removed.Error);
    }

    // ---- DeleteAsync ----------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_RemovesTheGroupAndItsMembersAndBuriesTheName()
    {
        var member = await GivenAContact("Ada");
        var group = await GivenAGroup("Amis", null, member.ToString());
        Sync.Invocations.Clear();

        var deleted = await Store.DeleteAsync(User, group, CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.False(await Context.Contacts.AnyAsync(c => c.Id == group, CancellationToken.None));
        Assert.Empty(await MemberUidsOf(group));
        // The members are contacts of their own; deleting what listed them deletes none of them.
        Assert.True(await Context.Contacts.AnyAsync(c => c.Id == member, CancellationToken.None));
        Sync.Verify(s => s.PlaceTombstoneAsync(
            User, $"{group}.vcf", It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Once);
        Sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Delete && r.ContactId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_OnAContact_AnswersNotFound()
    {
        var contact = await GivenAContact("Ada");

        var deleted = await Store.DeleteAsync(User, contact, CancellationToken.None);

        Assert.True(deleted.IsFailure);
        Assert.Equal(ContactStore.NotFound, deleted.Error);
        Assert.True(await Context.Contacts.AnyAsync(c => c.Id == contact, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_OnAnotherBooksGroup_AnswersNotFound()
    {
        var group = await GivenAGroup("Amis", Guid.NewGuid());

        var deleted = await Store.DeleteAsync(User, group, CancellationToken.None);

        Assert.True(deleted.IsFailure);
        Assert.Equal(ContactStore.NotFound, deleted.Error);
    }
}
