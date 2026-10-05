using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using weesky.Scotty.Microservice.Authentication.Authorization;
using weesky.Scotty.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Platform;

public sealed class GenericAdminRequirementHandlerTests
{
    private static async Task<bool> SucceedsAsync(params Claim[] claims)
    {
        var requirement = new AdminRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), resource: null);

        await new GenericAdminRequirementHandler(new GenericAdministrators(["michael@exemple.be"]))
            .HandleAsync(context);
        return context.HasSucceeded;
    }

    private static Claim[] Login(string name, string domain) =>
        [new(ClaimTypes.Upn, name), new(ClaimTypes.Dns, domain)];

    [Fact]
    public async Task AListedAddress_IsGranted() => Assert.True(await SucceedsAsync(Login("michael", "exemple.be")));

    [Fact]
    public async Task AListedAddress_InAnotherCase_IsGranted() =>
        Assert.True(await SucceedsAsync(Login("Michael", "EXEMPLE.be")));

    [Fact]
    public async Task AnUnlistedAddress_IsRefused() => Assert.False(await SucceedsAsync(Login("anne", "exemple.be")));

    [Fact]
    public async Task APrincipalWithoutLoginClaims_IsRefused() => Assert.False(await SucceedsAsync());
}
