using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The two opaque strings a client only ever compares to the one it stored last time. This plan
/// EMITS them; reading one back — and refusing it with <c>403 valid-sync-token</c> — belongs to
/// plan c, which is why nothing here parses.
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
    private const string TokenPrefix = "http://weesky.net/ns/sync/";

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
        $"{TokenPrefix}{state?.Epoch ?? Guid.Empty}/{state?.Seq ?? 0}";
}
