namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The two resources a "this and following" edit leaves behind, and whether exceptions
/// past the cut could not follow the new shape and were let go.</summary>
internal sealed record SplitOutcome(string Original, string Following, bool DroppedExceptions);
