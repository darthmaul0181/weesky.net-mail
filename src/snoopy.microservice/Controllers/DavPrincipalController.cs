using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Controllers.Dav;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The shapes both protocols share — the service root, the collection of principals and the
/// principal itself: where a client starts discovery, and where the home-sets are announced.
/// </summary>
public sealed class DavPrincipalController(
    PreferencesDbContext preferences,
    IUserAddresses addresses,
    TimeProvider clock,
    ILogger<DavPrincipalController> logger) : DavControllerBase(preferences, logger)
{
    /// <summary>
    /// The three reading verbs, one action per shape. REPORT is bound on the service root, where a
    /// 405 under our own <c>Allow</c> would make an RFC 9110 client retry the verb for ever; the
    /// default branch's <c>403 supported-report</c> is the considered answer there, and
    /// expand-property genuinely serves the root's principal.
    /// </summary>
    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = "")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task ServiceRootAsync(CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.ServiceRoot, null, null, null, DavPaths.Root + "/", cancellationToken);

    /// <summary>
    /// The bare root, OUTSIDE /dav but under the same policy: a client given the bare host tries
    /// "/" as much as the well-known, and a Bearer challenge there is the symptom the named policy
    /// exists to prevent. REPORT stays unbound on purpose: no catch-all of ours answers there, so
    /// its 405 carries routing's own Allow, which honestly omits the verb.
    /// </summary>
    [AcceptVerbs("PROPFIND", "PROPPATCH", Route = "/")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task BareRootAsync(CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.ServiceRoot, null, null, null, "/", cancellationToken);

    /// <summary>
    /// The intermediate collection, carrying no user segment: <c>principal-collection-set</c>
    /// PUBLISHES it (RFC 3744 § 5.8), so a 404 there is the server contradicting its own property.
    /// There is no <c>{userId}</c> to check, and none is wanted: the membership IS the identity of
    /// whoever holds the secret, so Depth 1 lists this account's one child and no other. REPORT is
    /// bound although <c>supported-report-set</c> is EMPTY here — the <c>Allow</c> names the verb,
    /// and the default branch's <c>403 supported-report</c> is a considered answer where a 405
    /// under our own header would make an RFC 9110 client retry the verb for ever.
    /// </summary>
    /// <param name="cancellationToken">the request's own</param>
    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = "principals")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PrincipalCollectionAsync(CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.PrincipalCollection, AuthenticatedUser.WebmailUid, null, null, null,
            cancellationToken);

    [AcceptVerbs("PROPFIND", "PROPPATCH", "REPORT", Route = "principals/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PrincipalAsync(Guid userId, CancellationToken cancellationToken) =>
        DispatchAsync(DavResourceKind.Principal, userId, null, null, null, cancellationToken);

    /// <summary>
    /// Capabilities, answered off the URL shape alone: <c>[AllowAnonymous]</c> on the OPTIONS
    /// actions and on no other action, because a client asks what the server can do before it holds
    /// any credentials — which is also why it consults no store and reveals nothing a URL did not
    /// already carry.
    /// </summary>
    [AcceptVerbs("OPTIONS", Route = "")]
    [AcceptVerbs("OPTIONS", Route = "/")]
    [AcceptVerbs("OPTIONS", Route = "principals")]
    [AcceptVerbs("OPTIONS", Route = "principals/{userId:guid}")]
    [AllowAnonymous]
    public void OptionsHome() => Capabilities(DavHeaders.HomeAllow);

    /// <summary>
    /// Last on purpose, and bound to no verb: carrying no method metadata, this action scores below
    /// every real route above, so action selection reaches it only when nothing else answers the
    /// verb. It carries <c>Allow</c> and nothing else. Routing supplies an Allow of its own, but it
    /// is the union of the verbs bound on the template, which is not what the surface does.
    /// </summary>
    [Route("")]
    [Route("principals")]
    [Route("principals/{userId:guid}")]
    public void MethodNotAllowedOnHome() => MethodNotAllowed(DavHeaders.HomeAllow);

    /// <summary>
    /// The context a nested expand-property target resolves against, or null for anything that is
    /// not this user's — the nested 404. The resolution is synchronous, so no context built here
    /// can carry a card or a sync state; the two kinds that would need one are refused rather than
    /// answered without it. No href property of ours designates either today, so the refusal is
    /// unreachable — and the day one does, a nested 404 sends the client back to a PROPFIND,
    /// where a ctag of "0" and a token on the empty epoch would have been believed instead.
    /// </summary>
    internal static DavResourceContext? NestedContext(DavResource resource, Guid userId, string email)
    {
        if (resource.Kind is DavResourceKind.ServiceRoot)
            return new DavResourceContext(DavResourceKind.ServiceRoot, userId, email, null, null);
        if (resource.Kind is DavResourceKind.Card or DavResourceKind.AddressBook
                or DavResourceKind.Calendar or DavResourceKind.Event
            || resource.UserId != userId)
        {
            return null;
        }

        return new DavResourceContext(resource.Kind, userId, email, null, null);
    }

    /// <summary>No shape here has a member to look up, so nothing is ever a 404.</summary>
    private protected override async Task<DavResourceContext?> ContextOrNotFoundAsync(
        DavResourceKind kind, string? collectionName, string? davName, DavPropertyRequest? request,
        CancellationToken cancellationToken)
    {
        var user = AuthenticatedUser;
        return new DavResourceContext(kind, user.WebmailUid, user.Email, null, null,
            Addresses: await AddressesOrNullAsync(kind, request, user, cancellationToken),
            CardDavEnabled: Enabled(DavProtocol.CardDav),
            CalDavEnabled: Enabled(DavProtocol.CalDav));
    }

    /// <summary>The principal's own tables, and the two homes an expand-property nests into from
    /// <c>addressbook-home-set</c> and <c>calendar-home-set</c>.</summary>
    private protected override (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, DavResourceContext resource) => resource.Kind switch
    {
        DavResourceKind.AddressBookHome => CardDavProperties.Resolve(request, resource),
        DavResourceKind.CalendarHome => CalDavProperties.Resolve(request, resource, clock),
        _ => DavPrincipalProperties.Resolve(request, resource),
    };

    /// <summary>
    /// Every address the principal answers to, in order: the account's own, then the same name on
    /// its other domains, then the curated sending identities — lower-cased and without duplicates.
    /// </summary>
    /// <remarks>
    /// Gathered ONLY when the body reads it, and once for the whole request: the domains come from
    /// <see cref="IAccountInfoProvider"/>, whose contract lets an implementation leave the process,
    /// and a synchronising device asks its principal on every cycle and on every device. The
    /// Depth: 1 child of the principal collection is a <c>with</c> of this context, so it inherits
    /// the one list rather than asking again. A platform that cannot answer is a WARNING and the
    /// primary address alone — never a 500, which would stop a device from ever discovering the
    /// two home-sets over one directory hiccup.
    /// </remarks>
    private async Task<IReadOnlyList<string>?> AddressesOrNullAsync(DavResourceKind kind,
        DavPropertyRequest? request, User user, CancellationToken cancellationToken)
    {
        var asked = request is not null
            && (request.Mode is DavPropertyMode.AllProp
                || request.Names.Contains(DavPrincipalProperties.CalendarUserAddressSet));
        if (!asked || kind is not (DavResourceKind.Principal or DavResourceKind.PrincipalCollection))
            return null;
        return await addresses.ForPrincipalAsync(user, cancellationToken);
    }

    /// <summary>
    /// The one member a Depth: 1 lists on the collection of principals, and it is always THIS
    /// account's: no <c>{userId}</c> reaches the shape, so serving another guid's child would be
    /// listing a resource the caller is not.
    /// </summary>
    private protected override async IAsyncEnumerable<(string Href, DavResourceContext Resource)> ChildrenAsync(
        DavResourceContext parent, DavPropertyRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        if (parent.Kind is DavResourceKind.PrincipalCollection)
            yield return (DavPaths.Principal(parent.UserId), parent with { Kind = DavResourceKind.Principal });
    }

    /// <summary>expand-property alone, on the two shapes whose <c>supported-report-set</c> names it
    /// (RFC 3253 § 3.8); the collection of principals announces none.</summary>
    private protected override async Task<bool> ServeReportAsync(DavReportKind report, XDocument body,
        DavResourceContext resource, string requestHref, Trace trace, CancellationToken cancellationToken)
    {
        if (report is not DavReportKind.ExpandProperty
            || resource.Kind is not (DavResourceKind.ServiceRoot or DavResourceKind.Principal))
        {
            return false;
        }

        trace.Responses = await ExpandPropertyReport.WriteAsync(Response, body, resource, requestHref,
            nested => NestedContext(nested, resource.UserId, resource.PrincipalAddress), Resolve,
            cancellationToken);
        return true;
    }

    private protected override bool NeedsSnapshot(DavResourceKind kind, DavDepthValue depth) => false;

    /// <summary>Either service is enough: the principal is where a client discovers both home-sets,
    /// and a 403 here would stop the one that IS switched on from ever being found.</summary>
    private protected override bool SwitchedOn() =>
        Enabled(DavProtocol.CardDav) || Enabled(DavProtocol.CalDav);

    private protected override bool IsCollection(DavResourceKind kind) =>
        kind is DavResourceKind.PrincipalCollection;

    private protected override string HrefOf(DavResourceKind kind, Guid userId, string? collectionName,
        string? davName, string? rootHref) => kind switch
    {
        DavResourceKind.ServiceRoot => rootHref!,
        DavResourceKind.PrincipalCollection => DavPaths.PrincipalCollection,
        DavResourceKind.Principal => DavPaths.Principal(userId),
        _ => throw new InvalidOperationException($"{kind} is not a shape of the principal tree."),
    };

    private protected override string? CanonicalOf(DavResourceKind kind, Guid userId, string? collectionName) =>
        kind switch
        {
            DavResourceKind.PrincipalCollection => DavPaths.PrincipalCollection,
            DavResourceKind.Principal => DavPaths.Principal(userId),
            _ => null,
        };
}
