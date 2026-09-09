using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// The <c>CALDAV:calendar-query</c> report (RFC 4791 § 7.8): the resources of the calendar its
/// filter keeps, each with the properties and the <c>calendar-data</c> — as stored or expanded —
/// the body asked for. The columns preselect, the file decides: a candidate is judged on its own
/// object model, so nothing a column cannot hold is ever what excludes a resource.
/// </summary>
internal static class CalendarQueryReport
{
    private static readonly XName FilterElement = DavXml.CalDav + "filter";

    /// <summary>
    /// Writes the report. <paramref name="single"/> is the event a query on a member is scoped to,
    /// null on the collection, where <paramref name="upTo"/> is the counter read in the same
    /// snapshot. Every refusal — the filter's, the collation's, <c>calendar-data</c>'s — is
    /// pronounced before the first byte; past the document's opening a member the resolver refuses
    /// answers its own 403, and past <see cref="MultigetReport.MaxHrefs"/> matches the truncation
    /// shape of 4c closes the document, never a short answer that says nothing.
    /// </summary>
    /// <returns>how many <c>response</c> elements the document carries, for the request log</returns>
    internal static async Task<int> WriteAsync(HttpResponse response, XDocument body, string requestHref,
        DavCalendar calendar, DavEvent? single, IDavCalendarReader reader, ulong upTo, EventMemberSource source,
        ILogger logger, CancellationToken cancellationToken)
    {
        var filter = body.Root!.Element(FilterElement) ?? throw new DavPreconditionException(CalDavError.ValidFilter);
        var spec = CalendarQueryFilter.Parse(filter);
        // RFC 4791 § 9.8's precedence, a MUST: the request's zone where it names one, else the
        // collection's — for the filter's dates and for an expanded calendar-data alike.
        var zone = CalendarRequestTimeZone.Of(body) ?? calendar.TimeZone;
        var (request, resolver) = source.Prepare(body, zone);

        var candidates = single is { } member
            ? Only(member)
            : reader.CandidatesAsync(calendar.Id, spec.Preselection?.FromUtc, spec.Preselection?.ToUtc,
                CalendarQueryFilter.Columns(spec), upTo, cancellationToken);

        await using var writer = await MultiStatusWriter.BeginAsync(response, cancellationToken);
        var truncated = false;
        await foreach (var candidate in candidates.WithCancellation(cancellationToken))
        {
            if (IcsDocument.TryLoad(candidate.IcsRaw) is not { } parsed)
            {
                logger.LogWarning("Event {DavName} of calendar {CalendarId} no longer parses and is left out of the query",
                    candidate.DavName, calendar.Id);
                continue;
            }

            // The bound counts MATCHES, so the evaluation comes first: the other way round, a
            // candidate the filter excludes would forge a 507 over a complete result set.
            if (!CalendarQueryFilter.Matches(parsed, spec, zone)) continue;
            if (writer.ResponseCount == MultigetReport.MaxHrefs)
            {
                truncated = true;
                break;
            }

            // No flush inside the snapshot the caller holds: the socket must not pace a read view.
            await MultigetReport.WriteMemberAsync(writer, source.HrefOf(candidate.DavName), request, candidate,
                resolver, cancellationToken);
        }

        if (truncated) await writer.WriteTruncatedAsync(requestHref, null, cancellationToken);
        return writer.ResponseCount;
    }

    private static async IAsyncEnumerable<DavEvent> Only(DavEvent member)
    {
        await Task.CompletedTask;
        yield return member;
    }
}
