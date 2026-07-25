using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Curated sending identities — a webmail preference, not mail-server data. No IMAP session and
/// no credentials cookie: both verbs are database reads, so this lives outside MailController.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class IdentitiesController(
    ISendingIdentityStore store, IAliasesRepository aliases, IUsersRepository users) : ApiBaseController
{
    /// <summary>
    /// The resolved list: the primary address always (FullName label unless overridden), then
    /// every stored row; a row whose alias vanished comes back stale, never silently dropped.
    /// </summary>
    /// <response code="200">The identities, default first</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IdentityListResponse>> List(CancellationToken cancellationToken)
    {
        var (stored, aliasAddresses, fullName) = await LoadSourcesAsync(cancellationToken);
        var resolved = IdentityResolver.Resolve(stored, AuthenticatedUser.Email, fullName, aliasAddresses);
        return Ok(new IdentityListResponse(resolved));
    }

    /// <summary>
    /// Replaces the whole set. Addresses must belong to the caller (primary, a live alias, or an
    /// already-stored row — the last keeps stale identities alive across saves).
    /// </summary>
    /// <param name="request">the full identity list</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="400">A foreign, duplicate or unparsable address, a bad label, or two defaults</response>
    /// <response code="401">Not authenticated</response>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Replace(ReplaceIdentitiesRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));

        var (stored, aliasAddresses, _) = await LoadSourcesAsync(cancellationToken);
        var validated = IdentityResolver.Validate(
            request.Identities ?? [], AuthenticatedUser.Email,
            aliasAddresses, stored.Select(r => r.Address).ToList());
        if (validated.IsFailure) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(validated.Error));

        await store.ReplaceAsync(AuthenticatedUser.WebmailUid, validated.Value, cancellationToken);
        return NoContent();
    }

    private async Task<(IReadOnlyList<Data.Preferences.SendingIdentity> Stored, List<string> AliasAddresses, string? FullName)>
        LoadSourcesAsync(CancellationToken cancellationToken)
    {
        var stored = await store.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        var aliasList = await aliases.GetAliasesAsync(AuthenticatedUser);
        var dbUser = await users.FindByEmailAsync(AuthenticatedUser.Email);
        return (stored, aliasList.ToAddresses(), dbUser?.FullName);
    }
}
