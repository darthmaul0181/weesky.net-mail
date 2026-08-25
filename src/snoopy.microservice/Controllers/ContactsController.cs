using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
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

    /// <summary>The full card, child lines carrying their <c>position</c> handle plus display fields.</summary>
    /// <param name="id">the contact's identifier</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The contact</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such contact for this user</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ContactDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var contact = await store.GetAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        return contact == null ? NotFoundEnveloppe(ContactStore.NotFound) : Ok(contact);
    }

    /// <summary>
    /// The projected avatar. Always an attachment disposition plus nosniff, and an <c>ETag</c> on
    /// <c>card_hash</c> so a client that already has the picture gets a bare 304.
    /// </summary>
    /// <param name="id">the contact's identifier</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The picture</response>
    /// <response code="304">Unchanged since the ETag the client sent</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such contact, or none carries a picture</response>
    [HttpGet("{id:guid}/Photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        var photo = await store.GetPhotoAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        if (photo == null) return NotFoundEnveloppe(ContactStore.NotFound);

        var etag = $"\"{photo.Value.CardHash}\"";
        Response.Headers.ETag = etag;
        Response.Headers.XContentTypeOptions = "nosniff";
        if (Request.Headers.IfNoneMatch.Contains(etag)) return StatusCode(StatusCodes.Status304NotModified);

        return File(photo.Value.Bytes, photo.Value.MediaType, "photo");
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
        // HasPhoto is false by construction: 4a gives the photo no write door (décision 12).
        return Ok(new ContactView(created.Value, write.FirstName, write.LastName, write.Nickname,
            write.IsFavorite,
            [.. write.Addresses.Select(a => IdentityResolver.Canonical(a.Address)).Distinct()],
            write.DisplayName, false));
    }

    /// <summary>
    /// Replaces the contact whole — names, favourite flag, and the entire address list. Sending
    /// back the <c>cardHash</c> GET answered is optional but recommended: it lets the store refuse
    /// the write when the card moved since it was read, rather than silently overwriting it.
    /// </summary>
    /// <param name="id">the contact's identifier</param>
    /// <param name="request">the full replacement contact</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="400">Neither name nor address, an unparsable address, or the card over the 1 MB ceiling</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such contact for this user</response>
    /// <response code="409">The card moved since <c>cardHash</c> was read; reload and retry</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Update(
        Guid id, ContactRequest request, CancellationToken cancellationToken)
    {
        var validated = ContactValidator.Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var saved = await store.UpdateAsync(
            AuthenticatedUser.WebmailUid, id, validated.Value, cancellationToken);
        if (saved.IsSuccess) return NoContent();

        // Exhaustive, not "CardMoved or 404": UpdateAsync can also fail with CardTooLarge (from
        // PrepareCard), and a contact refused for its size is not a missing one. NotFound is the
        // only reason that means "no such row"; CardMoved is the one reason that means "reload and
        // retry"; everything else — today only CardTooLarge — is a rejected body, exactly what
        // Create above already answers with BadRequestEnveloppe for its own failure reasons.
        if (saved.Error == ContactStore.NotFound) return NotFoundEnveloppe(saved.Error);
        if (saved.Error == ContactStore.CardMoved) return ConflictEnveloppe(saved.Error);
        return BadRequestEnveloppe(saved.Error);
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
    /// it and the read is capped before model binding buffers the body to disk. A vCard export of
    /// a whole address book carries its photos, which a CSV never did.
    /// </summary>
    private const int MaxUploadBytes = 20 * 1024 * 1024;

    private const int MaxReportedErrors = 50;

    // The media types a mail client, a phone and an address-book export put on a .vcf file.
    private static readonly string[] VCardMediaTypes = ["text/vcard", "text/x-vcard", "text/directory"];

    internal static readonly string UidTooLong =
        $"The card's UID exceeds {VCardProjector.MaxUidLength} characters";

    // Stored, a fragment would be an invalid vCard on the CardDAV route of 4c.
    internal const string CardIncomplete = "The card has no END:VCARD line";

    // What a pending error still needs to become a sentence, so none is written before the cap.
    private enum ErrorKind { Store, Address, OverLong }

    /// <summary>The rows a file yielded, and what it could not turn into one.</summary>
    private sealed record ImportInput(
        IReadOnlyList<ContactImportRow> Rows,
        IReadOnlyList<(int Line, ErrorKind Kind, string Value)> Pending);

    /// <summary>
    /// Merges a CSV or vCard file into the book and answers what it did. Nothing is overwritten and
    /// a row whose address is already on two contacts is skipped rather than filed at random.
    /// </summary>
    /// <param name="file">the CSV or vCard file</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The report</response>
    /// <response code="400">No file, an empty file, or no recognised column</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost("Import")]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ContactImportReport>> Import(
        IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0) return BadRequestEnveloppe("A file is required");

        // Sized up front: the default capacity of 0 doubles its way past the file's own length,
        // then ToArray copies it again. At this ceiling both live on the large object heap —
        // accepted, because either reader has to hold the whole document anyway.
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var read = IsVCard(file, bytes) ? Cards(bytes) : Csv(bytes);
        if (read.IsFailure) return BadRequestEnveloppe(read.Error);

        var outcome = await store.ImportAsync(
            AuthenticatedUser.WebmailUid, read.Value.Rows, cancellationToken);

        // The store's reasons, the reader's dropped addresses, and its over-long names are one list
        // to the reader: all three name a line in the file they can go and look at. Deduplicated,
        // because the same filler in two e-mail columns is one problem, not two.
        List<(int Line, ErrorKind Kind, string Value)> pending =
        [
            .. outcome.Errors.Select(e => (e.Line, ErrorKind.Store, e.Reason)),
            .. read.Value.Pending,
        ];
        var distinct = pending.Distinct().ToList();

        // Sorted and capped before a single sentence is interpolated: an adversarial file is some
        // millions of rows, and formatting all of them to answer fifty is hundreds of megabytes.
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

    /// <summary>
    /// The media type the picker declared, else the first bytes: a file dropped from a mail client
    /// carries one, one saved by a text editor carries nothing but the BOM in front of its BEGIN.
    /// </summary>
    private static bool IsVCard(IFormFile file, byte[] bytes) =>
        VCardMediaTypes.Any(t => (file.Headers?.ContentType.ToString() ?? string.Empty)
            .StartsWith(t, StringComparison.OrdinalIgnoreCase))
        || FileText.Decode([.. bytes.Take(16)])
            .StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A vCard file, cut before it is parsed so that every card is filed with its own bytes
    /// (décision 1). The two ceilings are measured here, on the chunk: past either of them the
    /// card is a line in error citing its BEGIN:VCARD, exactly like a skipped CSV row.
    /// </summary>
    private static Result<ImportInput> Cards(byte[] bytes)
    {
        var chunks = VCardSplitter.Split(FileText.Decode(bytes));
        if (chunks.Count == 0) return Result.Failure<ImportInput>("The file is empty");

        var rows = new List<ContactImportRow>();
        var pending = new List<(int Line, ErrorKind Kind, string Value)>();
        foreach (var chunk in chunks)
        {
            if (Encoding.UTF8.GetByteCount(chunk.Text) > ContactStore.MaxCardBytes)
                pending.Add((chunk.Line, ErrorKind.Store, ContactStore.CardTooLarge));
            else if (VCardImportMapper.UidOf(chunk.Text) is { Length: > VCardProjector.MaxUidLength })
                pending.Add((chunk.Line, ErrorKind.Store, UidTooLong));
            else if (!VCardSplitter.IsComplete(chunk))
                pending.Add((chunk.Line, ErrorKind.Store, CardIncomplete));
            else
                rows.Add(VCardImportMapper.Map(chunk));
        }

        return Result.Success(new ImportInput(rows, pending));
    }

    /// <summary>
    /// A CSV file, every row carrying the columns its card will be composed from: under décision 1
    /// a contact without a card is an invariant broken, so what the tables do not model travels to
    /// the store as a write rather than through a card writer of its own. Composing here instead
    /// would mean naming the contact's UID before the store has one — an identity of our making,
    /// which no reader is entitled to give.
    /// </summary>
    private static Result<ImportInput> Csv(byte[] bytes)
    {
        var document = CsvReader.Read(bytes);
        if (document.Header.Count == 0) return Result.Failure<ImportInput>("The file is empty");

        var mapped = ContactCsvMapper.Map(document);
        if (mapped.IsFailure) return Result.Failure<ImportInput>(mapped.Error);

        var rows = new List<ContactImportRow>();
        var pending = new List<(int Line, ErrorKind Kind, string Value)>();
        foreach (var row in mapped.Value)
        {
            pending.AddRange(row.RejectedAddresses.Select(a => (row.Line, ErrorKind.Address, a)));
            pending.AddRange(row.OverLongFields.Select(f => (row.Line, ErrorKind.OverLong, f)));
            rows.Add(new ContactImportRow(row.Line, row.FirstName, row.LastName, row.Nickname,
                row.IsFavorite, row.Addresses, null, null, WriteOf(row)));
        }

        return Result.Success(new ImportInput(rows, pending));
    }

    // The CSV columns the tables do not model, each on the vCard family it belongs to. The table
    // is ContactVCardWriter's, which this composes through instead (spec, § Le moteur).
    private static readonly (string Key, string Type)[] PhoneColumns =
    [
        ("mobilephone", "CELL"), ("othermobile", "CELL"), ("homephone", "HOME,VOICE"),
        ("businessphone", "WORK,VOICE"), ("homefax", "HOME,FAX"), ("businessfax", "WORK,FAX"),
        ("otherphone", "VOICE"),
    ];

    // Outlook's "Title" is the honorific — N's fourth component — and its "Job Title" the role.
    // The addresses are the store's to fill: it is the one that caps and canonicalises them. Every
    // line passes ContactValidator's own blank-line rule, so what ContactWrite promises about the
    // lines it carries holds for the ones built here too.
    private static ContactWrite WriteOf(ContactCsvRow row) =>
        new(row.FirstName, row.LastName, row.Nickname, null,
            Value(row, "middlename"), Value(row, "title"), null,
            Value(row, "company"), Value(row, "department"), Value(row, "jobtitle"),
            Value(row, "birthday"), Value(row, "webpage"), Value(row, "notes"), row.IsFavorite,
            [],
            [.. PhoneColumns.Select(c => new ContactWritePhone(null, Value(row, c.Key) ?? string.Empty, c.Type))
                .Where(ContactValidator.IsMeaningful)],
            [.. Postal(row, "home", "HOME", null)
                .Concat(Postal(row, "business", "WORK", Value(row, "officelocation")))
                .Where(ContactValidator.IsMeaningful)],
            "imported");

    // "Office Location" is the extended slot — the one place it means what it says.
    private static IEnumerable<ContactWriteAddress> Postal(
        ContactCsvRow row, string prefix, string type, string? extended)
    {
        string?[] parts =
        [
            extended, Value(row, $"{prefix}street"), Value(row, $"{prefix}city"),
            Value(row, $"{prefix}state"), Value(row, $"{prefix}postalcode"), Value(row, $"{prefix}country"),
        ];
        if (parts.All(p => p == null)) yield break;

        yield return new ContactWriteAddress(
            null, type, null, parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
    }

    private static string? Value(ContactCsvRow row, string key) =>
        row.Extras.TryGetValue(key, out var value) ? value : null;

    /// <summary>The whole book as a CSV file, in the columns other clients read.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The file</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet("Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Export(CancellationToken cancellationToken)
    {
        var contacts = await store.ExportAsync(AuthenticatedUser.WebmailUid, cancellationToken);

        return File(ContactCsvExporter.Write(contacts), "text/csv",
            $"contacts-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }
}
