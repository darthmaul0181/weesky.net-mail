using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The <c>DAV:sync-collection</c> report (RFC 6578): what moved since the token, deletions as
/// <c>404</c> responses whose <c>status</c> is a direct child, and the new token last. The order of
/// <see cref="WriteAsync"/> IS the decision: the counter is read BEFORE the rows, and the window is
/// bounded above by it. Read the other way round, a write committing in between would be covered by
/// the returned token without appearing in the response — the client believes it seen, never asks
/// again, and the card is missing for ever, with no error and no trace. In this order the same
/// write is simply served next round, where at worst an unchanged ETag makes the client ignore it.
/// </summary>
internal static class SyncCollectionReport
{
    private static readonly XName ValidSyncToken = DavXml.Dav + "valid-sync-token";

    /// <returns>the response count and the token minted, both for the request log</returns>
    /// <remarks>
    /// The snapshot spans the counter and the tombstones, and STOPS there. Those two together is
    /// what <see cref="IContactSyncStore.ReadStateAsync"/>'s contract demands: on MySQL's
    /// REPEATABLE READ the first SELECT pins one InnoDB read view, so a prune committing between
    /// them cannot delete deletions this response still owes under a watermark it read as lower —
    /// the client would then keep those cards for ever, with no error and nothing to signal it,
    /// since the token it files next is above the new watermark and is accepted.
    /// <para>
    /// The CARDS are read outside it, deliberately. A prune touches tombstones and revisions and
    /// never <c>contacts</c>, so no card read needs that view; and a read held open while the body
    /// drains makes whoever is draining the socket the reason an InnoDB read view stays open —
    /// blocking purge and growing the undo log for as long as a client cares to read slowly. A card
    /// written between the two reads simply takes a rank above this answer's bound and is served
    /// next round, which is the same tolerance the ordering of this method already rests on.
    /// </para>
    /// Opened through the execution strategy the way ContactStore.InTransactionAsync is. Nothing is
    /// written to the response inside it, so a retrying strategy, were one ever configured, replays
    /// reads alone and can never write the document twice.
    /// </remarks>
    internal static async Task<SyncReportOutcome> WriteAsync(HttpResponse response, XDocument body,
        string collectionHref, Guid userId, string principalAddress, string? depthHeader,
        IDavContactReader contacts, IContactSyncStore syncStore, PreferencesDbContext preferences,
        CancellationToken cancellationToken)
    {
        var root = body.Root!;

        Func<CancellationToken, Task<SyncWindow>> read = async token =>
        {
            await using var transaction = await preferences.Database.BeginTransactionAsync(token);
            // The counter first — an empty book needs an epoch to form its token, hence the create.
            var state = await syncStore.ReadOrCreateStateAsync(userId, token);
            var presented = DavSyncToken.Read(root.Element(DavXml.Dav + "sync-token"), state);

            // An initial sync serves the whole book and NO tombstone: the answer is authoritative
            // on what the book holds, and anything absent from it is what the client must forget.
            // A refused token reads none either — the refusal below is the whole answer.
            IReadOnlyList<ContactTombstone> tombstones = presented.Kind is SyncTokenKind.Sequence
                ? await contacts.TombstonesAsync(userId, presented.Sequence, state.Seq, token)
                : [];

            await transaction.CommitAsync(token);
            return new SyncWindow(state, presented, tombstones);
        };

        var window = await preferences.Database.CreateExecutionStrategy()
            .ExecuteAsync(read, cancellationToken);

        // After the commit, not inside it: an epoch drawn for a book that had none is worth keeping
        // even when this request is refused, or every refused token redraws one.
        if (window.Token.Kind is SyncTokenKind.Invalid)
        {
            // RFC 6578 § 3.2 names it, and nothing has been written yet: the error document still
            // owns the whole response.
            throw new DavPreconditionException(ValidSyncToken);
        }

        return await WriteWindowAsync(response, body, collectionHref, userId, principalAddress,
            depthHeader, window, contacts, cancellationToken);
    }

    private static async Task<SyncReportOutcome> WriteWindowAsync(HttpResponse response, XDocument body,
        string collectionHref, Guid userId, string principalAddress, string? depthHeader,
        SyncWindow window, IDavContactReader contacts, CancellationToken cancellationToken)
    {
        var root = body.Root!;
        var (state, token, tombstones) = window;

        EnsureSyncLevel(root, depthHeader);
        var limit = LimitOf(root);
        var request = DavPropertyRequest.Parse(body);

        var cards = contacts.ChangedAsync(userId, token.Sequence, state.Seq, cancellationToken);

        await using var writer = await MultiStatusWriter.BeginAsync(response, cancellationToken);

        var written = 0;
        var truncated = false;
        ulong lastComplete = 0;
        List<SyncChange> rank = [];

        // Whole ranks while the count stays under the bound. A batch write puts several rows at one
        // sequence, and a cut inside rank n followed by token n would abandon the rest for ever —
        // so the first rank is always served whole, even alone past the bound: exceeding it is an
        // inconvenience, losing half of a rank is data loss.
        async Task<bool> FlushRankAsync()
        {
            // A cut is legal only where the token it produces survives DavSyncToken.Read: at or
            // above the watermark, mirroring its "seq < pruned_below is refused" (ruling BG).
            // Below it the rank is served even past the bound — the refused alternative sends the
            // client back to an initial sync truncated at the same rank, for ever (RFC 6578 § 3.2).
            if (written > 0 && limit is { } bound && written + rank.Count > bound
                && lastComplete >= state.PrunedBelow)
                return false;

            foreach (var change in rank)
                await WriteChangeAsync(writer, change, request, userId, principalAddress,
                    cancellationToken);
            written += rank.Count;
            lastComplete = rank[0].Rank;
            rank.Clear();

            // On a rank boundary and never inside one: a flush is what starts the body, and a rank
            // must stay replaceable as a whole until it is complete. Safe here only because the
            // snapshot was closed before the first byte was composed.
            await writer.FlushIfDueAsync(cancellationToken);
            return true;
        }

        await foreach (var change in MergedByRankAsync(cards, tombstones, cancellationToken))
        {
            if (rank.Count > 0 && change.Rank != rank[0].Rank && !await FlushRankAsync())
            {
                truncated = true;
                break;
            }

            rank.Add(change);
        }

        if (!truncated && rank.Count > 0 && !await FlushRankAsync()) truncated = true;

        if (truncated) await writer.WriteTruncatedAsync(collectionHref, null, cancellationToken);

        // Truncated, the token names the last COMPLETE rank served, so the next round resumes
        // there; whole, it is the counter read before the rows — never anything read after them.
        var answered = truncated ? state with { Seq = lastComplete } : state;
        var minted = DavSyncToken.Token(answered);
        await writer.WriteSyncTokenAsync(minted, cancellationToken);

        // The very string the document carries, so the log can never claim a token the answer
        // did not: on a truncated response that is the cut, not the counter.
        return new SyncReportOutcome(writer.ResponseCount, minted);
    }

