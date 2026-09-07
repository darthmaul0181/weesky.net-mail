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

- **La suite CardDAV ne change pas de sens.** Après chaque tâche, `dotnet test` complet est vert.
  Dans `CardDav*Tests`, `DavContact*Tests` et les tests du socle (`Services/CardDav/*Tests`, qui
  suivent le code sous `Services/Dav/*Tests`) : **aucune assertion existante ne change de valeur
  attendue**. Ajouter des cas est libre — T2 étend `DavPathsTests`, T8 étend
  `SyncStateConsistencyCheckTests` — et les renommages sont mécaniques (espace de noms, nom de
  classe, valeur d'énumération, arité d'un record). Une valeur attendue qui change est une
  régression à corriger dans le produit, **à une exception nommée** :
  `snoopy.microservice.Tests/Controllers/CardDavDeleteTests.cs:241` attend `"1, 3, addressbook"` en
  littéral ; T2 complète `DavHeaders.ComplianceClasses` (§ 4 de la spec) et cette ligne devient
  `Assert.Equal(DavHeaders.ComplianceClasses, …)`, comme les huit autres assertions de l'en-tête
  `DAV:` qui lisent déjà la constante. C'est le seul littéral de la suite qui bouge ; tout autre
  est un bogue.
- **Tout service neuf est câblé.** `Configuration/ApplicationServicesConfiguration.cs` est le seul
  endroit où l'hôte réel enregistre `IDavCalendarReader` (T3) et `IDavCalendarWriter` (T4) ;
  `DavTestServer` enregistre les siens à la main. Une tâche qui oublie le câblage laisse la suite
  verte et donne un `500` à la première requête d'un téléphone.
