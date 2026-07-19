using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Authentication.Extensions
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class AuthorizationExtension
    {
        /// <summary>
        /// Adds JwtBearer Middleware to the Pipeline. The bearer options are configured
        /// through the named-options pipeline so <see cref="TokenConstants"/> is resolved
        /// lazily from the application container (no BuildServiceProvider).
        /// </summary>
        public static IServiceCollection AddJwtBearerAuthentication(this IServiceCollection services, bool cookiesSupport = false)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer();

            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<TokenConstants>>((options, tokenConstantsOptions) =>
                {
                    var tokenConstants = tokenConstantsOptions.Value;

                    options.TokenValidationParameters = new TokenValidationParameters()
                    {
                        ValidateAudience = true,
                        ValidateIssuer = true,
                        ValidateLifetime = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenConstants.Key)),
                        ValidIssuer = tokenConstants.Issuer,
                        ValidAudience = tokenConstants.Audience
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
                            if (cookiesSupport && context.Request.Cookies.TryGetValue(tokenConstants.AuthCookieName, out var cookie))
                            {
                                context.Token = cookie;
                            }

                            return Task.CompletedTask;
                        },
                        OnTokenValidated = async context =>
                        {
                            var claims = context.Principal?.Claims ?? Enumerable.Empty<Claim>();
                            var name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Upn)?.Value;
                            var domain = claims.FirstOrDefault(c => c.Type == ClaimTypes.Dns)?.Value;

                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain))
                            {
                                context.Fail("Missing required claims");
                                return;
                            }

                            // This runs on every authenticated request. It was cheap enough for
                            // an alias panel; a mail client is far chattier, so the lookup is
                            // cached briefly. The TTL bounds how long a deleted or disabled
                            // account keeps working.
                            var email = $"{name}@{domain}";
                            var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();

                            var exists = await cache.GetOrCreateAsync($"user-exists:{email}", async entry =>
                            {
                                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);

                                var repo = context.HttpContext.RequestServices.GetRequiredService<IUsersRepository>();
                                return await repo.FindByEmailAsync(email) != null;
                            });

                            if (!exists)
                            {
                                context.Fail("User no longer exists");
                            }
                        }
                    };
                });

            return services;
        }
    }
}
