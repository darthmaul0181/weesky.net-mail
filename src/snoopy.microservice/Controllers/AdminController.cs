using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AdminController : ApiBaseController
    {
        private readonly IAdminRepository _adminRepository;

        public AdminController(IAdminRepository adminRepository)
        {
            _adminRepository = adminRepository;
        }

        private bool IsCurrentUserAdmin() =>
            _adminRepository.IsAdmin(AuthenticatedUser?.Name, AuthenticatedUser?.Domain);

        /// <summary>Returns all users</summary>
        /// <response code="200">User list</response>
        /// <response code="401">Unauthenticated or not an admin</response>
        [HttpGet("users")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult<IEnumerable<AdminUserInfo>> GetUsers()
        {
            if (!IsCurrentUserAdmin()) return Unauthorized();
            return Ok(_adminRepository.GetAllUsers());
        }

        /// <summary>Creates a new user</summary>
        /// <response code="201">User created</response>
        /// <response code="400">Validation error</response>
        /// <response code="401">Unauthenticated or not an admin</response>
        [HttpPost("users")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult<AdminUserInfo> CreateUser(AdminUserRequest request)
        {
            if (!IsCurrentUserAdmin()) return Unauthorized();
            Result<AdminUserInfo> result = _adminRepository.CreateUser(request);
            if (result.IsFailure) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
            return StatusCode(StatusCodes.Status201Created, result.Value);
        }

        /// <summary>Updates an existing user</summary>
        /// <response code="200">User updated</response>
        /// <response code="400">Validation error</response>
        /// <response code="401">Unauthenticated or not an admin</response>
        [HttpPut("users/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult<AdminUserInfo> UpdateUser(int id, AdminUserRequest request)
        {
            if (!IsCurrentUserAdmin()) return Unauthorized();
            Result<AdminUserInfo> result = _adminRepository.UpdateUser(id, request);
            if (result.IsFailure) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
            return Ok(result.Value);
        }

        /// <summary>Deletes a user</summary>
        /// <response code="204">User deleted</response>
        /// <response code="400">User not found</response>
        /// <response code="401">Unauthenticated or not an admin</response>
        [HttpDelete("users/{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult DeleteUser(int id)
        {
            if (!IsCurrentUserAdmin()) return Unauthorized();
            Result result = _adminRepository.DeleteUser(id);
            return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
        }

        /// <summary>Returns all domains</summary>
        /// <response code="200">Domain list</response>
        /// <response code="401">Unauthenticated or not an admin</response>
        [HttpGet("domains")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult<IEnumerable<Domain>> GetDomains()
        {
            if (!IsCurrentUserAdmin()) return Unauthorized();
            return Ok(_adminRepository.GetAllDomains());
        }

        /// <summary>Creates a new domain</summary>
        /// <response code="201">Domain created</response>
        /// <response code="400">Validation error</response>
        /// <response code="401">Unauthenticated or not an admin</response>
        [HttpPost("domains")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult<Domain> CreateDomain(AdminDomainRequest request)
        {
            if (!IsCurrentUserAdmin()) return Unauthorized();
            Result<Domain> result = _adminRepository.CreateDomain(request);
            if (result.IsFailure) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
            return StatusCode(StatusCodes.Status201Created, result.Value);
        }

        /// <summary>Updates an existing domain</summary>
        /// <response code="200">Domain updated</response>
        /// <response code="400">Validation error</response>
        /// <response code="401">Unauthenticated or not an admin</response>
        [HttpPut("domains/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult<Domain> UpdateDomain(string id, AdminDomainRequest request)
        {
            if (!IsCurrentUserAdmin()) return Unauthorized();
            Result<Domain> result = _adminRepository.UpdateDomain(id, request);
            if (result.IsFailure) return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
            return Ok(result.Value);
        }

        /// <summary>Deletes a domain</summary>
        /// <response code="204">Domain deleted</response>
        /// <response code="400">Domain not found or still has users</response>
        /// <response code="401">Unauthenticated or not an admin</response>
        [HttpDelete("domains/{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult DeleteDomain(string id)
        {
            if (!IsCurrentUserAdmin()) return Unauthorized();
            Result result = _adminRepository.DeleteDomain(id);
            return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
        }
    }
}
