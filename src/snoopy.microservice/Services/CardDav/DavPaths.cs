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
    /// Longer than any href of ours — a 255-character name escapes to at most six characters per
    /// character — so a body offering more is refused before anything decodes it.
    /// </summary>
    private const int MaxPathLength = 2048;

    /// <summary>"/dav/principals/{userId}/" — always with its trailing slash.</summary>
    internal static string Principal(Guid userId) => $"{Root}/{PrincipalsSegment}/{userId}/";

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
    /// the decode that turns "%2F" into the '/' <see cref="DavName.IsValid"/> refuses.
    /// </remarks>
    internal static DavResource? Parse(string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath) || absolutePath.Length > MaxPathLength) return null;

        // A leading empty segment is what makes this an absolute path of ours: "//host/dav/…" and
        // "https://host/dav/…" both fail here rather than resolving against someone else's origin.
        var segments = absolutePath.Split('/');
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
