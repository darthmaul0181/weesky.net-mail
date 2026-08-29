using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The CardDAV surface. Deliberately NOT <c>[ApiController]</c>: its binding conventions and its
/// automatic 400 on an invalid ModelState would pre-empt this protocol's own responses. The policy
/// is named on purpose — a bare <c>[Authorize]</c> would challenge with <c>WWW-Authenticate:
/// Bearer</c>, which a CardDAV client has no token for and no way to ask for one. Hidden from the
/// API explorer: Swashbuckle has no OpenAPI operation type for a PROPFIND and throws at scan time.
/// </summary>
[Route("dav")]
[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class CardDavController(
    IDavContactReader contacts,
    IContactSyncStore syncStore,
    ILogger<CardDavController> logger) : ApiBaseController
{
    private const int MaxBodyBytes = 1024 * 1024;

    [AcceptVerbs("PROPFIND", Route = "")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindServiceRootAsync(CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.ServiceRoot, null, null, DavPaths.Root + "/", cancellationToken);

    /// <summary>
    /// The bare root, OUTSIDE /dav but under the same policy: a client given the bare host tries
    /// "/" as much as the well-known, and a Bearer challenge there is the symptom the named policy
    /// exists to prevent.
    /// </summary>
    [AcceptVerbs("PROPFIND", Route = "/")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindBareRootAsync(CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.ServiceRoot, null, null, "/", cancellationToken);

    [AcceptVerbs("PROPFIND", Route = "principals/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindPrincipalAsync(Guid userId, CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.Principal, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", Route = "addressbooks/{userId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindHomeAsync(Guid userId, CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.Home, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName)]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindCollectionAsync(Guid userId, CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.Collection, userId, null, null, cancellationToken);

    [AcceptVerbs("PROPFIND", Route = "addressbooks/{userId:guid}/" + DavPaths.BookName + "/{*davName}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task PropfindCardAsync(Guid userId, string? davName, CancellationToken cancellationToken) =>
        PropfindAsync(DavResourceKind.Card, userId, davName, null, cancellationToken);

    private async Task PropfindAsync(DavResourceKind kind, Guid? userId, string? davName,
        string? rootHref, CancellationToken cancellationToken)
    {
        var user = AuthenticatedUser;

        // Ownership first: a foreign {userId} answers 404, never 403 — a 403 would confirm the
        // existence of the principal aimed at.
        if (userId is { } target && target != user.WebmailUid)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var depth = DavDepth.Parse(DepthHeader());
        if (depth is null)
        {
            logger.LogInformation("PROPFIND refused: unreadable Depth header {Depth}", DepthHeader());
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (depth is DavDepthValue.Infinity)
        {
            await DavError.WriteAsync(Response, StatusCodes.Status403Forbidden,
                DavXml.Dav + "propfind-finite-depth", cancellationToken: cancellationToken,
                logger: logger);
            return;
        }

        var request = await ReadRequestAsync(cancellationToken);
        if (request is null) return; // the 400 is already on the response

        DavCard? card = null;
        if (kind is DavResourceKind.Card)
        {
            // An invalid name — routing has already decoded any %2F into the '/' IsValid refuses —
            // designates nothing, which is the same 404 an unknown name gets.
            card = DavName.IsValid(davName)
                ? await contacts.FindAsync(user.WebmailUid, davName!, cancellationToken)
                : null;
            if (card is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        var state = NeedsState(kind, depth.Value)
            ? await syncStore.ReadStateAsync(user.WebmailUid, cancellationToken)
            : null;

        var resource = new DavResourceContext(kind, user.WebmailUid, user.Email, card, state);
        var href = kind switch
        {
            DavResourceKind.ServiceRoot => rootHref!,
            DavResourceKind.Principal => DavPaths.Principal(user.WebmailUid),
            DavResourceKind.Home => DavPaths.Home(user.WebmailUid),
            DavResourceKind.Collection => DavPaths.Collection(user.WebmailUid),
            _ => DavPaths.Card(user.WebmailUid, card!.DavName),
        };

        await using var writer = await MultiStatusWriter.BeginAsync(Response, cancellationToken);
        await WriteResourceAsync(writer, href, request, resource, cancellationToken);

        if (depth is DavDepthValue.One && kind is DavResourceKind.Home)
        {
            await WriteResourceAsync(writer, DavPaths.Collection(user.WebmailUid), request,
                resource with { Kind = DavResourceKind.Collection }, cancellationToken);
        }

        if (depth is DavDepthValue.One && kind is DavResourceKind.Collection)
        {
            await foreach (var member in contacts.StreamAsync(user.WebmailUid, cancellationToken))
            {
                await WriteResourceAsync(writer, DavPaths.Card(user.WebmailUid, member.DavName),
                    request, resource with { Kind = DavResourceKind.Card, Card = member },
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Buffers the body asynchronously before handing it to the (synchronous) reader: Kestrel
    /// forbids synchronous reads on the request body, so feeding it to
    /// <see cref="DavXmlReader.Parse"/> directly would be a 500 on the first request.
    /// </summary>
    private async Task<DavPropertyRequest?> ReadRequestAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        try
        {
            await Request.Body.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return DavPropertyRequest.Parse(DavXmlReader.Parse(buffer, logger));
        }
        catch (BadHttpRequestException)
        {
            // [RequestSizeLimit] tripping: too large, not malformed — Kestrel's 413, not our 400.
            throw;
        }
        catch (DavBadRequestException ex)
        {
            logger.LogInformation("PROPFIND body refused: {Reason}", ex.Message);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return null;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            logger.LogWarning(ex, "The CardDAV request body stream failed while being read");
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return null;
        }
    }

    private string? DepthHeader() =>
        Request.Headers.TryGetValue("Depth", out var values) ? values.ToString() : null;

    private static async Task WriteResourceAsync(MultiStatusWriter writer, string href,
        DavPropertyRequest request, DavResourceContext resource, CancellationToken cancellationToken)
    {
        var (found, missing) = DavProperties.Resolve(request, resource);
        await writer.WriteResourceAsync(href, found, missing, cancellationToken);
    }

    /// <summary>Only the collection's properties read the sync state; fetch it only when one of
    /// this answer's resources is the collection.</summary>
    private static bool NeedsState(DavResourceKind kind, DavDepthValue depth) =>
        kind is DavResourceKind.Collection
        || (kind is DavResourceKind.Home && depth is DavDepthValue.One);
}
