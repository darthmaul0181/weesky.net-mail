using Microsoft.AspNetCore.Authorization;

namespace weesky.Snoopy.Microservice.Authentication.Authorization;

/// <summary>
/// Authorization requirement satisfied when the authenticated user has admin='Y'
/// in the mail database. Evaluated by <see cref="AdminRequirementHandler"/>.
/// </summary>
public sealed class AdminRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "Admin";
}
