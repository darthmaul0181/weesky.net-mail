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
    /// The window the preselection narrows on: the VEVENT's own, or failing that the widest an
    /// alarm filter names — an alarm only ever fires from an instance, so a resource with no
    /// instance near that window carries no alarm in it either, and CandidatesAsync widens by the
    /// same day AlarmFires does. Without this, iOS's own shape (a VALARM time-range and nothing
    /// else) reads, parses and re-expands every row of the calendar, once per alarmed component,
    /// inside the snapshot transaction.
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
            return new TimeRangeSpec(
                windows.All(w => w.FromUtc is not null) ? windows.Min(w => w.FromUtc) : null,
                windows.All(w => w.ToUtc is not null) ? windows.Max(w => w.ToUtc) : null);
        }
    }
}
