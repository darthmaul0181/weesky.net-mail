using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// DELETE, and the one thing it must never do: bury a card it has just refused to remove. A
/// tombstone is what tells every OTHER device the card is gone, and sync-collection serves it
/// faithfully — so a tombstone laid for a delete that did not happen erases the card everywhere
/// instead of nowhere.
/// </summary>
public sealed class CardDavDeleteTests : IAsyncLifetime
{
    private readonly Mock<ILogger<CardDavController>> logger = new();

    private DavTestServer server = null!;

    /// <summary>A spy, not a stub: Verify counts the controller's calls while every call runs the
    /// real writer over this server's own database, so the tombstones — and the ranks — are
    /// genuine rows a test reads back.</summary>
    private Mock<IDavContactWriter> Writer { get; } = new();

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync(overrides: services =>
        {
            services.AddScoped<IDavContactWriter>(_ => Writer.Object);
            services.AddSingleton(logger.Object);
        });
        DelegateToTheRealWriter();
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task ADelete_Answers204()
    {
        await GivenACardAndItsEtag("a.vcf");

        Assert.Equal(204, (await Delete(DavPaths.Card(UserId, "a.vcf"))).StatusCode);
    }

    [Fact]
    public async Task ADeleteWithNoIfMatch_IsNotAFailedPrecondition()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await Delete(DavPaths.Card(UserId, "a.vcf"));

        // RFC 7232 § 3.1: an ABSENT If-Match is not evaluated at all. Read as an empty header that
        // matches nothing, every unconditional DELETE — which is most of them — would answer 412.
        Assert.Equal(204, response.StatusCode);
        Writer.Verify(w => w.DeleteAsync(UserId, "a.vcf", It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ADeleteWithAMatchingIfMatch_Succeeds()
    {
        var etag = await GivenACardAndItsEtag("a.vcf");

        // Clients send it precisely so as not to erase a card modified elsewhere in between.
        Assert.Equal(204, (await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: etag)).StatusCode);
    }

    [Fact]
    public async Task ADeleteWithAStaleIfMatch_Answers412AndBuriesNothing()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: "\"stale\"");

        // Burying then refusing would make a card disappear from the book that the server has just
        // said it was keeping. The Times.Never is on the CALL, not on its effect: it is the call
        // that must not happen.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ARefusedDelete_LaysNoTombstone()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: "\"stale\"");

