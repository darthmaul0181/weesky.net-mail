using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Controllers;

/// <summary>
/// The running API's version, for the About tab. Authenticated: an exact server version tells an
/// attacker which published flaws apply to THIS deployment, and only a signed-in user has a use
/// for it. It is not a secret — the web bundle carries its own version and commit in the clear,
/// and both halves ship from one push — but an unauthenticated fingerprint is one gift fewer.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class VersionController : ApiBaseController
{
    /// <summary>Returns the product version and the commit it was built from</summary>
    /// <response code="200">Version and short commit (null when the build carried none)</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<ProductVersion> GetVersion() => Ok(ProductVersion.Current);
}
