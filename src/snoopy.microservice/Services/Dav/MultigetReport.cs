using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// The multiget report — <c>CARDDAV:addressbook-multiget</c> (RFC 6352 § 8.7) and its calendar
/// twin: a batch read over the hrefs the body lists. One <c>response</c> per href, in the order of
/// the body — clients exist that pair their responses by position, and the database's order would
/// hand them the members shuffled. An href that designates nothing of this collection answers
/// <c>404</c> INSIDE the multistatus: a stale name in a client's list is a common case, not a
/// fault, and a global error would throw away the members that WERE found.
/// </summary>
internal static class MultigetReport
{
    /// <summary>
    /// A multiget is a list the client composes and nothing bounds on the wire: a request of a few
    /// kilobytes must not be able to ask for fifty thousand reads. The excess answers with the
    /// truncation shape of § 8.6.2 — the motive clients already read — before a single lookup.
    /// </summary>
    internal const int MaxHrefs = 5000;

    /// <summary>First instruction: <c>source.Prepare(body)</c> — a refusal must leave the response
    /// untouched. Only then <see cref="MultiStatusWriter.BeginAsync"/>.</summary>
    /// <returns>how many <c>response</c> elements the document carries, for the request log</returns>
    internal static async Task<int> WriteAsync<T>(HttpResponse response, XDocument body, string requestHref,
        IDavMemberSource<T> source, CancellationToken cancellationToken)
    {
        var (request, resolver) = source.Prepare(body); // may refuse — before anything is written

        var hrefs = body.Root!.Elements(DavXml.Href).Select(href => href.Value.Trim()).ToList();
        if (hrefs.Count > MaxHrefs)
        {
            await using var truncated = await MultiStatusWriter.BeginAsync(response, cancellationToken);
            await truncated.WriteTruncatedAsync(requestHref, null, cancellationToken);
            return truncated.ResponseCount;
        }

        // One query for every name that belongs to THIS collection; anything else — a foreign one,
        // a collection href, a name no member may carry — is never looked up at all, and a batch
        // holding nothing of ours never touches the store.
        var names = hrefs.Select(source.MemberNameOf).OfType<string>()
            .Distinct(StringComparer.Ordinal).ToList();
        var members = names.Count == 0
            ? new Dictionary<string, T>(StringComparer.Ordinal)
            : (await source.FindManyAsync(names, cancellationToken))
                .ToDictionary(source.DavNameOf, StringComparer.Ordinal);

        await using var writer = await MultiStatusWriter.BeginAsync(response, cancellationToken);
        foreach (var href in hrefs)
        {
            if (source.MemberNameOf(href) is { } name && members.TryGetValue(name, out var member))
            {
                await WriteMemberAsync(writer, href, request, member, resolver, cancellationToken);
            }
            else
            {
                await writer.WriteStatusAsync(href, StatusCodes.Status404NotFound, cancellationToken);
            }

            // Up to MaxHrefs members, address-data included: the second heaviest answer here, and no
            // transaction is open, so the socket alone paces it.
            await writer.FlushIfDueAsync(cancellationToken);
        }

        return writer.ResponseCount;
    }

    /// <summary>A member the resolver refuses as asked — an expansion past its cap — answers its
    /// own 403, condition named: the document is open by now, so a global refusal could only
    /// truncate it, and the members that CAN be served are still owed. The one member loop of
    /// every report that resolves after opening its document — the query's included.</summary>
    internal static async Task WriteMemberAsync<T>(MultiStatusWriter writer, string href,
        DavPropertyRequest request, T member, IDavMemberResolver<T> resolver,
        CancellationToken cancellationToken)
    {
        List<XElement> found;
        List<XName> missing;
        try
        {
            (found, missing) = resolver.Resolve(request, member);
        }
        catch (DavPreconditionException refused)
        {
            await writer.WriteRefusedAsync(href, refused.Condition, cancellationToken);
            return;
        }

        await writer.WriteResourceAsync(href, found, missing, cancellationToken);
    }
}
