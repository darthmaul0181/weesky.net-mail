using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.HealthChecks;

namespace weesky.Snoopy.Microservice.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class DatabaseConfiguration
{
    /// <summary>
    /// The webmail's own database, plus the health check over it. A platform bringing a directory
    /// of its own registers that one separately — the dovecot schema belongs to Dovecot and can be
    /// rebuilt by mail-server provisioning, which would take our preferences with it.
    ///
    /// Preferences creation is manual — no EF migrations here; see
    /// docs/superpowers/mail-2a5-database-prerequisite.md — and the service refuses to start
    /// without its connection string rather than run with folder roles silently inert.
    /// </summary>
    public static IServiceCollection AddSnoopyDatabases(this IServiceCollection services, IConfiguration configuration)
    {
        var preferences = configuration.GetConnectionString("WebmailPreferencesDatabase");
        if (string.IsNullOrEmpty(preferences))
        {
            throw new InvalidOperationException(
                "Connection string 'WebmailPreferencesDatabase' is missing. " +
                "Apply docs/superpowers/mail-2a5-database-prerequisite.md, then configure the connection string. " +
                "Refusing to start rather than running with folder roles silently inert.");
        }

        // Detected once at startup: ServerVersion.AutoDetect opens a connection, so it must not
        // run on every DbContext instantiation.
        var preferencesVersion = ServerVersion.AutoDetect(preferences);

        services.AddDbContext<PreferencesDbContext>(options =>
            options.UseMySql(preferences, preferencesVersion));

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        return services;
    }
}
