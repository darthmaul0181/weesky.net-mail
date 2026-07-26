using Microsoft.OpenApi;

namespace weesky.Snoopy.Microservice.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class ApiDocumentationConfiguration
{
    /// <summary>
    /// The XML comments file is generated in every configuration (see the csproj) because Swagger
    /// reads it at startup, not at build time.
    /// </summary>
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        return services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Version = "v1",
                Title = "weesky.mail.snoopy",
                Description = "weesky.net mail account management",
                Contact = new OpenApiContact
                {
                    Name = "administrator",
                    Email = "root@weesky.net"
                }
            });

            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "ApiDocumentation.xml"));
        });
    }
}
