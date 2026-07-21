using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Reads the SPF and DKIM verdicts out of the Authentication-Results headers (RFC 8601).</summary>
internal static class AuthenticationResults
{
    private const string HeaderName = "Authentication-Results";

    public static MailAuthentication? Parse(HeaderList headers)
    {
        string? spf = null, dkim = null, first = null, verdicts = null;

        foreach (var header in headers)
        {
            if (!string.Equals(header.Field, HeaderName, StringComparison.OrdinalIgnoreCase)) continue;

            first ??= header.Value;

            var headerSpf = MethodResult(header.Value, "spf");
            var headerDkim = MethodResult(header.Value, "dkim");
            if (headerSpf is null && headerDkim is null) continue;

            spf ??= headerSpf;
            dkim ??= headerDkim;
            verdicts ??= header.Value;
        }

        return first is null ? null : new MailAuthentication(spf, dkim, verdicts ?? first);
    }

    private static string? MethodResult(string value, string method)
    {
        foreach (var token in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = token.IndexOf('=');
            if (equals < 0 || !string.Equals(token[..equals], method, StringComparison.OrdinalIgnoreCase)) continue;

            var result = token[(equals + 1)..].TrimStart();
            var end = result.IndexOfAny([' ', '\t', '(']);
            return (end < 0 ? result : result[..end]).ToLowerInvariant();
        }

        return null;
    }
}
