using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Curated sending identities — a webmail preference, not mail-server data. No IMAP session and
/// no credentials cookie: both verbs are database reads, so this lives outside the mail controllers.
/// The account id is decoded by <see cref="IAccountConnectionResolver.AccountIdFrom"/>, the same
/// reader every mail endpoint uses, so header and <c>?account=</c> mean the same thing here as
/// they do there; a connected account's set is validated against the account address itself,
/// since there is no alias list on our server for a mailbox we do not administer.
///
/// A platform that does not enforce ownership (<see cref="IAliasDirectory.EnforcesOwnership"/>)
/// puts the primary mailbox on that very same path: with no alias list to judge against, the rule
/// is the connected one rather than a third one of its own.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class IdentitiesController(
    ISendingIdentityStore store, IAliasDirectory aliasDirectory, IProfileReader profiles,
    IConnectedAccountStore accounts) : ApiBaseController
{
    /// <summary>
    /// The resolved list. In strict mode (<see cref="IAliasDirectory.EnforcesOwnership"/>): the
    /// primary address always first (FullName label unless overridden), then every stored row; a
    /// row whose alias vanished comes back stale, never silently dropped. In free mode — a
    /// connected account, or a platform that enforces no ownership at all — there is no alias list
    /// to check a row against, so the connected-style list applies instead: the account address
    /// first, then every stored row as-is, and nothing is ever stale.
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
        if (UnverifiableScope(scope.Account) is { } unverifiable)
        {
            var rows = await store.GetAsync(AuthenticatedUser.WebmailUid, unverifiable.Scope, cancellationToken);
            return Ok(new IdentityListResponse(IdentityResolver.ResolveConnected(rows, unverifiable.Address)));
        }

        var (stored, aliasAddresses, fullName) = await LoadSourcesAsync(cancellationToken);
        var resolved = IdentityResolver.Resolve(stored, AuthenticatedUser.Email, fullName, aliasAddresses);
        return Ok(new IdentityListResponse(resolved));
    }

    /// <summary>
    /// Replaces the whole set. In strict mode, addresses must belong to the caller (primary, a
    /// live alias, or an already-stored row — the last keeps stale identities alive across saves).
    /// In free mode there is no alias list to check ownership against, so any well-formed address
    /// is accepted as long as the set still contains the account address itself.
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
        if (UnverifiableScope(scope.Account) is { } unverifiable)
        {
            var accepted = IdentityResolver.ValidateConnected(request.Identities ?? [], unverifiable.Address);
            if (accepted.IsFailure) return BadRequestEnveloppe(accepted.Error);

            await store.ReplaceAsync(AuthenticatedUser.WebmailUid, unverifiable.Scope, accepted.Value, cancellationToken);
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

    /// <summary>
    /// The scope whose address is the only one anything can vouch for, and which therefore takes
    /// <c>ResolveConnected</c>/<c>ValidateConnected</c>: a connected account, or — when the platform
    /// enforces no ownership at all — the primary mailbox itself. Null means the alias-checked path.
    /// </summary>
    private (string Scope, string Address)? UnverifiableScope(Data.Preferences.ConnectedAccount? account)
    {
        if (account is { } connected) return (connected.Id.ToString(), connected.Email);
        return aliasDirectory.EnforcesOwnership ? null : (AccountScope.Primary, AuthenticatedUser.Email);
    }

    private async Task<(IReadOnlyList<Data.Preferences.SendingIdentity> Stored, IReadOnlyList<string> AliasAddresses, string? FullName)>
        LoadSourcesAsync(CancellationToken cancellationToken)
    {
        var stored = await store.GetAsync(AuthenticatedUser.WebmailUid, AccountScope.Primary, cancellationToken);
        var aliasAddresses = await aliasDirectory.GetAddressesAsync(AuthenticatedUser, cancellationToken);
        var fullName = await profiles.GetDisplayNameAsync(AuthenticatedUser, cancellationToken);
        return (stored, aliasAddresses, fullName);
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
            return (null, ConnectedAccountError(ConnectedAccountErrors.AccountNotFound));

        var account = await accounts.FindAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        return account is null
            ? (null, ConnectedAccountError(ConnectedAccountErrors.AccountNotFound))
            : (account, null);
    }
}
