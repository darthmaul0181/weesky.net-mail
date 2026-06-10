using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Authentication.Authorization
{
    public class AdminRequirementHandler : AuthorizationHandler<AdminRequirement>
    {
        private readonly IAdminRepository _adminRepository;

        public AdminRequirementHandler(IAdminRepository adminRepository)
        {
            _adminRepository = adminRepository;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
        {
            var name = context.User.FindFirst(ClaimTypes.Upn)?.Value;
            var domain = context.User.FindFirst(ClaimTypes.Dns)?.Value;

            if (!string.IsNullOrWhiteSpace(name) &&
                !string.IsNullOrWhiteSpace(domain) &&
                _adminRepository.IsAdmin(name, domain))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
