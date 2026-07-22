using MimeKit;
using MimeKit.Cryptography;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Reads the SPF and DKIM verdicts out of the topmost Authentication-Results header (RFC 7601).</summary>
internal static class MailAuthenticationReader
{
    private const string HeaderName = "Authentication-Results";

    public static MailAuthentication? Parse(HeaderList headers)
    {
        var header = headers.Topmost(HeaderName);
        if (header is null) return null;

        return AuthenticationResults.TryParse(header.RawValue, out var parsed)
            ? new MailAuthentication(Verdict(parsed.Results, "spf"), Verdict(parsed.Results, "dkim"), header.Value)
            : new MailAuthentication(null, null, header.Value);
    }

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
