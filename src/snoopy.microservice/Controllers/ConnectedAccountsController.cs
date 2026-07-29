using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MimeKit;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The mailboxes a user attached to their session: list them, attach one, re-enter its password,
/// detach it. This is the only place another mailbox's password enters the system, and three rules
/// hold across it.
///
/// A user never supplies a host. Endpoints come from appsettings for a local shared mailbox and
/// from the admin-curated domain row otherwise, so no request field can ever become the address of
/// an outbound connection.
///
/// A password is verified against the real server before it is stored, and the answer says only
/// that the mail server refused — never what it said, never the credentials that produced it.
///
/// Nothing here returns or logs a secret. The response records carry no cipher and no password,
/// and validity is reported from a local decrypt rather than from a connection: opening one per
/// listed account would make the settings page take seconds and hammer the providers.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class ConnectedAccountsController(
    IConnectedAccountStore accounts,
    IExternalDomainStore domains,
    ISendingIdentityStore identities,
    IMailCredentialStore credentials,
    IWebmailUserStore users,
    IImapConnectionFactory imap,
    IOptionsMonitor<MailOptions> options,
    ILogger<ConnectedAccountsController> logger) : ApiBaseController
{
    /// <summary>The probe is never persisted, so it needs no account id of its own.</summary>
    private const string ProbeAccountId = "probe";

    private const string ServerRefused = "Could not sign in to this mailbox. Check the address and the password.";

    /// <summary>A domain the user may not pick, whether it is absent or unusable — the caller has
    /// nothing to do about the difference, and it is administrator information.</summary>
    private const string UnknownDomain = "Unknown domain";

    /// <summary>The width of connected_accounts.email, which the default identity's address mirrors.</summary>
    internal const int MaxEmailLength = 255;

    /// <summary>
    /// The mailboxes attached to this session, each with the label of its default identity and
    /// whether its stored password still opens under the session key.
    /// </summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The connected accounts</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ConnectedAccountResponse>>> List(
        CancellationToken cancellationToken)
    {
        var kek = await ResolveKekAsync(cancellationToken);
        if (kek.IsFailure) return UnauthorizedEnveloppe(kek.Error);

        var rows = await accounts.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        var byId = (await domains.ListAsync(cancellationToken)).ToDictionary(d => d.Id);

        var responses = new List<ConnectedAccountResponse>(rows.Count);
        foreach (var row in rows)
        {
            var domain = row.DomainId is { } id && byId.TryGetValue(id, out var found) ? found : null;
            responses.Add(Describe(
                row, domain,
                await DefaultLabelAsync(row, cancellationToken),
                ConnectedAccountCipher.Decrypt(kek.Value, row.Cipher).IsSuccess));
        }

        return Ok(responses);
    }

    /// <summary>
    /// Attaches a mailbox after signing in to it, so a password that does not work is refused
    /// rather than stored.
    /// </summary>
    /// <param name="request">the domain to connect from (null for a local mailbox), address and password</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The connected account</response>
    /// <response code="400">Unusable address or password, an unknown domain, the caller's own mailbox, or already connected</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="429">Too many authentication attempts</response>
    /// <response code="502">The mail server refused the credentials</response>
    [HttpPost]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ConnectedAccountResponse>> Connect(
        ConnectAccountRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequestEnveloppe("Request body is required");

        var kek = await ResolveKekAsync(cancellationToken);
        if (kek.IsFailure) return UnauthorizedEnveloppe(kek.Error);

        if (RefusePassword(request.Password) is { } invalidPassword) return invalidPassword;

        if (!MailboxAddress.TryParse(RecipientAddressParser.Options, request.Email ?? string.Empty, out var parsed))
            return BadRequestEnveloppe("This is not a valid email address");
        var email = IdentityResolver.Canonical(parsed.Address);

        // The column is finite too: bound the address the way the password is bounded, rather
        // than let a strict-mode MariaDB turn an over-long login into a 500.
        if (email.Length > MaxEmailLength)
            return BadRequestEnveloppe($"An address must be at most {MaxEmailLength} characters");

        if (request.DomainId is null && email == IdentityResolver.Canonical(AuthenticatedUser.Email))
            return BadRequestEnveloppe("You are already signed in to this mailbox");

        ExternalDomain? domain = null;
        if (request.DomainId is { } domainId)
        {
            domain = await domains.FindAsync(domainId, cancellationToken);
            if (domain is null) return BadRequestEnveloppe(UnknownDomain);
        }

        var probe = BuildProbe(domain, email, request.Password);
        if (probe is null) return BadRequestEnveloppe(UnknownDomain);

        var verified = await VerifyAsync(probe, email, cancellationToken);
        if (verified.IsFailure) return BadGatewayEnveloppe(verified.Error);

        var created = await accounts.CreateAsync(new ConnectedAccount
        {
            UserId = AuthenticatedUser.WebmailUid,
            DomainId = request.DomainId,
            Email = email,
            Cipher = ConnectedAccountCipher.Encrypt(kek.Value, request.Password)
        }, cancellationToken);
        if (created.IsFailure) return BadRequestEnveloppe(created.Error);

        // The store writes the default identity with an empty label, so the UI falls back to the
        // address and a later rename of the mailbox leaves no stale name behind.
        return Ok(Describe(created.Value, domain, string.Empty, credentialsValid: true));
    }

    /// <summary>
    /// Replaces a connected mailbox's stored password, verifying the new one against the server
    /// first — a refused password leaves the previous cipher untouched.
    /// </summary>
    /// <param name="id">the connected account</param>
    /// <param name="request">the new password</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="400">Missing or oversized password</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such account</response>
    /// <response code="429">Too many authentication attempts</response>
    /// <response code="502">The mail server refused the credentials</response>
    [HttpPut("{id:guid}/Password")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> UpdatePassword(
        Guid id, ConnectedAccountPasswordRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequestEnveloppe("Request body is required");

        var kek = await ResolveKekAsync(cancellationToken);
        if (kek.IsFailure) return UnauthorizedEnveloppe(kek.Error);

        if (RefusePassword(request.Password) is { } invalidPassword) return invalidPassword;

        var row = await accounts.FindAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        if (row is null) return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);

        ExternalDomain? domain = null;
        if (row.DomainId is { } domainId)
        {
            domain = await domains.FindAsync(domainId, cancellationToken);
            if (domain is null) return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);
        }

        var probe = BuildProbe(domain, row.Email, request.Password);
        if (probe is null) return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);

        var verified = await VerifyAsync(probe, row.Email, cancellationToken);
        if (verified.IsFailure) return BadGatewayEnveloppe(verified.Error);

        await accounts.UpdateCipherAsync(
            row, ConnectedAccountCipher.Encrypt(kek.Value, request.Password), cancellationToken);
        return NoContent();
    }

    /// <summary>Detaches a mailbox, with its identities and folder-role overrides.</summary>
    /// <param name="id">the connected account</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Disconnected</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Disconnect(Guid id, CancellationToken cancellationToken)
    {
        if (await accounts.FindAsync(AuthenticatedUser.WebmailUid, id, cancellationToken) is null)
            return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);

        await accounts.DeleteAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// The domains a mailbox may be connected from, for the connect form. Names and ids only:
    /// hosts, ports and transport security are administrator information.
    /// </summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The choice list</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet("Domains")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ExternalDomainChoice>>> Domains(
        CancellationToken cancellationToken)
    {
        var rows = await domains.ListAsync(cancellationToken);
        return Ok(rows.Select(d => new ExternalDomainChoice(d.Id, d.Name)).ToList());
    }

    private static ConnectedAccountResponse Describe(
        ConnectedAccount row, ExternalDomain? domain, string displayName, bool credentialsValid) =>
        new(row.Id, row.Email, displayName, row.DomainId, domain?.Name,
            SieveSupported: row.DomainId is null || domain?.SieveHost is not null,
            credentialsValid, row.CreationDate);

    private async Task<string> DefaultLabelAsync(ConnectedAccount row, CancellationToken cancellationToken)
    {
        var stored = await identities.GetAsync(
            AuthenticatedUser.WebmailUid, row.Id.ToString(), cancellationToken);
        return stored.FirstOrDefault(i => i.Address == row.Email)?.DisplayName ?? string.Empty;
    }

    /// <summary>
    /// The key every stored cipher hangs off. A v1 cookie carries none, so it is derived from the
    /// persisted salt; re-issuing the upgraded cookie is the connection resolver's job.
    /// </summary>
    private async Task<Result<byte[]>> ResolveKekAsync(CancellationToken cancellationToken)
    {
        var retrieved = credentials.Retrieve(Request);
        if (retrieved.IsFailure) return Result.Failure<byte[]>(retrieved.Error);
        if (retrieved.Value.Kek is { } kek) return Result.Success(kek);

        var salt = await users.GetOrCreateKdfSaltAsync(AuthenticatedUser.Email, cancellationToken);
        return Result.Success(ConnectedAccountCipher.DeriveKek(retrieved.Value.Password, salt));
    }

    /// <summary>Null when the password cannot be stored, otherwise the 400 to answer with.</summary>
    private ActionResult? RefusePassword(string? password)
    {
        if (string.IsNullOrEmpty(password)) return BadRequestEnveloppe("A password is required");

        // The cipher column is finite and Encrypt throws past its bound: answer it here instead.
        return Encoding.UTF8.GetByteCount(password) > ConnectedAccountCipher.MaxSecretLength
            ? BadRequestEnveloppe(
                $"The password may not exceed {ConnectedAccountCipher.MaxSecretLength} bytes")
            : null;
    }

    /// <summary>Null when the domain row holds an unusable endpoint — logged, never described.</summary>
    private MailAccountConnection? BuildProbe(ExternalDomain? domain, string email, string password)
    {
        if (domain is null)
            return MailConnectionBuilder.Home(options.CurrentValue, ProbeAccountId, email, password);

        if (MailConnectionBuilder.TryExternal(domain, ProbeAccountId, email, password, out var connection))
            return connection;

        logger.LogError(
            "External domain {DomainName} ({DomainId}) holds an unusable security value",
            domain.Name, domain.Id);
        return null;
    }

    /// <summary>
    /// Opens a real session and closes it at once: the point is to refuse a wrong password before
    /// it is stored, and nothing about the session is kept.
    /// </summary>
    private async Task<Result> VerifyAsync(
        MailAccountConnection probe, string email, CancellationToken cancellationToken)
    {
        var session = await imap.OpenAsync(probe, cancellationToken);
        if (session.IsFailure)
        {
            // Neither the server's own text nor the credentials that produced it belong in a log.
            logger.LogWarning("Connected-account credentials refused for {Email}", email);
            return Result.Failure(ServerRefused);
        }

        await session.Value.DisposeAsync();
        return Result.Success();
    }
}
