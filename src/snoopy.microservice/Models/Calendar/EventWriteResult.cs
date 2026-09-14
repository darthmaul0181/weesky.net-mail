namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The row as it was before the write, with what the scheduler needs of it.</summary>
public sealed record ReplacedVersion(string Ics, string? SchedulingOwner, string? SchedulingHash);

/// <summary>One resource the write touched: where it lives now, what it replaced (null on a
/// creation), what it holds now (null on a removal).</summary>
public sealed record EventChange(Guid? EventId, Guid CalendarId, string DavName, ReplacedVersion? Before, string? After);

/// <summary>What one write of <see cref="Repositories.ICalendarEventStore"/> did, so the invitation
/// hook can see every row it touched without re-reading the database.</summary>
public sealed record EventWriteResult(Guid EventId, IReadOnlyList<EventChange> Changes);
