# Agenda 5c — le serveur CalDAV : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ouvrir `/dav/calendars/` aux clients CalDAV (DAVx⁵, Thunderbird, iOS) sur un socle WebDAV
partagé avec le CardDAV de 4c, sans changer une seule réponse du carnet.

**Architecture:** Le code WebDAV générique de `CardDavController` et de `Services/CardDav` est
extrait vers `Services/Dav` et un `DavControllerBase` ; trois contrôleurs minces
(`DavPrincipalController`, `CardDavController`, `CalDavController`) s'y branchent. Côté agenda, un
lecteur (`DavCalendarReader`) et un écrivain (`DavCalendarWriter`) jumeaux de ceux des contacts
s'appuient sur `CalendarEventStore.ApplyIcsAsync`, `ICalendarSyncStore` et `OccurrenceExpander`
de 5a. Le `PUT` stocke le fichier verbatim ; le `time-range` et l'`expand` passent par le moteur
d'expansion existant.

**Tech Stack:** ASP.NET Core (.NET 10), EF Core (MariaDB, InMemory en test), Ical.Net 5.2.3,
NodaTime TZDB, xUnit + Moq, `DavTestServer` (hôte routé) ; React + TypeScript, Vitest,
react-i18next.

**Spec:** `docs/superpowers/specs/2026-09-06-webmail-calendar-5c-caldav-design.md` (la référence
pour chaque décision citée « § N ») ; le cadrage
`docs/superpowers/specs/2026-09-04-webmail-calendar-5-overview-design.md` (décisions 2, 6, 8,
§ Paramètres) ; la spec 4c `2026-08-23-webmail-contacts-4c-carddav-design.md` pour ce que 5c
hérite (décisions 5, 7, 8, 10, 11, 13, 15, 16).

## Global Constraints

