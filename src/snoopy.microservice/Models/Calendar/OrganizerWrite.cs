namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The ORGANIZER the composer writes beside the guests: the account's default sending
/// identity, always an address this server hosts (décision 8).</summary>
public sealed record OrganizerWrite(string Email, string? Name);
