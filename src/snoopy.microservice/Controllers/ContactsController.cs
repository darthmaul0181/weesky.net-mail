using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The user's contacts — webmail data, not mail-server data. No IMAP session and no credentials
/// cookie: every action is a database read or write, the same shape as IdentitiesController.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class ContactsController(IContactStore store) : ApiBaseController
{
    /// <summary>
    /// The whole book in one answer. Search and sort are the client's job, over this cached list.
    /// </summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The contacts</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ContactListResponse>> List(CancellationToken cancellationToken)
    {
        var contacts = await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        return Ok(new ContactListResponse(contacts));
    }

    /// <summary>Creates a contact and answers it, id included.</summary>
    /// <param name="request">the contact to create</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Created</response>
    /// <response code="400">Neither name nor address, an unparsable address, or the cap reached</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ContactView>> Create(
        ContactRequest request, CancellationToken cancellationToken)
    {
        var validated = ContactValidator.Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var created = await store.CreateAsync(
            AuthenticatedUser.WebmailUid, validated.Value, cancellationToken);
        if (created.IsFailure) return BadRequestEnveloppe(created.Error);

        // Answered from the validated write rather than re-read: the store folded the addresses,
        // so echoing the request's spelling would hand back a form the next save would change.
        var write = validated.Value;
        return Ok(new ContactView(created.Value, write.FirstName, write.LastName, write.Nickname,
            write.IsFavorite, [.. write.Addresses.Select(IdentityResolver.Canonical).Distinct()]));
    }

    /// <summary>Replaces the contact whole — names, favourite flag, and the entire address list.</summary>
    /// <param name="id">the contact's identifier</param>
    /// <param name="request">the full replacement contact</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="400">Neither name nor address, or an unparsable address</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such contact for this user</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Update(
        Guid id, ContactRequest request, CancellationToken cancellationToken)
    {
        var validated = ContactValidator.Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var saved = await store.UpdateAsync(
            AuthenticatedUser.WebmailUid, id, validated.Value, cancellationToken);
        return saved.IsSuccess ? NoContent() : NotFoundEnveloppe(saved.Error);
    }

    /// <summary>Deletes the contact and its addresses.</summary>
    /// <param name="id">the contact's identifier</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Deleted</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such contact for this user</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        return deleted.IsSuccess ? NoContent() : NotFoundEnveloppe(deleted.Error);
    }

    /// <summary>
    /// Flips the favourite flag alone. Its own route because the star is toggled from a tile
    /// holding a possibly stale copy — a whole-contact PUT from there would clobber a concurrent
    /// edit, the same reason message flags have their own endpoint.
    /// </summary>
    /// <param name="id">the contact's identifier</param>
    /// <param name="request">the new favourite state</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="400">No body</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such contact for this user</response>
    [HttpPut("{id:guid}/Favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetFavorite(
        Guid id, FavoriteRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");

        var saved = await store.SetFavoriteAsync(
            AuthenticatedUser.WebmailUid, id, request.IsFavorite, cancellationToken);
        return saved.IsSuccess ? NoContent() : NotFoundEnveloppe(saved.Error);
    }
}
