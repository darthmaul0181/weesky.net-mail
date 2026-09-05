# Agenda 5a — fondations : modèle, moteur iCalendar, occurrences, API : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de tâche vont dans le scratchpad sous le préfixe `5a-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-09-04-webmail-calendar-5-overview-design.md`](../specs/2026-09-04-webmail-calendar-5-overview-design.md) — toute « décision N » citée ici y renvoie ; les « fonctionnalités N » sont celles de son § « Les fonctionnalités du socle ». Le plan couvre la tranche **5a** seule : ce que 5b (écrans) et 5c (CalDAV) consommeront, sans un écran ni une route `/dav`.

**Goal :** stocker un événement comme une **ressource iCalendar souveraine** (`ics_raw`) dont les colonnes sont un index recalculé à chaque écriture, savoir en dérouler les occurrences sur une fenêtre, écrire depuis le webmail les quatre gestes (créer, modifier tout, celle-ci seulement, celle-ci et les suivantes), et exposer le tout par une API REST par agenda.

**Architecture :** six tables (`calendars`, `calendar_events`, `calendar_attendees`, `calendar_sync_state`, `calendar_tombstones`, `calendar_revisions`) sur le modèle exact de 4a/4c ; des services purs dans `Services/Calendar/` (`IcsDocument`, `IcsTimeZones`, `IcsGuards`, `IcsProjector`, `OccurrenceExpander`, `IcsComposer`) qui ne touchent jamais la base ; deux stores (`CalendarStore`, `CalendarEventStore`) et un `CalendarSyncStore` par agenda ; deux contrôleurs. Un seul moteur — Ical.Net — en lecture comme en écriture (décision 4).

**Tech stack :** .NET 10, EF Core (Pomelo MySQL, InMemory pour les tests), xUnit 2.9.3, Moq, **Ical.Net 5.2.3** (nouvelle dépendance, MIT) et sa dépendance NodaTime (TZDB embarquée).

## Global constraints

- `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; build : `cd src && dotnet build`.
- Les tests repository tournent sur EF InMemory (`PreferencesTestDbContext`), qui n'applique **ni FK, ni longueur de colonne, ni transaction** : toute règle d'intégrité est portée par le code.
- `Assert.IsType<T>` vérifie le type exact : `BadRequestObjectResult` pour `BadRequest(body)`, jamais `ObjectResult`.
- `ApiDocumentation.xml` : ne committer que les membres réellement touchés ; réverter la dérive que `dotnet test` régénère.
- Style : file-scoped namespaces, un type par fichier, records pour les DTO de réponse, classes `sealed` à propriétés `init` pour les corps de requête, `internal` par défaut, primary constructors, cancellation tokens partout, ILogger structuré. Pas de commentaire quand le code se lit seul.
- Commits : concis (2 lignes max), jamais commencer/finir par `@`, terminer par `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. Le message se passe par heredoc `git commit -F -`, jamais par here-string PowerShell.
- **Aucun écran ne change** : l'entrée Agenda du rail reste `ComingSoon`. Le frontend ne gagne que le contrat (`api.js`, `calendarTypes.ts`) en tâche 6.
- Les blocs SQL sont **joués à la main** par l'utilisateur sur `snoopy_webmail` et `snoopy_webmail_dev` — l'ingénieur amende le document de prérequis, il n'exécute pas de SQL.
- **API Ical.Net : les noms de membres exacts (v5) se vérifient sur le paquet installé** (`~/.nuget/packages/ical.net/5.2.3/lib/`, ou la doc du dépôt `ical-org/ical.net`) avant usage. Les sondes de la tâche 1 sont le contrat ; un nom cité dans ce plan qui ne compile pas s'ajuste au nom réel, jamais l'inverse. Les tests de comportement (fichier `.ics` en entrée → sortie attendue) sont ce qui fait foi.
- Tous les instants en base sont en **UTC** (`DateTime` `Kind = Utc`), tous les identifiants de fuseau sont **IANA** (`Europe/Brussels`), résolus par la TZDB de NodaTime — jamais `TimeZoneInfo` (décision 4).
- Toute écriture d'événement passe par **une** méthode du store (`ApplyIcsAsync`) qui hache, projette et pose la séquence ; aucun appelant ne calcule un hash.

---

### Task 1 : socle — dépendance, sondes Ical.Net, entités EF, DbContext, prérequis SQL

**Files :**
- Modify : `src/snoopy.microservice/snoopy.microservice.core.csproj` (PackageReference)
- Create : `src/snoopy.microservice/Data/Preferences/Calendar.cs`
- Create : `src/snoopy.microservice/Data/Preferences/CalendarEvent.cs`
- Create : `src/snoopy.microservice/Data/Preferences/CalendarAttendee.cs`
- Create : `src/snoopy.microservice/Data/Preferences/CalendarSyncState.cs`
- Create : `src/snoopy.microservice/Data/Preferences/CalendarTombstone.cs`
- Create : `src/snoopy.microservice/Data/Preferences/CalendarRevision.cs`
- Modify : `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Create : `docs/superpowers/webmail-calendar-tables.md`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/IcalNetProbeTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Data/CalendarEntitiesTests.cs`

**Interfaces (produit pour les tâches suivantes) :**
- `Calendar(Id, UserId, DavName, DisplayName, Description = "", Color, Order, TimeZone, IsVisible = true, CreatedAt, UpdatedAt)` ; `CalendarEvent(Id, CalendarId, UserId, Uid, DavName, Summary?, Location?, Description?, StartsAt, EndsAt, IsAllDay, TimeZone?, IsRecurring, FirstOccurrence, LastOccurrence, Status?, Transparency = "OPAQUE", Class?, IcsRaw, IcsHash = "", SyncSequence = 0, UpdatedAt)` ; `CalendarAttendee(EventId, Position, RecurrenceId?, Email, Name?, Role?, PartStat?, IsOrganizer)` ; `CalendarSyncState(CalendarId, Epoch, Seq, PrunedBelow)` ; `CalendarTombstone(CalendarId, DavName, SyncSequence, DeletedAt)` ; `CalendarRevision(Id, UserId, CalendarId?, EventId?, Uid?, DavName?, IcsHash, IcsRaw, Cause : RevisionCause, ReplacedAt)`.
- `PreferencesDbContext` expose `Calendars`, `CalendarEvents`, `CalendarAttendees`, `CalendarSyncStates`, `CalendarTombstones`, `CalendarRevisions`.
- Les sondes fixent les **noms réels** d'Ical.Net 5.2.3 que les tâches 2–4 emploient : chargement, sérialisation, accès aux composants, `CalDateTime`, occurrences paresseuses, `VTimeZone`.

- [ ] **Step 1 : ajouter la dépendance**

```xml
<PackageReference Include="Ical.Net" Version="5.2.3" />
```

`cd src && dotnet build` doit passer. Vérifier dans `obj/project.assets.json` que NodaTime arrive en transitif (≥ 3.2.2, décision 4) ; sinon l'ajouter explicitement.

- [ ] **Step 2 : écrire les sondes Ical.Net (elles échouent tant que les noms sont faux)**

Le fichier suit le modèle du § « Ce que les sondes ont appris » de `docs/superpowers/contacts-4a-residuals.md` : chaque test épingle **un** fait dont le plan dépend. Écrire les sondes avec les noms ci-dessous, puis les corriger sur le paquet installé jusqu'au vert — et **consigner chaque nom corrigé** dans le rapport de tâche, pour que les tâches 2–4 partent des bons.

```csharp
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;

public sealed class IcalNetProbeTests
{
    private const string Weekly = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//probe//EN
        BEGIN:VTIMEZONE
        TZID:Europe/Brussels
        BEGIN:STANDARD
        DTSTART:19701025T030000
        RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU
        TZOFFSETFROM:+0200
        TZOFFSETTO:+0100
        END:STANDARD
        BEGIN:DAYLIGHT
        DTSTART:19700329T020000
        RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU
        TZOFFSETFROM:+0100
        TZOFFSETTO:+0200
        END:DAYLIGHT
        END:VTIMEZONE
        BEGIN:VEVENT
        UID:probe-1
        DTSTAMP:20260901T080000Z
        DTSTART;TZID=Europe/Brussels:20260907T090000
        DTEND;TZID=Europe/Brussels:20260907T100000
        RRULE:FREQ=WEEKLY
        EXDATE;TZID=Europe/Brussels:20260921T090000
        SUMMARY:Standup
        X-APPLE-TRAVEL-ADVISORY-BEHAVIOR:AUTOMATIC
        END:VEVENT
        BEGIN:VEVENT
        UID:probe-1
        RECURRENCE-ID;TZID=Europe/Brussels:20260914T090000
        DTSTAMP:20260901T080000Z
        DTSTART;TZID=Europe/Brussels:20260914T110000
        DTEND;TZID=Europe/Brussels:20260914T120000
        SUMMARY:Standup (moved)
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void Load_ReadsMasterAndOverride()
    {
        var calendar = Calendar.Load(Weekly);
        Assert.Equal(2, calendar.Events.Count);
        var master = calendar.Events.Single(e => e.RecurrenceId is null);
        Assert.Equal("Europe/Brussels", master.DtStart.TzId);
        Assert.True(master.DtStart.HasTime);
        Assert.Single(master.RecurrenceRules);
    }

    [Fact]
    public void Occurrences_AreLazy_ExdateRemoves_OverrideReplaces()
    {
        var calendar = Calendar.Load(Weekly);
        var from = new CalDateTime(2026, 9, 1, 0, 0, 0, "UTC");
        var to = new CalDateTime(2026, 10, 1, 0, 0, 0, "UTC");
        var occurrences = calendar.GetOccurrences(from).TakeWhileBefore(to).ToList();
        // 7, 14 (moved to 11:00), 28 — 21 is EXDATEd.
        Assert.Equal(3, occurrences.Count);
        var moved = occurrences.Single(o => o.Period.StartTime.Value.Day == 14);
        Assert.Equal(11, moved.Period.StartTime.Value.Hour);
        Assert.Equal("Standup (moved)", ((CalendarEvent)moved.Source).Summary);
    }

    [Fact]
    public void Occurrences_InfiniteRule_DoesNotEnumerateWithoutBound()
    {
        var calendar = Calendar.Load(Weekly);
        var from = new CalDateTime(2026, 9, 1, 0, 0, 0, "UTC");
        var first = calendar.GetOccurrences(from).Take(5).ToList();
        Assert.Equal(5, first.Count);
    }

    [Fact]
    public void AsUtc_ResolvesIanaZoneThroughTzdb()
    {
        var master = Calendar.Load(Weekly).Events.Single(e => e.RecurrenceId is null);
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), master.DtStart.AsUtc);
    }

    [Fact]
    public void Serialize_KeepsUnknownXProperty()
    {
        var calendar = Calendar.Load(Weekly);
        var text = new CalendarSerializer().SerializeToString(calendar);
        Assert.Contains("X-APPLE-TRAVEL-ADVISORY-BEHAVIOR:AUTOMATIC", text);
        Assert.Contains("EXDATE;TZID=Europe/Brussels:20260921T090000", text);
    }

    [Fact]
    public void VTimeZone_FromTzdb_CarriesTransitionRules()
    {
        var calendar = new Calendar();
        calendar.AddTimeZone(VTimeZone.FromDateTimeZone("Europe/Brussels", new DateTime(2026, 1, 1), false));
        var text = new CalendarSerializer().SerializeToString(calendar);
        Assert.Contains("BEGIN:STANDARD", text);
        Assert.Contains("BEGIN:DAYLIGHT", text);
        Assert.Contains("TZID:Europe/Brussels", text);
    }

    [Fact]
    public void WindowsTzid_IsNotKnownToTzdb_ButMappable()
    {
        Assert.Null(NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull("Romance Standard Time"));
        var mapping = NodaTime.TimeZones.TzdbDateTimeZoneSource.Default.WindowsMapping.PrimaryMapping;
        Assert.Equal("Europe/Paris", mapping["Romance Standard Time"]);
    }

    [Fact]
    public void ThisAndFuture_IsParsedButNotAppliedByExpansion()
    {
        var text = Weekly.Replace("RECURRENCE-ID;TZID=Europe/Brussels:20260914T090000",
                                  "RECURRENCE-ID;RANGE=THISANDFUTURE;TZID=Europe/Brussels:20260914T090000");
        var calendar = Calendar.Load(text);
        var from = new CalDateTime(2026, 9, 1, 0, 0, 0, "UTC");
        var to = new CalDateTime(2026, 10, 1, 0, 0, 0, "UTC");
        var day28 = calendar.GetOccurrences(from).TakeWhileBefore(to).Single(o => o.Period.StartTime.Value.Day == 28);
        // Issue #455 : si ce test devient FAUX (28 à 11:00), la lacune s'est fermée — le rapport le dit.
        Assert.Equal(9, day28.Period.StartTime.Value.Hour);
    }
}
```

