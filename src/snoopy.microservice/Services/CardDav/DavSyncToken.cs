using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The two opaque strings a client only ever compares to the one it stored last time, and the
/// reading of one back into a refusal or a resumable sequence.
/// </summary>
/// <remarks>
/// Both carry the epoch, and that is not decorative. A bare sequence would leave a hole through the
/// fallback path: after a restore, a sleeping client returning once the sequence has grown back to
/// its remembered value would see an EQUAL ctag on a divergent book, and skip resynchronising.
/// </remarks>
internal static class DavSyncToken
{
    /// <summary>
    /// An http URI under a domain we own, which is what sabre does. It is never dereferenced, only
    /// compared byte for byte. <c>urn:snoopy:</c> was ruled out — <c>snoopy</c> is not a registered
    /// NID, and a sync token is a URI.
    /// </summary>
    internal const string Prefix = "http://weesky.net/ns/sync/";

    /// <summary>
    /// <c>"{epoch}:{seq}"</c>. A book with no state row renders a bare <c>"0"</c>: it has never
    /// emitted anything, so it has nothing to protect, and the first real ctag differs from it.
    /// </summary>
    internal static string Ctag(SyncState? state) =>
        state is null ? "0" : $"{state.Epoch}:{state.Seq}";

    /// <summary>
    /// <c>"http://weesky.net/ns/sync/{epoch}/{seq}"</c>. Unlike the ctag the stateless case still
    /// spells a URI, since plan c parses this one; it spells it with the empty epoch, which no live
    /// book ever holds, so a client handing it back is told to resynchronise rather than believed.
    /// </summary>
    internal static string Token(SyncState? state) =>
        $"{Prefix}{state?.Epoch ?? Guid.Empty}/{state?.Seq ?? 0}";

    /// <summary>
    /// The token as the request log carries it: our prefix stripped, so epoch and rank — the
    /// discriminating tail — survive DavRequestLog's 64-character bound; control characters
    /// blanked, because this is the one field of the line that echoes a client's document, and a
    /// raw newline there is a forged second line to any log parser. Never null for a non-null
    /// value: the refusal path logs exactly what was refused.
    /// </summary>
    internal static string? ForLog(string? value)
    {
        if (value is null) return null;
        var readable = value.StartsWith(Prefix, StringComparison.Ordinal)
            ? value[Prefix.Length..]
            : value;
        return string.Concat(readable.Select(c => char.IsControl(c) ? '?' : c));
    }

    /// <summary>
    /// Reads the token element of a sync-collection body against the book's state. Never throws: an
    /// unreadable token is a refusal to write, not an exception to catch. An empty or absent element
    /// means the whole book; anything else is read only if it names this book's own epoch, at a
    /// sequence still within range and still past what has been pruned.
    /// </summary>
    internal static SyncTokenRead Read(XElement? tokenElement, SyncState state)
    {
        var value = tokenElement?.Value;
        if (string.IsNullOrEmpty(value))
            return new SyncTokenRead(SyncTokenKind.Initial, 0);

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return new SyncTokenRead(SyncTokenKind.Invalid, 0);

        var remainder = value.AsSpan(Prefix.Length);
        var slash = remainder.IndexOf('/');
        if (slash < 0 ||
            !Guid.TryParse(remainder[..slash], out var epoch) ||
            !ulong.TryParse(remainder[(slash + 1)..], out var seq))
            return new SyncTokenRead(SyncTokenKind.Invalid, 0);

        // pruned_below = P is the highest rank PruneAsync deleted: every tombstone above P
        // survives, so a token AT P omits nothing and only n < P is unrecoverable (ruling BG).
        // `<=` becomes right again only if PruneAsync ever makes the watermark mean "everything
        // strictly below is gone". Over ulong n < 0 is impossible, so P == 0 needs no sentinel.
        if (epoch != state.Epoch || seq > state.Seq || seq < state.PrunedBelow)
            return new SyncTokenRead(SyncTokenKind.Invalid, 0);

        return new SyncTokenRead(SyncTokenKind.Sequence, seq);
    }
}
