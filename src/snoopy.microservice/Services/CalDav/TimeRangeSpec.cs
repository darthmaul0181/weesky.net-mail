namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>A window <c>[FromUtc, ToUtc[</c> with both bounds closed (spec § 8): an open one was
/// shut at <see cref="Calendar.OccurrenceExpander.MaxSpan"/> of the other before it got here.</summary>
internal sealed record TimeRangeSpec(DateTime FromUtc, DateTime ToUtc);
