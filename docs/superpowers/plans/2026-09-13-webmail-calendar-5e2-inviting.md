# Agenda 5e2 — inviter : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `5e2-task-N-…`.

**Spec :** [la conception 5e](../specs/2026-09-12-webmail-calendar-5e-invitations-design.md),
décisions 8 à 13 — toute décision citée ici (« décision N ») y renvoie, et elle fait foi en cas de
doute. Ce plan couvre **la phase 2** (inviter) ; la phase 1 (recevoir, décisions 1 à 7) est livrée
par [le plan 5e1](2026-09-12-webmail-calendar-5e1-invitations.md) et la PR #74.

**Goal :** un événement du webmail peut avoir des invités ; les invitations, mises à jour et
annulations partent par mail d'où que vienne la modification, webmail ou téléphone CalDAV ; les
réponses des invités sont lues à l'ouverture de leur mail et reportées dans l'agenda.

**Architecture :** deux portes écrivent dans l'agenda, l'API du webmail (`CalendarEventsController`
→ `CalendarEventStore`) et le dépôt CalDAV (`CalDavController` → `DavCalendarWriter`). Chacune
rapporte désormais ce qu'elle a changé — le fichier remplacé et son état d'ordonnancement, le
fichier écrit — et appelle `InvitationScheduler` après son écriture réussie. Le planificateur
compare une **empreinte** (ce qui compte pour un invité, sur tous les composants) à celle du
dernier envoi, décide qui reçoit quoi (`SchedulingDecider`, pur), fait avancer `SEQUENCE` par une
seconde écriture textuelle quand le client ne l'a pas fait, compose les mails
(`InvitationMailer`) et les remet par la session SMTP de l'utilisateur (webmail) ou par le compte
de service via une file en mémoire (téléphone). Un `REPLY` reçu est lu par le bloc `invitation`
et appliqué par un point d'entrée dédié.

**Tech stack :** .NET 10, ASP.NET Core, EF Core (InMemory en test), MailKit/MimeKit, Ical.Net
5.2.3, `System.Threading.Channels`, xUnit 2.9.3, Moq 4.20.72 ; React 18, TypeScript, TanStack
Query, react-i18next, Vitest + Testing Library.

## Global Constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont
  ajoutés). Frontend : `cd src/frontend && npm run lint && npm run typecheck && npm test`.
- `src/snoopy.microservice/ApiDocumentation.xml` est régénéré par `dotnet test` : le réverter
  (`git checkout -- src/snoopy.microservice/ApiDocumentation.xml`) avant chaque commit.
- Messages de commit : deux lignes max, jamais de `@` en début ou fin ; passer le message par
  un heredoc `git commit -F -`, jamais par une here-string PowerShell.
- Dépôt en `autocrlf=true` : Edit/Write écrivent du LF, c'est attendu ; ne pas « corriger » les
  fins de ligne. Un test ne fige jamais une fin de ligne (`\r\n` vs `\n`) : il compare des
  lignes logiques.
- Typographie française dans les locales `fr` : espace insécable (U+00A0) avant `:` `;` `!` `?`
  et à l'intérieur des guillemets « ». Vérifier par `npm test -- keys`.
- Parité FR/EN des clés : `src/frontend/src/locales/parity.test.ts` doit rester vert. Une clé
  n'atteint jamais `t()` par une variable (`locales/keys.test.ts` ne la verrait pas).
- L'API omet les champs `null` (`WhenWritingNull`) : côté TypeScript un champ nullable est
  déclaré optionnel (`?`), jamais `| null`, et une fixture l'omet.
- Les enums du backend sortent en JSON par un `JsonStringEnumConverter` **sans** politique de
  casse : `Request`, `Cancel`, `Reply`, `Absent`… tels que nommés en C#.
- **Le crochet ne fait jamais échouer ni attendre une écriture** (décision 10) : `InvitationScheduler`
  attrape tout, journalise, et l'envoi hors session part par la file. Un `PUT` CalDAV répond
  `201`/`204` même SMTP en panne.
- **Deux portes seulement** appellent le crochet, `CalendarEventsController` et
  `CalDavController` (décision 9) ; ni `ImportAsync`, ni `Respond`, ni `ApplyReply`, ni la
  suppression d'un agenda entier.
- Le fichier stocké est la vérité et n'est modifié par le serveur que **textuellement**
  (`SEQUENCE` d'un composant, `PARTSTAT` d'un `ATTENDEE`), jamais par une resérialisation
  Ical.Net (décision 4 étendue).
- Le compte de service (`Scheduling:Smtp:*`) ne vit que dans la configuration locale
  (`dotnet user-secrets` du projet host, ou `appsettings.*.local.json` ignoré par git) ; le
  dépôt ne porte que des chaînes vides.
- Les identifiants de l'utilisateur ne descendent jamais dans la couche base : le contrôleur
  résout la session, le planificateur la reçoit en paramètre.

## Ce que ce plan suppose fait

