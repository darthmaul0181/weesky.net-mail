using System.Text.RegularExpressions;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// The address a synchronisation client is told to enter. It comes from here rather than from the
/// browser: the frontend knows the URL it calls, which is not necessarily the one the proxy
/// publishes, and a wrong address on that screen is a client configuration that fails with nothing
/// saying where.
///
/// Bare origin, no path and no port. A path breaks the clients that concatenate
/// <c>/.well-known/carddav</c> onto it, and some iOS versions ignore a non-standard port and try
/// 443 then 80 anyway — a configuration that works on one device and fails on the other for a
/// reason invisible from both. Empty is legal and means this deployment serves no /dav.
/// </summary>
public sealed class DavOptions
{
    public string? PublicUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicUrl);

    // An explicit ":443" is indistinguishable from no port at all once Uri has parsed it —
    // IsDefaultPort and Authority both normalise it away — so the literal text is checked instead.
    private static readonly Regex ExplicitPort = new(@"^https://(\[[^\]]+\]|[^:/]+):\d+$", RegexOptions.Compiled);

    /// <summary>Validated on start rather than on first use, where an operator is watching.</summary>
    internal static bool IsBareHttpsOrigin(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (value == value.Trim()
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && uri.PathAndQuery == "/"
            && !value.EndsWith('/')
            && !ExplicitPort.IsMatch(value));
}
