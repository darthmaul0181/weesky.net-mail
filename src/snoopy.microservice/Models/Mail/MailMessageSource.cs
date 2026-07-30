namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// A message as it arrived: the headers a reader wants distilled, plus the verbatim RFC822
/// bytes. <paramref name="Source"/> is capped — <paramref name="TotalBytes"/> is what the
/// server reports the whole message weighs, so the client can say what it is not showing.
/// </summary>
public sealed record MailMessageSource(
    string Subject,
    string? MessageId,
    DateTimeOffset Date,
    string FromName,
    string FromAddress,
    IReadOnlyList<MailAddressInfo> To,
    MailAuthentication? Authentication,
    string Source,
    long TotalBytes,
    bool Truncated)
{
    /// <summary>
    /// Truncation is decided from what the server says the message weighs, never from the
    /// length of what came back: a message of exactly the cap arrived whole, and inferring
    /// from the byte count alone would label it truncated forever.
    /// </summary>
    public static bool IsTruncated(long totalBytes, int maxBytes) => totalBytes > maxBytes;
}
