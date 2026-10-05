namespace weesky.Scotty.Microservice.Configuration;

/// <summary>
/// Where the service keeps what must survive a restart: the Data Protection key ring and the
/// session signing key. systemd's StateDirectory= provides it outside the deployment path — which
/// the release chmod/chown walk recursively — and owned by the service user.
/// </summary>
internal static class StateDirectory
{
    public static string Resolve(IHostEnvironment environment) =>
        Resolve(environment, Environment.GetEnvironmentVariable("STATE_DIRECTORY"));

    internal static string Resolve(IHostEnvironment environment, string? variable)
    {
        var stateDirectory = variable?.Split(':')[0];
        if (!string.IsNullOrEmpty(stateDirectory)) return stateDirectory;

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "STATE_DIRECTORY is not set. Add 'StateDirectory=scotty.microservice' to the systemd unit. " +
                "Refusing to start rather than falling back to a key ring under the deployment directory.");
        }

        // Under keys/, which git ignores: the session key cannot end up in a commit.
        return Path.Combine(environment.ContentRootPath, "keys");
    }
}
