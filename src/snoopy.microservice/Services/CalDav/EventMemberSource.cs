using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// One calendar of a user, as the two generic reports read it — <c>CardMemberSource</c>'s twin,
/// keyed by calendar. <paramref name="scope"/> is the event a report addressed to a member is
/// scoped to: there, an href naming any other resource is not a member of what was asked.
/// </summary>
internal sealed class EventMemberSource(
    IDavCalendarReader events, DavCalendar calendar, Guid userId, string principalAddress,
    DavPropertyResolver resolve, DavEvent? scope = null) : IDavMemberSource<DavEvent>
{
    public Task<IReadOnlyList<DavEvent>> FindManyAsync(IReadOnlyList<string> davNames, CancellationToken ct) =>
        events.FindManyAsync(calendar.Id, davNames, ct);

    public IAsyncEnumerable<DavEvent> ChangedAsync(ulong after, ulong upTo, CancellationToken ct) =>
        events.ChangedAsync(calendar.Id, after, upTo, ct);

    public async Task<IReadOnlyList<DavTombstone>> TombstonesAsync(ulong after, ulong upTo, CancellationToken ct) =>
        [.. (await events.TombstonesAsync(calendar.Id, after, upTo, ct))
            .Select(tombstone => new DavTombstone(tombstone.DavName, tombstone.SyncSequence))];

    public string? MemberNameOf(string href) =>
        DavPaths.Parse(href) is { Kind: DavResourceKind.Event } resource && resource.UserId == userId
        && string.Equals(resource.CollectionName, calendar.DavName, StringComparison.Ordinal)
        && DavName.IsValid(resource.DavName)
        && (scope is null || string.Equals(resource.DavName, scope.DavName, StringComparison.Ordinal))
            ? resource.DavName
            : null;

    public string HrefOf(string davName) => DavPaths.Event(userId, calendar.DavName, davName);

    public string DavNameOf(DavEvent member) => member.DavName;

    public ulong RankOf(DavEvent member) => member.SyncSequence;

    /// <summary>The generic reports' entry — multiget and sync-collection, whose grammars carry no
    /// <c>CALDAV:timezone</c> (RFC 4791 § 9.6, RFC 6578 § 3.2) — so the collection's zone is the
    /// one § 9.9 leaves them. Explicit: a calendar-aware caller must name the zone it means.</summary>
    (DavPropertyRequest Request, IDavMemberResolver<DavEvent> Resolver) IDavMemberSource<DavEvent>.Prepare(
        XDocument body) => Prepare(body, calendar.TimeZone);

    /// <param name="body">the report body, read once — see <see cref="IDavMemberSource{TMember}.Prepare"/></param>
    /// <param name="timeZone">the zone this report resolves dates in — the request's
    /// <c>CALDAV:timezone</c> where it named one (RFC 4791 § 9.8), the collection's otherwise.
    /// Never read off the collection here: the caller is the layer that knows which § 9.8 wants.</param>
    internal (DavPropertyRequest Request, IDavMemberResolver<DavEvent> Resolver) Prepare(
        XDocument body, string timeZone)
    {
        // Expansion is a multiget's and a query's (RFC 4791 § 9.6): on a sync-collection the
        // event table serves a named calendar-data as stored, like a PROPFIND would.
        if (ReportRequest.KindOf(body) is not (DavReportKind.CalendarMultiget or DavReportKind.CalendarQuery))
            return (DavPropertyRequest.Parse(body), new Resolver(ContextOf, resolve, timeZone, null));

        var request = CalendarDataRequest.PropertiesAsked(body);
        var calendarData = CalendarDataRequest.Asked(body); // may refuse — before anything is written
        return (request, new Resolver(ContextOf, resolve, timeZone, calendarData));
    }

    private DavResourceContext ContextOf(DavEvent member) => new(
        DavResourceKind.Event, userId, principalAddress, null, null,
        CollectionName: calendar.DavName, Calendar: calendar, Event: member);

    private sealed class Resolver(Func<DavEvent, DavResourceContext> contextOf, DavPropertyResolver resolve,
        string timeZone, CalendarDataRequest? calendarData) : IDavMemberResolver<DavEvent>
    {
        public (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, DavEvent member)
        {
            var (found, missing) = resolve(request, contextOf(member));
            if (calendarData is not null) found.Add(calendarData.Element(member, timeZone));
            return (found, missing);
        }
    }
}
