using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Platform.Generic;

namespace weesky.Snoopy.Microservice.Configuration;

/// <summary>
/// The seam between the generic webmail core and the platform hosting it. Only these three ports
/// know anything about the directory behind the mailboxes; everything else in the service is
/// written against them, so a deployment that administers no mailboxes swaps the registrations
/// rather than the code.
/// </summary>
internal static class PlatformConfiguration
{
    /// <summary>
    /// Reads the root <c>Platform</c> key. Both failure modes name the key and the two values it
    /// accepts: a service that guessed would either address a dovecot database that is not there
    /// or silently stop enforcing address ownership, and neither says so anywhere a reader looks.
    /// </summary>
    public static bool UsesWeeskyPlatform(this IConfiguration configuration) =>
        configuration["Platform"] switch
        {
            PlatformOptions.Weesky => true,
            PlatformOptions.Generic => false,
            null or "" => throw new InvalidOperationException(
                $"'Platform' is missing: set \"{PlatformOptions.Weesky}\" or \"{PlatformOptions.Generic}\"."),
            var unknown => throw new InvalidOperationException(
                $"Unknown Platform '{unknown}': use \"{PlatformOptions.Weesky}\" or \"{PlatformOptions.Generic}\".")
        };

    /// <summary>
    /// No platform behind the mailbox: the three ports answer from the token and from nothing else.
    /// Singletons, unlike the weesky adapters — these hold no per-request state and no DbContext.
    /// </summary>
    public static IServiceCollection AddGenericPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IAliasDirectory, FreeIdentityDirectory>();
        services.AddSingleton<IProfileReader, NullProfileReader>();
        services.AddSingleton<IAccountInfoProvider, ClaimsAccountInfoProvider>();

        return services;
    }
}
