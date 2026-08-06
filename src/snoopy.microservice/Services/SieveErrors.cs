namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The failures the rules stack reports about the service itself rather than about the request.
///
/// Shared constants, the same discipline <c>ImapSession.MessageNotFound</c> follows: the layer
/// that produces an error and the layer that picks a status code cannot drift apart on the exact
/// wording. Without them every ManageSieve outage — unconfigured, unreachable, refused credentials
/// — surfaced as 400, telling the client it had sent something wrong.
/// </summary>
public static class SieveErrors
{
    public const string NotConfigured = "Rules service is not configured";
    public const string Unreachable = "Unable to connect to the rules service";
    public const string AuthenticationFailed = "The rules service refused our credentials";
    public const string NotSecure = "Rules service refused: the connection could not be secured";

    /// <summary>The endpoint took the socket and then stopped answering inside the operation's
    /// budget. An outage: there is no smaller request the caller could have sent.</summary>
    public const string TimedOut = "The rules service stopped responding";

    /// <summary>The endpoint announced more bytes than a Sieve script can plausibly hold, so the
    /// read was refused before allocating. Also the service misbehaving, not the caller.</summary>
    public const string ResponseTooLarge = "The rules service sent an oversized response";

    /// <summary>The active account's domain has no Sieve endpoint at all. A 404, not a service
    /// failure: the frontend hides the Rules tab on this code rather than reporting an outage.</summary>
    public const string Unsupported = "sieve_unsupported";

    /// <summary>True when the failure is the service's and not the caller's — a 502, not a 400.</summary>
    public static bool IsServiceFailure(string? error) =>
        error is NotConfigured or Unreachable or AuthenticationFailed or NotSecure or TimedOut or ResponseTooLarge;
}
