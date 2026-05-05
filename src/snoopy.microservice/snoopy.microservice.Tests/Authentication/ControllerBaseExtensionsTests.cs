using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using weesky.Snoopy.Microservice.Authentication.Extensions;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication
{
    public class ControllerBaseExtensionsTests
    {
        private sealed class FakeController : ControllerBase { }

        private static FakeController CreateController(IEnumerable<Claim>? claims = null)
        {
            var controller = new FakeController();
            var httpContext = new DefaultHttpContext();
            if (claims != null)
            {
                var identity = new ClaimsIdentity(claims, "Test");
                httpContext.User = new ClaimsPrincipal(identity);
            }
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public void GetUser_WithBothClaims_ReturnsUser()
        {
            var controller = CreateController(new[]
            {
                new Claim(ClaimTypes.Upn, "john"),
                new Claim(ClaimTypes.Dns, "example.com")
            });

            var user = controller.GetUser();

            Assert.NotNull(user);
            Assert.Equal("john", user.Name);
            Assert.Equal("example.com", user.Domain);
        }

        [Fact]
        public void GetUser_WithNoClaims_ReturnsNull()
        {
            var controller = CreateController();

            var user = controller.GetUser();

            Assert.Null(user);
        }

        [Fact]
        public void GetUser_MissingUpnClaim_ReturnsNull()
        {
            var controller = CreateController(new[] { new Claim(ClaimTypes.Dns, "example.com") });

            var user = controller.GetUser();

            Assert.Null(user);
        }

        [Fact]
        public void GetUser_MissingDnsClaim_ReturnsNull()
        {
            var controller = CreateController(new[] { new Claim(ClaimTypes.Upn, "john") });

            var user = controller.GetUser();

            Assert.Null(user);
        }

        [Fact]
        public void GetUser_UpnAndDnsPresentWithWhitespace_ReturnsNull()
        {
            var controller = CreateController(new[]
            {
                new Claim(ClaimTypes.Upn, "   "),
                new Claim(ClaimTypes.Dns, "example.com")
            });

            var user = controller.GetUser();

            Assert.Null(user);
        }
    }
}
