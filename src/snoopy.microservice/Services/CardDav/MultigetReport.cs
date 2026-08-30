using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The <c>CARDDAV:addressbook-multiget</c> report (RFC 6352 § 8.7): a batch read over the hrefs
/// the body lists. One <c>response</c> per href, in the order of the body — clients exist that
/// pair their responses by position, and the database's order would hand them the cards shuffled.
/// An href that designates nothing of this user's collection answers <c>404</c> INSIDE the
/// multistatus: a stale name in a client's list is a common case, not a fault, and a global error
/// would throw away the cards that WERE found.
/// </summary>
internal static class MultigetReport
{
    /// <summary>
    /// A multiget is a list the client composes and nothing bounds on the wire: a request of a few
    /// kilobytes must not be able to ask for fifty thousand reads. The excess answers with the
    /// truncation shape of § 8.6.2 — the motive clients already read — before a single lookup.
    /// </summary>
    internal const int MaxHrefs = 5000;

    internal static async Task WriteAsync(HttpResponse response, XDocument body, string requestHref,
        Guid userId, string principalAddress, IDavContactReader contacts,
        CancellationToken cancellationToken)
    {
        var request = PropertiesAsked(body);
        var addressData = AddressDataAsked(body); // may refuse — before anything is written

        var hrefs = body.Root!.Elements(DavXml.Href).Select(href => href.Value.Trim()).ToList();
        if (hrefs.Count > MaxHrefs)
        {
            await using var truncated = await MultiStatusWriter.BeginAsync(response, cancellationToken);
            await truncated.WriteTruncatedAsync(requestHref, null, cancellationToken);
            return;
        }

        // One query for every name that belongs to THIS user's collection; anything else — a
        // foreign book, a collection href, a name no card may carry — is never looked up at all,
        // and a batch holding nothing of ours never touches the store.
        var names = hrefs.Select(DavPaths.Parse).Where(resource => IsOurs(resource, userId))
            .Select(resource => resource!.DavName!).Distinct(StringComparer.Ordinal).ToList();
        var cards = names.Count == 0
            ? new Dictionary<string, DavCard>(StringComparer.Ordinal)
            : (await contacts.FindManyAsync(userId, names, cancellationToken))
                .ToDictionary(card => card.DavName, StringComparer.Ordinal);

        await using var writer = await MultiStatusWriter.BeginAsync(response, cancellationToken);
        foreach (var href in hrefs)
        {
            var resource = DavPaths.Parse(href);
            if (IsOurs(resource, userId) && cards.TryGetValue(resource!.DavName!, out var card))
            {
                var context = new DavResourceContext(
                    DavResourceKind.Card, userId, principalAddress, card, null);
                var (found, missing) = DavProperties.Resolve(request, context);
                if (addressData is not null) found.Add(Serve(card, addressData));
                await writer.WriteResourceAsync(href, found, missing, cancellationToken);
            }
            else
            {
                await writer.WriteStatusAsync(href, StatusCodes.Status404NotFound, cancellationToken);
            }
        }
    }

    private static bool IsOurs(DavResource? resource, Guid userId) =>
        resource is { Kind: DavResourceKind.Card } && resource.UserId == userId
        && DavName.IsValid(resource.DavName);

    /// <summary>
    /// The card as this request asked to read it: converted FIRST, restricted SECOND. Restriction
    /// is textual, so a card restricted to EMAIL has no FN — converting afterwards would have the
    /// library re-insert what was just removed.
    /// </summary>
    private static XElement Serve(DavCard card, AddressDataRequest asked)
    {
        var text = asked.Version is { } version
            ? VCardVersionConverter.To(card.VCardRaw, version)
            : card.VCardRaw;
        return new XElement(DavXml.CardDav + "address-data",
            AddressDataFilter.Restrict(text, asked.PropertyNames));
    }

    /// <summary>
    /// <c>address-data</c> is served by hand, not by the property tables, so its name is lifted out
    /// of what <see cref="DavProperties.Resolve"/> is asked — left in, every card would carry a 404
    /// propstat naming the very property its 200 propstat serves.
    /// </summary>
    private static DavPropertyRequest PropertiesAsked(XDocument body)
    {
        var request = DavPropertyRequest.Parse(body);
        return request with
        {
            Names = [.. request.Names.Where(name => name != DavXml.CardDav + "address-data")]
        };
    }

    private static AddressDataRequest? AddressDataAsked(XDocument body)
    {
        var container = body.Root!.Element(DavXml.Prop) ?? body.Root!.Element(DavXml.Dav + "include");
        return container?.Element(DavXml.CardDav + "address-data") is { } element
            ? AddressDataFilter.Parse(element)
            : null;
    }
}
