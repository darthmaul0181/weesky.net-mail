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

    /// <summary>True when the failure is the service's and not the caller's — a 502, not a 400.</summary>
    public static bool IsServiceFailure(string? error) =>
        error is NotConfigured or Unreachable or AuthenticationFailed or NotSecure;
}
