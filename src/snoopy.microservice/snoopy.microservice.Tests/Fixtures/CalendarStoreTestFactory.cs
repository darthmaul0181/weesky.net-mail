using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;

namespace weesky.Snoopy.Microservice.Tests.Fixtures;

/// <summary>
/// The building blocks every calendar store test needs, shared so the slices after this one build
/// on the same shapes. Each factory call opens its OWN context — a fresh store per logical step,
/// as the contacts tests do — while the store and its sync store always share one.
/// </summary>
internal static class CalendarStoreTestFactory
{
    internal const string Zone = "Europe/Brussels";

    internal static CalendarStore Calendars(string databaseName) => CalendarsWithSync(databaseName).Store;

    /// <summary>The pair, for the tests that need to count the ranks a gesture took.</summary>
    internal static (CalendarStore Store, TestCalendarSyncStore Sync) CalendarsWithSync(string databaseName)
    {
        var context = new PreferencesTestDbContext(databaseName);
        var sync = new TestCalendarSyncStore(context);
        return (new CalendarStore(context, sync), sync);
    }

    internal static CalendarEventStore Events(
        string databaseName, ILogger<CalendarEventStore>? logger = null)
    {
        var context = new PreferencesTestDbContext(databaseName);
        return new CalendarEventStore(
            context, new TestCalendarSyncStore(context),
            logger ?? NullLogger<CalendarEventStore>.Instance);
    }

    /// <summary>A user holding the default calendar, and nothing else.</summary>
    internal static async Task<(string Db, Guid User, Guid Calendar)> SeedAsync(string databaseName)
    {
        var user = Guid.NewGuid();
        var calendar = await Calendars(databaseName)
            .EnsureDefaultAsync(user, Zone, CancellationToken.None);
        return (databaseName, user, calendar.Id);
    }

    /// <summary>An hour-long dated event in <see cref="Zone"/> — the minimal valid write.</summary>
    internal static EventWrite Write(
        Guid calendarId, DateTime? start = null, string? summary = "Standup",
        RecurrenceWrite? repeat = null)
    {
        var from = start ?? Local(2026, 9, 7, 9);
        return new EventWrite(
            calendarId, summary, null, null, false, from, from.AddHours(1), Zone, null, null,
            repeat, [], Availability.Busy, Visibility.Default, null);
    }

    internal static EventWrite AllDay(Guid calendarId, DateOnly day) =>
        new(calendarId, "Chores", null, null, true, null, null, null, day, day, null, [],
            Availability.Busy, Visibility.Default, null);

    internal static RecurrenceWrite Weekly() =>
        new("WEEKLY", 1, [], null, null, null, RecurrenceEnd.Never, null, null);

    /// <summary>A wall-clock reading, which is what the editor sends and the composer expects.</summary>
    internal static DateTime Local(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);

    internal static DateTime Wall(DateTime at) => DateTime.SpecifyKind(at, DateTimeKind.Unspecified);

    internal static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