        // The status alone cannot see this: a tombstone laid beside a 412 is served by the next
        // sync-collection to every OTHER device, which erases a card the server still holds. The
        // 412 is asserted all the same, or an unbound route would satisfy the absence for free.
        Assert.Equal(412, response.StatusCode);
        using var db = server.CreateContext();
        Assert.Empty(db.ContactTombstones.Where(t => t.UserId == UserId));
        Assert.Single(db.Contacts.Where(c => c.UserId == UserId && c.DavName == "a.vcf"));
    }

    [Fact]
    public async Task ARefusedDelete_TakesNoRank()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: "\"stale\"");

        // A rank consumed for nothing makes every client re-sync for a change that never happened;
        // the creating PUT's rank is still the last one. The 412 is asserted all the same, or an
        // unbound route would satisfy the absence for free.
        Assert.Equal(412, response.StatusCode);
        using var db = server.CreateContext();
        Assert.Equal(1ul, db.ContactSyncStates.Single(s => s.UserId == UserId).Seq);
    }

    [Fact]
    public async Task ARefusedDelete_ArchivesNothing()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: "\"stale\"");

        // It brings no bytes: it leaves decision 18's log line, and that is all. The 412 is
        // asserted here so the claim cannot be satisfied by a route that answers 405.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AWeakIfMatch_IsRefused()
    {
        var etag = await GivenACardAndItsEtag("a.vcf");

        var response = await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: $"W/{etag}");

        // If-Match guards a destruction and compares STRONGLY: a weak tag says "semantically
        // equivalent", which is no promise the bytes about to be erased are the ones seen.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task AnIfMatchListingSeveralTags_MatchesOnAny()
    {
        var etag = await GivenACardAndItsEtag("a.vcf");

        Assert.Equal(204, (await Delete(DavPaths.Card(UserId, "a.vcf"),
            ifMatch: $"\"other\", {etag}")).StatusCode);
    }

    [Fact]
    public async Task AnIfNoneMatchStarOnACardThatExists_Answers412()
    {
        await GivenACardAndItsEtag("a.vcf");

        // RFC 9110 § 13.1.2 applies If-None-Match to every method, not to GET alone: the star says
        // "only if it does not exist", and the card does.
        Assert.Equal(412, (await Delete(DavPaths.Card(UserId, "a.vcf"),
            ifNoneMatch: "*")).StatusCode);
    }

    [Fact]
    public async Task ADeleteOfWhatIsNotThere_Answers404() =>
        Assert.Equal(404, (await Delete(DavPaths.Card(UserId, "never.vcf"))).StatusCode);

    [Fact]
    public async Task ADeleteOfARowTheProtocolCannotSee_Answers404()
    {
        GivenAPreBackfillRow("invisible.vcf");

        var response = await Delete(DavPaths.Card(UserId, "invisible.vcf"));

        // A row the 4a backfill has not reached was never served; deleting what the protocol cannot
        // see is the same 404 an unknown name gets, and the writer is never asked.
        Assert.Equal(404, response.StatusCode);
        Writer.Verify(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task AnotherUsersCard_Answers404AndTouchesNothing()
    {
        var response = await Delete(DavPaths.Card(Guid.NewGuid(), "a.vcf"));

        Assert.Equal(404, response.StatusCode);
        Writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TheThreeAbsences_AreOneAnswer()
    {
        await GivenACardAndItsEtag("a.vcf");
        GivenAPreBackfillRow("invisible.vcf");

        var unknown = await Delete(DavPaths.Card(UserId, "never.vcf"));
        var invisible = await Delete(DavPaths.Card(UserId, "invisible.vcf"));
        var foreign = await Delete(DavPaths.Card(Guid.NewGuid(), "a.vcf"));

        // Told apart, they say whether a name exists and whose it is. The body and the headers as
        // much as the status: a 404 carrying an Allow the others lack is a distinction too.
        Assert.Equal(Signature(unknown), Signature(invisible));
        Assert.Equal(Signature(unknown), Signature(foreign));
        Assert.Equal((404, "", null, null, null), Signature(unknown));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..%5C..%5Cetc")]
    [InlineData("a.vcf%20")]
    public async Task AnInvalidName_Answers404AndReachesNoWriter(string escaped)
    {
        var response = await Delete($"{DavPaths.Collection(UserId)}{escaped}");

        // A literal slash, a backslash traversal and a PAD-SPACE collision — not "%2F", which the
        // catch-all keeps ENCODED and hands over as the harmless literal. A name the book will not
        // hold designates nothing, so it is the 404 of any other absence, never a 403: there is no
        // card here to refuse to delete.
        Assert.Equal(404, response.StatusCode);
        Writer.Verify(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ADeleteOnTheCollection_Is405()
    {
        var response = await Delete(DavPaths.Collection(UserId));

        // It would erase the whole book — a gesture the product offers nowhere and that no route
        // must offer by accident. The reference servers serve it, but their book is not tied to the
        // account the way ours is. The Allow, not the status alone: routing pronounces a 405 here
        // on its own, and asserting the status would stay green with the answer deleted.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CollectionAllow, response.Header("Allow"));
        Writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AfterADelete_TheCardIsGoneAndTheNameIsBuried()
    {
        await GivenACardAndItsEtag("a.vcf");

        await Delete(DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(404, (await server.SendAsync("GET", DavPaths.Card(UserId, "a.vcf"))).StatusCode);
        Writer.Verify(w => w.DeleteAsync(UserId, "a.vcf", It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ASuccessfulDelete_LaysExactlyOneTombstoneAtTheRankItTook()
    {
        await GivenACardAndItsEtag("a.vcf");

        Assert.Equal(204, (await Delete(DavPaths.Card(UserId, "a.vcf"))).StatusCode);

        // The rank and the tombstone are one statement: a tombstone at any other rank is invisible
        // to a client whose token already covers it, or replayed for ever by one whose does not.
        using var db = server.CreateContext();
        Assert.Equal(2ul, db.ContactSyncStates.Single(s => s.UserId == UserId).Seq);
        var tombstone = Assert.Single(db.ContactTombstones.Where(t => t.UserId == UserId));
        Assert.Equal("a.vcf", tombstone.DavName);
        Assert.Equal(2ul, tombstone.SyncSequence);
    }

    [Fact]
    public async Task ADeleteThatArrivedSecond_Answers404()
    {
        await GivenACardAndItsEtag("a.vcf");
        GivenTheWriterAnswers(DavWriteStatus.NotFound);

        // The row vanished between the read and the write: to this sender that is the same 404 an
        // absent name answers, never the 500 an untranslated outcome would be.
        Assert.Equal(404, (await Delete(DavPaths.Card(UserId, "a.vcf"))).StatusCode);
    }

    [Fact]
    public async Task ABusyBook_Answers503WithRetryAfter()
    {
        await GivenACardAndItsEtag("a.vcf");
        GivenTheWriterAnswers(DavWriteStatus.Busy);

        var response = await Delete(DavPaths.Card(UserId, "a.vcf"));

        // A lock race is a moment, not a state: 503 + Retry-After is what a client retries later,
        // where a 500 is what it retries for ever.
        Assert.Equal(503, response.StatusCode);
        Assert.Equal("1", response.Header("Retry-After"));
    }

    [Fact]
    public async Task ARefusedDelete_LeavesItsLine()
    {
        await GivenACardAndItsEtag("a.vcf");

        await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: "\"stale\"");

        // The refusal paths are the ones the line exists for: "the card is still there on the other
        // device" is invisible in an access log, which sees a 412 it cannot explain.
        logger.VerifyInformationLoggedWithAll("DELETE", "status=412");
    }

    [Fact]
    public async Task ADeleteOfWhatIsNotThere_LeavesItsLine()
    {
        await Delete(DavPaths.Card(UserId, "never.vcf"));

        logger.VerifyInformationLoggedWithAll("DELETE", "status=404");
    }

    [Fact]
    public async Task ASuccessfulDelete_LeavesItsLine()
    {
        await GivenACardAndItsEtag("a.vcf");

        await Delete(DavPaths.Card(UserId, "a.vcf"));

        logger.VerifyInformationLoggedWithAll("DELETE", "status=204");
    }

    /// <summary>Status, body and the headers a 404 could be told apart by.</summary>
    private static (int, string, string?, string?, string?) Signature(DavTestResponse response) =>
        (response.StatusCode, response.Body, response.Header("Allow"), response.Header("ETag"),
            response.Header("Content-Type"));

    private async Task<string> GivenACardAndItsEtag(string name)
    {
        var response = await Put(DavPaths.Card(UserId, name), ValidCard(Guid.NewGuid().ToString()));
        Assert.Equal(201, response.StatusCode);
        return response.Header("ETag")!;
    }

    /// <summary>A row the 4a backfill has not reached: stored, yet invisible to the protocol.</summary>
    private void GivenAPreBackfillRow(string name)
    {
        using var db = server.CreateContext();
        db.Contacts.Add(new Contact
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Uid = Guid.NewGuid().ToString(),
            DavName = name,
            VCardRaw = null,
            CardHash = string.Empty,
            UpdatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task ADeleteLosingTheReplacementRace_Answers412()
    {
        // The gate re-compared If-Match under the state lock and the tag no longer held: the very
        // version the header was protecting is the one the refusal leaves standing. The pre-check
        // here PASSES (the tag is current), so the 412 can only come from the gate — and the
        // header must have reached it, or it had nothing to compare.
        var etag = await GivenACardAndItsEtag("a.vcf");
        GivenTheWriterAnswers(DavWriteStatus.PreconditionFailed);

        var response = await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: etag);

        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), etag), Times.Once);
    }

    private void GivenTheWriterAnswers(DavWriteStatus status) =>
        Writer.Setup(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new DavWriteOutcome(status, null, null, 0));

    private void DelegateToTheRealWriter()
    {
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns((Guid user, string name, string card, CancellationToken token, bool createOnly,
                    string? ifMatch) =>
                WithRealWriter(real => real.PutAsync(user, name, card, token, createOnly, ifMatch)));
        Writer.Setup(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Returns((Guid user, string name, CancellationToken token, string? ifMatch) =>
                WithRealWriter(real => real.DeleteAsync(user, name, token, ifMatch)));
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

    private async Task<DavTestResponse> Put(string path, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.TryAddWithoutValidation("Content-Type", "text/vcard");
        request.Content = content;

        using var response = await server.Client.SendAsync(request);
        return await DavTestResponse.ReadAsync(response);
    }

    private Task<DavTestResponse> Delete(string path, string? ifMatch = null, string? ifNoneMatch = null)
    {
        var headers = new Dictionary<string, string>();
        if (ifMatch is not null) headers["If-Match"] = ifMatch;
        if (ifNoneMatch is not null) headers["If-None-Match"] = ifNoneMatch;
        return server.SendAsync("DELETE", path, headers: headers);
    }

    private static string ValidCard(string uid) =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{uid}\r\nFN:Grace\r\nEND:VCARD\r\n";
}