- **Accessibilités.** Les jumeaux agenda de 5a sont `internal` là où ceux des contacts sont
  `public` : un contrôleur (obligatoirement `public`, sinon MVC ne le découvre pas) ou une
  interface `public` qui les prend en paramètre ne compile pas — CS0051 sur un paramètre,
  CS0053 sur une propriété. La tranche tranche ainsi, une fois pour toutes :
  `ICalendarSyncStore` passe `public` (son jumeau `IContactSyncStore` l'est, et `CalDavController`
  l'injecte comme `CardDavController` injecte l'autre) ; `IcsPrecondition` passe `public` (il
  devient une propriété de `DavWriteOutcome`, qui l'est) ; `IDavCalendarReader` et
  `IDavCalendarWriter` naissent `public` (injectés dans le contrôleur) ; leurs implémentations
  `DavCalendarReader`/`DavCalendarWriter` et tout le reste (`CalendarEventStore`, `CalendarStore`,
  `Services/CalDav/*`) restent `internal`. Un Moq sur une interface `internal` reste possible —
  `InternalsVisibleTo` couvre `snoopy.microservice.Tests` et `DynamicProxyGenAssembly2` — mais
  aucun de ces quatre types n'a besoin de cette porte.
- **La base de contrôleur, elle, ne fait passer aucun type en `public`.** `DavControllerBase` est
  `public` sans échappatoire : le `ControllerFeatureProvider` par défaut de l'hôte réel n'expose
  que des types publics, et une classe publique ne dérive pas d'une base `internal` (CS0060). Or un
  membre `protected` d'une classe publique est visible **hors** assembly, donc un paramètre
  `internal` l'y fait échouer exactement comme sur un constructeur (CS0051/CS0053) — et
  `DavResourceKind`, `DavResourceContext`, `DavPropertyRequest`, `DavReportKind`, `DavDepthValue`
  et `Trace` le sont tous. Chaque membre et chaque crochet de la base est donc
  **`private protected`** : visible des dérivés du même assembly, où ces types vivent, invisible
  dehors. Seul `MaxBodyBytes`, un `int`, reste `protected`. C'est le pendant, côté base, du
  paragraphe précédent : rien ne s'ouvre du seul fait qu'un contrôleur est public.
- **Verbatim** : un `PUT` DAV stocke exactement les octets reçus (après le décodage UTF-8 de
  `DavBody`), sans `VTIMEZONE` ajouté, sans `DTSTAMP`, sans UID inséré. L'ETag est `"ics_hash"`.
- **Le rang d'abord** : toute écriture prend `ICalendarSyncStore.NextSequenceAsync(calendarId)` en
  première instruction de sa transaction, avant de toucher une ligne.
- **Constantes, jamais de littéraux** : `IcsGuards.MaxIcsBytes`, `IcsGuards.MaxInstancesPerYear`,
  `CalendarEventStore.MaxPerCalendar` (5000), `CalendarStore.MaxPerUser` (20), `MultigetReport.MaxHrefs`
  (5000), `OccurrenceExpander.MaxYears` (5), `DavPaths.MaxPathLength` (4864 ; `private` à 2560
  aujourd'hui, rendue `internal` en T2).
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
- **Avant le premier déploiement** (hors code, à la main par l'utilisateur) : rejouer le DDL
  `caldav_enabled` (T2) sur `snoopy_webmail` et `snoopy_webmail_dev`, puis la procédure d'atomicité
  du compteur de `webmail-calendar-tables.md` sur `calendar_id`. T8 corrige ce document, qui parle
  encore de routes `/caldav`. Puis, sur `snoopy_webmail_dev` seulement, la vérification que la
  suite InMemory ne peut pas faire (T2) : allumer CalDAV pour un compte sans agenda et voir naître
  ensemble la ligne `dav_credentials` et l'agenda `default` avec sa ligne `calendar_sync_state`.

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
| `Configuration/` | `ApplicationServicesConfiguration.cs` | le câblage du lecteur (T3) et de l'écrivain (T4) |
| Tests | `Infrastructure/DavTestServer` (trois contrôleurs, `MutableTimeProvider`), `Fixtures/TestCalendarSyncStore` (existe depuis 5a, réutilisé), `Controllers/CalDav*Tests`, `Services/CalDav/*Tests`, `Repositories/DavCalendar*Tests` | |
| Frontend | `SyncPage.tsx`, `SyncPage.test.tsx`, `api.js`, `types/dav.ts`, `locales/{en,fr}/settings.json` | second interrupteur |
| Docs | `webmail-carddav-tables.md`, `webmail-calendar-tables.md`, `carddav-restore-prerequisite.md`, `calendar-5a-residuals.md`, `calendar-5b-residuals.md`, `calendar-5c-residuals.md`, `architecture-calendar.md`, `src/frontend/CLAUDE.md`, `assets/calendar-sync-epoch-rotate.sql` | |

---

### Task 1 : le socle `Services/Dav` et les contrôleurs minces

**Files:**
- Move (commit 1, mécanique) : **35 des 45 fichiers** de `Services/CardDav/` vers `Services/Dav/`
  (espace de noms `weesky.Snoopy.Microservice.Services.Dav`) — les 35 que la spec § 1 énumère,
  dont les quatre que la suite de cette tâche modifie sous `Services/Dav/` :
  `DavResourceContext.cs`, `MultigetReport.cs`, `SyncCollectionReport.cs`,
  `SyncReportOutcome.cs`. Les **dix** qui restent sous `Services/CardDav/` : `AddressBookFilter`,
  `AddressBookFilterSpec`, `AddressBookQueryReport`, `AddressDataFilter`, `AddressDataRequest`,
  `VCardVersionConverter`, `DavProperties` (→ `CardDavProperties`), `DavOutcomeTranslator`
  (→ `CardDavOutcomeTranslator`), `SyncStateConsistencyCheck` et son
  `SyncStateConsistencyCheckHostedService` (T8 les déplace à leur tour, quand le contrôle cesse
  d'être un contrôle de contacts). 35 + 10 = 45 : le compte se vérifie par `ls`, pas de mémoire ;
  `Authentication/CardDav/*`
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
    /// <summary>Reads the report body ONCE — the properties asked, and whether address-data /
    /// calendar-data is among them and under which form. MAY refuse. Called before the multistatus
    /// is opened, never per member.</summary>
    (DavPropertyRequest Request, IDavMemberResolver<TMember> Resolver) Prepare(XDocument body);
}

internal interface IDavMemberResolver<TMember>
{
    (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, TMember member);
}
```

**Deux phases, et c'est un refus qui l'impose.** `MultigetReport` lit aujourd'hui
`AddressDataFilter.PropertiesAsked(body)` puis `AddressDataFilter.Asked(body)` **une fois, avant la
boucle** (`MultigetReport.cs:29-30`), sous son propre commentaire : « *may refuse — before anything
is written* ». Un `address-data` hors forme sort donc en `403` propre. Une
`Resolve(request, member, body)` par membre déplacerait ce refus **dans** le `207` déjà ouvert — un
multistatus tronqué au lieu d'un refus, que `StatusWrittenAfter` ne rattrape plus — et rejouerait
l'analyse XML jusqu'à cinq mille fois. `Prepare` garde le hoist : les deux rapports l'appellent en
**première** instruction, avant `MultiStatusWriter.BeginAsync`, et le résolveur rendu porte
l'`AddressDataRequest?` (ou le `CalendarDataRequest?`) déjà lu. `CardMemberSource.Prepare` est
exactement ces deux lignes d'aujourd'hui, déplacées ; `Resolve` ne voit plus le corps.

```csharp
internal static class MultigetReport
{
    internal const int MaxHrefs = 5000;
    /// <summary>First instruction: source.Prepare(body) — a refusal must leave the response
    /// untouched. Only then MultiStatusWriter.BeginAsync.</summary>
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
    /// <summary>Called by the caller INSIDE its transaction, after it read the state with its own
    /// key: reads the presented token against that state, then the tombstones through the source
    /// — only on a Sequence token, none on an initial or refused one, exactly today's order.</summary>
    internal static Task<SyncWindow> ReadWindowAsync<T>(XElement root, SyncState state,
        IDavMemberSource<T> source, CancellationToken ct);
}
```

La transaction et la lecture de l'état (`ReadOrCreateStateAsync` côté carnet, `ReadStateAsync` côté
agenda) sortent du rapport vers l'appelant, parce que la clé diffère ; le jeton et les tombes, eux,
restent lus par `ReadWindowAsync`, parce que l'ordre est contraint — les tombes ne se lisent qu'une
fois le jeton connu (`after = token.Sequence`, et seulement si `Kind is Sequence`). `CardDavController`
garde la transaction mot pour mot dans une méthode privée `ReadSyncWindowAsync` (ouvrir, état,
`ReadWindowAsync`, commit) ; `CalDavController` (tâche 6) écrira la sienne sur `calendar_id`. Le
`throw new DavPreconditionException(ValidSyncToken)` sur `SyncTokenKind.Invalid` reste dans
`WriteAsync`.

```csharp
namespace weesky.Snoopy.Microservice.Controllers.Dav;

// Les quatre attributs vivent ICI et nulle part ailleurs. MVC lit les attributs du type avec
// inherit: true : re-poser [Route("dav")] sur chaque dérivé donnerait deux IRouteTemplateProvider
// identiques, donc deux sélecteurs et deux endpoints de même gabarit par action —
// AmbiguousMatchException à la première requête. Les trois contrôleurs n'en portent aucun.
[Route("dav")]
[Authorize(Policy = DavAuthenticationDefaults.PolicyName)]
[ApiExplorerSettings(IgnoreApi = true)]
[NoFormBinding]
public abstract class DavControllerBase(ILogger logger) : ApiBaseController
{
    protected const int MaxBodyBytes = 1024 * 1024;

    // private protected et non protected partout où un type internal paraît, cf. les contraintes
    // globales : les dérivés sont dans cet assembly, la porte n'a pas à s'ouvrir plus loin.
    private protected Task DispatchAsync(DavResourceKind kind, Guid? userId, string? collectionName,
        string? davName, string? rootHref, CancellationToken ct);           // PROPFIND / PROPPATCH / REPORT
    private protected Task TracedAsync(Guid? userId, DavResourceKind kind, Func<Trace, Task> action);
    private protected Task PropfindAsync(...);   // la trame de 4c ; les trois crochets ci-dessous
    private protected Task ProppatchAsync(...);  // par défaut : tout à 403 (WriteRefusalAsync)
    private protected Task ReportAsync(...);     // lecture du corps, trace.Report, puis ServeReportAsync

    // Les crochets que chaque protocole remplit
    private protected abstract Task<DavResourceContext?> ContextOrNotFoundAsync(DavResourceKind kind,
        string? collectionName, string? davName, CancellationToken ct);
    private protected abstract (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, DavResourceContext resource);
    private protected abstract IAsyncEnumerable<(string Href, DavResourceContext Resource)> ChildrenAsync(
        DavResourceContext parent, DavPropertyRequest request, CancellationToken ct);
    private protected abstract Task<bool> ServeReportAsync(DavReportKind report, XDocument body,
        DavResourceContext resource, string requestHref, Trace trace, CancellationToken ct); // false = 403 supported-report
    private protected abstract bool NeedsSnapshot(DavResourceKind kind, DavDepthValue depth);
    private protected abstract string HrefOf(DavResourceKind kind, Guid userId, string? collectionName, string? davName, string? rootHref);
    private protected abstract string? CanonicalOf(DavResourceKind kind, Guid userId, string? collectionName);

    // Ce qui ne bouge pas de 4c, rendu private protected : RefuseAsync, BodyRefused, ReadBodyAsync
    // (ex-ReadCardBodyAsync), RefusedByPreconditions, DemandsCreation, HeaderOrNull, DepthHeader,
    // LogRequest, StatusWrittenAfter, Capabilities, MethodNotAllowed, InOneSnapshotAsync,
    // WriteResourceAsync, la classe Trace (private protected sealed).
}
```

`DavResourceContext` (déplacé sous `Services/Dav/`) gagne `string? CollectionName` et
`IReadOnlyList<string> Addresses` ; `DavCard? Card` reste (le contexte agenda ajoutera
`DavCalendar? Calendar`, `DavEvent? Event` et les deux drapeaux en tâche 3 — déclarer les
propriétés dès maintenant). **Chaque membre neuf s'ajoute en queue avec sa valeur par défaut**
(`= null`, les drapeaux à `true`, et `IReadOnlyList<string>? Addresses = null` — `= []` ne compile
pas en valeur par défaut, une collection expression n'étant pas une constante de compilation
(CS1736) ; le contrôleur pose la liste, la table lit `Addresses ?? []`) : les cinq paramètres de 4c gardent leur
position, et les deux sites de construction de `CardDavController` ne changent pas d'une virgule.
La même règle vaut pour tout record du socle que la tranche étend — c'est ce qui tient la
contrainte « aucune assertion existante ne change ».

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
      les propriétés résolues par la source, et **une source dont `Prepare` lève laisse la réponse
      intacte** (aucun octet écrit, le refus remonte à l'appelant) ; sync-collection fusionne membres et tombes par rang,
      tronque sur une frontière de rang, frappe le jeton du compteur. Ces tests rougissent (les
      signatures n'existent pas).
- [ ] **Step 5 : `IDavMemberSource`, `DavTombstone`, rapports génériques, `CardMemberSource`.**
      `CardMemberSource(IDavContactReader contacts, Guid userId, string principalAddress)` :
      `MemberNameOf` = l'ancien `IsOurs` (`DavPaths.Parse` + genre `Card` + même utilisateur +
      `DavName.IsValid`) ; `Prepare` = `AddressDataFilter.PropertiesAsked(body)` +
      `Asked(body)`, les deux lignes que `MultigetReport` tenait avant sa boucle, déplacées telles
      quelles. `ContactTombstone` → `DavTombstone` par une projection dans la source.
- [ ] **Step 6 : `DavControllerBase`, `DavPrincipalController`, `DavPropertyTables`,
      `DavPrincipalProperties` ; `CardDavController` réécrit dessus.** Extraire, ne pas réécrire :
      chaque méthode déplacée garde son corps et son commentaire. Les routes du principal
      (`""`, `"/"`, `principals`, `principals/{userId:guid}`, leurs `OPTIONS` et `405`) quittent
      `CardDavController`. Le `NestedContext` d'`expand-property` vit dans
      `DavPrincipalController` et résout les genres `Principal`, `AddressBookHome`, `CalendarHome`
      (ce dernier ajouté en tâche 2 ; ici, seulement ce qui existe).
- [ ] **Step 7 : `DavTestServer`** ajoute `DavPrincipalController` à
      `SelectedControllerFeatureProvider` (`CardDavController` et `WellKnownController` y sont
      déjà). Aucun test CardDAV ne change hormis les renommages.
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
  `DavCredentialStore.cs`, `Repositories/CalendarStore.cs` (`InTransactionAsync` réentrant),
  `Controllers/DavCredentialsController.cs`, `Models/DavCredentialsView.cs`,
  `docs/superpowers/webmail-carddav-tables.md`, `src/frontend/src/api.js`,
  `src/frontend/src/types/dav.ts` (`DavCredentials.calDavEnabled` — sans lui `SyncPage` ne
  compile pas), `src/frontend/src/modules/settings/sync/SyncPage.tsx`, `SyncPage.test.tsx`,
  `src/frontend/src/locales/{en,fr}/settings.json`, tests
  (`DavPathsTests`, `DavAuthenticationHandlerTests`, `DavCredentialStoreTests`,
  `DavCredentialsControllerTests`, `WellKnownControllerTests`, `DavTestUser`,
  `TestDavAuthenticationHandler`, et les trois autres fichiers qui construisent les records du
  store : `Authentication/AuthAttemptThrottleSeamTests.cs`,
  `Authentication/DavAuthenticationGenerationSeamTests.cs` (ex-`CardDav…`) et
  `Repositories/DavCredentialStoreTests.cs`).
- Create : `Services/Dav/DavProtocol.cs`, `Models/Dav/DavCalDavToggle.cs`, tests
  `Controllers/DavPrincipalTests.cs`.

**Interfaces:**

```csharp
internal enum DavResourceKind
{
    ServiceRoot, PrincipalCollection, Principal,
    AddressBookCollection, AddressBookHome, AddressBook, Card,
    CalendarCollection, CalendarHome, Calendar, Event
}

/// <summary>CollectionName: the calendar's URL segment, decoded once, unjudged; null off the
/// calendar tree. LAST and defaulted, so the three positional arguments of 4c keep their place:
/// every existing `new DavResource(kind, id, name)` (DavPaths, the tests) still compiles and
/// reads the same; DavPathsTests reads `.Kind`/`.DavName`, never a positional pattern.</summary>
internal sealed record DavResource(
    DavResourceKind Kind, Guid UserId, string? DavName, string? CollectionName = null);

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

// CalDavEnabled is inserted BESIDE CardDavEnabled, not appended: these records are the credential
// store's, outside the CardDAV suite the global constraint freezes. Inserting a bool in the middle
// is a COMPILE error at every construction — a bool never binds to a string or a DateTime? — so the
// compiler names them all and nothing drifts silently. There are five files, not two:
// DavCredentialStoreTests, DavCredentialsControllerTests, AuthAttemptThrottleSeamTests,
// DavAuthenticationGenerationSeamTests and DavAuthenticationHandlerTests (five constructions there).
// DavCredentialRecord.ToString(), the anti-leak override, prints both flags.
public readonly record struct DavCredentialState(bool Configured, bool CardDavEnabled, bool CalDavEnabled, DateTime? LastUsedAt);
public readonly record struct DavCredentialRecord(bool CardDavEnabled, bool CalDavEnabled, string SecretHash, byte[] Salt);

public interface IDavCredentialStore
{
    /// <param name="alongside">runs inside the enabling transaction, after the row is written;
    /// null for none. How the default calendar shares the switch's transaction without a
    /// controller opening one.</param>
    Task<string?> EnableAsync(Guid userId, DavProtocol protocol, Func<Task>? alongside, CancellationToken ct);
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
private protected bool Enabled(DavProtocol protocol) =>
    User.FindFirst(protocol is DavProtocol.CardDav ? WebmailClaimTypes.CardDav : WebmailClaimTypes.CalDav)?.Value != "0";
```

`TracedAsync` reçoit le protocole de la ressource et répond `403` (sans corps, `LogRequest`)
**avant** la vérification du `{userId}` quand `Enabled` est faux — sauf sur `OPTIONS`, anonyme.
`DavPrincipalController` passe par un `Enabled(CardDav) || Enabled(CalDav)`.

Handler : `if (!identity.CardDavEnabled && !identity.CalDavEnabled) return Refuse(Outcome.Forbidden);`
puis les deux claims. Le cache stocke la nouvelle `DavIdentity` (aucune autre modification).

`DavTestUser(string Email, Guid Uid, bool CardDav = true, bool CalDav = true)` ; le handler de
test écrit les deux claims.

`DavHeaders.ComplianceClasses = "1, 3, addressbook, calendar-access, extended-mkcol"` — la seule
valeur attendue de la suite CardDAV qui change, cf. les contraintes globales : `CardDavDeleteTests.cs:241`
passe du littéral à la constante, et les huit autres assertions la lisent déjà.
`CalendarCollectionAllow` n'existe pas : `/dav/calendars/` sert exactement la chaîne de
`HomeAllow`, qui est déjà celle de la racine, du principal et du home de contacts — une seconde
constante de même contenu serait le doublon que la règle interdit.
`CalendarHomeAllow = "OPTIONS, PROPFIND, PROPPATCH, REPORT, MKCALENDAR, MKCOL"` — le parent où l'on
crée, ce que sabre annonce aussi ;
`CalendarAllow = "OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT, MKCALENDAR, MKCOL"` et **non**
`CollectionAllow` : c'est **cette** forme qui sert les deux verbes (T5), et RFC 9110 § 15.5.6 veut
qu'un `405` annonce les méthodes que la ressource sert — `CollectionAllow` tel quel ferait mentir
le fourre-tout de l'agenda. Entre T2 et T5 l'annonce précède le service, comme celle de
`supported-report-set` entre T3 et T8, et pour la même raison : la découpe en tâches n'est pas un
état qu'un client voit. `EventAllow = CardAllow`,
`CalendarContentType = "text/calendar; charset=utf-8; component=VEVENT"`.
`DavXml.CalDav = "urn:ietf:params:xml:ns:caldav"`, `DavXml.Apple = "http://apple.com/ns/ical/"`.
`WellKnownController` : le `[Route(".well-known/carddav")]` est posé sur la **classe**, l'action
`CardDav()` n'en porte aucun ; ajouter un second `[Route(".well-known/caldav")]` sur la classe
(les deux gabarits se combinent sur la même action), sans toucher au corps.

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
    ICalendarStore calendars, CancellationToken ct)   // pas de PreferencesDbContext ici
```

À l'allumage : `toggle.TimeZone` obligatoire et `IcsTimeZones.IsKnownIana` (`400` sinon) ;
`store.EnableAsync(uid, DavProtocol.CalDav, alongside: () => calendars.EnsureDefaultAsync(uid, toggle.TimeZone!, ct), ct)`.
C'est le store qui ouvre la transaction (`CreateExecutionStrategy().ExecuteAsync` +
`BeginTransactionAsync`, la forme de `CalendarStore.InTransactionAsync`) et y exécute `alongside`
après avoir écrit la ligne.

**Cette transaction est neuve, et c'est le morceau de T2 qui coûte.** `DavCredentialStore.EnableAsync`
n'en ouvre aucune aujourd'hui : deux `SaveChangesAsync` nus et un `catch (DbUpdateException)` qui
rattrape la course de la première activation (détacher la perdante, relire la gagnante, la
rallumer). L'enveloppe s'ajoute **autour** de ce rattrapage, sans le toucher :
`BeginTransactionAsync` **à l'intérieur** d'`ExecuteAsync` et jamais dehors, sinon une stratégie de
reprise refuserait une transaction ouverte par l'appelant (aucune n'est configurée aujourd'hui —
`UseMySql` sans `EnableRetryOnFailure` — mais la forme est celle de `CalendarStore`).

**Et l'isolation est `ReadCommitted`, explicitement.** MariaDB n'annule pas la transaction sur une
clé dupliquée, donc le rattrapage de course survit — mais sous le `RepeatableRead` par défaut,
l'instantané est pris à la première lecture : la ligne que le rival commite dans la fenêtre de
course reste **invisible**, la relecture du gagnant rend `null` et le `catch` relance. Un double
clic devient un `500` là où 4c rendait l'état. Le même piège vaut pour le `catch` d'`EnsureDefaultAsync`
quand elle tourne en `alongside`. Le provider InMemory n'a ni transaction ni isolation : il ne peut
pas le voir. Le `ChangeTracker.Clear()` d'`EnsureDefaultAsync` est, lui, sans effet sur la ligne de
credentials, déjà persistée avant l'appel d'`alongside`.

**L'imbrication ne va pas de soi et se paie d'un changement.** `EnsureDefaultAsync` passe
aujourd'hui par `AddAsync` → `CalendarStore.InTransactionAsync` → `BeginTransactionAsync`
(`CalendarStore.cs`), sur le **même** `PreferencesDbContext` scoped que `DavCredentialStore` :
appelée telle quelle depuis `alongside`, elle lève `InvalidOperationException` (« the connection
is already in a transaction ») sur MariaDB. `CalendarStore.InTransactionAsync` devient donc
**réentrant** : `context.Database.CurrentTransaction is not null` ⇒ le corps s'exécute dans la
transaction ambiante, sans en ouvrir ni en commiter une — celui qui a ouvert commite, et une
exception remonte le rollback. Le seul appelant réentrant est `EnsureDefaultAsync`, qui ne rend
pas de `Result` d'échec : la règle « un `Result` en échec ne commite pas » reste entière pour les
appelants de premier niveau, et on ne fabrique pas un cas où un échec silencieux devrait annuler
la transaction de quelqu'un d'autre.

**Ce point ne se prouve pas par la suite.** Le provider InMemory des tests n'a pas de
transactions : `CurrentTransaction` y reste nul, la branche réentrante n'est jamais prise et
`PreferencesTestDbContext` ne fait qu'ignorer (ou rendre fatal) le
`TransactionIgnoredWarning`. Le filet est double — un test qui vérifie que `alongside` est
exécuté une fois et après l'écriture de la ligne (T2, step 1), et la **vérification manuelle**
ajoutée aux prérequis de déploiement : sur `snoopy_webmail_dev`, allumer CalDAV depuis l'écran
pour un compte qui n'a aucun agenda, et vérifier que la ligne `dav_credentials` et l'agenda
`default` (avec sa ligne `calendar_sync_state`) naissent ensemble.
`SetCardDav` passe `alongside: null`. Puis `throttle.ForgetIdentifier`,
`cache.Forget`, audit `Audit: caldav_sync user={UserId} enabled={Enabled} created={Created} outcome=success`.
À l'extinction : `DisableAsync`, cache oublié, audit. `EnableAsync` pose **explicitement**
`CardDavEnabled = protocol is CardDav`, `CalDavEnabled = protocol is CalDav` sur une ligne neuve.

Frontend : `api.setDavCalDav: (enabled, timeZone) => request('PUT', '/api/DavCredentials/CalDav', { enabled, timeZone })` ;
dans `SyncPage`, l'état optimiste devient **par interrupteur** :
`pending: { key: 'carddav' | 'caldav'; value: boolean } | null`, `write(call, optimistic?: Pending)`,
et chaque `ToggleRow` lit `checked={pending?.key === 'caldav' ? pending.value : state.calDavEnabled}`
(idem `carddav`) — le `pending` booléen unique d'aujourd'hui afficherait la valeur optimiste de l'un
sur l'autre. Second `ToggleRow` `id="sync-caldav"`, `label={t('sync.caldav')}`,
`hint={t('sync.caldavHint')}`, `disabled={busy}`,
`onChange={on => write(() => api.setDavCalDav(on, Intl.DateTimeFormat().resolvedOptions().timeZone), { key: 'caldav', value: on })}`.
Catalogues : `en` — `"caldav": "Calendar (CalDAV)"`, `"caldavHint": "Sync your calendars with your phone or Thunderbird. Turning this off stops every device; your password is kept."` ;
`fr` — `"caldav": "Agenda (CalDAV)"`, `"caldavHint": "Synchronisez vos agendas avec votre téléphone ou Thunderbird. Désactiver arrête tous les appareils ; votre mot de passe est conservé."` (espace insécable devant `;`, comme la ligne CardDAV).
Et **`sync.notConfigured` cesse d'être vrai** : il dit aujourd'hui « Turn Contacts (CardDAV) on to
get a password » / « Activez Contacts (CardDAV) pour obtenir un mot de passe », alors que
`EnableAsync(CalDav)` frappe désormais le même secret sur une table vide. `en` — `"Turn Contacts
(CardDAV) or Calendar (CalDAV) on to get a password"` ; `fr` — `"Activez Contacts (CardDAV) ou
Agenda (CalDAV) pour obtenir un mot de passe"`. Le panneau du mot de passe ne change pas de gate.

- [ ] **Step 1 : tests.** `DavPathsTests` : les quatre chemins agenda, `Calendar` avec un nom
      encodé (`Mes%20vacances` → `Mes vacances`), un `%2F` qui reste refusé par `DavName.IsValid`
      côté appelant (le parseur le rend décodé, tel quel), un segment vide au milieu → null, un
      chemin de 4865 caractères → null, `Event` avec le nom et le membre encodés, la
      correspondance aller-retour `Parse(Event(u, n, m))`. `DavAuthenticationHandlerTests` :
      `(false,false)` → 403 ; `(true,false)`, `(false,true)` → succès avec les deux claims.
      `DavPrincipalTests` (nouveau, `Controllers/` — rien n'est ajouté à `CardDav*Tests`,
      contrainte globale) : sous `DavTestUser(CardDav:false)`, `PROPFIND /dav/addressbooks/{u}/`
      → 403 sans corps, `PROPFIND /dav/principals/{u}/` → 207 tant que `CalDav:true` ; sous
      `(false,false)` le principal répond 403. `DavCredentialStoreTests` : `EnableAsync(CalDav)`
      sur une table vide crée `carddav_enabled = false, caldav_enabled = true` ; l'inverse ;
      `alongside` exécuté une fois, la ligne déjà visible dans le contexte ; `GetStateAsync` rend
      les deux. `DavCredentialsControllerTests` (store, cache et throttle restent des Moq) :
      `SetCalDav` avec fuseau appelle `EnableAsync(uid, CalDav, alongside, ct)` avec un `alongside`
      non nul que le test invoque et qui appelle `ICalendarStore.EnsureDefaultAsync(uid, fuseau)`
      (Moq vérifié) ; `400` sans fuseau ou avec `Mars/Olympus`, extinction sans fuseau OK, audit
      journalisé. `WellKnownControllerTests` :
      `PROPFIND /.well-known/caldav` → 301 vers `/dav/`, `Cache-Control: max-age=86400`.
      `SyncPage.test.tsx` : le second interrupteur rend `calDavEnabled`, un clic appelle
      `setDavCalDav(true, <fuseau>)` ; pendant cet appel en attente, l'interrupteur CardDAV garde
      `state.cardDavEnabled`. Tests de parité `en`/`fr` et de typographie : verts par
      construction (ils existent). Tout rougit.
- [ ] **Step 2 : implémenter** dans l'ordre chemins (`MaxPathLength` passe `internal`, 2560 →
      4864) → en-têtes → identité/claims → store/DDL → contrôleur → well-known → frontend
      (`types/dav.ts` avant `SyncPage.tsx`).
      L'en-tête complété fait rougir `CardDavDeleteTests.cs:241`, le seul littéral de la suite :
      il passe à `DavHeaders.ComplianceClasses` et rien d'autre ne bouge dans ce fichier.
- [ ] **Step 2b : les deux transactions, à part.** `CalendarStore.InTransactionAsync` devient
      réentrant, et `DavCredentialStore.EnableAsync` gagne l'enveloppe qu'il n'a pas
      (`CreateExecutionStrategy().ExecuteAsync` + `BeginTransactionAsync` dedans) autour de son
      rattrapage de course, puis exécute `alongside` après la ligne. Séparé du reste parce que
      c'est le seul endroit de la tranche où le comportement transactionnel change et où la suite
      InMemory ne peut rien prouver : le test qui l'accompagne vérifie ce qu'elle peut —
      `alongside` appelé une fois, après l'écriture, la ligne visible dans le contexte — et le
      reste est la vérification manuelle des prérequis de déploiement.
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
  `Repositories/CalendarEventStore.cs` (`Margin` et `Shift` rendus `internal`),
  `Repositories/ICalendarSyncStore.cs` (passe `public`, cf. contraintes globales),
  `Configuration/ApplicationServicesConfiguration.cs`
  (`services.AddScoped<IDavCalendarReader, DavCalendarReader>()`, à côté de son jumeau contacts —
  sans quoi la suite reste verte et l'hôte réel répond `500`),
  `Infrastructure/DavTestServer.cs` (+ `CalDavController`, `ICalendarStore`, `ICalendarSyncStore`,
  `IDavCalendarReader`, `ISendingIdentityStore` Moq vide, `IAccountInfoProvider` stub, et un
  `TimeProvider` : l'hôte de test est un `HostBuilder` nu qui n'en enregistre aucun — y poser le
  `MutableTimeProvider` d'`Infrastructure/`, figé sur une date connue, exposé par `DavTestServer`
  pour que les assertions sur `calendar-timezone` et `DTSTAMP` ne dépendent pas du jour).

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

`CandidatesAsync` : `e.CalendarId == calendarId && e.SyncSequence <= upTo`,
`&& e.FirstOccurrence < upper` si `toUtc` non nul, `&& e.LastOccurrence > lower` si `fromUtc` non
nul, `&& e.Status == columns.Status` etc. pour chaque colonne non nulle ; `AsNoTracking`,
`AsAsyncEnumerable`, ordre `dav_name`.

**Les bornes portent la marge d'un jour de `CalendarEventStore.WindowAsync`** — `lower = fromUtc − 1 j`,
`upper = toUtc + 1 j`, la même `Margin` et pour la même raison, écrite dans ce fichier : les
colonnes tiennent des instants posés dans le fuseau de l'agenda, et l'appartenance d'une journée
entière est décidée par l'expander, sur des dates ; les deux lectures diffèrent de moins d'un
jour. Sans elle, une journée entière ou un événement flottant au bord de la fenêtre est écarté
**avant** toute expansion, et le `calendar-query` comme le `free-busy-query` répondent faux sans
rien dire. La marge est une présélection large : c'est `Matches` (T7) et `Periods` (T8) qui
tranchent ensuite, sur le fichier. Extraire la constante existante plutôt que la réécrire —
`CalendarEventStore.Margin` et son `Shift` (qui garde la soustraction dans les bornes de
`DateTime`) passent `internal` et `DavCalendarReader` les lit.
`StreamAsync`/`ChangedAsync` : même forme que `DavContactReader`, sur `CalendarId`.

`CalDavController` (sur `DavControllerBase`) : routes `calendars`, `calendars/{userId:guid}`,
`calendars/{userId:guid}/{calendarName}`, `calendars/{userId:guid}/{calendarName}/{*davName}` pour
`PROPFIND`/`PROPPATCH`/`REPORT` ; `GET`/`HEAD` sur l'événement (verbatim, `ETag`,
`Content-Type` = `DavHeaders.CalendarContentType`, `Last-Modified`, `Content-Length` en octets
UTF-8) ; `OPTIONS` par forme ; `405` par forme ; `308` sur une collection sans barre. Injecte
`IDavCalendarReader`, `ICalendarSyncStore`, `PreferencesDbContext`, `TimeProvider` (pour la table
et, en T7/T8, les rapports), `ILogger`. `ContextOrNotFoundAsync` :
`{calendarName}` invalide ou inconnu → `404` ; `{davName}` invalide ou inconnu → `404`.
`ChildrenAsync` : `CalendarCollection` → le home ; `CalendarHome` → `ListAsync` (avec l'état de
chaque agenda lu **d'abord**, par `ReadStateAsync`, dans `InOneSnapshotAsync` étendu à ce genre) ;
`Calendar` → `StreamAsync(id, MemberBound(state))`. `NeedsSnapshot` : `Calendar` en `Depth: 1`,
`CalendarHome` en `Depth: 1`. `PROPPATCH` : le défaut de la base (tout `403`) — la tâche 5 ouvre
l'agenda. `ServeReportAsync` : `false` partout (la tâche 6 remplit). `D:supported-report-set`
annonce pourtant les cinq rapports dès ici : c'est la table de la spec § 6, et la découpe en
tâches n'est pas un état qu'un client voit. Entre T3 et T8 l'annonce précède donc le service, et
le seul test de rapport de T3 est le `403 supported-report` que la base rend.

`CalDavProperties.Tables` — chaque `XName` exact (`C` = `DavXml.CalDav`, `A` = `DavXml.Apple`,
`CS` = `DavXml.CalendarServer`, `D` = `DavXml.Dav`) :

- `CalendarCollection` : `IntermediateCollection("Calendar Homes")`.
- `CalendarHome` : `D:resourcetype` (`collection`), `D:displayname` `Calendars`,
  `D:supported-report-set` (`expand-property`), `D:current-user-principal`.
- `Calendar` : `D:resourcetype` (`collection` + `C:calendar`), `D:displayname`,
  `C:calendar-description`, `A:calendar-color`, `A:calendar-order`, `C:calendar-timezone`
  (`IcsDocument.Serialize` d'un `IcsCalendar` ne contenant que
  `IcsTimeZones.Emit(tz, new DateTime(year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc))`, où `year`
  vient du `TimeProvider` que le contrôleur passe à la table — comme T7 et T8 le font pour leurs
  rapports, et jamais `DateTime.UtcNow` : une table qui lit l'horloge du système donne une
  assertion qui dépend du jour où elle tourne),
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
`DavPrincipalController.AddressesAsync(User user)` : `user.Email`, puis pour chaque `Domain` de
`AccountInfo.Domains` dont `Name` n'est pas `user.Domain` : `$"{user.Name}@{domain.Name}"`
(`AliasExtensions` ne s'applique qu'à `Alias`, pas ici ; `AccountInfo.Mailbox` est l'id du domaine
principal, jamais une adresse), puis `ISendingIdentityStore.GetAllAsync(uid)` → `Address` ; `Distinct(OrdinalIgnoreCase)` en
minuscules, l'ordre conservé. Un `AccountInfo` indisponible (`Result` en échec) : l'adresse
principale seule, journalisée en `Warning`.

**Le coût est un appel au fournisseur par `PROPFIND` du principal**, c'est-à-dire une fois par
cycle de synchronisation et par appareil. La seule implémentation d'`IAccountInfoProvider`
aujourd'hui, `ClaimsAccountInfoProvider` (`Platform/Generic/`), construit `AccountInfo` depuis les
claims sans sortir du processus — le coût est nul ici ; c'est le contrat de l'interface qui
autorise une implémentation à interroger la plateforme, et c'est pour elle que la règle est
écrite. L'appel n'est fait **que** lorsque le corps demande `calendar-user-address-set` ou un
`allprop` — jamais sur les autres propriétés du principal, jamais sur le home ni sur un agenda —
et le résultat est gardé pour la durée de la requête dans `DavResourceContext.Addresses`, lu une
fois. Aucun cache entre requêtes en 5c : rien à mesurer tant que le fournisseur est celui-là, et
un cache posé avant une mesure serait une invalidation à écrire sans savoir ce qu'elle coûte.
`calendar-5c-residuals.md` (T8) porte la ligne.

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
      308 ; `DavTestUser(CalDav:false)` → 403 sur le home ; `DavPrincipalTests` (étendu) :
      les deux home-sets selon les drapeaux, `calendar-user-address-set` = principale + un domaine
      + une identité, sans doublon, en minuscules. `DavCalendarReaderTests` couvre aussi la marge,
      sur le cas que le commentaire de `CalendarEventStore.Margin` décrit : un agenda en
      `Pacific/Auckland`, une journée entière du 2 janvier — colonnes à
      `[01T11:00Z, 02T11:00Z]`, instance lue par l'expander à `[02T00:00Z, 03T00:00Z[` — reste
      candidate pour la fenêtre `[02T12:00Z, 03T00:00Z[`, que les bornes nues écarteraient.
- [ ] **Step 2 : implémenter** lecteur → propriétés → contrôleur → principal →
      `ApplicationServicesConfiguration` → `DavTestServer`.
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
  `Models/Dav/DavWriteOutcome.cs` (+ `Precondition`, voir plus bas),
  `Services/Calendar/IcsPrecondition.cs` (passe `public` : il devient une propriété d'un record
  qui l'est — sinon CS0053), `Controllers/CalDavController.cs`,
  `Repositories/CalendarEventStore.cs` (`ApplyIcsAsync` et `InTransactionAsync` sont déjà
  `internal` : rien à exposer, seulement à vérifier ; `Parse` appelle désormais `IcsGuards.CheckAll`
  et sa constante `NoStart` déménage vers `IcsGuards`, relayée ici),
  `Services/Calendar/IcsGuards.cs` (+ `CheckAll`, `CheckStart` et `NoStart`, voir plus bas),
  `Configuration/ApplicationServicesConfiguration.cs`
  (`services.AddScoped<IDavCalendarWriter, DavCalendarWriter>()`),
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

`IDavCalendarWriter` est `public` (le contrôleur, qui l'est nécessairement, l'injecte) et
`DavCalendarWriter` `internal sealed` : son constructeur prend `CalendarEventStore`, qui est
`internal`, et une classe publique ne le pourrait pas (CS0051). Le câblage vit dans l'assembly,
la porte reste fermée.

`DavCalendarWriter(CalendarEventStore store, ICalendarSyncStore sync, PreferencesDbContext context, ILogger<DavCalendarWriter> logger)`.

**Les cinq gardes, dans l'ordre de `CalendarEventStore.Parse`, et à un seul endroit.**
`IcsGuards.Check` ne juge ni la densité ni l'expansibilité : `max-instances` vient de
`CheckDensity`, « la récurrence ne se déroule pas » (`valid-calendar-data`) de `CheckExpansion`,
et `Parse` (`CalendarEventStore.cs:439`) les enchaîne après `Check`. **Il en enchaîne une
cinquième, hors d'`IcsGuards` : `master.DtStart is null → NoStart`** — `Check` ne lit jamais
`DTSTART` et `CheckExpansion` rend `null` quand il manque. Sans elle, un `PUT` DAV d'un `VEVENT`
sans `DTSTART` serait **accepté** : `IcsTimeZones.Place(null, …)` rend `NoInstant`, la ligne naît
avec `first_occurrence`/`last_occurrence` vides — invisible de toute fenêtre, de tout
`calendar-query`, de l'écran, et sans un mot dans les journaux — alors que RFC 5545 § 3.6.1 rend
`DTSTART` obligatoire pour un `VEVENT` sans `METHOD`. Le `PUT` doit donc appliquer les cinq, sans
recopier la séquence :

```csharp
internal const string NoStart = "The event carries no start";   // la constante de CalendarEventStore, déplacée ici

/// <summary>The whole judgement of one resource, in the order the store applies it: size, then
/// syntax, version and shape, then density, then expansion, then the DTSTART every VEVENT owes
/// (RFC 5545 § 3.6.1). Null when the file is accepted.</summary>
internal static IcsProblem? CheckAll(string ics, IcsCalendar? parsed) =>
    CheckSize(ics) ?? Check(ics, parsed) ?? CheckDensity(parsed!) ?? CheckExpansion(parsed!)
    ?? CheckStart(parsed!);

/// <summary>Nothing above reads DTSTART: Check judges shape and identity, CheckExpansion answers
/// null when there is no start to walk from. Called last, on a resource already known well-formed.</summary>
internal static IcsProblem? CheckStart(IcsCalendar parsed) =>
    (IcsDocument.MasterOf(parsed) ?? IcsDocument.Components(parsed).First()).DtStart is null
        ? new IcsProblem(IcsPrecondition.ValidCalendarData, NoStart)
        : null;
```

`CalendarEventStore.Parse` l'appelle à la place de ses cinq lignes ; sa constante `NoStart` déménage
vers `IcsGuards` et `CalendarEventStore.NoStart` la relaie (`= IcsGuards.NoStart`), pour que le
message et l'ordre ne changent pas d'un caractère — aucune assertion de 5a ne bouge. `PutAsync`, dans l'ordre : `IcsDocument.TryLoad(ics)` puis
`IcsGuards.CheckAll(ics, parsed)` → statut par précondition (`MaxResourceSize` → `TooLarge`,
`SupportedCalendarData` → `UnsupportedVersion`, `ValidCalendarData` → `InvalidCard`,
`ValidCalendarObjectResource` → `InvalidCard` avec `Precondition` porté,
`SupportedCalendarComponent` → `UnsupportedComponent`, `MaxInstances` → `TooManyInstances` ; le
`NoStart` de `CheckStart` sort en `ValidCalendarData` → `InvalidCard`) —
**`DavWriteOutcome` gagne `IcsPrecondition? Precondition = null`** pour que la traduction XML ne
devine pas. Le paramètre est **en queue et défaillant à `null`** : les constructions du carnet,
produit et tests, ne changent pas d'une virgule, et `IcsPrecondition` passe `public` puisque le
record l'est (spec § 10, déjà alignée).
Puis, **hors transaction** (le `GateAsync` du carnet),
une première lecture de la ligne : `ifMatch` faux → `PreconditionFailed`, `createOnly` sur un nom
pris → `AlreadyExists`, octets identiques (`row.IcsRaw == ics`) → `Replaced` avec l'ETag courant
et **sans rang** ; puis la porte (`store.InTransactionAsync`) :
`rank = sync.NextSequenceAsync(calendarId)` ; la ligne `Calendar` chargée
(`context.Calendars.SingleOrDefault(c => c.Id == calendarId && c.UserId == userId)`, nulle →
`NotFound` : c'est elle que `ApplyIcsAsync` et `ConflictHref` lisent, et un agenda supprimé entre
la résolution du contrôleur et la porte sort ici) ; `row` relu (`CalendarEvents.SingleOrDefault(calendarId, davName)`) ;
`ifMatch` recomparé (`EntityTagMatcher.Match(ifMatch, "\"" + row.IcsHash + "\"")`) →
`PreconditionFailed` ; `createOnly && row is not null` → `AlreadyExists` ; l'UID
(`IcsDocument.MasterOf(parsed)?.Uid ?? Components.First().Uid`) porté par un **autre** `dav_name`
du **même** agenda → `UidConflict` avec `ConflictHref = DavPaths.Event(userId, calendar.DavName, incumbent.DavName)` ;
`row is null && CountAsync >= CalendarEventStore.MaxPerCalendar` → `CollectionFull` ;
archive de l'ancien (`sync.ArchiveAsync(userId, calendarId, row.Id, row.Uid, davName, row.IcsRaw, RevisionCause.Put)`),
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

Contrôleur, `PUT …/{calendarName}/{davName}` : `[RequestSizeLimit(PutBodyBytes)]`, où
`private const int PutBodyBytes = 2 * IcsGuards.MaxIcsBytes` — la constante nommée du carnet
(`CardDavController.PutBodyBytes`), avec son commentaire : au-dessus du plafond annoncé, pour
qu'un corps qui le dépasse soit lu et refusé en `403 max-resource-size` plutôt qu'en `413` de
transport que rien n'annonce ;
`TracedAsync` ; `{davName}` invalide → `403 valid-calendar-data` ; agenda inconnu → 404 ;
`RefusedByPreconditions(etag courant)` → `412` + `ArchiveRejectedAsync` ; corps non décodable
(`ReadBodyAsync` null) → `403 valid-calendar-data` ; `writer.PutAsync(…, createOnly: DemandsCreation(), ifMatch: …)` ;
`AnswerPutOutcomeAsync` (le garde `mustCreate && Replaced` de 4c). `PUT /dav/calendars/{u}/x`
(un nom sans agenda) → `403 calendar-collection-location-ok`. `DELETE` d'un événement : `If-Match`,
`writer.DeleteAsync`, `204`/`404`/`412`.

- [ ] **Step 1 : tests** (rougissent). `DavCalendarWriterTests` (InMemory, `CalendarSyncStore`
      remplacé par `Fixtures/TestCalendarSyncStore`, qui existe depuis 5a et fait pour l'agenda ce
      qu'`InMemorySyncStore` fait pour le carnet — rien à écrire, ne pas en créer un second) :
      création (`Created`, `dav_name` = le nom donné, `ics_raw` identique
      octet pour octet, index projeté), remplacement (`Replaced`, révision `put` de l'ancien,
      rang avancé), octets identiques → pas de rang, `If-Match` faux → `PreconditionFailed`,
      `createOnly` sur un nom pris → `AlreadyExists`, `UidConflict` avec l'`href` complet, même
      UID dans un **autre** agenda accepté, plafond 5000 → `CollectionFull`, `DeleteAsync` pose
      la tombe et archive `delete`, `DeleteAllAsync` par lots avec un rang par lot,
      `ArchiveRejectedAsync` sans rang. `CalDavErrorTests` : les six correspondances.
      `CalDavOutcomeTranslatorTests` : chaque statut → code + élément + `href`. `CalDavPutTests` :
      chaque ligne du tableau § 10 avec le `<D:error>` attendu (corps de deux méga-octets → 413 ;
      `VERSION:1.0` ; `VTODO` seul ; `VTODO` + `VEVENT` ; deux UID ; deux maîtres ;
      **`VEVENT` sans `DTSTART` → 403 `valid-calendar-data`** ; 10 001
      instances par an ; `no-uid-conflict` ; `If-None-Match: *` sur un nom pris → 412 ;
      `If-Match` faux → 412 et révision `rejected` ; `Content-Type: text/plain` accepté ; `201`
      puis `204` avec `ETag` ; `PUT` sous `/dav/calendars/{u}/x` → 403 `location-ok` ; nom `a/b`
      → 403 `valid-calendar-data`). `CalDavDeleteTests` : `204` + tombe visible par
      `TombstonesAsync`, `If-Match` faux → 412, inconnu → 404. `CalDavNoFiveHundredTests` :
      reprendre la stratégie de `CardDavNoFiveHundredTests` (corps aléatoires, en-têtes cassés,
      verbes inconnus) sur les quatre formes agenda ; aucun 500.
- [ ] **Step 2 : implémenter** erreurs → writer → traducteur → contrôleur →
      `ApplicationServicesConfiguration`.
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
  `Services/Dav/MultiStatusWriter.cs` (+ `WriteMixedAsync(href, IReadOnlyList<XName> ok, IReadOnlyList<XName> refused, ct)`
  pour le `PROPPATCH`, + `WriteCreationRefusalAsync(XName root, IReadOnlyList<XName> ok, IReadOnlyList<XName> refused, ct)`
  sans `href` pour `MKCALENDAR`/`MKCOL`),
  `Services/Dav/DavHeaders.cs` (`NoCache = "no-cache"`), tests `CalendarStoreTests`.

**Interfaces:**

```csharp
public sealed record CalendarWrite(string DisplayName, string? Description, string? Color, int? Order, string? TimeZone = null);

public interface ICalendarStore
{
    /// <summary>Décision 2 du cadrage : the client's URL segment becomes dav_name. Colour next of
    /// the palette, rank last, the state row in the same transaction. When timeZone is null: the
    /// zone of `default`, and UTC when the account holds no calendar at all — a hand-restored base
    /// (§ 6), where a MKCALENDAR carries no browser to ask and inventing a zone would be worse.
    /// Failures: CapReached, NameTaken.</summary>
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

**Une seule création, deux façades — et la méthode partagée est à extraire, pas à appeler.**
`CalendarStore.CreateAsync` inline aujourd'hui tout son corps (plafond compté dans la transaction,
couleur validée `BadColour`, couleur suivante de la palette, rang dernier, `Calendars.Add` +
`SaveChangesAsync` + `sync.CreateStateAsync`) et **n'appelle pas** `AddAsync` : `AddAsync` est une
autre privée, du seul `EnsureDefaultAsync`, et les deux se recopient déjà à moitié. T5 extrait donc
du corps de `CreateAsync` la privée `CreateRowAsync(userId, davName, write, fallbackZone, ct)`, et
les deux façades l'appellent — `CreateAsync` avec `davName = id.ToString()` et le fuseau du
navigateur, `CreateNamedAsync` avec le segment de l'URL. `AddAsync` reste à `EnsureDefaultAsync`
(pas de plafond à compter, pas de couleur à choisir). Rien n'est recopié, et la moitié de
duplication que 5a avait laissée se referme au passage.
`CalendarStore` gagne une constante `NameTaken` (« This URL is already taken by another
calendar »), à côté de `CapReached` et `NotDeletable` : c'est l'index unique
`(user_id, dav_name)` qui la lève, comme `EnsureDefaultAsync` lit déjà son gagnant sur
`DbUpdateException`.

Règles de `Parse` (les deux) : couleur — `CalendarStore.Colour` (rendue `internal`) est le seul
juge, `#RRGGBB` ou `#RRGGBBFF` alpha retiré et chiffres repliés en minuscules, `null` ⇒ refusée ;
réécrire la forme ici serait deux vérités sur une même valeur ;
ordre : entier, sinon refusé ; `calendar-timezone` : `IcsDocument.TryLoad` → exactement un
`VTIMEZONE` → `IcsTimeZones.ResolveIana(TzId)` non nul, sinon refusé ; `displayname` :
`Trim()`, vide = absent (création : le segment ; `PROPPATCH` : refusé) ; `description` : brute ;
`supported-calendar-component-set` : chaque `<C:comp name>` doit être `VEVENT`, sinon
`AsksUnsupportedComponent` ; `resourcetype` : absent OK sur `MKCALENDAR` (obligatoire sur
`MKCOL`), présent ⇒ exactement `{D:collection, C:calendar}` sinon `ResourceTypeRefused` ;
`DAV:remove` sur `calendar-description` → `Accepted[description] = null` ; sur `displayname`,
`calendar-timezone` → `Refused`.

Contrôleur, `MKCALENDAR` et `MKCOL` sur `calendars/{userId:guid}/{calendarName}` —
`[AcceptVerbs("MKCALENDAR", "MKCOL")]`, Kestrel acceptant un verbe inconnu sans configuration
comme `PROPFIND` l'a prouvé (spec § 4) :
`TracedAsync` ; nom invalide → `403` nu ; `Parse` (`400` sur XML cassé) ; `ResourceTypeRefused`
→ `403 DAV:valid-resourcetype` ; `TimeZoneRefused` → `403 valid-calendar-data` ;
`AsksUnsupportedComponent` → un `propstat 403` sur `supported-calendar-component-set`, rien créé —
**et ni la racine ni la ligne de statut ne sont celles d'un multistatus**. RFC 4791 § 5.3.1.1 donne
`207` + `CALDAV:mkcalendar-response` au `MKCALENDAR` ; RFC 5689 § 3 donne **`403`** +
`DAV:mkcol-response` au `MKCOL` étendu — un `207` y serait un 2xx, lu « créé » par un client qui
juge sur la classe, et c'est la branche que DAVx⁵ emprunte pour une liste de tâches. Ni l'une ni
l'autre ne porte d'`href` (la ressource n'existe pas). D'où
`MultiStatusWriter.WriteCreationRefusalAsync(response, root, ok, refused, ct)`, qui déduit le
statut de la racine ; `WriteMixedAsync` reste celle du `PROPPATCH`, avec son `href` et son `207`.
C'est ce couple que `ccs-caldavtester` lit en 5d ; `CreateNamedAsync` : `NameTaken` → `405` avec `Allow` de
l'agenda, `CapReached` → `507` ; succès → `201`, `Cache-Control: no-cache`. `MKCALENDAR`/`MKCOL`
sur `calendars/{userId:guid}/{calendarName}/{*davName}` (sous un agenda) →
`403 calendar-collection-location-ok` (la route existe pour répondre ceci, pas `405`). **Sur le
home, en revanche, `405` avec son `Allow`** : le home existe, et RFC 4918 § 9.3.1 réserve `405` à
un `MKCOL` visant une ressource déjà là — c'est mot pour mot la réponse de « nom d'URL déjà pris »,
pour la même raison. `location-ok` garde son sens propre : un emplacement **hors** de l'arbre des
agendas. (Amendement au tableau du cadrage décision 2, qui donnait `403` aux deux.) Sous
`/dav/addressbooks/`, le verbe tombe dans le `405` fourre-tout de `CardDavController` (spec § 11) :
rien à ajouter au carnet.
`PROPPATCH` sur un agenda : `CalendarPropertyUpdate.Parse` ; `UpdateAsync` avec les valeurs
acceptées (les absentes gardent leur valeur : construire `CalendarWrite` depuis le `DavCalendar`
courant) ; `207` mixte par `WriteMixedAsync` ; l'ordre des `propstat` : `200` puis `403`. Aucun
rang, aucun ctag. `DELETE` sur `default` → `writer.DeleteAllAsync` — **et c'est ce câblage qui referme un trou de T4** :
`DeleteAllAsync` ne vérifie aujourd'hui ni l'existence ni l'appartenance de l'agenda (un id étranger
rendrait `204`), n'ayant aucun appelant ; le contrôleur résout l'agenda avant de l'appeler, et un
test l'épingle. Sa boucle par lots recopie celle de `CalendarStore.DeleteAsync` : les deux partagent
désormais la même privée. Sur un autre → `ICalendarStore.DeleteAsync` → `204` ; inconnu → `404` ;
sur le home → `405`.

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
      agenda → `403 location-ok` ; **sur le home → `405` avec `CalendarHomeAllow`** ; le corps
      d'échec du `VTODO` a pour racine `CALDAV:mkcalendar-response` en `MKCALENDAR` et
      `DAV:mkcol-response` en `MKCOL`, sans `href` ; un `MKCALENDAR` sur un compte **sans aucun
      agenda** crée le sien en UTC. `OPTIONS` sur un agenda annonce `MKCALENDAR, MKCOL`
      (`CalDavSurfaceTests` suit). `CalDavProppatchTests` : `207` avec `200`×5 et `403`×1
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
  (+ `internal static IcsCalendar Instance(IcsCalendar parsed, EventOccurrence occurrence, CalendarEvent source)`),
  `Services/Calendar/OccurrenceExpander.cs` (+ `internal static int CapFor(DateTime fromUtc, DateTime toUtc)`,
  extrait du `Cap` privé d'`Expansion`).

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
si le nombre rendu atteint le plafond, lève `max-instances`. Ce plafond est **le même objet**, pas
la même formule réécrite : `Expansion.Cap` est aujourd'hui privé dans une classe privée imbriquée,
il devient `OccurrenceExpander.CapFor(fromUtc, toUtc)` que l'expansion et le rapport lisent tous
deux. Recopier `IcsGuards.MaxInstancesPerYear * Math.Ceiling(jours / 365.2425) + 1` serait le
doublon que la règle interdit, et une instance d'écart entre le seuil qui tronque et celui qui
refuse.

**Le composant source d'une occurrence** est trouvé par son `InstanceId`, jamais deviné :
`EventOccurrence.IsOverride` dit qu'une surcharge la porte, et cette surcharge est celle dont
`IcsDocument.InstanceIdOf(component)` égale `occurrence.InstanceId` parmi
`IcsDocument.Components(parsed)` ; sinon c'est `IcsDocument.MasterOf(parsed)`. C'est le point où
« les surcharges remplacent l'instance qu'elles nomment » se joue : l'expander a déjà fait ce
travail pour l'écran de 5b, et le rapport le relit plutôt que de le refaire. Un `InstanceId` que
plus aucun composant ne porte (le fichier a changé sous l'expansion : impossible, tout est lu
d'un même `parsed`) tomberait sur le maître.

Pour chaque occurrence, `IcsComposer.Instance` clone le composant source
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
      `owner` (`calendar-query` et `free-busy-query` se testent en T7 et T8, pas ici : leurs
      assertions basculeraient). `CalDavSyncCollectionTests` : reprise des scénarios de
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
  `Services/Dav/DavCollation.cs` (+ `Octet = "i;octet"`, un comparateur ordinal, et une
  résolution **par protocole**, voir plus bas — le `Resolve(string?)` actuel reste tel quel pour
  le carnet).

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
    /// <summary>Throws DavPreconditionException(supported-filter | supported-collation |
    /// valid-filter) on the forms § 8 refuses; DavBadRequestException only when the body is not a
    /// filter at all. Open bounds are closed here, against <paramref name="nowUtc"/>.</summary>
    internal static CalendarQuerySpec Parse(XElement filter, DateTime nowUtc);
    /// <summary>Strict yyyyMMdd'T'HHmmss'Z'. RFC 4791 § 9.9: a time-range MUST carry at least one
    /// of start/end — neither of them is a refusal, never a window invented around now. One missing
    /// bound is closed at MaxYears from the other; bothRequired demands the two; end ≤ start is
    /// refused. `refusal` names the precondition to throw (CALDAV:valid-filter inside a filter),
    /// null for a plain DavBadRequestException. FreeBusyReport (T8) reuses it with
    /// bothRequired: true, refusal: null.</summary>
    internal static TimeRangeSpec ParseTimeRange(XElement timeRange, DateTime nowUtc, bool bothRequired,
        XName? refusal);
    internal static EventColumnFilter Columns(CalendarQuerySpec spec);   // STATUS/TRANSP/CLASS equals sans negate → colonne
    internal static bool Matches(IcsCalendar parsed, CalendarQuerySpec spec, string calendarTimeZone);
}

internal static class CalendarQueryReport
{
    internal static Task<int> WriteAsync(HttpResponse response, XDocument body, string requestHref,
        DavCalendar calendar, DavEvent? single, IDavCalendarReader reader, ulong upTo, EventMemberSource source,
        TimeProvider clock, CancellationToken ct);
}

/// <summary>What one protocol accepts in a `collation` attribute, and what it answers otherwise.
/// CardDAV (RFC 6352 § 8.3.1): ascii + unicode, unicode by default, CARDDAV:supported-collation.
/// CalDAV (RFC 4791 § 7.5): ascii + octet, ascii by default, CALDAV:supported-collation.</summary>
internal sealed record DavCollationSet(
    IReadOnlyDictionary<string, DavCollationComparer> Accepted, DavCollationComparer Default, XName Refusal)
{
    // Ascii and Unicode are DavCollation's two comparers, private today, made internal for these
    // two lines — one comparer per collation, never a second instance.
    internal static readonly DavCollationSet CardDav = new(…, DavCollation.Unicode, DavXml.CardDav + "supported-collation");
    internal static readonly DavCollationSet CalDav = new(…, DavCollation.Ascii, DavXml.CalDav + "supported-collation");
}

internal static class DavCollation
{
    internal const string Octet = "i;octet";
    /// <summary>Today's method, body unchanged: `Resolve(attribute, DavCollationSet.CardDav)`.</summary>
    internal static DavCollationComparer Resolve(string? attribute);
    /// <summary>An absent attribute and the literal `default` (RFC 4790 § 3.1, which today's
    /// method already honours) both answer <c>set.Default</c> — ascii on a calendar, unicode on a
    /// book. Anything the set does not hold throws its own Refusal.</summary>
    internal static DavCollationComparer Resolve(string? attribute, DavCollationSet set);
}
```

**Pourquoi une résolution par protocole et non un `Octet` ajouté au `Resolve` commun.**
`DavCollation.Resolve` rend `i;unicode-casemap` par défaut, refuse `i;octet` et lève le XName
**CardDAV** ; trois tests existants l'affirment (`DavCollationTests.cs:40`,
`AddressBookFilterTests.cs:144`, `CardDavQueryTests.cs:229` : `i;octet` → `403 supported-collation`).
Un `Octet` accepté dans le `Resolve` commun retournerait ces trois assertions — une valeur attendue
qui change, interdite par les contraintes globales — et donnerait aux agendas le mauvais défaut et
le mauvais espace de noms. Le jeu de l'agenda annonce exactement ce que `supported-collation-set`
publie (T3) ; `i;unicode-casemap` y répond `403 CALDAV:supported-collation` (spec § 8).
`CalendarQueryFilter.Parse` construit ses `TextMatchSpec` avec `Resolve(attr, DavCollationSet.CalDav)` ;
`AddressBookFilter` ne change pas.

`Parse` : bornes — `end` absent ⇒ `start + 5 ans` ; `start` absent ⇒ `end − 5 ans` ; **les deux
absents ⇒ refus**, RFC 4791 § 9.9 exigeant qu'un `time-range` en porte au moins une (fabriquer une
fenêtre autour de maintenant répondrait faux sans le dire) ; `end ≤ start` ⇒ refus. Dans un
`calendar-query` ces refus sont `403 CALDAV:valid-filter` — la précondition que le § 9.7 définit
exactement pour un filtre hors forme, et que `CalDavError.ValidFilter` porte déjà ; un `400` nu
laisserait le client deviner lequel de son filtre ou de son corps est en cause. Le format accepté
est `yyyyMMdd'T'HHmmss'Z'` strict, une autre forme étant le même refus. `prop-filter`/`param-filter`/`text-match` réutilisent
`PropFilterSpec`, `ParamFilterSpec`, `TextMatchSpec` de `Services/Dav` avec les `XName` CalDAV ;
`collation` : `DavCollationSet.CalDav` — `i;ascii-casemap` (défaut), `i;octet` ; autre, `i;unicode-casemap`
compris ⇒ `403 CALDAV:supported-collation`. `Matches` :
`TimeRange` → `OccurrenceExpander.Overlaps` ; chaque `PropFilterSpec` sur **un** composant au
moins (`IcsDocument.Components`) : la propriété par son nom via `component.Properties[name]`
(Ical.Net expose `Properties` — valeurs sérialisées par `property.Value?.ToString()` ; pour un
`CalDateTime`, `IcsDocument.LiteralOf`), paramètres via `property.Parameters` ; `AlarmFilters` :
`IsNotDefined` ⇒ aucune alarme dans aucun composant ; `TimeRange` ⇒ `OccurrenceExpander.AlarmFires`
(pour chaque instance dans `[from − 1 j, to + 1 j[`, chaque `Alarm` : déclenchement = `Trigger.DateTime`
absolu, ou `instance.Start`/`instance.End` (`Related`) + `Trigger.Duration` ; vrai si dans `[from, to[`).

**La marge d'un jour et les alarmes.** `CandidatesAsync` élargit d'un jour (T3) et `AlarmFires`
déroule sur `[from − 1 j, to + 1 j[` : un `TRIGGER:-PT15M` ou `-PT1H` tombe dedans, un
`TRIGGER:-P1W` — que Thunderbird propose — non. Un `VALARM time-range` d'une semaine avant
l'instance rend donc un résultat incomplet plutôt que faux : c'est la borne assumée, écrite dans
`calendar-5c-residuals.md` (T8) et non élargie ici, parce qu'une marge d'une semaine sur la
présélection ferait relire tout l'agenda à chaque `calendar-query` d'iOS, qui en émet un par
ouverture d'écran.

`WriteAsync` : `single` non nul ⇒ candidats = `[single]` ; sinon `reader.CandidatesAsync(calendar.Id,
spec.TimeRange?.FromUtc, spec.TimeRange?.ToUtc, Columns(spec), upTo)` ; pour chaque candidat
`IcsDocument.TryLoad(IcsRaw)` (nul ⇒ ignoré et journalisé `Warning`), `Matches` ⇒ `source.Resolve`
+ `WriteResourceAsync` ; troncature à `MultigetReport.MaxHrefs` réponses ⇒ `WriteTruncatedAsync`.
Le tout dans `InOneSnapshotAsync` (état lu d'abord, `upTo` = `state.Seq`).

- [ ] **Step 1 : tests** (rougissent). `CalendarQueryFilterTests` : chaque ligne de la grille
      de refus § 8 ; une borne ouverte fermée à cinq ans de l'autre ; **les deux absentes →
      `403 CALDAV:valid-filter`** (jamais une fenêtre autour de maintenant) ; `end ≤ start` et une
      date hors forme → le même `valid-filter` ; `Matches` sur : série hebdomadaire dont
      seule la troisième instance chevauche ; journée entière flottante jugée dans
      `Pacific/Auckland` contre `Europe/Brussels` (résultat différent) ; `EXDATE` retirant
      l'unique instance de la fenêtre ⇒ faux ; surcharge déplacée **dans** la fenêtre ⇒ vrai ;
      `VALARM` `TRIGGER:-PT15M` ; `TRIGGER;RELATED=END` ; `TRIGGER;VALUE=DATE-TIME` ;
      `prop-filter SUMMARY text-match` `i;octet` sensible à la casse et `i;ascii-casemap`
      insensible ; sans attribut `collation` ⇒ `ascii` (le défaut CalDAV, pas celui du carnet) ;
      `i;unicode-casemap` ⇒ `403 CALDAV:supported-collation` ; `DavCollationTests` gagne le jeu
      `CalDav` sans toucher aux cas `CardDav` ; `negate-condition` ; `param-filter ATTENDEE PARTSTAT` ; `is-not-defined`
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
- Modify : `Controllers/CalDavController.cs` ; `SyncStateConsistencyCheck.cs` et son
  `SyncStateConsistencyCheckHostedService.cs`, **déplacés de `Services/CardDav/` vers
  `Services/Dav/`** en même temps qu'ils gagnent la passe agenda : un contrôle qui compare les
  deux compteurs n'est plus du carnet, et le laisser sous `Services/CardDav` contredirait la
  décision 1 (déplacement mécanique, `git mv` + espace de noms, dans le même commit) ;
  `SyncStateConsistencyCheckTests.cs` suit sous `Services/Dav/` ;
  `Services/Calendar/OccurrenceExpander.cs`
  (commentaire `RANGE=THISANDFUTURE`), `docs/superpowers/carddav-restore-prerequisite.md`,
  `docs/superpowers/webmail-calendar-tables.md` (`/caldav` → `/dav/calendars/` dans le prérequis
  d'atomicité),
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

`WriteAsync` : `time-range` obligatoire, lu par
`CalendarQueryFilter.ParseTimeRange(el, now, bothRequired: true, refusal: null)`
(`400` si une borne manque ou si `end ≤ start` : RFC 4791 § 7.10 ne définit pas de précondition
pour ce rapport, donc pas de `valid-filter` ici), candidats par `CandidatesAsync(from, to, None)`, `OccurrenceExpander.Expand` par candidat, une
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
un `REPORT` adressé à une carte ou un événement inexistant qui répond `404` là où 4c rendait un
`207` — plus juste, mais c'est un changement du carnet que la suite ne couvrait pas ; la lecture
d'état de synchro que `ContextOrNotFoundAsync` fait uniformément et dont seul `expand-property` se
sert (un SELECT par clé primaire, non supprimé parce que le remède remettrait « quel verbe
sers-je » dans le crochet qu'on vient d'en sortir) ; les trois actions de forme d'URL qui
échappent à `SwitchedOn()` — `GetCollection` et les fourre-tout `405`, sans lecture ni fuite ; les
adresses du principal construites aussi sur un `PROPFIND Depth: 0` de `/dav/principals/`, où aucune
table ne les sert (un appel au fournisseur pour rien, invisible avec `ClaimsAccountInfoProvider`) ;
les deux branches mortes de `CardDavOutcomeTranslator` qui nomment `UnsupportedComponent` et
`TooManyInstances` — `DavContactWriter` n'en produit aucun, mais l'exhaustivité qu'exigent
`NoTwoStatuses` et `EveryEnumValue_IsHandled` laisse une arête `Services/CardDav` →
`Services/CalDav` ; le rollback du rang sur un refus décidé dans la porte, dont le provider
InMemory n'atteste que la consultation du prédicat ; une couleur ou un rang hors forme **ignorés**
à la création d'un agenda là où un `PROPPATCH` les refuse (aucun client visé n'y envoie de texte
libre, tous sortent d'un sélecteur) ; le `catch (DbUpdateException) → NameTaken` que seule une base
réelle exerce, l'InMemory n'appliquant pas l'index unique ; la branche `403 valid-calendar-data` du
`MKCALENDAR`, filet aujourd'hui inatteignable ; le `403 max-instances` d'un membre écrit **dans**
le multistatus plutôt qu'en refus global (le `207` est ouvert avant que le résolveur ne tourne, et
le juger plus tôt supposerait de pré-expanser les cinq mille membres) ; la `calendar-data` servie
dans un `sync-collection` là où 4c décision 8 rendait un `404` propstat pour `address-data` —
RFC 6578 § 3.2 ne restreint aucune propriété, sabre la sert, et le § 6 avait déjà tranché en la
mettant dans la table ; la marge d'un jour du marcheur, qui consomme le plafond sans entrer dans le
compte du rapport — une série à la densité de la porte peut perdre jusqu'à un jour d'instances sans
refus, dans une bande de quelques dixièmes de pour-cent ; l'ordre « jeton invalide avant `Prepare` »
qu'aucun test n'épingle, aucun `Prepare` de `sync-collection` ne pouvant plus lever ; `match-type`
honoré alors que RFC 4791 § 9.7.5 ne le définit pas (sabre l'ignore ; l'ignorer servirait un
`contains` à qui demande un `equals`, et le refuser comme `test="anyof"` n'a pas lieu d'être — il
s'évalue exactement), avec pour corollaire que la présélection par colonnes du § 8 ne se déclenche
pour aucun client conforme ; un `param-filter` négatif qui correspond sur un paramètre absent, là où
sabre dit non et où le RFC est muet ; une ressource non récurrente à plusieurs « cette occurrence
seulement » présélectionnée sur son premier composant,
`calendar-data` servie aussi dans un `PROPFIND` alors que RFC 4791 § 9.6 la réserve aux REPORT —
divergence héritée de 4c, qui sert `address-data` de même, et qu'un client ne peut que trouver
généreuse ; `calendar-user-address-set` annoncée alors qu'elle est définie par RFC 6638, dont la
tranche ne sert rien d'autre : c'est par elle qu'un client se reconnaît participant, et l'absence
d'`calendar-auto-schedule` dans l'en-tête `DAV:` dit assez qu'on n'ordonnance pas,
`limit-*` ignorés, la marge d'un jour qui borne le `time-range` d'un `VALARM` à un déclencheur
d'au plus 24 h, l'absence de cache d'`AccountInfo` sur le `calendar-user-address-set` du
principal — un appel plateforme par cycle de synchro et par appareil, à mesurer en 5d —, les
mineurs relevés par les revues), ce que les tests n'ont pas couvert (**l'atomicité de la bascule
CalDAV et de la création de `default` : le provider InMemory n'a pas de transactions, la
vérification est manuelle sur `snoopy_webmail_dev`**), ce
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

**Ce que ce plan corrige de la spec.** Les points où la spec ne tenait pas au contact du code
sont amendés dans les deux documents : `DavWriteOutcome` change bien (§ 10) ; la liste des
fichiers du socle (§ 1) est complète à 35 ; le partage de transaction du § 13 exige que
`CalendarStore.InTransactionAsync` devienne réentrant, faute de quoi il lève sur MariaDB tout en
passant en InMemory, **et que `DavCredentialStore.EnableAsync` ouvre la transaction qu'il n'a pas
du tout aujourd'hui** ; `DavResource` prend `CollectionName` en queue (§ 2) ; `IDavCalendarReader`
porte `CountAsync` (§ 7), que le plafond du `PUT` compte. S'y ajoutent la marge d'un jour de la
présélection (§ 7), que le webmail applique déjà et pour la raison que son propre code écrit ;
les **cinq** gardes du `PUT` (T4) — `IcsGuards.Check` ne jugeant ni la densité, ni l'expansibilité,
ni le `DTSTART` que `Parse` réclame à part et sans lequel un `VEVENT` sans début entrerait,
invisible ; et la résolution des collations par protocole (T7), le `Resolve` commun ayant le défaut
et le XName du carnet.

**Ce que le plan corrige de lui-même, après relecture contre le code et les RFC.** Cinq points qui
ne compilaient pas ou ne tenaient pas : les crochets de `DavControllerBase` passent
`private protected`, un `protected` d'une classe publique n'admettant pas un paramètre `internal` ;
les quatre attributs de route et de politique vivent sur la base **seule**, MVC les lisant avec
`inherit: true` ; `DavResourceContext.Addresses` par défaut à `null` et non `[]` (CS1736) ;
`IDavMemberSource` se scinde en `Prepare` puis `Resolve`, pour qu'un `address-data` hors forme
sorte encore en `403` avant l'ouverture du `207` et non dedans ; et la privée que les deux façades
de création devaient partager (T5) est à **extraire**, `CreateAsync` inlinant son corps
aujourd'hui. Trois écarts RFC sont refermés : la racine du corps d'échec d'un `MKCALENDAR`/`MKCOL`
(`CALDAV:mkcalendar-response` / `DAV:mkcol-response`, RFC 4791 § 5.3.1 et RFC 5689 § 3), le
`time-range` sans aucune borne (refusé, RFC 4791 § 9.9) et son refus nommé `valid-filter` plutôt
que `400`, et le `Allow` de la forme agenda, qui annonce désormais les deux verbes de création
qu'elle sert (RFC 9110 § 15.5.6) tandis que le home les refuse en `405` et non en `403`.

**Couverture de la spec.** § 1 → T1 ; § 2, 3, 4, 13 → T2 ; § 5, 6, 7 (lecteur, `PROPFIND`,
`GET`) → T3 ; § 10, 12 → T4 ; § 11 → T5 ; § 7 (rapports, `calendar-data`) → T6 ; § 8 → T7 ;
§ 9, 14 et docs → T8. `allprop` sans `calendar-data` : T3. `Content-Type` jamais consulté : T4.
`is_visible` jamais projeté : T3 (`ListAsync` ne filtre pas). Les deux `InTransactionAsync`
laissés : T4 utilise `store.InTransactionAsync` comme le carnet utilise celui de `ContactStore`.

**Cohérence des types.** Tout membre ajouté à un record du socle l'est **en queue, avec un
défaut** : `DavResourceContext(Kind, UserId, PrincipalAddress, Card, State, CollectionName = null,
IReadOnlyList<string>? Addresses = null, Calendar = null, Event = null, CardDavEnabled = true,
CalDavEnabled = true)` — `null` et non `[]`, une collection expression n'étant pas une constante de
compilation (CS1736), la table lisant `Addresses ?? []` ; déclaré en T1, rempli en T2/T3 ; `DavResource(Kind, UserId, DavName, CollectionName = null)` —
les trois premiers gardent leur position, donc `DavPathsTests` ne change que par ajout de cas ;
`DavWriteOutcome(Status, Etag, ConflictHref, Sequence, Precondition = null)` — ajouté en T4, nul
côté carnet. `IDavMemberSource<T>.Resolve` prend `XDocument body` : `CardMemberSource` (T1) et
`EventMemberSource` (T6) le respectent. `CalendarWrite` avec `TimeZone` (T5) reste compatible avec
le `PUT` webmail (`null`). `TimeRangeSpec` et `CalendarQueryFilter.ParseTimeRange` (T7) sont
réutilisés par `FreeBusyReport` (T8). `IDavCredentialStore.EnableAsync` avec `alongside` (T2) est
le seul endroit où la bascule et `EnsureDefaultAsync` partagent une transaction ; aucun contrôleur
n'en ouvre, et `CalendarStore.InTransactionAsync` devient réentrant pour que ce partage tienne
ailleurs qu'en InMemory.

**Accessibilités, une fois pour toutes.** Passent `public` : `ICalendarSyncStore` (T2, dont
`SyncState`, `PruneOutcome` et `RevisionCause` le sont déjà — rien d'autre à ouvrir),
`IcsPrecondition` (T4), et naissent `public` `IDavCalendarReader` (T3), `IDavCalendarWriter` (T4)
— tout ce qu'un contrôleur, qui ne peut qu'être `public`, prend en paramètre. `ICalendarStore`
l'est déjà (`CalendarsController` l'injecte), donc `CreateNamedAsync` et `CalendarWrite` ne coûtent
rien. Restent `internal` : `DavCalendarReader`, `DavCalendarWriter`, `CalendarEventStore`,
`CalendarStore`, tout `Services/CalDav` et tout `Services/Dav` — et c'est **`private protected`**,
pas `public`, qui laisse `DavControllerBase` les manipuler dans sa propre surface (contraintes
globales) : la base est publique, sa surface ne l'est pas.

**Câblage.** `ApplicationServicesConfiguration` est modifié en T3 (le lecteur) et en T4
(l'écrivain). `DavTestServer` enregistre les siens, plus un `MutableTimeProvider` (T3) que son
`HostBuilder` nu n'a pas : sans ces lignes, la suite reste verte et l'hôte réel répond `500`, ou
l'inverse pour l'horloge. Le store de synchro en mémoire de l'agenda existe (`Fixtures/TestCalendarSyncStore`),
il n'en naît pas un second.

**Sans espace réservé.** Chaque étape nomme ses fichiers, ses signatures et ses cas de test ;
le corps des méthodes extraites en T1 est celui de 4c, non recopié ici par choix — la règle
d'extraction (« garde son corps et son commentaire ») est l'instruction.