5e1 livrée (PR #74, `master` à `0cbe66d`), branche `caldav-inviting` créée depuis `master`.
Toutes les tâches sont exécutables hors ligne : IMAP et SMTP sont simulés par Moq, le compte de
service par une option vide (file désactivée). La recette réelle (Gmail, DAVx⁵) se fait en session
avec l'utilisateur après la tâche 8, comme 5e1.

## Écarts avec la lettre de la spec, arbitrés par ce plan

La spec décrit l'intention ; le code relevé le 13 septembre 2026 impose quelques ajustements de
forme. Chacun est un choix de ce plan, à contester à la relecture.

1. **Le contrôleur d'événements ne voit ni l'ancien ni le nouveau fichier.** Il écrit par
   `ICalendarEventStore` (`CreateAsync` → `Guid`, `UpdateAsync`/`DeleteAsync` → `Result` nu), pas
   par `IDavCalendarWriter`. Plutôt qu'un journal implicite, les trois méthodes d'écriture du
   store rendent un `EventWriteResult` : l'id et la liste des `EventChange` (agenda, nom DAV,
   version remplacée avec ses colonnes d'ordonnancement, fichier écrit). Une coupure « cette
   occurrence et les suivantes » en rapporte deux. Côté CalDAV, `DavWriteOutcome.Replaced` porte
   la même `ReplacedVersion` (décision 9), lue dans la transaction qui archive déjà.
2. **Le contrôleur d'événements n'a pas de session mail.** Il hérite d'`ApiBaseController`, pas
   de `MailControllerBase`. Il gagne `IAccountConnectionResolver` et résout le compte principal
   (aucun `X-Account-Id` n'est envoyé par l'agenda, décision 10). Si la résolution échoue
   (identifiants absents du cookie), les mails partent par le compte de service, journalisé.
3. **`SEQUENCE` et le composeur du webmail.** `IcsComposer.Shape` n'incrémente `SEQUENCE` que sur
   un changement de dates, de règle ou de statut ; un titre, un lieu ou un invité changés ne la
   bougent pas, alors que l'empreinte change. La règle « le serveur l'incrémente si le client ne
   l'a pas fait » (décision 9) s'applique donc **aux deux portes** de la même façon : une seconde
   écriture textuelle, uniquement quand un envoi précédent existe (`scheduling_hash` non nul).
   `IcsComposer.Shape` n'est pas touché : sa règle sert tous les événements, invités ou non.
4. **L'adresse de l'organisateur.** Pas de table `domains` à consulter : l'identité par défaut du
   compte principal, telle que `IdentityResolver.Resolve` la calcule (lignes stockées du compte
   principal, adresse principale, alias vivants), est par construction l'adresse principale ou un
   alias que le serveur héberge ; une identité d'un compte connecté (Gmail) n'y figure jamais.
   C'est la réserve de la décision 8, satisfaite sans nouvelle table.
5. **Le `CN` des invités** vient du client : le composeur du webmail connaît les contacts
   (`namesByAddressOf`), le serveur n'a aucune recherche par adresse. L'API accepte
   `attendees: [{ email, name? }]` et écrit le `CN` fourni.
6. **La langue des mails.** Depuis le webmail, la requête porte `language` comme `Respond` ;
   depuis un téléphone, la préférence `ui.language` de l'utilisateur (`user_preferences`), et
   l'anglais quand elle vaut `auto` ou manque.
7. **Le détail d'un événement dit s'il est invitable** : `canInvite` (aucun organisateur, ou
   l'organisateur est une adresse de l'utilisateur), calculé par le contrôleur avec la liste
   d'adresses qu'il possède déjà. Le frontend n'a pas cette liste.
8. **`PUT /api/Calendar/Events/{id}` répond `200`** avec `{ scheduling }` au lieu de `204`, pour
   porter le compte d'envois (décision « surface HTTP »). `DELETE` reste `204`.
9. **La cause d'archivage** de la seconde écriture est une valeur nouvelle, `RevisionCause.Scheduling` :
   ni un dépôt d'appareil ni un geste de l'utilisateur.

## Structure des fichiers

**Backend, créés**

| Fichier | Rôle |
|---|---|
| `Models/Calendar/AttendeeWrite.cs` | `AttendeeWrite(Email, Name)`, `OrganizerWrite(Email, Name)`, `AttendeeRequest` |
| `Models/Calendar/EventWriteResult.cs` | `ReplacedVersion`, `EventChange`, `EventWriteResult` |
| `Models/Calendar/SchedulingReport.cs` | `{ owner, sent }` rendu par `POST`/`PUT` ; `EventUpdated` |
| `Models/Calendar/SchedulingOptions.cs` | `Scheduling:Smtp:*`, délai de second essai |
| `Models/Calendar/ApplyReplyRequest.cs` | corps et réponse de `POST /api/Calendar/Invitations/ApplyReply` |
| `Models/Mail/InvitationReply.cs` | le sous-bloc `reply` du bloc `invitation` |
| `Services/Calendar/Scheduling/SchedulingShape.cs` | l'empreinte, tous composants ; les composants changés sans `SEQUENCE` avancée ; pur |
| `Services/Calendar/Scheduling/SchedulingDecider.cs` | avant/après → qui reçoit quoi ; pur |
| `Services/Calendar/Scheduling/SequenceRewriter.cs` | `SEQUENCE:n+1` textuel sur les composants nommés ; pur |
| `Services/Calendar/Scheduling/IcsMethod.cs` | pose ou remplace la ligne `METHOD` ; pur |
| `Services/Calendar/Scheduling/InvitationMailer.cs` | les `MimeMessage` `REQUEST`/`CANCEL`, double forme ; pur |
| `Services/Calendar/Scheduling/ServiceMailQueue.cs` | `IServiceMailQueue` : file en mémoire, un second essai, journal |
| `Services/Calendar/Scheduling/InvitationScheduler.cs` | `IInvitationScheduler` : le crochet |
| `Services/Calendar/Scheduling/OrganizerIdentity.cs` | `IOrganizerIdentity` : l'identité par défaut du compte principal |
| `Services/Calendar/Invitations/InvitationPartLoader.cs` | relit et décode une partie depuis IMAP (extrait du répondeur) |
| `Services/Calendar/Invitations/InvitationReplyApplier.cs` | `IInvitationReplyApplier` : applique un `REPLY` |
| `snoopy.microservice.Tests/Fixtures/Invitations/webmail-invited.ics` | un événement invité par le webmail (deux invités, `ORGANIZER` alice) |
| `snoopy.microservice.Tests/Fixtures/Invitations/webmail-invited-override.ics` | le même avec une surcharge déplacée |
| `snoopy.microservice.Tests/Fixtures/Invitations/outlook-reply-upper.ics` | un `REPLY` d'Outlook, adresse en majuscules |
| `snoopy.microservice.Tests/Fixtures/Invitations/google-reply-occurrence.ics` | un `REPLY` avec `RECURRENCE-ID` |

**Backend, modifiés**

| Fichier | Changement |
|---|---|
| `Data/Preferences/CalendarEvent.cs` | `scheduling_owner`, `scheduling_hash` |
| `Data/Preferences/RevisionCause.cs` | `Scheduling` |
| `docs/superpowers/webmail-calendar-tables.md` | les deux colonnes dans le DDL, l'`ALTER TABLE` |
| `Models/Calendar/EventRequest.cs`, `EventRequestValidator.cs` | `attendees`, `language` |
| `Models/Calendar/EventWrite.cs` | `Attendees`, `Organizer` |
| `Models/Calendar/EventDetail.cs`, `EventResponse.cs` | `CanInvite` |
| `Models/Calendar/StoredEventRef.cs` | `SchedulingOwner` |
| `Models/Calendar/CreatedId.cs` | `Scheduling` |
| `Models/Dav/DavWriteOutcome.cs` | `Replaced` |
| `Models/Mail/MailInvitation.cs` | `InvitationMethod.Reply`, `Reply` |
| `Services/Calendar/IcsComposer.cs` | `PlaceAttendees` |
| `Services/Calendar/Invitations/InvitationParser.cs`, `InvitationReader.cs`, `InvitationResponder.cs`, `InvitationText.cs` | `REPLY` lu ; partie chargée par `InvitationPartLoader` ; sujets et corps de l'organisateur |
| `Repositories/ICalendarEventStore.cs`, `CalendarEventStore.cs` | `EventWriteResult`, `SchedulingOfAsync`, `SetSchedulingAsync`, `FindByUidAsync` avec l'owner |
| `Repositories/DavCalendarWriter.cs` | remplit `Replaced` |
| `Controllers/CalendarEventsController.cs` | organisateur, `canInvite`, résolution du compte, crochet, `scheduling` |
| `Controllers/CalDavController.cs` | crochet après `PUT` et `DELETE` |
| `Controllers/CalendarInvitationsController.cs` | `ApplyReply` |
| `Configuration/ApplicationServicesConfiguration.cs` | options, enregistrements, service hébergé |
| `snoopy.microservice.host/appsettings.json` | la section `Scheduling` vide |

**Frontend, créés**

| Fichier | Rôle |
|---|---|
| `modules/calendar/AttendeesField.tsx` | puces + autocomplétion, sur `RecipientsField` |
| `modules/calendar/AttendeeStatusList.tsx` | les invités et leur pastille d'état |
| `modules/calendar/attendeeStatus.ts` | `guestAnswerOf(partStat, t)`, la classe de pastille |

**Frontend, modifiés**

| Fichier | Changement |
|---|---|
| `modules/calendar/calendarTypes.ts` | `AttendeeWrite`, `EventWrite.attendees`, `EventDetail.canInvite`, `SchedulingReport` |
| `modules/calendar/eventForm.ts` | `attendees`, `canInvite` ; `writeOf` |
| `modules/calendar/EventEditor.tsx` | le champ Invités sous Lieu ; la liste d'état ; retrait de `attendeesReadOnly` |
| `modules/calendar/EventPreview.tsx` | pastilles sur ses propres événements |
| `modules/mail/api/mailTypes.ts` | `'Reply'`, `InvitationReply`, `ApplyReplyResponse` |
| `api.js` | `applyInvitationReply` |
| `modules/mail/queries.ts` | `useApplyInvitationReply` |
| `modules/mail/reader/invitationText.ts`, `InvitationCard.tsx` | l'état `reply` |
| `styles/calendar.css`, `styles/mail.css` | `.attendee-dot…`, `.invitation-card-reply` |
| `locales/{fr,en}/calendar.json`, `mail.json` | clés |

---

### Task 1 : le schéma et le code pur — empreinte, décideur, `SEQUENCE`

**Files:**
- Modify: `src/snoopy.microservice/Data/Preferences/CalendarEvent.cs`
- Modify: `src/snoopy.microservice/Data/Preferences/RevisionCause.cs`
- Modify: `docs/superpowers/webmail-calendar-tables.md`
- Modify: `src/snoopy.microservice/Services/Calendar/Invitations/PartStatRewriter.cs` (`Fold` devient `internal`)
- Create: `src/snoopy.microservice/Services/Calendar/Scheduling/SchedulingShape.cs`
- Create: `src/snoopy.microservice/Services/Calendar/Scheduling/SchedulingDecider.cs`
- Create: `src/snoopy.microservice/Services/Calendar/Scheduling/SequenceRewriter.cs`
- Create: `src/snoopy.microservice/Services/Calendar/Scheduling/IcsMethod.cs`
- Create: `src/snoopy.microservice/snoopy.microservice.Tests/Fixtures/Invitations/webmail-invited.ics`, `webmail-invited-override.ics`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/Calendar/Scheduling/SchedulingShapeTests.cs`, `SchedulingDeciderTests.cs`, `SequenceRewriterTests.cs`, `IcsMethodTests.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Data/CalendarEntitiesTests.cs` (les deux colonnes)

**Interfaces:**
- Consumes : `IcsDocument.TryLoad/Components/InstanceIdOf/HashOf`, `IcsComposer.Shape(CalendarEvent)`,
  `IcsProjector.Address(Uri?)`, `PartStatRewriter.Unfold/Fold`, `InvitationParser.SequenceOf`.
- Produces (tout `internal`, namespace `weesky.Snoopy.Microservice.Services.Calendar.Scheduling`) :
  ```csharp
  enum WriteOrigin { Webmail, Device }
  enum MailKind { Invitation, Update, Cancellation }
  sealed record ScheduledMail(MailKind Kind, IReadOnlyList<string> Recipients);
  sealed record SchedulingBefore(string? Ics, string? Owner, string? Hash);
  sealed record SchedulingInput(SchedulingBefore Before, string? After, WriteOrigin Origin, IReadOnlySet<string> OwnAddresses);
  sealed record SchedulingDecision(IReadOnlyList<ScheduledMail> Mails, string? Owner, string? Hash);
  static class SchedulingDecider { const string WebmailOwner = "webmail"; SchedulingDecision Decide(SchedulingInput input); }
  static class SchedulingShape {
      string? Of(string ics, bool withAttendees = true);           // null si le fichier ne s'analyse pas
      string HashOf(string shape);                                  // SHA-256 hex minuscule
      IReadOnlySet<string> Addresses(string ics);                   // ATTENDEE de tous les composants, minuscules, sans mailto:
      string? OrganizerOf(string ics);                              // adresse du maître, minuscule
      IReadOnlyList<string> ChangedWithoutBump(string before, string after); // clés d'instance ("" = maître)
  }
  static class SequenceRewriter { string Bump(string ics, IReadOnlyCollection<string> instanceIds); }
  static class IcsMethod { string With(string ics, string method); }
  ```

**Pourquoi d'abord.** Tout le reste s'appuie sur ces quatre pièces pures ; elles se testent sans
base ni MIME, et c'est là que vit la table de la décision 9.

- [ ] **Step 1 : les deux colonnes**

`Data/Preferences/CalendarEvent.cs`, après `SyncSequence` :

```csharp
    /// <summary>"webmail" once the webmail has sent the first invitation for this event, else null
    /// — a device's or Thunderbird's own invitations are never doubled (spec 5e, décision 9).</summary>
    [Column("scheduling_owner")]
    [MaxLength(16)]
    public string? SchedulingOwner { get; set; }

    /// <summary>SHA-256 of the scheduling shape as it was at the last mail sent; null before any.</summary>
    [Column("scheduling_hash")]
    [MaxLength(64)]
    public string? SchedulingHash { get; set; }
```

`Data/Preferences/RevisionCause.cs`, dernière valeur :

```csharp
    /// <summary>The server advanced a component's SEQUENCE after a device wrote a change the
    /// invitees have to receive (spec 5e, décision 9) — neither a PUT nor the user's gesture.</summary>
    Scheduling
```

`docs/superpowers/webmail-calendar-tables.md` : dans le `CREATE TABLE calendar_events`, après la
ligne `sync_sequence` :

```sql
  `scheduling_owner` VARCHAR(16) NULL COMMENT '"webmail" quand le webmail a envoyé la première invitation (spec 5e, décision 9)',
  `scheduling_hash`  CHAR(64)    NULL COMMENT 'SHA-256 de l''empreinte au dernier envoi',
```

et une section après « Tranche 5e1 — un index » :

````markdown
## Tranche 5e2 — deux colonnes

Le planificateur d'invitations retient qui a invité (`webmail` ou personne) et l'empreinte du
dernier envoi (spec 5e, décision 9). Les révisions ne changent pas.

```sql
ALTER TABLE `calendar_events`
  ADD COLUMN `scheduling_owner` VARCHAR(16) NULL AFTER `sync_sequence`,
  ADD COLUMN `scheduling_hash`  CHAR(64)    NULL AFTER `scheduling_owner`;
```
````

Dans `Data/CalendarEntitiesTests.cs`, ajouter au test qui relit la table des colonnes de
`CalendarEvent` (chercher `calendar_events` dans le fichier) les deux noms `scheduling_owner` et
`scheduling_hash` avec leur longueur maximale ; puis
`dotnet test --filter FullyQualifiedName~CalendarEntitiesTests` doit être vert.

- [ ] **Step 2 : les fixtures**

`Fixtures/Invitations/webmail-invited.ics` (CRLF comme les autres fichiers du dossier ; `.gitattributes`
les déclare `eol=crlf`) :

```
BEGIN:VCALENDAR
PRODID:-//weesky//webmail//EN
VERSION:2.0
BEGIN:VEVENT
UID:web-1111-2222
DTSTAMP:20260913T090000Z
CREATED:20260913T090000Z
LAST-MODIFIED:20260913T090000Z
SEQUENCE:0
SUMMARY:Réunion de rentrée
LOCATION:Salle 2
DTSTART;TZID=Europe/Brussels:20261005T100000
DTEND;TZID=Europe/Brussels:20261005T110000
RRULE:FREQ=WEEKLY;COUNT=4
ORGANIZER;CN=Alice:mailto:alice@weesky.be
ATTENDEE;CN=Marc Dupont;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:marc.dupont@example.org
ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net
END:VEVENT
END:VCALENDAR
```

`webmail-invited-override.ics` : le même fichier, `SEQUENCE:1` sur le maître, plus un second
`VEVENT` après le premier :

```
BEGIN:VEVENT
UID:web-1111-2222
RECURRENCE-ID;TZID=Europe/Brussels:20261019T100000
DTSTAMP:20260913T100000Z
SEQUENCE:0
SUMMARY:Réunion de rentrée
LOCATION:Salle 2
DTSTART;TZID=Europe/Brussels:20261019T150000
DTEND;TZID=Europe/Brussels:20261019T160000
ORGANIZER;CN=Alice:mailto:alice@weesky.be
ATTENDEE;CN=Marc Dupont;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:marc.dupont@example.org
ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net
END:VEVENT
```

Vérifier que le `.csproj` de test copie `Fixtures/**` dans la sortie (c'est déjà le cas pour 5e1 :
`InvitationParserTests.Fixture` lit `AppContext.BaseDirectory/Fixtures/Invitations`).

- [ ] **Step 3 : les tests de `SchedulingShape` et `IcsMethod` (rouges)**

`Services/Calendar/Scheduling/SchedulingShapeTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Scheduling;

public class SchedulingShapeTests
{
    private static string Fixture(string name) => InvitationParserTests.Fixture(name);

    [Fact]
    public void Of_ChangesWhenOnlyAnOverrideMoves_WhileTheMasterDoesNot()
    {
        var series = Fixture("webmail-invited");
        var moved = Fixture("webmail-invited-override");

        Assert.NotEqual(SchedulingShape.Of(series), SchedulingShape.Of(moved));
        // The master's own line is identical in both: the difference is the override alone.
        Assert.StartsWith(SchedulingShape.Of(series)!.Split('\n')[0], SchedulingShape.Of(moved)!);
    }

    [Fact]
    public void Of_IgnoresAPartStat_AndTheCaseOfAnAddress()
    {
        var before = Fixture("webmail-invited");
        var answered = before.Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=DECLINED")
            .Replace("mailto:Julie@Example.net", "mailto:JULIE@EXAMPLE.NET");

        Assert.Equal(SchedulingShape.Of(before), SchedulingShape.Of(answered));
        Assert.Equal(SchedulingShape.HashOf(SchedulingShape.Of(before)!), SchedulingShape.HashOf(SchedulingShape.Of(answered)!));
    }

    [Fact]
    public void Of_ChangesOnTitle_Location_AndGuestList_ButNotOnDescription()
    {
        var before = Fixture("webmail-invited");
        var shape = SchedulingShape.Of(before);

        Assert.NotEqual(shape, SchedulingShape.Of(before.Replace("SUMMARY:Réunion de rentrée", "SUMMARY:Réunion")));
        Assert.NotEqual(shape, SchedulingShape.Of(before.Replace("LOCATION:Salle 2", "LOCATION:Salle 3")));
        Assert.NotEqual(shape, SchedulingShape.Of(before.Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n", "")));
        Assert.Equal(shape, SchedulingShape.Of(before.Replace("LOCATION:Salle 2", "LOCATION:Salle 2\r\nDESCRIPTION:Apportez le dossier")));
    }

    [Fact]
    public void Of_WithoutAttendees_SeesTheSameEvent_WhateverTheGuests()
    {
        var before = Fixture("webmail-invited");
        var fewer = before.Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n", "");

        Assert.Equal(SchedulingShape.Of(before, withAttendees: false), SchedulingShape.Of(fewer, withAttendees: false));
        Assert.Null(SchedulingShape.Of("not a calendar"));
    }

    [Fact]
    public void Addresses_AreEveryAttendee_Lowercased_WithoutMailto_AndTheOrganizerIsRead()
    {
        var addresses = SchedulingShape.Addresses(Fixture("webmail-invited-override"));

        Assert.Equal(new HashSet<string> { "marc.dupont@example.org", "julie@example.net" }, addresses);
        Assert.Equal("alice@weesky.be", SchedulingShape.OrganizerOf(Fixture("webmail-invited")));
        Assert.Null(SchedulingShape.OrganizerOf(Fixture("webmail-invited").Replace("ORGANIZER;CN=Alice:mailto:alice@weesky.be\r\n", "")));
    }

    // A client that moved a component without advancing its SEQUENCE: the master here (a new
    // title, SEQUENCE still 0); the override of the second fixture was written with its own
    // SEQUENCE:0 as a new component, which is not a lag — nothing was sent for it before.
    [Fact]
    public void ChangedWithoutBump_NamesTheComponentsWhoseShapeMoved_WithoutASequenceStep()
    {
        var before = Fixture("webmail-invited");
        var retitled = before.Replace("SUMMARY:Réunion de rentrée", "SUMMARY:Réunion");
        var retitledAndBumped = retitled.Replace("SEQUENCE:0", "SEQUENCE:1");

        Assert.Equal([""], SchedulingShape.ChangedWithoutBump(before, retitled));
        Assert.Empty(SchedulingShape.ChangedWithoutBump(before, retitledAndBumped));
        Assert.Empty(SchedulingShape.ChangedWithoutBump(before, Fixture("webmail-invited-override")));

        var overrideMovedAgain = Fixture("webmail-invited-override").Replace("DTSTART;TZID=Europe/Brussels:20261019T150000", "DTSTART;TZID=Europe/Brussels:20261019T160000");
        Assert.Equal(["20261019T100000"], SchedulingShape.ChangedWithoutBump(Fixture("webmail-invited-override"), overrideMovedAgain));
    }
}
```

`IcsMethodTests.cs` :

```csharp
public class IcsMethodTests
{
    [Fact]
    public void With_InsertsMethodAfterVersion_OrReplacesAnExistingOne_KeepingEveryOtherByte()
    {
        var stored = InvitationParserTests.Fixture("webmail-invited");
        var request = IcsMethod.With(stored, "REQUEST");

        var lines = request.Split("\r\n");
        Assert.Equal("VERSION:2.0", lines[2]);
        Assert.Equal("METHOD:REQUEST", lines[3]);
        Assert.Equal(stored.Replace("VERSION:2.0\r\n", "VERSION:2.0\r\nMETHOD:REQUEST\r\n"), request);

        var cancelled = IcsMethod.With(InvitationParserTests.Fixture("thunderbird-request"), "CANCEL");
        Assert.Single(cancelled.Split("\r\n"), l => l.StartsWith("METHOD:", StringComparison.Ordinal));
        Assert.Contains("METHOD:CANCEL\r\n", cancelled);
    }
}
```

Note : le fichier `InvitationParserTests` doit exposer `Fixture` en `internal static` (c'est déjà
le cas en 5e1) ; l'espace de noms des tests d'invitations est
`weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations` — vérifier le `namespace` réel en
tête de `InvitationParserTests.cs` et l'employer.

- [ ] **Step 4 : `dotnet test --filter FullyQualifiedName~Scheduling` échoue à la compilation** (types absents).

- [ ] **Step 5 : `IcsMethod` et `SchedulingShape`**

`Services/Calendar/Scheduling/IcsMethod.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>The one line an iTIP message adds to a stored file. Textual, so the file the
/// invitees receive is the file in base, byte for byte but for this line (décision 4).</summary>
internal static class IcsMethod
{
    internal static string With(string ics, string method)
    {
        var newline = ics.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = ics.Split(newline).Where(l => !l.StartsWith("METHOD:", StringComparison.OrdinalIgnoreCase)).ToList();
        var version = lines.FindIndex(l => l.StartsWith("VERSION:", StringComparison.OrdinalIgnoreCase));
        lines.Insert(version < 0 ? 1 : version + 1, "METHOD:" + method);
        return string.Join(newline, lines);
    }
}
```

`Services/Calendar/Scheduling/SchedulingShape.cs` :

```csharp
using Ical.Net.CalendarComponents;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>
/// What an invitee has to be told about: <see cref="IcsComposer.Shape"/> (dates, rule, status),
/// the title, the place and the guest list — on <b>every</b> component, master and overrides
/// each under its RECURRENCE-ID, so a single moved date changes the shape (décision 9). A
/// PARTSTAT or the case of an address changes nothing: an Outlook REPLY rewrites both.
/// </summary>
internal static class SchedulingShape
{
    internal static string? Of(string ics, bool withAttendees = true)
    {
        var parsed = IcsDocument.TryLoad(ics);
        if (parsed is null) return null;
        var parts = IcsDocument.Components(parsed)
            .Select(c => (Key: IcsDocument.InstanceIdOf(c), Text: ComponentShape(c, withAttendees)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key + "=" + p.Text);
        return string.Join("\n", parts);
    }

    internal static string HashOf(string shape) => IcsDocument.HashOf(shape);

    internal static IReadOnlySet<string> Addresses(string ics)
    {
        var parsed = IcsDocument.TryLoad(ics);
        if (parsed is null) return new HashSet<string>();
        return IcsDocument.Components(parsed).SelectMany(AddressesOf).ToHashSet(StringComparer.Ordinal);
    }

    internal static string? OrganizerOf(string ics)
    {
        var parsed = IcsDocument.TryLoad(ics);
        var master = parsed is null ? null : IcsDocument.MasterOf(parsed);
        return master?.Organizer is { } o ? IcsProjector.Address(o.Value)?.Trim().ToLowerInvariant() : null;
    }

    /// <summary>The components whose shape moved between the two files without their SEQUENCE
    /// moving too — what a client forgot, and what the server has to advance before sending
    /// (décision 9). A component new in <paramref name="after"/> is not a lag: nothing was sent for it.</summary>
    internal static IReadOnlyList<string> ChangedWithoutBump(string before, string after)
    {
        var earlier = ComponentsByKey(before);
        var later = ComponentsByKey(after);
        if (earlier is null || later is null) return [];
        return later.Where(kv => earlier.TryGetValue(kv.Key, out var was)
                && ComponentShape(was, true) != ComponentShape(kv.Value, true)
                && kv.Value.Sequence <= was.Sequence)
            .Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    private static Dictionary<string, CalendarEvent>? ComponentsByKey(string ics)
    {
        var parsed = IcsDocument.TryLoad(ics);
        return parsed is null ? null
            : IcsDocument.Components(parsed).GroupBy(IcsDocument.InstanceIdOf, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    private static string ComponentShape(CalendarEvent c, bool withAttendees) => string.Join("|",
        IcsComposer.Shape(c), c.Summary ?? "", c.Location ?? "",
        withAttendees ? string.Join(",", AddressesOf(c).Order(StringComparer.Ordinal)) : "");

    private static IEnumerable<string> AddressesOf(CalendarEvent c) => (c.Attendees ?? [])
        .Where(a => a is not null)
        .Select(a => IcsProjector.Address(a.Value))
        .OfType<string>()
        .Select(a => a.Trim().ToLowerInvariant());
}
```

- [ ] **Step 6 : `dotnet test --filter FullyQualifiedName~SchedulingShape|FullyQualifiedName~IcsMethod` est vert.**

Si `ChangedWithoutBump` sur les deux fixtures rend `[""]` au lieu de vide, c'est que le maître de la
seconde fixture n'a pas `SEQUENCE:1` : vérifier le Step 2, pas le code.

- [ ] **Step 7 : les tests de `SchedulingDecider` (rouges) — une ligne du tableau par test**

`SchedulingDeciderTests.cs` :

```csharp
public class SchedulingDeciderTests
{
    private static readonly IReadOnlySet<string> Alice = new HashSet<string> { "alice@weesky.be", "alice@weesky.net" };
    private static string Fixture(string name) => InvitationParserTests.Fixture(name);
    private static readonly string Invited = Fixture("webmail-invited");
    private static string Hash(string ics) => SchedulingShape.HashOf(SchedulingShape.Of(ics)!);

    private static SchedulingDecision Decide(string? before, string? after, string? owner, WriteOrigin origin = WriteOrigin.Webmail, IReadOnlySet<string>? own = null) =>
        SchedulingDecider.Decide(new SchedulingInput(
            new SchedulingBefore(before, owner, before is null || owner is null ? null : Hash(before)), after, origin, own ?? Alice));

    [Fact]
    public void FirstWriteWithGuests_FromTheWebmail_InvitesEveryone_AndTakesOwnership()
    {
        var decision = Decide(null, Invited, owner: null);

        var mail = Assert.Single(decision.Mails);
        Assert.Equal(MailKind.Invitation, mail.Kind);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"], mail.Recipients.Order());
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(Invited), decision.Hash);
    }

    [Fact]
    public void TakingOverAnEventThunderbirdInvited_SaysUpdate()
    {
        var decision = Decide(Invited, Invited.Replace("LOCATION:Salle 2", "LOCATION:Salle 3"), owner: null);

        Assert.Equal(MailKind.Update, Assert.Single(decision.Mails).Kind);
        Assert.Equal("webmail", decision.Owner);
    }

    [Fact]
    public void ADeviceWriting_AnEventNobodyOwns_SendsNothing()
    {
        var decision = Decide(null, Invited, owner: null, origin: WriteOrigin.Device);

        Assert.Empty(decision.Mails);
        Assert.Null(decision.Owner);
        Assert.Null(decision.Hash);
    }

    [Fact]
    public void NotTheOrganizer_OrNoGuests_SendsNothing()
    {
        var received = Invited.Replace("ORGANIZER;CN=Alice:mailto:alice@weesky.be", "ORGANIZER:mailto:marc.dupont@example.org");
        Assert.Empty(Decide(null, received, owner: null).Mails);
        Assert.Null(Decide(null, received, owner: null).Owner);

        var alone = Invited.Split("\r\n").Where(l => !l.StartsWith("ATTENDEE", StringComparison.Ordinal)).Aggregate((a, b) => a + "\r\n" + b);
        Assert.Empty(Decide(null, alone, owner: null).Mails);
    }

    [Fact]
    public void UnchangedShape_SendsNothing_AndKeepsTheColumns()
    {
        var answered = Invited.Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=ACCEPTED");
        var decision = Decide(Invited, answered, owner: "webmail", origin: WriteOrigin.Device);

        Assert.Empty(decision.Mails);
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(Invited), decision.Hash);
    }

    [Fact]
    public void ChangedShape_SameGuests_UpdatesEveryone_FromEitherDoor()
    {
        var moved = Fixture("webmail-invited-override");
        foreach (var origin in new[] { WriteOrigin.Webmail, WriteOrigin.Device })
        {
            var decision = Decide(Invited, moved, owner: "webmail", origin: origin);
            var mail = Assert.Single(decision.Mails);
            Assert.Equal(MailKind.Update, mail.Kind);
            Assert.Equal(2, mail.Recipients.Count);
            Assert.Equal(Hash(moved), decision.Hash);
        }
    }

    [Fact]
    public void GuestsAdded_AreInvited_AndTheOthersUpdatedOnlyIfSomethingElseChanged()
    {
        var added = Invited.Replace("END:VEVENT", "ATTENDEE;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:paul@example.org\r\nEND:VEVENT");
        var decision = Decide(Invited, added, owner: "webmail");
        var mail = Assert.Single(decision.Mails);
        Assert.Equal(MailKind.Invitation, mail.Kind);
        Assert.Equal(["paul@example.org"], mail.Recipients);

        var addedAndMoved = added.Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var both = Decide(Invited, addedAndMoved, owner: "webmail").Mails;
        Assert.Equal(2, both.Count);
        Assert.Equal(["paul@example.org"], both.Single(m => m.Kind == MailKind.Invitation).Recipients);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"], both.Single(m => m.Kind == MailKind.Update).Recipients.Order());
    }

    [Fact]
    public void GuestsRemoved_AreCancelledAlone_AndTheRestUpdatedOnlyIfSomethingElseChanged()
    {
        var removed = Invited.Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n", "");
        var decision = Decide(Invited, removed, owner: "webmail");
        var mail = Assert.Single(decision.Mails);
        Assert.Equal(MailKind.Cancellation, mail.Kind);
        Assert.Equal(["julie@example.net"], mail.Recipients);
        Assert.Equal("webmail", decision.Owner);

        var removedAndMoved = removed.Replace("SUMMARY:Réunion de rentrée", "SUMMARY:Réunion");
        var both = Decide(Invited, removedAndMoved, owner: "webmail").Mails;
        Assert.Equal(["marc.dupont@example.org"], both.Single(m => m.Kind == MailKind.Update).Recipients);
    }

    [Fact]
    public void NoGuestLeft_OrDeleted_CancelsEveryone_AndReleasesOwnership()
    {
        var alone = Invited.Split("\r\n").Where(l => !l.StartsWith("ATTENDEE", StringComparison.Ordinal)).Aggregate((a, b) => a + "\r\n" + b);
        foreach (var after in new[] { alone, null })
        {
            var decision = Decide(Invited, after, owner: "webmail", origin: WriteOrigin.Device);
            var mail = Assert.Single(decision.Mails);
            Assert.Equal(MailKind.Cancellation, mail.Kind);
            Assert.Equal(["julie@example.net", "marc.dupont@example.org"], mail.Recipients.Order());
            Assert.Null(decision.Owner);
            Assert.Null(decision.Hash);
        }
    }

    [Fact]
    public void DeletingAnEventNobodyOwns_SendsNothing()
    {
        Assert.Empty(Decide(Invited, null, owner: null).Mails);
    }

    // The organizer listed among the guests — Google's and Apple's habit, and any of the user's
    // own addresses — never receives their own invitation.
    [Fact]
    public void TheUsersOwnAddresses_AreNeverRecipients()
    {
        var withSelf = Invited.Replace("END:VEVENT", "ATTENDEE;PARTSTAT=ACCEPTED:mailto:Alice@weesky.net\r\nEND:VEVENT");
        var decision = Decide(null, withSelf, owner: null);

        Assert.DoesNotContain("alice@weesky.net", Assert.Single(decision.Mails).Recipients);
    }
}
```

- [ ] **Step 8 : `SchedulingDecider`**

```csharp
namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

internal enum WriteOrigin { Webmail, Device }

internal enum MailKind { Invitation, Update, Cancellation }

internal sealed record ScheduledMail(MailKind Kind, IReadOnlyList<string> Recipients);

/// <summary>The stored row before the write: its file (null on a creation) and its two
/// scheduling columns.</summary>
internal sealed record SchedulingBefore(string? Ics, string? Owner, string? Hash);

internal sealed record SchedulingInput(SchedulingBefore Before, string? After, WriteOrigin Origin, IReadOnlySet<string> OwnAddresses);

/// <summary>Who receives what, and the columns to write afterwards. Owner and Hash are the
/// values to store — unchanged when nothing is to be sent.</summary>
internal sealed record SchedulingDecision(IReadOnlyList<ScheduledMail> Mails, string? Owner, string? Hash);

/// <summary>The table of décision 9, and nothing else: no I/O, no clock, no mail.</summary>
internal static class SchedulingDecider
{
    internal const string WebmailOwner = "webmail";

    internal static SchedulingDecision Decide(SchedulingInput input)
    {
        var before = input.Before;
        var keep = new SchedulingDecision([], before.Owner, before.Hash);
        var owned = before.Owner == WebmailOwner;
        var wasInvited = before.Ics is null ? new HashSet<string>() : Guests(before.Ics, input.OwnAddresses);

        if (input.After is null)
            return owned && wasInvited.Count > 0 ? Cancel(wasInvited) : keep;

        var shape = SchedulingShape.Of(input.After);
        if (shape is null) return keep;
        var guests = Guests(input.After, input.OwnAddresses);
        var hash = SchedulingShape.HashOf(shape);

        if (!owned)
        {
            if (input.Origin != WriteOrigin.Webmail || guests.Count == 0) return keep;
            var organizer = SchedulingShape.OrganizerOf(input.After);
            if (organizer is null || !input.OwnAddresses.Contains(organizer)) return keep;
            var kind = wasInvited.Count > 0 ? MailKind.Update : MailKind.Invitation;
            return new SchedulingDecision([new ScheduledMail(kind, Sorted(guests))], WebmailOwner, hash);
        }

        if (guests.Count == 0) return Cancel(wasInvited);
        if (hash == before.Hash) return keep;

        var added = guests.Except(wasInvited).ToList();
        var removed = wasInvited.Except(guests).ToList();
        var kept = guests.Intersect(wasInvited).ToList();
        var otherChanged = before.Ics is null
            || SchedulingShape.Of(before.Ics, withAttendees: false) != SchedulingShape.Of(input.After, withAttendees: false);

        var mails = new List<ScheduledMail>();
        if (added.Count > 0) mails.Add(new ScheduledMail(MailKind.Invitation, Sorted(added)));
        if (removed.Count > 0) mails.Add(new ScheduledMail(MailKind.Cancellation, Sorted(removed)));
        if (kept.Count > 0 && otherChanged) mails.Add(new ScheduledMail(MailKind.Update, Sorted(kept)));
        return new SchedulingDecision(mails, WebmailOwner, hash);
    }

    private static SchedulingDecision Cancel(IReadOnlySet<string> recipients) =>
        new(recipients.Count == 0 ? [] : [new ScheduledMail(MailKind.Cancellation, Sorted(recipients))], null, null);

    private static HashSet<string> Guests(string ics, IReadOnlySet<string> own) =>
        SchedulingShape.Addresses(ics).Where(a => !own.Contains(a)).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<string> Sorted(IEnumerable<string> addresses) => [.. addresses.Order(StringComparer.Ordinal)];
}
```

Le `OwnAddresses` passé par le planificateur est déjà en minuscules (`IUserAddresses` le garantit).

- [ ] **Step 9 : `dotnet test --filter FullyQualifiedName~SchedulingDecider` est vert.**

- [ ] **Step 10 : `SequenceRewriter` — test puis code**

`SequenceRewriterTests.cs` :

```csharp
public class SequenceRewriterTests
{
    [Fact]
    public void Bump_AdvancesTheNamedComponentsOnly_AndWritesTheLineWhenAbsent()
    {
        var ics = InvitationParserTests.Fixture("webmail-invited-override");

        var master = SequenceRewriter.Bump(ics, [""]);
        Assert.Equal(2, InvitationParser.SequenceOf(master));
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Brussels:20261019T100000\r\nDTSTAMP:20260913T100000Z\r\nSEQUENCE:0", master);

        var both = SequenceRewriter.Bump(ics, ["", "20261019T100000"]);
        Assert.Contains("\r\nSEQUENCE:1\r\nSUMMARY:Réunion de rentrée\r\nLOCATION:Salle 2\r\nDTSTART;TZID=Europe/Brussels:20261019T150000", both);

        var without = ics.Replace("SEQUENCE:1\r\n", "").Replace("SEQUENCE:0\r\n", "");
        var written = SequenceRewriter.Bump(without, [""]);
        Assert.Equal(1, InvitationParser.SequenceOf(written));
        Assert.Equal(ics.Length - "SEQUENCE:0\r\n".Length, written.Length);
    }

    [Fact]
    public void Bump_LeavesEveryOtherByte()
    {
        var ics = InvitationParserTests.Fixture("webmail-invited");
        var bumped = SequenceRewriter.Bump(ics, [""]);
        Assert.Equal(ics.Replace("SEQUENCE:0", "SEQUENCE:1"), bumped);
        Assert.Equal(ics, SequenceRewriter.Bump(ics, []));
    }
}
```

`SequenceRewriter.cs` — sur les lignes logiques de `PartStatRewriter.Unfold` ; un composant est
identifié par sa ligne `RECURRENCE-ID` (valeur après le `:`, sans paramètres), `""` pour le maître ;
la ligne `SEQUENCE` remplacée sur place, ou insérée juste après `UID` quand elle manque :

```csharp
namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>SEQUENCE plus one on the components named, textually: the file a device just wrote
/// stays its own file but for that number (décision 9). Each component carries its own version,
/// so an override moved alone advances alone, as <c>IcsComposer.RewriteOne</c> does.</summary>
internal static class SequenceRewriter
{
    internal static string Bump(string ics, IReadOnlyCollection<string> instanceIds)
    {
        if (instanceIds.Count == 0) return ics;
        var newline = ics.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var physical = ics.Split(newline);
        var lines = PartStatRewriter.Unfold(physical);
        var output = new List<string>();
        var i = 0;
        while (i < lines.Count)
        {
            if (!lines[i].Text.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            { output.AddRange(Physical(physical, lines[i])); i++; continue; }
            var end = lines.FindIndex(i, l => l.Text.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase));
            if (end < 0) end = lines.Count - 1;
            var component = lines.GetRange(i, end - i + 1);
            var key = component.Select(l => l.Text).FirstOrDefault(t => t.StartsWith("RECURRENCE-ID", StringComparison.OrdinalIgnoreCase)) is { } rid
                ? rid[(rid.IndexOf(':') + 1)..] : "";
            output.AddRange(instanceIds.Contains(key) ? Bumped(component, physical) : component.SelectMany(l => Physical(physical, l)));
            i = end + 1;
        }
        return string.Join(newline, output);
    }

    private static IEnumerable<string> Bumped(List<PartStatRewriter.Line> component, string[] physical)
    {
        var sequence = component.FindIndex(l => l.Text.StartsWith("SEQUENCE:", StringComparison.OrdinalIgnoreCase));
        var current = sequence < 0 ? 0 : int.TryParse(component[sequence].Text["SEQUENCE:".Length..], out var n) ? n : 0;
        var line = "SEQUENCE:" + (current + 1);
        for (var j = 0; j < component.Count; j++)
        {
            if (j == sequence) { yield return line; continue; }
            foreach (var p in Physical(physical, component[j])) yield return p;
            if (sequence < 0 && component[j].Text.StartsWith("UID:", StringComparison.OrdinalIgnoreCase)) yield return line;
        }
    }

    private static IEnumerable<string> Physical(string[] physical, PartStatRewriter.Line line) =>
        physical.Skip(line.First).Take(line.Count);
}
```

`PartStatRewriter.Unfold(string[] physical)` est privé aujourd'hui : le passer `internal` (la
surcharge `Unfold(string)` l'est déjà) ; `Line` est déjà `internal`. La clé d'un `RECURRENCE-ID`
est sa valeur brute (`20261019T100000`), exactement ce que rend `IcsDocument.InstanceIdOf` pour
une date locale — vérifier sur la fixture que les deux coïncident (le test du Step 10 le fait
via `ChangedWithoutBump` + `Bump`) ; si `InstanceIdOf` normalise autrement (UTC, `Z`), aligner la
clé de `SequenceRewriter` sur `IcsDocument.InstanceIdOf` en analysant le composant plutôt que de
lire la ligne.

- [ ] **Step 11 : `dotnet test --filter FullyQualifiedName~Scheduling` puis `cd src && dotnet test` complets, verts.**

- [ ] **Step 12 : commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Data src/snoopy.microservice/Services/Calendar docs/superpowers/webmail-calendar-tables.md src/snoopy.microservice/snoopy.microservice.Tests
git commit -F - <<'EOF'
feat(agenda): 5e2, l'empreinte d'invitation et la table des envois (code pur)

Deux colonnes sur calendar_events, SchedulingShape sur tous les composants, SchedulingDecider, SEQUENCE avancée textuellement.
EOF
```

---

### Task 2 : les invités dans le modèle d'écriture — `attendees`, `ORGANIZER`, `canInvite`

**Files:**
- Create: `src/snoopy.microservice/Models/Calendar/AttendeeWrite.cs`
- Modify: `Models/Calendar/EventRequest.cs`, `EventRequestValidator.cs`, `EventWrite.cs`, `EventDetail.cs`, `EventResponse.cs`, `StoredEventRef.cs`
- Modify: `Services/Calendar/IcsComposer.cs` (`PlaceAttendees`)
- Create: `Services/Calendar/Scheduling/OrganizerIdentity.cs`
- Modify: `Repositories/CalendarEventStore.cs` (`FindByUidAsync` lit `SchedulingOwner`)
- Modify: `Controllers/CalendarEventsController.cs` (organisateur, `canInvite`, `not_organizer`)
- Modify: `Configuration/ApplicationServicesConfiguration.cs` (`IOrganizerIdentity`)
- Test: `snoopy.microservice.Tests/Models/Calendar/EventRequestValidatorTests.cs` (existant : chercher le fichier qui teste `EventRequestValidator.Validate` ; sinon le créer), `Services/IcsComposerTests.cs`, `Services/Calendar/Scheduling/OrganizerIdentityTests.cs`, `Controllers/CalendarEventsControllerTests.cs`, `Services/EventResponseContractTests.cs`

**Interfaces:**
- Consumes : `IdentityResolver.Resolve(stored, primaryAddress, fullName, aliasAddresses)` →
  `IReadOnlyList<SendingIdentityInfo>` (`Address`, `DisplayName`, `IsDefault`) ;
  `ISendingIdentityStore.GetAsync(userId, "", ct)` ; `IAliasDirectory.EnforcesOwnership/GetAddressesAsync` ;
  `IProfileReader.GetDisplayNameAsync(user, ct)`.
- Produces :
  ```csharp
  public sealed record AttendeeWrite(string Email, string? Name);      // Email en minuscules, sans mailto:
  public sealed record OrganizerWrite(string Email, string? Name);
  public sealed class AttendeeRequest { public string Email { get; set; } = ""; public string? Name { get; set; } }
  // EventRequest : public List<AttendeeRequest>? Attendees { get; set; }   public string Language { get; set; } = "en";
  // EventWrite : ..., bool KeepRepeat = false, IReadOnlyList<AttendeeWrite>? Attendees = null, OrganizerWrite? Organizer = null
  // EventDetail / EventResponse : ..., string? MyPartStat = null, bool CanInvite = true
  // StoredEventRef(Guid Id, Guid CalendarId, string DavName, string IcsRaw, string? SchedulingOwner = null)
  public interface IOrganizerIdentity { Task<OrganizerWrite> ResolveAsync(User user, CancellationToken cancellationToken); }
  // CalendarEventsController : const string NotOrganizer = "not_organizer"
  ```
- Sémantique de `EventWrite.Attendees` : `null` = ne pas toucher aux lignes `ATTENDEE`/`ORGANIZER`
  (un client qui ignore le champ, le glisser-déposer de la grille) ; liste vide = retirer tous les
  invités et l'`ORGANIZER` ; liste = la liste exacte, `PARTSTAT` conservé pour une adresse déjà
  présente, `NEEDS-ACTION;RSVP=TRUE` pour une nouvelle, `ROLE=REQ-PARTICIPANT` (décision 8).

- [ ] **Step 1 : les modèles**

`Models/Calendar/AttendeeWrite.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>A guest as the editor names them: the address, lower-cased and bare, and the name the
/// client's contacts know — the server has no lookup by address (spec 5e, décision 8).</summary>
public sealed record AttendeeWrite(string Email, string? Name);

/// <summary>The ORGANIZER the composer writes beside the guests: the account's default sending
/// identity, always an address this server hosts (décision 8).</summary>
public sealed record OrganizerWrite(string Email, string? Name);

/// <summary>One entry of <c>attendees</c> in the request body.</summary>
public sealed class AttendeeRequest
{
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
}
```

`EventRequest.cs`, après `Url` :

```csharp
    /// <summary>The guests, the whole list; null leaves the file's ATTENDEE lines as they are,
    /// an empty list removes them all (spec 5e, décision 8).</summary>
    public List<AttendeeRequest>? Attendees { get; set; }

    /// <summary>The language of the invitation mails this write may send: "fr" or "en".</summary>
    public string Language { get; set; } = "en";
```

`EventWrite.cs` : ajouter en fin de liste positionnelle
`IReadOnlyList<AttendeeWrite>? Attendees = null, OrganizerWrite? Organizer = null`.
`EventDetail.cs` et `EventResponse.cs` : ajouter `bool CanInvite = true` après `MyPartStat`
(`EventResponse.From` le recopie). `StoredEventRef.cs` : `string? SchedulingOwner = null` en fin.

`EventRequestValidator.Validate`, avant le `return Result.Success(...)` :

```csharp
        IReadOnlyList<AttendeeWrite>? attendees = null;
        if (request.Attendees is { } guests)
        {
            if (guests.Count > MaxAttendees) return Result.Failure<EventWrite>(TooManyAttendees);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<AttendeeWrite>();
            foreach (var guest in guests)
            {
                var email = (guest?.Email ?? string.Empty).Trim().ToLowerInvariant();
                if (!MimeKit.MailboxAddress.TryParse(email, out var parsed) || parsed.Address != email)
                    return Result.Failure<EventWrite>(InvalidAttendee);
                if (!seen.Add(email)) continue;
                var name = string.IsNullOrWhiteSpace(guest!.Name) ? null : guest.Name.Trim().ReplaceLineEndings(" ");
                list.Add(new AttendeeWrite(email, name is { Length: > MaxAttendeeName } ? name[..MaxAttendeeName] : name));
            }
            attendees = list;
        }
```

et `attendees` passé au constructeur (`..., request.KeepRepeat, attendees)`). Constantes :

```csharp
    internal const int MaxAttendees = 100;
    internal const int MaxAttendeeName = 100;
    internal const string TooManyAttendees = "At most 100 attendees";
    internal const string InvalidAttendee = "An attendee needs a valid address";
```

Tests du validateur (dans le fichier existant qui teste `Validate`) :

```csharp
    [Fact]
    public void Attendees_AreLowercased_Deduplicated_AndNamed_OrRefused()
    {
        var request = ValidRequest();
        request.Attendees = [
            new() { Email = " Marc.Dupont@Example.org ", Name = "Marc\r\nDupont" },
            new() { Email = "marc.dupont@example.org" },
            new() { Email = "julie@example.net", Name = "  " }];

        var write = EventRequestValidator.Validate(request).Value;

        Assert.Equal([new AttendeeWrite("marc.dupont@example.org", "Marc Dupont"), new AttendeeWrite("julie@example.net", null)], write.Attendees);
        Assert.Null(write.Organizer);

        request.Attendees = [new() { Email = "not an address" }];
        Assert.Equal(EventRequestValidator.InvalidAttendee, EventRequestValidator.Validate(request).Error);
        request.Attendees = [.. Enumerable.Range(0, 101).Select(i => new AttendeeRequest { Email = $"g{i}@example.org" })];
        Assert.Equal(EventRequestValidator.TooManyAttendees, EventRequestValidator.Validate(request).Error);
        request.Attendees = null;
        Assert.Null(EventRequestValidator.Validate(request).Value.Attendees);
        request.Attendees = [];
        Assert.Empty(EventRequestValidator.Validate(request).Value.Attendees!);
    }
```

(`ValidRequest()` : reprendre le constructeur de requête valide du fichier ; s'il n'y en a pas,
copier celui de `CalendarEventsControllerTests`.)

- [ ] **Step 2 : `dotnet test --filter FullyQualifiedName~EventRequestValidator` rouge, puis vert** après le Step 1. `EventResponseContractTests.TheWireShape_RelaysEveryFieldOfTheStoreShape` doit rester vert : il relaie `CanInvite` comme les autres champs (ajouter le champ à la fixture du test si sa construction est positionnelle).

- [ ] **Step 3 : le composeur — test (rouge)**

Dans `Services/IcsComposerTests.cs`, ajouter au helper `Write(...)` deux paramètres
`IReadOnlyList<AttendeeWrite>? attendees = null, OrganizerWrite? organizer = null` transmis au
constructeur, puis :

```csharp
    private static readonly OrganizerWrite Alice = new("alice@weesky.be", "Alice");

    [Fact]
    public void ComposeNew_WritesOrganizerAndEveryGuest_NeedsActionWithRsvp()
    {
        var ics = IcsComposer.ComposeNew(Write(start: Local(2026, 9, 7, 9), end: Local(2026, 9, 7, 10), tz: Ics.Zone,
            attendees: [new("marc.dupont@example.org", "Marc Dupont"), new("julie@example.net", null)], organizer: Alice), "u1", Now);

        var master = IcsDocument.MasterOf(IcsDocument.TryLoad(ics)!)!;
        Assert.Equal("mailto:alice@weesky.be", master.Organizer.Value.ToString());
        Assert.Equal("Alice", master.Organizer.CommonName);
        Assert.Collection(master.Attendees,
            a => { Assert.Equal("mailto:marc.dupont@example.org", a.Value.ToString()); Assert.Equal("Marc Dupont", a.CommonName); Assert.Equal("NEEDS-ACTION", a.ParticipationStatus); Assert.True(a.Rsvp); Assert.Equal("REQ-PARTICIPANT", a.Role); },
            a => { Assert.Equal("mailto:julie@example.net", a.Value.ToString()); Assert.Null(a.CommonName); });
    }

    [Fact]
    public void RewriteAll_KeepsAKnownGuestsAnswer_DropsTheRemoved_AndReachesOverrides()
    {
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited-override"))!;
        var ics = IcsComposer.RewriteAll(existing, Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone,
            repeat: Weekly(), attendees: [new("julie@example.net", "Julie"), new("paul@example.org", null)], organizer: Alice), Now);

        foreach (var component in IcsDocument.Components(IcsDocument.TryLoad(ics)!))
        {
            Assert.Equal(["julie@example.net", "paul@example.org"], component.Attendees.Select(a => a.Value.ToString()["mailto:".Length..]).Order());
            Assert.Equal("ACCEPTED", component.Attendees.Single(a => a.Value.ToString().EndsWith("julie@example.net", StringComparison.Ordinal)).ParticipationStatus);
            Assert.Equal("NEEDS-ACTION", component.Attendees.Single(a => a.Value.ToString().EndsWith("paul@example.org", StringComparison.Ordinal)).ParticipationStatus);
        }
    }

    [Fact]
    public void RewriteAll_WithNullAttendees_LeavesTheLines_AndWithEmptyRemovesThemAndTheOrganizer()
    {
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited"))!;
        var untouched = IcsComposer.RewriteAll(existing, Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone, repeat: Weekly()), Now);
        Assert.Equal(2, IcsDocument.MasterOf(IcsDocument.TryLoad(untouched)!)!.Attendees.Count);

        var emptied = IcsComposer.RewriteAll(existing, Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone, repeat: Weekly(), attendees: []), Now);
        var master = IcsDocument.MasterOf(IcsDocument.TryLoad(emptied)!)!;
        Assert.Empty(master.Attendees);
        Assert.Null(master.Organizer);
    }
```

(`Weekly()` du fichier prend `byDay` ; la fixture répète le lundi, `COUNT=4` — si le helper
n'exprime pas `COUNT`, la règle réécrite diffère de la fixture, ce qui n'affecte pas ces
assertions.)

- [ ] **Step 4 : `PlaceAttendees`**

Dans `IcsComposer.Apply`, dernière ligne : `PlaceAttendees(evt, w);`. Dans `RewriteAll`, après
`Apply(master, w, withRule: true)` :

```csharp
        // The guest list is one for the whole series: an override keeps its own dates but the
        // people invited to it are the people invited to the event (décision 8).
        if (w.Attendees is not null)
            foreach (var component in IcsDocument.Components(calendar).Where(c => !ReferenceEquals(c, master)))
                PlaceAttendees(component, w);
```

et la méthode :

```csharp
    /// <summary>Décision 8: the list exactly as sent — a guest already there keeps their answer,
    /// a new one is asked (NEEDS-ACTION, RSVP) — under the ORGANIZER the caller resolved; null
    /// leaves every line, an empty list removes the guests and the organizer with them.</summary>
    private static void PlaceAttendees(CalendarEvent evt, EventWrite w)
    {
        if (w.Attendees is null) return;
        var previous = (evt.Attendees ?? []).Where(a => a is not null)
            .Select(a => (Address: IcsProjector.Address(a.Value)?.Trim().ToLowerInvariant(), Line: a))
            .Where(p => p.Address is not null)
            .GroupBy(p => p.Address!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Line, StringComparer.Ordinal);
        evt.Attendees.Clear();
        foreach (var guest in w.Attendees)
        {
            var line = new Attendee("mailto:" + guest.Email) { CommonName = guest.Name, Role = "REQ-PARTICIPANT" };
            if (previous.TryGetValue(guest.Email, out var kept))
            {
                line.ParticipationStatus = string.IsNullOrEmpty(kept.ParticipationStatus) ? "NEEDS-ACTION" : kept.ParticipationStatus;
                line.Rsvp = kept.Rsvp;
                line.CommonName ??= kept.CommonName;
            }
            else { line.ParticipationStatus = "NEEDS-ACTION"; line.Rsvp = true; }
            evt.Attendees.Add(line);
        }
        if (w.Attendees.Count == 0) { evt.Properties.Remove("ORGANIZER"); return; }
        if (w.Organizer is { } o) evt.Organizer = new Organizer("mailto:" + o.Email) { CommonName = o.Name };
    }
```

`Attendee` et `Organizer` viennent d'`Ical.Net.DataTypes`. Si `evt.Attendees` est null sur un
composant vierge (Ical.Net rend une liste vide en 5.x, à vérifier à la compilation), l'affecter
avant `Clear()` : `evt.Attendees ??= new List<Attendee>();`.

- [ ] **Step 5 : `dotnet test --filter FullyQualifiedName~IcsComposerTests` vert**, y compris les tests existants (`RewriteAll_KeepsForeignLines…` : un `Write()` sans invités laisse tout).

- [ ] **Step 6 : `IOrganizerIdentity` — test (rouge) puis code**

`Services/Calendar/Scheduling/OrganizerIdentityTests.cs` :

```csharp
public class OrganizerIdentityTests
{
    private readonly Mock<ISendingIdentityStore> _identities = new();
    private readonly Mock<IAliasDirectory> _aliases = new();
    private readonly Mock<IProfileReader> _profiles = new();
    private readonly User _user = new("alice@weesky.be") { WebmailUid = Guid.NewGuid(), FullName = "Alice Martin" };

    private OrganizerIdentity Create() => new(_identities.Object, _aliases.Object, _profiles.Object);

    [Fact]
    public async Task Resolve_TakesTheDefaultLiveAlias_WithItsStoredName()
    {
        _aliases.SetupGet(a => a.EnforcesOwnership).Returns(true);
        _aliases.Setup(a => a.GetAddressesAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync(["contact@weesky.net"]);
        _identities.Setup(i => i.GetAsync(_user.WebmailUid, string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "contact@weesky.net", DisplayName = "Alice (contact)", IsDefault = true }]);
        _profiles.Setup(p => p.GetDisplayNameAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync("Alice");

        Assert.Equal(new OrganizerWrite("contact@weesky.net", "Alice (contact)"), await Create().ResolveAsync(_user, CancellationToken.None));
    }

    // A default row the platform does not vouch for — a stale alias, or a free identity on a
    // platform that verifies nothing — falls back to the primary: the only address the service
    // account is always allowed to speak for (décision 8).
    [Fact]
    public async Task Resolve_FallsBackToThePrimary_WhenTheDefaultIsNotOwned()
    {
        _aliases.SetupGet(a => a.EnforcesOwnership).Returns(true);
        _aliases.Setup(a => a.GetAddressesAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _identities.Setup(i => i.GetAsync(_user.WebmailUid, string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "alice@gmail.com", DisplayName = "Alice G", IsDefault = true }]);
        _profiles.Setup(p => p.GetDisplayNameAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync("Alice");

        Assert.Equal(new OrganizerWrite("alice@weesky.be", "Alice"), await Create().ResolveAsync(_user, CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_NamesThePrimaryAfterTheAccount_ThenTheProfile()
    {
        _aliases.SetupGet(a => a.EnforcesOwnership).Returns(false);
        _identities.Setup(i => i.GetAsync(_user.WebmailUid, string.Empty, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _profiles.Setup(p => p.GetDisplayNameAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        Assert.Equal(new OrganizerWrite("alice@weesky.be", "Alice Martin"), await Create().ResolveAsync(_user, CancellationToken.None));
    }
}
```

`OrganizerIdentity.cs` :

```csharp
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>The address the replies will find (décision 8): the primary account's default
/// sending identity as <see cref="IdentityResolver"/> settles it, which is the primary address
/// or a live alias — both hosted here, so the service account may send in their name.</summary>
public interface IOrganizerIdentity
{
    Task<OrganizerWrite> ResolveAsync(User user, CancellationToken cancellationToken);
}

internal sealed class OrganizerIdentity(
    ISendingIdentityStore identities, IAliasDirectory aliases, IProfileReader profiles) : IOrganizerIdentity
{
    public async Task<OrganizerWrite> ResolveAsync(User user, CancellationToken cancellationToken)
    {
        var stored = await identities.GetAsync(user.WebmailUid, string.Empty, cancellationToken);
        var owned = aliases.EnforcesOwnership ? await aliases.GetAddressesAsync(user, cancellationToken) : [];
        var profile = await profiles.GetDisplayNameAsync(user, cancellationToken);
        var fullName = string.IsNullOrWhiteSpace(profile) ? user.FullName : profile;
        var chosen = IdentityResolver.Resolve(stored, user.Email, fullName, owned).First(i => i.IsDefault);
        return new OrganizerWrite(chosen.Address, string.IsNullOrWhiteSpace(chosen.DisplayName) ? null : chosen.DisplayName);
    }
}
```

Le `LabelFor` d'`IdentityResolver` rend l'adresse quand ni ligne ni nom n'existent : dans ce cas
`DisplayName == Address`, et on écrit alors `CN` = l'adresse, ce qui est inoffensif. Enregistrer
`services.AddScoped<IOrganizerIdentity, OrganizerIdentity>();` dans `AddMailServices`.
`SendingIdentity` vit dans `Data.Preferences` ; `User` a un constructeur `User(string email)`
(voir `InvitationResponderStoreTests`) — si sa forme diffère, reprendre celle des tests voisins.

- [ ] **Step 7 : le contrôleur — tests (rouges)**

Dans `CalendarEventsControllerTests` : le constructeur prend désormais `IOrganizerIdentity` (le
crochet et le résolveur de compte viennent en tâche 5 ; d'ici là le constructeur est
`(ICalendarEventStore store, IUserAddresses addresses, IOrganizerIdentity organizer)`). Ajouter
`private readonly Mock<IOrganizerIdentity> _organizer = new();` avec un `Setup` par défaut rendant
`new OrganizerWrite("john@example.com", "John")`, et :

```csharp
    [Fact]
    public async Task Create_WithAttendees_WritesTheResolvedOrganizer()
    {
        var request = ValidRequest();
        request.Attendees = [new() { Email = "marc@example.org", Name = "Marc" }];
        EventWrite? written = null;
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, EventWrite, CancellationToken>((_, w, _) => written = w)
            .ReturnsAsync(Result.Success(Guid.NewGuid()));

        await CreateController().Create(request, CancellationToken.None);

        Assert.Equal(new OrganizerWrite("john@example.com", "John"), written!.Organizer);
        Assert.Equal([new AttendeeWrite("marc@example.org", "Marc")], written.Attendees);
    }

    [Fact]
    public async Task Create_WithoutAttendees_ResolvesNoOrganizer()
    {
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Guid.NewGuid()));

        await CreateController().Create(ValidRequest(), CancellationToken.None);

        _organizer.Verify(o => o.ResolveAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WithAttendees_OnAnEventSomebodyElseOrganizes_Is400()
    {
        var id = Guid.NewGuid();
        var received = Detail(id) with { Attendees = [new AttendeeProjection(null, "lea@example.net", "Léa", null, null, true)] };
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(received);
        var request = ValidUpdateRequest();
        request.Attendees = [new() { Email = "marc@example.org" }];

        var result = await CreateController().Update(id, request, CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains(CalendarEventsController.NotOrganizer, refused.Value!.ToString());
        _store.Verify(s => s.UpdateAsync(Uid, id, It.IsAny<EditScope>(), It.IsAny<string?>(), It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WithAttendees_OnAnEventTheUserOrganizes_ByAnAlias_Proceeds()
    {
        var id = Guid.NewGuid();
        var mine = Detail(id) with { Attendees = [new AttendeeProjection(null, "John@Example.net", null, null, null, true)] };
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);
        _store.Setup(s => s.UpdateAsync(Uid, id, EditScope.All, null, It.IsAny<EventWrite>(), "abc123", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var request = ValidUpdateRequest();
        request.Attendees = [new() { Email = "marc@example.org" }];

        var result = await CreateController().Update(id, request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Get_SaysWhetherTheUserMayInvite()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail(id) with { Attendees = [new AttendeeProjection(null, "lea@example.net", "Léa", null, null, true)] });
        var received = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>((await CreateController().Get(id, CancellationToken.None)).Result).Value);
        Assert.False(received.CanInvite);

        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Detail(id));
        var own = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>((await CreateController().Get(id, CancellationToken.None)).Result).Value);
        Assert.True(own.CanInvite);
    }
```

`_addresses` rend déjà `["john@example.com", "john@example.net"]` (ou l'équivalent) par le
`Setup` par défaut du fichier — vérifier ; sinon l'ajouter. Le `Assert.IsType<OkObjectResult>` sur
`Get` reprend la forme des tests existants de `Get` du fichier (adapter si `Get` rend l'objet nu).

- [ ] **Step 8 : le contrôleur**

```csharp
public sealed class CalendarEventsController(
    ICalendarEventStore store, IUserAddresses addresses, IOrganizerIdentity organizer) : ApiBaseController
{
    internal const string NotOrganizer = "not_organizer";
```

`Get` : après avoir lu `mine` et stampé `MyPartStat`, calculer `canInvite` :

```csharp
        var own = await addresses.ForPrincipalAsync(AuthenticatedUser, cancellationToken);
        var host = detail.Attendees.FirstOrDefault(a => a.IsOrganizer && a.RecurrenceId is null);
        var canInvite = host is null || own.Contains(host.Email.Trim().ToLowerInvariant());
        return Ok(EventResponse.From(detail with { MyPartStat = ..., CanInvite = canInvite }));
```

(`OwnAnswersAsync` appelle déjà `ForPrincipalAsync` ; le refactorer pour que `Get` n'appelle la
liste qu'une fois : `var own = await addresses.ForPrincipalAsync(...)` puis
`store.OwnPartStatsAsync(uid, [id], own, ct)`.)

`Create` : après `Validate`, `var write = await WithOrganizerAsync(validated.Value, cancellationToken);`
et passer `write` au store. `Update` : après `Validate`,

```csharp
        if (validated.Value.Attendees is not null)
        {
            var current = await store.GetAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
            if (current is null) return NotFoundEnveloppe(CalendarEventStore.NotFound);
            if (!await MayInviteAsync(current, cancellationToken)) return BadRequestEnveloppe(NotOrganizer);
        }
        var write = await WithOrganizerAsync(validated.Value, cancellationToken);
```

et les deux aides :

```csharp
    /// <summary>The ORGANIZER goes with the guests: resolved only when there are some.</summary>
    private async Task<EventWrite> WithOrganizerAsync(EventWrite write, CancellationToken cancellationToken) =>
        write.Attendees is { Count: > 0 }
            ? write with { Organizer = await organizer.ResolveAsync(AuthenticatedUser, cancellationToken) }
            : write;

    /// <summary>No organizer yet, or one of the user's own addresses: décision 8's rule for the field.</summary>
    private async Task<bool> MayInviteAsync(EventDetail detail, CancellationToken cancellationToken)
    {
        var host = detail.Attendees.FirstOrDefault(a => a.IsOrganizer && a.RecurrenceId is null);
        if (host is null) return true;
        var own = await addresses.ForPrincipalAsync(AuthenticatedUser, cancellationToken);
        return own.Contains(host.Email.Trim().ToLowerInvariant());
    }
```

`Get` réutilise `MayInviteAsync`. Le `NotFoundEnveloppe` existe sur `ApiBaseController` (vérifier
son nom exact dans le fichier ; `MapFailure` mappe déjà `CalendarEventStore.NotFound` → 404, on
peut aussi écrire `return MapFailure(CalendarEventStore.NotFound);`).

`FindByUidAsync` (`CalendarEventStore.cs:380`) : ajouter `e.SchedulingOwner` à la projection
`StoredEventRef`. Test dans `CalendarEventStoreTests` : après un `CreateAsync`, poser
`SchedulingOwner = "webmail"` sur la ligne via un `PreferencesTestDbContext` et vérifier que
`FindByUidAsync` le rend.

- [ ] **Step 9 : `cd src && dotnet test` vert.** Les tests qui construisent `CalendarEventsController` à trois arguments passent tous par `CreateController()` — un seul point à changer.

- [ ] **Step 10 : commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
feat(agenda): 5e2, les invités et l'organisateur dans l'écriture d'un événement

attendees dans l'API, ORGANIZER par l'identité d'envoi par défaut, canInvite sur le détail, not_organizer.
EOF
```

---

### Task 3 : ce que les portes rapportent — `EventWriteResult`, `DavWriteOutcome.Replaced`, les colonnes

**Files:**
- Create: `Models/Calendar/EventWriteResult.cs`
- Modify: `Models/Dav/DavWriteOutcome.cs`, `Repositories/DavCalendarWriter.cs`
- Modify: `Repositories/ICalendarEventStore.cs`, `CalendarEventStore.cs`, `Controllers/CalendarEventsController.cs`, `Services/Calendar/Invitations/InvitationResponder.cs` (si `CreateAsync` y est appelé — non ; vérifier les appelants par `grep -rn "\.CreateAsync(\|\.UpdateAsync(\|\.DeleteAsync(" Controllers Services`)
- Test: `Repositories/CalendarEventStoreTests.cs`, `Repositories/DavCalendarWriterTests.cs`, `Controllers/CalendarEventsControllerTests.cs`, `Controllers/CalendarsControllerTests.cs` (si `ImportAsNew` lit `.Value` de `CreateAsync`)

**Interfaces — Produces :**

```csharp
/// The row as it was before the write, with what the scheduler needs of it.
public sealed record ReplacedVersion(string Ics, string? SchedulingOwner, string? SchedulingHash);
/// One resource the write touched: where it lives now, what it replaced (null on a creation), what it holds now (null on a removal).
public sealed record EventChange(Guid? EventId, Guid CalendarId, string DavName, ReplacedVersion? Before, string? After);
public sealed record EventWriteResult(Guid EventId, IReadOnlyList<EventChange> Changes);
// ICalendarEventStore
Task<Result<EventWriteResult>> CreateAsync(Guid userId, EventWrite write, CancellationToken ct);
Task<Result<EventWriteResult>> UpdateAsync(Guid userId, Guid eventId, EditScope scope, string? instanceId, EventWrite write, string? ifHash, CancellationToken ct);
Task<Result<EventWriteResult>> DeleteAsync(Guid userId, Guid eventId, EditScope scope, string? instanceId, CancellationToken ct);
Task SetSchedulingAsync(Guid userId, Guid calendarId, string davName, string? owner, string? hash, CancellationToken ct);
// DavWriteOutcome(DavWriteStatus Status, string? Etag, string? ConflictHref, ulong Sequence, IcsPrecondition? Precondition = null, ReplacedVersion? Replaced = null)
```

`EventId` est nul sur la porte CalDAV (le contrôleur ne le connaît pas) ; le planificateur
n'adresse la ligne que par `(CalendarId, DavName)`.

- [ ] **Step 1 : tests du store (rouges)**

Dans `CalendarEventStoreTests` (reprendre `CalendarStoreTestFactory.SeedAsync/Events/Write`) :

```csharp
    [Fact]
    public async Task Create_Update_Delete_ReportWhatTheyChanged()
    {
        var (db, user, calendar) = await CalendarStoreTestFactory.SeedAsync(Guid.NewGuid().ToString());
        var store = CalendarStoreTestFactory.Events(db);

        var created = (await store.CreateAsync(user, CalendarStoreTestFactory.Write(calendar, summary: "Réunion"), CancellationToken.None)).Value;
        var creation = Assert.Single(created.Changes);
        Assert.Equal(created.EventId, creation.EventId);
        Assert.Equal(calendar, creation.CalendarId);
        Assert.Null(creation.Before);
        Assert.Contains("SUMMARY:Réunion", creation.After);

        var detail = (await store.GetAsync(user, created.EventId, CancellationToken.None))!;
        var updated = (await store.UpdateAsync(user, created.EventId, EditScope.All, null,
            CalendarStoreTestFactory.Write(calendar, summary: "Réunion 2"), detail.IcsHash, CancellationToken.None)).Value;
        var change = Assert.Single(updated.Changes);
        Assert.Equal(creation.After, change.Before!.Ics);
        Assert.Null(change.Before.SchedulingOwner);
        Assert.Contains("SUMMARY:Réunion 2", change.After);
        Assert.Equal(creation.DavName, change.DavName);

        await store.SetSchedulingAsync(user, calendar, change.DavName, "webmail", "abc", CancellationToken.None);
        var deleted = (await store.DeleteAsync(user, created.EventId, EditScope.All, null, CancellationToken.None)).Value;
        var removal = Assert.Single(deleted.Changes);
        Assert.Equal(("webmail", "abc"), (removal.Before!.SchedulingOwner, removal.Before.SchedulingHash));
        Assert.Null(removal.After);
    }

    [Fact]
    public async Task Update_ThisAndFollowing_ReportsTheCutSeries_AndTheNewOne()
    {
        var (db, user, calendar) = await CalendarStoreTestFactory.SeedAsync(Guid.NewGuid().ToString());
        var store = CalendarStoreTestFactory.Events(db);
        var created = (await store.CreateAsync(user, CalendarStoreTestFactory.Write(calendar, repeat: CalendarStoreTestFactory.Weekly()), CancellationToken.None)).Value;
        var detail = (await store.GetAsync(user, created.EventId, CancellationToken.None))!;
        var third = (await store.WindowAsync(user, CalendarStoreTestFactory.Utc(2026, 9, 1), CalendarStoreTestFactory.Utc(2026, 10, 1), CalendarStoreTestFactory.Zone, CancellationToken.None)).Value[2];

        var updated = (await store.UpdateAsync(user, created.EventId, EditScope.ThisAndFollowing, third.InstanceId,
            CalendarStoreTestFactory.Write(calendar, summary: "Suite"), detail.IcsHash, CancellationToken.None)).Value;

        Assert.Equal(2, updated.Changes.Count);
        var cut = updated.Changes.Single(c => c.EventId == created.EventId);
        Assert.NotNull(cut.Before);
        Assert.Contains("UNTIL=", cut.After);
        var following = updated.Changes.Single(c => c.EventId != created.EventId);
        Assert.Null(following.Before);
        Assert.Contains("SUMMARY:Suite", following.After);
    }

    [Fact]
    public async Task Delete_ThisOccurrence_IsReportedAsAnUpdate()
    {
        var (db, user, calendar) = await CalendarStoreTestFactory.SeedAsync(Guid.NewGuid().ToString());
        var store = CalendarStoreTestFactory.Events(db);
        var created = (await store.CreateAsync(user, CalendarStoreTestFactory.Write(calendar, repeat: CalendarStoreTestFactory.Weekly()), CancellationToken.None)).Value;
        var second = (await store.WindowAsync(user, CalendarStoreTestFactory.Utc(2026, 9, 1), CalendarStoreTestFactory.Utc(2026, 10, 1), CalendarStoreTestFactory.Zone, CancellationToken.None)).Value[1];

        var deleted = (await store.DeleteAsync(user, created.EventId, EditScope.This, second.InstanceId, CancellationToken.None)).Value;

        var change = Assert.Single(deleted.Changes);
        Assert.NotNull(change.Before);
        Assert.Contains("EXDATE", change.After);
    }
```

(`Write(calendar, repeat: …)` : `CalendarStoreTestFactory.Write` a un paramètre `repeat` ; le
`start` par défaut du helper tombe en septembre 2026 — vérifier la date par défaut dans le
factory et ajuster la fenêtre si besoin.)

Dans `DavCalendarWriterTests`, un test : un `PutAsync` de création rend `Replaced` nul ; un second
`PutAsync` d'un autre contenu rend `Replaced.Ics` égal au premier contenu ; après
`SetSchedulingAsync(..., "webmail", "h")`, un `PutAsync` **byte-identique** rend `Replaced` non nul
avec `("webmail", "h")` (le court-circuit rapporte la ligne, sans transaction) ; un `DeleteAsync`
rend `Replaced` avec le dernier contenu et les colonnes.

- [ ] **Step 2 : le store et le writer**

`EventWriteResult.cs` avec les trois records ci-dessus (namespace `Models.Calendar` ; `ReplacedVersion`
y vit aussi, `Models.Dav` l'importe).

`CalendarEventStore` :
- une aide `private static ReplacedVersion Replaced(CalendarEvent row) => new(row.IcsRaw, row.SchedulingOwner, row.SchedulingHash);`
  appelée **avant** `ApplyIcsAsync` / la suppression, tant que `row.IcsRaw` est l'ancien texte ;
- `CreateAsync` : `Result.Success(new EventWriteResult(id, [new EventChange(id, calendarId, davName, null, ics)]))` ;
- `UpdateAsync` : une liste locale `changes`, alimentée à chaque `ApplyIcsAsync` (l'original :
  `Before = Replaced(row)` pris avant, `After = ics` ; la suite d'une coupure : `Before = null`) ;
  un déplacement d'agenda rapporte `CalendarId` = l'agenda cible et le nom DAV final (après
  `FreeNameAsync`) ;
- `DeleteAsync` : scope `This` (→ `RemoveOne`, une réécriture) rapporte `Before`/`After` ; les
  autres rapportent `After = null` ;
- `SetSchedulingAsync` : `UPDATE` des deux colonnes sur la ligne `(user_id, calendar_id, dav_name)`,
  sans toucher `sync_sequence`, `updated_at` ni `ics_hash` (les colonnes ne font pas partie de la
  ressource) ; silencieux si la ligne n'existe plus.

`DavCalendarWriter` : `Replaced(row)` dans `GateAsync` avant `sync.ArchiveAsync` (L270) et dans le
court-circuit byte-identique (L209) ; dans `DeleteAsync` avant la suppression. L'aide peut vivre
dans `CalendarEvent` lui-même : `internal ReplacedVersion AsReplaced() => new(IcsRaw, SchedulingOwner, SchedulingHash);`
— un seul endroit pour les deux portes.

`CalendarEventsController` : `created.Value.EventId` dans `Create` ; les autres appels lisent
`IsSuccess`/`Error` comme avant. `CalendarsController.ImportAsNew` : `.Value.EventId` si elle
utilise `CreateAsync` (l'agenda, pas l'événement — vérifier : c'est `ICalendarStore.CreateAsync`,
non concerné).

- [ ] **Step 3 : les tests existants** : chaque `(await store.CreateAsync(...)).Value` employé comme `Guid` devient `.Value.EventId` (`grep -rn "CreateAsync(" snoopy.microservice.Tests | grep -v ICalendarStore` ; les Moq `ReturnsAsync(Result.Success(Guid))` sur `ICalendarEventStore.CreateAsync` deviennent `Result.Success(new EventWriteResult(id, []))`, et `Result.Success()` sur `UpdateAsync`/`DeleteAsync` devient `Result.Success(new EventWriteResult(id, []))`). Un `sed` guidé, puis `cd src && dotnet test` vert.

- [ ] **Step 4 : commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
feat(agenda): 5e2, les deux portes d'écriture rapportent le fichier remplacé et le fichier écrit

EventWriteResult sur le store, Replaced sur DavWriteOutcome, SetSchedulingAsync.
EOF
```

---

### Task 4 : les mails de l'organisateur et les deux sessions — `InvitationMailer`, compte de service, file

**Files:**
- Create: `Models/Calendar/SchedulingOptions.cs`
- Create: `Services/Calendar/Scheduling/InvitationMailer.cs`, `ServiceMailQueue.cs`
- Modify: `Services/Calendar/Invitations/InvitationText.cs` (sujets et corps de l'organisateur), `ReplyComposer.cs` (`Cn`, `EscapeText` passent `internal`)
- Modify: `Configuration/ApplicationServicesConfiguration.cs`, `snoopy.microservice.host/appsettings.json`
- Test: `Services/Calendar/Scheduling/InvitationMailerTests.cs`, `ServiceMailQueueTests.cs`, `Services/Calendar/Invitations/InvitationTextTests.cs`

**Interfaces:**
- Consumes : `IcsMethod.With`, `InvitationParser.Read/SequenceOf`, `InvitationText.When(ParsedInvitation, tz, language)`,
  `ISmtpConnectionFactory.OpenAsync(MailAccountConnection, ct)` → `Result<ISmtpSession>`, `ISmtpSession.SendAsync(MimeMessage, ct)`,
  `MailAccountConnection(AccountId, IsHomeServer, ImapHost, ImapPort, ImapSecurity, SmtpHost, SmtpPort, SmtpSecurity, SieveHost, SievePort, Username, Credential)`,
  `PasswordCredential(Password)`.
- Produces :
  ```csharp
  public sealed class SchedulingOptions { public ServiceSmtpOptions Smtp { get; set; } = new(); public int RetrySeconds { get; set; } = 60; public bool Enabled => !string.IsNullOrWhiteSpace(Smtp.Host); }
  public sealed class ServiceSmtpOptions { public string Host { get; set; } = ""; public int Port { get; set; } = 587; public string Login { get; set; } = ""; public string Password { get; set; } = ""; }
  internal sealed record InvitationMailInput(MailKind Kind, string StoredIcs, int CancelSequence, IReadOnlyList<string> Recipients, OrganizerWrite Organizer, string Language, DateTime NowUtc);
  internal static class InvitationMailer { MimeMessage Compose(InvitationMailInput input); string Calendar(InvitationMailInput input); }
  internal sealed record QueuedMail(MimeMessage Message, string Description);
  public interface IServiceMailQueue { bool TryEnqueue(QueuedMail mail); }   // false : compte de service non configuré ou file pleine, journalisé
  // InvitationText : string OrganizerSubject(MailKind kind, string? summary, string language)
  //                  string OrganizerBody(MailKind kind, string? summary, string when, string? location, string organizer, string language)
  ```

- [ ] **Step 1 : les options et la configuration**

`Models/Calendar/SchedulingOptions.cs` comme ci-dessus. Dans `AddSnoopyOptions` :
`services.AddOptions<SchedulingOptions>().Bind(configuration.GetSection("Scheduling"));` — sans
`ValidateOnStart` : une section vide est un état légitime (pas de compte de service, la file
refuse et journalise). `appsettings.json` du host :

```json
  "Scheduling": {
    "Smtp": { "Host": "", "Port": 587, "Login": "", "Password": "" },
    "RetrySeconds": 60
  }
```

Les vraies valeurs vont dans `dotnet user-secrets set "Scheduling:Smtp:Password" "…"` (projet
host) ; la documenter dans `docs/superpowers/webmail-calendar-tables.md` n'a pas de sens, l'écrire
dans le `README` de configuration s'il existe (chercher où `Mail:` est documenté : `grep -rln
"WebmailBaseUrl" docs README*`), sinon dans la section « Configuration » de
`docs/architecture-calendar.md`.

- [ ] **Step 2 : `InvitationText` — tests (rouges) puis code**

Dans `InvitationTextTests.cs` :

```csharp
    [Theory]
    [InlineData(MailKind.Invitation, "fr", "Invitation : Dîner")]
    [InlineData(MailKind.Update, "fr", "Mise à jour : Dîner")]
    [InlineData(MailKind.Cancellation, "fr", "Annulation : Dîner")]
    [InlineData(MailKind.Invitation, "en", "Invitation: Dîner")]
    [InlineData(MailKind.Update, "en", "Updated: Dîner")]
    [InlineData(MailKind.Cancellation, "en", "Cancelled: Dîner")]
    public void OrganizerSubject_NamesTheKind_ThenTheTitle(MailKind kind, string language, string expected) =>
        Assert.Equal(expected, InvitationText.OrganizerSubject(kind, "Dîner", language));

    [Fact]
    public void OrganizerSubject_WithoutTitle_SaysSo()
    {
        Assert.Equal("Invitation : (sans titre)", InvitationText.OrganizerSubject(MailKind.Invitation, null, "fr"));
        Assert.Equal("Invitation: (no title)", InvitationText.OrganizerSubject(MailKind.Invitation, " ", "en"));
    }

    [Fact]
    public void OrganizerBody_ListsTitle_When_Where_Organizer_AndHowToAnswer()
    {
        var body = InvitationText.OrganizerBody(MailKind.Invitation, "Dîner", "lundi 5 octobre 2026 · 10:00 – 11:00", "Salle 2", "Alice", "fr");
        Assert.Equal(["Dîner", "Quand : lundi 5 octobre 2026 · 10:00 – 11:00", "Où : Salle 2", "Organisé par Alice", "", "Répondez depuis votre agenda, ou par retour de mail."], body.Split("\r\n"));

        var cancelled = InvitationText.OrganizerBody(MailKind.Cancellation, "Dîner", "…", null, "Alice", "en");
        Assert.Equal(["Dîner", "When: …", "Organised by Alice", "", "This event is cancelled."], cancelled.Split("\r\n"));
    }
```

Les deux-points français portent l'insécable (U+00A0) avant eux, comme les sujets de 5e1
(`InvitationText.Subject`) — reprendre la même constante. Implémentation dans `InvitationText` :

```csharp
    internal static string OrganizerSubject(MailKind kind, string? summary, string language)
    {
        var title = string.IsNullOrWhiteSpace(summary) ? (IsFrench(language) ? "(sans titre)" : "(no title)") : summary.Trim();
        var head = (kind, IsFrench(language)) switch
        {
            (MailKind.Invitation, true) => "Invitation", (MailKind.Update, true) => "Mise à jour", (MailKind.Cancellation, true) => "Annulation",
            (MailKind.Invitation, false) => "Invitation", (MailKind.Update, false) => "Updated", (_, false) => "Cancelled",
        };
        return IsFrench(language) ? $"{head} : {title}" : $"{head}: {title}";
    }

    internal static string OrganizerBody(MailKind kind, string? summary, string when, string? location, string organizer, string language)
    {
        var fr = IsFrench(language);
        var lines = new List<string> { string.IsNullOrWhiteSpace(summary) ? (fr ? "(sans titre)" : "(no title)") : summary.Trim() };
        lines.Add(fr ? $"Quand : {when}" : $"When: {when}");
        if (!string.IsNullOrWhiteSpace(location)) lines.Add(fr ? $"Où : {location}" : $"Where: {location}");
        lines.Add(fr ? $"Organisé par {organizer}" : $"Organised by {organizer}");
        lines.Add("");
        lines.Add(kind == MailKind.Cancellation
            ? (fr ? "Ce rendez-vous est annulé." : "This event is cancelled.")
            : (fr ? "Répondez depuis votre agenda, ou par retour de mail." : "Reply from your calendar, or by return mail."));
        return string.Join("\r\n", lines);
    }
```

- [ ] **Step 3 : `InvitationMailer` — tests (rouges)**

```csharp
public class InvitationMailerTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly OrganizerWrite Alice = new("alice@weesky.be", "Alice");
    private static string Stored => InvitationParserTests.Fixture("webmail-invited");

    private static InvitationMailInput Input(MailKind kind, params string[] to) =>
        new(kind, Stored, CancelSequence: 1, to, Alice, "fr", Now);

    [Fact]
    public void Request_CarriesTheStoredFileWithMethod_TwiceOver_FromTheOrganizer()
    {
        var message = InvitationMailer.Compose(Input(MailKind.Invitation, "marc.dupont@example.org", "julie@example.net"));

        Assert.Equal("Alice", message.From.Mailboxes.Single().Name);
        Assert.Equal("alice@weesky.be", message.From.Mailboxes.Single().Address);
        Assert.Equal(["marc.dupont@example.org", "julie@example.net"], message.To.Mailboxes.Select(m => m.Address));
        Assert.Equal("Invitation : Réunion de rentrée", message.Subject);
        Assert.Equal(new DateTimeOffset(Now), message.Date);

        var mixed = Assert.IsType<Multipart>(message.Body);
        var alternative = Assert.IsType<MultipartAlternative>(mixed[0]);
        var text = Assert.IsType<TextPart>(alternative[0]);
        Assert.StartsWith("Réunion de rentrée\r\nQuand : lundi 5 octobre 2026", text.Text);
        Assert.Contains("Où : Salle 2", text.Text);
        var calendar = Assert.IsType<TextPart>(alternative[1]);
        Assert.Equal("text/calendar", calendar.ContentType.MimeType);
        Assert.Equal("REQUEST", calendar.ContentType.Parameters["method"]);
        Assert.Equal(IcsMethod.With(Stored, "REQUEST"), calendar.Text);
        var attachment = Assert.IsType<MimePart>(mixed[1]);
        Assert.Equal("application/ics", attachment.ContentType.MimeType);
        Assert.Equal("invitation.ics", attachment.FileName);
        Assert.True(attachment.IsAttachment);
        using var bytes = new MemoryStream();
        attachment.Content.DecodeTo(bytes);
        Assert.Equal(calendar.Text, Encoding.UTF8.GetString(bytes.ToArray()));
    }

    [Fact]
    public void Update_SaysSo_AndCarriesTheSameFile()
    {
        var message = InvitationMailer.Compose(Input(MailKind.Update, "marc.dupont@example.org") with { Language = "en" });
        Assert.Equal("Updated: Réunion de rentrée", message.Subject);
        Assert.Equal("REQUEST", ((MultipartAlternative)((Multipart)message.Body)[0]).OfType<TextPart>().Last().ContentType.Parameters["method"]);
    }

    [Fact]
    public void Cancel_IsAReducedFile_ToTheRecipientsOnly_WithTheGivenSequence()
    {
        var message = InvitationMailer.Compose(Input(MailKind.Cancellation, "julie@example.net") with { CancelSequence = 2 });

        Assert.Equal("Annulation : Réunion de rentrée", message.Subject);
        var calendar = ((MultipartAlternative)((Multipart)message.Body)[0]).OfType<TextPart>().Last();
        Assert.Equal("CANCEL", calendar.ContentType.Parameters["method"]);
        var lines = calendar.Text.Split("\r\n");
        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Contains("METHOD:CANCEL", lines);
        Assert.Contains("UID:web-1111-2222", lines);
        Assert.Contains("SEQUENCE:2", lines);
        Assert.Contains("DTSTAMP:20260913T100000Z", lines);
        Assert.Contains("ORGANIZER;CN=Alice:mailto:alice@weesky.be", lines);
        Assert.Contains("ATTENDEE:mailto:julie@example.net", lines);
        Assert.DoesNotContain(lines, l => l.Contains("marc.dupont", StringComparison.Ordinal));
        Assert.Contains("DTSTART;TZID=Europe/Brussels:20261005T100000", lines);
        Assert.Contains("SUMMARY:Réunion de rentrée", lines);
        Assert.Contains("STATUS:CANCELLED", lines);
        Assert.Equal("END:VCALENDAR", lines[^1]);
        Assert.Contains("Ce rendez-vous est annulé.", ((TextPart)((MultipartAlternative)((Multipart)message.Body)[0])[0]).Text);
    }
}
```

- [ ] **Step 4 : `InvitationMailer`**

```csharp
using System.Text;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

internal sealed record InvitationMailInput(
    MailKind Kind, string StoredIcs, int CancelSequence, IReadOnlyList<string> Recipients,
    OrganizerWrite Organizer, string Language, DateTime NowUtc);

/// <summary>
/// Décision 11: a REQUEST carries the stored file, METHOD added, twice — inline text/calendar
/// and an invitation.ics attachment, the double form Gmail, Outlook and Apple want before they
/// show their buttons; a CANCEL carries the reduced file the RFC asks for. Pure.
/// </summary>
internal static class InvitationMailer
{
    internal static MimeMessage Compose(InvitationMailInput input)
    {
        var method = input.Kind == MailKind.Cancellation ? "CANCEL" : "REQUEST";
        var parsed = InvitationParser.Read(IcsMethod.With(input.StoredIcs, "REQUEST")).Invitation
            ?? throw new InvalidOperationException("The stored file does not read as an event");
        var calendarText = Calendar(input);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(input.Organizer.Name ?? string.Empty, input.Organizer.Email));
        foreach (var recipient in input.Recipients) message.To.Add(new MailboxAddress(string.Empty, recipient));
        message.Date = new DateTimeOffset(input.NowUtc);
        message.Subject = InvitationText.OrganizerSubject(input.Kind, parsed.Summary, input.Language);

        var when = InvitationText.When(parsed, ZoneOf(parsed), input.Language);
        var text = new TextPart("plain")
        {
            Text = InvitationText.OrganizerBody(input.Kind, parsed.Summary, when, parsed.Location,
                input.Organizer.Name ?? input.Organizer.Email, input.Language) + "\r\n",
        };
        var calendar = new TextPart("calendar") { Text = calendarText };
        calendar.ContentType.Parameters.Add("method", method);
        calendar.ContentType.Charset = "utf-8";
        var attachment = new MimePart("application", "ics")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(calendarText))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = "invitation.ics",
        };
        message.Body = new Multipart("mixed") { new MultipartAlternative { text, calendar }, attachment };
        return message;
    }

    internal static string Calendar(InvitationMailInput input)
    {
        if (input.Kind != MailKind.Cancellation) return IcsMethod.With(input.StoredIcs, "REQUEST");
        var e = InvitationParser.Read(IcsMethod.With(input.StoredIcs, "REQUEST")).Invitation
            ?? throw new InvalidOperationException("The stored file does not read as an event");
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//webmail//EN\r\nMETHOD:CANCEL\r\nBEGIN:VEVENT\r\n");
        sb.Append("UID:").Append(e.Uid).Append("\r\n");
        sb.Append("SEQUENCE:").Append(input.CancelSequence).Append("\r\n");
        sb.Append("DTSTAMP:").Append(input.NowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("ORGANIZER").Append(ReplyComposer.Cn(input.Organizer.Name)).Append(":mailto:").Append(input.Organizer.Email).Append("\r\n");
        foreach (var recipient in input.Recipients) sb.Append("ATTENDEE:mailto:").Append(recipient).Append("\r\n");
        if (e.DtStartLine is { } start) sb.Append(start).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(e.Summary)) sb.Append("SUMMARY:").Append(ReplyComposer.EscapeText(e.Summary)).Append("\r\n");
        sb.Append("STATUS:CANCELLED\r\nEND:VEVENT\r\nEND:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>The event's own zone — "dans le fuseau de l'événement" — read off its DTSTART; UTC otherwise.</summary>
    private static string ZoneOf(ParsedInvitation e)
    {
        const string marker = "TZID=";
        var line = e.DtStartLine ?? string.Empty;
        var at = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return "UTC";
        var end = line.IndexOfAny([';', ':'], at + marker.Length);
        return end < 0 ? "UTC" : line[(at + marker.Length)..end].Trim('"');
    }
}
```

`ReplyComposer.Cn(string?)` rend `;CN=…` ou vide (vérifier sa forme exacte : en 5e1 il produit le
paramètre complet à concaténer après `ORGANIZER`/`ATTENDEE`) et `EscapeText` échappe `\ ; , \n` ;
les deux passent `internal static`. `InvitationText.When` attend un fuseau IANA : si `ZoneOf`
rend un nom inconnu, `When` doit retomber sur UTC — vérifier qu'il le fait déjà (5e1 le fait pour
le fuseau du navigateur) ; sinon, `IcsTimeZones.IsKnownIana(zone) ? zone : "UTC"` dans `ZoneOf`.

- [ ] **Step 5 : `dotnet test --filter FullyQualifiedName~InvitationMailer|FullyQualifiedName~InvitationText` vert.**

- [ ] **Step 6 : `ServiceMailQueue` — tests (rouges)**

```csharp
public class ServiceMailQueueTests
{
    private readonly Mock<ISmtpConnectionFactory> _smtp = new();
    private readonly Mock<ISmtpSession> _session = new();
    private readonly Mock<ILogger<ServiceMailQueue>> _logger = new();

    private ServiceMailQueue Create(string host = "mail.weesky.be")
    {
        var services = new ServiceCollection();
        services.AddSingleton(_smtp.Object);
        var options = new Mock<IOptionsMonitor<SchedulingOptions>>();
        options.SetupGet(o => o.CurrentValue).Returns(new SchedulingOptions
        {
            Smtp = new ServiceSmtpOptions { Host = host, Port = 587, Login = "noreply-agenda@weesky.net", Password = "pw" },
            RetrySeconds = 0,
        });
        return new ServiceMailQueue(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), options.Object, _logger.Object);
    }

    private static QueuedMail Mail() => new(new MimeMessage { Subject = "Invitation" }, "Invitation web-1111");

    [Fact]
    public void TryEnqueue_RefusesWithoutAServiceAccount()
    {
        Assert.False(Create(host: "").TryEnqueue(Mail()));
    }

    [Fact]
    public async Task Sends_WithTheServiceLogin_OnceTheHostRuns()
    {
        MailAccountConnection? opened = null;
        var sent = new TaskCompletionSource();
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Callback<MailAccountConnection, CancellationToken>((c, _) => opened = c)
            .ReturnsAsync(Result.Success(_session.Object));
        _session.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent.TrySetResult()).ReturnsAsync(Result.Success());
        var queue = Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await queue.StartAsync(cts.Token);

        Assert.True(queue.TryEnqueue(Mail()));
        await sent.Task.WaitAsync(cts.Token);
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal("noreply-agenda@weesky.net", opened!.Username);
        Assert.Equal(("mail.weesky.be", 587), (opened.SmtpHost, opened.SmtpPort));
    }

    [Fact]
    public async Task TriesTwice_ThenGivesUpWithAnError()
    {
        var attempts = 0;
        var done = new TaskCompletionSource();
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Callback(() => { if (++attempts == 2) done.TrySetResult(); })
            .ReturnsAsync(Result.Failure<ISmtpSession>("down"));
        var queue = Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await queue.StartAsync(cts.Token);

        queue.TryEnqueue(Mail());
        await done.Task.WaitAsync(cts.Token);
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(2, attempts);
        _logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("web-1111")), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
```

- [ ] **Step 7 : `ServiceMailQueue`**

```csharp
using System.Threading.Channels;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

internal sealed record QueuedMail(MimeMessage Message, string Description);

/// <summary>Décision 10: the mails a device's write owes the invitees, sent out of any session by
/// the service account — after the PUT has answered, one try then one more, then a line in the
/// log. In memory: a restart during the wait loses the mail, and says nothing.</summary>
public interface IServiceMailQueue
{
    bool TryEnqueue(QueuedMail mail);
}

internal sealed class ServiceMailQueue(
    IServiceScopeFactory scopes, IOptionsMonitor<SchedulingOptions> options, ILogger<ServiceMailQueue> logger)
    : BackgroundService, IServiceMailQueue
{
    private const int Capacity = 1000;
    private readonly Channel<QueuedMail> _channel = Channel.CreateBounded<QueuedMail>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public bool TryEnqueue(QueuedMail mail)
    {
        if (!options.CurrentValue.Enabled)
        {
            logger.LogWarning("No service account configured; invitation mail not sent: {Description}", mail.Description);
            return false;
        }
        if (_channel.Writer.TryWrite(mail)) return true;
        logger.LogError("Invitation mail queue full; not sent: {Description}", mail.Description);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var mail in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            if (await TrySendAsync(mail, stoppingToken)) continue;
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, options.CurrentValue.RetrySeconds)), stoppingToken);
            if (!await TrySendAsync(mail, stoppingToken))
                logger.LogError("Invitation mail abandoned after two tries: {Description}", mail.Description);
        }
    }

    private async Task<bool> TrySendAsync(QueuedMail mail, CancellationToken cancellationToken)
    {
        try
        {
            var smtp = options.CurrentValue.Smtp;
            var account = new MailAccountConnection("scheduling", true, string.Empty, 0, SecureSocketOptions.None,
                smtp.Host, smtp.Port, SecureSocketOptions.StartTls, null, null, smtp.Login, new PasswordCredential(smtp.Password));
            using var scope = scopes.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<ISmtpConnectionFactory>();
            var opened = await factory.OpenAsync(account, cancellationToken);
            if (opened.IsFailure) { logger.LogWarning("Service SMTP unavailable ({Error}): {Description}", opened.Error, mail.Description); return false; }
            await using var session = opened.Value;
            var sent = await session.SendAsync(mail.Message, cancellationToken);
            if (sent.IsFailure) { logger.LogWarning("Service SMTP refused ({Error}): {Description}", sent.Error, mail.Description); return false; }
            logger.LogInformation("Invitation mail sent by the service account: {Description}", mail.Description);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Service SMTP threw: {Description}", mail.Description);
            return false;
        }
    }
}
```

Enregistrement (dans `AddMailServices`) :

```csharp
        services.AddSingleton<ServiceMailQueue>();
        services.AddSingleton<IServiceMailQueue>(sp => sp.GetRequiredService<ServiceMailQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<ServiceMailQueue>());
```

Le `Description` porte le genre, l'`UID`, la `SEQUENCE` et les destinataires — jamais le corps
(décision 10). `MailAccountConnection` : si son constructeur a une forme différente de celle
relevée (`Models/Mail/MailAccountConnection.cs`), reprendre l'ordre exact du record ; `Port 587`
en `StartTls` est celui testé le 12 septembre 2026.

- [ ] **Step 8 : `cd src && dotnet test` vert ; commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
feat(agenda): 5e2, les mails REQUEST/CANCEL de l'organisateur et la file du compte de service

Double forme text/calendar + invitation.ics, sujets FR/EN, un second essai puis journal.
EOF
```

---

### Task 5 : le crochet — `InvitationScheduler`, branché sur les deux portes

**Files:**
- Create: `Models/Calendar/WriteOrigin.cs` (déplacé de `SchedulingDecider.cs`, rendu `public`), `Models/Calendar/SchedulingReport.cs`
- Create: `Services/Calendar/Scheduling/InvitationScheduler.cs`
- Modify: `Models/Calendar/CreatedId.cs`, `Controllers/CalendarEventsController.cs`, `Controllers/CalDavController.cs`, `Configuration/ApplicationServicesConfiguration.cs`
- Test: `Services/Calendar/Scheduling/InvitationSchedulerStoreTests.cs`, `Controllers/CalendarEventsControllerTests.cs`, `Controllers/CalDavSchedulingTests.cs`

**Interfaces — Produces :**

```csharp
public enum WriteOrigin { Webmail, Device }
public sealed record SchedulingReport(string? Owner, int Sent);
public sealed record CreatedId(Guid Id, SchedulingReport? Scheduling = null);
public sealed record EventUpdated(SchedulingReport Scheduling);
public interface IInvitationScheduler
{
    /// Called by a door after ONE successful write. Never throws; never blocks on SMTP.
    /// `session` opens the user's own SMTP session, called only when a mail is due from the webmail; null from a device.
    /// `language` null: read from the user's ui.language preference.
    Task<SchedulingReport> AfterWriteAsync(User user, EventChange change, WriteOrigin origin,
        Func<CancellationToken, Task<MailAccountConnection?>>? session, string? language, CancellationToken cancellationToken);
}
```

- [ ] **Step 1 : tests du planificateur sur de vrais stores (rouges)**

`InvitationSchedulerStoreTests.cs`, câblé comme `InvitationResponderStoreTests` (même
`InitializeAsync`, mêmes `PreferencesTestDbContext`/`TestCalendarSyncStore`/`CalendarEventStore`/`DavCalendarWriter`) :

```csharp
public sealed class InvitationSchedulerStoreTests : IAsyncLifetime
{
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly Mock<IUserAddresses> _addresses = new();
    private readonly Mock<IOrganizerIdentity> _organizer = new();
    private readonly Mock<IMailSender> _sender = new();
    private readonly Mock<IServiceMailQueue> _queue = new();
    private readonly Mock<IUserPreferenceStore> _preferences = new();
    private readonly List<MimeMessage> _sentBySession = [];
    private readonly List<QueuedMail> _queued = [];
    private readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private string database = "";
    private User user = null!;
    private PreferencesTestDbContext context = null!;
    private CalendarEventStore events = null!;
    private DavCalendarWriter writer = null!;
    private Guid calendar;
    private InvitationScheduler scheduler = null!;

    public async Task InitializeAsync()
    {
        database = Guid.NewGuid().ToString();
        var (_, userId, calendarId) = await CalendarStoreTestFactory.SeedAsync(database);
        calendar = calendarId;
        user = new User("alice@weesky.be") { WebmailUid = userId, FullName = "Alice" };
        context = new PreferencesTestDbContext(database);
        var sync = new TestCalendarSyncStore(context);
        events = new CalendarEventStore(context, sync, NullLogger<CalendarEventStore>.Instance);
        writer = new DavCalendarWriter(events, sync, context, NullLogger<DavCalendarWriter>.Instance);
        _addresses.Setup(a => a.ForPrincipalAsync(user, None)).ReturnsAsync(["alice@weesky.be", "alice@weesky.net"]);
        _organizer.Setup(o => o.ResolveAsync(user, None)).ReturnsAsync(new OrganizerWrite("alice@weesky.be", "Alice"));
        _sender.Setup(s => s.SendBuiltAsync(user, Conn, It.IsAny<MimeMessage>(), None))
            .Callback<User, MailAccountConnection, MimeMessage, CancellationToken>((_, _, m, _) => _sentBySession.Add(m))
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));
        _queue.Setup(q => q.TryEnqueue(It.IsAny<QueuedMail>())).Callback<QueuedMail>(_queued.Add).Returns(true);
        _preferences.Setup(p => p.GetAsync(userId, None)).ReturnsAsync([new UserPreference { PreferenceKey = "ui.language", PreferenceValue = "fr" }]);
        scheduler = new InvitationScheduler(events, writer, _addresses.Object, _organizer.Object, _sender.Object, _queue.Object,
            _preferences.Object, TimeProvider.System, NullLogger<InvitationScheduler>.Instance);
    }

    public Task DisposeAsync() { context.Dispose(); return Task.CompletedTask; }

    private Task<MailAccountConnection?> Session(CancellationToken _) => Task.FromResult<MailAccountConnection?>(Conn);

    /// The file as the webmail would have written it: put through the DAV writer under a name, so
    /// the row, its revisions and its sync rank exist as in production.
    private async Task<(string DavName, string Ics)> StoredAsync(string ics, string? owner = null, string? hash = null)
    {
        var name = $"{Guid.NewGuid()}.ics";
        Assert.Equal(DavWriteStatus.Created, (await writer.PutAsync(user.WebmailUid, calendar, name, ics, None)).Status);
        if (owner is not null) await events.SetSchedulingAsync(user.WebmailUid, calendar, name, owner, hash, None);
        return (name, ics);
    }

    private async Task<CalendarEvent> RowAsync(string davName) =>
        await context.CalendarEvents.AsNoTracking().SingleAsync(e => e.DavName == davName);

    private static string Hash(string ics) => SchedulingShape.HashOf(SchedulingShape.Of(ics)!);

    [Fact]
    public async Task FirstInvitationFromTheWebmail_SendsBySession_AndStampsTheColumns()
    {
        var (name, ics) = await StoredAsync(InvitationParserTests.Fixture("webmail-invited"));

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Webmail, Session, "fr", None);

        Assert.Equal(new SchedulingReport("webmail", 1), report);
        var mail = Assert.Single(_sentBySession);
        Assert.Equal("Invitation : Réunion de rentrée", mail.Subject);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"], mail.To.Mailboxes.Select(m => m.Address).Order());
        Assert.Empty(_queued);
        var row = await RowAsync(name);
        Assert.Equal(("webmail", Hash(ics)), (row.SchedulingOwner, row.SchedulingHash));
        Assert.Equal(0, InvitationParser.SequenceOf(row.IcsRaw));
    }

    [Fact]
    public async Task ADeviceMovingAnOverride_WithoutBumping_GetsASecondWrite_AndTheQueue()
    {
        var before = InvitationParserTests.Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var moved = InvitationParserTests.Fixture("webmail-invited-override").Replace("SEQUENCE:1", "SEQUENCE:0");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, moved, None);
        var rankAfterDevice = put.Sequence;
        var revisionsAfterDevice = await context.CalendarRevisions.CountAsync();

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, moved), WriteOrigin.Device, null, null, None);

        Assert.Equal(new SchedulingReport("webmail", 1), report);
        Assert.Empty(_sentBySession);
        var queued = Assert.Single(_queued);
        Assert.Equal("Mise à jour : Réunion de rentrée", queued.Message.Subject);
        var row = await RowAsync(name);
        Assert.Equal(1, InvitationParser.SequenceOf(row.IcsRaw));
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Brussels:20261019T100000\r\nDTSTAMP:20260913T100000Z\r\nSEQUENCE:0", row.IcsRaw);
        Assert.True(row.SyncSequence > rankAfterDevice);
        Assert.Equal(revisionsAfterDevice + 1, await context.CalendarRevisions.CountAsync());
        Assert.Equal(RevisionCause.Scheduling, (await context.CalendarRevisions.OrderByDescending(r => r.Id).FirstAsync()).Cause);
        Assert.Equal(Hash(moved), row.SchedulingHash);
        var calendarPart = ((MultipartAlternative)((Multipart)queued.Message.Body)[0]).OfType<TextPart>().Last().Text;
        Assert.Contains("\r\nSEQUENCE:1\r\n", calendarPart);
    }

    [Fact]
    public async Task ADeviceThatBumped_GetsNoSecondWrite()
    {
        var before = InvitationParserTests.Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var moved = InvitationParserTests.Fixture("webmail-invited-override");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, moved, None);
        var revisions = await context.CalendarRevisions.CountAsync();

        await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, moved), WriteOrigin.Device, null, null, None);

        Assert.Equal(revisions, await context.CalendarRevisions.CountAsync());
        Assert.Equal(put.Sequence, (await RowAsync(name)).SyncSequence);
        Assert.Single(_queued);
    }

    [Fact]
    public async Task AnAnswerWrittenByAPhone_SendsNothing_AndKeepsTheColumns()
    {
        var before = InvitationParserTests.Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var answered = before.Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=ACCEPTED");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, answered, None);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, answered), WriteOrigin.Device, null, null, None);

        Assert.Equal(new SchedulingReport("webmail", 0), report);
        Assert.Empty(_queued);
        Assert.Equal(("webmail", Hash(before)), ((await RowAsync(name)).SchedulingOwner, (await RowAsync(name)).SchedulingHash));
    }

    [Fact]
    public async Task ADeletion_CancelsEveryone_WithTheSequencePlusOne()
    {
        var before = InvitationParserTests.Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var outcome = await writer.DeleteAsync(user.WebmailUid, calendar, name, None);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, outcome.Replaced, null), WriteOrigin.Webmail, Session, "en", None);

        Assert.Equal(new SchedulingReport(null, 1), report);
        var mail = Assert.Single(_sentBySession);
        Assert.Equal("Cancelled: Réunion de rentrée", mail.Subject);
        Assert.Contains("SEQUENCE:1", ((MultipartAlternative)((Multipart)mail.Body)[0]).OfType<TextPart>().Last().Text);
    }

    [Fact]
    public async Task AnEventNobodyOwns_WrittenByADevice_SendsNothing()
    {
        var (name, ics) = await StoredAsync(InvitationParserTests.Fixture("webmail-invited"));
        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Device, null, null, None);
        Assert.Equal(new SchedulingReport(null, 0), report);
        Assert.Null((await RowAsync(name)).SchedulingOwner);
    }

    [Fact]
    public async Task WithoutASession_TheWebmailFallsBackToTheQueue_AndAFailedSendStillStampsTheColumns()
    {
        var (name, ics) = await StoredAsync(InvitationParserTests.Fixture("webmail-invited"));
        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Webmail, _ => Task.FromResult<MailAccountConnection?>(null), "fr", None);
        Assert.Equal(new SchedulingReport("webmail", 1), report);
        Assert.Single(_queued);

        // A device's change the queue refuses (no service account): the mail is lost and logged,
        // never resent — the columns still say the invitees were owed this version (décision 10).
        _queued.Clear();
        _queue.Setup(q => q.TryEnqueue(It.IsAny<QueuedMail>())).Returns(false);
        var owned = InvitationParserTests.Fixture("webmail-invited").Replace("web-1111-2222", "web-3333");
        var (other, _) = await StoredAsync(owned, "webmail", Hash(owned));
        var moved = owned.Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var put = await writer.PutAsync(user.WebmailUid, calendar, other, moved, None);
        var failed = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, other, put.Replaced, moved), WriteOrigin.Device, null, null, None);
        Assert.Equal(new SchedulingReport("webmail", 0), failed);
        Assert.Equal(Hash(moved), (await RowAsync(other)).SchedulingHash);
    }

    [Fact]
    public async Task NeverThrows()
    {
        _addresses.Setup(a => a.ForPrincipalAsync(user, None)).ThrowsAsync(new InvalidOperationException("boom"));
        var (name, ics) = await StoredAsync(InvitationParserTests.Fixture("webmail-invited"));
        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Webmail, Session, "fr", None);
        Assert.Equal(new SchedulingReport(null, 0), report);
    }
}
```

- [ ] **Step 2 : le planificateur**

```csharp
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

public interface IInvitationScheduler
{
    Task<SchedulingReport> AfterWriteAsync(User user, EventChange change, WriteOrigin origin,
        Func<CancellationToken, Task<MailAccountConnection?>>? session, string? language, CancellationToken cancellationToken);
}

/// <summary>
/// Décision 9's hook, called by the two doors after their write: decides from the file before
/// and the file after, advances a SEQUENCE a client left behind by one more textual write,
/// composes, hands the mails to the user's session or to the service queue, and stamps the two
/// columns. It never throws and never waits on SMTP out of session (décision 10).
/// </summary>
internal sealed class InvitationScheduler(
    ICalendarEventStore store, IDavCalendarWriter writer, IUserAddresses addresses, IOrganizerIdentity organizer,
    IMailSender sender, IServiceMailQueue queue, IUserPreferenceStore preferences, TimeProvider clock,
    ILogger<InvitationScheduler> logger) : IInvitationScheduler
{
    private const string LanguageKey = "ui.language";

    public async Task<SchedulingReport> AfterWriteAsync(
        User user, EventChange change, WriteOrigin origin,
        Func<CancellationToken, Task<MailAccountConnection?>>? session, string? language, CancellationToken cancellationToken)
    {
        try { return await RunAsync(user, change, origin, session, language, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduling failed after a write of {DavName}; invitees not told", change.DavName);
            return new SchedulingReport(change.Before?.SchedulingOwner, 0);
        }
    }

    private async Task<SchedulingReport> RunAsync(
        User user, EventChange change, WriteOrigin origin,
        Func<CancellationToken, Task<MailAccountConnection?>>? session, string? language, CancellationToken cancellationToken)
    {
        var own = (await addresses.ForPrincipalAsync(user, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var before = new SchedulingBefore(change.Before?.Ics, change.Before?.SchedulingOwner, change.Before?.SchedulingHash);
        var decision = SchedulingDecider.Decide(new SchedulingInput(before, change.After, origin, own));
        var after = change.After;
        var sent = 0;

        if (decision.Mails.Count > 0)
        {
            if (after is not null && before.Ics is not null && before.Hash is not null)
                after = await AdvancedAsync(user, change, before.Ics, after, cancellationToken);
            var source = after ?? before.Ics!;
            var host = await HostOfAsync(user, source, cancellationToken);
            var lang = language ?? await LanguageAsync(user, cancellationToken);
            var cancelSequence = InvitationParser.SequenceOf(source) + (change.After is null ? 1 : 0);
            var now = clock.GetUtcNow().UtcDateTime;
            var uid = IcsDocument.TryLoad(source) is { } parsed ? IcsDocument.MasterOf(parsed)?.Uid : null;
            // The user's session is opened once, for every mail of this write, and only from the webmail.
            var connection = origin == WriteOrigin.Webmail && session is not null ? await session(cancellationToken) : null;
            foreach (var mail in decision.Mails)
            {
                var message = InvitationMailer.Compose(new InvitationMailInput(mail.Kind, source, cancelSequence, mail.Recipients, host, lang, now));
                var description = $"{mail.Kind} {uid} seq {InvitationParser.SequenceOf(source)} to {string.Join(", ", mail.Recipients)}";
                if (await DeliverAsync(user, connection, message, description, cancellationToken)) sent++;
            }
        }

        if (decision.Owner != before.Owner || decision.Hash != before.Hash)
            await store.SetSchedulingAsync(user.WebmailUid, change.CalendarId, change.DavName, decision.Owner, decision.Hash, cancellationToken);
        return new SchedulingReport(decision.Owner, sent);
    }

    /// <summary>A client that changed a component without advancing its SEQUENCE: the server
    /// does, by one more write under the same name (décision 9). The file already stored stays
    /// the source of the mail when that write is refused.</summary>
    private async Task<string> AdvancedAsync(User user, EventChange change, string before, string after, CancellationToken cancellationToken)
    {
        var lagging = SchedulingShape.ChangedWithoutBump(before, after);
        if (lagging.Count == 0) return after;
        var bumped = SequenceRewriter.Bump(after, lagging);
        var outcome = await writer.PutAsync(user.WebmailUid, change.CalendarId, change.DavName, bumped, cancellationToken, cause: RevisionCause.Scheduling);
        if (outcome.Status is DavWriteStatus.Created or DavWriteStatus.Replaced) return bumped;
        logger.LogWarning("SEQUENCE could not be advanced on {DavName}: {Status}", change.DavName, outcome.Status);
        return after;
    }

    /// <summary>From = the file's ORGANIZER, the address the replies come back to; the resolved
    /// identity only when the file names none (a cancellation of a file that lost its line).</summary>
    private async Task<OrganizerWrite> HostOfAsync(User user, string ics, CancellationToken cancellationToken)
    {
        var parsed = InvitationParser.Read(IcsMethod.With(ics, "REQUEST")).Invitation;
        return parsed?.Organizer is { } o ? new OrganizerWrite(o.Email.Trim().ToLowerInvariant(), o.Name) : await organizer.ResolveAsync(user, cancellationToken);
    }

    private async Task<bool> DeliverAsync(
        User user, MailAccountConnection? connection, MimeKit.MimeMessage message, string description, CancellationToken cancellationToken)
    {
        if (connection is null) return queue.TryEnqueue(new QueuedMail(message, description));
        var sent = await sender.SendBuiltAsync(user, connection, message, cancellationToken);
        if (sent.IsSuccess) { logger.LogInformation("Invitation mail sent by the user's session: {Description}", description); return true; }
        logger.LogWarning("Invitation mail refused by the user's session ({Error}): {Description}", sent.Error, description);
        return false;
    }

    private async Task<string> LanguageAsync(User user, CancellationToken cancellationToken)
    {
        var value = (await preferences.GetAsync(user.WebmailUid, cancellationToken))
            .FirstOrDefault(p => p.PreferenceKey == LanguageKey)?.PreferenceValue;
        return value is not null && value.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "fr" : "en";
    }
}
```

Enregistrement :
`services.AddScoped<IInvitationScheduler, InvitationScheduler>();` ; `TimeProvider` est déjà
enregistré (le contrôleur CalDAV le reçoit). `IUserPreferenceStore` : vérifier le nom réel de
l'interface dans `Repositories/` (celle de `user_preferences`) et sa méthode de lecture.

- [ ] **Step 3 : `dotnet test --filter FullyQualifiedName~InvitationSchedulerStoreTests` vert.**

Si `ADeviceMovingAnOverride…` échoue sur le `Contains` de la surcharge (`SEQUENCE:0` attendu),
c'est que `ChangedWithoutBump` a compté la surcharge nouvelle comme en retard : la tâche 1 le
proscrit, reprendre son test.

- [ ] **Step 4 : la porte API — tests (rouges) puis code**

`CalendarEventsControllerTests` : le constructeur devient
`(ICalendarEventStore store, IUserAddresses addresses, IOrganizerIdentity organizer, IInvitationScheduler scheduler, IAccountConnectionResolver connections)`.
Ajouter `Mock<IInvitationScheduler> _scheduler` (défaut : `ReturnsAsync(new SchedulingReport(null, 0))`)
et `Mock<IAccountConnectionResolver> _connections` (défaut : `ReturnsAsync(Result.Success(TestConnections.Primary("john@example.com", "pw")))`).

```csharp
    [Fact]
    public async Task Create_CallsTheHookForItsChange_AndAnswersTheReport()
    {
        var id = Guid.NewGuid();
        var change = new EventChange(id, Guid.NewGuid(), $"{id}.ics", null, "BEGIN:VCALENDAR");
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EventWriteResult(id, [change])));
        _scheduler.Setup(s => s.AfterWriteAsync(It.IsAny<User>(), change, WriteOrigin.Webmail, It.IsNotNull<Func<CancellationToken, Task<MailAccountConnection?>>>(), "fr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulingReport("webmail", 2));
        var request = ValidRequest();
        request.Language = "fr";

        var result = await CreateController().Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedId>(Assert.IsType<ObjectResult>(result.Result).Value);
        Assert.Equal(new CreatedId(id, new SchedulingReport("webmail", 2)), created);
    }

    [Fact]
    public async Task Update_CallsTheHookOncePerChange_AndAnswers200WithTheSum()
    {
        var id = Guid.NewGuid();
        var changes = new[] { new EventChange(id, Guid.NewGuid(), "a.ics", new ReplacedVersion("old", "webmail", "h"), "new"), new EventChange(Guid.NewGuid(), Guid.NewGuid(), "b.ics", null, "split") };
        _store.Setup(s => s.UpdateAsync(Uid, id, EditScope.All, null, It.IsAny<EventWrite>(), "abc123", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EventWriteResult(id, changes)));
        _scheduler.Setup(s => s.AfterWriteAsync(It.IsAny<User>(), It.IsAny<EventChange>(), WriteOrigin.Webmail, It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulingReport("webmail", 1));

        var result = await CreateController().Update(id, ValidUpdateRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new EventUpdated(new SchedulingReport("webmail", 2)), ok.Value);
        _scheduler.Verify(s => s.AfterWriteAsync(It.IsAny<User>(), It.IsAny<EventChange>(), WriteOrigin.Webmail, It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), "en", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Delete_CallsTheHook_AndStays204()
    {
        var id = Guid.NewGuid();
        var change = new EventChange(id, Guid.NewGuid(), "a.ics", new ReplacedVersion("old", "webmail", "h"), null);
        _store.Setup(s => s.DeleteAsync(Uid, id, EditScope.All, null, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EventWriteResult(id, [change])));

        Assert.IsType<NoContentResult>(await CreateController().Delete(id, EditScope.All, null, CancellationToken.None));
        _scheduler.Verify(s => s.AfterWriteAsync(It.IsAny<User>(), change, WriteOrigin.Webmail, It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheSession_IsResolvedLazily_AndNullWhenTheResolverRefuses()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new EventWriteResult(id, [new EventChange(id, Guid.NewGuid(), "a.ics", null, "x")])));
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure<MailAccountConnection>("no cookie"));
        Func<CancellationToken, Task<MailAccountConnection?>>? opener = null;
        _scheduler.Setup(s => s.AfterWriteAsync(It.IsAny<User>(), It.IsAny<EventChange>(), WriteOrigin.Webmail, It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<User, EventChange, WriteOrigin, Func<CancellationToken, Task<MailAccountConnection?>>?, string?, CancellationToken>((_, _, _, o, _, _) => opener = o)
            .ReturnsAsync(new SchedulingReport(null, 0));

        await CreateController().Create(ValidRequest(), CancellationToken.None);

        _connections.Verify(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(await opener!(CancellationToken.None));
    }
```

Contrôleur :

```csharp
    private async Task<SchedulingReport> ScheduleAsync(EventWriteResult written, string language, CancellationToken cancellationToken)
    {
        var owner = (string?)null;
        var sent = 0;
        foreach (var change in written.Changes)
        {
            var report = await scheduler.AfterWriteAsync(AuthenticatedUser, change, WriteOrigin.Webmail, OpenSessionAsync, language, cancellationToken);
            sent += report.Sent;
            owner ??= report.Owner;
        }
        return new SchedulingReport(owner, sent);
    }

    /// <summary>The user's own SMTP session, opened only when a mail is due: the primary account,
    /// since the organizer is always its identity (décision 10). Null — the queue takes over —
    /// when the cookie carries no credentials.</summary>
    private async Task<MailAccountConnection?> OpenSessionAsync(CancellationToken cancellationToken)
    {
        var resolved = await connections.ResolveAsync(AuthenticatedUser, Request, cancellationToken);
        return resolved.IsSuccess ? resolved.Value : null;
    }
```

`Create` : `var report = await ScheduleAsync(created.Value, request.Language, ct); return StatusCode(201, new CreatedId(created.Value.EventId, report));`.
`Update` : `return updated.IsSuccess ? Ok(new EventUpdated(await ScheduleAsync(updated.Value, request.Language, ct))) : MapFailure(updated.Error);`
avec `[ProducesResponseType(StatusCodes.Status200OK)]` à la place du 204 et le commentaire
`<response code="200">Saved ; scheduling says what was sent</response>`.
`Delete` : après succès, `await ScheduleAsync(deleted.Value, "en", ct)` — la suppression n'a pas de
corps, donc pas de langue : ajouter un paramètre de requête `string language = "en"` à `Delete`
(`[FromQuery]`) que le frontend renseigne (tâche 7). `MailAccountConnection` et `HttpRequest` :
usings `Models.Mail` et `Microsoft.AspNetCore.Http`.

- [ ] **Step 5 : la porte CalDAV — test (rouge) puis code**

`Controllers/CalDavSchedulingTests.cs`, sur `DavTestServer` avec un enregistreur :

```csharp
internal sealed class RecordingScheduler : IInvitationScheduler
{
    public List<(EventChange Change, WriteOrigin Origin)> Calls { get; } = [];
    public bool Throw { get; set; }
    public Task<SchedulingReport> AfterWriteAsync(User user, EventChange change, WriteOrigin origin,
        Func<CancellationToken, Task<MailAccountConnection?>>? session, string? language, CancellationToken cancellationToken)
    {
        Calls.Add((change, origin));
        if (Throw) throw new InvalidOperationException("boom");
        Assert.Null(session);
        Assert.Null(language);
        return Task.FromResult(new SchedulingReport(null, 0));
    }
}

public sealed class CalDavSchedulingTests : IAsyncLifetime
{
    private readonly RecordingScheduler recorder = new();
    private DavTestServer server = null!;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync(overrides: services => services.AddSingleton<IInvitationScheduler>(recorder));
        await server.GivenCalendar("work");   // reprendre l'aide de CalDavPutTests
    }

    public async Task DisposeAsync() => await server.DisposeAsync();

    [Fact]
    public async Task Put_ThenPut_ThenDelete_CallTheHook_WithBeforeAndAfter()
    {
        var first = InvitationParserTests.Fixture("webmail-invited");
        var second = first.Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var url = server.EventUrl("work", "web.ics");   // l'aide de CalDavPutTests qui bâtit l'URL d'un membre

        Assert.Equal(HttpStatusCode.Created, (await server.PutAsync(url, first)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await server.PutAsync(url, second)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await server.DeleteAsync(url)).StatusCode);

        Assert.Equal(3, recorder.Calls.Count);
        Assert.All(recorder.Calls, c => Assert.Equal(WriteOrigin.Device, c.Origin));
        Assert.Null(recorder.Calls[0].Change.Before);
        Assert.Equal(first, recorder.Calls[0].Change.After);
        Assert.Equal(first, recorder.Calls[1].Change.Before!.Ics);
        Assert.Equal(second, recorder.Calls[1].Change.After);
        Assert.Equal(second, recorder.Calls[2].Change.Before!.Ics);
        Assert.Null(recorder.Calls[2].Change.After);
        Assert.All(recorder.Calls, c => Assert.Equal("web.ics", c.Change.DavName));
    }

    [Fact]
    public async Task ARefusedPut_DoesNotCallTheHook()
    {
        var url = server.EventUrl("work", "web.ics");
        await server.PutAsync(url, InvitationParserTests.Fixture("webmail-invited"));
        var refused = await server.PutAsync(url, InvitationParserTests.Fixture("webmail-invited"), ifMatch: "\"stale\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        Assert.Single(recorder.Calls);
    }
}
```

Les aides `PutAsync`/`DeleteAsync`/`EventUrl`/`GivenCalendar` sont celles de `CalDavPutTests` et
`CalDavDeleteTests` : reprendre leurs noms exacts (le sous-agent lit ces deux fichiers d'abord).
Le `RecordingScheduler` est enregistré `Singleton` pour que les appels survivent aux scopes des
requêtes. Pas de test « le crochet lève » ici : l'interface est tenue par `InvitationScheduler`
lui-même (`NeverThrows` de la tâche), et le contrôleur l'appelle sans `try` de plus.

`CalDavController` : constructeur `+ IInvitationScheduler scheduler` ; dans `PutEventAsync`, après
les archivages de refus (L183–184) et avant `AnswerPutOutcomeAsync` :

```csharp
        if (outcome.Status is DavWriteStatus.Created or DavWriteStatus.Replaced)
            await scheduler.AfterWriteAsync(user, new EventChange(null, calendar.Id, davName, outcome.Replaced, body),
                WriteOrigin.Device, null, null, cancellationToken);
```

Dans `DeleteEventAsync`, remplacer l'appel imbriqué par :

```csharp
        var outcome = await writer.DeleteAsync(AuthenticatedUser.WebmailUid, calendar.Id, member.DavName, cancellationToken, HeaderOrNull(Request.Headers.IfMatch));
        if (outcome.Status == DavWriteStatus.Deleted)
            await scheduler.AfterWriteAsync(AuthenticatedUser, new EventChange(null, calendar.Id, member.DavName, outcome.Replaced, null),
                WriteOrigin.Device, null, null, cancellationToken);
        trace.Condition = await AnswerOutcomeAsync(outcome, cancellationToken);
```

`DavTestServer.ConfigureServices` enregistre les services concrets du DAV : y ajouter
`IInvitationScheduler` → `InvitationScheduler` et ses dépendances (`IOrganizerIdentity`,
`IMailSender`, `IServiceMailQueue`, `IUserPreferenceStore`, `IUserAddresses`) **ou** un
`NullScheduler` interne à l'infrastructure de test (`Calls` ignorés, `SchedulingReport(null, 0)`)
que chaque test peut remplacer par `overrides` — la seconde voie garde les treize suites CalDAV
existantes sans SMTP simulé. Choisir la seconde.

- [ ] **Step 6 : `cd src && dotnet test` vert** (les treize suites CalDAV comprises) ; **commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
feat(agenda): 5e2, le crochet d'invitation après chaque écriture, webmail et CalDAV

Décide, avance SEQUENCE, compose, remet par la session ou la file ; scheduling dans les réponses de l'API.
EOF
```

---

### Task 6 : les réponses — `REPLY` lu par le bloc, appliqué par `ApplyReply`

**Files:**
- Create: `Models/Mail/InvitationReply.cs`, `Models/Calendar/ApplyReplyRequest.cs`
- Create: `Services/Calendar/Invitations/InvitationPartLoader.cs`, `InvitationReplyApplier.cs`
- Modify: `Models/Mail/MailInvitation.cs` (`InvitationMethod.Reply`, `Reply`), `Services/Calendar/Invitations/InvitationParser.cs`, `InvitationReader.cs`, `InvitationResponder.cs`, `Controllers/CalendarInvitationsController.cs`, `Configuration/ApplicationServicesConfiguration.cs`
- Create: `snoopy.microservice.Tests/Fixtures/Invitations/outlook-reply-upper.ics`, `google-reply-occurrence.ics`
- Test: `Services/Calendar/Invitations/InvitationParserTests.cs`, `InvitationReaderTests.cs`, `InvitationReplyApplierStoreTests.cs`, `InvitationResponderTests.cs`, `Controllers/CalendarInvitationsControllerTests.cs`

**Interfaces — Produces :**

```csharp
public enum InvitationMethod { Request, Cancel, Reply }
public enum ReplyStatus { Applicable, UnknownUid, NotOwner, UnknownAttendee, OccurrenceOnly, Stale }
/// The guest who answered, what they said, whether the calendar may take it, whether it already has.
public sealed record InvitationReply(string Email, string? Name, string PartStat, ReplyStatus Status, bool Applied);
// MailInvitation : public InvitationReply? Reply { get; init; }
public sealed class ApplyReplyRequest { [Required] public string Folder { get; set; } = ""; public uint Uid { get; set; } [Required(AllowEmptyStrings = true)] public string Part { get; set; } = ""; }
public sealed record ApplyReplyResponse(MailInvitation Invitation, bool Applied, string? ApplyError);
public interface IInvitationReplyApplier { Task<Result<ApplyReplyResponse, ResponderFailure>> ApplyAsync(User user, MailAccountConnection connection, ApplyReplyRequest request, CancellationToken ct); }
internal sealed class InvitationPartLoader(IMailMessageRepository messages) { Task<Result<string, ResponderFailure>> LoadAsync(User user, MailAccountConnection connection, string folder, uint uid, string part, CancellationToken ct); }
// codes stables : "reply_not_a_reply" (400), "reply_not_applicable" (400) ; les codes du writer (calendar_busy 502, calendar_conflict 409, calendar_refused 422) voyagent dans applyError avec un 200
```

Sur un `REPLY`, le bloc porte `method: Reply`, `reply`, `inCalendar` (`Absent` / `Current` /
`Newer`), `calendarId` de la ligne trouvée, `savedPartStat` = ce que le fichier stocké dit de cet
invité ; `addressedTo` et `filePartStat` restent nuls. Décision 12 : le détail **lit**, l'encart
appelle `ApplyReply` une fois quand `reply.status == Applicable && !reply.applied`.

- [ ] **Step 1 : les fixtures**

`outlook-reply-upper.ics` (Outlook renvoie l'adresse en majuscules, et sans `CN`) :

```
BEGIN:VCALENDAR
PRODID:Microsoft Exchange Server 2010
VERSION:2.0
METHOD:REPLY
BEGIN:VEVENT
UID:web-1111-2222
SEQUENCE:0
DTSTAMP:20260914T081500Z
ORGANIZER:mailto:alice@weesky.be
ATTENDEE;PARTSTAT=TENTATIVE:MAILTO:MARC.DUPONT@EXAMPLE.ORG
DTSTART;TZID=Europe/Brussels:20261005T100000
SUMMARY:Provisoire : Réunion de rentrée
END:VEVENT
END:VCALENDAR
```

`google-reply-occurrence.ics` :

```
BEGIN:VCALENDAR
PRODID:-//Google Inc//Google Calendar 70.9054//EN
VERSION:2.0
METHOD:REPLY
BEGIN:VEVENT
UID:web-1111-2222
RECURRENCE-ID;TZID=Europe/Brussels:20261012T100000
SEQUENCE:0
DTSTAMP:20260914T091000Z
ORGANIZER:mailto:alice@weesky.be
ATTENDEE;CN=Julie;PARTSTAT=DECLINED:mailto:julie@example.net
DTSTART;TZID=Europe/Brussels:20261012T100000
SUMMARY:Réunion de rentrée
END:VEVENT
END:VCALENDAR
```

- [ ] **Step 2 : le parseur — test (rouge) puis code**

`InvitationParserTests` : remplacer `Reply_IsIgnored_NotUnreadable` par

```csharp
    [Fact]
    public void Reply_IsRead_WithItsOneAttendee()
    {
        var reading = InvitationParser.Read(Fixture("google-reply"));
        Assert.False(reading.Ignored);
        var reply = reading.Invitation!;
        Assert.Equal(InvitationMethod.Reply, reply.Method);
        Assert.Equal("aaaa1111-bbbb-2222-cccc-3333dddd4444", reply.Uid);
        var who = Assert.Single(reply.Attendees);
        Assert.Equal(("marc.dupont@example.org", "DECLINED"), (who.Email, who.PartStat));
        Assert.False(reply.OccurrenceOnly);

        Assert.Equal("MARC.DUPONT@EXAMPLE.ORG", Assert.Single(InvitationParser.Read(Fixture("outlook-reply-upper")).Invitation!.Attendees).Email);
        Assert.True(InvitationParser.Read(Fixture("google-reply-occurrence")).Invitation!.OccurrenceOnly);
    }
```

Dans `InvitationParser.Read`, la table des méthodes gagne `"REPLY" => InvitationMethod.Reply`
(le reste du parseur lit déjà les `ATTENDEE` avec leur `PARTSTAT` et `OccurrenceOnly`). Si le
parseur normalise l'adresse en minuscules, adapter l'assertion Outlook à `marc.dupont@example.org`
— l'appariement de `PartStatRewriter`/`PartStatOf` est de toute façon sans casse.

- [ ] **Step 3 : le lecteur — tests (rouges)**

`InvitationReaderTests` (mocks `IUserAddresses`, `ICalendarEventStore`) :

```csharp
    private static StoredEventRef Invited(string? owner = "webmail", string? ics = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "web.ics", (ics ?? InvitationParserTests.Fixture("webmail-invited")), owner);

    private static MailCalendarPart ReplyPart(string fixture) => new("2", InvitationParserTests.Fixture(fixture).Replace("aaaa1111-bbbb-2222-cccc-3333dddd4444", "web-1111-2222"), false);

    [Fact]
    public async Task Reply_ToAnEventTheWebmailInvited_IsApplicable_AndSaysWhatTheFileHolds()
    {
        var stored = Invited();
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([stored]);

        var block = (await Create().ReadAsync(_user, Conn, ReplyPart("google-reply"), CancellationToken.None))!;

        Assert.Equal(InvitationMethod.Reply, block.Method);
        Assert.Equal(new InvitationReply("marc.dupont@example.org", "Marc Dupont", "DECLINED", ReplyStatus.Applicable, Applied: false), block.Reply);
        Assert.Equal(InvitationPresence.Current, block.InCalendar);
        Assert.Equal(stored.CalendarId, block.CalendarId);
        Assert.Equal("NEEDS-ACTION", block.SavedPartStat);
        Assert.Null(block.AddressedTo);
    }

    [Fact]
    public async Task Reply_AlreadyInTheFile_IsApplied()
    {
        var stored = Invited(ics: InvitationParserTests.Fixture("webmail-invited").Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=DECLINED"));
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([stored]);

        var block = (await Create().ReadAsync(_user, Conn, ReplyPart("google-reply"), CancellationToken.None))!;

        Assert.True(block.Reply!.Applied);
        Assert.Equal(ReplyStatus.Applicable, block.Reply.Status);
    }

    [Theory]
    [InlineData(null, ReplyStatus.UnknownUid, InvitationPresence.Absent)]
    [InlineData("", ReplyStatus.NotOwner, InvitationPresence.Current)]
    public async Task Reply_ToAnUnknownUid_OrAnEventNotInvitedHere_IsNotApplicable(string? owner, ReplyStatus status, InvitationPresence presence)
    {
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner is null ? [] : [Invited(owner: null)]);

        var block = (await Create().ReadAsync(_user, Conn, ReplyPart("google-reply"), CancellationToken.None))!;

        Assert.Equal(status, block.Reply!.Status);
        Assert.Equal(presence, block.InCalendar);
    }

    [Fact]
    public async Task Reply_FromSomeoneNotInvited_OrForOneDate_OrStale_IsNotApplicable()
    {
        var stored = Invited(ics: InvitationParserTests.Fixture("webmail-invited").Replace("SEQUENCE:0", "SEQUENCE:2"));
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([stored]);
        Assert.Equal(ReplyStatus.Stale, (await Create().ReadAsync(_user, Conn, ReplyPart("google-reply"), CancellationToken.None))!.Reply!.Status);

        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([Invited()]);
        var stranger = new MailCalendarPart("2", ReplyPart("google-reply").Ics.Replace("marc.dupont@example.org", "paul@example.org"), false);
        Assert.Equal(ReplyStatus.UnknownAttendee, (await Create().ReadAsync(_user, Conn, stranger, CancellationToken.None))!.Reply!.Status);

        var oneDate = (await Create().ReadAsync(_user, Conn, ReplyPart("google-reply-occurrence"), CancellationToken.None))!;
        Assert.Equal(ReplyStatus.OccurrenceOnly, oneDate.Reply!.Status);
        Assert.True(oneDate.OccurrenceOnly);
        _events.Verify(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Reply_MatchesTheGuest_WhateverTheCase()
    {
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([Invited()]);
        var block = (await Create().ReadAsync(_user, Conn, ReplyPart("outlook-reply-upper"), CancellationToken.None))!;
        Assert.Equal(ReplyStatus.Applicable, block.Reply!.Status);
        Assert.Equal("TENTATIVE", block.Reply.PartStat);
    }
```

(`_user`, `Conn`, `Create()` : les aides existantes du fichier. Le `Times.Exactly(2)` compte les
deux lectures avec ligne — `Stale` et `UnknownAttendee` — et vérifie que le `REPLY` d'occurrence
n'a pas cherché en base.)

- [ ] **Step 4 : le lecteur**

`InvitationReader.ReadAsync` : après `InvitationParser.Read`, si `parsed.Method == Reply` →
`return Block(parsed, await ResolveReplyAsync(user, parsed, ct), part.Part)` où `ResolveReplyAsync`
rend un `InvitationContext` étendu d'un champ `Reply` (`InvitationReply?`, dernier positionnel,
`null` par défaut — les appels existants de `Block` ne changent pas) :

```csharp
    internal async Task<InvitationContext> ResolveReplyAsync(User user, ParsedInvitation parsed, CancellationToken cancellationToken)
    {
        var who = parsed.Attendees.FirstOrDefault(a => a.PartStat is not null) ?? parsed.Attendees.FirstOrDefault();
        if (who is null) return new InvitationContext(null, null, null, 0, null, InvitationPresence.Absent, null);
        var email = who.Email.Trim().ToLowerInvariant();
        var partStat = (who.PartStat ?? "NEEDS-ACTION").ToUpperInvariant();
        InvitationReply Reply(ReplyStatus status, bool applied = false) => new(email, who.Name, partStat, status, applied);

        if (parsed.OccurrenceOnly)
            return new InvitationContext(null, null, null, 0, null, InvitationPresence.Absent, Reply(ReplyStatus.OccurrenceOnly));
        var stored = (await events.FindByUidAsync(user.WebmailUid, parsed.Uid, cancellationToken)).FirstOrDefault();
        if (stored is null)
            return new InvitationContext(null, null, null, 0, null, InvitationPresence.Absent, Reply(ReplyStatus.UnknownUid));

        var savedSequence = InvitationParser.SequenceOf(stored.IcsRaw);
        var saved = InvitationParser.PartStatOf(stored.IcsRaw, email);
        var presence = parsed.Sequence < savedSequence ? InvitationPresence.Newer : InvitationPresence.Current;
        var status = stored.SchedulingOwner != SchedulingDecider.WebmailOwner ? ReplyStatus.NotOwner
            : presence == InvitationPresence.Newer ? ReplyStatus.Stale
            : saved is null ? ReplyStatus.UnknownAttendee
            : ReplyStatus.Applicable;
        return new InvitationContext(null, null, stored, savedSequence, saved, presence,
            Reply(status, applied: status == ReplyStatus.Applicable && string.Equals(saved, partStat, StringComparison.OrdinalIgnoreCase)));
    }
```

`Block` recopie `context.Reply` dans `MailInvitation.Reply`. Un `REPLY` sans `ATTENDEE` est
`Unreadable` avec la raison `"reply_without_attendee"` (le premier `return` ci-dessus devient
alors un bloc illisible plutôt qu'un contexte vide — choisir cette forme : `Block` sait déjà
produire un bloc `Unreadable` par `Reason`).

- [ ] **Step 5 : `InvitationPartLoader` et le répondeur**

Extraire la méthode privée `ReadPartAsync` du répondeur (L95–108 : `GetAttachmentAsync`, plafond,
`DecodeText`) dans `InvitationPartLoader.LoadAsync` ; le répondeur l'appelle. Dans le répondeur,
la compatibilité méthode/réponse devient explicite :

```csharp
        var compatible = parsed.Method switch
        {
            InvitationMethod.Request => request.Answer is not InvitationAnswer.Remove,
            InvitationMethod.Cancel => request.Answer is InvitationAnswer.Remove,
            _ => false,
        };
        if (!compatible) return Failed(400, Incompatible);
```

Test dans `InvitationResponderTests` : un `REPLY` (fixture `google-reply`) avec `Remove` → 400
`invitation_incompatible_answer`, rien d'écrit.

- [ ] **Step 6 : l'applicateur — tests sur de vrais stores (rouges)**

`InvitationReplyApplierStoreTests.cs`, câblé comme `InvitationResponderStoreTests` (le
`IMailMessageRepository` simulé rend la partie, `IUserAddresses` inutile ici) :

```csharp
    private async Task<StoredEventRef> InvitedAsync(string? owner = "webmail")
    {
        var name = $"{Guid.NewGuid()}.ics";
        await writer.PutAsync(user.WebmailUid, calendar, name, InvitationParserTests.Fixture("webmail-invited"), None);
        if (owner is not null) await events.SetSchedulingAsync(user.WebmailUid, calendar, name, owner, "h", None);
        return (await events.FindByUidAsync(user.WebmailUid, "web-1111-2222", None)).Single();
    }

    private void Part(string fixture) => _messages.Setup(m => m.GetAttachmentAsync(user, Conn, "INBOX", 7u, "2", None))
        .ReturnsAsync(Result.Success(new MailAttachmentContent(new MemoryStream(Encoding.UTF8.GetBytes(
            InvitationParserTests.Fixture(fixture).Replace("aaaa1111-bbbb-2222-cccc-3333dddd4444", "web-1111-2222"))), "text/calendar", "invite.ics", "utf-8")));
    // reprendre la forme exacte de MailAttachmentContent et du Setup de InvitationResponderStoreTests.Part

    private static readonly ApplyReplyRequest Request = new() { Folder = "INBOX", Uid = 7, Part = "2" };

    [Fact]
    public async Task Apply_WritesTheGuestsAnswer_IntoTheStoredFile_UnderItsOwnName_AndIsIdempotent()
    {
        var stored = await InvitedAsync();
        Part("google-reply");

        var first = (await applier.ApplyAsync(user, Conn, Request, None)).Value;

        Assert.True(first.Applied);
        Assert.Null(first.ApplyError);
        Assert.True(first.Invitation.Reply!.Applied);
        Assert.Equal("DECLINED", first.Invitation.SavedPartStat);
        var row = await context.CalendarEvents.AsNoTracking().SingleAsync(e => e.Id == stored.Id);
        Assert.Equal("DECLINED", InvitationParser.PartStatOf(row.IcsRaw, "marc.dupont@example.org"));
        Assert.Equal("ACCEPTED", InvitationParser.PartStatOf(row.IcsRaw, "julie@example.net"));
        Assert.Equal(("webmail", "h"), (row.SchedulingOwner, row.SchedulingHash));
        Assert.Equal(RevisionCause.Webmail, (await context.CalendarRevisions.OrderByDescending(r => r.Id).FirstAsync()).Cause);

        var revisions = await context.CalendarRevisions.CountAsync();
        var rank = row.SyncSequence;
        var second = (await applier.ApplyAsync(user, Conn, Request, None)).Value;
        Assert.True(second.Applied);
        Assert.Equal(revisions, await context.CalendarRevisions.CountAsync());
        Assert.Equal(rank, (await context.CalendarEvents.AsNoTracking().SingleAsync(e => e.Id == stored.Id)).SyncSequence);
    }

    [Fact]
    public async Task Apply_MatchesAnUppercaseAddress_WithoutChangingTheShape()
    {
        await InvitedAsync();
        Part("outlook-reply-upper");
        var before = SchedulingShape.Of((await events.FindByUidAsync(user.WebmailUid, "web-1111-2222", None)).Single().IcsRaw);

        var applied = (await applier.ApplyAsync(user, Conn, Request, None)).Value;

        Assert.True(applied.Applied);
        var after = (await events.FindByUidAsync(user.WebmailUid, "web-1111-2222", None)).Single().IcsRaw;
        Assert.Equal("TENTATIVE", InvitationParser.PartStatOf(after, "marc.dupont@example.org"));
        Assert.Equal(before, SchedulingShape.Of(after));
    }

    [Theory]
    [InlineData("google-reply-occurrence")]
    public async Task Apply_RefusesWhatTheReaderSaysIsNotApplicable(string fixture)
    {
        await InvitedAsync();
        Part(fixture);
        var result = await applier.ApplyAsync(user, Conn, Request, None);
        Assert.Equal((400, "reply_not_applicable"), (result.Error.Status, result.Error.Message));
    }

    [Fact]
    public async Task Apply_OnAnEventNotInvitedHere_OrUnknown_Is400()
    {
        await InvitedAsync(owner: null);
        Part("google-reply");
        Assert.Equal("reply_not_applicable", (await applier.ApplyAsync(user, Conn, Request, None)).Error.Message);
    }

    [Fact]
    public async Task Apply_OnARequest_Is400()
    {
        Part("google-request");
        Assert.Equal((400, "reply_not_a_reply"), ((await applier.ApplyAsync(user, Conn, Request, None)).Error.Status, (await applier.ApplyAsync(user, Conn, Request, None)).Error.Message));
    }
```

Et un test avec le writer **simulé** (`InvitationReplyApplierTests`, mocks partout) : `PutAsync`
rend `Busy` → `200`, `Applied: false`, `ApplyError: "calendar_busy"`, le bloc rendu porte
toujours `reply.applied: false`.

- [ ] **Step 7 : l'applicateur et le point d'entrée**

```csharp
public interface IInvitationReplyApplier
{
    Task<Result<ApplyReplyResponse, ResponderFailure>> ApplyAsync(
        User user, MailAccountConnection connection, ApplyReplyRequest request, CancellationToken cancellationToken);
}

/// <summary>Décision 12: the guest's PARTSTAT into their ATTENDEE line of the stored file, by the
/// PUT path under the stored name; a second application is byte-identical and the writer ignores
/// it. It never sends a mail and never calls the scheduler: the shape does not move.</summary>
internal sealed class InvitationReplyApplier(
    InvitationPartLoader parts, InvitationReader reader, IDavCalendarWriter writer, ILogger<InvitationReplyApplier> logger)
    : IInvitationReplyApplier
{
    internal const string NotAReply = "reply_not_a_reply";
    internal const string NotApplicable = "reply_not_applicable";

    public async Task<Result<ApplyReplyResponse, ResponderFailure>> ApplyAsync(
        User user, MailAccountConnection connection, ApplyReplyRequest request, CancellationToken cancellationToken)
    {
        var ics = await parts.LoadAsync(user, connection, request.Folder, request.Uid, request.Part, cancellationToken);
        if (ics.IsFailure) return Result.Failure<ApplyReplyResponse, ResponderFailure>(ics.Error);
        var reading = InvitationParser.Read(ics.Value);
        if (reading.Invitation is not { Method: InvitationMethod.Reply } parsed)
            return Result.Failure<ApplyReplyResponse, ResponderFailure>(new ResponderFailure(400, NotAReply));

        var context = await reader.ResolveReplyAsync(user, parsed, cancellationToken);
        if (context.Reply is not { Status: ReplyStatus.Applicable } reply || context.Stored is null)
            return Result.Failure<ApplyReplyResponse, ResponderFailure>(new ResponderFailure(400, NotApplicable));

        var rewritten = PartStatRewriter.Rewrite(context.Stored.IcsRaw, reply.Email, reply.PartStat);
        if (rewritten is null) return Result.Failure<ApplyReplyResponse, ResponderFailure>(new ResponderFailure(400, NotApplicable));
        var outcome = await writer.PutAsync(user.WebmailUid, context.Stored.CalendarId, context.Stored.DavName, rewritten,
            cancellationToken, cause: RevisionCause.Webmail);
        var applied = outcome.Status is DavWriteStatus.Created or DavWriteStatus.Replaced;
        var error = applied ? null : InvitationResponder.CodeOf(outcome.Status);
        if (!applied) logger.LogWarning("Reply not applied on {DavName}: {Status}", context.Stored.DavName, outcome.Status);

        var after = await reader.ResolveReplyAsync(user, parsed, cancellationToken);
        return Result.Success<ApplyReplyResponse, ResponderFailure>(
            new ApplyReplyResponse(InvitationReader.Block(parsed, after, request.Part), applied, error));
    }
}
```

`InvitationResponder.Map(DavWriteOutcome)` détient déjà le passage statut → code (`calendar_busy`,
`calendar_conflict`, `calendar_refused`) : l'exposer en `internal static string CodeOf(DavWriteStatus)`
et faire `Map` s'en servir. Contrôleur :

```csharp
    [HttpPost("ApplyReply")]
    public async Task<ActionResult<ApplyReplyResponse>> ApplyReply(ApplyReplyRequest request, CancellationToken cancellationToken)
    {
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;
        var result = await applier.ApplyAsync(AuthenticatedUser, connection, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value)
            : StatusCode(result.Error.Status, ResultEnveloppe.CreateErrorEnveloppe(result.Error.Message));
    }
```

(constructeur `+ IInvitationReplyApplier applier`, avec les mêmes attributs `ProducesResponseType`
et commentaires XML que `Respond` : `200`, `400`, `401`, `404`, `502`). Test contrôleur : le
succès rend le corps, un échec rend son statut et son code, comme les tests de `Respond`.
Enregistrements : `InvitationPartLoader` (scoped, concret), `IInvitationReplyApplier`.

- [ ] **Step 8 : `cd src && dotnet test` vert ; commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
feat(agenda): 5e2, un REPLY est lu par le bloc invitation et appliqué par ApplyReply

Statut applicable/périmé/inconnu/une seule date ; PARTSTAT réécrit sous le nom stocké, idempotent, sans mail.
EOF
```

---

### Task 7 : le côté organisateur à l'écran — champ Invités, pastilles d'état, langue des envois

**Files:**
- Modify: `modules/calendar/calendarTypes.ts`, `eventForm.ts`, `queries.ts`, `EventEditor.tsx`, `EventPreview.tsx`, `CalendarLayout.tsx`, `api.js`
- Create: `modules/calendar/attendeeStatus.ts`, `AttendeeStatusList.tsx`, `AttendeesField.tsx`, `probes/event-attendees.html`
- Modify: `styles/calendar.css`, `locales/{fr,en}/calendar.json`
- Test: `eventForm.test.ts`, `queries.test.tsx`, `attendeeStatus.test.ts`, `AttendeesField.test.tsx`, `EventEditor.test.tsx`, `EventPreview.test.tsx`

**Interfaces:**
- Consumes : `RecipientsField` (`{ id, label, tokens: string[], onChange(tokens), contacts?: Contact[] }`,
  chemin `modules/mail/compose/RecipientsField.tsx`, `namesByAddressOf(contacts)` exporté du même
  fichier), `useContacts()` (`modules/contacts/queries.ts`), `myAnswerOf` (`myAnswer.ts`), `i18next`.
- Produces :
  ```ts
  export interface AttendeeWrite { email: string; name?: string }
  // EventWrite : attendees?: AttendeeWrite[]; language?: string
  // EventDetail : canInvite: boolean
  export interface SchedulingReport { owner?: string; sent: number }
  export interface CreatedId { id: string; scheduling?: SchedulingReport }
  export interface EventUpdated { scheduling: SchedulingReport }
  // EventFormState : attendees: AttendeeWrite[]; canInvite: boolean
  export function guestAnswerOf(partStat: string | undefined, t: TFunction<'calendar'>): string   // 'guestAnswer.accepted' | 'tentative' | 'declined' | 'pending'
  export function dotClassOf(partStat: string | undefined): 'is-accepted' | 'is-tentative' | 'is-declined' | 'is-pending'
  export default function AttendeeStatusList({ guests }: { guests: AttendeeProjection[] })
  export default function AttendeesField({ value, onChange }: { value: AttendeeWrite[]; onChange(next: AttendeeWrite[]): void })
  export function mailLanguage(): 'fr' | 'en'     // queries.ts, depuis i18next.language
  ```

- [ ] **Step 1 : types, formulaire, langue — tests (rouges)**

`eventForm.test.ts`, dans `describe('formOf')` et un `describe('writeOf')` :

```ts
  it('seeds the guests of an event the user organizes, and none of a received one', () => {
    const guests = [
      { email: 'alice@weesky.be', name: 'Alice', isOrganizer: true },
      { email: 'marc@example.org', name: 'Marc', isOrganizer: false, partStat: 'ACCEPTED' },
      { email: 'julie@example.net', isOrganizer: false, recurrenceId: '20261019T100000' },
    ]
    const own = formOf(detailOf({ attendees: guests, canInvite: true }), null, TZ)
    expect(own.attendees).toEqual([{ email: 'marc@example.org', name: 'Marc' }])
    expect(own.canInvite).toBe(true)

    const received = formOf(detailOf({ attendees: guests, canInvite: false }), null, TZ)
    expect(received.attendees).toEqual([])
    expect(received.canInvite).toBe(false)
  })

  it('writes the guests only when the user may invite', () => {
    const own = form({ attendees: [{ email: 'marc@example.org', name: 'Marc' }], canInvite: true })
    expect(writeOf(own).attendees).toEqual([{ email: 'marc@example.org', name: 'Marc' }])
    expect(writeOf(form({ attendees: [], canInvite: true })).attendees).toEqual([])
    expect(writeOf(form({ attendees: [{ email: 'x@y.z' }], canInvite: false })).attendees).toBeUndefined()
  })
```

(`form()` : l'aide du fichier qui bâtit un `EventFormState` complet ; `detailOf` doit poser
`canInvite: true` par défaut.) `movedBody` ne change pas : un test existant vérifie qu'il recopie
`detail.fields`, dont `attendees` est absent → le glisser d'un chip laisse les invités tels quels.

`queries.test.tsx` : `useCreateEvent` envoie `language` (`'fr'` quand `i18next.language` commence
par `fr`, `'en'` sinon) ; `useUpdateEvent` aussi ; `useDeleteEvent` passe `language` à
`api.deleteEvent`. Reprendre la forme des tests existants du fichier (mock d'`api`).

- [ ] **Step 2 : types, formulaire, langue**

`calendarTypes.ts` : les types ci-dessus ; `EventDetail.canInvite: boolean`. `eventForm.ts` :

```ts
// EventFormState
  attendees: AttendeeWrite[]
  /** False on an event somebody else organizes: the field gives way to the read-only list. */
  canInvite: boolean
// newEventForm : attendees: [], canInvite: true
// formOf :
    attendees: detail.canInvite
      ? detail.attendees.filter(a => !a.isOrganizer && !a.recurrenceId).map(a => ({ email: a.email, name: a.name }))
      : [],
    canInvite: detail.canInvite,
// writeOf, après visibility :
  if (form.canInvite) write.attendees = form.attendees
```

`queries.ts` :

```ts
import i18next from 'i18next'
/** The language the server writes the invitation mails in: the screen's, never a guess. */
export function mailLanguage(): 'fr' | 'en' {
  return i18next.language?.startsWith('fr') ? 'fr' : 'en'
}
export function useCreateEvent() {
  return useCalendarMutation((event: EventWrite) => api.createEvent({ ...event, language: mailLanguage() }) as Promise<CreatedId>)
}
export function useUpdateEvent() {
  return useCalendarMutation(
    ({ id, body }: { id: string; body: EventUpdateBody }) => api.updateEvent(id, { ...body, language: mailLanguage() }) as Promise<EventUpdated>)
}
export function useDeleteEvent() {
  return useCalendarMutation(
    ({ id, scope, instanceId }: { id: string; scope: EditScope; instanceId?: string }) =>
      api.deleteEvent(id, scope, instanceId, mailLanguage()))
}
```

`api.js` : `deleteEvent: (id, scope, instanceId, language)` ajoute `&language=` à la requête (lire
la forme actuelle de la ligne 270 et y ajouter le paramètre). `EventUpdateBody extends EventWrite`
porte donc déjà `attendees?` et `language?`.

`CalendarLayout.tsx`, après `mutateAsync` : lire `scheduling` sur le résultat et dire ce qui est
parti :

```ts
      const result = detail
        ? await updateEvent.mutateAsync({ id: detail.id, body: updateBodyOf(...) })
        : await createEvent.mutateAsync(writeOf(form))
      const sent = result?.scheduling?.sent ?? 0
      rememberCalendar(form.calendarId)
      addToast(sent > 0 ? t('editor.savedSent', { count: sent }) : t('editor.saved'), 'success')
```

(`updateEvent`/`createEvent` typés par leurs `Promise<EventUpdated>`/`Promise<CreatedId>` ;
`api.js` rend le JSON tel quel.)

- [ ] **Step 3 : `npm run typecheck` puis `npm test -- eventForm queries` verts.**

- [ ] **Step 4 : `attendeeStatus.ts`, `AttendeeStatusList`, `AttendeesField` — tests (rouges)**

`attendeeStatus.test.ts` :

```ts
const t = ((key: string) => key) as unknown as TFunction<'calendar'>
it('words a guest’s answer, pending by default', () => {
  expect(guestAnswerOf('ACCEPTED', t)).toBe('guestAnswer.accepted')
  expect(guestAnswerOf('tentative', t)).toBe('guestAnswer.tentative')
  expect(guestAnswerOf('DECLINED', t)).toBe('guestAnswer.declined')
  expect(guestAnswerOf('NEEDS-ACTION', t)).toBe('guestAnswer.pending')
  expect(guestAnswerOf(undefined, t)).toBe('guestAnswer.pending')
  expect(dotClassOf('DELEGATED')).toBe('is-pending')
  expect(dotClassOf('accepted')).toBe('is-accepted')
})
```

`AttendeesField.test.tsx` (mock `../contacts/queries` → `useContacts: () => ({ data: contacts })`) :

```ts
it('offers a contact, keeps its name on the guest, and removes a chip', async () => {
  const onChange = vi.fn()
  render(<AttendeesField value={[{ email: 'marc@example.org', name: 'Marc' }]} onChange={onChange} />)
  expect(screen.getByText('Marc')).toBeInTheDocument()        // le chip montre le nom du contact quand il l'a
  await userEvent.type(screen.getByLabelText('Attendees'), 'jul')
  await userEvent.click(await screen.findByText(/Julie/))
  expect(onChange).toHaveBeenLastCalledWith([{ email: 'marc@example.org', name: 'Marc' }, { email: 'julie@example.net', name: 'Julie Martin' }])
  await userEvent.click(screen.getAllByRole('button', { name: /remove/i })[0])
  expect(onChange).toHaveBeenLastCalledWith([])
})
```

(Le libellé exact du bouton de retrait et la façon dont `RecipientsField` affiche un contact —
nom ou adresse — se lisent dans `RecipientsField.tsx` et son test ; ajuster les sélecteurs à ce que
le composant rend réellement, pas l'inverse.)

`EventEditor.test.tsx` :

```ts
  it('shows the guests field on a new event and on one the user organizes', () => {
    draw()
    expect(screen.getByLabelText('Attendees')).toBeInTheDocument()
    draw({ detail: detailOf({ canInvite: true, attendees: [{ email: 'marc@example.org', name: 'Marc', isOrganizer: false, partStat: 'ACCEPTED' }] }) })
    expect(screen.getAllByLabelText('Attendees').length).toBeGreaterThan(0)
    expect(screen.getByText(/Marc/)).toBeInTheDocument()
    expect(screen.getByText('accepted')).toBeInTheDocument()          // la liste d'état sous le champ
  })

  it('shows names only, no field, on a received event', () => {
    draw({ detail: detailOf({ canInvite: false, attendees: [{ email: 'lea@example.net', name: 'Léa', isOrganizer: true }, { email: 'me@weesky.be', isOrganizer: false, partStat: 'ACCEPTED' }], myPartStat: 'ACCEPTED' }) })
    expect(screen.queryByLabelText('Attendees')).toBeNull()
    expect(screen.getByText('Léa')).toBeInTheDocument()
    expect(screen.queryByText('Read only until invitations are supported')).toBeNull()
  })

  it('hands the guests to onSave', async () => {
    const { onSave } = draw({ initial: form({ attendees: [{ email: 'marc@example.org' }], canInvite: true }) })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(onSave.mock.calls[0][0].attendees).toEqual([{ email: 'marc@example.org' }])
  })
```

`EventPreview.test.tsx` :

```ts
  it('draws a dot per guest on an event the user organizes', async () => {
    api.getEvent.mockResolvedValue(detailOf({ id: 'e1', canInvite: true, attendees: [
      { email: 'alice@weesky.be', isOrganizer: true },
      { email: 'marc@example.org', name: 'Marc', isOrganizer: false, partStat: 'ACCEPTED' },
      { email: 'julie@example.net', isOrganizer: false, partStat: 'NEEDS-ACTION' }] }))
    const preview = draw(DENTIST)
    await screen.findByText('Marc')
    expect(preview.querySelectorAll('.attendee-dot.is-accepted')).toHaveLength(1)
    expect(preview.querySelectorAll('.attendee-dot.is-pending')).toHaveLength(1)
    expect(screen.queryByText(/Organised by/)).toBeNull()
  })

  it('draws names only on a received event', async () => {
    api.getEvent.mockResolvedValue(detailOf({ id: 'e1', canInvite: false, attendees: [
      { email: 'lea@example.net', name: 'Léa', isOrganizer: true }, { email: 'marc@example.org', name: 'Marc', isOrganizer: false, partStat: 'ACCEPTED' }] }))
    const preview = draw(DENTIST)
    await screen.findByText(/Organised by Léa/)
    expect(preview.querySelector('.attendee-dot')).toBeNull()
  })
```

- [ ] **Step 5 : les composants**

`attendeeStatus.ts` :

```ts
import type { TFunction } from 'i18next'

/** What a guest answered, in the organizer's calendar — where the answers actually arrive
    (spec 5e, décision 12). Anything but the three answers is "no answer yet". */
export function guestAnswerOf(partStat: string | undefined, t: TFunction<'calendar'>): string {
  switch (partStat?.toUpperCase()) {
    case 'ACCEPTED': return t('guestAnswer.accepted', { ns: 'calendar' })
    case 'TENTATIVE': return t('guestAnswer.tentative', { ns: 'calendar' })
    case 'DECLINED': return t('guestAnswer.declined', { ns: 'calendar' })
    default: return t('guestAnswer.pending', { ns: 'calendar' })
  }
}

export function dotClassOf(partStat: string | undefined): 'is-accepted' | 'is-tentative' | 'is-declined' | 'is-pending' {
  switch (partStat?.toUpperCase()) {
    case 'ACCEPTED': return 'is-accepted'
    case 'TENTATIVE': return 'is-tentative'
    case 'DECLINED': return 'is-declined'
    default: return 'is-pending'
  }
}
```

`AttendeeStatusList.tsx` :

```tsx
import { useTranslation } from 'react-i18next'
import type { AttendeeProjection } from './calendarTypes'
import { dotClassOf, guestAnswerOf } from './attendeeStatus'

/** The guests and what each answered: one dot, one name, one word. Drawn only where the answers
    are true — the user's own events (décision 12). */
export default function AttendeeStatusList({ guests }: { guests: AttendeeProjection[] }) {
  const { t } = useTranslation('calendar')
  return (
    <ul className="attendee-status">
      {guests.map(guest => (
        <li key={guest.email}>
          <span className={`attendee-dot ${dotClassOf(guest.partStat)}`} aria-hidden="true" />
          <span className="attendee-name">{guest.name || guest.email}</span>
          <span className="attendee-answer">{guestAnswerOf(guest.partStat, t)}</span>
        </li>
      ))}
    </ul>
  )
}
```

`AttendeesField.tsx` :

```tsx
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useContacts } from '../contacts/queries'
import RecipientsField, { namesByAddressOf } from '../mail/compose/RecipientsField'
import type { AttendeeWrite } from './calendarTypes'

