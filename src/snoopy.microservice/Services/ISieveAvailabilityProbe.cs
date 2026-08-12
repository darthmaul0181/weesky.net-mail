namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Whether a ManageSieve server is reachable at all — settled by reading its RFC 5804 greeting,
/// never by opening a full session. The greeting arrives before STARTTLS on every ManageSieve
/// server, so no credential is ever at risk here; a passive observer of the banner already sees
/// everything this probe reads.
///
/// The result is memoised per (host, port) for the life of the process: the target only changes on
/// redeploy, so a UI capability check must not open a fresh socket on every page load.
/// </summary>
public interface ISieveAvailabilityProbe
{
    /// <summary>False without connecting when <paramref name="host"/> is empty or blank.</summary>
    Task<bool> IsAvailableAsync(string host, int port, CancellationToken cancellationToken);
}
