using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    public class AdminController : ApiBaseController
    {
        private readonly IAdminRepository _adminRepository;
        private readonly IDovecotQuotaClient _dovecotQuotaClient;

        public AdminController(IAdminRepository adminRepository, IDovecotQuotaClient dovecotQuotaClient)
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
        public ActionResult<IEnumerable<AdminUserInfo>> GetUsers()
        {
            return Ok(_adminRepository.GetAllUsers());
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
        public ActionResult<AdminUserInfo> CreateUser(AdminUserRequest request)
        {
            if (string.IsNullOrEmpty(request.Password))
                return BadRequest(ResultEnveloppe.CrateErrorEnveloppe("Password is required"));
            Result<AdminUserInfo> result = _adminRepository.CreateUser(request);
            if (result.IsFailure) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
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
        public ActionResult<AdminUserInfo> UpdateUser(int id, AdminUserRequest request)
        {
            Result<AdminUserInfo> result = _adminRepository.UpdateUser(id, request);
            if (result.IsFailure) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
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
        public ActionResult DeleteUser(int id)
        {
            Result result = _adminRepository.DeleteUser(id);
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
        public ActionResult<IEnumerable<Domain>> GetDomains()
        {
            return Ok(_adminRepository.GetAllDomains());
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
        public ActionResult<Domain> CreateDomain(AdminDomainRequest request)
        {
            Result<Domain> result = _adminRepository.CreateDomain(request);
            if (result.IsFailure) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
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
        public ActionResult<Domain> UpdateDomain(string id, AdminDomainRequest request)
        {
            Result<Domain> result = _adminRepository.UpdateDomain(id, request);
            if (result.IsFailure) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
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
            var users = _adminRepository.GetAllUsers();
            var userInfo = users.FirstOrDefault(u => u.Id == id);
            if (userInfo == null) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe($"User {id} not found"));

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
        public ActionResult DeleteDomain(string id)
        {
            Result result = _adminRepository.DeleteDomain(id);
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
        public ActionResult<IEnumerable<VirtualDomainInfo>> GetVirtualDomains()
        {
            return Ok(_adminRepository.GetAllVirtualDomains());
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
        public ActionResult<VirtualDomainInfo> AddVirtualDomainOwner(string domainId, AdminVirtualDomainOwnerRequest request)
        {
            Result<VirtualDomainInfo> result = _adminRepository.AddVirtualDomainOwner(domainId, request.UserId);
            if (result.IsFailure) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
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
        public ActionResult RemoveVirtualDomainOwner(string domainId, int userId)
        {
            Result result = _adminRepository.RemoveVirtualDomainOwner(domainId, userId);
            return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
        }
    }
}