interface Props { value: AttendeeWrite[]; onChange(next: AttendeeWrite[]): void }

/** The composer's recipients field, on the guest list: chips, the book's suggestions, and the
    name the book knows carried along as the CN the server will write (décision 8). */
export default function AttendeesField({ value, onChange }: Props) {
  const { t } = useTranslation('calendar')
  const { data: contacts } = useContacts()
  const names = useMemo(() => namesByAddressOf(contacts ?? []), [contacts])
  const change = (tokens: string[]) =>
    onChange(tokens.map(email => ({ email, name: value.find(a => a.email === email)?.name ?? names.get(email) })))
  return (
    <RecipientsField id="event-attendees" label={t('editor.attendees')} tokens={value.map(a => a.email)}
      onChange={change} contacts={contacts ?? []} />
  )
}
```

Si `namesByAddressOf` indexe par adresse canonique (minuscules), passer `email.toLowerCase()`
à `names.get` ; `RecipientsField` canonise déjà ses tokens (`lib/canonicalAddress`) — vérifier et
ne pas re-canoniser.

`EventEditor.tsx` : sous le champ Lieu, avant Disponibilité :

```tsx
        {form.canInvite && (
          <div className="field-h event-attendees">
            <AttendeesField value={form.attendees} onChange={attendees => set({ attendees })} />
          </div>
        )}
        {form.canInvite && detail && guests.length > 0 && (
          <div className="field-h">
            <span className="field-h-label" />
            <AttendeeStatusList guests={guests} />
          </div>
        )}
