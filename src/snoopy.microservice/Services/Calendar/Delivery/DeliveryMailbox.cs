namespace weesky.Snoopy.Microservice.Services.Calendar.Delivery;

/// <summary>The X-Delivery-Mailbox header, validated before it is logged or handed to
/// <c>new User(...)</c> (which throws on anything but one '@'). Exactly one address, no whitespace,
/// no control character, the shape WebmailUserStore.FindByEmailAsync canonicalises to.</summary>
internal static class DeliveryMailbox
{
    private const int MaxLength = 320;

    internal static bool TryNormalize(string? raw, out string mailbox)
    {
        mailbox = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        if (mailbox.Length is 0 or > MaxLength) return false;
        if (mailbox.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        var at = mailbox.IndexOf('@');
        return at > 0 && at < mailbox.Length - 1 && mailbox.IndexOf('@', at + 1) < 0;
    }
}
