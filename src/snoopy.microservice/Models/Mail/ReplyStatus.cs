namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>Whether a guest's REPLY may enter the calendar (spec 5e, décision 12): only on an event
/// the webmail invited, for the version it holds, from a guest it lists, for the whole series, not
/// older than the answer the file already holds from that guest, and saying yes, maybe or no.</summary>
public enum ReplyStatus { Applicable, UnknownUid, NotOwner, UnknownAttendee, OccurrenceOnly, Stale, Superseded, UnsupportedAnswer }