```

avec `const guests = (detail?.attendees ?? []).filter(a => !a.isOrganizer && !a.recurrenceId)`.
Le bloc en lecture seule sous « Plus d'options » ne s'affiche plus que si `!form.canInvite`
(les noms, l'organisateur marqué, `editor-my-answer`), et la ligne `editor.attendeesReadOnly`
disparaît avec sa clé. Le `more` s'ouvre d'office quand `!form.canInvite && attendees.length > 0`.

`EventPreview.tsx` : la ligne des invités devient

```tsx
          {guests.length > 0 && (detail?.canInvite
            ? <AttendeeStatusList guests={guests} />
            : <p className="event-preview-row event-preview-attendees"><PeopleIcon size={14} />{guests.map(a => a.name || a.email).join(', ')}</p>)}
```

et la ligne « Organisé par » ne se dessine que si `!detail?.canInvite` (sur son propre événement,
l'organisateur est soi).

CSS (`calendar.css`) :

```css
/* The guest list with its answers — preview and editor. The dot is the answer, the word says it
   for a screen reader and a colour-blind eye alike. */
.attendee-status { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 4px; font-size: 13px; }
.attendee-status li { display: flex; align-items: center; gap: 8px; min-width: 0; }
.attendee-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.attendee-answer { color: var(--text-muted); flex: none; }
.attendee-dot { flex: none; width: 8px; height: 8px; border-radius: 50%; border: 1px solid var(--text-muted); background: transparent; }
.attendee-dot.is-accepted { background: var(--success); border-color: var(--success); }
.attendee-dot.is-tentative { background: var(--action-primary); border-color: var(--action-primary); }
.attendee-dot.is-declined { background: var(--danger); border-color: var(--danger); }
.event-preview .attendee-status { margin-left: 20px; }
/* The composer's field inside the editor's label/control grid: its own label and box become
   the row's two cells. */
