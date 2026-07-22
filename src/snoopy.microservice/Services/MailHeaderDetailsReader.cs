using System.Text.RegularExpressions;
using MimeKit;
using MimeKit.Cryptography;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Reads the reader's expanded-header details out of a message's headers. Topmost occurrence only.</summary>
internal static partial class MailHeaderDetailsReader
{
    public static MailHeaderDetails Parse(HeaderList headers)
    {
        var auth = TopmostAuthenticationResults(headers);

        return new MailHeaderDetails(
            TopmostValue(headers, "List-Id"),
            SentBy(headers, auth),
            SignedBy(headers, auth),
            UnsubscribeUrl(TopmostValue(headers, "List-Unsubscribe")),
            TlsReceived(TopmostValue(headers, "Received")));
    }

    private static string? TopmostValue(HeaderList headers, string field) => headers.Topmost(field)?.Value.Trim();

    private static AuthenticationResults? TopmostAuthenticationResults(HeaderList headers)
    {
        var header = headers.Topmost("Authentication-Results");
        return header is not null && AuthenticationResults.TryParse(header.RawValue, out var parsed) ? parsed : null;
    }

    // Gmail's "mailed by": the envelope domain. The authenticated smtp.mailfrom is the most
    // trustworthy source; Return-Path (written by our own MTA) and Sender come after.
    private static string? SentBy(HeaderList headers, AuthenticationResults? auth)
        => DomainOf(SpfMailFrom(auth))
           ?? DomainOf(TopmostValue(headers, "Return-Path"))
           ?? DomainOf(TopmostValue(headers, "Sender"));

    private static string? SpfMailFrom(AuthenticationResults? auth)
    {
        var spf = auth?.Results.FirstOrDefault(r => string.Equals(r.Method, "spf", StringComparison.OrdinalIgnoreCase));
        return spf is null ? null : PropertyOf(spf, "smtp", "mailfrom");
    }

    // A line claiming "signed by X" must not survive a failed verification — no fallback either.
    // Any passing signature outranks a failing one (a list breaks the original while its own verifies).
    private static string? SignedBy(HeaderList headers, AuthenticationResults? auth)
    {
        var dkim = auth?.Results
            .Where(r => string.Equals(r.Method, "dkim", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var best = dkim?.FirstOrDefault(r => string.Equals(r.Result, "pass", StringComparison.OrdinalIgnoreCase))
                   ?? dkim?.FirstOrDefault();
        if (best is not null && string.Equals(best.Result, "fail", StringComparison.OrdinalIgnoreCase)) return null;

        return (best is null ? null : PropertyOf(best, "header", "d"))
               ?? DkimSignatureDomain(TopmostValue(headers, "DKIM-Signature"));
    }

    private static string? PropertyOf(AuthenticationMethodResult result, string ptype, string name)
    {
        foreach (var property in result.Properties)
            if (string.Equals(property.PropertyType, ptype, StringComparison.OrdinalIgnoreCase)
                && string.Equals(property.Property, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;

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
    // RFC 2369 delimits entries with <> because a URL may contain commas — the comma split is
    // only the fallback for bracketless values.
    private static string? UnsubscribeUrl(string? value)
    {
        if (value is null) return null;

        var tokens = AngleToken().Matches(value);
        var entries = tokens.Count > 0
            ? tokens.Select(m => m.Groups[1].Value)
            : value.Split(',');

        string? mailto = null;
        foreach (var entry in entries)
        {
            var url = NormalizeScheme(entry.Trim());
            if (url.StartsWith("https://", StringComparison.Ordinal)
                || url.StartsWith("http://", StringComparison.Ordinal)) return url;
            if (mailto is null && url.StartsWith("mailto:", StringComparison.Ordinal)) mailto = url;
        }

        return mailto;
    }

    // The frontend branches on the scheme case-sensitively; the wire scheme is case-insensitive.
    private static string NormalizeScheme(string url)
    {
        var colon = url.IndexOf(':');
        return colon <= 0 ? url : string.Concat(url[..colon].ToLowerInvariant(), url[colon..]);
    }

    // ESMTPS (covers ESMTPSA) is the with-TLS SMTP dialect; "TLS" catches version/cipher notes
    // (TLS1_2, TLSv1.3). The leading boundary keeps ids like "ABTLSQ7" out; "TLS" stays
    // case-sensitive so a lowercase "tls" inside a hostname (e.g. tls-relay.example.com) can't match.
    private static bool? TlsReceived(string? value)
        => value is null ? null : EsmtpsToken().IsMatch(value) || TlsToken().IsMatch(value);

    [GeneratedRegex("<([^>]*)>")]
    private static partial Regex AngleToken();

    [GeneratedRegex("(?<![A-Za-z0-9])ESMTPS", RegexOptions.IgnoreCase)]
    private static partial Regex EsmtpsToken();

    [GeneratedRegex("(?<![A-Za-z0-9])TLS")]
    private static partial Regex TlsToken();
}
