using System.Text.RegularExpressions;
using MimeKit;

namespace weesky.Scotty.Microservice.Services;

/// <summary>
/// What a list row needs to ask for a sender's BIMI logo: the DMARC-verified domain and the
/// selector the sender declared. Neither is a trust decision — the logo itself is vetted elsewhere.
/// </summary>
internal static partial class MailBimiHint
{
    /// <summary>
    /// The header fields a summary FETCH has to ask for. On the wire this is one
    /// BODY.PEEK[HEADER.FIELDS (...)] alongside the other items — one more item in the same
    /// round trip, not a second request.
    /// </summary>
    public static readonly string[] Fields = [.. MailPriorityReader.Fields, "Authentication-Results", "BIMI-Selector"];

    public static (string? Domain, string? Selector) Parse(HeaderList headers)
    {
        var domain = MailAuthenticationReader.DmarcPassDomain(headers);
        return domain is null ? (null, null) : (domain, Selector(headers));
    }

    private static string? Selector(HeaderList headers)
    {
        var value = headers.Topmost("BIMI-Selector")?.Value;
        var tag = value?.Split(';').Select(t => t.Trim()).FirstOrDefault(t => t.StartsWith("s=", StringComparison.OrdinalIgnoreCase));
        var selector = tag?[2..].Trim().ToLowerInvariant();
        return selector is not null && IsSelector(selector) ? selector : null;
    }

    public static bool IsSelector(string selector) => SelectorPattern().IsMatch(selector);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,62}$")]
    private static partial Regex SelectorPattern();
}