.event-attendees > .recipients-field { display: contents; }
```

`RecipientsField` rend `<div class="recipients-field"><label for=…/><div class="recipients-box">…`
et ses styles vivent dans `mail.css`, chargé globalement par `main.tsx` : rien à dupliquer.

- [ ] **Step 6 : mesurer, pas raisonner** — `probes/event-attendees.html`, sur le modèle de
`probes/invitation-card.html` (liens vers `calendar.css` et `mail.css`, DOM de l'éditeur reproduit
verbatim : un `field-h` Lieu, puis le `field-h event-attendees` avec le DOM que `RecipientsField`
rend, avec deux chips). Mesures attendues, à 900 et à 400 px, par Edge headless
(`msedge.exe --headless=new --dump-dom` sur le serveur Vite, port 5178, comme en 5e1) :
- le bord gauche de `.recipients-box` égale celui de `#event-location` (écart 0) ;
- le libellé « Invités » est sur la même colonne que « Lieu » (même `left`) ;
- aucun débordement horizontal de la boîte de chips à 400 px.
Si `display: contents` ne place pas le libellé (un navigateur qui le retire de l'arbre d'accessibilité — Edge ne le fait plus depuis 2021, vérifier
que `getByLabelText` passe en jsdom), remplacer par un libellé propre dans `AttendeesField` et
`.event-attendees .recipients-field > label { display: none }`. Noter les chiffres mesurés dans
l'en-tête de la sonde.

