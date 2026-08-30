using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CardDavPutTests : IAsyncLifetime
{
    private DavTestServer server = null!;

    /// <summary>A spy, not a stub: Verify counts the controller's calls while every call runs the
    /// real writer over this server's own database, so the outcomes — and the archives — are
    /// genuine.</summary>
    private Mock<IDavContactWriter> Writer { get; } = new();

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync(overrides: services =>
            services.AddScoped<IDavContactWriter>(_ => Writer.Object));
        DelegateToTheRealWriter();
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task ACreatingPut_Answers201WithItsEtag()
    {
        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        Assert.Equal(201, response.StatusCode);
        Assert.NotNull(response.Header("ETag"));
    }

    [Fact]
    public async Task AReplacingPut_Answers204()
    {
        await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"));

        // The ETag rides the 204 as much as the 201: without it DAVx5 re-GETs every card after
        // every edit it makes.
        Assert.Equal(204, response.StatusCode);
        Assert.NotNull(response.Header("ETag"));
    }

    [Fact]
    public async Task WhatAPutStores_IsWhatAGetServes()
    {
        // A card the projection will partly lose (bare LF nowhere, but an accent and a second FN
        // would be) — the bytes, not the projection, are what a GET must serve back, and the ETag
        // is the SHA-256 of exactly those bytes.
        var card = ValidCard("u1", fn: "Adèle du train");
        var put = await Put(DavPaths.Card(UserId, "a.vcf"), card);

        var get = await server.SendAsync("GET", DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(Encoding.UTF8.GetBytes(card), get.BodyBytes);
        Assert.Equal(put.Header("ETag"), get.Header("ETag"));
    }

    [Theory]
    [InlineData("\"{etag}\"")]
    [InlineData("*")]
    [InlineData("\"other\", \"{etag}\"")]
    public async Task AMatchingIfMatch_IsAccepted(string template)
    {
        var etag = await GivenACardAndItsEtag("a.vcf");

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"),
            ifMatch: template.Replace("{etag}", etag.Trim('"')));

        // A client sending two ETags is common, and refusing it wrongly would erase its edit on a
        // 412 it does not deserve.
        Assert.Equal(204, response.StatusCode);
    }

    [Fact]
    public async Task AWeakIfMatch_IsRefused()
    {
        var etag = await GivenACardAndItsEtag("a.vcf");

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"),
            ifMatch: $"W/{etag}");

        // If-Match guards a WRITE and compares strongly: a weak tag says "semantically equivalent",
        // which is not a promise a byte-for-byte replacement can rest on.
        Assert.Equal(412, response.StatusCode);
    }

    [Fact]
    public async Task AStaleIfMatch_Answers412()
    {
        await GivenACardAndItsEtag("a.vcf");

        Assert.Equal(412, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"),
            ifMatch: "\"stale\"")).StatusCode);
    }

    [Fact]
    public async Task AnIfMatchOnAnAbsentResource_Answers412AndNot404()
    {
        var response = await Put(DavPaths.Card(UserId, "never.vcf"), ValidCard("u1"), ifMatch: "\"x\"");

        // The condition is false, and 412 is what the client reads as "re-read before rewriting".
        Assert.Equal(412, response.StatusCode);
    }

    [Fact]
    public async Task AnIfNoneMatchStar_OnAnExistingResource_Answers412()
    {
        await GivenACardAndItsEtag("a.vcf");
        var refused = ValidCard("u1", fn: "G");

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), refused, ifNoneMatch: "*");

        // By the PRECONDITION, not by the race demotion, which answers 412 only after writing:
        // the star's whole point is that the client holds no copy whose loss it could tolerate,
        // so the write must never run — only the fixture's creating PUT ever reached the writer.
        // And this 412 flavour archives like any other: the bytes are here, refused, unstored.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Once);
        Writer.Verify(w => w.ArchiveRejectedAsync(UserId, "a.vcf", refused, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnIfNoneMatchStar_OnANewName_Creates() =>
        Assert.Equal(201, (await Put(DavPaths.Card(UserId, "new.vcf"), ValidCard("u1"),
            ifNoneMatch: "*")).StatusCode);

    [Fact]
    public async Task AnIfNoneMatchListingTheCurrentEtag_Answers412()
    {
        var etag = await GivenACardAndItsEtag("a.vcf");

        Assert.Equal(412, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"),
            ifNoneMatch: $"\"other\", {etag}")).StatusCode);
    }

    [Fact]
    public async Task ARefusedPut_ArchivesItsBodyBeforeThe412Leaves()
    {
        await GivenACardAndItsEtag("a.vcf");
        var refused = ValidCard("u1", fn: "Written on a train");

        await Put(DavPaths.Card(UserId, "a.vcf"), refused, ifMatch: "\"stale\"");

        // DAVx5 applies "the server wins" without consulting anyone — its manual says so in those
        // terms. The refusal is right; the erasure that follows is not. This is the one place in the
        // slice where we do strictly better than both reference servers: Radicale's git hook sees
        // only ACCEPTED writes, and sabre sees nothing at all.
        Writer.Verify(w => w.ArchiveRejectedAsync(UserId, "a.vcf", refused, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ARefusedPut_IsArchivedBeforeTheStatusIsWritten()
    {
        await GivenACardAndItsEtag("a.vcf");
        var refused = ValidCard("u1", fn: "G");

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), refused, ifMatch: "\"stale\"");

        // Order matters: the condition is evaluated FIRST (RFC 7232 puts preconditions before body
        // processing) — so the refused body never reaches PutAsync — and the archive happens before
        // the 412 leaves.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Writer.Verify(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), refused,
            It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ARefusedPut_TakesNoRankAndLeavesARejectedRevision()
    {
        await GivenACardAndItsEtag("a.vcf");
        var refused = ValidCard("u1", fn: "Written on a train");

        await Put(DavPaths.Card(UserId, "a.vcf"), refused, ifMatch: "\"stale\"");

        // The archive is real — the spy delegates to the real writer over this same database — and
        // the 412 path wakes no client: the rank the creating PUT took is still the last one.
        using var db = server.CreateContext();
        Assert.Equal(1ul, db.ContactSyncStates.Single(s => s.UserId == UserId).Seq);
        var revision = db.ContactRevisions.Single(r => r.Cause == RevisionCause.Rejected);
        Assert.Equal(refused, revision.VCardRaw);
    }

    [Fact]
    public async Task ARefusedPutWhoseBodyIsNotUtf8_IsNotArchived()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await PutBytes(DavPaths.Card(UserId, "a.vcf"), Latin1Bytes(), ifMatch: "\"stale\"");

        // Storage is text: archiving bytes a MEDIUMTEXT would betray violates the promise of
        // restitution. It answers 412 with no revision, and decision 18's log line keeps the trace.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ABodyThatIsNotStrictUtf8_Answers403ValidAddressData()
    {
        var response = await PutBytes(DavPaths.Card(UserId, "a.vcf"), Latin1Bytes());

        // An ISO-8859-1 body would decode to U+FFFD and the ETag would LIE: what is stored would no
        // longer be what was sent, and the client would believe it holds its card.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "valid-address-data", ConditionOf(response));
    }

    [Fact]
    public async Task ABodyThatIsNoCard_Answers403ValidAddressData()
    {
        var response = await Put(DavPaths.Card(UserId, "a.vcf"), "this is no card at all");

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "valid-address-data", ConditionOf(response));
    }

    [Fact]
    public async Task AVersionWeDoNotAnnounce_Answers403SupportedAddressData()
    {
        // Readable, yet refusable — old Android still exports 2.1 — under its OWN condition: a
        // client re-exports on this one where it abandons on valid-address-data.
        var response = await Put(DavPaths.Card(UserId, "a.vcf"),
            "BEGIN:VCARD\r\nVERSION:2.1\r\nUID:u1\r\nFN:Old\r\nEND:VCARD\r\n");

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "supported-address-data", ConditionOf(response));
    }

    [Fact]
    public async Task ACardOverTheStorageCeiling_Answers403MaxResourceSize()
    {
        // Exactly the request limit, so [RequestSizeLimit] lets it pass — the two ceilings compose
        // without overlap: the transport 413 refuses what cannot be READ, this 403 what cannot be
        // STORED once the UID the card declares none of is stamped in.
        var response = await Put(DavPaths.Card(UserId, "a.vcf"), CardWithNoUidOfExactly(1024 * 1024));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "max-resource-size", ConditionOf(response));
    }

    [Fact]
    public async Task ABodyPastTheRequestLimit_Answers413EvenWithAFailingPrecondition()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await PutBytes(DavPaths.Card(UserId, "a.vcf"),
            new byte[1024 * 1024 + 1], ifMatch: "\"stale\"");

        // The read is bounded before anything is judged: bytes the server refuses to hold cannot
        // be archived either, so the 413 wins over the 412 and nothing is kept.
        Assert.Equal(413, response.StatusCode);
        Writer.Verify(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("text/vcard")]
    [InlineData("text/x-vcard")]
    [InlineData("text/directory")]
    [InlineData(null)]
    public async Task TheContentTypeIsNotAJudge(string? contentType) =>
        Assert.Equal(201, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"),
            contentType: contentType)).StatusCode);

    [Fact]
    public async Task AUidConflict_CarriesTheHrefOfTheConflict()
    {
        await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("shared"));

        var response = await Put(DavPaths.Card(UserId, "b.vcf"), ValidCard("shared"));

        // A SHOULD of § 6.3.2.1 that its DTD makes mandatory as soon as the element is emitted:
        // without it the client knows it failed but not what to re-read.
        Assert.Equal(403, response.StatusCode);
        var condition = ErrorRootOf(response).Element(DavXml.CardDav + "no-uid-conflict")!;
        Assert.Equal(DavPaths.Card(UserId, "a.vcf"), condition.Element(DavXml.Dav + "href")!.Value);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..%5C..%5Cetc")]
    [InlineData("a.vcf%20")]
    public async Task AnInvalidNameIsRefusedByAConsideredAnswer_NotByRouting(string escaped)
    {
        // A route pattern demanding .vcf would refuse a name by a routing 404 — the one code a
        // client reads as "this collection does not contain that" rather than "that name will not
        // do". A literal slash, a backslash traversal and a PAD-SPACE collision; not the brief's
        // bare "..", which System.Uri folds away, nor "%2F", which the catch-all keeps ENCODED —
        // it reaches DavName as the harmless literal "%2F", not as the '/' Ruling AM assumed.
        var response = await Put($"{DavPaths.Collection(UserId)}{escaped}", ValidCard("u1"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "valid-address-data", ConditionOf(response));
        Writer.Verify(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ACardDeclaringNoUid_IsCreatedWithNoEtag()
    {
        var response = await Put(DavPaths.Card(UserId, "a.vcf"),
            "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:NoUid\r\nEND:VCARD\r\n");

        // The stored bytes carry a stamped UID the sent ones do not: an ETag here would make the
        // client believe it holds what the server stored. No ETag is the honest answer; it re-reads.
        Assert.Equal(201, response.StatusCode);
        Assert.Null(response.Header("ETag"));
    }

    [Fact]
    public async Task AnotherUsersBook_Answers404AndTouchesNothing()
    {
        var response = await Put(DavPaths.Card(Guid.NewGuid(), "a.vcf"), ValidCard("u1"));

        Assert.Equal(404, response.StatusCode);
        Writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task APutOnTheCollection_Answers405()
    {
        var response = await Put(DavPaths.Collection(UserId), ValidCard("u1"));

        // The collection URL presents no name segment: creating "the collection" is MKCOL
        // territory, and the answer stays the 405 the catch-all gave, Allow included.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CollectionAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task AFullBook_Answers507()
    {
        GivenTheWriterAnswers(DavWriteStatus.BookFull);

        // RFC 4918 § 11.5 — no CardDAV precondition names the cap, so the status carries it alone.
        Assert.Equal(507, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"))).StatusCode);
    }

    [Fact]
    public async Task ABusyBook_Answers503WithRetryAfter()
    {
        GivenTheWriterAnswers(DavWriteStatus.Busy);

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        // A lock race is a moment, not a state: 503 + Retry-After is what a client retries later,
        // where a 500 is what it retries forever.
        Assert.Equal(503, response.StatusCode);
        Assert.Equal("1", response.Header("Retry-After"));
    }

    [Fact]
    public async Task TwoCreatingPutsOnOneName_AreReplayedAsAReplacement()
    {
        GivenTheUniqueIndexWillTripOnce();

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        // Left alone this is a 500, exactly what the errors table promises never to answer. Replayed
        // as a replacement of the resource the other write just created — which is what the same PUT
        // arriving a second later would have been. 204, not the 201 the controller's own pre-read
        // would suggest: the outcome, never the pre-read, names what happened.
        Assert.Equal(204, response.StatusCode);
    }

    [Fact]
    public async Task TwoCreatingPutsWithIfNoneMatchStar_GiveThe412ToTheLoser()
    {
        // What the real writer answers when create-only loses the race — the gate's own refusal,
        // nothing written (DavContactWriterTests pin that over the racing context).
        GivenTheWriterAnswers(DavWriteStatus.AlreadyExists);
        var refused = ValidCard("u1");

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), refused, ifNoneMatch: "*");

        // Its condition is now false — and since the gate wrote nothing, the loser's body earns
        // the archive of any other 412, and the star's intent must have reached the writer.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), true, It.IsAny<string?>()), Times.Once);
        Writer.Verify(w => w.ArchiveRejectedAsync(UserId, "a.vcf", refused, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AReplacementRace_GivesTheLoserA412AndArchivesItsBody()
    {
        // What the gate answers when the If-Match it re-compared under the state lock no longer
        // held — the commit landed between the controller's pre-check and the lock. The pre-check
        // here PASSES (the tag is current), so the 412 can only come from the gate's refusal.
        var etag = await GivenACardAndItsEtag("a.vcf");
        GivenTheWriterAnswers(DavWriteStatus.PreconditionFailed);
        var refused = ValidCard("u1", fn: "Loser");

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), refused, ifMatch: etag);

        // Nothing was written, so the body earns the archive of any other 412 — and the header
        // must have reached the writer, or the gate had nothing to compare.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<bool>(), etag), Times.Once);
        Writer.Verify(w => w.ArchiveRejectedAsync(UserId, "a.vcf", refused, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AReplacedOutcomeUnderCreateOnly_IsStillDemotedTo412()
    {
        // The net beneath the gate's refusal: a writer that replaced anyway under If-None-Match: *
        // must not be reported as a successful create.
        GivenTheWriterAnswers(DavWriteStatus.Replaced, etag: "\"winner\"", sequence: 2);

        Assert.Equal(412, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"),
            ifNoneMatch: "*")).StatusCode);
    }

    private async Task<string> GivenACardAndItsEtag(string name)
    {
        var response = await Put(DavPaths.Card(UserId, name), ValidCard("u1"));
        Assert.Equal(201, response.StatusCode);
        return response.Header("ETag")!;
    }

    /// <summary>What the real writer answers once the unique index tripped its first attempt and
    /// the replay went through as a replacement of the winner's row — DavContactWriterTests prove
    /// that translation over a racing context; here the outcome drives the controller's mapping.
    /// </summary>
    private void GivenTheUniqueIndexWillTripOnce() =>
        GivenTheWriterAnswers(DavWriteStatus.Replaced, etag: "\"winner\"", sequence: 2);

    private void GivenTheWriterAnswers(DavWriteStatus status, string? etag = null, ulong sequence = 0) =>
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .ReturnsAsync(new DavWriteOutcome(status, etag, null, sequence));

    private void DelegateToTheRealWriter()
    {
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns((Guid user, string name, string card, CancellationToken token, bool createOnly,
                    string? ifMatch) =>
                WithRealWriter(real => real.PutAsync(user, name, card, token, createOnly, ifMatch)));
        Writer.Setup(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, string name, string card, CancellationToken token) =>
                WithRealWriter(real => real.ArchiveRejectedAsync(user, name, card, token)));
    }

    private async Task<T> WithRealWriter<T>(Func<IDavContactWriter, Task<T>> call)
    {
        await using var context = server.CreateContext();
        var sync = new InMemorySyncStore(context);
        return await call(new DavContactWriter(context, new ContactStore(context, sync), sync,
            NullLogger<DavContactWriter>.Instance));
    }

    private Task<DavTestResponse> Put(string path, string body, string? ifMatch = null,
        string? ifNoneMatch = null, string? contentType = "text/vcard") =>
        PutBytes(path, Encoding.UTF8.GetBytes(body), ifMatch, ifNoneMatch, contentType);

    private async Task<DavTestResponse> PutBytes(string path, byte[] body, string? ifMatch = null,
        string? ifNoneMatch = null, string? contentType = "text/vcard")
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        var content = new ByteArrayContent(body);
        if (contentType is not null)
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        request.Content = content;
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

        using var response = await server.Client.SendAsync(request);
        return await DavTestResponse.ReadAsync(response);
    }

    private static string ValidCard(string uid, string fn = "Grace") =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{uid}\r\nFN:{fn}\r\nEND:VCARD\r\n";

    /// <summary>Real ISO-8859-1 bytes — the 'è' is the single byte 0xE8, invalid as UTF-8.</summary>
    private static byte[] Latin1Bytes() => Encoding.Latin1.GetBytes(
        "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Adèle\r\nEND:VCARD\r\n");

    /// <summary>All ASCII, so characters are bytes; no UID, so the stamp pushes it past the card
    /// ceiling while the body itself still fits the request limit.</summary>
    private static string CardWithNoUidOfExactly(int bytes)
    {
        const string head = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Big\r\nNOTE:";
        const string tail = "\r\nEND:VCARD\r\n";
        return head + new string('x', bytes - head.Length - tail.Length) + tail;
    }

    private static XElement ErrorRootOf(DavTestResponse response)
    {
        var root = XDocument.Parse(response.Body).Root!;
        Assert.Equal(DavXml.Error, root.Name);
        return root;
    }

    private static XName ConditionOf(DavTestResponse response) =>
        Assert.Single(ErrorRootOf(response).Elements()).Name;
}
