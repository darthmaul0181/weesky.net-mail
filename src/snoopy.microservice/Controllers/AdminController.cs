using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
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

    private static readonly HashSet<string> AllowedSecurities = ["None", "StartTls", "SslOnConnect"];

    private readonly IAdminRepository _adminRepository;
    private readonly IDovecotQuotaClient _dovecotQuotaClient;
    private readonly IExternalDomainStore _externalDomains;

    public AdminController(
        IAdminRepository adminRepository,
        IDovecotQuotaClient dovecotQuotaClient,
        IExternalDomainStore externalDomains)
    {
        _adminRepository = adminRepository;
        _dovecotQuotaClient = dovecotQuotaClient;
        _externalDomains = externalDomains;
    }

    /// <summary>Returns all users</summary>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpGet("users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<AdminUserInfo>>> GetUsers()
    {
        return Ok(await _adminRepository.GetAllUsersAsync());
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
    public async Task<ActionResult<AdminUserInfo>> CreateUser(AdminUserRequest request)
    {
        if (string.IsNullOrEmpty(request.Password))
            return BadRequestEnveloppe("Password is required");
        Result<AdminUserInfo> result = await _adminRepository.CreateUserAsync(request);
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
    public async Task<ActionResult<AdminUserInfo>> UpdateUser(int id, AdminUserRequest request)
    {
        Result<AdminUserInfo> result = await _adminRepository.UpdateUserAsync(id, request);
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
    public async Task<ActionResult> DeleteUser(int id)
    {
        Result result = await _adminRepository.DeleteUserAsync(id);
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
    public async Task<ActionResult<IEnumerable<Domain>>> GetDomains()
    {
        return Ok(await _adminRepository.GetAllDomainsAsync());
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
    public async Task<ActionResult<Domain>> CreateDomain(AdminDomainRequest request)
    {
        Result<Domain> result = await _adminRepository.CreateDomainAsync(request);
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
    public async Task<ActionResult<Domain>> UpdateDomain(string id, AdminDomainRequest request)
    {
        Result<Domain> result = await _adminRepository.UpdateDomainAsync(id, request);
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
        var userInfo = await _adminRepository.GetUserByIdAsync(id);
        if (userInfo == null) return BadRequestEnveloppe($"User {id} not found");

        var user = new User($"{userInfo.UserName}@{userInfo.DomainName}");
        Result<Quota> result = await _dovecotQuotaClient.GetQuotaAsync(user, cancellationToken);
        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>Deletes a domain</summary>
    /// <response code="204">Domain deleted</response>
    /// <response code="400">Domain not found or still has users</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpDelete("domains/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteDomain(string id)
    {
        Result result = await _adminRepository.DeleteDomainAsync(id);
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
    public async Task<ActionResult<IEnumerable<VirtualDomainInfo>>> GetVirtualDomains()
    {
        return Ok(await _adminRepository.GetAllVirtualDomainsAsync());
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
    public async Task<ActionResult<VirtualDomainInfo>> AddVirtualDomainOwner(string domainId, AdminVirtualDomainOwnerRequest request)
    {
        Result<VirtualDomainInfo> result = await _adminRepository.AddVirtualDomainOwnerAsync(domainId, request.UserId);
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
    public async Task<ActionResult> RemoveVirtualDomainOwner(string domainId, int userId)
    {
        Result result = await _adminRepository.RemoveVirtualDomainOwnerAsync(domainId, userId);
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
        var validated = Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var created = await _externalDomains.CreateAsync(ToEntity(Guid.Empty, request), cancellationToken);
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
        var validated = Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var result = await _externalDomains.UpdateAsync(ToEntity(id, request), cancellationToken);
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
        domain.SmtpHost, domain.SmtpPort, domain.SmtpSecurity, domain.SieveHost, domain.SievePort);

    private static ExternalDomain ToEntity(Guid id, ExternalDomainRequest request) => new()
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
        SievePort = request.SievePort
    };

    /// <summary>
    /// Securities are checked by exact, case-sensitive string match against the three literals —
    /// not <c>Enum.TryParse</c>, which also accepts a numeric string and would let a value the
    /// admin never typed reach the resolver that reads this row back.
    /// </summary>
    private static Result Validate(ExternalDomainRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return Result.Failure("Name must be between 1 and 100 characters");

        if (ValidateHost(request.ImapHost) is { } imapHostError) return Result.Failure(imapHostError);
        if (ValidateHost(request.SmtpHost) is { } smtpHostError) return Result.Failure(smtpHostError);

        if (request.ImapPort is < 1 or > 65535) return Result.Failure("Imap port must be between 1 and 65535");
        if (request.SmtpPort is < 1 or > 65535) return Result.Failure("Smtp port must be between 1 and 65535");

        if (!AllowedSecurities.Contains(request.ImapSecurity))
            return Result.Failure("Imap security must be exactly one of None, StartTls, SslOnConnect");
        if (!AllowedSecurities.Contains(request.SmtpSecurity))
            return Result.Failure("Smtp security must be exactly one of None, StartTls, SslOnConnect");

        if (request.SieveHost is null != request.SievePort is null)
            return Result.Failure("Sieve host and port must both be present or both be absent");
        if (request.SieveHost is not null)
        {
            if (ValidateHost(request.SieveHost) is { } sieveHostError) return Result.Failure(sieveHostError);
            if (request.SievePort is < 1 or > 65535) return Result.Failure("Sieve port must be between 1 and 65535");
        }

        return Result.Success();
    }

    private static string? ValidateHost(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 255) return "Host must be between 1 and 255 characters";
        return Uri.CheckHostName(host) == UriHostNameType.Unknown ? "Host is not a valid hostname or IP address" : null;
    }
}
