using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class VersionControllerTests
{
    [Fact]
    public void Get_AnswersTheRunningVersion()
    {
        var result = new VersionController().GetVersion();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(ProductVersion.Current, ok.Value);
    }

    // An exact version tells an attacker which known flaws apply; only a signed-in user may read it.
    [Fact]
    public void Get_RequiresAnAuthenticatedUser()
    {
        Assert.NotEmpty(typeof(VersionController).GetCustomAttributes(typeof(AuthorizeAttribute), false));
        var method = typeof(VersionController).GetMethod(nameof(VersionController.GetVersion))!;
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), false));
    }
}