- [ ] **Step 3 : les faire passer** — `cd src && dotnet test --filter IcalNetProbeTests`. Corriger les noms (par exemple `Period.StartTime` peut s'appeler `Period.Start`, `TakeWhileBefore` peut vivre dans `Ical.Net.Evaluation`, `VTimeZone.FromDateTimeZone` peut avoir une autre arité). Si `VTimeZone_FromTzdb_CarriesTransitionRules` ne peut pas passer avec la bibliothèque — le bloc sort sans `STANDARD`/`DAYLIGHT` (issues #58/#241) —, l'écrire ainsi : le test est marqué `Skip` avec la raison, et la tâche 2 sérialise le `VTIMEZONE` elle-même depuis NodaTime (`DateTimeZone.GetZoneIntervals`). Le rapport de tâche tranche ce point explicitement.

- [ ] **Step 4 : écrire les tests d'entités qui échouent**

```csharp
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
        await db.SaveChangesAsync();
        Assert.Single(await db.CalendarEvents.Where(e => e.CalendarId == calendar.Id).ToListAsync());
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
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.CalendarAttendees.CountAsync());
    }

    [Fact]
    public void Revision_SurvivesItsEvent()
    {
        var revision = new CalendarRevision { UserId = Guid.NewGuid(), EventId = null, CalendarId = null,
            IcsHash = "", IcsRaw = "", Cause = RevisionCause.Delete, ReplacedAt = DateTime.UtcNow };
        Assert.Null(revision.EventId);
    }
}
```

- [ ] **Step 5 : vérifier l'échec** — compilation en échec (types absents).

- [ ] **Step 6 : implémenter les entités et le DbContext**

Chaque entité est `[Table("…")]` avec `[Column("…")]` sur chaque propriété, dans l'ordre du bloc SQL ci-dessous, sur le modèle exact de `Contact.cs`. Points qui ne se devinent pas :

```csharp
[Table("calendar_events")]
public sealed class CalendarEvent
{
    [Column("id")] public Guid Id { get; set; }
    [Column("calendar_id")] public Guid CalendarId { get; set; }
    [Column("user_id")] public Guid UserId { get; set; }
    [Column("uid")] public string Uid { get; set; } = string.Empty;
    [Column("dav_name")] public string DavName { get; set; } = string.Empty;
    [Column("summary")] public string? Summary { get; set; }
    [Column("location")] public string? Location { get; set; }
    [Column("description")] public string? Description { get; set; }
    [Column("starts_at")] public DateTime StartsAt { get; set; }
    [Column("ends_at")] public DateTime EndsAt { get; set; }
    [Column("is_all_day")] public bool IsAllDay { get; set; }
    /// <summary>IANA id, "UTC", or null = floating (décision 5).</summary>
    [Column("time_zone")] public string? TimeZone { get; set; }
    [Column("is_recurring")] public bool IsRecurring { get; set; }
    [Column("first_occurrence")] public DateTime FirstOccurrence { get; set; }
    [Column("last_occurrence")] public DateTime LastOccurrence { get; set; }
    [Column("status")] public string? Status { get; set; }
    [Column("transparency")] public string Transparency { get; set; } = "OPAQUE";
    [Column("class")] public string? Class { get; set; }
    [Column("ics_raw")] public string IcsRaw { get; set; } = string.Empty;
    /// <summary>SHA-256 hex of IcsRaw — base of the CalDAV ETag. "" = not computed yet.</summary>
    [Column("ics_hash")] public string IcsHash { get; set; } = string.Empty;
    [Column("sync_sequence")] public ulong SyncSequence { get; set; }
    [Column("updated_at")] public DateTime UpdatedAt { get; set; }
}
```

`Calendar.IsVisible` : colonne `is_visible`, défaut `true`, jamais projetée vers DAV (décision 2). `CalendarRevision.Cause` réutilise `RevisionCause` et la même conversion `HasConversion(v => v.ToString().ToLowerInvariant(), …).HasMaxLength(8)` que `ContactRevision`.

Dans `OnModelCreating`, à la suite des blocs contacts :

```csharp
modelBuilder.Entity<Calendar>().HasKey(c => c.Id);
modelBuilder.Entity<Calendar>().HasIndex(c => new { c.UserId, c.DavName }).IsUnique();
modelBuilder.Entity<Calendar>().HasOne<WebmailUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<CalendarEvent>().HasKey(e => e.Id);
modelBuilder.Entity<CalendarEvent>().HasIndex(e => new { e.CalendarId, e.Uid }).IsUnique();
modelBuilder.Entity<CalendarEvent>().HasIndex(e => new { e.CalendarId, e.DavName }).IsUnique();
modelBuilder.Entity<CalendarEvent>().HasIndex(e => new { e.UserId, e.FirstOccurrence, e.LastOccurrence });
modelBuilder.Entity<CalendarEvent>().HasIndex(e => new { e.CalendarId, e.SyncSequence });
modelBuilder.Entity<CalendarEvent>().HasOne<Calendar>().WithMany().HasForeignKey(e => e.CalendarId).OnDelete(DeleteBehavior.Cascade);
modelBuilder.Entity<CalendarEvent>().HasOne<WebmailUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<CalendarAttendee>().HasKey(a => new { a.EventId, a.Position });
modelBuilder.Entity<CalendarAttendee>().HasOne<CalendarEvent>().WithMany().HasForeignKey(a => a.EventId).OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<CalendarSyncState>().HasKey(s => s.CalendarId);
modelBuilder.Entity<CalendarSyncState>().HasOne<Calendar>().WithMany().HasForeignKey(s => s.CalendarId).OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<CalendarTombstone>().HasKey(t => new { t.CalendarId, t.DavName });
modelBuilder.Entity<CalendarTombstone>().HasIndex(t => new { t.CalendarId, t.SyncSequence });
modelBuilder.Entity<CalendarTombstone>().HasOne<Calendar>().WithMany().HasForeignKey(t => t.CalendarId).OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<CalendarRevision>().HasKey(r => r.Id);
modelBuilder.Entity<CalendarRevision>().Property(r => r.Id).ValueGeneratedOnAdd();
modelBuilder.Entity<CalendarRevision>().HasIndex(r => new { r.UserId, r.ReplacedAt });
modelBuilder.Entity<CalendarRevision>().HasIndex(r => r.ReplacedAt);
modelBuilder.Entity<CalendarRevision>().HasOne<WebmailUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
```

**Pas de FK de `calendar_revisions` vers `calendars` ni `calendar_events`** — décision 2 : la suppression d'un agenda effacerait l'archive qu'elle vient d'écrire.

- [ ] **Step 7 : vérifier le vert** — `cd src && dotnet test`.

- [ ] **Step 8 : écrire le document de prérequis**

`docs/superpowers/webmail-calendar-tables.md`, sur le modèle de `webmail-carddav-tables.md` (français, en-tête « à rejouer avant le déploiement sur les deux bases », FK vers `users`, un `COMMENT` par colonne, puis les « Pourquoi »). Le DDL, verbatim :

```sql
CREATE TABLE `calendars` (
  `id`           CHAR(36)     NOT NULL,
  `user_id`      CHAR(36)     NOT NULL,
  `dav_name`     VARCHAR(255) NOT NULL COLLATE utf8mb4_bin COMMENT 'Dernier segment de l''URL CalDAV ; fixé à la création, jamais renommé',
  `display_name` VARCHAR(255) NOT NULL,
  `description`  TEXT         NOT NULL,
  `color`        CHAR(7)      NOT NULL COMMENT '#RRGGBB ; le canal alpha d''Apple est retiré à l''écriture',
  `order`        INT          NOT NULL DEFAULT 0,
  `time_zone`    VARCHAR(64)  NOT NULL COMMENT 'Identifiant IANA ; celui du navigateur à la création (décision 6)',
  `is_visible`   TINYINT(1)   NOT NULL DEFAULT 1 COMMENT 'Case de la barre latérale ; jamais projetée vers DAV',
  `created_at`   DATETIME     NOT NULL,
  `updated_at`   DATETIME     NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_calendars_user_dav_name` (`user_id`, `dav_name`),
  CONSTRAINT `fk_calendars_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `calendar_events` (
  `id`               CHAR(36)     NOT NULL,
  `calendar_id`      CHAR(36)     NOT NULL,
  `user_id`          CHAR(36)     NOT NULL COMMENT 'Redondant avec calendars.user_id : la fenêtre de l''API interroge tous les agendas d''un coup',
  `uid`              VARCHAR(255) NOT NULL COLLATE utf8mb4_bin COMMENT 'Unique par agenda, pas par utilisateur (RFC 4791 § 4.1)',
  `dav_name`         VARCHAR(255) NOT NULL COLLATE utf8mb4_bin,
  `summary`          VARCHAR(255) NULL,
  `location`         VARCHAR(255) NULL,
  `description`      TEXT         NULL,
  `starts_at`        DATETIME     NOT NULL COMMENT 'UTC ; une date sans heure ou une heure flottante est posée dans le fuseau de l''agenda',
  `ends_at`          DATETIME     NOT NULL,
  `is_all_day`       TINYINT(1)   NOT NULL DEFAULT 0,
  `time_zone`        VARCHAR(64)  NULL COMMENT 'IANA, UTC, ou NULL = flottant',
  `is_recurring`     TINYINT(1)   NOT NULL DEFAULT 0,
  `first_occurrence` DATETIME     NOT NULL,
  `last_occurrence`  DATETIME     NOT NULL COMMENT '2100-01-01 pour une règle sans fin (décision 1)',
  `status`           VARCHAR(16)  NULL,
  `transparency`     VARCHAR(16)  NOT NULL DEFAULT 'OPAQUE',
  `class`            VARCHAR(16)  NULL,
  `ics_raw`          MEDIUMTEXT   NOT NULL COMMENT 'La ressource CalDAV entière, souveraine ; les colonnes en sont un index',
  `ics_hash`         CHAR(64)     NOT NULL DEFAULT '' COMMENT 'SHA-256 hex de ics_raw ; base de l''ETag',
  `sync_sequence`    BIGINT UNSIGNED NOT NULL DEFAULT 0,
  `updated_at`       TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_calendar_events_uid` (`calendar_id`, `uid`),
  UNIQUE KEY `ux_calendar_events_dav_name` (`calendar_id`, `dav_name`),
  KEY `ix_calendar_events_window` (`user_id`, `first_occurrence`, `last_occurrence`),
  KEY `ix_calendar_events_seq` (`calendar_id`, `sync_sequence`),
  CONSTRAINT `fk_calendar_events_calendar` FOREIGN KEY (`calendar_id`) REFERENCES `calendars` (`id`) ON DELETE CASCADE,
  CONSTRAINT `fk_calendar_events_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `calendar_attendees` (
  `event_id`      CHAR(36)     NOT NULL,
  `position`      INT          NOT NULL,
  `recurrence_id` VARCHAR(64)  NULL COMMENT 'Valeur littérale du RECURRENCE-ID du composant d''origine ; NULL = le maître',
  `email`         VARCHAR(320) NOT NULL,
  `name`          VARCHAR(255) NULL,
  `role`          VARCHAR(32)  NULL,
  `partstat`      VARCHAR(32)  NULL,
  `is_organizer`  TINYINT(1)   NOT NULL DEFAULT 0,
  PRIMARY KEY (`event_id`, `position`),
  CONSTRAINT `fk_calendar_attendees_event` FOREIGN KEY (`event_id`) REFERENCES `calendar_events` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `calendar_sync_state` (
  `calendar_id`  CHAR(36)        NOT NULL,
  `epoch`        CHAR(36)        NOT NULL,
  `seq`          BIGINT UNSIGNED NOT NULL DEFAULT 0,
  `pruned_below` BIGINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (`calendar_id`),
  CONSTRAINT `fk_calendar_sync_state_calendar` FOREIGN KEY (`calendar_id`) REFERENCES `calendars` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `calendar_tombstones` (
  `calendar_id`   CHAR(36)        NOT NULL,
  `dav_name`      VARCHAR(255)    NOT NULL COLLATE utf8mb4_bin,
  `sync_sequence` BIGINT UNSIGNED NOT NULL,
  `deleted_at`    TIMESTAMP       NOT NULL,
  PRIMARY KEY (`calendar_id`, `dav_name`),
  KEY `ix_calendar_tombstones_seq` (`calendar_id`, `sync_sequence`),
  KEY `ix_calendar_tombstones_time` (`deleted_at`),
  CONSTRAINT `fk_calendar_tombstones_calendar` FOREIGN KEY (`calendar_id`) REFERENCES `calendars` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `calendar_revisions` (
  `id`          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id`     CHAR(36)     NOT NULL,
  `calendar_id` CHAR(36)     NULL COMMENT 'Sans FK : survit à l''agenda (décision 2)',
  `event_id`    CHAR(36)     NULL COMMENT 'Sans FK : survit à l''événement',
  `uid`         VARCHAR(255) NULL,
  `dav_name`    VARCHAR(255) NULL COLLATE utf8mb4_bin,
  `ics_hash`    CHAR(64)     NOT NULL,
  `ics_raw`     MEDIUMTEXT   NOT NULL,
  `cause`       ENUM('put','webmail','import','delete','rejected') NOT NULL,
  `replaced_at` TIMESTAMP    NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_calendar_revisions_user_time` (`user_id`, `replaced_at`),
  KEY `ix_calendar_revisions_time` (`replaced_at`),
  KEY `ix_calendar_revisions_uid` (`calendar_id`, `uid`),
  CONSTRAINT `fk_calendar_revisions_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Les « Pourquoi » à écrire : `user_id` redondant sur les événements ; `uid` unique par agenda et non par utilisateur ; `dav_name` NOT NULL (table neuve, aucun rattrapage) ; `last_occurrence` et sa date-butoir ; `seq` et non `sequence` ; les révisions sans FK ; `calendars.order` entre back-quotes parce que `ORDER` est un mot-clé — **nommer la colonne `sort_order`** pour ne pas créer un piège de production : corriger le DDL et l'entité (`[Column("sort_order")] public int Order`) avant de committer.

- [ ] **Step 9 : commit** — `feat(calendar): 5a socle - entites, DbContext, sondes Ical.Net, prerequis SQL`

---

### Task 2 : le moteur de lecture — `IcsDocument`, `IcsTimeZones`, `IcsGuards`, `IcsProjector`, corpus réel

**Files :**
- Create : `src/snoopy.microservice/Services/Calendar/IcsDocument.cs`
- Create : `src/snoopy.microservice/Services/Calendar/IcsTimeZones.cs`
- Create : `src/snoopy.microservice/Services/Calendar/IcsGuards.cs`
- Create : `src/snoopy.microservice/Services/Calendar/IcsPrecondition.cs`
- Create : `src/snoopy.microservice/Services/Calendar/IcsProjector.cs`
- Create : `src/snoopy.microservice/Models/Calendar/EventProjection.cs`
- Create : `src/snoopy.microservice/Models/Calendar/AttendeeProjection.cs`
- Existing : `src/snoopy.microservice/snoopy.microservice.Tests/Fixtures/ICalendar/*.ics` + `SOURCES.md` (huit fichiers réels, déjà déposés — voir Step 1)
- Modify : `src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj` (`<Content Include="Fixtures\ICalendar\*.ics" CopyToOutputDirectory="PreserveNewest" />`)
- Test : `…/Tests/Services/IcsTimeZonesTests.cs`, `IcsGuardsTests.cs`, `IcsProjectorTests.cs`, `IcsCorpusTests.cs`

**Interfaces (produit pour les tâches suivantes) :**
```csharp
internal static class IcsDocument
{
    internal static Calendar? TryLoad(string ics);                       // null si le texte ne se parse pas
    internal static string Serialize(Calendar calendar);                 // CalendarSerializer, CRLF
    internal static string HashOf(string ics);                           // SHA-256 hex minuscule
    internal static CalendarEvent? MasterOf(Calendar calendar);          // le VEVENT sans RECURRENCE-ID, ou null
    internal static string InstanceIdOf(CalendarEvent component);       // valeur littérale du RECURRENCE-ID ("20260914T090000", "20260914"), "" pour le maître
}
internal static class IcsTimeZones
{
    internal const string Utc = "UTC";
    internal static string? ResolveIana(string? tzid);                   // IANA connu → lui-même ; nom Windows → IANA ; sinon null
    internal static bool IsKnownIana(string id);
    internal static VTimeZone Emit(string ianaId, DateTime earliestUtc); // bloc avec STANDARD/DAYLIGHT
    internal static DateTime ToUtc(DateTime local, string ianaId);       // via NodaTime, jamais TimeZoneInfo
    internal static DateTime FromUtc(DateTime utc, string ianaId);
}
internal enum IcsPrecondition { SupportedCalendarData, ValidCalendarData, ValidCalendarObjectResource, SupportedCalendarComponent, MaxResourceSize, MaxInstances }
internal sealed record IcsProblem(IcsPrecondition Precondition, string Message);
internal static class IcsGuards
{
    internal const int MaxIcsBytes = 1024 * 1024;
    internal const int MaxInstancesPerYear = 10_000;
    internal static IcsProblem? Check(string ics, Calendar? parsed);     // toutes les préconditions sauf la densité
    internal static IcsProblem? CheckDensity(Calendar parsed);           // 10 000 occurrences dans l'année qui suit DTSTART (décision 4)
}
internal sealed record AttendeeProjection(string? RecurrenceId, string Email, string? Name, string? Role, string? PartStat, bool IsOrganizer);
internal sealed record EventProjection(
    string Uid, string? Summary, string? Location, string? Description,
    DateTime StartsAt, DateTime EndsAt, bool IsAllDay, string? TimeZone,
    bool IsRecurring, DateTime FirstOccurrence, DateTime LastOccurrence,
    string? Status, string Transparency, string? Class,
    IReadOnlyList<AttendeeProjection> Attendees);
internal static class IcsProjector
{
    internal static readonly DateTime NoEnd = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    internal static EventProjection Project(Calendar parsed, string calendarTimeZone);   // totale : jamais d'exception
}
```

- [ ] **Step 1 : le corpus** — il est **déjà en place** : huit fichiers réels dans `Fixtures/ICalendar/` (iCloud, Google ×2, Thunderbird, Exchange 2010, Outlook 2003, Nextcloud, Etar), repris de suites de tests open source sous licence MIT/BSD/Apache, avec leur provenance dans `Fixtures/ICalendar/SOURCES.md`. **Ne rien fabriquer ni retoucher** : un fichier inventé teste ce qu'on sait déjà, et une ligne « corrigée » cache ce qu'on devait apprendre. Les tests corpus tournent en `[MemberData]` sur les fichiers **présents** (`Directory.GetFiles`) ; si un fichier ne passe pas, c'est le moteur qui s'adapte ou le rapport qui nomme la divergence — jamais le fichier. Ajouter au csproj : `<Content Include="Fixtures\ICalendar\*.ics" CopyToOutputDirectory="PreserveNewest" />`.

- [ ] **Step 2 : tests de fuseaux (échouent)**

```csharp
public sealed class IcsTimeZonesTests
{
    [Theory]
    [InlineData("Europe/Brussels", "Europe/Brussels")]
    [InlineData("Romance Standard Time", "Europe/Paris")]
    [InlineData("(UTC+01:00) Bruxelles, Copenhague, Madrid, Paris", null)]
    [InlineData("Nowhere/Land", null)]
    [InlineData(null, null)]
    public void ResolveIana(string? tzid, string? expected) => Assert.Equal(expected, IcsTimeZones.ResolveIana(tzid));

    [Fact]
    public void ToUtc_FollowsTzdbNotHost()
    {
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc),
            IcsTimeZones.ToUtc(new DateTime(2026, 3, 29, 3, 30, 0), "Europe/Brussels")); // première demi-heure d'été
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            IcsTimeZones.ToUtc(new DateTime(2026, 9, 7, 9, 0, 0), "Europe/Brussels"));
    }

    [Fact]
    public void Emit_CarriesRules()
    {
        var text = IcsDocument.Serialize(WithZone(IcsTimeZones.Emit("Europe/Brussels", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
        Assert.Contains("BEGIN:DAYLIGHT", text);
        Assert.Contains("TZOFFSETTO:+0200", text);
    }

    private static Calendar WithZone(VTimeZone zone) { var c = new Calendar(); c.AddTimeZone(zone); return c; }
}
```

- [ ] **Step 3 : tests des gardes (échouent)**

```csharp
public sealed class IcsGuardsTests
{
    [Fact] public void Unparsable_IsValidCalendarData() =>
        Assert.Equal(IcsPrecondition.ValidCalendarData, IcsGuards.Check("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n", null)!.Precondition);
    [Fact] public void TwoUids_IsValidCalendarObjectResource() =>
        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, Check(Ics.Events(("a", null), ("b", null)))!.Precondition);
    [Fact] public void TwoMasters_IsValidCalendarObjectResource() =>
        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, Check(Ics.Events(("a", null), ("a", null)))!.Precondition);
    [Fact] public void MissingUid_IsValidCalendarObjectResource() =>
        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, Check(Ics.Events(("", null)))!.Precondition);
    [Fact] public void Vtodo_IsSupportedCalendarComponent() =>
        Assert.Equal(IcsPrecondition.SupportedCalendarComponent, Check(Ics.Todo())!.Precondition);
    [Fact] public void ExceptionsWithoutMaster_Pass() =>
        Assert.Null(Check(Ics.Events(("a", "20260914"), ("a", "20260921"))));
    [Fact] public void MissingVtimezone_Passes() => Assert.Null(Check(Ics.WeeklyWithoutZone()));
    [Fact] public void OverOneMegabyte_IsMaxResourceSize() =>
        Assert.Equal(IcsPrecondition.MaxResourceSize, IcsGuards.Check(Ics.Padded(IcsGuards.MaxIcsBytes + 1), null)!.Precondition);
    [Theory]
    [InlineData("FREQ=DAILY", null)]
    [InlineData("FREQ=HOURLY", null)]
    [InlineData("FREQ=MINUTELY", IcsPrecondition.MaxInstances)]
    [InlineData("FREQ=SECONDLY;COUNT=1000000", IcsPrecondition.MaxInstances)]
    public void Density_IsJudgedOnOneYear(string rrule, IcsPrecondition? expected) =>
        Assert.Equal(expected, IcsGuards.CheckDensity(IcsDocument.TryLoad(Ics.Rule(rrule))!)?.Precondition);

    private static IcsProblem? Check(string ics) => IcsGuards.Check(ics, IcsDocument.TryLoad(ics));
}
```

`Ics` est une petite fabrique de test (`Tests/Fixtures/Ics.cs`) qui écrit des `VCALENDAR` minimaux à la main (`Events(params (string uid, string? recurrenceId)[])`, `Todo()`, `Rule(string rrule)`, `WeeklyWithoutZone()`, `Padded(int bytes)`) — la créer dans cette tâche, les tâches 3–5 s'en servent.

- [ ] **Step 4 : tests du projecteur (échouent)**

```csharp
public sealed class IcsProjectorTests
{
    [Fact]
    public void Dated_ProjectsUtcAndZone()
    {
        var p = Project(Ics.Single(start: "DTSTART;TZID=Europe/Brussels:20260907T090000", end: "DTEND;TZID=Europe/Brussels:20260907T100000"));
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), p.StartsAt);
        Assert.Equal("Europe/Brussels", p.TimeZone);
        Assert.False(p.IsAllDay); Assert.False(p.IsRecurring);
        Assert.Equal(p.StartsAt, p.FirstOccurrence); Assert.Equal(p.EndsAt, p.LastOccurrence);
    }

    [Fact]
    public void AllDay_IsPosedInCalendarZone_EndExclusive()
    {
        var p = Project(Ics.Single(start: "DTSTART;VALUE=DATE:20260907", end: "DTEND;VALUE=DATE:20260908"), "America/New_York");
        Assert.True(p.IsAllDay); Assert.Null(p.TimeZone);
        Assert.Equal(new DateTime(2026, 9, 7, 4, 0, 0, DateTimeKind.Utc), p.StartsAt);   // minuit New York
        Assert.Equal(new DateTime(2026, 9, 8, 4, 0, 0, DateTimeKind.Utc), p.EndsAt);
    }

    [Fact]
    public void Floating_IsPosedInCalendarZone() =>
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            Project(Ics.Single(start: "DTSTART:20260907T090000", end: "DTEND:20260907T100000")).StartsAt);

    [Fact]
    public void NoDtend_DateLastsOneDay_TimeLastsZero()
    {
        Assert.Equal(TimeSpan.FromDays(1), Span(Ics.Single(start: "DTSTART;VALUE=DATE:20260907", end: null)));
        Assert.Equal(TimeSpan.Zero, Span(Ics.Single(start: "DTSTART:20260907T090000Z", end: null)));
    }

    [Fact]
    public void Duration_IsHonoured() =>
        Assert.Equal(TimeSpan.FromMinutes(90), Span(Ics.Single(start: "DTSTART:20260907T090000Z", end: "DURATION:PT1H30M")));

    [Fact]
    public void InfiniteRule_LastIsSentinel_FirstIsDtstart()
    {
        var p = Project(Ics.Rule("FREQ=WEEKLY"));
        Assert.True(p.IsRecurring);
        Assert.Equal(IcsProjector.NoEnd, p.LastOccurrence);
    }

    [Fact]
    public void Count_And_Until_BoundLast()
    {
        Assert.Equal(new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc), Project(Ics.Rule("FREQ=WEEKLY;COUNT=4")).LastOccurrence); // 7,14,21,28 · fin 10:00 Bruxelles
        Assert.Equal(new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc), Project(Ics.Rule("FREQ=WEEKLY;UNTIL=20260921T235959Z")).LastOccurrence);
    }

    [Fact]
    public void Rdate_And_MovedOverride_ExtendLast()
    {
        var p = Project(Ics.Rule("FREQ=WEEKLY;COUNT=2", extra: "RDATE;TZID=Europe/Brussels:20261225T090000"));
        Assert.Equal(new DateTime(2026, 12, 25, 9, 0, 0, DateTimeKind.Utc), p.LastOccurrence);
        var q = Project(Ics.RuleWithOverride("FREQ=WEEKLY;COUNT=2", overrideStart: "20261130T090000"));
        Assert.Equal(new DateTime(2026, 11, 30, 9, 0, 0, DateTimeKind.Utc), q.LastOccurrence);
    }

    [Fact]
    public void ExceptionsWithoutMaster_ReadFirstException_NotRecurring()
    {
        var p = Project(Ics.Events(("a", "20260914"), ("a", "20260921")));
        Assert.False(p.IsRecurring);
        Assert.Equal(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc) + TimeSpan.FromDays(1) - TimeSpan.FromHours(2), p.LastOccurrence);
    }

    [Fact]
    public void Attendees_ComeFromEveryComponent_WithTheirRecurrenceId()
    {
        var p = Project(Ics.WithAttendees());
        Assert.Contains(p.Attendees, a => a.IsOrganizer && a.Email == "michel@weesky.be" && a.RecurrenceId is null);
        Assert.Contains(p.Attendees, a => a.Email == "lea@example.org" && a.RecurrenceId == "20260914T090000" && a.PartStat == "ACCEPTED");
    }

    [Fact]
    public void StatusTranspClass_AreProjected()
    {
        var p = Project(Ics.Single(start: "DTSTART:20260907T090000Z", end: null, extra: "STATUS:TENTATIVE\r\nTRANSP:TRANSPARENT\r\nCLASS:PRIVATE"));
        Assert.Equal("TENTATIVE", p.Status); Assert.Equal("TRANSPARENT", p.Transparency); Assert.Equal("PRIVATE", p.Class);
    }

    [Fact]
    public void WindowsTzid_ResolvesThroughMapping_ThenFileZone_ThenFloating()
    {
        Assert.Equal("Europe/Paris", Project(Ics.Single(start: "DTSTART;TZID=Romance Standard Time:20260907T090000", end: null, zone: Ics.WindowsZone("Romance Standard Time"))).TimeZone);
        var byFile = Project(Ics.Single(start: "DTSTART;TZID=Custom/Zone:20260907T090000", end: null, zone: Ics.FixedZone("Custom/Zone", "+0300")));
        Assert.Equal(new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc), byFile.StartsAt);   // le VTIMEZONE du fichier fait foi
        Assert.Null(Project(Ics.Single(start: "DTSTART;TZID=Nowhere/Land:20260907T090000", end: null)).TimeZone); // flottant, journalisé
    }

    private static EventProjection Project(string ics, string zone = "Europe/Brussels") => IcsProjector.Project(IcsDocument.TryLoad(ics)!, zone);
    private static TimeSpan Span(string ics) { var p = Project(ics); return p.EndsAt - p.StartsAt; }
}
```

- [ ] **Step 5 : tests corpus (échouent tant que le projecteur manque)**

```csharp
public sealed class IcsCorpusTests
{
    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ICalendar"), "*.ics")) data.Add(Path.GetFileName(f));
        return data;
    }

    [Theory, MemberData(nameof(Files))]
    public void Corpus_EveryResourceParsesProjectsAndSurvivesRoundTrip(string file)
    {
        foreach (var resource in IcsResources.Split(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ICalendar", file))))
        {
            var parsed = IcsDocument.TryLoad(resource);
            Assert.NotNull(parsed);
            Assert.Null(IcsGuards.Check(resource, parsed));
            var p = IcsProjector.Project(parsed, "Europe/Brussels");
            Assert.NotEqual(string.Empty, p.Uid);
            Assert.True(p.LastOccurrence >= p.FirstOccurrence);

            var again = IcsDocument.TryLoad(IcsDocument.Serialize(parsed));
            Assert.NotNull(again);
            Assert.Equal(p with { Attendees = [] }, IcsProjector.Project(again, "Europe/Brussels") with { Attendees = [] });
            Assert.Equal(p.Attendees.Count, IcsProjector.Project(again, "Europe/Brussels").Attendees.Count);
        }
    }
}
```

`IcsResources.Split(string vcalendar)` — dans `Services/Calendar/IcsResources.cs` — regroupe les composants d'un `VCALENDAR` multi-événements **par `UID`** en une liste de `VCALENDAR` complets (fonctionnalité 6 : « c'est le regroupement par UID qui fabrique la ressource »), chacun portant les `VTIMEZONE` que ses composants citent, les `VTODO`/`VJOURNAL` étant rendus à part dans `IcsResources.SplitOutcome(Resources, IgnoredTodos, IgnoredJournals)`. L'écrire ici : l'import (tâche 5) et l'export en dépendent. Il opère **sur l'objet Ical.Net** (nouveau `Calendar` par UID, composants copiés) et sérialise — pas sur le texte.