- **La suite CardDAV ne change pas de sens.** Après chaque tâche, `dotnet test` complet est vert ;
  les seules modifications tolérées dans `CardDav*Tests`, `DavContact*Tests`,
  `Services/CardDav/*Tests` sont des renommages (espace de noms, nom de classe, valeur
  d'énumération). Une valeur attendue qui change est une régression à corriger dans le produit.
- **Verbatim** : un `PUT` DAV stocke exactement les octets reçus (après le décodage UTF-8 de
  `DavBody`), sans `VTIMEZONE` ajouté, sans `DTSTAMP`, sans UID inséré. L'ETag est `"ics_hash"`.
- **Le rang d'abord** : toute écriture prend `ICalendarSyncStore.NextSequenceAsync(calendarId)` en
  première instruction de sa transaction, avant de toucher une ligne.
- **Constantes, jamais de littéraux** : `IcsGuards.MaxIcsBytes`, `IcsGuards.MaxInstancesPerYear`,
  `CalendarStore.MaxPerCalendar` (5000), `CalendarStore.MaxPerUser` (20), `MultigetReport.MaxHrefs`
  (5000), `OccurrenceExpander.MaxYears` (5), `DavPaths.MaxPathLength` (4864).
- **Un segment est décodé une fois** : `DavPaths.Parse` reçoit le chemin brut ; les valeurs de
  route ASP.NET Core (déjà décodées) ne repassent jamais par lui.
- **Aucun `500` sur une entrée client** : chaque refus est nommé (tableaux des § 8, 10, 11) ;
  `CalDavNoFiveHundredTests` en est le filet.
- **Hôtes** : dev Windows, CI Linux ; aucune assertion sur une fin de ligne, un ordre de
  dictionnaire ou une date locale.
- **Commits** : message concis, deux lignes max, jamais commençant ni finissant par `@` ; utiliser
  `git commit -F -` avec un heredoc ; ne jamais pousser. Révertir `ApiDocumentation.xml` si
  `dotnet test` l'a régénéré (`git checkout -- src/snoopy.microservice/ApiDocumentation.xml`).
- **Tests backend** : `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj`
  (jamais `--no-build`). **Frontend** : `npm test -- --run` dans `src/frontend`.
- **Pas de commentaire évident, 3 lignes max quand il faut ; pas de doublon ; l'UI en anglais.**

---

## Structure des fichiers

| Zone | Fichiers | Responsabilité |
|---|---|---|
| `Services/Dav/` | ce qui migre de `Services/CardDav` (§ 1 de la spec) + `IDavMemberSource.cs`, `DavTombstone.cs`, `DavPropertyTables.cs`, `DavPrincipalProperties.cs`, `DavProtocol.cs` | WebDAV sans protocole |
| `Controllers/Dav/DavControllerBase.cs` | la trame commune | cadre, `PROPFIND`, `PROPPATCH`, `REPORT`, `OPTIONS`, `405` |
| `Controllers/DavPrincipalController.cs` | `/`, `/dav/`, `/dav/principals/…` | les deux home-sets, `expand-property` |
| `Controllers/CardDavController.cs` | `/dav/addressbooks/…` | ce qui reste vCard |
| `Controllers/CalDavController.cs` | `/dav/calendars/…` | tout CalDAV |
| `Services/CalDav/` | `CalDavProperties`, `CalDavError`, `CalDavOutcomeTranslator`, `EventMemberSource`, `CalendarDataRequest`, `ExpandedCalendarData`, `CalendarQuerySpec`, `CalendarQueryFilter`, `CalendarQueryReport`, `FreeBusyReport`, `MkCalendarRequest`, `CalendarPropertyUpdate` | le protocole CalDAV |
| `Repositories/` | `IDavCalendarReader`, `DavCalendarReader`, `IDavCalendarWriter`, `DavCalendarWriter` | lecture et écriture par agenda |
| `Models/Calendar/` | `DavCalendar`, `DavEvent`, `EventColumnFilter` | formes DAV |
| `Models/Dav/` | `DavWriteStatus`, `DavWriteOutcome` (déplacés), `DavCalDavToggle` | |
| `Authentication/Dav/` | ex-`Authentication/CardDav`, deux drapeaux | |
| Tests | `Infrastructure/DavTestServer` (trois contrôleurs), `Controllers/CalDav*Tests`, `Services/CalDav/*Tests`, `Repositories/DavCalendar*Tests` | |
| Frontend | `SyncPage.tsx`, `api.js`, `locales/{en,fr}/settings.json` | second interrupteur |
| Docs | `webmail-carddav-tables.md`, `carddav-restore-prerequisite.md`, `calendar-5c-residuals.md`, `architecture-calendar.md`, `assets/calendar-sync-epoch-rotate.sql` | |

---

### Task 1 : le socle `Services/Dav` et les contrôleurs minces

**Files:**
- Move (commit 1, mécanique) : les 27 fichiers de la spec § 1 de `Services/CardDav/` vers
  `Services/Dav/` (espace de noms `weesky.Snoopy.Microservice.Services.Dav`) ; `Authentication/CardDav/*`
  vers `Authentication/Dav/*` avec `CardDavAuthenticationHandler` → `DavAuthenticationHandler`,
  `CardDavAuthenticationDefaults` → `DavAuthenticationDefaults`, `CardDavAuthenticationOptions` →
  `DavAuthenticationOptions` ; `Models/Contacts/DavWriteStatus.cs`, `DavWriteOutcome.cs` →
  `Models/Dav/` (espace `Models.Dav`) ; `Services/CardDav/DavProperties.cs` → `CardDavProperties.cs`
  (classe renommée) ; `DavOutcomeTranslator.cs` → `CardDavOutcomeTranslator.cs`. Tests : mêmes
  déplacements sous `snoopy.microservice.Tests/Services/Dav/`, `Authentication/` (renommages),
  `Infrastructure/TestCardDavAuthenticationHandler.cs` → `TestDavAuthenticationHandler.cs`.
- Create : `Services/Dav/IDavMemberSource.cs`, `Services/Dav/DavTombstone.cs`,
  `Services/CardDav/CardMemberSource.cs`, `Controllers/Dav/DavControllerBase.cs`,
  `Controllers/DavPrincipalController.cs`, `Services/Dav/DavPrincipalProperties.cs`,
  `Services/Dav/DavPropertyTables.cs`.
- Modify : `Controllers/CardDavController.cs`, `Services/Dav/MultigetReport.cs`,
  `Services/Dav/SyncCollectionReport.cs`, `Services/Dav/ExpandPropertyReport.cs`,
  `Services/CardDav/CardDavProperties.cs`, `Program.cs`/`SecurityConfiguration` (les constantes
  renommées), `snoopy.microservice.Tests/Infrastructure/DavTestServer.cs`.

**Interfaces:**
- Consumes : tout `Services/CardDav` tel quel.
- Produces :

```csharp
namespace weesky.Snoopy.Microservice.Services.Dav;

internal sealed record DavTombstone(string DavName, ulong Rank);

/// <summary>What multiget and sync-collection need of a collection, whichever protocol owns it.
/// One instance is bound to one collection (a user's book, a calendar) for one request.</summary>
internal interface IDavMemberSource<TMember>
{
    Task<IReadOnlyList<TMember>> FindManyAsync(IReadOnlyList<string> davNames, CancellationToken ct);
    IAsyncEnumerable<TMember> ChangedAsync(ulong after, ulong upTo, CancellationToken ct);
    Task<IReadOnlyList<DavTombstone>> TombstonesAsync(ulong after, ulong upTo, CancellationToken ct);
    /// <summary>The resource an href from a body designates, or null when it is not a member of
    /// THIS collection — a card href on a calendar, another user's, an invalid name.</summary>
    string? MemberNameOf(string href);
    string HrefOf(string davName);
    string DavNameOf(TMember member);
    ulong RankOf(TMember member);
    (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, TMember member, XDocument body);
}
```

`Resolve` reçoit le corps du rapport parce que c'est lui qui dit si `address-data` /
`calendar-data` est demandée et sous quelle forme ; `CardMemberSource.Resolve` reprend
`AddressDataFilter.Asked(body)` + `CardDavProperties.Resolve`, exactement le code d'aujourd'hui.

```csharp
internal static class MultigetReport
{
    internal const int MaxHrefs = 5000;
    internal static Task<int> WriteAsync<T>(HttpResponse response, XDocument body, string requestHref,
        IDavMemberSource<T> source, CancellationToken ct);
}

internal static class SyncCollectionReport
{
    /// <param name="window">read by the caller in ITS snapshot, with ITS key (user or calendar)</param>
    internal static Task<SyncReportOutcome> WriteAsync<T>(HttpResponse response, XDocument body,
        string collectionHref, string? depthHeader, SyncWindow window, IDavMemberSource<T> source,
        CancellationToken ct);
    internal sealed record SyncWindow(SyncState State, SyncTokenRead Token, IReadOnlyList<DavTombstone> Tombstones);
    internal static SyncWindow ReadWindow(XElement root, SyncState state, IReadOnlyList<DavTombstone> tombstones);
}
```

La lecture transactionnelle de l'état (`ReadOrCreateStateAsync` + `TombstonesAsync` + commit) sort
du rapport vers l'appelant : `CardDavController` la garde mot pour mot dans une méthode privée
`ReadSyncWindowAsync` ; `CalDavController` (tâche 6) écrira la sienne sur `calendar_id`. Le
`throw new DavPreconditionException(ValidSyncToken)` sur `SyncTokenKind.Invalid` reste dans
`WriteAsync`.

```csharp
namespace weesky.Snoopy.Microservice.Controllers.Dav;

[Route("dav")]
[Authorize(Policy = DavAuthenticationDefaults.PolicyName)]
[ApiExplorerSettings(IgnoreApi = true)]
[NoFormBinding]
public abstract class DavControllerBase(ILogger logger) : ApiBaseController
{
    protected const int MaxBodyBytes = 1024 * 1024;

    protected Task DispatchAsync(DavResourceKind kind, Guid? userId, string? collectionName,
        string? davName, string? rootHref, CancellationToken ct);           // PROPFIND / PROPPATCH / REPORT
    protected Task TracedAsync(Guid? userId, DavResourceKind kind, Func<Trace, Task> action);
    protected Task PropfindAsync(...);   // la trame de 4c ; les trois crochets ci-dessous
    protected Task ProppatchAsync(...);  // par défaut : tout à 403 (WriteRefusalAsync)
    protected Task ReportAsync(...);     // lecture du corps, trace.Report, puis ServeReportAsync

    // Les crochets que chaque protocole remplit
    protected abstract Task<DavResourceContext?> ContextOrNotFoundAsync(DavResourceKind kind,
        string? collectionName, string? davName, CancellationToken ct);
    protected abstract (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, DavResourceContext resource);
    protected abstract IAsyncEnumerable<(string Href, DavResourceContext Resource)> ChildrenAsync(
        DavResourceContext parent, DavPropertyRequest request, CancellationToken ct);
    protected abstract Task<bool> ServeReportAsync(DavReportKind report, XDocument body,
        DavResourceContext resource, string requestHref, Trace trace, CancellationToken ct); // false = 403 supported-report
    protected abstract bool NeedsSnapshot(DavResourceKind kind, DavDepthValue depth);
    protected abstract string HrefOf(DavResourceKind kind, Guid userId, string? collectionName, string? davName, string? rootHref);
    protected abstract string? CanonicalOf(DavResourceKind kind, Guid userId, string? collectionName);

    // Ce qui ne bouge pas de 4c, rendu protected : RefuseAsync, BodyRefused, ReadBodyAsync (ex-ReadCardBodyAsync),
    // RefusedByPreconditions, DemandsCreation, HeaderOrNull, DepthHeader, LogRequest, StatusWrittenAfter,
    // Capabilities, MethodNotAllowed, InOneSnapshotAsync, WriteResourceAsync, la classe Trace (protected sealed).
}
```

`DavResourceContext` gagne `string? CollectionName` et `IReadOnlyList<string> Addresses` (vide
par défaut) ; `DavCard? Card` reste (le contexte agenda ajoutera `DavCalendar? Calendar`,
`DavEvent? Event` en tâche 3 — déclarer les trois propriétés dès maintenant, à `null`).

`DavPrincipalController` sert `ServiceRoot` (`""` et `"/"`), `PrincipalCollection`, `Principal`
avec `DavPrincipalProperties` (la table du principal et des deux collections intermédiaires,
extraite de `CardDavProperties`) et `ExpandPropertyReport`. `CardDavController` garde
`addressbooks`, `addressbooks/{userId}`, la collection et la carte. `DavPropertyTables` porte
`Set`, `PropertySet`, `Href`, `PrivilegeSet`, `ReportSet`, `HttpDate`, `EntityTag`,
`CurrentUserPrincipal`, `PrincipalUrl`, `IntermediateCollection`, `AllPropExclusions`, `Resolve`
(générique sur une table).

- [ ] **Step 1 : le déplacement mécanique.** `git mv` des fichiers, `sed` des espaces de noms et
      des trois classes d'authentification, `using` mis à jour partout (produit et tests).
      `DavAuthenticationDefaults.AuthenticationScheme` et `PolicyName` gardent leurs **valeurs**
      de chaîne (`"CardDav"`) : la configuration et les journaux existants ne bougent pas.
- [ ] **Step 2 : `dotnet test` complet vert.** Aucun autre changement dans ce commit.
- [ ] **Step 3 : commit** — `refactor(dav): deplace le socle WebDAV sous Services/Dav`.
- [ ] **Step 4 : tests du socle.** Écrire `Services/Dav/MultigetReportTests.cs` et
      `SyncCollectionReportTests.cs` sur une `FakeMemberSource<string>` (membres en mémoire) :
      multiget rend `404` pour un `href` que `MemberNameOf` refuse, `507` au-delà de `MaxHrefs`,
      les propriétés résolues par la source ; sync-collection fusionne membres et tombes par rang,
      tronque sur une frontière de rang, frappe le jeton du compteur. Ces tests rougissent (les
      signatures n'existent pas).
- [ ] **Step 5 : `IDavMemberSource`, `DavTombstone`, rapports génériques, `CardMemberSource`.**
      `CardMemberSource(IDavContactReader contacts, Guid userId, string principalAddress)` :
      `MemberNameOf` = l'ancien `IsOurs` (`DavPaths.Parse` + genre `Card` + même utilisateur +
      `DavName.IsValid`). `ContactTombstone` → `DavTombstone` par une projection dans la source.
- [ ] **Step 6 : `DavControllerBase`, `DavPrincipalController`, `DavPropertyTables`,
      `DavPrincipalProperties` ; `CardDavController` réécrit dessus.** Extraire, ne pas réécrire :
      chaque méthode déplacée garde son corps et son commentaire. Les routes du principal
      (`""`, `"/"`, `principals`, `principals/{userId:guid}`, leurs `OPTIONS` et `405`) quittent
      `CardDavController`. Le `NestedContext` d'`expand-property` vit dans
      `DavPrincipalController` et résout les genres `Principal`, `AddressBookHome`, `CalendarHome`
      (ce dernier ajouté en tâche 2 ; ici, seulement ce qui existe).
- [ ] **Step 7 : `DavTestServer`** enregistre `DavPrincipalController`, `CardDavController`,
      `WellKnownController` (`SelectedControllerFeatureProvider`). Aucun test CardDAV ne change
      hormis les renommages.
- [ ] **Step 8 : `dotnet test` complet vert.** Vérifier par `git diff --stat` sur
      `snoopy.microservice.Tests/Controllers/CardDav*` que seules des lignes de `using`/noms ont
      bougé.
- [ ] **Step 9 : commit** — `refactor(dav): un DavControllerBase et deux rapports generiques`.

---

### Task 2 : chemins, identité à deux drapeaux, `caldav_enabled`, l'interrupteur

**Files:**
- Modify : `Services/Dav/DavPaths.cs`, `DavResourceKind.cs`, `DavResource.cs`, `DavHeaders.cs`,
  `DavXml.cs`, `Authentication/Dav/IDavAuthenticationCache.cs` (`DavIdentity`),
  `DavAuthenticationHandler.cs`, `Authentication/WebmailClaimTypes.cs`,
  `Controllers/Dav/DavControllerBase.cs` (`Enabled(DavProtocol)`), `CardDavController.cs`,
  `DavPrincipalController.cs`, `Controllers/WellKnownController.cs`,
  `Data/Preferences/DavCredential.cs`, `Repositories/IDavCredentialStore.cs`,
  `DavCredentialStore.cs`, `Controllers/DavCredentialsController.cs`, `Models/DavCredentialsView.cs`,
  `docs/superpowers/webmail-carddav-tables.md`, `src/frontend/src/api.js`,
  `src/frontend/src/modules/settings/sync/SyncPage.tsx`, `SyncPage.test.tsx`,
  `src/frontend/src/locales/{en,fr}/settings.json`, tests
  (`DavPathsTests`, `DavAuthenticationHandlerTests`, `DavCredentialStoreTests`,
  `DavCredentialsControllerTests`, `WellKnownControllerTests`, `DavTestUser`,
  `TestDavAuthenticationHandler`).
- Create : `Services/Dav/DavProtocol.cs`, `Models/Dav/DavCalDavToggle.cs`.

**Interfaces:**

```csharp
internal enum DavResourceKind
{
    ServiceRoot, PrincipalCollection, Principal,
    AddressBookCollection, AddressBookHome, AddressBook, Card,
    CalendarCollection, CalendarHome, Calendar, Event
}

/// <summary>CollectionName: the calendar's URL segment, decoded once, unjudged; null off the calendar tree.</summary>
internal sealed record DavResource(DavResourceKind Kind, Guid UserId, string? CollectionName, string? DavName);

internal static class DavPaths
{
    internal const int MaxPathLength = 4864;
    internal const string CalendarCollection = Root + "/calendars/";
    internal static string CalendarHome(Guid userId) => $"{Root}/calendars/{userId}/";
    internal static string Calendar(Guid userId, string name) => $"{CalendarHome(userId)}{Uri.EscapeDataString(name)}/";
    internal static string Event(Guid userId, string calendarName, string davName) =>
        $"{Calendar(userId, calendarName)}{Uri.EscapeDataString(davName)}";
    // Parse: les cinq motifs de 4c inchangés (avec les genres renommés) plus
    //   [_, _, "calendars", ""]                       => CalendarCollection
    //   [_, _, "calendars", user, ""]                 => CalendarHome
    //   [_, _, "calendars", user, name, ""]           => Calendar   (name = Uri.UnescapeDataString(name))
    //   [_, _, "calendars", user, name, member]       => Event      (les deux décodés une fois)
    // Un segment vide au milieu ("calendars/{u}//x") ne correspond à rien : null.
}

public enum DavProtocol { CardDav, CalDav }

public readonly record struct DavIdentity(Guid UserId, bool CardDavEnabled, bool CalDavEnabled);

public static class WebmailClaimTypes
{
    public const string Uid = "webmail_uid";
    public const string Stamp = "webmail_stamp";
    /// <summary>"1"/"0". Absent on a JWT principal: the webmail session is never a synchronising device.</summary>
    public const string CardDav = "webmail_carddav";
    public const string CalDav = "webmail_caldav";
}

public readonly record struct DavCredentialState(bool Configured, bool CardDavEnabled, bool CalDavEnabled, DateTime? LastUsedAt);
public readonly record struct DavCredentialRecord(bool CardDavEnabled, bool CalDavEnabled, string SecretHash, byte[] Salt);

public interface IDavCredentialStore
{
    Task<string?> EnableAsync(Guid userId, DavProtocol protocol, CancellationToken ct);
    Task DisableAsync(Guid userId, DavProtocol protocol, CancellationToken ct);
    // le reste inchangé
}

public sealed record DavCalDavToggle { public required bool Enabled { get; init; } public string? TimeZone { get; init; } }

public sealed record DavCredentialsView(string ServerUrl, string Username, bool Configured,
    bool CardDavEnabled, bool CalDavEnabled, DateTime? LastUsedAt, string? Password);
```

`DavControllerBase` :

```csharp
/// <summary>The switch of the protocol this controller serves. A principal without the claim
/// (JWT) is allowed: the switch guards devices, not the webmail's own session.</summary>
protected bool Enabled(DavProtocol protocol) =>
    User.FindFirst(protocol is DavProtocol.CardDav ? WebmailClaimTypes.CardDav : WebmailClaimTypes.CalDav)?.Value != "0";
```

`TracedAsync` reçoit le protocole de la ressource et répond `403` (sans corps, `LogRequest`)
**avant** la vérification du `{userId}` quand `Enabled` est faux — sauf sur `OPTIONS`, anonyme.
`DavPrincipalController` passe par un `Enabled(CardDav) || Enabled(CalDav)`.

Handler : `if (!identity.CardDavEnabled && !identity.CalDavEnabled) return Refuse(Outcome.Forbidden);`
puis les deux claims. Le cache stocke la nouvelle `DavIdentity` (aucune autre modification).

`DavTestUser(string Email, Guid Uid, bool CardDav = true, bool CalDav = true)` ; le handler de
test écrit les deux claims.

`DavHeaders.ComplianceClasses = "1, 3, addressbook, calendar-access, extended-mkcol"` ;
`CalendarCollectionAllow = "OPTIONS, PROPFIND, PROPPATCH, REPORT"`,
`CalendarHomeAllow = "OPTIONS, PROPFIND, PROPPATCH, REPORT, MKCALENDAR, MKCOL"`,
`CalendarAllow = CollectionAllow`, `EventAllow = CardAllow`,
`CalendarContentType = "text/calendar; charset=utf-8; component=VEVENT"`.
`DavXml.CalDav = "urn:ietf:params:xml:ns:caldav"`, `DavXml.Apple = "http://apple.com/ns/ical/"`.

DDL (dans `webmail-carddav-tables.md`, nouvelle section « Tranche 5c ») :

```sql
ALTER TABLE `dav_credentials`
  ADD COLUMN `caldav_enabled` TINYINT(1) NOT NULL DEFAULT 0
    COMMENT 'Né à 0 : la ligne existe déjà pour des comptes qui n''ont rien demandé (5c)'
  AFTER `carddav_enabled`;
```

`DavCredentialsController.SetCalDav` :

```csharp
[HttpPut("CalDav")]
public async Task<ActionResult<DavCredentialsView>> SetCalDav(DavCalDavToggle toggle,
    ICalendarStore calendars, PreferencesDbContext preferences, CancellationToken ct)
```

À l'allumage : `toggle.TimeZone` obligatoire et `IcsTimeZones.IsKnownIana` (`400` sinon) ; dans
**une** transaction (`preferences.Database.CreateExecutionStrategy().ExecuteAsync` +
`BeginTransactionAsync`) : `store.EnableAsync(uid, DavProtocol.CalDav)` puis
`calendars.EnsureDefaultAsync(uid, toggle.TimeZone)` ; `throttle.ForgetIdentifier`,
`cache.Forget`, audit `Audit: caldav_sync user={UserId} enabled={Enabled} created={Created} outcome=success`.
À l'extinction : `DisableAsync`, cache oublié, audit. `EnableAsync` pose **explicitement**
`CardDavEnabled = protocol is CardDav`, `CalDavEnabled = protocol is CalDav` sur une ligne neuve.

Frontend : `api.setDavCalDav: (enabled, timeZone) => request('PUT', '/api/DavCredentials/CalDav', { enabled, timeZone })` ;
dans `SyncPage`, second `ToggleRow` `id="sync-caldav"`, `label={t('sync.caldav')}`,
`hint={t('sync.caldavHint')}`, `checked={state.calDavEnabled}`,
`onChange={on => write(() => api.setDavCalDav(on, Intl.DateTimeFormat().resolvedOptions().timeZone), on)}`.
Catalogues : `en` — `"caldav": "Calendar (CalDAV)"`, `"caldavHint": "Sync your calendars with your phone or Thunderbird. Turning this off stops every device; your password is kept."` ;
`fr` — `"caldav": "Agenda (CalDAV)"`, `"caldavHint": "Synchronisez vos agendas avec votre téléphone ou Thunderbird. Désactiver arrête tous les appareils ; votre mot de passe est conservé."` (espace insécable devant `;`, comme la ligne CardDAV).

- [ ] **Step 1 : tests.** `DavPathsTests` : les quatre chemins agenda, `Calendar` avec un nom
      encodé (`Mes%20vacances` → `Mes vacances`), un `%2F` qui reste refusé par `DavName.IsValid`
      côté appelant (le parseur le rend décodé, tel quel), un segment vide au milieu → null, un
      chemin de 4865 caractères → null, `Event` avec le nom et le membre encodés, la
      correspondance aller-retour `Parse(Event(u, n, m))`. `DavAuthenticationHandlerTests` :
      `(false,false)` → 403 ; `(true,false)`, `(false,true)` → succès avec les deux claims.
      `CardDavSurfaceTests` : sous `DavTestUser(CardDav:false)`, `PROPFIND /dav/addressbooks/{u}/`
      → 403 sans corps, `PROPFIND /dav/principals/{u}/` → 207 tant que `CalDav:true` ; sous
      `(false,false)` le principal répond 403. `DavCredentialStoreTests` : `EnableAsync(CalDav)`
      sur une table vide crée `carddav_enabled = false, caldav_enabled = true` ; l'inverse ;
      `GetStateAsync` rend les deux. `DavCredentialsControllerTests` : `SetCalDav` avec fuseau
      crée `default` avec ce fuseau (via un `ICalendarStore` Moq vérifié), `400` sans fuseau ou
      avec `Mars/Olympus`, extinction sans fuseau OK, audit journalisé. `WellKnownControllerTests` :
      `PROPFIND /.well-known/caldav` → 301 vers `/dav/`, `Cache-Control: max-age=86400`.
      `SyncPage.test.tsx` : le second interrupteur rend `calDavEnabled`, un clic appelle
      `setDavCalDav(true, <fuseau>)`. Tests de parité `en`/`fr` et de typographie : verts par
      construction (ils existent). Tout rougit.
- [ ] **Step 2 : implémenter** dans l'ordre chemins → en-têtes → identité/claims → store/DDL →
      contrôleur → well-known → frontend.
- [ ] **Step 3 : `dotnet test` complet et `npm test -- --run` verts.**
- [ ] **Step 4 : commit** — `feat(dav): chemins d'agenda, identite a deux drapeaux, caldav_enabled`.

---

### Task 3 : `CalDavController` en lecture — `PROPFIND`, `GET`, découverte du principal

**Files:**
- Create : `Repositories/IDavCalendarReader.cs`, `Repositories/DavCalendarReader.cs`,
  `Models/Calendar/DavCalendar.cs`, `Models/Calendar/DavEvent.cs`,
  `Models/Calendar/EventColumnFilter.cs`, `Services/CalDav/CalDavProperties.cs`,
  `Controllers/CalDavController.cs`, tests `Repositories/DavCalendarReaderTests.cs`,
  `Services/CalDav/CalDavPropertiesTests.cs`, `Controllers/CalDavPropfindTests.cs`,
  `Controllers/CalDavGetTests.cs`, `Controllers/CalDavSurfaceTests.cs` (OPTIONS, 405, 308, 403 éteint).
- Modify : `Services/Dav/DavResourceContext.cs` (`DavCalendar? Calendar`, `DavEvent? Event`),
  `Services/Dav/DavPrincipalProperties.cs` (+ `calendar-home-set`, `calendar-user-address-set`),
  `Controllers/DavPrincipalController.cs` (adresses, `NestedContext` sur `CalendarHome`),
  `Infrastructure/DavTestServer.cs` (+ `CalDavController`, `ICalendarStore`, `ICalendarSyncStore`,
  `IDavCalendarReader`, `ISendingIdentityStore` Moq vide, `IAccountInfoProvider` stub).

**Interfaces:**

```csharp
public sealed record DavCalendar(Guid Id, Guid UserId, string DavName, string DisplayName,
    string Description, string Color, int Order, string TimeZone);

public sealed record DavEvent(Guid EventId, Guid CalendarId, string DavName, string Uid,
    string IcsRaw, string IcsHash, DateTime UpdatedAt, ulong SyncSequence);

public sealed record EventColumnFilter(string? Status, string? Transparency, string? Class)
{
    public static readonly EventColumnFilter None = new(null, null, null);
}

public interface IDavCalendarReader
{
    Task<IReadOnlyList<DavCalendar>> ListAsync(Guid userId, CancellationToken ct);            // ordre sort_order, puis dav_name
    Task<DavCalendar?> FindCalendarAsync(Guid userId, string calendarName, CancellationToken ct);
    IAsyncEnumerable<DavEvent> StreamAsync(Guid calendarId, ulong upTo, CancellationToken ct);
    Task<DavEvent?> FindAsync(Guid calendarId, string davName, CancellationToken ct);
    Task<IReadOnlyList<DavEvent>> FindManyAsync(Guid calendarId, IReadOnlyList<string> davNames, CancellationToken ct);
    IAsyncEnumerable<DavEvent> ChangedAsync(Guid calendarId, ulong after, ulong upTo, CancellationToken ct);
    Task<IReadOnlyList<CalendarTombstone>> TombstonesAsync(Guid calendarId, ulong after, ulong upTo, CancellationToken ct);
    IAsyncEnumerable<DavEvent> CandidatesAsync(Guid calendarId, DateTime? fromUtc, DateTime? toUtc,
        EventColumnFilter columns, ulong upTo, CancellationToken ct);
    Task<int> CountAsync(Guid calendarId, CancellationToken ct);
}
```

`CandidatesAsync` : `e.CalendarId == calendarId && e.SyncSequence <= upTo`, `&& e.FirstOccurrence < toUtc`
si `toUtc` non nul, `&& e.LastOccurrence > fromUtc` si `fromUtc` non nul, `&& e.Status == columns.Status`
etc. pour chaque colonne non nulle ; `AsNoTracking`, `AsAsyncEnumerable`, ordre `dav_name`.
`StreamAsync`/`ChangedAsync` : même forme que `DavContactReader`, sur `CalendarId`.

`CalDavController` (sur `DavControllerBase`) : routes `calendars`, `calendars/{userId:guid}`,
`calendars/{userId:guid}/{calendarName}`, `calendars/{userId:guid}/{calendarName}/{*davName}` pour
`PROPFIND`/`PROPPATCH`/`REPORT` ; `GET`/`HEAD` sur l'événement (verbatim, `ETag`,
`Content-Type` = `DavHeaders.CalendarContentType`, `Last-Modified`, `Content-Length` en octets
UTF-8) ; `OPTIONS` par forme ; `405` par forme ; `308` sur une collection sans barre. Injecte
`IDavCalendarReader`, `ICalendarSyncStore`, `PreferencesDbContext`, `ILogger`. `ContextOrNotFoundAsync` :
`{calendarName}` invalide ou inconnu → `404` ; `{davName}` invalide ou inconnu → `404`.
`ChildrenAsync` : `CalendarCollection` → le home ; `CalendarHome` → `ListAsync` (avec l'état de
chaque agenda lu **d'abord**, par `ReadStateAsync`, dans `InOneSnapshotAsync` étendu à ce genre) ;
`Calendar` → `StreamAsync(id, MemberBound(state))`. `NeedsSnapshot` : `Calendar` en `Depth: 1`,
`CalendarHome` en `Depth: 1`. `PROPPATCH` : le défaut de la base (tout `403`) — la tâche 5 ouvre
l'agenda. `ServeReportAsync` : `false` partout (la tâche 6 remplit).

`CalDavProperties.Tables` — chaque `XName` exact (`C` = `DavXml.CalDav`, `A` = `DavXml.Apple`,
`CS` = `DavXml.CalendarServer`, `D` = `DavXml.Dav`) :

- `CalendarCollection` : `IntermediateCollection("Calendar Homes")`.
- `CalendarHome` : `D:resourcetype` (`collection`), `D:displayname` `Calendars`,
  `D:supported-report-set` (`expand-property`), `D:current-user-principal`.
- `Calendar` : `D:resourcetype` (`collection` + `C:calendar`), `D:displayname`,
  `C:calendar-description`, `A:calendar-color`, `A:calendar-order`, `C:calendar-timezone`
  (`IcsDocument.Serialize` d'un `IcsCalendar` ne contenant que
  `IcsTimeZones.Emit(tz, new DateTime(DateTime.UtcNow.Year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc))`),
  `C:supported-calendar-component-set` (`<C:comp name="VEVENT"/>`), `C:supported-calendar-data`
  (`<C:calendar-data content-type="text/calendar" version="2.0"/>`), `C:supported-collation-set`
  (`i;ascii-casemap`, `i;octet`), `C:max-resource-size`, `C:max-instances`, `CS:getctag`,
  `D:sync-token`, `D:supported-report-set` (`C:calendar-multiget`, `C:calendar-query`,
  `C:free-busy-query`, `D:sync-collection`, `D:expand-property`), `D:current-user-privilege-set`,
  `D:owner`, `D:current-user-principal`.
- `Event` : `D:getetag`, `D:getcontenttype`, `D:getcontentlength`, `D:getlastmodified`,
  `D:resourcetype` (vide), `D:current-user-privilege-set`, `D:supported-report-set`
  (`C:calendar-multiget`, `C:calendar-query`), `C:calendar-data` (`IcsRaw`, **exclu d'`allprop`** :
  `AllPropExclusions` de cette table = `sync-token`, `current-user-privilege-set`, `calendar-data`).

`DavPrincipalProperties` : `C:calendar-home-set` → `Href(CalendarHome)` si `r.CalDavEnabled`,
sinon `null` (donc `404`) — `DavResourceContext` gagne `bool CardDavEnabled`, `bool CalDavEnabled`,
posés par le contrôleur ; `addressbook-home-set` conditionné de même. `C:calendar-user-address-set`
→ un `D:href` `mailto:{adresse}` par entrée de `r.Addresses`. Adresses, dans
`DavPrincipalController.AddressesAsync(User user)` : `user.Email`, puis pour chaque domaine de
`AccountInfo.Domains` autre que le principal `{localpart}@{domaine}` (via `AliasExtensions`),
puis `ISendingIdentityStore.GetAllAsync(uid)` → `Address` ; `Distinct(OrdinalIgnoreCase)` en
minuscules, l'ordre conservé. Un `AccountInfo` indisponible (`Result` en échec) : l'adresse
principale seule, journalisée en `Warning`.

- [ ] **Step 1 : tests** (rougissent). `DavCalendarReaderTests` : liste triée, agenda étranger
      → null, `CandidatesAsync` par bornes et par colonne, `StreamAsync` borné par `upTo`.
      `CalDavPropertiesTests` : table de l'agenda propriété par propriété (le `calendar-timezone`
      se recharge par `IcsDocument.TryLoad` et son unique `VTIMEZONE.TzId` = `Europe/Brussels`),
      `allprop` sans `calendar-data`, `propname` liste `calendar-data`. `CalDavPropfindTests` :
      home vide → un seul `response` ; trois agendas → quatre `response` dans l'ordre ;
      `Depth: infinity` → `403 propfind-finite-depth` ; agenda inconnu → 404 ; agenda `Depth: 1`
      avec deux événements → trois `response`, `getetag` = `"ics_hash"` ; `{userId}` étranger →
      404. `CalDavGetTests` : octets identiques (`Encoding.UTF8.GetBytes` comparés), en-têtes,
      `HEAD` sans corps avec `Content-Length`, 404. `CalDavSurfaceTests` : `OPTIONS` sur les quatre
      formes avec `Allow` et `DAV:` exacts ; `MOVE` → 405 + `Allow` ; `…/{agenda}` sans barre →
      308 ; `DavTestUser(CalDav:false)` → 403 sur le home ; `DavPrincipalTests` (nouveau) :
      les deux home-sets selon les drapeaux, `calendar-user-address-set` = principale + un domaine
      + une identité, sans doublon, en minuscules.
