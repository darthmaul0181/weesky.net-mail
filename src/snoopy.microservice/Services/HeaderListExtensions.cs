using MimeKit;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Header lookups shared by the mail header readers.</summary>
internal static class HeaderListExtensions
{
    /// <summary>The topmost occurrence — everything below it could have been forged by the sender.</summary>
    public static Header? Topmost(this HeaderList headers, string field)
    {
        foreach (var header in headers)
            if (string.Equals(header.Field, field, StringComparison.OrdinalIgnoreCase)) return header;

        return null;
    }
}
