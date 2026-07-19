using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers
{
    /// <summary>
    /// Mail access over IMAP.
    ///
    /// Two conventions hold across every action. Folder paths never appear in a route
    /// segment — the hierarchy separator may be '/', which would break routing — so they
    /// travel in the query string or the request body. And the two failure modes are
    /// distinct: a missing or undecryptable credentials cookie is 401 with the code
    /// "credentials_unavailable" so the client can sign in again, while anything the mail
    /// server refuses is 502.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MailController : ApiBaseController
    {
        private readonly IMailFolderRepository _folders;
        private readonly IMailCredentialStore _credentials;

        public MailController(IMailFolderRepository folders, IMailCredentialStore credentials)
        {
            _folders = folders;
            _credentials = credentials;
        }

        /// <summary>
        /// Returns the caller's folder tree: hierarchy, well-known roles, subscription state
        /// and message counts.
        /// </summary>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="200">The folder tree</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="502">The mail server could not be reached</response>
        [HttpGet("Folders")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<IReadOnlyList<MailFolderNode>>> GetFolders(CancellationToken cancellationToken)
        {
            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _folders.GetTreeAsync(AuthenticatedUser, password.Value, cancellationToken);
            return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
        }
    }
}
