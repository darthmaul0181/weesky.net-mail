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
        private readonly IMailMessageRepository _messages;
        private readonly IMailCredentialStore _credentials;

        public MailController(
            IMailFolderRepository folders,
            IMailMessageRepository messages,
            IMailCredentialStore credentials)
        {
            _folders = folders;
            _messages = messages;
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

        /// <summary>
        /// Creates a folder and subscribes it, so it appears without a further step.
        /// </summary>
        /// <param name="request">parent path and leaf name</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="200">Full path of the new folder</response>
        /// <response code="400">The request body or the folder name is missing</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="502">The mail server refused the operation</response>
        [HttpPost("Folders")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<string>> CreateFolder(CreateFolderRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
            if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder name is required"));

            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _folders.CreateFolderAsync(
                AuthenticatedUser, password.Value, request.ParentPath ?? string.Empty, request.Name, cancellationToken);

            return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
        }

        /// <summary>Renames a folder, optionally moving it under a different parent.</summary>
        /// <param name="request">current path, new parent path and new leaf name</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="200">Path of the renamed folder</response>
        /// <response code="400">The request body, the path or the new name is missing</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="502">The mail server refused the operation</response>
        [HttpPut("Folders")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<string>> RenameFolder(RenameFolderRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
            if (string.IsNullOrWhiteSpace(request.Path)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder path is required"));
            if (string.IsNullOrWhiteSpace(request.NewName)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder name is required"));

            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _folders.RenameFolderAsync(
                AuthenticatedUser, password.Value, request.Path, request.NewParentPath ?? string.Empty, request.NewName, cancellationToken);

            return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
        }

        /// <summary>Deletes a folder. The inbox cannot be deleted.</summary>
        /// <param name="request">path of the folder to delete</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="204">Folder deleted</response>
        /// <response code="400">The request body or the path is missing</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="502">The mail server refused the operation</response>
        [HttpDelete("Folders")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult> DeleteFolder(DeleteFolderRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
            if (string.IsNullOrWhiteSpace(request.Path)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder path is required"));

            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _folders.DeleteFolderAsync(AuthenticatedUser, password.Value, request.Path, cancellationToken);

            return FromResult(result,
                errorStatusCode: StatusCodes.Status502BadGateway,
                successStatusCode: StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// Subscribes or unsubscribes a folder. This is how a folder is hidden from the
        /// folder list without deleting it.
        /// </summary>
        /// <param name="request">folder path and desired subscription state</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="204">Visibility changed</response>
        /// <response code="400">The request body or the path is missing</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="502">The mail server refused the operation</response>
        [HttpPut("Folders/Subscription")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult> SetFolderSubscription(FolderSubscriptionRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
            if (string.IsNullOrWhiteSpace(request.Path)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder path is required"));

            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _folders.SetSubscriptionAsync(
                AuthenticatedUser, password.Value, request.Path, request.Subscribed, cancellationToken);

            return FromResult(result,
                errorStatusCode: StatusCodes.Status502BadGateway,
                successStatusCode: StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// One page of a folder, newest message first. The folder path travels in the query
        /// string rather than a route segment because the hierarchy separator may be '/'.
        /// </summary>
        /// <param name="folder">full folder path</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">messages per page, 1 to 200</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="200">The page, with the folder's UidValidity</response>
        /// <response code="400">The folder is missing, or the paging arguments are out of range</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="502">The mail server could not be reached</response>
        [HttpGet("Messages")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<MailFolderPage>> GetMessages(
            [FromQuery] string folder,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(folder)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
            if (page < 0) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page must not be negative"));

            // An unbounded page size lets one request pull an entire mailbox.
            if (pageSize is < 1 or > 200) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page size must be between 1 and 200"));

            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _messages.ListAsync(AuthenticatedUser, password.Value, folder, page, pageSize, cancellationToken);

            return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
        }

        /// <summary>
        /// A single message: sanitised HTML body, plain-text body, headers and attachment list.
        /// Remote images are withheld and counted, so the client can offer to load them.
        /// </summary>
        /// <param name="folder">full folder path</param>
        /// <param name="uid">message UID, valid only for the folder's current UidValidity</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="200">The message</response>
        /// <response code="400">The folder is missing</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="404">No message with that UID in that folder</response>
        /// <response code="502">The mail server could not be reached</response>
        [HttpGet("Messages/Detail")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<MailMessageDetail>> GetMessage(
            [FromQuery] string folder,
            [FromQuery] uint uid,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(folder)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));

            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _messages.GetAsync(AuthenticatedUser, password.Value, folder, uid, cancellationToken);

            if (result.IsFailure && result.Error == ImapSession.MessageNotFound)
            {
                return NotFound(ResultEnveloppe.CreateErrorEnveloppe(result.Error));
            }

            return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
        }

        /// <summary>
        /// Downloads one attachment. Always served as an attachment disposition: message
        /// content must never render inline in the browser.
        /// </summary>
        /// <param name="folder">full folder path</param>
        /// <param name="uid">message UID</param>
        /// <param name="part">MIME part specifier, taken from the message's attachment list</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="200">The attachment bytes</response>
        /// <response code="400">The folder or the part is missing</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="404">No such message, or no such part on it</response>
        /// <response code="502">The mail server could not be reached</response>
        [HttpGet("Messages/Attachment")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult> GetAttachment(
            [FromQuery] string folder,
            [FromQuery] uint uid,
            [FromQuery] string part,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(folder)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
            if (string.IsNullOrWhiteSpace(part)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A part is required"));

            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _messages.GetAttachmentAsync(AuthenticatedUser, password.Value, folder, uid, part, cancellationToken);

            if (result.IsFailure)
            {
                var status = result.Error is ImapSession.MessageNotFound or ImapSession.AttachmentNotFound
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status502BadGateway;

                return StatusCode(status, ResultEnveloppe.CreateErrorEnveloppe(result.Error));
            }

            return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
        }
    }
}
