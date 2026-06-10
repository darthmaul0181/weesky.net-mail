using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Repositories;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication
{
    public class AdminRequirementHandlerTests
    {
        private readonly Mock<IAdminRepository> _repo = new();

        private static ClaimsPrincipal CreatePrincipal(string username = null, string domain = null)
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
            _repo.Setup(r => r.IsAdmin("john", "example.com")).Returns(true);
            var context = await EvaluateAsync(CreatePrincipal("john", "example.com"));
            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task WhenUserIsNotAdmin_DoesNotSucceed()
        {
            _repo.Setup(r => r.IsAdmin("john", "example.com")).Returns(false);
            var context = await EvaluateAsync(CreatePrincipal("john", "example.com"));
            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task WhenUpnClaimMissing_DoesNotSucceedAndSkipsRepository()
        {
            var context = await EvaluateAsync(CreatePrincipal(domain: "example.com"));
            Assert.False(context.HasSucceeded);
            _repo.Verify(r => r.IsAdmin(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task WhenDnsClaimMissing_DoesNotSucceedAndSkipsRepository()
        {
            var context = await EvaluateAsync(CreatePrincipal(username: "john"));
            Assert.False(context.HasSucceeded);
            _repo.Verify(r => r.IsAdmin(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task WhenNoClaims_DoesNotSucceed()
        {
            var context = await EvaluateAsync(new ClaimsPrincipal(new ClaimsIdentity()));
            Assert.False(context.HasSucceeded);
        }
    }
}