- [ ] **Step 2 : implémenter** lecteur → propriétés → contrôleur → principal → `DavTestServer`.
- [ ] **Step 3 : `dotnet test` complet vert.**
- [ ] **Step 4 : commit** — `feat(caldav): PROPFIND et GET sur les agendas, principal complet`.

---

### Task 4 : `PUT` et `DELETE` d'un événement, les refus en XML

**Files:**
- Create : `Repositories/IDavCalendarWriter.cs`, `Repositories/DavCalendarWriter.cs`,
  `Services/CalDav/CalDavError.cs`, `Services/CalDav/CalDavOutcomeTranslator.cs`, tests
  `Repositories/DavCalendarWriterTests.cs`, `Services/CalDav/CalDavErrorTests.cs`,
  `Services/CalDav/CalDavOutcomeTranslatorTests.cs`, `Controllers/CalDavPutTests.cs`,
  `Controllers/CalDavDeleteTests.cs`, `Controllers/CalDavNoFiveHundredTests.cs`.
- Modify : `Models/Dav/DavWriteStatus.cs` (+ `UnsupportedComponent`, `TooManyInstances`,
  `CollectionFull` renommé depuis `BookFull` — renommage mécanique côté carnet),
  `Controllers/CalDavController.cs`, `Repositories/CalendarEventStore.cs` (`ApplyIcsAsync` rendu
  `internal` accessible au writer — il l'est déjà ; exposer `InTransactionAsync` de même),
  `DavTestServer` (+ `IDavCalendarWriter`, `CalendarEventStore`).

**Interfaces:**

```csharp
public interface IDavCalendarWriter
{
    Task<DavWriteOutcome> PutAsync(Guid userId, Guid calendarId, string davName, string ics,
        CancellationToken ct, bool createOnly = false, string? ifMatch = null);
    Task<DavWriteOutcome> DeleteAsync(Guid userId, Guid calendarId, string davName,
        CancellationToken ct, string? ifMatch = null);
    Task<DavWriteOutcome> DeleteAllAsync(Guid userId, Guid calendarId, CancellationToken ct);
    Task<bool> ArchiveRejectedAsync(Guid userId, Guid calendarId, string davName, string ics, CancellationToken ct);
}

internal static class CalDavError
{
    internal static XName Of(IcsPrecondition precondition);   // la table § 12
    internal static readonly XName NoUidConflict, CalendarCollectionLocationOk, ValidResourceType,
        SupportedFilter, SupportedCollation, ValidFilter, NumberOfMatchesWithinLimits, ValidSyncToken;
}
```

`DavCalendarWriter(CalendarEventStore store, ICalendarSyncStore sync, PreferencesDbContext context, ILogger<DavCalendarWriter> logger)`.
`PutAsync`, dans l'ordre : `IcsGuards.CheckSize(ics)` → `TooLarge` ; `IcsDocument.TryLoad` ;
`IcsGuards.Check(ics, parsed)` → statut par précondition (`SupportedCalendarData` →
`UnsupportedVersion`, `ValidCalendarData` → `InvalidCard`, `ValidCalendarObjectResource` →
`InvalidCard` avec `Precondition` porté, `SupportedCalendarComponent` → `UnsupportedComponent`,
`MaxInstances` → `TooManyInstances`) — **`DavWriteOutcome` gagne `IcsPrecondition? Precondition`**
pour que la traduction XML ne devine pas ; puis la porte (`store.InTransactionAsync`) :
`rank = sync.NextSequenceAsync(calendarId)` ; `row = CalendarEvents.SingleOrDefault(calendarId, davName)` ;
`ifMatch` recomparé (`EntityTagMatcher.Match(ifMatch, "\"" + row.IcsHash + "\"")`) →
`PreconditionFailed` ; `createOnly && row is not null` → `AlreadyExists` ; l'UID
(`IcsDocument.MasterOf(parsed)?.Uid ?? Components.First().Uid`) porté par un **autre** `dav_name`
du **même** agenda → `UidConflict` avec `ConflictHref = DavPaths.Event(userId, calendar.DavName, incumbent.DavName)` ;
`row is null && CountAsync >= CalendarStore.MaxPerCalendar` → `CollectionFull` ; octets identiques
(`row.IcsRaw == ics`) → `Replaced` sans nouveau rang (retour avant la transaction, comme le carnet) ;
sinon archive de l'ancien (`sync.ArchiveAsync(userId, calendarId, row.Id, row.Uid, davName, row.IcsRaw, RevisionCause.Put)`),
ligne neuve (`Id`, `CalendarId`, `UserId`, `DavName = davName`) ou existante, `store.ApplyIcsAsync(row, calendar, ics, parsed, rank)`,
`SaveChangesAsync`, `sync.LiftTombstoneAsync`, `Created`/`Replaced` avec `Etag = "\"" + row.IcsHash + "\""`.
`1205`/`1213` → `Busy`. `DeleteAsync` : rang, `ifMatch`, archive `Delete`, suppression des
participants (cascade EF) et de la ligne, `PlaceTombstoneAsync`, `Deleted`. `DeleteAllAsync` : les
ids d'abord, puis par lots de `CalendarStore.DeleteBatch` chacun sous son rang avec une tombe par
événement ; `Deleted` sur un agenda vide sans rang.

`CalDavOutcomeTranslator.WriteAsync(HttpResponse, DavWriteOutcome, ct, logger)` : même forme que
le carnet ; `ConditionOf` : `InvalidCard` → `outcome.Precondition is { } p ? CalDavError.Of(p) : valid-calendar-data`,
`UnsupportedVersion` → `supported-calendar-data`, `UnsupportedComponent` →
`supported-calendar-component`, `TooManyInstances` → `max-instances`, `TooLarge` →
`max-resource-size`, `UidConflict` → `no-uid-conflict` + `href`, `CollectionFull` → `507`,
`AlreadyExists`/`PreconditionFailed` → `412`, `NotFound` → `404`, `Busy` → `503 Retry-After: 1`,
`Created` → `201`, `Replaced`/`Deleted` → `204` ; l'ETag est **toujours** écrit sur `201`/`204`
du `PUT`.

Contrôleur, `PUT …/{calendarName}/{davName}` : `[RequestSizeLimit(2 * IcsGuards.MaxIcsBytes)]` ;
`TracedAsync` ; `{davName}` invalide → `403 valid-calendar-data` ; agenda inconnu → 404 ;
`RefusedByPreconditions(etag courant)` → `412` + `ArchiveRejectedAsync` ; corps non décodable
(`ReadBodyAsync` null) → `403 valid-calendar-data` ; `writer.PutAsync(…, createOnly: DemandsCreation(), ifMatch: …)` ;
`AnswerPutOutcomeAsync` (le garde `mustCreate && Replaced` de 4c). `PUT /dav/calendars/{u}/x`
(un nom sans agenda) → `403 calendar-collection-location-ok`. `DELETE` d'un événement : `If-Match`,
`writer.DeleteAsync`, `204`/`404`/`412`.

- [ ] **Step 1 : tests** (rougissent). `DavCalendarWriterTests` (InMemory, `CalendarSyncStore`
      remplacé par un `InMemoryCalendarSyncStore` à écrire dans `Infrastructure/`, jumeau de
      `InMemorySyncStore`) : création (`Created`, `dav_name` = le nom donné, `ics_raw` identique
      octet pour octet, index projeté), remplacement (`Replaced`, révision `put` de l'ancien,
      rang avancé), octets identiques → pas de rang, `If-Match` faux → `PreconditionFailed`,
      `createOnly` sur un nom pris → `AlreadyExists`, `UidConflict` avec l'`href` complet, même
      UID dans un **autre** agenda accepté, plafond 5000 → `CollectionFull`, `DeleteAsync` pose
      la tombe et archive `delete`, `DeleteAllAsync` par lots avec un rang par lot,
      `ArchiveRejectedAsync` sans rang. `CalDavErrorTests` : les six correspondances.
      `CalDavOutcomeTranslatorTests` : chaque statut → code + élément + `href`. `CalDavPutTests` :
      chaque ligne du tableau § 10 avec le `<D:error>` attendu (corps de deux méga-octets → 413 ;
      `VERSION:1.0` ; `VTODO` seul ; `VTODO` + `VEVENT` ; deux UID ; deux maîtres ; 10 001
      instances par an ; `no-uid-conflict` ; `If-None-Match: *` sur un nom pris → 412 ;
      `If-Match` faux → 412 et révision `rejected` ; `Content-Type: text/plain` accepté ; `201`
      puis `204` avec `ETag` ; `PUT` sous `/dav/calendars/{u}/x` → 403 `location-ok` ; nom `a/b`
      → 403 `valid-calendar-data`). `CalDavDeleteTests` : `204` + tombe visible par
      `TombstonesAsync`, `If-Match` faux → 412, inconnu → 404. `CalDavNoFiveHundredTests` :
      reprendre la stratégie de `CardDavNoFiveHundredTests` (corps aléatoires, en-têtes cassés,
      verbes inconnus) sur les quatre formes agenda ; aucun 500.
- [ ] **Step 2 : implémenter** erreurs → writer → traducteur → contrôleur.
- [ ] **Step 3 : `dotnet test` complet vert.** Vérifier qu'aucune assertion CardDAV n'a changé
      hormis `BookFull` → `CollectionFull`.
- [ ] **Step 4 : commit** — `feat(caldav): PUT et DELETE d'un evenement, refus RFC 4791 en XML`.

---

### Task 5 : créer, régler et supprimer un agenda depuis un client

**Files:**
- Create : `Services/CalDav/MkCalendarRequest.cs`, `Services/CalDav/CalendarPropertyUpdate.cs`,
  tests `Services/CalDav/MkCalendarRequestTests.cs`, `CalendarPropertyUpdateTests.cs`,
  `Controllers/CalDavMkcalendarTests.cs`, `Controllers/CalDavProppatchTests.cs`,
  `Controllers/CalDavDeleteCollectionTests.cs`.
- Modify : `Repositories/ICalendarStore.cs`, `CalendarStore.cs`, `Models/Calendar/CalendarWrite.cs`
  (+ `string? TimeZone`), `Controllers/CalendarsController.cs` (aucun changement de contrat :
  le `PUT` webmail passe `TimeZone = null`), `Controllers/CalDavController.cs`,
  `Services/Dav/MultiStatusWriter.cs` (+ `WriteMixedAsync(href, IReadOnlyList<XName> ok, IReadOnlyList<XName> refused, ct)`),
  `Services/Dav/DavHeaders.cs` (`NoCache = "no-cache"`), tests `CalendarStoreTests`.

**Interfaces:**

```csharp
public sealed record CalendarWrite(string DisplayName, string? Description, string? Color, int? Order, string? TimeZone = null);

public interface ICalendarStore
{
    /// <summary>Décision 2 du cadrage : the client's URL segment becomes dav_name. Colour next of
    /// the palette, rank last, zone of `default` when timeZone is null; the state row in the same
    /// transaction. Failures: CapReached, NameTaken.</summary>
    Task<Result<Guid>> CreateNamedAsync(Guid userId, string davName, CalendarWrite write, CancellationToken ct);
    // UpdateAsync honours write.TimeZone when non-null (validated IANA by the caller)
}

internal sealed record MkCalendarRequest(
    string? DisplayName, string? Description, string? Color, int? Order, string? TimeZoneId,
    bool AsksUnsupportedComponent, bool ResourceTypeRefused, bool TimeZoneRefused)
{
    /// <param name="extendedMkcol">true for MKCOL (a body is then mandatory and must declare a calendar)</param>
    internal static MkCalendarRequest Parse(XDocument? body, bool extendedMkcol);   // lève DavBadRequestException sur un XML hors forme
}

internal sealed record CalendarPropertyUpdate(
    IReadOnlyDictionary<XName, string?> Accepted,   // valeur (null = DAV:remove) pour les cinq
    IReadOnlyList<XName> Refused,                   // tout le reste + les cinq à valeur invalide
    string? TimeZoneId)
{
    internal static CalendarPropertyUpdate Parse(XDocument? body);
}
```

Règles de `Parse` (les deux) : couleur `#RRGGBB` ou `#RRGGBBFF` (alpha retiré), sinon refusée ;
ordre : entier, sinon refusé ; `calendar-timezone` : `IcsDocument.TryLoad` → exactement un
`VTIMEZONE` → `IcsTimeZones.ResolveIana(TzId)` non nul, sinon refusé ; `displayname` :
`Trim()`, vide = absent (création : le segment ; `PROPPATCH` : refusé) ; `description` : brute ;
`supported-calendar-component-set` : chaque `<C:comp name>` doit être `VEVENT`, sinon
`AsksUnsupportedComponent` ; `resourcetype` : absent OK sur `MKCALENDAR` (obligatoire sur
`MKCOL`), présent ⇒ exactement `{D:collection, C:calendar}` sinon `ResourceTypeRefused` ;
`DAV:remove` sur `calendar-description` → `Accepted[description] = null` ; sur `displayname`,
`calendar-timezone` → `Refused`.

Contrôleur, `MKCALENDAR` et `MKCOL` sur `calendars/{userId:guid}/{calendarName}` :
`TracedAsync` ; nom invalide → `403` nu ; `Parse` (`400` sur XML cassé) ; `ResourceTypeRefused`
→ `403 DAV:valid-resourcetype` ; `TimeZoneRefused` → `403 valid-calendar-data` ;
`AsksUnsupportedComponent` → `207` avec `propstat 403` sur `supported-calendar-component-set`
(via `WriteMixedAsync`), rien créé ; `CreateNamedAsync` : `NameTaken` → `405` avec `Allow` de
l'agenda, `CapReached` → `507` ; succès → `201`, `Cache-Control: no-cache`. `MKCALENDAR`/`MKCOL`
sur `calendars/{userId:guid}/{calendarName}/{*davName}` (sous un agenda) et sur le home →
`403 calendar-collection-location-ok` (la route existe pour répondre ceci, pas `405`).
`PROPPATCH` sur un agenda : `CalendarPropertyUpdate.Parse` ; `UpdateAsync` avec les valeurs
acceptées (les absentes gardent leur valeur : construire `CalendarWrite` depuis le `DavCalendar`
courant) ; `207` mixte par `WriteMixedAsync` ; l'ordre des `propstat` : `200` puis `403`. Aucun
rang, aucun ctag. `DELETE` sur `default` → `writer.DeleteAllAsync` ; sur un autre →
`ICalendarStore.DeleteAsync` → `204` ; inconnu → `404` ; sur le home → `405`.

- [ ] **Step 1 : tests** (rougissent). `CalendarStoreTests` : `CreateNamedAsync` prend le nom,
      l'affichage par défaut = le nom, la couleur suivante, le rang dernier, le fuseau de
      `default` ; `NameTaken` ; `CapReached` à vingt ; la ligne d'état existe après.
      `MkCalendarRequestTests` : le corps réel de DAVx⁵ (discussion #2209 : `resourcetype`,
      `displayname`, `calendar-description` vide, `apple:calendar-color`,
      `supported-calendar-component-set VEVENT`) → tout accepté ; corps Apple avec
      `calendar-free-busy-set` ignoré ; `VTODO` demandé ; `calendarserver:subscribed` ; `MKCOL`
      sans corps ; `calendar-timezone` Windows (`Romance Standard Time`) résolu ; `#FF0000FF` →
      `#FF0000`. `CalendarPropertyUpdateTests` : mixte, `remove`, ordre non entier.
      `CalDavMkcalendarTests` : chaque ligne du tableau § 11 sur les deux verbes ; après un
      `MKCALENDAR`, le `PROPFIND` du home liste le nouvel agenda avec ses valeurs ; sous un
      agenda → `403 location-ok`. `CalDavProppatchTests` : `207` avec `200`×5 et `403`×1
      (`default-alarm-vevent-date`) ; ctag identique avant/après ; valeur relue par `PROPFIND` ;
      sur le home et sur un événement → tout `403`. `CalDavDeleteCollectionTests` : `default`
      vidé (`204`, agenda toujours listé, tombes présentes), secondaire supprimé (`204` puis
      `404`), home → `405`.
- [ ] **Step 2 : implémenter** store → requêtes → writer mixte → contrôleur.
- [ ] **Step 3 : `dotnet test` complet vert.**
- [ ] **Step 4 : commit** — `feat(caldav): MKCALENDAR, MKCOL etendu, PROPPATCH mixte et DELETE d'agenda`.

---

### Task 6 : `calendar-multiget`, `sync-collection`, `expand-property`, `calendar-data` expansée

**Files:**
- Create : `Services/CalDav/EventMemberSource.cs`, `Services/CalDav/CalendarDataRequest.cs`,
  `Services/CalDav/ExpandedCalendarData.cs`, tests `Services/CalDav/CalendarDataRequestTests.cs`,
  `ExpandedCalendarDataTests.cs`, `Controllers/CalDavReportTests.cs`,
  `Controllers/CalDavSyncCollectionTests.cs`.
- Modify : `Controllers/CalDavController.cs` (`ServeReportAsync`), `Services/Dav/ReportRequest.cs`
  (+ `CalendarMultiget`, `CalendarQuery`, `FreeBusyQuery`), `Services/Calendar/IcsComposer.cs`
  (+ `internal static IcsCalendar Instance(IcsCalendar parsed, EventOccurrence occurrence, CalendarEvent source)`).

**Interfaces:**

```csharp
internal sealed record CalendarDataRequest(DateTime? ExpandFrom, DateTime? ExpandTo)
{
    internal bool Expands => ExpandFrom is not null;
    /// <summary>null when the body names no calendar-data; throws DavPreconditionException
    /// (valid-filter) when expand is malformed; comp/prop children are ignored (spec § 7).</summary>
    internal static CalendarDataRequest? Asked(XDocument body);
    internal static DavPropertyRequest PropertiesAsked(XDocument body);   // sans calendar-data
    internal XElement Element(DavEvent evt, string calendarTimeZone);     // <C:calendar-data>…</C:calendar-data>
}

internal static class ExpandedCalendarData
{
    /// <summary>RFC 4791 § 9.6.5: one VEVENT per instance in [from, to[, RECURRENCE-ID and times in
    /// UTC, no RRULE/RDATE/EXDATE, no VTIMEZONE; overrides replace the instance they name; alarms
    /// copied. Throws DavPreconditionException(max-instances) past the cap.</summary>
    internal static string Expand(string icsRaw, DateTime fromUtc, DateTime toUtc, string calendarTimeZone);
}
```

`Expand` appelle `OccurrenceExpander.Expand(eventId: Guid.Empty, calendarId: Guid.Empty, parsed, from, to, tz, tz)` ;
si le nombre rendu atteint `IcsGuards.MaxInstancesPerYear * années + 1` (le `Cap` interne), lève
`max-instances`. Pour chaque occurrence, `IcsComposer.Instance` clone le composant source
(`source.Copy<CalendarEvent>()`), retire `RRULE`/`RDATE`/`EXDATE`, pose `RecurrenceIdentifier`
(`IcsComposer.Utc(StartUtc)` ; pour une journée entière `new CalDateTime(StartDate)`), `DtStart`/`DtEnd`
en UTC (`Utc(StartUtc)`, `Utc(EndUtc)`) ou en `DATE` ; les instances sont ajoutées à un
`IcsCalendar` neuf sans `VTIMEZONE`, `IcsDocument.Serialize`. Une occurrence flottante s'expanse
dans le fuseau de l'agenda (`LocalStart` → `IcsTimeZones.ToUtc(local, tz)`).

`EventMemberSource(IDavCalendarReader reader, DavCalendar calendar, Guid userId, string principalAddress)` :
`MemberNameOf` = `DavPaths.Parse(href)` avec genre `Event`, même utilisateur, `CollectionName ==
calendar.DavName` (ordinal), `DavName.IsValid` ; `Resolve` = `CalDavProperties.Resolve` +
`CalendarDataRequest.Asked(body)?.Element(evt, calendar.TimeZone)`.

Contrôleur, `ServeReportAsync` : `CalendarMultiget` sur `Calendar`/`Event` (sur `Event` : les
`href` sont filtrés à ce seul membre) ; `SyncCollection` sur `Calendar` (`ReadSyncWindowAsync`
sur `calendar_id`, `ReadOrCreateStateAsync` n'existe pas côté agenda — un agenda a toujours son
état, `ReadStateAsync` nul ⇒ `403 valid-sync-token` journalisé en `Error` car c'est une base
incohérente) ; `ExpandProperty` sur `CalendarHome`/`Calendar` ; les autres → `false`.

- [ ] **Step 1 : tests** (rougissent). `CalendarDataRequestTests` : nue, `expand` valide,
      `expand` sans `end` → `valid-filter`, `comp` ignoré. `ExpandedCalendarDataTests` : série
      hebdomadaire de trois semaines → trois `VEVENT` avec `RECURRENCE-ID` UTC, sans `RRULE` ni
      `VTIMEZONE` ; une surcharge remplace son instance ; journée entière en `DATE` ; alarme
      recopiée ; flottant posé dans le fuseau ; plafond → `max-instances`. `CalDavReportTests` :
      multiget de deux `href` + un étranger + un d'un autre agenda → `200`, `200`, `404`, `404` ;
      multiget avec `expand` ; 5001 `href` → `507` ; `expand-property` sur l'agenda résout
      `owner` ; `free-busy-query` (pas encore servi) → `403 supported-report` ;
      `calendar-query` idem à ce stade. `CalDavSyncCollectionTests` : reprise des scénarios de
      `CardDavSyncCollectionTests` sur un agenda ; **un jeton d'un agenda présenté à un autre**
      (epoch différente) → `403 valid-sync-token` ; deux agendas modifiés indépendamment ne se
      réveillent pas l'un l'autre (le ctag de B ne bouge pas quand A écrit).
- [ ] **Step 2 : implémenter.**
- [ ] **Step 3 : `dotnet test` complet vert.**
- [ ] **Step 4 : commit** — `feat(caldav): multiget, sync-collection, expand-property et calendar-data expansee`.

---

### Task 7 : `calendar-query` — filtres, `time-range`, collations

**Files:**
- Create : `Services/CalDav/CalendarQuerySpec.cs`, `Services/CalDav/CalendarQueryFilter.cs`,
  `Services/CalDav/CalendarQueryReport.cs`, tests `Services/CalDav/CalendarQueryFilterTests.cs`,
  `Controllers/CalDavQueryTests.cs`.
- Modify : `Controllers/CalDavController.cs`, `Services/Calendar/OccurrenceExpander.cs`
  (+ `internal static bool Overlaps(IcsCalendar parsed, DateTime fromUtc, DateTime toUtc, string calendarTimeZone)` :
  même marche que `Expand`, s'arrête à la première instance qui chevauche ;
  + `internal static bool AlarmFires(IcsCalendar parsed, DateTime fromUtc, DateTime toUtc, string calendarTimeZone)`),
  `Services/Dav/DavCollation.cs` (+ `Octet = "i;octet"`, `DavCollationComparer` ordinal).

**Interfaces:**

```csharp
internal sealed record TimeRangeSpec(DateTime FromUtc, DateTime ToUtc);   // bornes fermées (§ 8)

internal sealed record CalendarQuerySpec(
    bool AllEvents,                       // pas de comp-filter VEVENT : tout l'agenda
    bool NoneMatch,                       // is-not-defined sur VEVENT : rien
    TimeRangeSpec? TimeRange,
    IReadOnlyList<PropFilterSpec> PropFilters,
    IReadOnlyList<AlarmFilterSpec> AlarmFilters);

internal sealed record AlarmFilterSpec(bool IsNotDefined, TimeRangeSpec? TimeRange);

internal static class CalendarQueryFilter
{
    /// <summary>Throws DavPreconditionException(supported-filter | supported-collation) or
    /// DavBadRequestException on the forms § 8 refuses. Open bounds are closed here, against
    /// <paramref name="nowUtc"/>.</summary>
    internal static CalendarQuerySpec Parse(XElement filter, DateTime nowUtc);
    internal static EventColumnFilter Columns(CalendarQuerySpec spec);   // STATUS/TRANSP/CLASS equals sans negate → colonne
    internal static bool Matches(IcsCalendar parsed, CalendarQuerySpec spec, string calendarTimeZone);
}

internal static class CalendarQueryReport
{
    internal static Task<int> WriteAsync(HttpResponse response, XDocument body, string requestHref,
        DavCalendar calendar, DavEvent? single, IDavCalendarReader reader, ulong upTo, EventMemberSource source,
        TimeProvider clock, CancellationToken ct);
}
```

`Parse` : bornes — `end` absent ⇒ `start + 5 ans` ; `start` absent ⇒ `end − 5 ans` ; les deux
absents ⇒ `[now − 5 ans, now + 5 ans[` ; `end ≤ start` ⇒ `DavBadRequestException`. Le format
accepté est `yyyyMMdd'T'HHmmss'Z'` strict. `prop-filter`/`param-filter`/`text-match` réutilisent
`PropFilterSpec`, `ParamFilterSpec`, `TextMatchSpec` de `Services/Dav` avec les `XName` CalDAV ;
`collation` : `i;ascii-casemap` (défaut), `i;octet` ; autre ⇒ `supported-collation`. `Matches` :
`TimeRange` → `OccurrenceExpander.Overlaps` ; chaque `PropFilterSpec` sur **un** composant au
moins (`IcsDocument.Components`) : la propriété par son nom via `component.Properties[name]`
(Ical.Net expose `Properties` — valeurs sérialisées par `property.Value?.ToString()` ; pour un
`CalDateTime`, `IcsDocument.LiteralOf`), paramètres via `property.Parameters` ; `AlarmFilters` :
`IsNotDefined` ⇒ aucune alarme dans aucun composant ; `TimeRange` ⇒ `OccurrenceExpander.AlarmFires`
(pour chaque instance dans `[from − 1 j, to + 1 j[`, chaque `Alarm` : déclenchement = `Trigger.DateTime`
absolu, ou `instance.Start`/`instance.End` (`Related`) + `Trigger.Duration` ; vrai si dans `[from, to[`).

`WriteAsync` : `single` non nul ⇒ candidats = `[single]` ; sinon `reader.CandidatesAsync(calendar.Id,
spec.TimeRange?.FromUtc, spec.TimeRange?.ToUtc, Columns(spec), upTo)` ; pour chaque candidat
`IcsDocument.TryLoad(IcsRaw)` (nul ⇒ ignoré et journalisé `Warning`), `Matches` ⇒ `source.Resolve`
+ `WriteResourceAsync` ; troncature à `MultigetReport.MaxHrefs` réponses ⇒ `WriteTruncatedAsync`.
Le tout dans `InOneSnapshotAsync` (état lu d'abord, `upTo` = `state.Seq`).

- [ ] **Step 1 : tests** (rougissent). `CalendarQueryFilterTests` : chaque ligne de la grille
      de refus § 8 ; bornes ouvertes fermées à cinq ans ; `Matches` sur : série hebdomadaire dont
      seule la troisième instance chevauche ; journée entière flottante jugée dans
      `Pacific/Auckland` contre `Europe/Brussels` (résultat différent) ; `EXDATE` retirant
      l'unique instance de la fenêtre ⇒ faux ; surcharge déplacée **dans** la fenêtre ⇒ vrai ;
      `VALARM` `TRIGGER:-PT15M` ; `TRIGGER;RELATED=END` ; `TRIGGER;VALUE=DATE-TIME` ;
      `prop-filter SUMMARY text-match` `i;octet` sensible à la casse et `i;ascii-casemap`
      insensible ; `negate-condition` ; `param-filter ATTENDEE PARTSTAT` ; `is-not-defined`
      `LOCATION` ; `Columns` : `STATUS equals CONFIRMED` ⇒ colonne, `STATUS contains` ⇒ pas de
      colonne. `CalDavQueryTests` : DAVx⁵ (`time-range` + `calendar-data`), Thunderbird
      (`comp-filter VEVENT` nu), iOS (`VALARM time-range`), `expand` dans la réponse, sur un
      événement, `403`/`400` des refus, `507` de troncature.
- [ ] **Step 2 : implémenter** spec → filtre → expander → rapport → contrôleur.
- [ ] **Step 3 : `dotnet test` complet vert.**
- [ ] **Step 4 : commit** — `feat(caldav): calendar-query avec time-range, prop-filter et VALARM`.

---

### Task 8 : `free-busy-query`, consistance par agenda, documentation et résidus

**Files:**
- Create : `Services/CalDav/FreeBusyReport.cs`, `assets/calendar-sync-epoch-rotate.sql`,
  `docs/superpowers/calendar-5c-residuals.md`, tests `Services/CalDav/FreeBusyReportTests.cs`,
  `Controllers/CalDavFreeBusyTests.cs`.
- Modify : `Controllers/CalDavController.cs`, `Services/CardDav/SyncStateConsistencyCheck.cs`
  (+ la passe agenda) et `SyncStateConsistencyCheckTests.cs`, `Services/Calendar/OccurrenceExpander.cs`
  (commentaire `RANGE=THISANDFUTURE`), `docs/superpowers/carddav-restore-prerequisite.md`,
  `docs/superpowers/calendar-5a-residuals.md` et `calendar-5b-residuals.md` (marquer « 5c » les
  lignes reprises), `src/frontend/docs/architecture-calendar.md` (section « Ce que CalDAV voit »),
  `src/frontend/CLAUDE.md` si la section agenda y renvoie.

**Interfaces:**

```csharp
internal static class FreeBusyReport
{
    internal sealed record BusyPeriod(DateTime StartUtc, DateTime EndUtc, string Type);   // "BUSY" | "BUSY-TENTATIVE"
    /// <summary>§ 9: TRANSPARENT and CANCELLED dropped, TENTATIVE → BUSY-TENTATIVE, the rest BUSY;
    /// same-type overlapping or touching periods coalesced; ordered by start.</summary>
    internal static IReadOnlyList<BusyPeriod> Periods(IEnumerable<EventOccurrence> occurrences, string calendarTimeZone);
    internal static string Compose(DateTime fromUtc, DateTime toUtc, IReadOnlyList<BusyPeriod> periods, DateTime nowUtc);
    internal static Task WriteAsync(HttpResponse response, XDocument body, DavCalendar calendar,
        IDavCalendarReader reader, ulong upTo, TimeProvider clock, CancellationToken ct);
}
```

`WriteAsync` : `time-range` obligatoire avec les deux bornes (`400` sinon, `end ≤ start` ⇒ `400`),
candidats par `CandidatesAsync(from, to, None)`, `OccurrenceExpander.Expand` par candidat, une
journée entière ⇒ `[minuit UTC de StartDate, minuit UTC de EndDateExclusive[`, un flottant posé
par `IcsTimeZones.ToUtc(local, tz)` ; `200`, `Content-Type: text/calendar; charset=utf-8`, le
`VCALENDAR` composé par Ical.Net (`FreeBusy` avec `Entries` `FreeBusyEntry(Period, FreeBusyStatus)`),
`DTSTAMP` = `clock.GetUtcNow()`. Servi sur l'agenda seul.

`SyncStateConsistencyCheck.RunAsync` : après la passe contacts, `MAX(calendar_events.sync_sequence)`
par `calendar_id` contre `calendar_sync_state.seq` ; une inégalité ⇒ `LogError` avec le
`calendar_id` et le renvoi à `assets/calendar-sync-epoch-rotate.sql` (forme par agenda :
`UPDATE calendar_sync_state SET epoch = UUID() WHERE calendar_id = ?` ; forme base entière : sans
`WHERE`).

`calendar-5c-residuals.md` : le modèle de `calendar-5b-residuals.md` — ce que 5c laisse
(récupération partielle de `calendar-data`, `RANGE=THISANDFUTURE`, bornes ouvertes à cinq ans,
`limit-*` ignorés, les mineurs relevés par les revues), ce que les tests n'ont pas couvert, ce
dont 5d hérite (la procédure du « cinquième cas », les `<features>` de `ccs-caldavtester` à
allumer : `Extended MKCOL`, `free-busy-query`, `calendar-query` avec `expand`).

- [ ] **Step 1 : tests** (rougissent). `FreeBusyReportTests` : fusion de deux plages qui se
      touchent ; `BUSY` et `BUSY-TENTATIVE` chevauchants non fusionnés ; `TRANSPARENT` et
      `CANCELLED` exclus ; journée entière ; flottant ; sortie relue par `IcsDocument.TryLoad`
      avec un `FreeBusy` de N entrées. `CalDavFreeBusyTests` : `200` `text/calendar`, `400` sans
      `end`, sur un événement `403 supported-report`, `supported-report-set` l'annonce.
      `SyncStateConsistencyCheckTests` : un agenda dont un événement outrepasse son compteur
      journalise l'erreur avec son id ; un agenda cohérent se tait.
- [ ] **Step 2 : implémenter**, puis écrire les documents.
- [ ] **Step 3 : `dotnet test` complet vert ; `npm test -- --run` vert.**
- [ ] **Step 4 : commit** — `feat(caldav): free-busy-query, consistance par agenda, docs et residus 5c`.

---

## Auto-revue du plan

**Couverture de la spec.** § 1 → T1 ; § 2, 3, 4, 13 → T2 ; § 5, 6, 7 (lecteur, `PROPFIND`,
`GET`) → T3 ; § 10, 12 → T4 ; § 11 → T5 ; § 7 (rapports, `calendar-data`) → T6 ; § 8 → T7 ;
§ 9, 14 et docs → T8. `allprop` sans `calendar-data` : T3. `Content-Type` jamais consulté : T4.
`is_visible` jamais projeté : T3 (`ListAsync` ne filtre pas). Les deux `InTransactionAsync`
laissés : T4 utilise `store.InTransactionAsync` comme le carnet utilise celui de `ContactStore`.

**Cohérence des types.** `DavResourceContext(Kind, UserId, PrincipalAddress, Card, State,
CollectionName, Addresses, Calendar, Event, CardDavEnabled, CalDavEnabled)` — déclaré en T1
(les cinq derniers à leurs défauts), rempli en T2/T3. `DavWriteOutcome(Status, Etag, ConflictHref,
Sequence, Precondition)` — `Precondition` ajouté en T4, nul côté carnet. `IDavMemberSource<T>.Resolve`
prend `XDocument body` : `CardMemberSource` (T1) et `EventMemberSource` (T6) le respectent.
`CalendarWrite` avec `TimeZone` (T5) reste compatible avec le `PUT` webmail (`null`).
`TimeRangeSpec` (T7) est réutilisé par `FreeBusyReport` (T8) via `CalendarQueryFilter.ParseTimeRange`
— à exposer `internal static TimeRangeSpec ParseTimeRange(XElement, DateTime nowUtc, bool bothRequired)`.

**Sans espace réservé.** Chaque étape nomme ses fichiers, ses signatures et ses cas de test ;
le corps des méthodes extraites en T1 est celui de 4c, non recopié ici par choix — la règle
d'extraction (« garde son corps et son commentaire ») est l'instruction.
