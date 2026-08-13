using Microsoft.AspNetCore.Authorization;

namespace weesky.Snoopy.Microservice.Authentication.Authorization;

/// <summary>
/// Authorization requirement satisfied when the authenticated user has admin='Y'
/// in the mail database. The handler that can satisfy it belongs to the weesky platform
/// (<c>AdminRequirementHandler</c>); on a platform holding no admin directory, no handler is
/// registered and the policy is unsatisfiable — which is what a deployment serving none of its
/// routes should answer.
/// </summary>
public sealed class AdminRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "Admin";
}
