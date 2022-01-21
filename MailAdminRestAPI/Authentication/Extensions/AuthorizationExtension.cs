using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using weesky.MailAdminRestAPI.Authentication.Models;

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
                    OnAuthenticationFailed = (context) =>
                    {
                        Console.WriteLine(context.Exception);
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
                    OnTokenValidated = (context) =>
                    {
                        return Task.CompletedTask;
                    }
                };
            });

            return services;
        }
    }
}