    private static async Task WriteChangeAsync(MultiStatusWriter writer, SyncChange change,
        DavPropertyRequest request, Guid userId, string principalAddress,
        CancellationToken cancellationToken)
    {
        var href = DavPaths.Card(userId, change.DavName);
        if (change.Card is { } card)
        {
            var context = new DavResourceContext(
                DavResourceKind.Card, userId, principalAddress, card, null);
            // address-data is deliberately NOT served here: RFC 6352 § 10.4 defines it only in
            // query and multiget, and the tables leave it in the 404 propstat, where Thunderbird
            // reads the absence and chains a multiget.
            var (found, missing) = DavProperties.Resolve(request, context);
            await writer.WriteResourceAsync(href, found, missing, cancellationToken);
        }
        else
        {
            await writer.WriteStatusAsync(href, StatusCodes.Status404NotFound, cancellationToken);
        }
    }

    /// <summary>Both inputs arrive ordered by rank; one pass merges them, tombstones first within
    /// a shared rank so the order is deterministic.</summary>
    private static async IAsyncEnumerable<SyncChange> MergedByRankAsync(
        IAsyncEnumerable<DavCard> cards, IReadOnlyList<ContactTombstone> tombstones,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var next = 0;
        await foreach (var card in cards.WithCancellation(cancellationToken))
        {
            while (next < tombstones.Count && tombstones[next].SyncSequence <= card.SyncSequence)
            {
                var tombstone = tombstones[next++];
                yield return new SyncChange(tombstone.SyncSequence, tombstone.DavName, null);
            }

            yield return new SyncChange(card.SyncSequence, card.DavName, card);
        }

        while (next < tombstones.Count)
        {
            var tombstone = tombstones[next++];
            yield return new SyncChange(tombstone.SyncSequence, tombstone.DavName, null);
        }
    }

    /// <summary>
    /// RFC 6578 § 3 wants <c>sync-level</c>, at <c>1</c> or <c>infinite</c> — one flat book makes
    /// them the same answer. Absent, ANY <c>Depth</c> header converts (appendix A read wider than
    /// the letter: refusing the conforming header a pre-RFC client set would punish the client
    /// closest to the norm on its very first request); absent both, nothing is left to convert.
    /// </summary>
    private static void EnsureSyncLevel(XElement root, string? depthHeader)
    {
        var level = root.Element(DavXml.Dav + "sync-level");
        if (level is null)
        {
            if (depthHeader is null)
                throw new DavBadRequestException(
                    "The sync-collection carries no sync-level and the request no Depth header.");
        }
        else if (level.Value.Trim() is not ("1" or "infinite"))
        {
            throw new DavBadRequestException(
                "The sync-level admits 1 or infinite; anything else would be a guess.");
        }
    }

    /// <summary>
    /// <c>DAV:limit</c>, never <c>CARDDAV:limit</c>: two namespaces share the local name, RFC 6578
    /// § 3.6 defines this report's in <c>DAV:</c> and RFC 6352 § 10.6 addressbook-query's in the
    /// other — a client's stray <c>CARDDAV:limit</c> here bounds nothing.
    /// </summary>
    private static int? LimitOf(XElement root)
    {
        var nresults = root.Element(DavXml.Dav + "limit")?.Element(DavXml.Dav + "nresults");
        if (nresults is null) return null;

        return int.TryParse(nresults.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture,
            out var bound) && bound > 0
            ? bound
            : throw new DavBadRequestException("The DAV:limit carries no readable DAV:nresults.");
    }

    /// <summary>One row of the window: a card to serve, or a tombstone when <see cref="Card"/> is
    /// null.</summary>
    private sealed record SyncChange(ulong Rank, string DavName, DavCard? Card);

    /// <summary>
    /// Everything the snapshot had to answer, carried out of it so the response can be written with
    /// the read view already closed. The tombstones are materialised — that is the point; the cards
    /// are not here at all, and are streamed afterwards.
    /// </summary>
    private sealed record SyncWindow(
        SyncState State, SyncTokenRead Token, IReadOnlyList<ContactTombstone> Tombstones);
}
