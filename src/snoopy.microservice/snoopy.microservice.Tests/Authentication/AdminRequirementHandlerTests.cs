using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Repositories;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class AdminRequirementHandlerTests
{
    private readonly Mock<IAdminRepository> _repo = new();

    private static ClaimsPrincipal CreatePrincipal(string? username = null, string? domain = null)
    {
        var claims = new List<Claim>();
        if (username != null) claims.Add(new Claim(ClaimTypes.Upn, username));
        if (domain != null) claims.Add(new Claim(ClaimTypes.Dns, domain));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private async Task<AuthorizationHandlerContext> EvaluateAsync(ClaimsPrincipal principal)
    {
        var requirement = new AdminRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);
        var handler = new AdminRequirementHandler(_repo.Object);
        await handler.HandleAsync(context);
        return context;
    }

    [Fact]
    public async Task WhenUserIsAdmin_Succeeds()
    {
        _repo.Setup(r => r.IsAdminAsync("john", "example.com", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var context = await EvaluateAsync(CreatePrincipal("john", "example.com"));
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task WhenUserIsNotAdmin_DoesNotSucceed()
    {
        _repo.Setup(r => r.IsAdminAsync("john", "example.com", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var context = await EvaluateAsync(CreatePrincipal("john", "example.com"));
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task WhenUpnClaimMissing_DoesNotSucceedAndSkipsRepository()
    {
        var context = await EvaluateAsync(CreatePrincipal(domain: "example.com"));
        Assert.False(context.HasSucceeded);
        _repo.Verify(r => r.IsAdminAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenDnsClaimMissing_DoesNotSucceedAndSkipsRepository()
    {
        var context = await EvaluateAsync(CreatePrincipal(username: "john"));
        Assert.False(context.HasSucceeded);
        _repo.Verify(r => r.IsAdminAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenNoClaims_DoesNotSucceed()
    {
        var context = await EvaluateAsync(new ClaimsPrincipal(new ClaimsIdentity()));
        Assert.False(context.HasSucceeded);
    }

    // The handler runs in the authorization pipeline, not in an action, so the only request-scoped
    // token it can reach is the one the middleware hands through as the resource.
    [Fact]
    public async Task PassesTheRequestsCancellationTokenToTheRepository()
    {
        using var cts = new CancellationTokenSource();
        var http = new DefaultHttpContext { RequestAborted = cts.Token };
        _repo.Setup(r => r.IsAdminAsync("john", "example.com", cts.Token)).ReturnsAsync(true);

        var context = new AuthorizationHandlerContext(
            [new AdminRequirement()], CreatePrincipal("john", "example.com"), http);
        await new AdminRequirementHandler(_repo.Object).HandleAsync(context);

        Assert.True(context.HasSucceeded);
        _repo.Verify(r => r.IsAdminAsync("john", "example.com", cts.Token), Times.Once);
    }

    // No request means no request to abandon; the policy must still be evaluable.
    [Fact]
    public async Task WithNoHttpContextResource_StillEvaluatesTheRequirement()
    {
        _repo.Setup(r => r.IsAdminAsync("john", "example.com", CancellationToken.None)).ReturnsAsync(true);

        var context = await EvaluateAsync(CreatePrincipal("john", "example.com"));

        Assert.True(context.HasSucceeded);
    }
}
