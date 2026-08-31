using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class DavContactWriterTests : IDisposable
{
    private readonly PreferencesTestDbContext Context;
    private readonly Mock<IContactSyncStore> SyncStore;
    private readonly DavContactWriter Writer;
    private readonly Guid UserId = Guid.NewGuid();

    public DavContactWriterTests()
    {
        Context = ContactStoreTestFactory.NewContext();
        SyncStore = ContactStoreTestFactory.NewSyncCounting();
        Writer = NewWriter(Context, SyncStore);
    }

    public void Dispose() => Context.Dispose();

    [Fact]
    public async Task PuttingANewName_Creates()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.Created, outcome.Status);
        Assert.NotNull(outcome.Etag);
        Assert.Equal(1ul, outcome.Sequence);
    }

    [Fact]
    public async Task APutCard_KeepsItsOwnUidAndTakesTheDavSource()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        var row = await RowOf("a.vcf");
        // The card's UID is the identity the client syncs on: reading back another duplicates it.
        Assert.Equal("u1", row.Uid);
        Assert.Equal("carddav", row.Source);
    }

    [Fact]
    public async Task PuttingOverAnExistingName_ReplacesAndArchives()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Ada"), CancellationToken.None);
        SyncStore.Invocations.Clear();

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Grace"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
        // The replaced bytes, under the Put cause — not the incoming ones.
        SyncStore.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Put && r.VCardRaw.Contains("FN:Ada")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhatSurvivesAReplacement_IsIdAndFavouriteAndSource()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
        await GivenTheContactIsFavourite("a.vcf");
        var before = await RowOf("a.vcf");
        before.Source = "imported";
        await Context.SaveChangesAsync(CancellationToken.None);
        var idBefore = before.Id;

        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Grace"), CancellationToken.None);

        // No new write path, no business rule duplicated: everything else is a projection and is
        // recomputed.
        var row = await RowOf("a.vcf");
        Assert.Equal(idBefore, row.Id);
        Assert.True(row.IsFavorite);
        Assert.Equal("imported", row.Source);
        Assert.Equal("Grace", row.FirstName);
    }

    [Fact]
    public async Task PuttingOverATombstonedName_LiftsItInTheSameTransaction()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
        await Writer.DeleteAsync(UserId, "a.vcf", CancellationToken.None);
        SyncStore.Invocations.Clear();

        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u2"), CancellationToken.None);

        // A tombstone and a living card must never coexist on one name: a sync-collection would
        // return both, and the order the client applies them in would decide whether it keeps the
        // card or erases it.
        SyncStore.Verify(s => s.LiftTombstoneAsync(UserId, "a.vcf", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AUidHeldByAnotherResource_IsRefusedWithItsHref()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("shared-uid"), CancellationToken.None);

        var outcome = await Writer.PutAsync(UserId, "b.vcf", ValidCard("shared-uid"), CancellationToken.None);

        // The unique index (user_id, uid) laid by 4a IS this guard: translating its violation is all
        // that is needed, rather than letting it come back as a 500. And without the href the client
        // knows it failed but not what to re-read — its only remaining move is to retry identically.
        Assert.Equal(DavWriteStatus.UidConflict, outcome.Status);
        Assert.Equal(DavPaths.Card(UserId, "a.vcf"), outcome.ConflictHref);
        Assert.Single(Context.Contacts.Where(c => c.UserId == UserId));
    }

    [Fact]
    public async Task AUidChangedUnderTheSameName_IsAcceptedAndTheOldIdentityArchived()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u2"), CancellationToken.None);

        // RFC 6352 § 6.3.2.1 defines no-uid-conflict for a UID ANOTHER resource holds, not for one
        // that changes under its own name; sabre accepts it. Refused, a client that regenerated a
        // UID would loop on the 403 for ever — DAVx5 never abandons one — on a card nothing holds.
        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
        var row = await RowOf("a.vcf");
        Assert.Equal("u2", row.Uid);
        Assert.Contains("UID:u2", row.VCardRaw);
        SyncStore.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Uid == "u1" && r.VCardRaw.Contains("UID:u1")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnUnconditionalPut_LandingAfterAnotherWrite_ArchivesThatWriteNotItsOwnRead()
    {
        // No If-Match, so nothing refuses the second writer — but the revision it archives must be
        // the version stored when it took the lock, not the one it read before: otherwise the
        // winner's card never enters contact_revisions, the table whose whole job is to lose nothing.
        var database = Guid.NewGuid().ToString();
        using var context = new PreferencesTestDbContext(database);
        await NewWriter(context, ContactStoreTestFactory.NewSyncCounting())
            .PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "First"), CancellationToken.None);
        var racing = RacingSync(() => Replace(database, "a.vcf", ValidCard("u1", fn: "Winner")));

        var outcome = await NewWriter(context, racing)
            .PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Loser"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
        racing.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.VCardRaw.Contains("FN:Winner")), It.IsAny<CancellationToken>()),
            Times.Once);
        using var check = new PreferencesTestDbContext(database);
        Assert.Contains("FN:Loser", check.Contacts.Single(
            c => c.UserId == UserId && c.DavName == "a.vcf").VCardRaw);
    }

    [Fact]
    public async Task AnUnconditionalDelete_LandingAfterAReplacement_ArchivesTheReplacement()
    {
        var database = Guid.NewGuid().ToString();
        using var context = new PreferencesTestDbContext(database);
        await NewWriter(context, ContactStoreTestFactory.NewSyncCounting())
            .PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "First"), CancellationToken.None);
        var racing = RacingSync(() => Replace(database, "a.vcf", ValidCard("u1", fn: "Winner")));

        var outcome = await NewWriter(context, racing).DeleteAsync(UserId, "a.vcf", CancellationToken.None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        racing.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.VCardRaw.Contains("FN:Winner")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AUidChangedToOneAnotherResourceHolds_NamesThatResource()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
        await Writer.PutAsync(UserId, "b.vcf", ValidCard("u2"), CancellationToken.None);

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u2"), CancellationToken.None);

        // The same refusal as above, but here a resource genuinely holds u2 — and it is b.vcf the
        // client must go and read, never the a.vcf it was writing. The two cases share a status and
        // differ only in this href, which is the whole of what the client can act on.
        Assert.Equal(DavWriteStatus.UidConflict, outcome.Status);
        Assert.Equal(DavPaths.Card(UserId, "b.vcf"), outcome.ConflictHref);
        Assert.Contains("UID:u1", (await RowOf("a.vcf")).VCardRaw);
        Assert.Contains("UID:u2", (await RowOf("b.vcf")).VCardRaw);
    }

    [Fact]
    public async Task ABodyThatDoesNotParse_IsRefusedAsInvalidCard()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf", "not a vcard at all", CancellationToken.None);

        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
        Assert.Empty(Context.Contacts);
    }

    [Fact]
    public async Task ABodyCarryingTwoCards_IsRefused()
    {
        var outcome = await Writer.PutAsync(
            UserId, "a.vcf", ValidCard("u1") + ValidCard("u2"), CancellationToken.None);

        // An address resource is ONE card (§ 5.1). This is the point the 4a residual announced for
        // this slice — VCardProjector would keep the first card and lose the second in silence —
        // and the explicit refusal must PRECEDE the projection, not follow it.
        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
        Assert.Empty(Context.Contacts);
    }

    [Fact]
    public async Task DebrisBeforeTheCard_IsRefused()
    {
        var outcome = await Writer.PutAsync(
            UserId, "a.vcf", "debris\r\n" + ValidCard("u1"), CancellationToken.None);

        // The bytes are stored as they arrive, so debris around the card would be served back as
        // part of the resource — and DAVx5 would fail to parse it on every sync from then on.
        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
        Assert.Empty(Context.Contacts);
    }

    [Fact]
    public async Task DebrisAfterTheCard_IsRefusedToo()
    {
        var outcome = await Writer.PutAsync(
            UserId, "a.vcf", ValidCard("u1") + "debris", CancellationToken.None);

        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
        Assert.Empty(Context.Contacts);
    }

    [Fact]
    public async Task WhitespaceAroundTheCard_IsToleratedAndStoredVerbatim()
    {
        // The guard refuses content, not air: a trailing blank line is what hand-edited exports
        // carry, and refusing it would refuse correct clients over nothing.
        var wrapped = "\r\n" + ValidCard("u1") + "\r\n";

        var outcome = await Writer.PutAsync(UserId, "a.vcf", wrapped, CancellationToken.None);

        Assert.Equal(DavWriteStatus.Created, outcome.Status);
        Assert.Equal(wrapped, (await RowOf("a.vcf")).VCardRaw);
    }

    [Theory]
    [InlineData("\u0007")]
    [InlineData("\u0000")]
    [InlineData("\u001F")]
    [InlineData("\u007F")]
    public async Task AControlCharacterInAValue_IsRefused(string control)
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf",
            CardWithNote($"Illegal control here ->{control}<-"), CancellationToken.None);

        // RFC 2426's ABNF excludes CTL from every value, and the bytes are stored as they arrive:
        // accepted, a BEL is served back on every sync to clients that will not parse it.
        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
        Assert.Empty(Context.Contacts);
        AssertNoRankWasTaken();
    }

    [Fact]
    public async Task AControlCharacterEndingAValue_IsRefusedToo()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf",
            CardWithNote("Trailing bell\u0007"), CancellationToken.None);

        // The last character before the CRLF: a scan stopping at the line's own terminator would
        // read the card as clean and store the bell anyway.
        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
        Assert.Empty(Context.Contacts);
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\r\n ")]
    public async Task AnHtabOrAFoldInAValue_IsStillAccepted(string separator)
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf",
            CardWithNote($"Before{separator}after"), CancellationToken.None);

        // CR, LF and HTAB are what folding and real values are MADE of: refusing them would refuse
        // the correct clients the guard exists to protect.
        Assert.Equal(DavWriteStatus.Created, outcome.Status);
    }

    [Theory]
    [InlineData("UID:u2")]
    [InlineData("item1.UID:u2")]
    public async Task ACardCarryingTwoUids_IsInvalid_NotAUidConflict(string second)
    {
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\n" + second + "\r\nEND:VCARD\r\n";

        var outcome = await Writer.PutAsync(UserId, "a.vcf", card, CancellationToken.None);

        // RFC 6352 § 5.1: one resource, one vCard, one identity. Answered no-uid-conflict, the
        // client is told to go and read an href that names nothing — the group prefix counts, or
        // the second UID hides behind an "item1." the reader would never look past.
        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
        Assert.Empty(Context.Contacts);
        AssertNoRankWasTaken();
    }

    [Fact]
    public async Task ALogicalLineWithoutAColon_IsRefused()
    {
        // The tester's verrors/3: an ADR value spilled onto its own physical line, unfolded, so
        // "View;CA;94040;USA" is a logical line that is no contentline at all.
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\n" +
            "ADR;type=WORK:;;2 Fidel Ave.;Mountain\r\nView;CA;94040;USA\r\nEND:VCARD\r\n";

        var outcome = await Writer.PutAsync(UserId, "a.vcf", card, CancellationToken.None);

        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
        Assert.Empty(Context.Contacts);
        AssertNoRankWasTaken();
    }

    [Fact]
    public async Task AColonInsideQuotedParameters_StillCountsAsAContentline()
    {
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\n" +
            "TEL;TYPE=\"WORK;HOME\":555\r\nEND:VCARD\r\n";

        var outcome = await Writer.PutAsync(UserId, "a.vcf", card, CancellationToken.None);

        // The separator is judged OUTSIDE quotes, and a quoted parameter value may legally carry
        // both a ';' and a ':' — reading the first ':' anywhere would be a different rule.
        Assert.Equal(DavWriteStatus.Created, outcome.Status);
    }

    [Fact]
    public async Task ACardWithNoVersionAtAll_IsInvalid_NotUnsupported()
    {
        var outcome = await Writer.PutAsync(
            UserId, "a.vcf", CardWithoutVersion(), CancellationToken.None);

        // The two conditions are NOT interchangeable: supported-address-data tells the client to
        // re-export and retry; valid-address-data tells it the card itself is no card — VERSION is
        // mandatory in 3.0 and 4.0 alike, so a card without one is no card of either.
        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
    }

    [Fact]
    public async Task AVersionWeDoNotAnnounce_HasItsOwnCondition()
    {
        var outcome = await Writer.PutAsync(
            UserId, "a.vcf", CardOfVersion("2.1"), CancellationToken.None);

        // Old Android exports still produce 2.1. A card can be perfectly readable while being
        // refusable, and the two conditions say different things to the client.
        Assert.Equal(DavWriteStatus.UnsupportedVersion, outcome.Status);
    }

    [Fact]
    public async Task ACardOverTheCeiling_IsRefusedAsTooLarge()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf", HugeCard(), CancellationToken.None);

        Assert.Equal(DavWriteStatus.TooLarge, outcome.Status);
    }

    [Fact]
    public async Task AFullBook_IsRefusedAsBookFull()
    {
        await GivenTheBookIsFull();

        var outcome = await Writer.PutAsync(UserId, "new.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.BookFull, outcome.Status);
    }

    [Fact]
    public async Task AFullBook_StillAcceptsAReplacement()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Ada"), CancellationToken.None);
        await GivenTheBookIsFull(ContactStore.MaxPerUser - 1);

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Grace"), CancellationToken.None);

        // The ceiling bounds the count of contacts; a replacement adds none.
        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
    }

    [Fact]
    public async Task AStoredCardDifferingFromWhatWasSent_AnswersNoEtag()
    {
        // 4a inserts a UID into a card that declares none — the invariant holds for every stored
        // card. When that happens on a PUT, what is stored differs from what was sent, and the RFC
        // then requires NO ETag so the client re-reads. Returning the stored bytes' ETag would be
        // WORSE than none: the client would believe it holds the card it sent, and never re-read.
        var outcome = await Writer.PutAsync(UserId, "a.vcf", CardWithoutUid(), CancellationToken.None);

        Assert.Equal(DavWriteStatus.Created, outcome.Status);
        Assert.Null(outcome.Etag);
    }

    [Fact]
    public async Task ACardDeclaringNoUid_IsStampedWithTheStoredIdentity()
    {
        await Writer.PutAsync(UserId, "a.vcf", CardWithoutUid(), CancellationToken.None);

        var row = await RowOf("a.vcf");
        Assert.True(Guid.TryParse(row.Uid, out _));
        Assert.Contains($"UID:{row.Uid}", row.VCardRaw);
    }

    [Fact]
    public async Task AnUntransformedCard_AnswersItsEtag()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.NotNull(outcome.Etag);
        // The very tag a later GET or PROPFIND serves for the row, quotes included.
        Assert.Equal($"\"{(await RowOf("a.vcf")).CardHash}\"", outcome.Etag);
    }

    [Fact]
    public async Task TheBytesAreStoredAsTheyArrive_LineEndingsIncluded()
    {
        const string lfOnly = "BEGIN:VCARD\nVERSION:3.0\nUID:u1\nFN:Ada\nEND:VCARD\n";

        await Writer.PutAsync(UserId, "a.vcf", lfOnly, CancellationToken.None);

        // Normalising would be a TRANSFORMATION — hence a response with no ETag, a re-read, and a
        // card that never coincides with the client's. The server's job is to hand any other client
        // exactly what it received, and it is also what makes card_hash the SHA-256 of what is served.
        Assert.Equal(lfOnly, (await RowOf("a.vcf")).VCardRaw);
    }

    [Fact]
    public async Task AByteIdenticalRePut_TakesNoRankAndKeepsItsEtag()
    {
        var card = ValidCard("u1");
        var first = await Writer.PutAsync(UserId, "a.vcf", card, CancellationToken.None);
        SyncStore.Invocations.Clear();

        var second = await Writer.PutAsync(UserId, "a.vcf", card, CancellationToken.None);

        // The idempotent retry every DAV client makes: nothing changes, so no rank and no client
        // woken over a write that moved nothing.
        Assert.Equal(DavWriteStatus.Replaced, second.Status);
        Assert.Equal(first.Etag, second.Etag);
        SyncStore.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        SyncStore.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TheRank_IsTakenBeforeTheReplacedCardIsArchived()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Ada"), CancellationToken.None);
        SyncStore.Invocations.Clear();

        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Grace"), CancellationToken.None);

        // The state row's lock first, always — the order the two existing gates take, and the only
        // one that cannot deadlock against them.
        var calls = SyncStore.Invocations.Select(i => i.Method.Name).ToList();
        Assert.True(calls.IndexOf(nameof(IContactSyncStore.NextSequenceAsync))
            < calls.IndexOf(nameof(IContactSyncStore.ArchiveAsync)));
    }

    [Fact]
    public async Task WhenTheRankCannotBeTaken_TheStoredCardIsUntouched()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Ada"), CancellationToken.None);
        SyncStore.Invocations.Clear();
        SyncStore.Setup(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no ambient transaction"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Grace"), CancellationToken.None));

        // The rank comes before any contact row is touched: when it cannot be taken, no archive has
        // happened and the stored card still is what it was.
        Assert.Contains("FN:Ada", (await RowOf("a.vcf")).VCardRaw);
        SyncStore.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Deleting_ArchivesAndBuries()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
        SyncStore.Invocations.Clear();

        var outcome = await Writer.DeleteAsync(UserId, "a.vcf", CancellationToken.None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        Assert.Equal(2ul, outcome.Sequence);
        SyncStore.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Delete), It.IsAny<CancellationToken>()),
            Times.Once);
        // Buried under the very rank the deletion took, never another.
        SyncStore.Verify(s => s.PlaceTombstoneAsync(UserId, "a.vcf", 2ul, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Empty(Context.Contacts);
    }

    [Fact]
    public async Task DeletingTheWholeBook_BuriesAndArchivesEveryVisibleCard()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
        await Writer.PutAsync(UserId, "b.vcf", ValidCard("u2", fn: "Grace"), CancellationToken.None);
        SyncStore.Invocations.Clear();

        var outcome = await Writer.DeleteAllAsync(UserId, CancellationToken.None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        Assert.Equal(0ul, outcome.Sequence);
        Assert.Empty(Context.Contacts);
        SyncStore.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Delete), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        SyncStore.Verify(s => s.PlaceTombstoneAsync(
            UserId, "a.vcf", It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Once);
        SyncStore.Verify(s => s.PlaceTombstoneAsync(
            UserId, "b.vcf", It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletingTheWholeBook_SpansTheStoreBatches()
    {
        // One over ContactStore.BatchSize, so the emptying MUST take two batch transactions — two
        // ranks — and still bury every card.
        const int cards = ContactStore.BatchSize + 1;
        for (var i = 0; i < cards; i++)
            await Writer.PutAsync(UserId, $"c{i}.vcf", ValidCard($"u{i}", fn: $"N{i}"), CancellationToken.None);
        SyncStore.Invocations.Clear();

        var outcome = await Writer.DeleteAllAsync(UserId, CancellationToken.None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        Assert.Empty(Context.Contacts);
        SyncStore.Verify(s => s.NextSequenceAsync(UserId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        SyncStore.Verify(s => s.PlaceTombstoneAsync(
            UserId, It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Exactly(cards));
    }

    [Fact]
    public async Task DeletingTheWholeBook_LeavesInvisibleRowsAlone()
    {
        // A row the 4a backfill has not reached was never served: the protocol cannot be asked to
        // delete it, and the webmail contact behind it must survive the book's emptying.
        await GivenAnInvisibleRow("ghost.vcf", uid: "u9");
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
        SyncStore.Invocations.Clear();

        var outcome = await Writer.DeleteAllAsync(UserId, CancellationToken.None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        Assert.Equal("ghost.vcf", Assert.Single(Context.Contacts.Where(c => c.UserId == UserId)).DavName);
        SyncStore.Verify(s => s.PlaceTombstoneAsync(
            UserId, "a.vcf", It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Once);
        SyncStore.Verify(s => s.PlaceTombstoneAsync(
            UserId, "ghost.vcf", It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletingAnEmptyBook_IsDeletedAndWakesNobody()
    {
        var outcome = await Writer.DeleteAllAsync(UserId, CancellationToken.None);

        // 204 on nothing, and NO rank taken: a rank consumed here would wake every client for a
        // change that never happened — the same rule DeleteAsync's refusals follow.
        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        Assert.Equal(0ul, outcome.Sequence);
        SyncStore.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        SyncStore.Verify(s => s.PlaceTombstoneAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletingTheWholeBook_TouchesOnlyItsOwner()
    {
        var other = Guid.NewGuid();
        await Writer.PutAsync(other, "theirs.vcf", ValidCard("u5"), CancellationToken.None);
        await Writer.PutAsync(UserId, "mine.vcf", ValidCard("u1"), CancellationToken.None);

        await Writer.DeleteAllAsync(UserId, CancellationToken.None);

        Assert.Equal("theirs.vcf", Assert.Single(Context.Contacts).DavName);
    }

    [Fact]
    public async Task PuttingOverAnInvisibleRow_CreatesWithoutArchiving()
    {
        // A row the 4a backfill never reached: named, but with no card and no hash — invisible to
        // the protocol, so the client believes it creates. The row is adopted, never duplicated.
        var idBefore = await GivenAnInvisibleRow("a.vcf", uid: "u1");

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.Created, outcome.Status);
        // No card, no revision: archiving NULL bytes has nothing to restitute.
        SyncStore.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var row = await RowOf("a.vcf");
        Assert.Equal(idBefore, row.Id);
        Assert.Contains("FN:Ada", row.VCardRaw);
    }

    [Fact]
    public async Task DeletingAnInvisibleRow_IsNotFound()
    {
        await GivenAnInvisibleRow("a.vcf", uid: "u1");

        var outcome = await Writer.DeleteAsync(UserId, "a.vcf", CancellationToken.None);

        // The reader never served it, so deleting it must be the same 404 an unknown name gets —
        // and the webmail contact behind it must survive a DELETE aimed at nothing.
        Assert.Equal(DavWriteStatus.NotFound, outcome.Status);
        Assert.Single(Context.Contacts.Where(c => c.UserId == UserId));
        SyncStore.Verify(s => s.PlaceTombstoneAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeletingWhatIsNotThere_IsNotFound() =>
        Assert.Equal(DavWriteStatus.NotFound,
            (await Writer.DeleteAsync(UserId, "never.vcf", CancellationToken.None)).Status);

    [Fact]
    public async Task TwoDeletesOnOneName_GiveTheLoserThe404OfAnAbsentName()
    {
        // The race the whole slice had left unpinned: the row vanishes between the read and the
        // write, EF answers DbUpdateConcurrencyException, and untranslated that is the 500 a DAV
        // client retries on the same card every cycle — for a card already gone.
        var database = Guid.NewGuid().ToString();
        Seed(database, "a.vcf", ValidCard("u1"));
        using var racing = new RacingDbContext(database, () => Erase(database, "a.vcf"),
            () => new DbUpdateConcurrencyException("The row was already gone"),
            EntityState.Deleted);
        var writer = NewWriter(racing, ContactStoreTestFactory.NewSyncCounting());

        var outcome = await writer.DeleteAsync(UserId, "a.vcf", CancellationToken.None);

        Assert.Equal(DavWriteStatus.NotFound, outcome.Status);
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task ADeleteLosingALockRace_IsBusyRatherThanAFault(int number)
    {
        // An import holding the state lock until its COMMIT makes this write wait up to
        // innodb_lock_wait_timeout, and EF hands the 1205 over wrapped: read only the outer
        // exception and the answer is a 500 the client retries for ever.
        var database = Guid.NewGuid().ToString();
        Seed(database, "a.vcf", ValidCard("u1"));
        using var racing = new RacingDbContext(database, () => { },
            () => new DbUpdateException("save failed", MySqlErrors.With(number)),
            EntityState.Deleted);
        var writer = NewWriter(racing, ContactStoreTestFactory.NewSyncCounting());

        var outcome = await writer.DeleteAsync(UserId, "a.vcf", CancellationToken.None);

        Assert.Equal(DavWriteStatus.Busy, outcome.Status);
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task APutLosingALockRace_IsBusyRatherThanReplayed(int number)
    {
        // The 1205 arrives inside a DbUpdateException, the same shape the unique-index race takes:
        // replaying it would only wait again, so it must be told apart and answered as busy.
        var database = Guid.NewGuid().ToString();
        using var racing = new RacingDbContext(database, () => { },
            () => new DbUpdateException("save failed", MySqlErrors.With(number)));
        var sync = ContactStoreTestFactory.NewSyncCounting();
        var writer = NewWriter(racing, sync);

        var outcome = await writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.Busy, outcome.Status);
        // And no replay: the gate is entered once, so exactly one rank was ever taken.
        sync.Verify(s => s.NextSequenceAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARejectedBodyThatDecodes_IsArchived()
    {
        var archived = await Writer.ArchiveRejectedAsync(
            UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.True(archived);
        // With the UID the card carries: it is what lets a restore find the revision again.
        SyncStore.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Uid == "u1" && r.Cause == RevisionCause.Rejected),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task ArchivingARejectedBody_LosingALockRace_AnswersFalseRatherThanEscaping(int number)
    {
        // The one write the 412 path performs, and the last place a transient could still have
        // turned a correct refusal into the 500 this tranche forbids.
        GivenTheArchiveThrows(new DbUpdateException("save failed", MySqlErrors.With(number)));

        Assert.False(await Writer.ArchiveRejectedAsync(
            UserId, "a.vcf", ValidCard("u1"), CancellationToken.None));
    }

    [Fact]
    public async Task ArchivingARejectedBody_OnARealFault_StillEscapes()
    {
        // A fault is not a lock race: dressing it as "not archived" would bury a broken store.
        GivenTheArchiveThrows(new DbUpdateException("the revisions table is gone"));

        await Assert.ThrowsAsync<DbUpdateException>(() => Writer.ArchiveRejectedAsync(
            UserId, "a.vcf", ValidCard("u1"), CancellationToken.None));
    }

    private void GivenTheArchiveThrows(Exception exception) =>
        SyncStore.Setup(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

    [Fact]
    public async Task ArchivingARejectedBody_TakesNoRankAndNoLock()
    {
        SyncStore.Invocations.Clear();

        await Writer.ArchiveRejectedAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        // Nothing visible to the protocol has changed, and the 412 path must wake no client.
        SyncStore.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ARejectedBodyThatDoesNotParse_IsStillArchived_WithNoUid()
    {
        var archived = await Writer.ArchiveRejectedAsync(
            UserId, "a.vcf", "garbage but valid utf-8", CancellationToken.None);

        // It is an ARCHIVE, not a card. contact_revisions.uid is nullable for exactly this.
        Assert.True(archived);
        SyncStore.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Uid == null && r.DavName == "a.vcf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARejectedBodyTheWindowDropped_AnswersFalse()
    {
        SyncStore.Setup(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Assert.False(await Writer.ArchiveRejectedAsync(
            UserId, "a.vcf", ValidCard("u1"), CancellationToken.None));
    }

    [Fact]
    public async Task ARejectedBodyOverTheCeiling_IsNotArchived()
    {
        var archived = await Writer.ArchiveRejectedAsync(
            UserId, "a.vcf", HugeCard(), CancellationToken.None);

        // The store ceiling translated on the 412 path too, never surfaced as a database refusal.
        Assert.False(archived);
        SyncStore.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void ABodyInIso8859_DoesNotDecode()
    {
        // A REAL ISO-8859-1 body, as an old 3.0 export still produces: 'è' is the single byte 0xE8,
        // which is no valid UTF-8 sequence. Decoded with the replacement fallback it would become
        // U+FFFD, be stored, and the ETag would lie about what was sent.
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Agnès\r\nEND:VCARD\r\n";

        Assert.False(DavBody.TryDecode(Encoding.Latin1.GetBytes(card), out _));
    }

    [Fact]
    public void TheSameBodyInUtf8_DecodesVerbatim()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Agnès\r\nEND:VCARD\r\n";

        Assert.True(DavBody.TryDecode(Encoding.UTF8.GetBytes(card), out var text));
        Assert.Equal(card, text);
    }

    [Fact]
    public async Task TwoCreatingPuts_TheLoserReplaysAsAReplacement()
    {
        // The race no InMemory index can stage on its own: the loser passes the existence pre-check,
        // the winner's row lands, and the loser's insert dies on (user_id, dav_name). The saboteur
        // context seeds the winner at the exact moment the index would have refused.
        var database = Guid.NewGuid().ToString();
        var winner = ValidCard("u1", fn: "Winner");
        using var racing = new RacingDbContext(database, () => Seed(database, "a.vcf", winner));
        var sync = ContactStoreTestFactory.NewSyncCounting();
        var writer = NewWriter(racing, sync);

        var outcome = await writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Loser"), CancellationToken.None);

        // What the same PUT arrived a second later would have been: a replacement of the winner.
        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
        using var check = new PreferencesTestDbContext(database);
        var row = await check.Contacts.SingleAsync(
            c => c.UserId == UserId && c.DavName == "a.vcf", CancellationToken.None);
        Assert.Contains("FN:Loser", row.VCardRaw);
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Put && r.VCardRaw.Contains("FN:Winner")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARacedUid_IsTranslatedToAConflictWithTheWinnersHref()
    {
        // Same race, other index: the winner lands the shared UID under ANOTHER name, so the replay
        // would only die again — the translation is a refusal carrying the winner's href.
        var database = Guid.NewGuid().ToString();
        using var racing = new RacingDbContext(
            database, () => Seed(database, "b.vcf", ValidCard("u1", fn: "Winner")));
        var sync = ContactStoreTestFactory.NewSyncCounting();
        var writer = NewWriter(racing, sync);

        var outcome = await writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Loser"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.UidConflict, outcome.Status);
        Assert.Equal(DavPaths.Card(UserId, "b.vcf"), outcome.ConflictHref);
    }

    [Fact]
    public async Task AConditionalPut_WithTheTagItRead_Replaces()
    {
        var created = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Grace"),
            CancellationToken.None, ifMatch: created.Etag);

        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
    }

    [Fact]
    public async Task AConditionalPut_WithAStaleTag_IsRefusedByTheGateItself()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Grace"),
            CancellationToken.None, ifMatch: "\"stale\"");

        // The gate enforces its own precondition — the edge's pre-check is only the fast path —
        // and the refusal takes no rank: only the fixture's create ever drew one.
        Assert.Equal(DavWriteStatus.PreconditionFailed, outcome.Status);
        SyncStore.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AConditionalPut_OnAnAbsentName_IsRefused()
    {
        var outcome = await Writer.PutAsync(UserId, "never.vcf", ValidCard("u1"),
            CancellationToken.None, ifMatch: "\"x\"");

        // No current representation, so If-Match fails whatever it lists — including *.
        Assert.Equal(DavWriteStatus.PreconditionFailed, outcome.Status);
    }

    [Fact]
    public async Task AConditionalPut_LosingTheReplacementRace_IsRefusedUnderTheLock()
    {
        // The seam ruling BO left open for replacement: the edge's pre-check and this gate's own
        // first read both precede the state lock, and the winner commits exactly in between. The
        // lock is where the loser must die — here the hook on the rank call, which IS the lock's
        // stand-in, lands the winner at the last instant before the decisive comparison.
        var database = Guid.NewGuid().ToString();
        using var context = new PreferencesTestDbContext(database);
        var loserTag = (await NewWriter(context, ContactStoreTestFactory.NewSyncCounting())
            .PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None)).Etag;
        var winner = ValidCard("u1", fn: "Winner");
        var racing = RacingSync(() => Replace(database, "a.vcf", winner));

        var outcome = await NewWriter(context, racing).PutAsync(UserId, "a.vcf",
            ValidCard("u1", fn: "Loser"), CancellationToken.None, ifMatch: loserTag);

        // Refused, the winner's bytes intact, and nothing archived: the losing editor re-reads
        // instead of silently erasing the very version its If-Match was protecting.
        Assert.Equal(DavWriteStatus.PreconditionFailed, outcome.Status);
        using var check = new PreferencesTestDbContext(database);
        Assert.Contains("FN:Winner", check.Contacts.Single(
            c => c.UserId == UserId && c.DavName == "a.vcf").VCardRaw);
        racing.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AConditionalDelete_WithTheTagItRead_Deletes()
    {
        var created = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        var outcome = await Writer.DeleteAsync(UserId, "a.vcf", CancellationToken.None,
            ifMatch: created.Etag);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
    }

    [Fact]
    public async Task AConditionalDelete_WithAStaleTag_IsRefusedWithoutATombstone()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        var outcome = await Writer.DeleteAsync(UserId, "a.vcf", CancellationToken.None,
            ifMatch: "\"stale\"");

        Assert.Equal(DavWriteStatus.PreconditionFailed, outcome.Status);
        SyncStore.Verify(s => s.PlaceTombstoneAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AConditionalDelete_LosingTheReplacementRace_DeletesNothing()
    {
        // The DELETE shape of the same seam: the very version If-Match was protecting is the one
        // a refusal must leave standing. RacingDbContext is NOT the stage here — it trips on an
        // Added entity — the hook on the rank call is, exactly as for the PUT above.
        var database = Guid.NewGuid().ToString();
        using var context = new PreferencesTestDbContext(database);
        var loserTag = (await NewWriter(context, ContactStoreTestFactory.NewSyncCounting())
            .PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None)).Etag;
        var winner = ValidCard("u1", fn: "Winner");
        var racing = RacingSync(() => Replace(database, "a.vcf", winner));

        var outcome = await NewWriter(context, racing).DeleteAsync(UserId, "a.vcf",
            CancellationToken.None, ifMatch: loserTag);

        Assert.Equal(DavWriteStatus.PreconditionFailed, outcome.Status);
        using var check = new PreferencesTestDbContext(database);
        Assert.Contains("FN:Winner", check.Contacts.Single(
            c => c.UserId == UserId && c.DavName == "a.vcf").VCardRaw);
        racing.Verify(s => s.PlaceTombstoneAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Never);
        racing.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ACreateOnlyPut_LosingTheRace_IsRefusedWithoutWriting()
    {
        // The same race, with the If-None-Match: * intent carried into the gate: the replay finds
        // the winner's row and refuses INSTEAD of replacing — the lost update this closes. Without
        // it the loser's bytes go live while the loser is told nothing happened, and the winner's
        // next sync adopts them.
        var database = Guid.NewGuid().ToString();
        var winner = ValidCard("u1", fn: "Winner");
        using var racing = new RacingDbContext(database, () => Seed(database, "a.vcf", winner));
        var sync = ContactStoreTestFactory.NewSyncCounting();
        var writer = NewWriter(racing, sync);

        var outcome = await writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Loser"),
            CancellationToken.None, createOnly: true);

        Assert.Equal(DavWriteStatus.AlreadyExists, outcome.Status);
        using var check = new PreferencesTestDbContext(database);
        var row = await check.Contacts.SingleAsync(
            c => c.UserId == UserId && c.DavName == "a.vcf", CancellationToken.None);
        Assert.Contains("FN:Winner", row.VCardRaw);
        // And nothing archived: the winner's card was never replaced, so there is nothing to keep.
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ACreateOnlyPut_OverAnExistingName_IsRefusedBeforeAnything()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Winner"), CancellationToken.None);

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Loser"),
            CancellationToken.None, createOnly: true);

        // The winner landed BEFORE the read this time: same refusal, no exception involved.
        Assert.Equal(DavWriteStatus.AlreadyExists, outcome.Status);
        Assert.Contains("FN:Winner", (await RowOf("a.vcf")).VCardRaw);
    }

    [Fact]
    public async Task ACreateOnlyPut_OnAFreeName_Creates()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None,
            createOnly: true);

        Assert.Equal(DavWriteStatus.Created, outcome.Status);
    }

    [Fact]
    public async Task ACreateOnlyPut_OverAnInvisibleRow_StillCreates()
    {
        // The protocol never served the pre-backfill row, so the client's "create only" is honest.
        await GivenAnInvisibleRow("a.vcf", "u1");

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None,
            createOnly: true);

        Assert.Equal(DavWriteStatus.Created, outcome.Status);
    }

    private DavContactWriter NewWriter(PreferencesDbContext context, Mock<IContactSyncStore> sync) =>
        new(context, new ContactStore(context, sync.Object), sync.Object,
            Mock.Of<ILogger<DavContactWriter>>());

    private static string ValidCard(string uid, string fn = "Ada") =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{uid}\r\nN:Lovelace;{fn};;;\r\nFN:{fn}\r\nEND:VCARD\r\n";

    private static string CardWithNote(string note) =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nNOTE:{note}\r\nEND:VCARD\r\n";

    /// <summary>No rank taken means no tombstone lifted and no gap in the sequence a client
    /// synchronises on: a refusal must cost the book nothing at all.</summary>
    private void AssertNoRankWasTaken() =>
        SyncStore.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private static string CardOfVersion(string version) =>
        $"BEGIN:VCARD\r\nVERSION:{version}\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n";

    private static string CardWithoutVersion() =>
        "BEGIN:VCARD\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n";

    private static string CardWithoutUid() =>
        "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Lovelace;Ada;;;\r\nFN:Ada\r\nEND:VCARD\r\n";

    private static string HugeCard() =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nNOTE:{new string('a', ContactStore.MaxCardBytes)}\r\nEND:VCARD\r\n";

    private async Task<Contact> RowOf(string davName) =>
        await Context.Contacts.SingleAsync(
            c => c.UserId == UserId && c.DavName == davName, CancellationToken.None);

    private async Task<Guid> GivenAnInvisibleRow(string davName, string uid)
    {
        var row = new Contact
        {
            Id = Guid.NewGuid(), UserId = UserId, Uid = uid, DavName = davName,
            VCardRaw = null, CardHash = "", UpdatedAt = DateTime.UtcNow
        };
        Context.Contacts.Add(row);
        await Context.SaveChangesAsync(CancellationToken.None);
        return row.Id;
    }

    private async Task GivenTheContactIsFavourite(string davName)
    {
        (await RowOf(davName)).IsFavorite = true;
        await Context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task GivenTheBookIsFull(int count = ContactStore.MaxPerUser)
    {
        for (var i = 0; i < count; i++)
        {
            Context.Contacts.Add(new Contact
            {
                Id = Guid.NewGuid(), UserId = UserId, Uid = Guid.NewGuid().ToString(),
                UpdatedAt = DateTime.UtcNow
            });
        }

        await Context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>A sync double whose FIRST rank call runs <paramref name="competitor"/> — the rank
    /// call is where the production gate takes the state lock, so a commit landed there is a
    /// commit landed at the last instant the decisive If-Match comparison can still catch.</summary>
    private static Mock<IContactSyncStore> RacingSync(Action competitor)
    {
        var sync = ContactStoreTestFactory.NewSyncCounting(first: 5);
        var raced = false;
        var next = 5ul;
        sync.Setup(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (!raced)
                {
                    raced = true;
                    competitor();
                }

                return Task.FromResult(next++);
            });
        return sync;
    }

    /// <summary>The competitor's replacement, committed through its own context — the tracked row
    /// the loser already read keeps its stale values, exactly as under the real lock.</summary>
    private void Replace(string database, string davName, string card)
    {
        using var competitor = new PreferencesTestDbContext(database);
        var row = competitor.Contacts.Single(c => c.UserId == UserId && c.DavName == davName);
        row.VCardRaw = card;
        row.CardHash = ContactStore.CardHashOf(card);
        row.SyncSequence = 4;
        competitor.SaveChanges();
    }

    private void Seed(string database, string davName, string card)
    {
        using var competitor = new PreferencesTestDbContext(database);
        competitor.Contacts.Add(new Contact
        {
            Id = Guid.NewGuid(), UserId = UserId, Uid = "u1", DavName = davName,
            VCardRaw = card, CardHash = ContactStore.CardHashOf(card),
            SyncSequence = 1, UpdatedAt = DateTime.UtcNow
        });
        competitor.SaveChanges();
    }

    /// <summary>The competing DELETE, landing on its own context: what makes the row vanish
    /// between this writer's read and its save.</summary>
    private void Erase(string database, string davName)
    {
        using var competitor = new PreferencesTestDbContext(database);
        competitor.Contacts.RemoveRange(
            competitor.Contacts.Where(c => c.UserId == UserId && c.DavName == davName));
        competitor.SaveChanges();
    }

    /// <summary>
    /// A context whose first save touching a contact in <paramref name="on"/> runs
    /// <paramref name="competitor"/> and then throws <paramref name="thrown"/> — the only way the
    /// InMemory provider, which enforces no index and arbitrates no lock, can stage a race between
    /// two writes. <c>Added</c> stages the race of two creating PUTs on the unique index;
    /// <c>Deleted</c> stages two DELETEs, where the loser's row is already gone.
    /// </summary>
    private sealed class RacingDbContext(
        string databaseName, Action competitor, Func<Exception> thrown,
        EntityState on = EntityState.Added)
        : PreferencesDbContext(OptionsOf(databaseName))
    {
        private bool raced;

        internal RacingDbContext(string databaseName, Action competitor)
            : this(databaseName, competitor,
                () => new DbUpdateException("Duplicate entry, as the unique index would say"))
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!raced && ChangeTracker.Entries<Contact>().Any(e => e.State == on))
            {
                raced = true;
                competitor();
                throw thrown();
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        private static DbContextOptions<PreferencesDbContext> OptionsOf(string name) =>
            new DbContextOptionsBuilder<PreferencesDbContext>()
                .UseInMemoryDatabase(name, PreferencesTestDbContext.Root)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
    }
}
