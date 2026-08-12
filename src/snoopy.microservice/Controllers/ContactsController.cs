using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Contacts;
using weesky.Snoopy.Microservice.Services.Csv;

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

    /// <summary>The most ids one bulk call may name — the batch size PUT /Mail/Messages/Flags takes.</summary>
    private const int MaxBatch = 200;

    /// <summary>
    /// Deletes a batch. An id this user does not own resolves to nothing and is skipped in silence:
    /// a batch may not half-fail, and a 404 on a foreign id would confirm that it exists.
    /// </summary>
    /// <param name="request">the ids to delete</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Deleted, whether or not every id matched</response>
    /// <response code="400">No id, or more than 200</response>
    /// <response code="401">Not authenticated</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> DeleteMany(
        BulkContactsRequest request, CancellationToken cancellationToken)
    {
        if (Refuse(request?.Ids) is { } refusal) return refusal;

        await store.DeleteManyAsync(AuthenticatedUser.WebmailUid, request!.Ids, cancellationToken);
        return NoContent();
    }

    /// <summary>Sets or clears the favourite flag over a batch, under the same silent-skip rule.</summary>
    /// <param name="request">the ids and the flag they are given</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Applied, whether or not every id matched</response>
    /// <response code="400">No id, or more than 200</response>
    /// <response code="401">Not authenticated</response>
    [HttpPut("Favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> SetFavoriteMany(
        BulkFavoriteRequest request, CancellationToken cancellationToken)
    {
        if (Refuse(request?.Ids) is { } refusal) return refusal;

        await store.SetFavoriteManyAsync(
            AuthenticatedUser.WebmailUid, request!.Ids, request.IsFavorite, cancellationToken);
        return NoContent();
    }

    /// <summary>The one gate both bulk routes pass, so the two cannot drift on what they refuse.</summary>
    private ActionResult? Refuse(IReadOnlyList<Guid>? ids) => ids switch
    {
        null or { Count: 0 } => BadRequestEnveloppe("At least one contact is required"),
        { Count: > MaxBatch } => BadRequestEnveloppe($"No more than {MaxBatch} contacts at a time"),
        _ => null,
    };

    /// <summary>
    /// What bounds the request. A constant rather than configuration, so the attribute can carry
    /// it and the read is capped before model binding buffers the body to disk.
    /// </summary>
    private const int MaxImportBytes = 5 * 1024 * 1024;

    private const int MaxReportedErrors = 50;

    // What a pending error still needs to become a sentence, so none is written before the cap.
    private enum ErrorKind { Store, Address, OverLong }

    /// <summary>
    /// Merges a CSV file into the book and answers what it did. Nothing is overwritten and a row
    /// whose address is already on two contacts is skipped rather than filed at random.
    /// </summary>
    /// <param name="file">the CSV file</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The report</response>
    /// <response code="400">No file, an empty file, or no recognised column</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost("Import")]
    [RequestSizeLimit(MaxImportBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ContactImportReport>> Import(
        IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0) return BadRequestEnveloppe("A file is required");

        // Sized up front: the default capacity of 0 doubles its way to 8 MB on a 5 MB file, then
        // ToArray copies another 5 MB out. The length is already trusted one line above.
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);

        var document = CsvReader.Read(buffer.ToArray());
        if (document.Header.Count == 0) return BadRequestEnveloppe("The file is empty");

        var mapped = ContactCsvMapper.Map(document);
        if (mapped.IsFailure) return BadRequestEnveloppe(mapped.Error);

        var rows = mapped.Value;
        var outcome = await store.ImportAsync(
            AuthenticatedUser.WebmailUid,
            [.. rows.Select(r => new ContactImportRow(
                r.Line, r.FirstName, r.LastName, r.Nickname, r.IsFavorite, r.Addresses,
                ContactVCardWriter.Write(r)))],
            cancellationToken);

        // The store's reasons, the mapper's dropped addresses, and its over-long names are one list
        // to the reader: all three name a line in the file they can go and look at. Deduplicated,
        // because the same filler in two e-mail columns is one problem, not two.
        List<(int Line, ErrorKind Kind, string Value)> pending =
        [
            .. outcome.Errors.Select(e => (e.Line, ErrorKind.Store, e.Reason)),
            .. rows.SelectMany(r => r.RejectedAddresses.Select(a => (r.Line, ErrorKind.Address, a))),
            .. rows.SelectMany(r => r.OverLongFields.Select(f => (r.Line, ErrorKind.OverLong, f))),
        ];
        var distinct = pending.Distinct().ToList();

        // Sorted and capped before a single sentence is interpolated: an adversarial 5 MB file is
        // some 870 000 rows, and formatting all of them to answer fifty is tens of megabytes.
        var reported = distinct.OrderBy(e => e.Line).Take(MaxReportedErrors)
            .Select(e => new ContactImportError(e.Line, e.Kind switch
            {
                ErrorKind.Address => $"'{e.Value}' is not a valid e-mail address and was ignored",
                ErrorKind.OverLong => $"The {e.Value} on this row was too long and was left out",
                _ => e.Value,
            }));

        return Ok(new ContactImportReport(
            outcome.Created, outcome.Merged, outcome.Skipped, outcome.Failed,
            distinct.Count, [.. reported]));
    }

    /// <summary>The whole book as a CSV file, in the columns other clients read.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The file</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet("Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Export(CancellationToken cancellationToken)
    {
        var contacts = await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);

        return File(ContactCsvExporter.Write(contacts), "text/csv",
            $"contacts-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }
}
