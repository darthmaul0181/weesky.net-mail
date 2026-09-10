namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// What POST /api/Calendars/Import answers: the calendar the file was poured into, born of that
/// same call, and what the pouring did. The sidebar needs both — one to show the new collection
/// without a second round-trip, the other to say how many events arrived.
/// </summary>
public sealed record CalendarImportResponse(CalendarView Calendar, CalendarImportReport Report);
