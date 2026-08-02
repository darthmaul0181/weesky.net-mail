using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Curated sending identities — a webmail preference, not mail-server data. No IMAP session and
/// no credentials cookie: both verbs are database reads, so this lives outside MailController.
/// The account id is decoded by <see cref="IAccountConnectionResolver.AccountIdFrom"/>, the same
/// reader every mail endpoint uses, so header and <c>?account=</c> mean the same thing here as
/// they do there; a connected account's set is validated against the account address itself,
/// since there is no alias list on our server for a mailbox we do not administer.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class IdentitiesController(
    ISendingIdentityStore store, IAliasesRepository aliases, IUsersRepository users,
    IConnectedAccountStore accounts) : ApiBaseController
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
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope.Error is not null) return scope.Error;
        if (scope.Account is { } account)
        {
            var connectedStored = await store.GetAsync(AuthenticatedUser.WebmailUid, account.Id.ToString(), cancellationToken);
            return Ok(new IdentityListResponse(IdentityResolver.ResolveConnected(connectedStored, account.Email)));
        }

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
        if (request == null) return BadRequestEnveloppe("Request body is required");

        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope.Error is not null) return scope.Error;
        if (scope.Account is { } account)
        {
            var connectedValidated = IdentityResolver.ValidateConnected(request.Identities ?? [], account.Email);
            if (connectedValidated.IsFailure) return BadRequestEnveloppe(connectedValidated.Error);

            await store.ReplaceAsync(AuthenticatedUser.WebmailUid, account.Id.ToString(), connectedValidated.Value, cancellationToken);
            return NoContent();
        }

        var (stored, aliasAddresses, _) = await LoadSourcesAsync(cancellationToken);
        var validated = IdentityResolver.Validate(
            request.Identities ?? [], AuthenticatedUser.Email,
            aliasAddresses, stored.Select(r => r.Address).ToList());
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        await store.ReplaceAsync(AuthenticatedUser.WebmailUid, AccountScope.Primary, validated.Value, cancellationToken);
        return NoContent();
    }

    private async Task<(IReadOnlyList<Data.Preferences.SendingIdentity> Stored, List<string> AliasAddresses, string? FullName)>
        LoadSourcesAsync(CancellationToken cancellationToken)
    {
        var stored = await store.GetAsync(AuthenticatedUser.WebmailUid, AccountScope.Primary, cancellationToken);
        var aliasList = await aliases.GetAliasesAsync(AuthenticatedUser, cancellationToken);
        var dbUser = await users.FindByEmailAsync(AuthenticatedUser.Email, cancellationToken);
        return (stored, aliasList.ToAddresses(), dbUser?.FullName);
    }

    /// <summary>
    /// Reads the account id through the resolver's own decoder — no credentials cookie, no live
    /// connection: these are database verbs over stored rows. Null <c>Account</c> with null
    /// <c>Error</c> means the primary path; an unparseable, unknown or foreign id is a 404,
    /// indistinguishable.
    /// </summary>
    private async Task<(Data.Preferences.ConnectedAccount? Account, ActionResult? Error)> ResolveScopeAsync(
        CancellationToken cancellationToken)
    {
        var accountId = IAccountConnectionResolver.AccountIdFrom(Request);
        if (accountId is null) return (null, null);

        if (!Guid.TryParse(accountId, out var id))
            return (null, NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound));

        var account = await accounts.FindAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        return account is null
            ? (null, NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound))
            : (account, null);
    }
}
