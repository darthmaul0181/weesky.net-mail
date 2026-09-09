namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>A <c>comp-filter</c> on VALARM: no alarm at all, an alarm firing inside a window, or —
/// neither named — an alarm at all.</summary>
internal sealed record AlarmFilterSpec(bool IsNotDefined, TimeRangeSpec? TimeRange);
