using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Services.Contacts;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

/// <summary>
/// Décision 7: a deleted contact leaves every group that carries it, inside the deleting
/// transaction — the card must say what the book knows. Three doors delete a contact, and all
/// three answer for it here.
/// </summary>
public sealed class ContactStoreGroupStripTests
{
    private readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Deleting_StripsTheMemberFromTheGroupCardInBothValueForms()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 42);
        var store = new ContactStore(context, sync.Object);
        var contact = (await store.CreateAsync(
            UserId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None)).Value;
        var uid = contact.ToString();

        // Both spellings the reader accepts, on one card: the bare UID and the urn:uuid: form.
        var group = await GivenAGroup(context, "Amis", [uid, VCardProjector.UrnUuidPrefix + uid]);
        sync.Invocations.Clear();

        Assert.True((await store.DeleteAsync(UserId, contact, CancellationToken.None)).IsSuccess);

        var card = (await context.Contacts.SingleAsync(c => c.Id == group, CancellationToken.None));
        Assert.DoesNotContain(uid, card.VCardRaw, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ContactGroupMembers.Where(m => m.GroupId == group));
        Assert.Equal(42ul, card.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.ContactId == group && r.Cause == RevisionCause.Webmail),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deleting_LeavesAGroupThatNeverCarriedItAlone()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 42);
        var store = new ContactStore(context, sync.Object);
        var contact = (await store.CreateAsync(
            UserId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None)).Value;
        var group = await GivenAGroup(context, "Amis", [Guid.NewGuid().ToString()]);
        var before = (await context.Contacts.SingleAsync(c => c.Id == group, CancellationToken.None)).CardHash;
        sync.Invocations.Clear();

        await store.DeleteAsync(UserId, contact, CancellationToken.None);

        // A rank and a revision on a card nothing changed wakes every client of this book for
        // nothing — and the tests below rest on that silence to prove the exclusion.
        var row = await context.Contacts.SingleAsync(c => c.Id == group, CancellationToken.None);
        Assert.Equal(before, row.CardHash);
        Assert.Equal(1ul, row.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Webmail),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The book may still name a member the card no longer carries — a card replaced over DAV
    /// leaves that window open. The retrait then changes nothing, and nothing is what it costs:
    /// no MEDIUMTEXT revision, no rank, no UpdatedAt.
    /// </summary>
    [Fact]
    public async Task Deleting_WritesNothingIntoAGroupWhoseCardNoLongerCarriesTheMember()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 42);
        var store = new ContactStore(context, sync.Object);
        var contact = await GivenAContact(context, "u1");
        var group = await GivenAGroup(context, "Amis", []);

        // The desynchronisation, posed by hand: the row names u1, the card never did.
        context.ContactGroupMembers.Add(
            new ContactGroupMember { GroupId = group, MemberUid = "u1", Position = 0 });
        await context.SaveChangesAsync(CancellationToken.None);
        var before = await context.Contacts.AsNoTracking()
            .SingleAsync(c => c.Id == group, CancellationToken.None);
        sync.Invocations.Clear();

        Assert.True((await store.DeleteAsync(UserId, contact, CancellationToken.None)).IsSuccess);

        var row = await context.Contacts.SingleAsync(c => c.Id == group, CancellationToken.None);
        Assert.Equal(before.VCardRaw, row.VCardRaw);
        Assert.Equal(before.CardHash, row.CardHash);
        Assert.Equal(before.UpdatedAt, row.UpdatedAt);
        Assert.Equal(1ul, row.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.ContactId == group), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeletingAContactWhoseUidCarriesThePrefix_StillStripsIt()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);

        // The asymmetry Forms() exists for: the column keeps the UID a foreign card brought,
        // prefix included, while the member row only ever holds the stripped value.
        var uid = VCardProjector.UrnUuidPrefix + "11111111-1111-1111-1111-111111111111";
        var contact = await GivenAContact(context, uid);
        var group = await GivenAGroup(context, "Amis", [uid]);

        Assert.True((await store.DeleteAsync(UserId, contact, CancellationToken.None)).IsSuccess);

        var card = (await context.Contacts.SingleAsync(c => c.Id == group, CancellationToken.None)).VCardRaw;
        Assert.DoesNotContain("MEMBER", card, StringComparison.Ordinal);
        Assert.Empty(context.ContactGroupMembers.Where(m => m.GroupId == group));
    }

    [Fact]
    public async Task DeletingOverDav_StripsTheMemberFromTheGroupCard()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSyncCounting(first: 7);
        var store = new ContactStore(context, sync.Object);
        var writer = new DavContactWriter(
            context, store, sync.Object, Mock.Of<ILogger<DavContactWriter>>());
        var contact = await GivenAContact(context, "u1");
        var group = await GivenAGroup(context, "Amis", ["u1"]);

        var outcome = await writer.DeleteAsync(UserId, "u1.vcf", CancellationToken.None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        Assert.Empty(context.Contacts.Where(c => c.Id == contact));
        var card = await context.Contacts.SingleAsync(c => c.Id == group, CancellationToken.None);
        Assert.DoesNotContain("MEMBER", card.VCardRaw, StringComparison.Ordinal);
        Assert.Empty(context.ContactGroupMembers.Where(m => m.GroupId == group));
        // The rank of the deleting transaction, never one of its own.
        Assert.Equal(outcome.Sequence, card.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.ContactId == group && r.Cause == RevisionCause.Webmail),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Décision 7 holds on the group door too: a group nested in another (décision 9 stores the
    /// member unresolved) leaves its parent when it is deleted, or the parent keeps a MEMBER line
    /// and a membership row pointing at a card that no longer exists.
    /// </summary>
    [Fact]
    public async Task DeletingAGroupThroughTheGroupStore_StripsItFromItsParents()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSyncCounting(first: 7);
        var store = new ContactStore(context, sync.Object);
        var nested = await GivenAGroup(context, "Collègues", []);
        var parent = await GivenAGroup(
            context, "Amis", [(await context.Contacts.SingleAsync(c => c.Id == nested)).Uid]);
        sync.Invocations.Clear();

        var deleted = await new ContactGroupStore(context, store, sync.Object)
            .DeleteAsync(UserId, nested, CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        var row = await context.Contacts.SingleAsync(c => c.Id == parent, CancellationToken.None);
        Assert.DoesNotContain("MEMBER", row.VCardRaw, StringComparison.Ordinal);
        Assert.Empty(context.ContactGroupMembers.Where(m => m.GroupId == parent));
        Assert.Equal(7ul, row.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.ContactId == parent && r.Cause == RevisionCause.Webmail),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A PUT accepted under the same href with a different UID re-keys the groups rather than
    /// orphaning them: the contact stays a member, and the group card names the identity that now
    /// exists. Same accounting as the retrait — one rank, one revision, on the group.
    /// </summary>
    [Fact]
    public async Task PuttingANewUidUnderTheSameHref_RekeysTheGroupsThatCarriedTheOldOne()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSyncCounting(first: 7);
        var store = new ContactStore(context, sync.Object);
        var writer = new DavContactWriter(
            context, store, sync.Object, Mock.Of<ILogger<DavContactWriter>>());
        var contact = await GivenAContact(context, "u1");
        var group = await GivenAGroup(context, "Amis", ["u1"]);

        var outcome = await writer.PutAsync(UserId, "u1.vcf",
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u2\r\nFN:Ada\r\nEND:VCARD\r\n", CancellationToken.None);

        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
        var card = await context.Contacts.SingleAsync(c => c.Id == group, CancellationToken.None);
        Assert.Contains("X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:u2", card.VCardRaw, StringComparison.Ordinal);
        Assert.DoesNotContain(":u1", card.VCardRaw, StringComparison.Ordinal);
        Assert.Equal(["u2"], context.ContactGroupMembers
            .Where(m => m.GroupId == group).Select(m => m.MemberUid).ToList());
        Assert.Equal(outcome.Sequence, card.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.ContactId == group && r.Cause == RevisionCause.Webmail),
            It.IsAny<CancellationToken>()), Times.Once);

        // The whole point of the re-key: the screen still shows the contact under its group.
        var groups = await new ContactGroupStore(context, store, sync.Object)
            .ListAsync(UserId, CancellationToken.None);
        Assert.Equal(contact, Assert.Single(Assert.Single(groups).MemberIds));
    }

    /// <summary>
    /// The contract of the batch door, and it only holds beyond one batch: the exclusion is
    /// computed on the ids handed to the method, never on the slice being processed, or a group
    /// the list also carries would be rewritten by the slice that precedes its own burial.
    /// </summary>
    [Fact]
    public async Task EmptyingBeyondOneBatch_NeverWritesIntoAGroupTheListAlsoTakes()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSyncCounting();
        var store = new ContactStore(context, sync.Object);

        var contacts = new List<Guid>();
        var uids = new List<string>();
        for (var i = 0; i < ContactStore.BatchSize + 5; i++)
        {
            var uid = $"c{i}";
            contacts.Add(await GivenAContact(context, uid));
            uids.Add(uid);
        }

        // One member from each slice, on both groups: the first is buried with the list, the
        // second survives it and must therefore be touched twice.
        var dying = await GivenAGroup(context, "Emportés", [uids[0], uids[102]]);
        var surviving = await GivenAGroup(context, "Restants", [uids[0], uids[102]]);

        var removed = await store.DeleteManyAsync(
            UserId, [.. contacts, dying], includeGroups: true, CancellationToken.None);

        Assert.Equal(ContactStore.BatchSize + 6, removed);
        Assert.Empty(context.Contacts.Where(c => c.Id == dying));
        // A condemned card takes no rank and leaves no Webmail revision: it is about to be buried,
        // and rewriting it would publish a version no client will ever read.
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.ContactId == dying && r.Cause == RevisionCause.Webmail),
            It.IsAny<CancellationToken>()), Times.Never);

        var row = await context.Contacts.SingleAsync(c => c.Id == surviving, CancellationToken.None);
        Assert.DoesNotContain("MEMBER", row.VCardRaw, StringComparison.Ordinal);
        Assert.Empty(context.ContactGroupMembers.Where(m => m.GroupId == surviving));
        // Two slices touched it, so two transactions rewrote it: the last rank taken is its own,
        // and the two revisions are the proof it was published between them.
        Assert.Equal(2ul, row.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.ContactId == surviving && r.Cause == RevisionCause.Webmail),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeletingABatchThatNamesAGroupItDoesNotTake_StillStripsThatGroup()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSyncCounting(first: 9);
        var store = new ContactStore(context, sync.Object);
        var contact = await GivenAContact(context, "u1");
        var group = await GivenAGroup(context, "Amis", ["u1"]);

        // The webmail's bulk door takes no group: an id naming one is skipped in silence, so it is
        // a SURVIVOR — excluding it from the strip would leave it pointing at an erased contact.
        var removed = await store.DeleteManyAsync(
            UserId, [contact, group], includeGroups: false, CancellationToken.None);

        Assert.Equal(1, removed);
        var row = await context.Contacts.SingleAsync(c => c.Id == group, CancellationToken.None);
        Assert.DoesNotContain("MEMBER", row.VCardRaw, StringComparison.Ordinal);
        Assert.Equal(9ul, row.SyncSequence);
    }

    private async Task<Guid> GivenAContact(PreferencesDbContext context, string uid)
    {
        var id = Guid.NewGuid();
        var card = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{uid}\r\nFN:Ada\r\nEND:VCARD\r\n";
        context.Contacts.Add(new Contact
        {
            Id = id, UserId = UserId, Uid = uid, VCardRaw = card,
            CardHash = ContactStore.CardHashOf(card), DavName = $"{uid}.vcf",
            UpdatedAt = DateTime.UtcNow, SyncSequence = 1
        });
        await context.SaveChangesAsync(CancellationToken.None);
        return id;
    }

    /// <summary>
    /// A stored group and its member rows, posed rather than PUT: what the strip has to agree
    /// with is the pair — the card and the table the projection derives from it.
    /// </summary>
    private async Task<Guid> GivenAGroup(
        PreferencesDbContext context, string name, IReadOnlyList<string> memberValues)
    {
        var id = Guid.NewGuid();
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:" + id + $"\r\nFN:{name}\r\n"
            + "X-ADDRESSBOOKSERVER-KIND:group\r\n"
            + string.Concat(memberValues.Select(m => $"X-ADDRESSBOOKSERVER-MEMBER:{m}\r\n"))
            + "END:VCARD\r\n";
        context.Contacts.Add(new Contact
        {
            Id = id, UserId = UserId, Uid = id.ToString(), Kind = ContactKinds.Group,
            DisplayName = name, VCardRaw = card, CardHash = ContactStore.CardHashOf(card),
            DavName = $"{id}.vcf", UpdatedAt = DateTime.UtcNow, SyncSequence = 1
        });

        var position = 0;
        foreach (var member in memberValues.Select(VCardProjector.StripUrnUuid).Distinct(StringComparer.Ordinal))
            context.ContactGroupMembers.Add(new ContactGroupMember
            {
                GroupId = id, MemberUid = member, Position = position++
            });

        await context.SaveChangesAsync(CancellationToken.None);
        return id;
    }
}
