namespace weesky.Scotty.Microservice.Services.Calendar.Invitations;

/// <summary>One ATTENDEE line a stored file holds for a guest: the answer it carries, and the
/// DTSTAMP of the REPLY that wrote it when one did (<see cref="PartStatRewriter.ReplyStampParameter"/>).</summary>
internal sealed record GuestLine(string PartStat, string? Stamp);