- [ ] **Step 7 : locales**

`calendar.json` (en) : `"editor.savedSent": "Saved · invitations sent: {{count}}"`, retirer
`editor.attendeesReadOnly`, groupe `"guestAnswer": { "accepted": "accepted", "tentative": "answered maybe", "declined": "declined", "pending": "no answer yet" }`.
(fr) : `"savedSent": "Enregistré · invitations envoyées : {{count}}"` (insécable avant `:`),
`"guestAnswer": { "accepted": "a accepté", "tentative": "a répondu peut-être", "declined": "a refusé", "pending": "sans réponse" }`.

- [ ] **Step 8 : `npm run lint && npm run typecheck && npm test` verts ; commit**

```bash
git add src/frontend
git commit -F - <<'EOF'
feat(agenda): 5e2, le champ Invités dans l'éditeur et l'état de chaque invité

Puces avec les contacts, pastille par invité sur ses propres événements, langue des envois, compte d'invitations envoyées.
EOF
```

---

### Task 8 : l'encart `REPLY`, la documentation, la clôture

**Files:**
- Modify: `modules/mail/api/mailTypes.ts`, `api.js`, `modules/mail/queries.ts`, `modules/mail/reader/invitationText.ts`, `InvitationCard.tsx`, `styles/mail.css`, `locales/{fr,en}/mail.json`, `probes/invitation-card.html`
- Modify: `docs/superpowers/specs/2026-09-12-webmail-calendar-5e-invitations-design.md` (état), `docs/superpowers/specs/2026-09-04-webmail-calendar-5-overview-design.md` (tableau d'état : 5a à 5e livrées), `docs/architecture-calendar.md` (section « Invitations envoyées »)
- Create: `docs/superpowers/calendar-5e2-residuals.md` (rempli par la revue finale)
- Test: `invitationText.test.ts`, `InvitationCard.test.tsx`, `queries.test.tsx` (mail)

**Interfaces — Produces :**

```ts
export type InvitationMethod = 'Request' | 'Cancel' | 'Reply'
export type ReplyStatus = 'Applicable' | 'UnknownUid' | 'NotOwner' | 'UnknownAttendee' | 'OccurrenceOnly' | 'Stale'
export interface InvitationReply { email: string; name?: string; partStat: string; status: ReplyStatus; applied: boolean }
// MailInvitation : reply?: InvitationReply
export interface ApplyReplyArgs { folder: string; uid: number; part: string }
export interface ApplyReplyResponse { invitation: MailInvitation; applied: boolean; applyError?: string }
// api.js : applyInvitationReply: (body, options) => request('POST', '/api/Calendar/Invitations/ApplyReply', body, options)
export function useApplyInvitationReply(): UseMutationResult<ApplyReplyResponse, ApiError, ApplyReplyArgs>
// invitationText.ts : CardState gagne 'reply' ; cardStateOf rend 'reply' pour method === 'Reply' (après 'unreadable')
```

- [ ] **Step 1 : tests (rouges)**

`invitationText.test.ts` : `cardStateOf({ ...base, method: 'Reply', reply: {...} })` rend `'reply'`,
et `'unreadable'` garde la main. `queries.test.tsx` (mail) : `useApplyInvitationReply` appelle
`api.applyInvitationReply(args, { accountId })` et invalide `['calendar', 'primary']`, jamais le
message ni les dossiers.

`InvitationCard.test.tsx` (ajouter `applyInvitationReply: vi.fn()` aux mocks) :

```ts
const reply = (status: ReplyStatus, applied = false, partStat = 'ACCEPTED'): MailInvitation => ({
  ...base, method: 'Reply', addressedTo: undefined, inCalendar: status === 'UnknownUid' ? 'Absent' : 'Current', calendarId: 'c1',
  reply: { email: 'marc@example.org', name: 'Marc', partStat, status, applied },
})

it('a reply applies itself once, then says what the guest answered', async () => {
  mocks.applyInvitationReply.mockResolvedValue({ invitation: reply('Applicable', true), applied: true })
  const { view } = renderCard(reply('Applicable'))

  expect(await screen.findByText('Marc accepted')).toBeInTheDocument()
  expect(mocks.applyInvitationReply).toHaveBeenCalledWith({ folder: 'INBOX', uid: 7, part: '2' }, expect.anything())
  view.rerender(<InvitationCard invitation={reply('Applicable')} folderPath="INBOX" uid={7} onTrashed={() => {}} />)
  await new Promise(r => setTimeout(r, 0))
  expect(mocks.applyInvitationReply).toHaveBeenCalledTimes(1)
  expect(screen.queryByRole('button')).toBeNull()
  expect(screen.getByLabelText('Reply')).toBeInTheDocument()     // le badge
})

it('an already applied reply calls nothing', async () => {
  renderCard(reply('Applicable', true, 'DECLINED'))
  expect(await screen.findByText('Marc declined')).toBeInTheDocument()
  expect(mocks.applyInvitationReply).not.toHaveBeenCalled()
})

it.each([
  ['Stale', 'Marc answered maybe · reply to an earlier version'],
  ['UnknownUid', 'This event no longer exists'],
  ['NotOwner', 'This event was not invited from the webmail'],
  ['UnknownAttendee', 'Marc is not on the guest list'],
  ['OccurrenceOnly', 'Reply for a single date of the series, not carried into the calendar'],
] as const)('%s is read only', async (status, sentence) => {
  renderCard(reply(status, false, 'TENTATIVE'))
  expect(await screen.findByText(sentence)).toBeInTheDocument()
  expect(mocks.applyInvitationReply).not.toHaveBeenCalled()
})

it('a reply the calendar refused offers to retry', async () => {
  mocks.applyInvitationReply
    .mockResolvedValueOnce({ invitation: reply('Applicable'), applied: false, applyError: 'calendar_busy' })
    .mockResolvedValueOnce({ invitation: reply('Applicable', true), applied: true })
  renderCard(reply('Applicable'))

  expect(await screen.findByText('Reply not recorded in the calendar.')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
  expect(await screen.findByText('Marc accepted')).toBeInTheDocument()
  expect(mocks.applyInvitationReply).toHaveBeenCalledTimes(2)
})
```

- [ ] **Step 2 : le code**

`queries.ts` (mail) :

```ts
/** Décision 12: the guest's answer into the organizer's calendar, asked for once by the card
    when the block says it applies. The calendar is what changed; the message is not refetched —
    the card already holds the block the answer hands back. */
export function useApplyInvitationReply() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  return useMutation<ApplyReplyResponse, ApiError, ApplyReplyArgs>({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: args => api.applyInvitationReply(args, { accountId }) as Promise<ApplyReplyResponse>,
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: calendarKeys.all(accountId) }) },
  })
}
```

`invitationText.ts` : `CardState` + `'reply'` ; dans `cardStateOf`, après `unreadable` :
`if (i.method === 'Reply') return 'reply'`. Une fonction `replySentenceOf(reply, t)` — un `switch`
sur `reply.status` avec des clés littérales (`reader.invitation.reply.stale`, `.unknownUid`,
`.notOwner`, `.unknownAttendee`, `.occurrenceOnly`) ; pour `Applicable` la phrase de la réponse
seule (`reader.invitation.reply.answer.ACCEPTED` / `TENTATIVE` / `DECLINED` avec `{{name}}`,
`Stale` la même suivie de ` · ` + `reply.stale`).

`InvitationCard.tsx` :
- `badgeKind` gagne `'reply'` → `t('reader.invitation.badgeReply')` ;
- `const apply = useApplyInvitationReply()` ; `const attempted = useRef(false)` ;

```tsx
  // Once per mounting, never on a refresh (décision 12): the block says it applies and has not
  // been applied; the answer redraws the card and nothing else asks again.
  useEffect(() => {
    const reply = invitation.reply
    if (state !== 'reply' || !reply || reply.status !== 'Applicable' || reply.applied || attempted.current) return
    attempted.current = true
    void applyReply()
  }, [])   // eslint-disable-line react-hooks/exhaustive-deps — the guard is the ref, by design

  async function applyReply() {
    setError(null)
    try {
      const result = await apply.mutateAsync({ folder: folderPath, uid, part: invitation.part })
      setInvitation(result.invitation)
      setApplyFailed(!result.applied)
    } catch (caught) {
      setApplyFailed(true)
      setError({ message: apiErrorMessage(caught, t('reader.invitation.failed')), gone: caught instanceof ApiError && caught.status === 404 })
    }
  }
```

- `case 'reply'` du `foot` : une ligne `invitation-card-actions is-answered` avec la coche quand
  `reply.applied`, la phrase de `replySentenceOf`, aucun bouton ; quand `applyFailed`, un
  `invitation-card-error` « Réponse non enregistrée dans l'agenda. » et un bouton `btn btn-ghost`
  « Réessayer » qui rappelle `applyReply`. Le `dl` des lignes Quand/Où/Organisateur reste ; la
  ligne Invités affiche le seul répondant.
- Le `aria-label` de la `section` est le badge (`Reply`), ce que `getByLabelText('Reply')` lit.

`mail.css` : rien de neuf si la phrase tient dans `.invitation-card-answer` ; sinon
`.invitation-card-reply { color: var(--text-muted) }` pour les phrases en lecture seule. Ajouter
les états `reply` à `probes/invitation-card.html` (deux : appliqué, en lecture seule) et remesurer
comme en 5e1 (escape 0, pas de retour à la ligne parasite à 900).

`mail.json` (en) sous `reader.invitation` :

```json
"badgeReply": "Reply",
"reply": {
  "answer": { "ACCEPTED": "{{name}} accepted", "TENTATIVE": "{{name}} answered maybe", "DECLINED": "{{name}} declined" },
  "stale": "reply to an earlier version",
  "unknownUid": "This event no longer exists",
  "notOwner": "This event was not invited from the webmail",
  "unknownAttendee": "{{name}} is not on the guest list",
  "occurrenceOnly": "Reply for a single date of the series, not carried into the calendar",
  "notApplied": "Reply not recorded in the calendar.",
  "retry": "Retry"
}
```

(fr) : « Réponse », « {{name}} a accepté / a répondu peut-être / a refusé », « réponse à une
version précédente », « Ce rendez-vous n'existe plus », « Ce rendez-vous n'a pas été invité depuis
le webmail », « {{name}} n'est pas dans la liste des invités », « Réponse pour une seule date de
la série, non reportée dans l'agenda », « Réponse non enregistrée dans l'agenda. », « Réessayer ».
Le `{{name}}` est le nom, sinon l'adresse.

- [ ] **Step 3 : `npm run lint && npm run typecheck && npm test` verts ; commit**

```bash
git add src/frontend
git commit -F - <<'EOF'
feat(webmail): 5e2, l'encart lit une réponse d'invité et la reporte dans l'agenda

Une seule application au montage, phrases pour périmée, inconnue, une seule date ; Réessayer sur refus.
EOF
```

- [ ] **Step 4 : la documentation**

- Spec 5e, tableau « Où en est le projet » : `5e1 livrée ; 5e2 livrée` ; décision 9 : une ligne
  renvoyant aux écarts 1 à 3 de ce plan (le store rapporte les changements ; la seconde écriture
  vaut pour les deux portes) ; décision 8 : l'écart 4 (identité par défaut, pas de table).
- Cadrage 5 (`2026-09-04-…-overview-design.md`), tableau des tranches : 5a, 5b, 5c, 5d, 5e →
  « livrée » avec le lien de chaque spec.
- `docs/architecture-calendar.md` : section « Invitations envoyées » — les deux portes, le
  crochet, les deux colonnes, la file, les codes d'erreur ; retirer la phrase « no participant
  is composed by this screen ».
- `docs/superpowers/calendar-5e2-residuals.md` : créé par la revue finale, même forme que celui
  de 5e1.

```bash
git add docs
git commit -F - <<'EOF'
docs(agenda): 5e2 livrée, le cadrage et l'architecture à jour
EOF
```

- [ ] **Step 5 : la clôture, en session avec l'utilisateur** (hors sous-agents)

1. Rejouer le harnais `caldavtester` sur dev, comme 5d et 5e1 (la procédure est dans la spec 5d,
   § Tests) : aucune régression sur `PUT`/`DELETE`, le crochet ne change ni les codes ni les
   ETags d'un fichier sans invités.
2. Configurer le compte de service sur dev (`Scheduling:Smtp:*`, mot de passe **changé** depuis la
   conversation de conception, décision 10).
3. Recette : créer un événement avec un invité Gmail depuis le webmail → l'invitation arrive chez
   Gmail avec ses boutons ; répondre depuis Gmail → le mail `REPLY` ouvert dans le webmail
   montre « Marc a accepté » et la pastille passe au vert dans l'aperçu ; déplacer l'événement
   depuis DAVx⁵ ou Thunderbird → la mise à jour arrive chez Gmail, `DKIM: PASS`, `From` =
   l'utilisateur, aucun en-tête visible du compte de service, `SEQUENCE` avancée ; supprimer
   depuis l'iPhone → l'annulation arrive.
4. `superpowers:finishing-a-development-branch`.

---

## Auto-revue du plan

**Couverture de la spec (décisions 8 à 13).**

| Décision | Tâche(s) |
|---|---|
| 8 — champ Invités, `ORGANIZER` = identité par défaut, `CN`, `PARTSTAT` conservé, réserve du domaine | 2 (modèle, composeur, `OrganizerIdentity`, `not_organizer`), 7 (le champ, `canInvite`) |
| 9 — empreinte tous composants, `scheduling_owner/hash`, tableau des envois, deux portes, `Replaced`, `SEQUENCE` avancée par seconde écriture, les autres écritures | 1 (empreinte, décideur, `SequenceRewriter`), 3 (`EventWriteResult`, `Replaced`), 5 (le crochet, les deux branchements) ; import et vidage d'agenda n'appellent rien (contrainte globale) |
| 10 — session de l'utilisateur vs compte de service, file asynchrone, journal, `From` de l'utilisateur | 4 (`ServiceMailQueue`, options), 5 (`DeliverAsync`, session paresseuse) |
| 11 — sujets, corps, double forme, `CANCEL` réduit et sa `SEQUENCE` | 4 (`InvitationText`, `InvitationMailer`), 5 (`cancelSequence`) |
| 12 — `REPLY` lu, `ApplyReply`, `applicable`, cas non applicables, idempotence, `Réessayer`, pastilles | 6 (backend), 8 (encart), 7 (pastilles) |
| 13 — rien de nouveau annoncé aux clients | aucune tâche ne touche `DavPrincipalController` ni l'en-tête `DAV:` ; `caldavtester` rejoué en clôture |
| Surface HTTP — `attendees` sur `POST`/`PUT`, `scheduling` en réponse, `ApplyReply` | 2, 5, 6 |
| Schéma — deux colonnes, DDL documenté | 1 |
| Tests 5e2 serveur et frontend (§ Tests de la spec) | chaque ligne a son test nommé dans les tâches 1 à 8 ; la recette réelle en clôture |

**Placeholders.** Aucun « TBD » ; les points où le sous-agent doit lire un fichier voisin avant de
choisir un nom sont dits comme tels (aides de `CalDavPutTests`, forme de `MailAttendeeContent`,
espace de noms des tests d'invitations).

**Cohérence des types.** `EventChange(Guid? EventId, Guid CalendarId, string DavName, ReplacedVersion? Before, string? After)`
est employé tel quel en 3, 5 et dans les tests ; `SchedulingReport(string? Owner, int Sent)` en 5, 7 ;
`WriteOrigin` naît `internal` en 1 et devient `public` dans `Models/Calendar` en 5 (un déplacement,
pas un renommage) ; `InvitationContext` gagne un dernier positionnel `Reply` en 6 avec `null` par
défaut, ce qui laisse `Block` et le répondeur de 5e1 intacts ; `IInvitationScheduler.AfterWriteAsync`
a la même signature en 5 (interface), dans les tests du store, du contrôleur d'événements et du
`RecordingScheduler` CalDAV.

**Ordre et dépendances.** 1 → 2 → 3 → 4 → 5 (le crochet consomme tout ce qui précède) → 6 (indépendante
de 4 et 5, mais lit `SchedulingOwner` de 2) → 7 → 8. Les tâches 4 et 6 pourraient s'exécuter en
parallèle ; le plan les garde en séquence, une seule vague d'implémentation à la fois.

