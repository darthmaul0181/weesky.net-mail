using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// A parsed calendar-query filter, conjunctive throughout. No VEVENT comp-filter means the whole
/// calendar; <c>is-not-defined</c> on the VEVENT means nothing, the collection holding nothing
/// else. The prop-filters and the alarm filters are satisfied together by ONE component of the
/// resource, master or override — RFC 4791 § 9.7.1's « targeted calendar component ».
/// </summary>
internal sealed record CalendarQuerySpec(
    bool AllEvents,
    bool NoneMatch,
    TimeRangeSpec? TimeRange,
    IReadOnlyList<PropFilterSpec> PropFilters,
    IReadOnlyList<AlarmFilterSpec> AlarmFilters)
{
    /// <summary>
    /// The window the preselection narrows on: the VEVENT's own, or failing that the envelope of
    /// the alarm windows widened by the day <see cref="OccurrenceExpander.AlarmFires"/> walks on
    /// each side of its own — an alarm only ever fires from an instance, and the instances that
    /// walk can reach are the rows worth reading. The reader adds its own slack for the zone
    /// spread on top; neither margin stands in for the other. Without this, iOS's own shape (a
    /// VALARM time-range and nothing else) reads, parses and re-expands every row of the calendar,
    /// once per alarmed component, inside the snapshot transaction.
    /// </summary>
    internal TimeRangeSpec? Preselection
    {
        get
        {
            if (TimeRange is not null) return TimeRange;

            var windows = AlarmFilters.Select(alarm => alarm.TimeRange).OfType<TimeRangeSpec>().ToList();
            if (windows.Count == 0) return null;

            // A null bound is an infinity and must WIDEN the envelope, while Min/Max over DateTime?
            // skip nulls — which would narrow it, and drop rows the filter had still to judge.
            var from = windows.All(w => w.FromUtc is not null) ? windows.Min(w => w.FromUtc) : null;
            var to = windows.All(w => w.ToUtc is not null) ? windows.Max(w => w.ToUtc) : null;
            return new TimeRangeSpec(
                from is { } f ? OccurrenceExpander.Shift(f, -OccurrenceExpander.Margin) : null,
                to is { } t ? OccurrenceExpander.Shift(t, OccurrenceExpander.Margin) : null);
        }
    }
}
