using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Data;

public sealed class CalendarEntitiesTests
{
    [Fact]
    public async Task Calendar_AndEvent_RoundTrip()
    {
        using var db = new PreferencesTestDbContext(nameof(Calendar_AndEvent_RoundTrip));
        var user = Guid.NewGuid();
        var calendar = new Calendar { Id = Guid.NewGuid(), UserId = user, DavName = "default", DisplayName = "Personal",
            Color = "#3b82c4", Order = 0, TimeZone = "Europe/Brussels", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Calendars.Add(calendar);
        db.CalendarSyncStates.Add(new CalendarSyncState { CalendarId = calendar.Id, Epoch = Guid.NewGuid() });
        db.CalendarEvents.Add(new CalendarEvent { Id = Guid.NewGuid(), CalendarId = calendar.Id, UserId = user, Uid = "u1",
            DavName = "u1.ics", StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow, FirstOccurrence = DateTime.UtcNow,
            LastOccurrence = DateTime.UtcNow, IcsRaw = "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n", UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Single(await db.CalendarEvents.ToListAsync(), e => e.CalendarId == calendar.Id);
        Assert.True(calendar.IsVisible);
        Assert.Equal("OPAQUE", (await db.CalendarEvents.SingleAsync()).Transparency);
    }

    [Fact]
    public async Task Attendee_KeyIsEventIdAndPosition()
    {
        using var db = new PreferencesTestDbContext(nameof(Attendee_KeyIsEventIdAndPosition));
        var id = Guid.NewGuid();
        db.CalendarAttendees.Add(new CalendarAttendee { EventId = id, Position = 0, Email = "a@b.c" });
        db.CalendarAttendees.Add(new CalendarAttendee { EventId = id, Position = 1, Email = "a@b.c", RecurrenceId = "20260914T090000" });
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(2, await db.CalendarAttendees.CountAsync());
    }

    [Fact]
    public void Revision_SurvivesItsEvent()
    {
        var revision = new CalendarRevision { UserId = Guid.NewGuid(), EventId = null, CalendarId = null,
            IcsHash = "", IcsRaw = "", Cause = RevisionCause.Delete, ReplacedAt = DateTime.UtcNow };

        Assert.Null(revision.EventId);
    }

    // The InMemory provider enforces no foreign key, so nothing functional here can reproduce the
    // INSERT-order failure a real MariaDB gives. The declared edge is what makes EF write parents
    // first instead of falling back to alphabetical table order, and it is the only assertable part.
    [Theory]
    [InlineData(typeof(Calendar), nameof(Calendar.UserId), typeof(WebmailUser))]
    [InlineData(typeof(CalendarEvent), nameof(CalendarEvent.UserId), typeof(WebmailUser))]
    [InlineData(typeof(CalendarEvent), nameof(CalendarEvent.CalendarId), typeof(Calendar))]
    [InlineData(typeof(CalendarAttendee), nameof(CalendarAttendee.EventId), typeof(CalendarEvent))]
    [InlineData(typeof(CalendarSyncState), nameof(CalendarSyncState.CalendarId), typeof(Calendar))]
    [InlineData(typeof(CalendarTombstone), nameof(CalendarTombstone.CalendarId), typeof(Calendar))]
    [InlineData(typeof(CalendarRevision), nameof(CalendarRevision.UserId), typeof(WebmailUser))]
    public void Entity_DeclaresCascadingForeignKey(Type entityType, string propertyName, Type principalType)
    {
        using var db = new PreferencesTestDbContext(
            $"{nameof(Entity_DeclaresCascadingForeignKey)}_{entityType.Name}_{propertyName}");

        var entity = db.Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        var foreignKey = Assert.Single(
            entity.GetForeignKeys(), k => k.Properties.Single().Name == propertyName);

        Assert.Equal(principalType, foreignKey.PrincipalEntityType.ClrType);
        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    // Décision 2 : une révision survit à l'agenda et à l'événement qu'elle archive. Une arête vers
    // l'un ou l'autre effacerait en cascade l'archive que la suppression vient d'écrire.
    [Fact]
    public void Revision_HasNoEdgeToCalendarOrEvent()
    {
        using var db = new PreferencesTestDbContext(nameof(Revision_HasNoEdgeToCalendarOrEvent));

        var entity = db.Model.FindEntityType(typeof(CalendarRevision));
        Assert.NotNull(entity);
        var foreignKey = Assert.Single(entity.GetForeignKeys());

        Assert.Equal(typeof(WebmailUser), foreignKey.PrincipalEntityType.ClrType);
    }

    [Fact]
    public void Revision_CauseIsStoredAsTheLowercaseEnumName()
    {
        using var db = new PreferencesTestDbContext(nameof(Revision_CauseIsStoredAsTheLowercaseEnumName));

        var property = db.Model.FindEntityType(typeof(CalendarRevision))!.FindProperty(nameof(CalendarRevision.Cause));
        Assert.NotNull(property);
        var converter = property.GetValueConverter();
        Assert.NotNull(converter);

        Assert.Equal("rejected", converter.ConvertToProvider(RevisionCause.Rejected));
        Assert.Equal(RevisionCause.Webmail, converter.ConvertFromProvider("webmail"));
    }
}
