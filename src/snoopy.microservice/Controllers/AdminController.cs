using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Policy = AdminRequirement.PolicyName)]
public sealed class AdminController : ApiBaseController
{
    /// <summary>Fixed rather than a live count: the store's <c>domain_in_use</c> check does not
    /// carry one, and adding a method just for it is not worth it for an admin-only error.</summary>
    private const string AccountsStillConnected = "Accounts are still connected to this domain";

    private static readonly HashSet<string> EncryptedSecurities = ["StartTls", "SslOnConnect"];

    /// <summary>Bounds the protected blob under oauth_client_secret's VARBINARY(1024): 512 bytes
    /// of plaintext stay well inside it once Data Protection adds its framing.</summary>
    private const int MaxClientSecretBytes = 512;

    private readonly IAdminRepository _adminRepository;
    private readonly IDovecotQuotaClient _dovecotQuotaClient;
    private readonly IExternalDomainStore _externalDomains;
    private readonly IClientSecretProtector _secretProtector;
    private readonly IOptionsMonitor<MailOptions> _mailOptions;

    public AdminController(
        IAdminRepository adminRepository,
        IDovecotQuotaClient dovecotQuotaClient,
        IExternalDomainStore externalDomains,
        IClientSecretProtector secretProtector,
        IOptionsMonitor<MailOptions> mailOptions)
    {
        _adminRepository = adminRepository;
        _dovecotQuotaClient = dovecotQuotaClient;
        _externalDomains = externalDomains;
        _secretProtector = secretProtector;
        _mailOptions = mailOptions;
    }

    /// <summary>Returns all users</summary>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpGet("users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<AdminUserInfo>>> GetUsers(CancellationToken cancellationToken)
    {
        return Ok(await _adminRepository.GetAllUsersAsync(cancellationToken));
    }

    /// <summary>Creates a new user</summary>
    /// <response code="201">User created</response>
    /// <response code="400">Validation error</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpPost("users")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminUserInfo>> CreateUser(AdminUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Password))
            return BadRequestEnveloppe("Password is required");
        Result<AdminUserInfo> result = await _adminRepository.CreateUserAsync(request, cancellationToken);
        if (result.IsFailure) return BadRequestEnveloppe(result.Error);
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Updates an existing user</summary>
    /// <response code="200">User updated</response>
    /// <response code="400">Validation error</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpPut("users/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminUserInfo>> UpdateUser(int id, AdminUserRequest request, CancellationToken cancellationToken)
    {
        Result<AdminUserInfo> result = await _adminRepository.UpdateUserAsync(id, request, cancellationToken);
        if (result.IsFailure) return BadRequestEnveloppe(result.Error);
        return Ok(result.Value);
    }

