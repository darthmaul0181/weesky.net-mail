using MimeKit;
using MimeKit.Cryptography;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Mail;

namespace weesky.Scotty.Microservice.Services;

/// <summary>Reads the SPF, DKIM and DMARC verdicts out of the topmost Authentication-Results header (RFC 7601).</summary>
internal static class MailAuthenticationReader
{
    private const string HeaderName = "Authentication-Results";

    public static MailAuthentication? Parse(HeaderList headers)
    {
        var header = headers.Topmost(HeaderName);
        if (header is null) return null;

        return TryParse(header, out var parsed)
            ? new MailAuthentication(
                Verdict(parsed.Results, "spf"),
                Verdict(parsed.Results, "dkim"),
                Verdict(parsed.Results, "dmarc"),
                header.Value)
            : new MailAuthentication(null, null, null, header.Value);
    }

    /// <summary>The lower-cased header.from of a dmarc=pass in the topmost header; null when absent or not a DNS name.</summary>
    public static string? DmarcPassDomain(HeaderList headers)
    {
        var header = headers.Topmost(HeaderName);
        if (header is null || !TryParse(header, out var parsed)) return null;

        var dmarc = parsed.Results.FirstOrDefault(r =>
            string.Equals(r.Method, "dmarc", StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Result, "pass", StringComparison.OrdinalIgnoreCase));
        var from = dmarc?.Properties.FirstOrDefault(p =>
            string.Equals(p.PropertyType, "header", StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Property, "from", StringComparison.OrdinalIgnoreCase))?.Value;

        var domain = from?.Trim().TrimEnd('.').ToLowerInvariant();
        return domain is not null && BimiRequest.IsDomainName(domain) ? domain : null;
    }

    private static bool TryParse(Header header, out AuthenticationResults parsed) =>
        AuthenticationResults.TryParse(header.RawValue, out parsed);

    // A method can appear more than once (e.g. two DKIM signatures). If any occurrence
    // passed, the method passed; otherwise the first occurrence's value stands.
    private static string? Verdict(List<AuthenticationMethodResult> results, string method)
    {
        string? first = null;
        foreach (var result in results)
        {
            if (!string.Equals(result.Method, method, StringComparison.OrdinalIgnoreCase)) continue;

            var value = result.Result.ToLowerInvariant();
            if (value == "pass") return "pass";
            first ??= value;
        }

        return first;
    }
}
