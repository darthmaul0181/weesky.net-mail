using Microsoft.AspNetCore.Authorization;

namespace weesky.Scotty.Microservice.Authentication.Authorization;

/// <summary>
/// Authorization requirement satisfied when the authenticated user is an administrator
/// as its platform defines it. Each platform registers the handler that can satisfy it:
/// <c>AdminRequirementHandler</c> reads the weesky directory, <c>GenericAdminRequirementHandler</c>
/// the configured list.
/// </summary>
public sealed class AdminRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "Admin";
}
