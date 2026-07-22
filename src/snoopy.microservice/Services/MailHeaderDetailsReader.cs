using MimeKit;
using MimeKit.Cryptography;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Reads the reader's expanded-header details out of a message's headers. Topmost occurrence only.</summary>
internal static class MailHeaderDetailsReader
{
    public static MailHeaderDetails Parse(HeaderList headers)
    {
        var auth = TopmostAuthenticationResults(headers);

        return new MailHeaderDetails(
            Topmost(headers, "List-Id"),
            SentBy(headers, auth),
            SignedBy(headers, auth),
            UnsubscribeUrl(Topmost(headers, "List-Unsubscribe")),
            TlsReceived(Topmost(headers, "Received")));
    }

    // HeaderList preserves message order and relays prepend, so the first match is the topmost.
    private static string? Topmost(HeaderList headers, string field)
    {
        foreach (var header in headers)
            if (string.Equals(header.Field, field, StringComparison.OrdinalIgnoreCase)) return header.Value.Trim();

        return null;
    }

    private static AuthenticationResults? TopmostAuthenticationResults(HeaderList headers)
    {
        foreach (var header in headers)
        {
            if (!string.Equals(header.Field, "Authentication-Results", StringComparison.OrdinalIgnoreCase)) continue;
            return AuthenticationResults.TryParse(header.RawValue, out var parsed) ? parsed : null;
        }

        return null;
    }

    // Gmail's "mailed by": the envelope domain. The authenticated smtp.mailfrom is the most
    // trustworthy source; Return-Path (written by our own MTA) and Sender come after.
    private static string? SentBy(HeaderList headers, AuthenticationResults? auth)
        => DomainOf(Property(auth, "spf", "smtp", "mailfrom"))
           ?? DomainOf(Topmost(headers, "Return-Path"))
           ?? DomainOf(Topmost(headers, "Sender"));

    private static string? SignedBy(HeaderList headers, AuthenticationResults? auth)
        => Property(auth, "dkim", "header", "d") ?? DkimSignatureDomain(Topmost(headers, "DKIM-Signature"));

    private static string? Property(AuthenticationResults? auth, string method, string ptype, string name)
    {
        if (auth is null) return null;

        foreach (var result in auth.Results)
        {
            if (!string.Equals(result.Method, method, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var property in result.Properties)
                if (string.Equals(property.PropertyType, ptype, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(property.Property, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
        }

        return null;
    }

    private static string? DkimSignatureDomain(string? value)
    {
        if (value is null) return null;

        foreach (var segment in value.Split(';'))
        {
            var trimmed = segment.Trim();
            if (trimmed.StartsWith("d=", StringComparison.OrdinalIgnoreCase)) return trimmed[2..].Trim();
        }

        return null;
    }

    // Accepts "a@b.c", "<a@b.c>" or "Name <a@b.c>" — the part after the last @.
    private static string? DomainOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        var cleaned = address.Trim().TrimStart('<').TrimEnd('>');
        var at = cleaned.LastIndexOf('@');
        var domain = at >= 0 ? cleaned[(at + 1)..] : cleaned;
        return domain.Length > 0 ? domain : null;
    }

    // Sender-controlled and rendered as a link: only http(s) and mailto survive, https first.
    private static string? UnsubscribeUrl(string? value)
    {
        if (value is null) return null;

        string? mailto = null;
        foreach (var entry in value.Split(','))
        {
            var url = entry.Trim().TrimStart('<').TrimEnd('>');
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return url;
            if (mailto is null && url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) mailto = url;
        }

        return mailto;
    }

    // ESMTPS (covers ESMTPSA) is the with-TLS SMTP dialect; "TLS" catches version/cipher notes.
    // "TLS" is case-sensitive so a lowercase "tls" inside a hostname (e.g. tls-relay.example.com) doesn't match.
    private static bool? TlsReceived(string? value)
        => value is null
            ? null
            : value.Contains("ESMTPS", StringComparison.OrdinalIgnoreCase) || value.Contains("TLS", StringComparison.Ordinal);
}
