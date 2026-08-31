namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The CardDAV URL space, in both directions: the hrefs a response advertises, and the resource an
/// href in a request body designates. Nothing else builds or reads these paths — an encoder and a
/// decoder written apart drift, and here that drift is a directory traversal.
/// </summary>
internal static class DavPaths
{
    internal const string Root = "/" + RootSegment;
    internal const string BookName = "default";

    private const string RootSegment = "dav";
    private const string PrincipalsSegment = "principals";
    private const string BooksSegment = "addressbooks";

    /// <summary>
    /// The round number just above the longest href we can build. 63 characters of prefix
    /// ("/dav/addressbooks/" + a 36-character GUID + "/default/") and an escaped name of at most
    /// 255 characters, at up to <em>nine</em> characters each: three UTF-8 bytes written "%XX",
    /// which is every BMP character above Latin-1 — most of Asia. (Six is the surrogate-pair
    /// figure, four bytes spread over two characters, and it is not the worst case.)
    /// 63 + 255 * 9 = 2358. Anything longer is refused before a single character is decoded.
    /// </summary>
    private const int MaxPathLength = 2560;

    /// <summary>
    /// "/dav/principals/" — the collection that CONTAINS principals, which is what RFC 3744 § 5.8
    /// asks <c>principal-collection-set</c> for; a principal's own URL is a different answer.
    /// </summary>
    internal const string PrincipalCollection = Root + "/" + PrincipalsSegment + "/";

    /// <summary>"/dav/addressbooks/" — the collection that CONTAINS the address-book homes.</summary>
    internal const string BookCollection = Root + "/" + BooksSegment + "/";

    /// <summary>"/dav/principals/{userId}/" — always with its trailing slash.</summary>
    internal static string Principal(Guid userId) => $"{PrincipalCollection}{userId}/";

    /// <summary>"/dav/addressbooks/{userId}/" — the address-book home.</summary>
    internal static string Home(Guid userId) => $"{Root}/{BooksSegment}/{userId}/";

    /// <summary>"/dav/addressbooks/{userId}/default/" — the collection.</summary>
    internal static string Collection(Guid userId) => $"{Home(userId)}{BookName}/";

    /// <summary>
    /// "/dav/addressbooks/{userId}/default/{escaped name}" — never a trailing slash. The name is
    /// escaped as one path segment: a name carrying a space, a '#' or a '?' — all of which a client
    /// may choose — would otherwise produce an href that same client cannot read back.
    /// </summary>
    internal static string Card(Guid userId, string davName) =>
        $"{Collection(userId)}{Uri.EscapeDataString(davName)}";

    /// <summary>
    /// The resource an href from a request body designates, or null when it is not one of ours.
    /// Never throws: an href is client input, and the worst a broken one may do is designate
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Takes the <em>encoded</em> path and decodes the name segment exactly once. It must never be
    /// handed a route value: ASP.NET Core has already decoded those, so a second pass would turn
    /// "%252F" back into "/" and hand the caller a traversal that neither this nor the store would
    /// see. Conversely the decode happens <em>before</em> anything judges the name, because it is
    /// the decode that turns "%2F" into the '/' <see cref="DavName.IsValid"/> refuses. A request
    /// target may be handed over whole: the query and the fragment are cut here, not left as a
    /// duty on every caller.
    /// </remarks>
    internal static DavResource? Parse(string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return null;

        // An absolute-URI is a legal href (RFC 4918 § 8.3): its path designates the resource, as
        // sabre and Radicale read it; the authority is not judged, any more than they judge it.
        if (!absolutePath.StartsWith('/') && absolutePath.IndexOf("://", StringComparison.Ordinal) is > 0 and var scheme)
        {
            var slash = absolutePath.IndexOf('/', scheme + 3);
            if (slash < 0) return null;
            absolutePath = absolutePath[slash..];
        }

        // A '?' or a '#' inside a segment must be written "%3F" or "%23" to be legal, so a raw one
        // is always the delimiter and cutting it can never lose a name. Kestrel's RawTarget — the
        // only property carrying the encoded path — carries the query too, and without this cut
        // "/dav/addressbooks/{u}/default/?x=1" would read as a card named "?x=1": a PROPFIND on the
        // collection silently answered as a card fetch.
        var delimiter = absolutePath.IndexOfAny(['?', '#']);
        var path = delimiter < 0 ? absolutePath : absolutePath[..delimiter];
        if (path.Length is 0 or > MaxPathLength) return null;

        // A leading empty segment is what makes this an absolute path of ours: a scheme-relative
        // "//host/dav/…" fails here rather than resolving against someone else's origin.
        var segments = path.Split('/');
        if (segments.Length < 3 || segments[0].Length != 0 || segments[1] != RootSegment) return null;

        // Every segment but the name is compared literally, so "de%66ault" is not our book and
        // "%31111-…" is not our user: one resource keeps one spelling.
        return segments switch
        {
            [_, _, ""] => new DavResource(DavResourceKind.ServiceRoot, Guid.Empty, null),
            [_, _, PrincipalsSegment, var user, ""] when TryUser(user, out var id) =>
                new DavResource(DavResourceKind.Principal, id, null),
            [_, _, BooksSegment, var user, ""] when TryUser(user, out var id) =>
                new DavResource(DavResourceKind.Home, id, null),
            [_, _, BooksSegment, var user, BookName, ""] when TryUser(user, out var id) =>
                new DavResource(DavResourceKind.Collection, id, null),
            [_, _, BooksSegment, var user, BookName, var name] when TryUser(user, out var id) =>
                new DavResource(DavResourceKind.Card, id, Uri.UnescapeDataString(name)),
            _ => null
        };
    }

    private static bool TryUser(string segment, out Guid userId) =>
        Guid.TryParseExact(segment, "D", out userId);
}
