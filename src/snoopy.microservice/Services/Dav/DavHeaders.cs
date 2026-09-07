namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// The protocol's fixed header and content-type strings — literal, once, so every route reads the
/// same value rather than a copy that could drift.
/// </summary>
internal static class DavHeaders
{
    // No access-control: RFC 3744 § 5 and § 8 would then owe acl, supported-privilege-set,
    // acl-restrictions, inherited-acl-set on every resource and the ACL method — none of which a
    // single-owner book serves. current-user-principal (RFC 5397) needs no class to be read.
    // extended-mkcol is a MUST of RFC 5689 § 3.1 as soon as the extended MKCOL is served, and it is
    // what ccs-caldavtester reads the feature off. Neither calendar-auto-schedule nor
    // calendar-schedule: nothing here schedules.
    internal const string ComplianceClasses = "1, 3, addressbook, calendar-access, extended-mkcol";

    internal const string CollectionAllow = "OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT";

    /// <summary>
    /// Root, principal, the two intermediate collections and the address-book home: every
    /// collection verb but DELETE, which only the address book serves (4d decision 3) — one shared
    /// string here and the home would announce a verb it 405s.
    /// </summary>
    internal const string HomeAllow = "OPTIONS, PROPFIND, PROPPATCH, REPORT";

    /// <summary>The parent one creates in: sabre announces the two creation verbs here too,
    /// because it is where a client reads the capability before creating. On the home itself both
    /// answer 405 — the collection exists, and RFC 4918 § 9.3.1 reserves 405 to that case.</summary>
    internal const string CalendarHomeAllow = "OPTIONS, PROPFIND, PROPPATCH, REPORT, MKCALENDAR, MKCOL";

    /// <summary>
    /// The calendar, and not <see cref="CollectionAllow"/>: it is THIS shape of URL that serves the
    /// two creation verbs — a client creates by naming the calendar to be born, not its parent —
    /// and RFC 9110 § 15.5.6 wants a 405 to announce what the resource does serve.
    /// </summary>
    internal const string CalendarAllow =
        "OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT, MKCALENDAR, MKCOL";

    internal const string CardAllow = "OPTIONS, HEAD, GET, PUT, DELETE, PROPFIND, PROPPATCH, REPORT";
    internal const string EventAllow = CardAllow;
    internal const string VCardContentType = "text/vcard; charset=utf-8";
    internal const string CalendarContentType = "text/calendar; charset=utf-8; component=VEVENT";

    /// <summary>A <c>free-busy-query</c> answer: no <c>component=</c> parameter, since a VFREEBUSY
    /// is not a VEVENT.</summary>
    internal const string FreeBusyContentType = "text/calendar; charset=utf-8";

    internal const string XmlContentType = "application/xml; charset=utf-8";

    /// <summary>What a 201 from a creation verb carries: the parent's listing is now stale, and a
    /// cached one is a calendar the client cannot see it has just made.</summary>
    internal const string NoCache = "no-cache";

    /// <summary>
    /// Sets the <c>DAV:</c> header naming the compliance classes. Applied on <em>every</em> response
    /// that carries it, not only OPTIONS: sabre does this deliberately, and Apple's CardDAV clients
    /// (Contacts.app, addressbookd) read capabilities off whichever response they already have —
    /// typically the PROPFIND that opens a sync — rather than issuing a dedicated OPTIONS first.
    /// </summary>
    internal static void ApplyDav(HttpResponse response) => response.Headers["DAV"] = ComplianceClasses;

    /// <summary>A content type without its parameters — what an announced media type is compared to.</summary>
    internal static string MediaTypeOf(string contentType)
    {
        var end = contentType.IndexOf(';');
        return (end < 0 ? contentType : contentType[..end]).Trim();
    }
}
