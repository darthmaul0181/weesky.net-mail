using MimeKit;

namespace weesky.Snoopy.Microservice.Services.Calendar.Delivery;

internal enum DeliveryPartStatus { None, TooLarge, Found }

internal sealed record DeliveryCalendarPart(DeliveryPartStatus Status, string? Ics)
{
    internal static readonly DeliveryCalendarPart Nothing = new(DeliveryPartStatus.None, null);
    internal static readonly DeliveryCalendarPart TooLarge = new(DeliveryPartStatus.TooLarge, null);
}

/// <summary>The raw mail the mail server posts, to the calendar text the applier reads. Bounded
/// where the attacker sits: nesting during the parse (a StackOverflowException is not catchable),
/// the part count and the decoded size after it (spec 5e3, § Le traitement).</summary>
internal static class DeliveryMailReader
{
    internal const int MaxMimeDepth = 16;
    internal const int MaxParts = 256;

    internal static async Task<DeliveryCalendarPart> ReadAsync(Stream body, CancellationToken cancellationToken)
    {
        var options = new ParserOptions { MaxMimeDepth = MaxMimeDepth };
        try
        {
            using var message = await MimeMessage.LoadAsync(options, body, cancellationToken);
            if (message.BodyParts.Take(MaxParts + 1).Count() > MaxParts) return DeliveryCalendarPart.Nothing;
            if (MailMessageMapper.CalendarPart(message) is not { Content: not null } part) return DeliveryCalendarPart.Nothing;

            using var decoded = new MemoryStream();
            await part.Content.DecodeToAsync(decoded, cancellationToken);
            if (decoded.Length > IcsGuards.MaxIcsBytes) return DeliveryCalendarPart.TooLarge;
            return new DeliveryCalendarPart(DeliveryPartStatus.Found,
                MailMessageMapper.DecodeText(decoded.GetBuffer(), (int)decoded.Length, part.ContentType.Charset));
        }
        catch (FormatException)
        {
            return DeliveryCalendarPart.Nothing;
        }
    }
}
