using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Services.CardDav;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// RFC 6764 § 6 discovery. Anonymous as a whole, and bound to no verb at all: DAVx⁵ and
/// Thunderbird open discovery with a PROPFIND here, not a GET, and a redirect reserved for GET
/// hands them a 405 on the very first gesture. Hidden from the API explorer for the reason
/// <see cref="CardDavController"/> is — Swashbuckle has no operation type for a verbless action.
/// </summary>
[AllowAnonymous]
[Route(".well-known/carddav")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class WellKnownController : ControllerBase
{
    /// <summary>A day, and not for ever: a bare 301 is cached permanently, and changing the /dav
    /// path would then be impossible on the devices already paired.</summary>
    private const string BoundedCaching = "max-age=86400";

    public void CardDav()
    {
        Response.Headers.Location = DavPaths.Root + "/";
        Response.Headers.CacheControl = BoundedCaching;
        Response.StatusCode = StatusCodes.Status301MovedPermanently;
    }
}
