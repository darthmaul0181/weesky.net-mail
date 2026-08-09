using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MimeKit;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.ConnectedAccounts;
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
    IOAuthHandshakeStore handshakes,
    IOAuthTokenService oauth,
    IOptionsMonitor<MailOptions> options,
    ILogger<ConnectedAccountsController> logger) : ApiBaseController
{
    /// <summary>The probe is never persisted, so it needs no account id of its own.</summary>
    private const string ProbeAccountId = "probe";

    private const string ServerRefused = "Could not sign in to this mailbox. Check the address and the password.";

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
        var identitiesByAccount = (await identities.GetAllAsync(AuthenticatedUser.WebmailUid, cancellationToken))
            .ToLookup(i => i.AccountId);

        var responses = new List<ConnectedAccountResponse>(rows.Count);
        foreach (var row in rows)
        {
            var domain = row.DomainId is { } id && byId.TryGetValue(id, out var found) ? found : null;
            responses.Add(Describe(
                row, domain,
                DefaultLabel(identitiesByAccount[row.Id.ToString()], row.Email),
                ConnectedAccountCipher.Decrypt(
                    kek.Value, row.Cipher, ConnectedAccountCipher.Context(row)).IsSuccess));
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
            return BadRequestEnveloppe(ConnectedAccountErrors.InvalidEmailAddress);
        var email = IdentityResolver.Canonical(parsed.Address);

        // The column is finite too: bound the address the way the password is bounded, rather
        // than let a strict-mode MariaDB turn an over-long login into a 500.
        if (email.Length > MaxEmailLength)
            return BadRequestEnveloppe(ConnectedAccountErrors.AddressTooLong);

        if (request.DomainId is null && email == IdentityResolver.Canonical(AuthenticatedUser.Email))
            return BadRequestEnveloppe(ConnectedAccountErrors.AlreadySignedIn);

        ExternalDomain? domain = null;
        if (request.DomainId is { } domainId)
        {
            domain = await domains.FindAsync(domainId, cancellationToken);
            if (domain is null) return BadRequestEnveloppe(ConnectedAccountErrors.UnknownDomain);

            // A password probed here would be stored as a Password row on a provider domain —
            // a mode divergence no screen offers and the closed credential design exists to avoid.
            if (domain.AuthMode is MailAuthMode.OAuth2)
                return BadRequestEnveloppe(ConnectedAccountErrors.ProviderDomain);
        }

        var probe = BuildProbe(domain, email, request.Password);
        if (probe is null) return BadRequestEnveloppe(ConnectedAccountErrors.UnknownDomain);

        var verified = await VerifyAsync(probe, email, cancellationToken);
        if (verified.IsFailure) return BadGatewayEnveloppe(verified.Error);

        // The id is minted here rather than by the store: the cipher is bound to it, so it has to
        // exist before the password is encrypted.
        var row = new ConnectedAccount
        {
            Id = Guid.NewGuid(),
            UserId = AuthenticatedUser.WebmailUid,
            DomainId = request.DomainId,
            AuthMode = MailAuthMode.Password,
            Email = email
        };
        row.Cipher = ConnectedAccountCipher.Encrypt(
            kek.Value, request.Password, ConnectedAccountCipher.Context(row));

        var created = await accounts.CreateAsync(row, cancellationToken);
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

        // A password encrypted under this row's oauth2 context, with auth_mode still saying
        // token, could never authenticate again. Reconnecting is the OAuth twin of this endpoint —
        // OAuthStart's reconnect branch makes the mirror-image check, in the other direction.
        if (row.AuthMode is MailAuthMode.OAuth2)
            return BadRequestEnveloppe(ConnectedAccountErrors.AccountUsesProvider);

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
            row,
            ConnectedAccountCipher.Encrypt(
                kek.Value, request.Password, ConnectedAccountCipher.Context(row)),
            cancellationToken);
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
        return Ok(rows.Select(d => new ExternalDomainChoice(d.Id, d.Name, d.AuthMode)).ToList());
    }

    /// <summary>
    /// Begins a consent. Answers the URL to navigate to; nothing is written until Complete.
    /// </summary>
    /// <param name="request">the domain to attach from, or the account to re-authenticate</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The authorization URL and its state</response>
    /// <response code="400">Not exactly one of domainId/accountId, or a domain that is not OAuth</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such account</response>
    /// <response code="429">Too many authentication attempts</response>
    [HttpPost("OAuth/Start")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OAuthStartResponse>> OAuthStart(
        OAuthStartRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequestEnveloppe("Request body is required");
        if (request.DomainId is null == request.AccountId is null)
            return BadRequestEnveloppe(ConnectedAccountErrors.OAuthStartTargetRequired);

        var domainId = request.DomainId;
        if (request.AccountId is { } accountId)
        {
            var row = await accounts.FindAsync(AuthenticatedUser.WebmailUid, accountId, cancellationToken);
            if (row?.DomainId is null) return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);

            // The mirror of the UpdatePassword guard: a Password row re-consented here would hold
            // a refresh token its own auth_mode says to replay as a password.
            if (row.AuthMode is not MailAuthMode.OAuth2)
                return BadRequestEnveloppe(ConnectedAccountErrors.AccountUsesPassword);
            domainId = row.DomainId;
        }

        var domain = await domains.FindAsync(domainId!.Value, cancellationToken);
        if (domain is null) return BadRequestEnveloppe(ConnectedAccountErrors.UnknownDomain);
        if (domain.AuthMode is not MailAuthMode.OAuth2)
            return BadRequestEnveloppe(ConnectedAccountErrors.NotAProviderDomain);
        if (!OAuthProviderConfig.TryFrom(domain, out var provider))
        {
            // The spec's rule for an unusable row: logged as an administrator error, like the
            // resolver path — Domains advertised this button off authMode alone.
            logger.LogError(
                "External domain {DomainName} ({DomainId}) is in OAuth2 mode but its provider " +
                "configuration is incomplete",
                domain.Name, domain.Id);
            return BadRequestEnveloppe(ConnectedAccountErrors.ProviderConfigIncomplete);
        }

        var handshake = handshakes.Start(AuthenticatedUser.WebmailUid, domain.Id, request.AccountId);
        return Ok(new OAuthStartResponse(AuthorizationUrl(provider, handshake), handshake.State));
    }

    /// <summary>
    /// Where the provider sends the browser back. Anonymous by necessity: this is a cross-site
    /// top-level navigation and both session cookies are SameSite=Strict, so nothing here can
    /// identify the caller. It therefore writes nothing — it exchanges the code and parks the
    /// result for the same-site Complete call that follows.
    /// </summary>
    /// <param name="code">the authorization code, exchanged server-side and never forwarded</param>
    /// <param name="state">the handle minted at Start</param>
    /// <param name="error">the provider's refusal, when the user declined</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="302">Back to the settings page, carrying the state or an error</response>
    [HttpGet("OAuth/Callback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<ActionResult> OAuthCallback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BackToSettings(state: null);

        if (handshakes.Find(state) is not { } handshake) return BackToSettings(state: null);

        var domain = await domains.FindAsync(handshake.DomainId, cancellationToken);
        if (domain is null || !OAuthProviderConfig.TryFrom(domain, out var provider))
            return BackToSettings(state: null);

        var exchanged = await oauth.ExchangeCodeAsync(
            provider, code, handshake.CodeVerifier, RedirectUri, cancellationToken);
        if (exchanged.IsFailure) return BackToSettings(state: null);

        if (MailboxFrom(exchanged.Value.IdToken) is not { } email) return BackToSettings(state: null);

        // Audited like the password probe: this is the other way a mailbox becomes attached.
        logger.LogInformation(
            "Audit: oauth_callback domain={DomainName} target={Target} outcome=success",
            domain.Name, email);

        return handshakes.Attach(state, exchanged.Value, email)
            ? BackToSettings(state)
            : BackToSettings(state: null);
    }

    /// <summary>
    /// Finishes a consent. Same-site, so the credentials cookie travels and the refresh token can
    /// be encrypted under the session key — which is the whole reason this is a second call.
    /// </summary>
    /// <param name="request">the state the callback redirect carried</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The connected account</response>
    /// <response code="400">The handshake never completed, the mailbox is already connected, or it
    /// is not the mailbox this reconnection was started for</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such handshake</response>
    /// <response code="429">Too many authentication attempts</response>
    [HttpPost("OAuth/Complete")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ConnectedAccountResponse>> OAuthComplete(
        OAuthCompleteRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequestEnveloppe("Request body is required");

        var kek = await ResolveKekAsync(cancellationToken);
        if (kek.IsFailure) return UnauthorizedEnveloppe(kek.Error);

        if (handshakes.Consume(request.State ?? string.Empty, AuthenticatedUser.WebmailUid)
            is not { } handshake)
            return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);

        if (handshake.Tokens?.RefreshToken is not { Length: > 0 } refreshToken
            || handshake.Email is not { Length: > 0 } email)
            return BadRequestEnveloppe(ConnectedAccountErrors.HandshakeIncomplete);

        if (Encoding.UTF8.GetByteCount(refreshToken) > ConnectedAccountCipher.MaxSecretLength)
            return BadRequestEnveloppe("This provider's token is too large to store");

        var domain = await domains.FindAsync(handshake.DomainId, cancellationToken);
        if (domain is null) return BadRequestEnveloppe(ConnectedAccountErrors.UnknownDomain);

        return handshake.AccountId is { } accountId
            ? await ReconnectAsync(accountId, domain, email, refreshToken, kek.Value, cancellationToken)
            : await AttachAsync(domain, email, refreshToken, kek.Value, cancellationToken);
    }

    private async Task<ActionResult<ConnectedAccountResponse>> AttachAsync(
        ExternalDomain domain, string email, string refreshToken, byte[] kek,
        CancellationToken cancellationToken)
    {
        // The provider chose this address, not the caller, but the column bound still applies.
        if (email.Length > MaxEmailLength)
            return BadRequestEnveloppe(ConnectedAccountErrors.AddressTooLong);

        var row = new ConnectedAccount
        {
            Id = Guid.NewGuid(),
            UserId = AuthenticatedUser.WebmailUid,
            DomainId = domain.Id,
            Email = email,
            AuthMode = MailAuthMode.OAuth2
        };
        row.Cipher = ConnectedAccountCipher.Encrypt(
            kek, refreshToken, ConnectedAccountCipher.Context(row));

        var created = await accounts.CreateAsync(row, cancellationToken);
        return created.IsFailure
            ? BadRequestEnveloppe(created.Error)
            : Ok(Describe(created.Value, domain, string.Empty, credentialsValid: true));
    }

    /// <summary>
    /// The cipher context is bound to the address, so a token for another mailbox would encrypt
    /// under a context this row can never reproduce: it would open once and never again.
    /// </summary>
    private async Task<ActionResult<ConnectedAccountResponse>> ReconnectAsync(
        Guid accountId, ExternalDomain domain, string email, string refreshToken, byte[] kek,
        CancellationToken cancellationToken)
    {
        var row = await accounts.FindAsync(AuthenticatedUser.WebmailUid, accountId, cancellationToken);
        if (row is null) return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);

        if (!string.Equals(row.Email, email, StringComparison.Ordinal))
            return BadRequestEnveloppe(ConnectedAccountErrors.ReconnectMismatch);

        await accounts.UpdateCipherAsync(
            row,
            ConnectedAccountCipher.Encrypt(kek, refreshToken, ConnectedAccountCipher.Context(row)),
            cancellationToken);

        return Ok(Describe(row, domain, string.Empty, credentialsValid: true));
    }

    /// <summary>Registered with the provider byte for byte, which is why it is configured rather
    /// than rebuilt from the incoming request — and spelled once for the three actions.</summary>
    private string RedirectUri => options.CurrentValue.OAuthRedirectUri;

    /// <summary>
    /// access_type=offline is Google's refresh-token opt-in; Microsoft ignores it and grants one
    /// for offline_access in the scopes. prompt=consent Microsoft honours — every attach and
    /// Reconnect shows the full permissions dialog — and it is kept deliberately: it guarantees
    /// the fresh grant a reconnect exists to obtain, and one provider-neutral URL builder beats a
    /// per-provider switch. A test pins both parameters.
    /// </summary>
    private string AuthorizationUrl(OAuthProviderConfig provider, OAuthHandshake handshake) =>
        QueryHelpers.AddQueryString(provider.AuthorizationUrl, new Dictionary<string, string?>
        {
            ["client_id"] = provider.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = provider.Scopes,
            ["state"] = handshake.State,
            ["code_challenge"] = handshake.CodeChallenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        });

    /// <summary>The SPA screen that resumes the handshake, as src/frontend/src/routes.tsx
    /// registers it ('settings' → 'accounts'). The route table ends in a catch-all to /mail, so a
    /// drift here does not 404 — it silently drops the consent. A test pins the exact URL.</summary>
    private const string SettingsAccountsPath = "/settings/accounts";

    /// <summary>A null state is the generic failure: the page says the sign-in did not complete
    /// and offers to start again. Naming the cause would describe another user's session.</summary>
    private ActionResult BackToSettings(string? state)
    {
        var url = $"{options.CurrentValue.WebmailBaseUrl.TrimEnd('/')}{SettingsAccountsPath}";
        return Redirect(QueryHelpers.AddQueryString(url,
            state is null ? "oauthError" : "oauthState", state ?? "1"));
    }

    /// <summary>
    /// The mailbox the user actually signed in to, read from the id_token's email claim.
    /// The signature is not validated: the token came back over TLS on a direct call to the token
    /// endpoint, which OpenID Connect accepts as sufficient for a confidential client.
    /// </summary>
    private static string? MailboxFrom(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;

        try
        {
            var claims = new JsonWebTokenHandler().ReadJsonWebToken(idToken);
            var email = ClaimOrNull(claims, "email") ?? ClaimOrNull(claims, "preferred_username");
            return MailboxAddress.TryParse(RecipientAddressParser.Options, email ?? string.Empty, out var parsed)
                ? IdentityResolver.Canonical(parsed.Address)
                : null;
        }
        catch (Exception malformed) when (malformed is ArgumentException or SecurityTokenException)
        {
            return null;
        }
    }

    private static string? ClaimOrNull(JsonWebToken token, string name) =>
        token.TryGetClaim(name, out var claim) ? claim.Value : null;

    private static ConnectedAccountResponse Describe(
        ConnectedAccount row, ExternalDomain? domain, string displayName, bool credentialsValid) =>
        new(row.Id, row.Email, displayName, row.DomainId, domain?.Name,
            SieveSupported: row.DomainId is null || domain?.SieveHost is not null,
            credentialsValid, row.CreationDate, row.AuthMode);

    private static string DefaultLabel(IEnumerable<SendingIdentity> stored, string email) =>
        stored.FirstOrDefault(i => i.Address == email)?.DisplayName ?? string.Empty;

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
        if (string.IsNullOrEmpty(password)) return BadRequestEnveloppe(ConnectedAccountErrors.PasswordRequired);

        // The cipher column is finite and Encrypt throws past its bound: answer it here instead.
        return Encoding.UTF8.GetByteCount(password) > ConnectedAccountCipher.MaxSecretLength
            ? BadRequestEnveloppe(ConnectedAccountErrors.PasswordTooLong)
            : null;
    }

    /// <summary>Null when the domain row holds an unusable endpoint — logged, never described.</summary>
    private MailAccountConnection? BuildProbe(ExternalDomain? domain, string email, string password)
    {
        if (domain is null)
            return MailConnectionBuilder.Home(
                options.CurrentValue, ProbeAccountId, email, new PasswordCredential(password));

        // Same opt-in the resolver applies: a stricter probe would refuse to verify a password
        // against a domain the resolver would then happily open.
        if (MailConnectionBuilder.TryExternal(
                domain, ProbeAccountId, email, new PasswordCredential(password), out var connection,
                options.CurrentValue.AllowCleartext))
            return connection;

        logger.LogError(
            "External domain {DomainName} ({DomainId}) holds an unusable security value — " +
            "unknown, or None while Mail:AllowCleartext is off",
            domain.Name, domain.Id);
        return null;
    }

    /// <summary>
    /// Opens a real session and closes it at once: the point is to refuse a wrong password before
    /// it is stored, and nothing about the session is kept.
    ///
    /// Both outcomes are audited, and both name the actor as well as the address probed. This
    /// endpoint drives a real IMAP login against any address the caller supplies, so a log holding
    /// only the target cannot tell somebody attaching their own mailbox from somebody sweeping
    /// addresses that are not theirs — and a success is the half worth having, since that is the
    /// one that found a password.
    /// </summary>
    private async Task<Result> VerifyAsync(
        MailAccountConnection probe, string email, CancellationToken cancellationToken)
    {
        var session = await imap.OpenAsync(probe, cancellationToken);
        if (session.IsFailure)
        {
            // Neither the server's own text nor the credentials that produced it belong in a log.
            logger.LogWarning(
                "Audit: connect_account user={User} target={Target} outcome=failure reason=server_refused",
                AuthenticatedUser.Email, email);
            return Result.Failure(ServerRefused);
        }

        logger.LogInformation(
            "Audit: connect_account user={User} target={Target} outcome=success",
            AuthenticatedUser.Email, email);

        await session.Value.DisposeAsync();
        return Result.Success();
    }
}
