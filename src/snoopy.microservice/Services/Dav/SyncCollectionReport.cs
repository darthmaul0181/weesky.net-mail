using System.Runtime.CompilerServices;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Dav;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// The <c>DAV:sync-collection</c> report (RFC 6578): what moved since the token, deletions as
/// <c>404</c> responses whose <c>status</c> is a direct child, and the new token last. The order of
/// the reads IS the decision: the counter is read BEFORE the rows, and the window is bounded above
/// by it. Read the other way round, a write committing in between would be covered by the returned
/// token without appearing in the response — the client believes it seen, never asks again, and the
/// member is missing for ever, with no error and no trace. In this order the same write is simply
/// served next round, where at worst an unchanged ETag makes the client ignore it.
/// </summary>
internal static class SyncCollectionReport
{
    private static readonly XName ValidSyncToken = DavXml.Dav + "valid-sync-token";

    /// <summary>
    /// Called by the caller INSIDE its transaction, after it read the state with its own key: reads
    /// the presented token against that state, then the tombstones through the source — only on a
    /// Sequence token, none on an initial or refused one.
    /// </summary>
    internal static async Task<SyncWindow> ReadWindowAsync<T>(XElement root, SyncState state,
        IDavMemberSource<T> source, CancellationToken cancellationToken)
    {
        var presented = DavSyncToken.Read(root.Element(DavXml.Dav + "sync-token"), state);

        // An initial sync serves the whole collection and NO tombstone: the answer is authoritative
        // on what it holds, and anything absent from it is what the client must forget. A refused
        // token reads none either — the refusal in WriteAsync is the whole answer.
        IReadOnlyList<DavTombstone> tombstones = presented.Kind is SyncTokenKind.Sequence
            ? await source.TombstonesAsync(presented.Sequence, state.Seq, cancellationToken)
            : [];

        return new SyncWindow(state, presented, tombstones);
    }

    /// <summary>The window was read by the caller in ITS snapshot, with ITS key (user or calendar).</summary>
    /// <returns>the response count and the token minted, both for the request log</returns>
    /// <remarks>
    /// The members are read outside the caller's snapshot, deliberately. A prune touches tombstones
    /// and revisions and never the members, so no member read needs that view; and a read held open
    /// while the body drains makes whoever is draining the socket the reason an InnoDB read view
    /// stays open — blocking purge and growing the undo log for as long as a client cares to read
    /// slowly. A member written between the two reads simply takes a rank above this answer's
    /// bound and is served next round, which is the same tolerance the ordering rests on.
    /// </remarks>
    internal static async Task<SyncReportOutcome> WriteAsync<T>(HttpResponse response, XDocument body,
        string collectionHref, string? depthHeader, SyncWindow window, IDavMemberSource<T> source,
        CancellationToken cancellationToken) where T : class
    {
        // Before Prepare, and not after: RFC 6578 § 3.2 owes an invalid token its own precondition,
        // and a body that also carries a malformed calendar-data would otherwise answer that one
        // instead — the loop 4c put out. After the caller's commit, not inside it: an epoch drawn
        // for a collection that had none is worth keeping even when this request is refused, or
        // every refused token redraws one.
        if (window.Token.Kind is SyncTokenKind.Invalid)
        {
            throw new DavPreconditionException(ValidSyncToken);
        }

        var (request, resolver) = source.Prepare(body); // may refuse — before anything is written

        var root = body.Root!;
        var (state, token, tombstones) = window;

        EnsureSyncLevel(root, depthHeader);
        var limit = DavLimit.Read(root, DavXml.Dav);

        var members = source.ChangedAsync(token.Sequence, state.Seq, cancellationToken);

        await using var writer = await MultiStatusWriter.BeginAsync(response, cancellationToken);

        var written = 0;
        var truncated = false;
        ulong lastComplete = 0;
        List<SyncChange<T>> rank = [];

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
                await WriteChangeAsync(writer, change, request, source, resolver, cancellationToken);
            written += rank.Count;
            lastComplete = rank[0].Rank;
            rank.Clear();

            // On a rank boundary and never inside one: a flush is what starts the body, and a rank
            // must stay replaceable as a whole until it is complete. Safe here only because the
            // snapshot was closed before the first byte was composed.
            await writer.FlushIfDueAsync(cancellationToken);
            return true;
        }

        await foreach (var change in MergedByRankAsync(members, tombstones, source, cancellationToken))
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

    private static async Task WriteChangeAsync<T>(MultiStatusWriter writer, SyncChange<T> change,
        DavPropertyRequest request, IDavMemberSource<T> source, IDavMemberResolver<T> resolver,
        CancellationToken cancellationToken) where T : class
    {
        var href = source.HrefOf(change.DavName);
        if (change.Member is { } member)
        {
            var (found, missing) = resolver.Resolve(request, member);
            await writer.WriteResourceAsync(href, found, missing, cancellationToken);
        }
        else
        {
            await writer.WriteStatusAsync(href, StatusCodes.Status404NotFound, cancellationToken);
        }
    }

    /// <summary>Both inputs arrive ordered by rank; one pass merges them, tombstones first within
    /// a shared rank so the order is deterministic.</summary>
    private static async IAsyncEnumerable<SyncChange<T>> MergedByRankAsync<T>(
        IAsyncEnumerable<T> members, IReadOnlyList<DavTombstone> tombstones, IDavMemberSource<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
    {
        var next = 0;
        await foreach (var member in members.WithCancellation(cancellationToken))
        {
            var memberRank = source.RankOf(member);
            while (next < tombstones.Count && tombstones[next].Rank <= memberRank)
            {
                var tombstone = tombstones[next++];
                yield return new SyncChange<T>(tombstone.Rank, tombstone.DavName, null);
            }

            yield return new SyncChange<T>(memberRank, source.DavNameOf(member), member);
        }

        while (next < tombstones.Count)
        {
            var tombstone = tombstones[next++];
            yield return new SyncChange<T>(tombstone.Rank, tombstone.DavName, null);
        }
    }

    /// <summary>
    /// RFC 6578 § 3 wants <c>sync-level</c>, at <c>1</c> or <c>infinite</c> — one flat collection
    /// makes them the same answer. Absent, ANY <c>Depth</c> header converts (appendix A read wider
    /// than the letter: refusing the conforming header a pre-RFC client set would punish the client
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

    /// <summary>One row of the window: a member to serve, or a tombstone when <see cref="Member"/>
    /// is null.</summary>
    private sealed record SyncChange<T>(ulong Rank, string DavName, T? Member) where T : class;

    /// <summary>
    /// Everything the caller's snapshot had to answer, carried out of it so the response can be
    /// written with the read view already closed. The tombstones are materialised — that is the
    /// point; the members are not here at all, and are streamed afterwards.
    /// </summary>
    internal sealed record SyncWindow(
        SyncState State, SyncTokenRead Token, IReadOnlyList<DavTombstone> Tombstones);
}
