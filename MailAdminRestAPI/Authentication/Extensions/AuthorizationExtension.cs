using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Repositories;

namespace weesky.MailAdminRestAPI.Authentication.Extensions
{
	public static class AuthorizationExtension
	{
        /// <summary>
        /// Adds JwtBearer Middleware to the Pipeline
        /// </summary>
        public static IServiceCollection AddJwtBearerAuthentication(this IServiceCollection services, bool cookiesSupport = false)
        {
           var tokenConstants = services.BuildServiceProvider().GetService<IOptions<TokenConstants>>();
            

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters()
                {
                    ValidateAudience = true,
                    ValidateIssuer = true,
                    ValidateLifetime = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenConstants.Value.Key)),
                    ValidIssuer = tokenConstants.Value.Issuer,
                    ValidAudience = tokenConstants.Value.Audience
                };

                options.Events = new JwtBearerEvents()
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtBearerAuth");
                        logger.LogWarning("JWT authentication failed: {Reason}", context.Exception.Message);
                        return Task.CompletedTask;
                    },
                    OnMessageReceived = (context) =>
                    {
                        if (cookiesSupport && context.Request.Cookies.TryGetValue(tokenConstants.Value.AuthCookieName, out var cookie))
                        {
                            context.Token = cookie;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var claims = context.Principal?.Claims ?? Enumerable.Empty<Claim>();
                        var name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Upn)?.Value;
                        var domain = claims.FirstOrDefault(c => c.Type == ClaimTypes.Dns)?.Value;

                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain))
                        {
                            context.Fail("Missing required claims");
                            return Task.CompletedTask;
                        }

                        var repo = context.HttpContext.RequestServices.GetRequiredService<IUsersRepository>();
                        if (repo.FindByEmail($"{name}@{domain}") == null)
                        {
                            context.Fail("User no longer exists");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

            return services;
        }
    }
}
