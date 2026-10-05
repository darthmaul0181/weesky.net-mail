using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Authentication.Authorization;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Providers.Weesky.Repositories;
using weesky.Scotty.Providers.Weesky.Services;

namespace weesky.Scotty.Providers.Weesky.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Policy = AdminRequirement.PolicyName)]
public sealed class AdminController : ApiBaseController
{
    private readonly IAdminRepository _adminRepository;
    private readonly IDovecotQuotaClient _dovecotQuotaClient;

    public AdminController(
        IAdminRepository adminRepository,
        IDovecotQuotaClient dovecotQuotaClient)
    {
        _adminRepository = adminRepository;
        _dovecotQuotaClient = dovecotQuotaClient;
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
}
