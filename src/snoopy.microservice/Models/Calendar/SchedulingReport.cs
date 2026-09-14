namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>What the invitation hook did after a write: the <c>scheduling_owner</c> the event now has,
/// and how many recipients the mails handed to a session or to the queue carried.</summary>
public sealed record SchedulingReport(string? Owner, int Sent);
