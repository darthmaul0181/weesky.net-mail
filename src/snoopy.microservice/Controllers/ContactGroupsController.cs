using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The user's groups — the species <see cref="ContactsController"/> refuses, on a controller of
/// its own. Three statuses and no fourth: 400 for a body that means nothing, 404 for an id this
/// book does not hold (another user's group included, which must be indistinguishable from one
/// that does not exist), and never 409 — a group write carries no precondition to lose.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class ContactGroupsController(IContactGroupStore store) : ApiBaseController
{
    /// <summary>Every group, each with the members this book actually holds.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The groups</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ContactGroupsResponse>> List(CancellationToken cancellationToken)
    {
        var groups = await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        return Ok(new ContactGroupsResponse(groups));
    }

    /// <summary>Creates an empty group and answers it, id included.</summary>
    /// <param name="request">the group's name</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Created</response>
    /// <response code="400">No name, a name over 255 characters, or the cap reached</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ContactGroupView>> Create(
        ContactGroupRequest request, CancellationToken cancellationToken)
    {
        var validated = ContactValidator.ValidateGroupName(request?.Name);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var created = await store.CreateAsync(
            AuthenticatedUser.WebmailUid, validated.Value, cancellationToken);

        // The only reason left is the ceiling, which is a refused body, not a missing group.
        return created.IsSuccess ? Ok(created.Value) : BadRequestEnveloppe(created.Error);
    }

    /// <summary>Renames the group. Nothing else of its card moves — its members least of all.</summary>
    /// <param name="id">the group's identifier</param>
    /// <param name="request">the new name</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="400">No name, or a name over 255 characters</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such group for this user</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Rename(
        Guid id, ContactGroupRequest request, CancellationToken cancellationToken)
    {
        var validated = ContactValidator.ValidateGroupName(request?.Name);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var saved = await store.RenameAsync(
            AuthenticatedUser.WebmailUid, id, validated.Value, cancellationToken);
        return Answer(saved);
    }

    /// <summary>Deletes the group. The contacts it listed are untouched.</summary>
    /// <param name="id">the group's identifier</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Deleted</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such group for this user</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        return Answer(deleted);
    }

    /// <summary>
    /// Adds a batch of contacts to the group. An id this user does not own — and a group's own id
    /// — resolves to nothing and is skipped in silence: a batch may not half-fail, and a 404 on a
    /// foreign id would confirm that it exists.
    /// </summary>
    /// <param name="id">the group's identifier</param>
    /// <param name="request">the contacts to add</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Added, whether or not every id matched</response>
    /// <response code="400">No id, or more than 200</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such group for this user</response>
    [HttpPost("{id:guid}/Members")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AddMembers(
        Guid id, ContactGroupMembersRequest request, CancellationToken cancellationToken)
    {
        if (Refuse(request?.ContactIds) is { } refusal) return refusal;

        var saved = await store.AddMembersAsync(
            AuthenticatedUser.WebmailUid, id, request!.ContactIds!, cancellationToken);
        return Answer(saved);
    }

    /// <summary>Removes a batch of contacts from the group, under the same silent-skip rule.</summary>
    /// <param name="id">the group's identifier</param>
    /// <param name="request">the contacts to remove</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Removed, whether or not every id matched</response>
    /// <response code="400">No id, or more than 200</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such group for this user</response>
    [HttpDelete("{id:guid}/Members")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveMembers(
        Guid id, ContactGroupMembersRequest request, CancellationToken cancellationToken)
    {
        if (Refuse(request?.ContactIds) is { } refusal) return refusal;

        var saved = await store.RemoveMembersAsync(
            AuthenticatedUser.WebmailUid, id, request!.ContactIds!, cancellationToken);
        return Answer(saved);
    }

    /// <summary>The one gate both member routes pass, so the two cannot drift on what they refuse.
    /// Bounded by <see cref="ContactsController.MaxBatch"/> — one ceiling, one number.</summary>
    private ActionResult? Refuse(IReadOnlyList<Guid>? ids) => ids switch
    {
        null or { Count: 0 } => BadRequestEnveloppe("At least one contact is required"),
        { Count: > ContactsController.MaxBatch } =>
            BadRequestEnveloppe($"No more than {ContactsController.MaxBatch} contacts at a time"),
        _ => null,
    };

    /// <summary>The four writes answer alike: 204, or 404 for an id this book does not hold —
    /// their only failure, the name having been validated before the store was ever called.</summary>
    private ActionResult Answer(Result saved) =>
        saved.IsSuccess ? NoContent() : NotFoundEnveloppe(saved.Error);
}