- [ ] **Step 6 : implémenter** — `IcsDocument`, `IcsTimeZones` (trois paliers de la décision 4 : `DateTimeZoneProviders.Tzdb.GetZoneOrNull`, puis `TzdbDateTimeZoneSource.Default.WindowsMapping.PrimaryMapping`, sinon `null` ; `Emit` via `VTimeZone.FromDateTimeZone` ou, selon le verdict de la sonde, un bloc construit depuis `DateTimeZone.GetZoneIntervals` sur ±1 an), `IcsGuards`, `IcsResources`, `IcsProjector`. Règles du projecteur (décision 1 et 5) : colonnes lues sur le maître, ou sur la **première** exception quand il n'y en a pas ; une date sans heure ou une heure flottante posée dans `calendarTimeZone` ; `TimeZone` = IANA résolu, `"UTC"` pour un `Z`, `null` pour flottant ; `FirstOccurrence`/`LastOccurrence` = min/max sur les occurrences réelles jusqu'à `NoEnd` — pour une règle sans fin, **ne pas énumérer** : `LastOccurrence = NoEnd`, `FirstOccurrence` = première occurrence réelle (DTSTART ou le plus tôt des RDATE/overrides) ; pour `COUNT`/`UNTIL`, énumérer paresseusement jusqu'à la fin, en tenant compte des overrides déplacés et des `RDATE` ; `Description` tronquée à 65 535 octets, `Summary`/`Location` à 255 caractères. Un `TZID` inconnu des deux premiers paliers : si le fichier porte son `VTIMEZONE`, Ical.Net l'applique de lui-même (la sonde le dit ; sinon l'appliquer par `calendar.TimeZones`) ; sinon flottant et `ILogger`… — le projecteur est statique et sans logger : il renvoie le fait dans `EventProjection` par `TimeZone = null`, et c'est le **store** (tâche 5) qui journalise.

- [ ] **Step 7 : vert** — `cd src && dotnet test`. Le rapport de tâche liste les fichiers corpus présents et ce qu'ils ont appris (comme `contacts-4a-residuals.md` § sondes).

- [ ] **Step 8 : commit** — `feat(calendar): moteur de lecture iCalendar - fuseaux, gardes, projection, corpus`

---

### Task 3 : `OccurrenceExpander` — la liste plate d'occurrences

**Files :**
- Create : `src/snoopy.microservice/Services/Calendar/OccurrenceExpander.cs`
- Create : `src/snoopy.microservice/Models/Calendar/EventOccurrence.cs`
- Test : `…/Tests/Services/OccurrenceExpanderTests.cs`

**Interfaces (produit pour les tâches suivantes) :**
```csharp
/// Une occurrence telle que l'API la rend (décision 5) : la forme suit celle de l'heure.
internal sealed record EventOccurrence(
    string Uid,
    string InstanceId,                 // valeur littérale du RECURRENCE-ID à écrire ("" = événement non récurrent)
    bool IsOverride,                   // vient d'un VEVENT à RECURRENCE-ID
    bool IsAllDay, bool IsFloating, string? TimeZone,
    DateTime? StartUtc, DateTime? EndUtc,            // datée
    DateOnly? StartDate, DateOnly? EndDateExclusive, // journée entière
    DateTime? LocalStart, DateTime? LocalEnd,        // flottante (Kind Unspecified)
    string? Summary, string? Location, string? Status, string Transparency, string? Class,
    bool HasAlarm, string? RecurrenceText);          // RecurrenceText : la RRULE brute du maître, pour l'affichage « se répète »

internal static class OccurrenceExpander
{
    internal const int MaxYears = 5;
    /// Fenêtre [fromUtc, toUtc[ ; viewTimeZone découpe les jours pour les journées entières et les flottantes (décision 5) ;
    /// calendarTimeZone pose les flottantes pour le protocole (décision 6). Jamais d'exception : une règle qui casse rend une liste vide.
    internal static IReadOnlyList<EventOccurrence> Expand(Calendar parsed, DateTime fromUtc, DateTime toUtc, string calendarTimeZone, string viewTimeZone);
}
```

- [ ] **Step 1 : tests (échouent)**

```csharp
public sealed class OccurrenceExpanderTests
{
    private static readonly DateTime From = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Weekly_WithExdateAndOverride()
    {
        var list = Expand(Ics.WeeklyWithExdateAndOverride()); // 7, 14 déplacé à 11:00, 21 EXDATE, 28
        Assert.Equal([7, 14, 28], list.Select(o => o.StartUtc!.Value.Day));
        var moved = list.Single(o => o.IsOverride);
        Assert.Equal("20260914T090000", moved.InstanceId);            // l'identifiant est l'origine, pas l'heure déplacée
        Assert.Equal(9, moved.StartUtc!.Value.Hour);                   // 11:00 Bruxelles = 09:00 UTC
        Assert.All(list.Where(o => !o.IsOverride), o => Assert.Equal("Europe/Brussels", o.TimeZone));
    }

    [Fact]
    public void Single_HasEmptyInstanceId() => Assert.Equal("", Expand(Ics.Single(start: "DTSTART:20260907T090000Z", end: null)).Single().InstanceId);

    [Fact]
    public void AllDay_KeepsDates_EndExclusive_AndFollowsViewZoneAtTheEdge()
    {
        var ics = Ics.Single(start: "DTSTART;VALUE=DATE:20260930", end: "DTEND;VALUE=DATE:20261001");
        var brussels = Expand(ics, From, To, view: "Europe/Brussels");
        Assert.Equal(new DateOnly(2026, 9, 30), brussels.Single().StartDate);
        Assert.Equal(new DateOnly(2026, 10, 1), brussels.Single().EndDateExclusive);
        // Fenêtre [1er sept 00:00 UTC, 1er oct 00:00 UTC[ vue depuis Los Angeles : le 30 sept y est encore dans la fenêtre.
        Assert.Single(Expand(ics, From, To, view: "America/Los_Angeles"));
        // Une fenêtre qui s'arrête au 30 sept 00:00 UTC ne contient pas le 30 vu de Bruxelles.
        Assert.Empty(Expand(ics, From, new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc), view: "Europe/Brussels"));
    }

    [Fact]
    public void Floating_IsReturnedAsLocal_AndCutInViewZone()
    {
        var o = Expand(Ics.Single(start: "DTSTART:20260907T090000", end: "DTEND:20260907T100000")).Single();
        Assert.True(o.IsFloating); Assert.Null(o.StartUtc);
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0), o.LocalStart);
    }

    [Fact]
    public void RecurringMatchesWhenOneOccurrenceTouchesTheWindow() =>
        Assert.Single(Expand(Ics.Rule("FREQ=WEEKLY"), new DateTime(2026, 9, 14, 8, 30, 0, DateTimeKind.Utc), new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public void BrokenRule_YieldsEmpty_NotThrow() => Assert.Empty(Expand(Ics.Rule("FREQ=WEEKLY;BYDAY=XX")));

    [Fact]
    public void Alarm_And_RecurrenceText_AreCarried()
    {
        var o = Expand(Ics.Rule("FREQ=WEEKLY", extra: "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nDESCRIPTION:x\r\nEND:VALARM")).First();
        Assert.True(o.HasAlarm); Assert.Equal("FREQ=WEEKLY", o.RecurrenceText);
    }

    private static IReadOnlyList<EventOccurrence> Expand(string ics, DateTime? from = null, DateTime? to = null, string view = "Europe/Brussels") =>
        OccurrenceExpander.Expand(IcsDocument.TryLoad(ics)!, from ?? From, to ?? To, "Europe/Brussels", view);
}
```

- [ ] **Step 2 : implémenter** — `calendar.GetOccurrences(start).TakeWhileBefore(end)` sur une fenêtre élargie d'un jour de chaque côté (décision 1), puis le tri final par occurrence : datée → chevauche `[from, to[` en UTC ; journée entière → ses dates touchent un jour de `[from, to[` découpé dans `viewTimeZone` ; flottante → posée dans `viewTimeZone`. `InstanceId` = `IcsDocument.InstanceIdOf(override)` pour un override, sinon la valeur littérale de l'occurrence dans la forme du `DTSTART` maître (`yyyyMMdd` pour une date, `yyyyMMddTHHmmss` en heure locale du `TZID` pour une datée, avec `Z` pour un `DTSTART` UTC) — **jamais** l'instant UTC (décision 5). `try/catch` autour de l'énumération : vide + rien d'autre (le store journalise). Le plafond de densité n'est pas revérifié ici — il est garanti à l'écriture (tâche 2) ; une fenêtre n'a donc jamais plus de `10 000 × années` occurrences par ressource.

- [ ] **Step 3 : vert, commit** — `feat(calendar): expansion des occurrences sur une fenetre, trois formes d'heure`

---

### Task 4 : `IcsComposer` — les écritures du webmail

**Files :**
- Create : `src/snoopy.microservice/Services/Calendar/IcsComposer.cs`
- Create : `src/snoopy.microservice/Models/Calendar/EventWrite.cs`
- Create : `src/snoopy.microservice/Models/Calendar/RecurrenceWrite.cs`
- Create : `src/snoopy.microservice/Models/Calendar/EditScope.cs`
- Test : `…/Tests/Services/IcsComposerTests.cs`

**Interfaces (produit pour les tâches suivantes) :**
```csharp
internal enum Availability { Busy, Tentative, Free }
internal enum Visibility { Default, Private }
internal enum EditScope { This, ThisAndFollowing, All }
internal enum RecurrenceEnd { Never, Count, Until }
internal sealed record RecurrenceWrite(
    string Frequency,                    // DAILY | WEEKLY | MONTHLY | YEARLY
    int Interval,
    IReadOnlyList<string> ByDay,         // "MO".."SU", vide sinon
    int? ByMonthDay, int? BySetPos, string? BySetPosDay,   // mensuel « le 15 » ou « le 2e mardi »
    RecurrenceEnd End, int? Count, DateOnly? Until);
internal sealed record EventWrite(
    Guid CalendarId, string? Summary, string? Location, string? Description,
    bool IsAllDay,
    DateTime? Start, DateTime? End, string? TimeZone,   // datée : heure locale (Kind Unspecified) + TZID du navigateur
    DateOnly? StartDate, DateOnly? EndDateInclusive,    // journée entière : l'éditeur affiche la fin incluse (décision 5)
    RecurrenceWrite? Repeat,
    IReadOnlyList<int> ReminderMinutesBefore,
    Availability Availability, Visibility Visibility, string? Url);

internal static class IcsComposer
{
    internal static string ComposeNew(EventWrite w, string uid, DateTime nowUtc);
    /// Réécrit toute la série (scope All) : conserve les composants inconnus, les rappels non-DISPLAY, les X-, les exceptions.
    internal static string RewriteAll(Calendar existing, EventWrite w, DateTime nowUtc);
    /// Cette occurrence seulement : ajoute ou remplace le VEVENT à RECURRENCE-ID = instanceId (forme du DTSTART maître).
    internal static string RewriteOne(Calendar existing, string instanceId, EventWrite w, DateTime nowUtc);
    /// Celle-ci et les suivantes : coupe (UNTIL) et rend la suite sous newUid. DroppedExceptions dit si des exceptions ultérieures ont été abandonnées.
    internal static SplitOutcome Split(Calendar existing, string instanceId, EventWrite w, string newUid, DateTime nowUtc);
    internal static string RemoveOne(Calendar existing, string instanceId, DateTime nowUtc);   // EXDATE + retrait de l'override
    /// Forme canonique pour « rien n'a changé » (décision 4) : re-sérialisation sans DTSTAMP/LAST-MODIFIED/SEQUENCE.
    internal static string Canonical(Calendar parsed);
    internal static bool SameContent(Calendar before, Calendar after) => Canonical(before) == Canonical(after);
}
internal sealed record SplitOutcome(string Original, string Following, bool DroppedExceptions);
```

- [ ] **Step 1 : tests (échouent)** — un fichier par geste, chaque test relit le résultat avec `IcsDocument.TryLoad` et vérifie par `OccurrenceExpander` ou par le texte :

```csharp
public sealed class IcsComposerTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_Dated_WritesTzidAndVtimezone_NeverUtc()
    {
        var text = IcsComposer.ComposeNew(Write(start: Local(2026, 9, 7, 9), end: Local(2026, 9, 7, 10), tz: "Europe/Brussels", repeat: Weekly()), "u1", Now);
        Assert.Contains("DTSTART;TZID=Europe/Brussels:20260907T090000", text);
        Assert.Contains("BEGIN:VTIMEZONE", text); Assert.Contains("BEGIN:DAYLIGHT", text);
        Assert.Contains("RRULE:FREQ=WEEKLY", text);
        Assert.Contains("DTSTAMP:20260904T100000Z", text); Assert.Contains("CREATED:20260904T100000Z", text);
        Assert.DoesNotContain("DTSTART:20260907T070000Z", text);
    }

    [Fact]
    public void New_AllDay_WritesDateAndExclusiveEnd_Transparent()
    {
        var text = IcsComposer.ComposeNew(Write(allDay: (new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 17)), availability: Availability.Free), "u1", Now);
        Assert.Contains("DTSTART;VALUE=DATE:20260915", text);
        Assert.Contains("DTEND;VALUE=DATE:20260918", text);   // fin incluse 17 → exclusive 18
        Assert.Contains("TRANSP:TRANSPARENT", text);
    }

    [Fact]
    public void Availability_MapsToStatusAndTransp()
    {
        Assert.Contains("STATUS:TENTATIVE", Compose(availability: Availability.Tentative));
        var busy = Compose(availability: Availability.Busy);
        Assert.DoesNotContain("STATUS:", busy); Assert.DoesNotContain("TRANSP:TRANSPARENT", busy);
        Assert.Contains("CLASS:PRIVATE", Compose(visibility: Visibility.Private));
    }

    [Fact]
    public void Reminder_WritesDisplayAlarmRelativeToStart() =>
        Assert.Contains("TRIGGER:-PT15M", Compose(reminders: [15]));

    [Fact]
    public void RewriteAll_KeepsForeignLines_AndBumpsSequenceOnlyOnSignificantChange()
    {
        var existing = IcsDocument.TryLoad(Ics.FromPhone())!;   // X-APPLE-…, VALARM ACTION:EMAIL, override
        var same = IcsComposer.RewriteAll(existing, WriteMatching(existing), Now);
        Assert.True(IcsComposer.SameContent(existing, IcsDocument.TryLoad(same)!));
        var retitled = IcsDocument.TryLoad(IcsComposer.RewriteAll(existing, WriteMatching(existing) with { Summary = "Renamed" }, Now))!;
        Assert.Contains("X-APPLE-TRAVEL-ADVISORY-BEHAVIOR", IcsDocument.Serialize(retitled));
        Assert.Contains("ACTION:EMAIL", IcsDocument.Serialize(retitled));
        Assert.Equal(IcsDocument.MasterOf(existing)!.Sequence, IcsDocument.MasterOf(retitled)!.Sequence);        // titre : pas significatif
        var moved = IcsDocument.TryLoad(IcsComposer.RewriteAll(existing, WriteMatching(existing) with { Start = Local(2026, 9, 7, 10) }, Now))!;
        Assert.Equal(IcsDocument.MasterOf(existing)!.Sequence + 1, IcsDocument.MasterOf(moved)!.Sequence);        // début : significatif
        Assert.Equal(Now, IcsDocument.MasterOf(moved)!.LastModified!.AsUtc);
    }

    [Fact]
    public void RewriteOne_WritesRecurrenceIdInMasterForm()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY"))!;
        var text = IcsComposer.RewriteOne(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 14, 11), End = Local(2026, 9, 14, 12) }, Now);
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Brussels:20260914T090000", text);
        var occurrences = OccurrenceExpander.Expand(IcsDocument.TryLoad(text)!, new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), "Europe/Brussels", "Europe/Brussels");
        Assert.Equal(9, Assert.Single(occurrences).StartUtc!.Value.Hour);  // une seule, à 11:00 Bruxelles
    }

    [Fact]
    public void RewriteOne_OnAllDay_WritesDateForm() =>
        Assert.Contains("RECURRENCE-ID;VALUE=DATE:20260914", IcsComposer.RewriteOne(IcsDocument.TryLoad(Ics.AllDayWeekly())!, "20260914", WriteAllDay(new DateOnly(2026, 9, 15)), Now));

    [Fact]
    public void RemoveOne_AddsExdate_AndDropsTheOverride()
    {
        var text = IcsComposer.RemoveOne(IcsDocument.TryLoad(Ics.WeeklyWithExdateAndOverride())!, "20260914T090000", Now);
        Assert.Contains("EXDATE;TZID=Europe/Brussels:20260914T090000", text);
        Assert.Single(IcsDocument.TryLoad(text)!.Events);
    }

    [Fact]
    public void Split_UntilIsTheInstantBefore_InUtc_CountIsCarried()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY;COUNT=10"))!;   // 7 sept 09:00 Bruxelles, hebdo
        var outcome = IcsComposer.Split(existing, "20260928T090000", WriteMatching(existing), "u2", Now);
        Assert.Contains("RRULE:FREQ=WEEKLY;UNTIL=20260928T065959Z", outcome.Original);   // l'instant avant 07:00Z, jamais la veille
        Assert.DoesNotContain("COUNT", outcome.Original);
        Assert.Contains("RRULE:FREQ=WEEKLY;COUNT=7", outcome.Following);                // 3 produites avant la coupe
        Assert.Contains("UID:u2", outcome.Following);
        Assert.Contains("DTSTART;TZID=Europe/Brussels:20260928T090000", outcome.Following);
        Assert.False(outcome.DroppedExceptions);
    }

    [Fact]
    public void Split_AllDay_UntilIsTheDayBefore() =>
        Assert.Contains("UNTIL=20260913", IcsComposer.Split(IcsDocument.TryLoad(Ics.AllDayWeekly())!, "20260914", WriteAllDay(new DateOnly(2026, 9, 14)), "u2", Now).Original);

    [Fact]
    public void Split_MovesLaterExceptions_RebasesTimeOnlyChange_DropsDayChange()
    {
        var existing = IcsDocument.TryLoad(Ics.WeeklyWithExdateAndOverride())!;  // override le 14, EXDATE le 21
        var timeOnly = IcsComposer.Split(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 14, 10), End = Local(2026, 9, 14, 11) }, "u2", Now);
        Assert.Contains("EXDATE;TZID=Europe/Brussels:20260921T100000", timeOnly.Following);      // rebasé à la nouvelle heure
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Brussels:20260914T100000", timeOnly.Following);
        Assert.DoesNotContain("EXDATE", timeOnly.Original);
        Assert.False(timeOnly.DroppedExceptions);

        var dayChange = IcsComposer.Split(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 15, 9), End = Local(2026, 9, 15, 10), Repeat = Weekly("TU") }, "u2", Now);
        Assert.True(dayChange.DroppedExceptions);
        Assert.Single(IcsDocument.TryLoad(dayChange.Following)!.Events);
        Assert.Contains("BYDAY=TU", dayChange.Following);
    }

    [Fact]
    public void Canonical_IgnoresStampsAndFormatting()
    {
        var a = IcsDocument.TryLoad(Ics.FromPhone())!;
        var b = IcsDocument.TryLoad(Ics.FromPhone().Replace("DTSTAMP:20260901T080000Z", "DTSTAMP:20260904T100000Z").Replace("\r\n", "\n"))!;
        Assert.True(IcsComposer.SameContent(a, b));
    }

    // Helpers : Write(...), WriteMatching(Calendar) (relit le maître en EventWrite), WriteAllDay(DateOnly), Weekly(string byDay = "MO"), Local(...), Compose(...)
}
```

- [ ] **Step 2 : implémenter** — règles à respecter à la lettre (décisions 4 et 5) :
  - `ComposeNew` : `PRODID:-//weesky//webmail//EN`, `VERSION:2.0`, un `VTIMEZONE` par `TZID` cité (`IcsTimeZones.Emit`, earliest = DTSTART − 1 an), `UID`, `DTSTAMP`, `CREATED`, `LAST-MODIFIED`, `SEQUENCE:0`, `DTSTART;TZID=…` en heure locale, `DTEND` (exclusif +1 jour pour une date), `RRULE` depuis `RecurrenceWrite` (`UNTIL` en UTC dès qu'il y a un `TZID`, en `DATE` pour une journée entière ; `COUNT` xor `UNTIL`), `VALARM ACTION:DISPLAY;TRIGGER:-PT{n}M`, `STATUS`/`TRANSP`/`CLASS`/`URL` selon la table de la fonctionnalité 3, journée entière **Libre par défaut** (`TRANSP:TRANSPARENT`) sauf `Availability.Busy` explicite.
  - `RewriteAll` : modifie l'objet chargé, jamais un nouveau ; ne touche que les propriétés que `EventWrite` porte ; **retire** `STATUS:CANCELLED` quand on enregistre (fonctionnalité 3) ; remplace les `VALARM ACTION:DISPLAY` à `TRIGGER` relatif au début par ceux de `ReminderMinutesBefore` et conserve tous les autres ; puis `Stamp(master, nowUtc, significant)` : `DTSTAMP` et `LAST-MODIFIED` = now ; `SEQUENCE++` si début, fin, règle, `RDATE`/`EXDATE` ou `STATUS` ont changé.
  - `RewriteOne` : `RECURRENCE-ID` dans la **forme du `DTSTART` maître** (`VALUE=DATE` / même `TZID`), jamais UTC ; le nouvel override copie le maître puis applique `EventWrite` ; un override existant du même `instanceId` est remplacé.
  - `Split` : `UNTIL` = instant qui précède l'occurrence choisie (une seconde avant, en UTC ; la veille en `DATE`) ; `COUNT` → reliquat = `COUNT − (occurrences produites par la règle avant la coupe, EXDATE compris, RDATE non)` ; `DTSTART` de la suite = **valeur saisie** ; `BYDAY` recalculé si le jour change sur un `WEEKLY` ; exceptions postérieures : rebasées (même date, nouvelle heure) si seule l'heure a changé, abandonnées sinon (`DroppedExceptions = true`) ; les antérieures restent à l'original ; `VTIMEZONE` copiés dans la suite.
  - `Canonical` : sérialisation d'une copie sans `DTSTAMP`, `LAST-MODIFIED`, `SEQUENCE`, `CREATED`, avec les propriétés de chaque composant **triées** par nom (l'ordre d'Ical.Net n'est pas garanti stable entre deux chargements) et les fins de ligne normalisées.

- [ ] **Step 3 : vert, commit** — `feat(calendar): le composeur ecrit les quatre gestes du webmail et la forme canonique`

---

### Task 5 : les stores — `CalendarStore`, `CalendarEventStore`, `CalendarSyncStore`, balayeur

**Files :**
- Create : `src/snoopy.microservice/Repositories/ICalendarStore.cs`, `CalendarStore.cs`
- Create : `src/snoopy.microservice/Repositories/ICalendarEventStore.cs`, `CalendarEventStore.cs`
- Create : `src/snoopy.microservice/Repositories/ICalendarSyncStore.cs`, `CalendarSyncStore.cs`
- Create : `src/snoopy.microservice/Services/CalendarTombstoneSweeper.cs`
- Create : `src/snoopy.microservice/Models/Calendar/` — `CalendarView.cs`, `CalendarWrite.cs`, `EventDetail.cs`, `CalendarImportOutcome.cs`, `CalendarPalette.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs` (`AddRepositories`, `AddMailServices`)
- Test : `…/Tests/Repositories/CalendarStoreTests.cs`, `CalendarEventStoreTests.cs`, `CalendarEventStoreImportTests.cs`, `CalendarSyncStoreTests.cs`, `…/Tests/Services/CalendarTombstoneSweeperTests.cs`, `…/Tests/Fixtures/CalendarStoreTestFactory.cs`

**Interfaces (produit pour les tâches suivantes) :**
```csharp
internal sealed record CalendarView(Guid Id, string DavName, string DisplayName, string Description, string Color, int Order, string TimeZone, bool IsVisible, bool IsDefault);
internal sealed record CalendarWrite(string DisplayName, string? Description, string? Color, int? Order);
internal interface ICalendarStore
{
    Task<IReadOnlyList<CalendarView>> ListAsync(Guid userId, CancellationToken ct);
    /// Crée `default` s'il manque, avec browserTimeZone (décision 6). Idempotent.
    Task<CalendarView> EnsureDefaultAsync(Guid userId, string browserTimeZone, CancellationToken ct);
    Task<Result<Guid>> CreateAsync(Guid userId, CalendarWrite write, string browserTimeZone, CancellationToken ct);   // dav_name = id, couleur suivante de la palette, ordre dernier
    Task<Result> UpdateAsync(Guid userId, Guid calendarId, CalendarWrite write, CancellationToken ct);                 // n'avance ni ctag ni séquence
    Task<Result> SetVisibleAsync(Guid userId, Guid calendarId, bool visible, CancellationToken ct);
    Task<Result> DeleteAsync(Guid userId, Guid calendarId, CancellationToken ct);   // refuse default ; archive par lots de 100 ; supprime état et tombes
}
// CalendarStore (la classe qui implémente ICalendarStore) porte les constantes : MaxPerUser = 20, DefaultDavName = "default", DeleteBatch = 100,
// CapReached, NotDeletable ("The default calendar cannot be deleted"), NotFound — toutes internal, comme ContactStore.

internal sealed record EventDetail(Guid Id, Guid CalendarId, string Uid, string IcsHash, EventWrite Fields, string? RecurrenceText, IReadOnlyList<AttendeeProjection> Attendees, string? Status);
internal sealed record CalendarImportOutcome(int Created, int Replaced, int IgnoredTodos, int IgnoredJournals, int Failed, IReadOnlyList<ContactImportError> Errors);
internal interface ICalendarEventStore
{
    Task<IReadOnlyList<EventOccurrence>> WindowAsync(Guid userId, DateTime fromUtc, DateTime toUtc, string viewTimeZone, CancellationToken ct);  // tous les agendas visibles ou non : le client filtre par calendarId (chaque occurrence porte EventId + CalendarId)
    Task<EventDetail?> GetAsync(Guid userId, Guid eventId, CancellationToken ct);
    Task<Result<Guid>> CreateAsync(Guid userId, EventWrite write, CancellationToken ct);
    Task<Result> UpdateAsync(Guid userId, Guid eventId, EditScope scope, string? instanceId, EventWrite write, string? ifHash, CancellationToken ct);
    Task<Result> DeleteAsync(Guid userId, Guid eventId, EditScope scope, string? instanceId, CancellationToken ct);
    Task<IReadOnlyList<EventOccurrence>> SearchAsync(Guid userId, string text, CancellationToken ct);    // fonctionnalité 5 : un résultat par événement, à sa prochaine occurrence
    Task<CalendarImportOutcome> ImportAsync(Guid userId, Guid calendarId, string vcalendar, CancellationToken ct);
    Task<string> ExportAsync(Guid userId, Guid calendarId, CancellationToken ct);                          // un VCALENDAR, VTIMEZONE dédupliqués, NAME/COLOR/X-WR-CALNAME
}
// CalendarEventStore porte : MaxPerCalendar = 5000, MaxImportBytes = 20 * 1024 * 1024, CapReached, NotFound,
// EventMoved ("The event changed since it was read. Reload it and try again."), SearchLimit = 200.

internal interface ICalendarSyncStore   // le jumeau par agenda d'IContactSyncStore
{
    Task<ulong> NextSequenceAsync(Guid calendarId, CancellationToken ct);               // à appeler EN PREMIER dans la transaction
    Task CreateStateAsync(Guid calendarId, CancellationToken ct);                        // dans la transaction de création de l'agenda (décision 2)
    Task<SyncState?> ReadStateAsync(Guid calendarId, CancellationToken ct);
    Task PlaceTombstoneAsync(Guid calendarId, string davName, ulong rank, CancellationToken ct);
    Task LiftTombstoneAsync(Guid calendarId, string davName, CancellationToken ct);
    Task ArchiveAsync(Guid userId, Guid? calendarId, Guid? eventId, string? uid, string? davName, string icsRaw, RevisionCause cause, CancellationToken ct);
    Task<PruneOutcome> PruneAsync(DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken ct);
}
```
`EventOccurrence` gagne deux champs en tête pour ce store : `Guid EventId, Guid CalendarId` (mettre à jour le record de la tâche 3 et ses tests ; l'expander les reçoit en paramètre).

- [ ] **Step 1 : tests des agendas (échouent)**

```csharp
public sealed class CalendarStoreTests
{
    [Fact]
    public async Task EnsureDefault_CreatesOnce_WithBrowserZone_AndItsState()
    {
        var db = nameof(EnsureDefault_CreatesOnce_WithBrowserZone_AndItsState); var user = Guid.NewGuid();
        var first = await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", CancellationToken.None);
        var again = await Store(db).EnsureDefaultAsync(user, "America/New_York", CancellationToken.None);
        Assert.Equal(first.Id, again.Id); Assert.Equal("Europe/Brussels", again.TimeZone); Assert.True(again.IsDefault);
        Assert.NotNull(await new PreferencesTestDbContext(db).CalendarSyncStates.FindAsync(first.Id));
    }

    [Fact]
    public async Task Create_TakesNextPaletteColour_LastOrder_IdAsDavName()
    {
        var db = nameof(Create_TakesNextPaletteColour_LastOrder_IdAsDavName); var user = Guid.NewGuid();
        await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", CancellationToken.None);
        var id = (await Store(db).CreateAsync(user, new CalendarWrite("Work", null, null, null), "Europe/Brussels", CancellationToken.None)).Value;
        var view = (await Store(db).ListAsync(user, CancellationToken.None)).Single(c => c.Id == id);
        Assert.Equal(id.ToString(), view.DavName); Assert.Equal(CalendarPalette.Colours[1], view.Color); Assert.Equal(1, view.Order);
    }

    [Fact]
    public async Task Create_RefusesTheTwentyFirst()
    {
        var db = nameof(Create_RefusesTheTwentyFirst); var user = Guid.NewGuid();
        await Store(db).EnsureDefaultAsync(user, "Europe/Brussels", CancellationToken.None);
        for (var i = 1; i < CalendarStore.MaxPerUser; i++)
            Assert.True((await Store(db).CreateAsync(user, new CalendarWrite($"c{i}", null, null, null), "Europe/Brussels", CancellationToken.None)).IsSuccess);
        var refused = await Store(db).CreateAsync(user, new CalendarWrite("one too many", null, null, null), "Europe/Brussels", CancellationToken.None);
        Assert.Equal(CalendarStore.CapReached, refused.Error);
    }

    [Fact]
    public async Task Delete_RefusesDefault_ArchivesEventsInBatches_RemovesStateAndTombstones() { /* 250 événements → 250 révisions cause delete, 3 rangs, plus d'état ni de tombe ; default → échec NotDeletable */ }

    [Fact]
    public async Task SetVisible_DoesNotTouchTheSequence() { /* seq lu avant/après identique */ }

    private static CalendarStore Store(string db) => CalendarStoreTestFactory.Calendars(db);
}
```

- [ ] **Step 2 : tests des événements (échouent)**

```csharp
public sealed class CalendarEventStoreTests
{
    [Fact]
    public async Task Create_ProjectsColumns_HashesAndRanks_UidIsId()
    {
        var (db, user, cal) = await Seed(nameof(Create_ProjectsColumns_HashesAndRanks_UidIsId));
        var id = (await Events(db).CreateAsync(user, Write(cal, start: Local(2026, 9, 7, 9)), CancellationToken.None)).Value;
        var row = await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync();
        Assert.Equal(id.ToString(), row.Uid); Assert.Equal($"{id}.ics", row.DavName);
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), row.StartsAt);
        Assert.Equal(IcsDocument.HashOf(row.IcsRaw), row.IcsHash); Assert.Equal(1UL, row.SyncSequence);
    }

    [Fact]
    public async Task Update_All_ArchivesWebmailRevision_AdvancesRank_SkipsWhenNothingChanged()
    {
        var (db, user, cal) = await Seed(nameof(Update_All_ArchivesWebmailRevision_AdvancesRank_SkipsWhenNothingChanged));
        var id = (await Events(db).CreateAsync(user, Write(cal), CancellationToken.None)).Value;
        var before = (await Events(db).GetAsync(user, id, CancellationToken.None))!;
        Assert.True((await Events(db).UpdateAsync(user, id, EditScope.All, null, before.Fields, before.IcsHash, CancellationToken.None)).IsSuccess);
        Assert.Equal(before.IcsHash, (await Events(db).GetAsync(user, id, CancellationToken.None))!.IcsHash);           // rien n'a bougé
        Assert.Empty(new PreferencesTestDbContext(db).CalendarRevisions);
        Assert.True((await Events(db).UpdateAsync(user, id, EditScope.All, null, before.Fields with { Summary = "Renamed" }, before.IcsHash, CancellationToken.None)).IsSuccess);
        Assert.Equal(RevisionCause.Webmail, (await new PreferencesTestDbContext(db).CalendarRevisions.SingleAsync()).Cause);
        Assert.Equal(2UL, (await new PreferencesTestDbContext(db).CalendarEvents.SingleAsync()).SyncSequence);
    }

    [Fact]
    public async Task Update_WithStaleHash_IsRefusedAsMoved() { /* ifHash faux → Result.Failure(CalendarEventStore.EventMoved), aucune révision */ }

    [Fact]
    public async Task Update_ThisOnly_ThenDelete_ThisOnly() { /* hebdo ; scope This sur 14/09 → deux VEVENT ; DeleteAsync This sur 14/09 → EXDATE et un seul VEVENT */ }

    [Fact]
    public async Task Update_ThisAndFollowing_CreatesASecondRow_InOneTransaction() { /* deux lignes, deux UID, même agenda, séquence avancée une fois de plus */ }

    [Fact]
    public async Task Update_ToAnotherCalendar_TombstonesOld_RanksNew() { /* tombe (oldCal, davName) au rang de l'ancien ; ligne portée par le nouveau rang ; UID et dav_name inchangés */ }

    [Fact]
    public async Task Delete_All_ArchivesDelete_AndTombstones() { /* révision cause delete avec EventId null, tombe posée */ }

    [Fact]
    public async Task Window_UsesColumnsToPreselect_ThenExpands() { /* 3 événements : dedans, dehors, récurrent infini né avant → 2 rendus ; le journée-entière du bord rendu avec la marge */ }

    [Fact]
    public async Task Create_RefusesDensity_AndCap() { /* FREQ=MINUTELY → MaxInstances ; 5000e + 1 → CapReached */ }

    [Fact]
    public async Task Search_OneResultPerEvent_AtNextOccurrence() { /* hebdo « Standup » → une occurrence, la prochaine après now */ }
}
```

- [ ] **Step 3 : tests d'import/export (échouent)**

```csharp
public sealed class CalendarEventStoreImportTests
{
    [Fact]
    public async Task Import_GroupsByUid_ReplacesExisting_IgnoresTodos_CountsFailures()
    {
        var (db, user, cal) = await Seed(nameof(Import_GroupsByUid_ReplacesExisting_IgnoresTodos_CountsFailures));
        var first = await Events(db).ImportAsync(user, cal, Ics.GoogleLikeExport(), CancellationToken.None);   // 2 UID, dont un hebdo avec 2 overrides, 1 VTODO, 1 VEVENT sans DTSTART
        Assert.Equal((2, 0, 1, 0, 1), (first.Created, first.Replaced, first.IgnoredTodos, first.IgnoredJournals, first.Failed));
        var again = await Events(db).ImportAsync(user, cal, Ics.GoogleLikeExport(), CancellationToken.None);
        Assert.Equal((0, 2), (again.Created, again.Replaced));
        Assert.Equal(2, await new PreferencesTestDbContext(db).CalendarRevisions.CountAsync(r => r.Cause == RevisionCause.Import));
    }

    [Fact]
    public async Task Import_InsertsMissingUid_AndCapsAtTwentyMegabytes() { /* VEVENT sans UID → UID inséré ; > MaxImportBytes → Result failure avant tout parse */ }

    [Fact]
    public async Task Export_IsOneVcalendar_WithDedupedZones_AndCalendarName()
    {
        var (db, user, cal) = await Seed(nameof(Export_IsOneVcalendar_WithDedupedZones_AndCalendarName));
        await Events(db).CreateAsync(user, Write(cal), CancellationToken.None); await Events(db).CreateAsync(user, Write(cal), CancellationToken.None);
        var text = await Events(db).ExportAsync(user, cal, CancellationToken.None);
        Assert.Equal(1, Regex.Matches(text, "BEGIN:VTIMEZONE").Count);
        Assert.Contains("X-WR-CALNAME:Personal", text); Assert.Contains("NAME:Personal", text); Assert.Contains("COLOR:", text);
        var reimported = await Events(db).ImportAsync(user, cal, text, CancellationToken.None);
        Assert.Equal((0, 2, 0), (reimported.Created, reimported.Replaced, reimported.Failed));
    }
}
```

- [ ] **Step 4 : tests du sync store et du balayeur** — copier `ContactSyncStoreTests` et `ContactTombstoneSweeperTests` en les clés par `calendarId` : `NextSequence` monotone par agenda et indépendant entre deux agendas ; tombe `INSERT … ON DUPLICATE KEY` (deux suppressions du même nom) ; `PruneAsync` pose `pruned_below` **puis** supprime, dans une transaction (4c décision 8) ; balayeur quotidien, 180 jours de tombes, 30 jours de révisions.

- [ ] **Step 5 : implémenter** — mêmes gabarits que `ContactStore`/`ContactSyncStore` : `InTransactionAsync` (copier tel quel), `NextSequenceAsync` **premier** dans chaque transaction, `ApplyIcsAsync(row, ics, rank)` unique point d'écriture d'`ics_raw` (hash, projection via `IcsProjector.Project(parsed, calendar.TimeZone)`, purge/réinsertion des `calendar_attendees`, `dav_name ??= $"{id}.ics"`), refus par `IcsGuards` avant toute écriture et journalisation `LogWarning` d'un `TimeZone` inconnu (« fuseau {Tzid} inconnu, heure traitée comme flottante »). Chemins à ne pas rater :
  - `UpdateAsync` : `ifHash` ≠ `IcsHash` → `EventMoved` ; `SameContent` → succès sans écriture ; `ThisAndFollowing` → `Split`, l'original réécrit et la suite créée sous `Guid.NewGuid()`, **même transaction, même rang** ; `write.CalendarId` ≠ `row.CalendarId` → tombe sur l'ancien agenda au rang de **son** `NextSequenceAsync`, ligne portée sur le nouveau au rang du **sien**, `dav_name` renommé seulement en cas de collision (décision 2), tout dans une transaction.
  - `DeleteAsync(This)` → `RemoveOne` puis `ApplyIcsAsync` ; `DeleteAsync(ThisAndFollowing)` → `Split` puis on ne garde que l'original ; `DeleteAsync(All)` → archive `Delete` (`EventId = null`), retire, tombe.
  - `WindowAsync` : `WHERE user_id = @u AND first_occurrence < @to + 1j AND last_occurrence > @from − 1j`, puis `OccurrenceExpander.Expand` par ligne avec le fuseau **de son agenda** ; une ligne qui n'expanse pas est journalisée et sautée.
  - `SearchAsync` : `LIKE` sur `summary`, `location`, `description` (paramétré, échappé), puis pour chaque ligne la prochaine occurrence après `UtcNow` (ou la dernière si finie) via une expansion bornée à `[now, last_occurrence]` (première seulement) ; borné à 200 résultats.
  - `ImportAsync` : `IcsResources.Split` ; par ressource : `UID` inséré s'il manque (comme `ContactStore.WithUid`, par insertion textuelle **avant** parse), `IcsGuards`, puis création ou **remplacement entier** du `UID` existant (révision `Import` de l'ancien), par lots de 100 sous un même rang ; les plafonds : 20 Mo (avant tout parse), 5000 par agenda, densité.
  - `ExportAsync` : un `Calendar` Ical.Net neuf, `NAME`/`COLOR`/`X-WR-CALNAME`/`X-APPLE-CALENDAR-COLOR`, `VTIMEZONE` ajoutés une fois par `TZID`, tous les composants de chaque ressource.
  - `CalendarPalette.Colours` : dix couleurs `#RRGGBB`, la première `#3b82c4` (celle des maquettes) ; « suivante » = celle de rang `count % 10`.
  - DI : `AddScoped<CalendarStore>()` + `AddScoped<ICalendarStore>(p => p.GetRequiredService<CalendarStore>())`, idem `CalendarEventStore`, `AddScoped<ICalendarSyncStore, CalendarSyncStore>()`, `AddHostedService<CalendarTombstoneSweeper>()`.

- [ ] **Step 6 : vert, commit** — `feat(calendar): stores des agendas et des evenements, synchro par agenda, balayeur`

---

### Task 6 : l'API — `CalendarsController`, `CalendarEventsController`, contrat frontend, docs

**Files :**
- Create : `src/snoopy.microservice/Controllers/CalendarsController.cs`
- Create : `src/snoopy.microservice/Controllers/CalendarEventsController.cs`
- Create : `src/snoopy.microservice/Models/Calendar/` — `CalendarRequest.cs`, `CalendarVisibleRequest.cs`, `EventRequest.cs`, `EventUpdateRequest.cs`, `EventResponse.cs`, `OccurrenceResponse.cs`, `CalendarImportReport.cs`, `EventRequestValidator.cs`
- Create : `src/frontend/src/modules/calendar/calendarTypes.ts`
- Modify : `src/frontend/src/api.js` (bloc `calendar` après `contacts`)
- Create : `docs/superpowers/calendar-5a-residuals.md`
- Test : `…/Tests/Controllers/CalendarsControllerTests.cs`, `CalendarEventsControllerTests.cs`, `…/Tests/Models/EventRequestValidatorTests.cs`

**Interfaces (produit pour 5b) — la surface HTTP :**
```
GET    /api/Calendars?tz=Europe/Brussels          → { calendars: CalendarView[] }   (crée default au passage, décision 6 ; tz obligatoire)
POST   /api/Calendars?tz=…        { displayName, description?, color?, order? } → 201 { id }   · 400 CapReached
PUT    /api/Calendars/{id}        { displayName, description?, color?, order? } → 204 · 404
PUT    /api/Calendars/{id}/Visible { visible } → 204
DELETE /api/Calendars/{id}        → 204 · 400 (default) · 404
GET    /api/Calendars/{id}/Export → text/calendar, "<displayName>-<yyyy-MM-dd>.ics"
POST   /api/Calendars/{id}/Import (multipart file, 20 Mo) → CalendarImportReport

GET    /api/Calendar/Events?from=…Z&to=…Z&tz=…   → { occurrences: OccurrenceResponse[] }  · 400 si to − from > 5 ans, si tz inconnu, si from ≥ to
GET    /api/Calendar/Events/Search?q=…            → { occurrences: OccurrenceResponse[] }
GET    /api/Calendar/Events/{id}                  → EventResponse (EventDetail + icsHash)
POST   /api/Calendar/Events                       EventRequest → 201 { id } · 400 (validation, MaxInstances, CapReached)
PUT    /api/Calendar/Events/{id}                  EventUpdateRequest { scope, instanceId?, ifHash, ...EventRequest } → 204 · 409 (EventMoved) · 404
DELETE /api/Calendar/Events/{id}?scope=&instanceId= → 204 · 404
```
`OccurrenceResponse` est `EventOccurrence` tel quel (camelCase, `WhenWritingNull`) ; `EventRequest` est une classe à propriétés `init` toutes optionnelles, validée par `EventRequestValidator.Validate(EventRequest) → Result<EventWrite>` avec **un** message clair par refus (« Start must be before end », « An all-day event needs startDate », « Unknown time zone », « Repeat: count and until are exclusive », « Reminder must be between 0 and 40320 minutes »).

- [ ] **Step 1 : tests du validateur (échouent)** — un `[Theory]` par message ci-dessus, plus le cas nominal daté et le cas nominal journée entière (fin incluse → `EndDateInclusive`).

- [ ] **Step 2 : tests des contrôleurs (échouent)** — sur le modèle exact de `ContactsControllerTests` (Moq du store, `ControllerTestHelpers.CreateAuthenticatedContext`, `Assert.IsType` exact) :

```csharp
[Fact] public async Task Window_RefusesMoreThanFiveYears() =>
    Assert.IsType<BadRequestObjectResult>((await Controller().Window(From, From.AddYears(5).AddDays(1), "Europe/Brussels", CancellationToken.None)).Result);
[Fact] public async Task Window_RefusesUnknownZone() => …("Nowhere/Land")…;
[Fact] public async Task Window_PassesZoneAndBoundsToTheStore() { /* Verify(WindowAsync(Uid, From, To, "Europe/Brussels", …)) ; 200 avec la liste */ }
[Fact] public async Task Update_MapsEventMovedTo409() { /* store → Result.Failure(CalendarEventStore.EventMoved) ⇒ ConflictObjectResult */ }
[Fact] public async Task Update_ThisNeedsInstanceId() { /* scope This sans instanceId ⇒ 400 */ }
[Fact] public async Task List_EnsuresDefaultWithTheBrowserZone() { /* Verify(EnsureDefaultAsync(Uid, "Europe/Brussels", …)) */ }
[Fact] public async Task Delete_DefaultIs400() { … }
[Fact] public async Task Import_RefusesWrongMediaType_And_Export_IsTextCalendar() { … }
```

- [ ] **Step 3 : implémenter** — `[Route("api/[controller]")]` pour `CalendarsController`, `[Route("api/Calendar/Events")]` explicite pour `CalendarEventsController` ; `[ProducesResponseType]` et bloc XML `<response>` par action ; `[RequestSizeLimit(CalendarEventStore.MaxImportBytes)]` sur l'import et `VCalendarMediaTypes = ["text/calendar", "application/ics"]` ; `FromResult` d'`ApiBaseController` pour les `Result`, `ConflictEnveloppe` pour `EventMoved`, `NotFoundEnveloppe` pour `NotFound` ; `tz` vérifié par `IcsTimeZones.IsKnownIana` avant d'appeler le store ; la fenêtre bornée à `OccurrenceExpander.MaxYears`.

- [ ] **Step 4 : le contrat frontend** — `calendarTypes.ts` : `Calendar`, `Occurrence` (les trois formes, champs optionnels, jamais `| null`), `EventDetail`, `EventWrite`, `RecurrenceWrite`, `EditScope`, `CalendarImportReport` ; `api.js` : `getCalendars(tz)`, `createCalendar(body, tz)`, `updateCalendar(id, body)`, `setCalendarVisible(id, visible)`, `deleteCalendar(id)`, `exportCalendar(id)` (blob), `importCalendar(id, file)` (FormData), `getOccurrences(from, to, tz)`, `searchEvents(q)`, `getEvent(id)`, `createEvent(body)`, `updateEvent(id, body)`, `deleteEvent(id, scope, instanceId)` — chaque entrée avec sa ligne de justification comme le bloc contacts.

- [ ] **Step 5 : docs** — `docs/superpowers/calendar-5a-residuals.md` sur le modèle de `contacts-4a-residuals.md` : « À traiter en 5b », « À traiter en 5c » (le routage `/dav` à généraliser, `caldav_enabled`, `IcsPrecondition` → codes XML), « Laissé tel quel, et pourquoi », « Ce que les sondes ont appris d'Ical.Net 5.2.3 » (repris des rapports des tâches 1 et 2, y compris le verdict `VTIMEZONE` et l'état de l'issue #455). Amender la spec : le § « Ce que 5a doit trancher en premier » reçoit une ligne « tranché » par point, avec le renvoi au fichier.

- [ ] **Step 6 : vert, commit** — `feat(calendar): API des agendas et des evenements, contrat frontend, residus 5a`

---

## Self-review (fait à l'écriture du plan)

**Couverture de la spec.** Décision 1 (fichier souverain, colonnes, première/dernière occurrence, date-butoir, exceptions sans maître, participants projetés, `UID` par agenda) → tâches 1, 2, 5. Décision 2 (agendas, `default`, palette, ordre, `is_visible`, plafond 20, changement d'agenda en une transaction, suppression par lots, révisions sans FK, état par agenda) → tâches 1, 5 ; `MKCALENDAR`/`PROPPATCH`/`DELETE` DAV → **5c**. Décision 3 → `IcsGuards.SupportedCalendarComponent` et l'import qui ignore et compte. Décision 4 (Ical.Net seul, TZDB, réécriture entière, forme canonique, estampilles, `RANGE`, `RRULE` malformée, `VTIMEZONE` écrits, `TZID` Windows, densité, fenêtre 5 ans) → tâches 1–4, 6. Décision 5 (formes d'occurrence, `tz`, `RECURRENCE-ID` littéral, `EXDATE`/`RDATE`, coupure en cinq points) → tâches 3, 4. Décision 6 (fuseau de l'agenda, `default` créé par le webmail avec le fuseau du navigateur) → tâches 5, 6. Décision 7 → projection lecture seule (tâche 2). Décision 8 → **5c** ; seule `IcsPrecondition` est posée ici pour que 5c la traduise. Fonctionnalités 5 (recherche) et 6 (import/export, regroupement par `UID`, remplacement, `NAME`/`COLOR`) → tâche 5. Fonctionnalités 1–4, 7, 8 et tout « L'interface » → **5b**.

**Trous assumés.** Pas de rattrapage : les tables sont neuves. Pas de `capabilities.calendar` : le module existe toujours. `caldav_enabled` : 5c.

**Cohérence des types.** `EventOccurrence` est défini en tâche 3 et étendu (`EventId`, `CalendarId`) en tâche 5 — l'extension est nommée dans les deux tâches. `EventWrite`, `EditScope`, `RecurrenceWrite` (tâche 4) sont ceux que `ICalendarEventStore` (5) et `EventRequestValidator` (6) consomment. `IcsProblem`/`IcsPrecondition` (2) sont ce que les stores (5) renvoient en `Result.Failure(problem.Message)` et que 5c traduira en XML. `ContactImportError` est réutilisé tel quel pour les erreurs d'import.

**Ordre.** 1 → 2 → 3 → 4 → 5 → 6, chaque tâche testable seule ; 3 et 4 ne dépendent que de 2 et pourraient se paralléliser si les sous-agents le permettent.