    /// <summary>Deletes a user</summary>
    /// <response code="204">User deleted</response>
    /// <response code="400">User not found</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpDelete("users/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteUser(int id, CancellationToken cancellationToken)
    {
        Result result = await _adminRepository.DeleteUserAsync(id, cancellationToken);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>Returns all domains</summary>
    /// <response code="200">Domain list</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpGet("domains")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<Domain>>> GetDomains(CancellationToken cancellationToken)
    {
        return Ok(await _adminRepository.GetAllDomainsAsync(cancellationToken));
    }

    /// <summary>Creates a new domain</summary>
    /// <response code="201">Domain created</response>
    /// <response code="400">Validation error</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpPost("domains")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Domain>> CreateDomain(AdminDomainRequest request, CancellationToken cancellationToken)
    {
        Result<Domain> result = await _adminRepository.CreateDomainAsync(request, cancellationToken);
        if (result.IsFailure) return BadRequestEnveloppe(result.Error);
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Updates an existing domain</summary>
    /// <response code="200">Domain updated</response>
    /// <response code="400">Validation error</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpPut("domains/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Domain>> UpdateDomain(string id, AdminDomainRequest request, CancellationToken cancellationToken)
    {
        Result<Domain> result = await _adminRepository.UpdateDomainAsync(id, request, cancellationToken);
        if (result.IsFailure) return BadRequestEnveloppe(result.Error);
        return Ok(result.Value);
    }

    /// <summary>Returns the Dovecot mailbox quota for a specific user</summary>
    /// <response code="200">Quota information</response>
    /// <response code="400">User not found</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    /// <response code="502">Unable to reach Dovecot</response>
    [HttpGet("users/{id}/quota")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<Quota>> GetUserQuota(int id, CancellationToken cancellationToken)
    {
        var userInfo = await _adminRepository.GetUserByIdAsync(id, cancellationToken);
        if (userInfo == null) return BadRequestEnveloppe($"User {id} not found");

        var user = new User($"{userInfo.UserName}@{userInfo.DomainName}");
        Result<Quota> result = await _dovecotQuotaClient.GetQuotaAsync(user, cancellationToken);
        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>Deletes a domain</summary>
    /// <param name="id">the domain to delete</param>
    /// <param name="deleteAliases">
    /// Acknowledges that every alias anchored on the domain is deleted with it. Omitted, the
    /// request is refused and the refusal names how many there are.
    /// </param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Domain deleted</response>
    /// <response code="400">Domain not found, still has users, or holds unacknowledged aliases</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpDelete("domains/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteDomain(
        string id, [FromQuery] bool deleteAliases, CancellationToken cancellationToken)
    {
        Result result = await _adminRepository.DeleteDomainAsync(id, deleteAliases, cancellationToken);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>Returns all virtual (alias) domains with their owners</summary>
    /// <response code="200">Virtual domain list</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpGet("domains/virtuals")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<VirtualDomainInfo>>> GetVirtualDomains(CancellationToken cancellationToken)
    {
        return Ok(await _adminRepository.GetAllVirtualDomainsAsync(cancellationToken));
    }

    /// <summary>Adds an owner to a virtual domain</summary>
    /// <response code="200">Owner added</response>
    /// <response code="400">Validation error</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpPut("domains/virtuals/{domainId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VirtualDomainInfo>> AddVirtualDomainOwner(string domainId, AdminVirtualDomainOwnerRequest request, CancellationToken cancellationToken)
    {
        Result<VirtualDomainInfo> result = await _adminRepository.AddVirtualDomainOwnerAsync(domainId, request.UserId, cancellationToken);
        if (result.IsFailure) return BadRequestEnveloppe(result.Error);
        return Ok(result.Value);
    }

    /// <summary>Removes a specific owner from a virtual domain</summary>
    /// <response code="204">Owner removed</response>
    /// <response code="400">Owner not found</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpDelete("domains/virtuals/{domainId}/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> RemoveVirtualDomainOwner(string domainId, int userId, CancellationToken cancellationToken)
    {
        Result result = await _adminRepository.RemoveVirtualDomainOwnerAsync(domainId, userId, cancellationToken);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>Returns every admin-curated external mail provider a user may connect from</summary>
    /// <response code="200">Domain list</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpGet("domains/external")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<ExternalDomainResponse>>> GetExternalDomains(
        CancellationToken cancellationToken)
    {
        var rows = await _externalDomains.ListAsync(cancellationToken);
        return Ok(rows.Select(Describe).ToList());
    }

    /// <summary>Registers a new external mail provider</summary>
    /// <response code="200">Domain created</response>
    /// <response code="400">Validation error, or the name is already taken</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpPost("domains/external")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ExternalDomainResponse>> CreateExternalDomain(
        ExternalDomainRequest request, CancellationToken cancellationToken)
    {
        var validated = Validate(request, _mailOptions.CurrentValue.AllowCleartext, requireSecret: true);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var created = await _externalDomains.CreateAsync(
            ToEntity(Guid.Empty, request, ProtectedSecret(request, existing: null)), cancellationToken);
        if (created.IsFailure) return BadRequestEnveloppe(created.Error);
        return Ok(Describe(created.Value));
    }

    /// <summary>Updates an existing external mail provider, rewriting every field</summary>
    /// <response code="204">Domain updated</response>
    /// <response code="400">Validation error, or the name is already taken</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    /// <response code="404">No such domain</response>
    [HttpPut("domains/external/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateExternalDomain(
        Guid id, ExternalDomainRequest request, CancellationToken cancellationToken)
    {
        // Read first: an empty secret on an edit means "keep the stored one" — the secret is
        // write-only, so the edit screen has nothing to send back — and validation must still
        // refuse an OAuth2 row that would end up with no secret at all.
        var existing = await _externalDomains.FindAsync(id, cancellationToken);
        if (existing is null) return NotFoundEnveloppe(ExternalDomainStore.NotFound);

        var validated = Validate(
            request, _mailOptions.CurrentValue.AllowCleartext,
            requireSecret: existing.OAuthClientSecret is not { Length: > 0 });
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var result = await _externalDomains.UpdateAsync(
            ToEntity(id, request, ProtectedSecret(request, existing.OAuthClientSecret)), cancellationToken);
        if (result.IsFailure)
            return result.Error == ExternalDomainStore.NotFound
                ? NotFoundEnveloppe(result.Error)
                : BadRequestEnveloppe(result.Error);
        return NoContent();
    }

    /// <summary>Removes an external mail provider</summary>
    /// <response code="204">Domain deleted</response>
    /// <response code="400">Accounts are still connected to this domain</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpDelete("domains/external/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteExternalDomain(Guid id, CancellationToken cancellationToken)
    {
        var result = await _externalDomains.DeleteAsync(id, cancellationToken);
        if (result.IsFailure)
            return BadRequestEnveloppe(result.Error == ExternalDomainStore.InUse ? AccountsStillConnected : result.Error);
        return NoContent();
    }

    private static ExternalDomainResponse Describe(ExternalDomain domain) => new(
        domain.Id, domain.Name, domain.ImapHost, domain.ImapPort, domain.ImapSecurity,
        domain.SmtpHost, domain.SmtpPort, domain.SmtpSecurity, domain.SieveHost, domain.SievePort,
        domain.AuthMode, domain.OAuthAuthorizationUrl, domain.OAuthTokenUrl,
        domain.OAuthScopes, domain.OAuthClientId,
        OAuthClientSecretSet: domain.OAuthClientSecret is { Length: > 0 });

    /// <summary>A Password row carries no OAuth column at all, so a later flip back to OAuth2
    /// starts clean rather than resurrecting whatever an earlier configuration held.</summary>
    private static ExternalDomain ToEntity(Guid id, ExternalDomainRequest request, byte[]? protectedSecret)
    {
        var oauth = ParsedAuthMode(request) is MailAuthMode.OAuth2;
        return new()
        {
            Id = id,
            Name = request.Name,
            ImapHost = request.ImapHost,
            ImapPort = request.ImapPort,
            ImapSecurity = request.ImapSecurity,
            SmtpHost = request.SmtpHost,
            SmtpPort = request.SmtpPort,
            SmtpSecurity = request.SmtpSecurity,
            SieveHost = request.SieveHost,
            SievePort = request.SievePort,
            AuthMode = oauth ? MailAuthMode.OAuth2 : MailAuthMode.Password,
            OAuthAuthorizationUrl = oauth ? request.OAuthAuthorizationUrl!.Trim() : null,
            OAuthTokenUrl = oauth ? request.OAuthTokenUrl!.Trim() : null,
            OAuthScopes = oauth ? request.OAuthScopes!.Trim() : null,
            OAuthClientId = oauth ? request.OAuthClientId!.Trim() : null,
            OAuthClientSecret = oauth ? protectedSecret : null
        };
    }

    /// <summary>Null for a Password domain, the stored bytes when the edit left the field empty,
    /// the freshly protected plaintext otherwise.</summary>
    private byte[]? ProtectedSecret(ExternalDomainRequest request, byte[]? existing) =>
        ParsedAuthMode(request) is not MailAuthMode.OAuth2 ? null
        : string.IsNullOrEmpty(request.OAuthClientSecret) ? existing
        : _secretProtector.Protect(request.OAuthClientSecret);

    /// <summary>Exact-literal rule, like the securities; null when unrecognised, and a null
    /// request value means Password so pre-OAuth callers keep their exact meaning.</summary>
    private static MailAuthMode? ParsedAuthMode(ExternalDomainRequest request) => request.AuthMode switch
    {
        null or "Password" => MailAuthMode.Password,
        "OAuth2" => MailAuthMode.OAuth2,
        _ => null
    };

    /// <summary>
    /// Securities are checked by exact, case-sensitive string match against the three literals —
    /// not <c>Enum.TryParse</c>, which also accepts a numeric string and would let a value the
    /// admin never typed reach the resolver that reads this row back. The cleartext opt-in is the
    /// same one the resolver applies, so a row that saves here is a row that resolves there.
    /// </summary>
    private static Result Validate(ExternalDomainRequest request, bool allowCleartext, bool requireSecret)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return Result.Failure("Name must be between 1 and 100 characters");

        if (ValidateHost(request.ImapHost) is { } imapHostError) return Result.Failure(imapHostError);
        if (ValidateHost(request.SmtpHost) is { } smtpHostError) return Result.Failure(smtpHostError);

        if (request.ImapPort is < 1 or > 65535) return Result.Failure("Imap port must be between 1 and 65535");
        if (request.SmtpPort is < 1 or > 65535) return Result.Failure("Smtp port must be between 1 and 65535");

        if (ValidateSecurity(request.ImapSecurity, allowCleartext) is { } imapSecurityError)
            return Result.Failure($"Imap {imapSecurityError}");
        if (ValidateSecurity(request.SmtpSecurity, allowCleartext) is { } smtpSecurityError)
            return Result.Failure($"Smtp {smtpSecurityError}");

        if (request.SieveHost is null != request.SievePort is null)
            return Result.Failure("Sieve host and port must both be present or both be absent");
        if (request.SieveHost is not null)
        {
            if (ValidateHost(request.SieveHost) is { } sieveHostError) return Result.Failure(sieveHostError);
            if (request.SievePort is < 1 or > 65535) return Result.Failure("Sieve port must be between 1 and 65535");
        }

        return ValidateOAuth(request, requireSecret);
    }

    /// <summary>
    /// Mirrors <see cref="OAuthProviderConfig.TryFrom"/> field for field, plus the column widths:
    /// an OAuth2 row that saves here is one the consent flow will accept, so an operator cannot
    /// store a half-configured provider and discover it at consent time.
    /// </summary>
    private static Result ValidateOAuth(ExternalDomainRequest request, bool requireSecret)
    {
        var mode = ParsedAuthMode(request);
        if (mode is null)
            return Result.Failure("Auth mode must be exactly one of Password, OAuth2");
        if (mode is MailAuthMode.Password) return Result.Success();

        if (!OAuthProviderConfig.IsHttps(request.OAuthAuthorizationUrl) || request.OAuthAuthorizationUrl!.Length > 512)
            return Result.Failure("Authorization URL must be an absolute https URL of at most 512 characters");
        if (!OAuthProviderConfig.IsHttps(request.OAuthTokenUrl) || request.OAuthTokenUrl!.Length > 512)
            return Result.Failure("Token URL must be an absolute https URL of at most 512 characters");
        if (string.IsNullOrWhiteSpace(request.OAuthScopes) || request.OAuthScopes.Length > 1024)
            return Result.Failure("Scopes must be between 1 and 1024 characters");
        if (string.IsNullOrWhiteSpace(request.OAuthClientId) || request.OAuthClientId.Length > 255)
            return Result.Failure("Client id must be between 1 and 255 characters");

        if (requireSecret && string.IsNullOrEmpty(request.OAuthClientSecret))
            return Result.Failure("A client secret is required for an OAuth2 domain");
        if (request.OAuthClientSecret is { } secret
            && Encoding.UTF8.GetByteCount(secret) > MaxClientSecretBytes)
            return Result.Failure($"The client secret may not exceed {MaxClientSecretBytes} bytes");
        return Result.Success();
    }

    private static string? ValidateHost(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 255) return "Host must be between 1 and 255 characters";
        return Uri.CheckHostName(host) == UriHostNameType.Unknown ? "Host is not a valid hostname or IP address" : null;
    }

    /// <summary>Refusing None here rather than at read time is what stops an admin from saving a
    /// row that would answer 404 on every use, with nothing on screen saying why.</summary>
    private static string? ValidateSecurity(string security, bool allowCleartext) => security switch
    {
        _ when EncryptedSecurities.Contains(security) => null,
        "None" when allowCleartext => null,
        "None" => "security cannot be None: set Mail:AllowCleartext to accept an unencrypted endpoint",
        _ => "security must be exactly one of None, StartTls, SslOnConnect"
    };
}
