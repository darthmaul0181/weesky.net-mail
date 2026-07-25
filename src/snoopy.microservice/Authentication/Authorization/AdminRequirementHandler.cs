using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Authentication.Authorization;

public sealed class AdminRequirementHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly IAdminRepository _adminRepository;

    public AdminRequirementHandler(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        var name = context.User.FindFirst(ClaimTypes.Upn)?.Value;
        var domain = context.User.FindFirst(ClaimTypes.Dns)?.Value;

        if (!string.IsNullOrWhiteSpace(name) &&
            !string.IsNullOrWhiteSpace(domain) &&
            await _adminRepository.IsAdminAsync(name, domain))
        {
            context.Succeed(requirement);
        }
    }
}
