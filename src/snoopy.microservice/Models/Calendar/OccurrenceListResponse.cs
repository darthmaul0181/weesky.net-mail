namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>GET /api/Calendar/Events and .../Search — <see cref="EventOccurrence"/> travels as is,
/// this is only the envelope naming the list.</summary>
public sealed record OccurrenceListResponse(IReadOnlyList<EventOccurrence> Occurrences);
