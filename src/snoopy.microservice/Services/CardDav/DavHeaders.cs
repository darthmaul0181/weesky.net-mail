namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The protocol's fixed header and content-type strings — literal, once, so every route reads the
/// same value rather than a copy that could drift.
/// </summary>
internal static class DavHeaders
{
    // No access-control: RFC 3744 § 5 and § 8 would then owe acl, supported-privilege-set,
    // acl-restrictions, inherited-acl-set on every resource and the ACL method — none of which a
    // single-owner book serves. current-user-principal (RFC 5397) needs no class to be read.
    internal const string ComplianceClasses = "1, 3, addressbook";
    internal const string CollectionAllow = "OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT";
    internal const string CardAllow = "OPTIONS, HEAD, GET, PUT, DELETE, PROPFIND, PROPPATCH, REPORT";
    internal const string VCardContentType = "text/vcard; charset=utf-8";
    internal const string XmlContentType = "application/xml; charset=utf-8";

    /// <summary>
    /// Root, principal and home: every collection verb but DELETE, which only the address book
    /// serves (4d decision 3) — one shared string here and the home would announce a verb it 405s.
    /// </summary>
    internal const string HomeAllow = "OPTIONS, PROPFIND, PROPPATCH, REPORT";

    /// <summary>
    /// Sets the <c>DAV:</c> header naming the compliance classes. Applied on <em>every</em> response
    /// that carries it, not only OPTIONS: sabre does this deliberately, and Apple's CardDAV clients
    /// (Contacts.app, addressbookd) read capabilities off whichever response they already have —
    /// typically the PROPFIND that opens a sync — rather than issuing a dedicated OPTIONS first.
    /// </summary>
    internal static void ApplyDav(HttpResponse response) => response.Headers["DAV"] = ComplianceClasses;
}
