namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// What the Sync screen shows. <see cref="Password"/> is set on the one response that draws a
/// secret — enabling for the first time, or regenerating — and is null everywhere else, so the
/// serialiser omits it: there is nothing to reveal, and never will be.
/// </summary>
public sealed record DavCredentialsView(
    string ServerUrl,
    string Username,
    bool Configured,
    bool CardDavEnabled,
    DateTime? LastUsedAt,
    string? Password);
