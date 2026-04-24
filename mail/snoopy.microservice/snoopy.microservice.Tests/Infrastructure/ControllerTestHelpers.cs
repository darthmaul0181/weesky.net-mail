using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure
{
    internal static class ControllerTestHelpers
    {
        public static ControllerContext CreateAuthenticatedContext(string username, string domain)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Upn, username),
                new(ClaimTypes.Dns, domain)
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
        }
    }
}
