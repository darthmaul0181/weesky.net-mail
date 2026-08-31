using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The <c>CARDDAV:addressbook-query</c> report (RFC 6352 § 8.6): the cards of the book its filter
/// keeps, each with the properties and the <c>address-data</c> the body asked for. Two rules that
/// look alike say the opposite of each other, which is why they are written side by side here: a
/// <c>filter</c> PRESENT but empty matches the whole book, while an ABSENT one is a
/// <c>400</c> — § 10.3's grammar is <c>((allprop | propname | prop)?, filter, limit?)</c>, with no
/// question mark on <c>filter</c>, so its absence is an incomplete request rather than a filter we
/// cannot evaluate, and <c>403 supported-filter</c> would lie about what is missing.
/// </summary>
internal static class AddressBookQueryReport
{
    private static readonly XName FilterElement = DavXml.CardDav + "filter";

    /// <summary>
    /// Writes the report. <paramref name="card"/> is the single address resource a query on a card
    /// is scoped to, and null for the collection — where the SQL pre-filter narrows the read.
    /// </summary>
    /// <returns>how many <c>response</c> elements the document carries, for the request log</returns>
    internal static async Task<int> WriteAsync(HttpResponse response, XDocument body,
        string requestHref, Guid userId, string principalAddress, DavCard? card,
        IDavContactReader contacts, CancellationToken cancellationToken)
    {
        var root = body.Root!;
        var filter = root.Element(FilterElement)
            ?? throw new DavBadRequestException(
                "The addressbook-query carries no CARDDAV:filter, which § 10.3 makes mandatory.");

        // Every refusal is pronounced here, before the first byte: the filter's own
        // (supported-filter, supported-collation), the bound's 400, and address-data's.
        var spec = AddressBookFilter.Parse(filter);
        var limit = DavLimit.Read(root, DavXml.CardDav);
        var request = AddressDataFilter.PropertiesAsked(body);
        var addressData = AddressDataFilter.Asked(body);

        // The whole book, streamed, and every card judged on the card itself. No SQL clause narrows
        // it: the only column that could — display_name — holds the FIRST FN of a card whose 4.0
        // cardinality is 1*, so a card matching on its second FN was dropped before the exact
        // evaluation ever saw it. Under-selection is the one thing a pre-filter may not do, and the
        // 5000-card cap of ContactStore is what makes reading it all acceptable instead.
        var candidates = card is { } single
            ? Only(single)
            : contacts.StreamAsync(userId, ulong.MaxValue, cancellationToken);

        await using var writer = await MultiStatusWriter.BeginAsync(response, cancellationToken);
        var written = 0;
        var truncated = false;

        await foreach (var candidate in candidates.WithCancellation(cancellationToken))
        {
            // The bound counts MATCHES, so the exact evaluation comes first: tested the other way
            // round, a card the filter excludes would forge a 507 over a complete result set, and a
            // client told its answer was truncated re-queries for ever.
            if (!AddressBookFilter.Matches(candidate.VCardRaw, spec)) continue;
            if (limit is { } bound && written == bound)
            {
                truncated = true;
                break;
            }

            var context = new DavResourceContext(
                DavResourceKind.Card, userId, principalAddress, candidate, null);
            var (found, missing) = DavProperties.Resolve(request, context);
            if (addressData is not null)
                found.Add(AddressDataFilter.Element(candidate.VCardRaw, addressData));
            await writer.WriteResourceAsync(DavPaths.Card(userId, candidate.DavName), found, missing,
                cancellationToken);
            written++;

            // The heaviest answer of this surface — the whole book, address-data included — and
            // the only one measured in gigabytes. Without this the streaming writer buffers it all
            // the way to disposal, which is the one thing its whole design refuses. No transaction
            // is open here, so nothing but the socket paces the read.
            await writer.FlushIfDueAsync(cancellationToken);
        }

        // § 8.6.2's shape, the one clients already read: a 507 response on the Request-URI carrying
        // number-of-matches-within-limits — never a bare 403, and never a silent short answer.
        if (truncated) await writer.WriteTruncatedAsync(requestHref, null, cancellationToken);

        return writer.ResponseCount;
    }

    private static async IAsyncEnumerable<DavCard> Only(DavCard card)
    {
        await Task.CompletedTask;
        yield return card;
    }
}
