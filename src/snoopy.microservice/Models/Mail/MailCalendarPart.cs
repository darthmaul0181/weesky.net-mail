namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The calendar part the detail downloaded, carried to the invitation reader and never
/// serialized. <see cref="TooLarge"/>: the part's announced size passed IcsGuards.MaxIcsBytes, so
/// nothing was fetched and the card says the invitation is unreadable.</summary>
public sealed record MailCalendarPart(string Part, string Ics, bool TooLarge);
