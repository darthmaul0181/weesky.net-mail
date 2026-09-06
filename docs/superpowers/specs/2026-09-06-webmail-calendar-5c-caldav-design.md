# Agenda 5c — le serveur CalDAV

**Tranche 5c du projet Agenda 5.** Le cadrage (`2026-09-04-webmail-calendar-5-overview-design.md`)
a fixé, dans ses décisions 2, 6 et 8 et sa § Paramètres, ce que le serveur CalDAV annonce, accepte
et refuse ; 5a a posé les tables, le moteur Ical.Net et les stores ; 5b a livré l'écran. Cette
tranche ouvre `/dav/calendars/` aux téléphones et à Thunderbird. Ce document ne répète pas le
cadrage : il le cite, tranche ce qu'il laissait ouvert, et fixe l'architecture qui le réalise —
en premier lieu la façon dont le code CardDAV de 4c devient un socle commun aux deux protocoles.

## Où en est le projet

Le serveur CardDAV de 4c tient dans un contrôleur de 946 lignes (`CardDavController`), 45 fichiers
sous `Services/CardDav`, un schéma d'authentification `Basic` (`Authentication/CardDav`), un lecteur
et un écrivain DAV (`DavContactReader`, `DavContactWriter`) et une quinzaine de classes de tests
sur un hôte réellement routé (`DavTestServer`). 4d l'a passé sous `ccs-caldavtester`, Thunderbird
et DAVx⁵.

Côté agenda, 5a a livré ce que le protocole va lire et écrire : `calendar_events.ics_raw`
souverain, `ics_hash` (base de l'ETag), `dav_name` unique par agenda, `calendar_sync_state` et
`calendar_tombstones` **par agenda**, `calendar_revisions` par utilisateur ; `IcsGuards` juge un
fichier avec les six préconditions du RFC 4791 § 5.3.2.1 (`IcsPrecondition`) ; `OccurrenceExpander`
déroule une série sur une fenêtre ; `CalendarEventStore.ApplyIcsAsync` est le seul endroit où une
ressource et son index s'écrivent. Rien de tout cela ne parle encore HTTP brut.

`calendar-5a-residuals.md` (§ À traiter en 5c) et `calendar-5b-residuals.md` (§ Ce dont 5c hérite)
listent ce que les deux tranches adressent à celle-ci ; chaque point est repris ici, à l'endroit
où il se tranche.

## Ce que fait la tranche

À la fin de 5c, un téléphone ou Thunderbird appairé avec le secret de synchronisation de 4c voit
les agendas de l'utilisateur, les lit, y écrit, en crée, en supprime, et le webmail voit chaque
écriture dans la seconde — sans qu'un seul test CardDAV ait changé de sens. Concrètement :

- le socle WebDAV de 4c est extrait de `CardDavController` et partagé (décision 1) ;
- `/dav/calendars/{userId}/{agenda}/` répond à `PROPFIND`, `PROPPATCH`, `REPORT`, `MKCALENDAR`,
  `MKCOL`, `DELETE` sur les collections ; `GET`/`HEAD`/`PUT`/`DELETE` sur les événements ;
- cinq rapports : `calendar-multiget`, `calendar-query` (avec `time-range`, `expand`,
  `text-match`), `sync-collection`, `expand-property`, `free-busy-query` ;
- `/.well-known/caldav`, le principal qui annonce le home d'agendas et les adresses de
  l'utilisateur, l'en-tête `DAV:` complété ;
- la colonne `caldav_enabled`, née à 0, et le second interrupteur de l'onglet Sync ;
- les refus du `PUT` en XML WebDAV, code par code.

Les clients réels et `ccs-caldavtester` sont 5d ; rien ici n'est joué contre un téléphone.

## Décisions

### 1. Un socle WebDAV commun, trois contrôleurs minces

**Le problème.** `CardDavController` mêle deux choses : ce qui est du carnet (vCard, `me-card`,
`addressbook-query`) et ce qui est du WebDAV (lire un corps XML sans DTD, juger `Depth`, écrire un
`207` au fil de l'eau, refuser en `403` avec une précondition nommée, journaliser une ligne par
requête, répondre `405` avec `Allow`). Recopier la seconde moitié pour CalDAV donnerait deux
endroits où corriger le prochain bogue DAV ; l'écrire dans le même fichier donnerait 1 500 lignes
où `addressbook-query` et `calendar-query` se croisent.

**Ce qu'on fait.** Ce qui ne parle ni vCard ni carnet quitte `Services/CardDav` pour un espace de
noms `Services/Dav` : `DavBadRequestException`, `DavBody`, `DavCollation`, `DavCollationComparer`,
`DavDepth`, `DavError`, `DavHeaders`, `DavLimit`, `DavName`, `DavPaths`, `DavPreconditionException`,
`DavPropertyRequest`, `DavPropertyUpdate`, `DavRequestLog`, `DavResource`, `DavResourceKind`,
`DavSyncToken` (et ses `SyncTokenKind`/`SyncTokenRead`), `DavXml`, `DavXmlReader`,
`EntityTagMatcher`, `MultiStatusWriter`, `NoFormBindingAttribute`, `ReportRequest`,
`XmlWriterExtensions`, `ExpandPropertyReport`, les spécifications de filtre génériques
(`TextMatchKind`, `TextMatchSpec`, `ParamFilterSpec`, `PropFilterSpec`). Restent sous
`Services/CardDav` : `AddressBookFilter` et sa `Spec`, `AddressDataFilter` et sa `Request`,
`AddressBookQueryReport`, `VCardVersionConverter`, `DavProperties` (renommé `CardDavProperties`),
`DavOutcomeTranslator` (renommé `CardDavOutcomeTranslator`), `SyncStateConsistencyCheck`. Le
déplacement est **un commit à part, sans une ligne de logique** : `git log --follow` garde
l'histoire, et un relecteur sait que rien n'a changé que le chemin.

**Deux rapports deviennent génériques.** `MultigetReport` et `SyncCollectionReport` ne savent rien
d'une carte : l'un trouve des membres par nom et en écrit les propriétés, l'autre fusionne des
membres changés et des tombes par rang, tronque sur une frontière de rang et frappe un jeton. Ce
qu'ils prennent aujourd'hui en paramètre (`IDavContactReader`, `DavProperties.Resolve`) devient une
**source de membres** :

```csharp
internal interface IDavMemberSource<TMember>
{
    Task<IReadOnlyList<TMember>> FindManyAsync(IReadOnlyList<string> davNames, CancellationToken ct);
    IAsyncEnumerable<TMember> ChangedAsync(ulong after, ulong upTo, CancellationToken ct);
    Task<IReadOnlyList<DavTombstone>> TombstonesAsync(ulong after, ulong upTo, CancellationToken ct);
    string HrefOf(TMember member);
    string DavNameOf(TMember member);
    ulong RankOf(TMember member);
    (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, TMember member);
}
```

`DavTombstone(string DavName, ulong Rank)` est le seul type commun ; `ContactTombstone` et
`CalendarTombstone` s'y projettent. Le carnet fournit `CardMemberSource` (sur `IDavContactReader`,
lié à un utilisateur) ; l'agenda fournit `EventMemberSource` (sur `IDavCalendarReader`, lié à un
agenda). Le corps des deux rapports ne bouge pas d'une ligne de logique : seuls les appels
changent de cible. La lecture de l'état de synchro (`ReadStateAsync`) et la transaction-instantané
restent à l'appelant, parce que la clé diffère (`user_id` d'un côté, `calendar_id` de l'autre).

**Un contrôleur de base porte la trame.** `DavControllerBase` (`Controllers/Dav/`) reçoit de
`CardDavController` : `TracedAsync` (propriété du `{userId}`, barre finale canonique, ligne de
journal), `PropfindAsync` (profondeur, corps, `207` en instantané), `ProppatchAsync`,
`ReportAsync` (le squelette : lecture du corps, `trace.Report`, la table des rapports laissée
**abstraite**), `RefuseAsync`, `BodyRefused`, `ReadBodyAsync`, `RefusedByPreconditions`,
`DemandsCreation`, `HeaderOrNull`, `Capabilities`, `MethodNotAllowed`, `Trace`. Trois choses restent
abstraites, parce qu'elles sont le protocole : la résolution des propriétés d'une ressource, la
liste des enfants d'une collection en `Depth: 1`, et le tableau des rapports servis par genre de
ressource.

Trois contrôleurs dessus, chacun sous `[Route("dav")]`, la même politique nommée, le même
`[ApiExplorerSettings(IgnoreApi = true)]` et le même `[NoFormBinding]` :

| Contrôleur | Routes | Ce qu'il sert |
|---|---|---|
| `DavPrincipalController` | `/`, `/dav/`, `/dav/principals/`, `/dav/principals/{userId}/` | `current-user-principal`, le principal avec ses **deux** home-sets, `expand-property`, `OPTIONS`, `405` |
| `CardDavController` | `/dav/addressbooks/…` | ce qu'il sert aujourd'hui, moins ce qui a migré |
| `CalDavController` | `/dav/calendars/…` | tout ce que ce document décrit |

Le principal quitte `CardDavController` parce qu'il appartient aux deux : `addressbook-home-set` et
`calendar-home-set` sortent de la même réponse, chacun conditionné par son interrupteur (§ 3).
ASP.NET Core route par gabarit, donc trois contrôleurs sous `dav` ne se disputent aucune URL tant
que leurs gabarits sont disjoints — et ils le sont, par leur second segment.

**Ce que le refactor doit prouver.** La suite CardDAV — les quinze classes `CardDav*Tests`,
`DavContactReaderTests`, `DavContactWriterTests`, `Services/CardDav/*Tests` — reste verte, et les
seules modifications qu'elle subit sont des renommages mécaniques (espace de noms, nom de classe,
valeur d'énumération). Une assertion qui change de valeur attendue est une régression, pas un
renommage. 5d rejouera `ccs-caldavtester` côté CardDAV pour la même raison.

**Le schéma d'authentification suit le mouvement.** `Authentication/CardDav` devient
`Authentication/Dav` : `DavAuthenticationHandler`, `DavAuthenticationDefaults` (le nom de la
politique et du schéma changent de constante, pas de valeur, pour que la configuration
`appsettings` et les journaux existants ne bougent pas), `DavAuthenticationOptions`,
`DavAuthenticationCache`, `AuthAttemptThrottle`. Le renommage est dans le même commit mécanique.

### 2. Les chemins : un parseur pour deux arbres, un segment d'agenda variable

`DavPaths.Parse` est câblé en littéral sur `principals`/`addressbooks`/`default` (résidu 5a).
Il rend désormais `DavResource(Kind, UserId, CollectionName, DavName)` et l'énumération gagne
quatre genres :

| Genre | Chemin | Note |
|---|---|---|
| `ServiceRoot` | `/dav/` | inchangé |
| `PrincipalCollection` | `/dav/principals/` | inchangé |
| `Principal` | `/dav/principals/{userId}/` | inchangé |
| `AddressBookCollection` | `/dav/addressbooks/` | ex-`BookCollection` |
| `AddressBookHome` | `/dav/addressbooks/{userId}/` | ex-`Home` |
| `AddressBook` | `/dav/addressbooks/{userId}/default/` | ex-`Collection` |
| `Card` | `/dav/addressbooks/{userId}/default/{nom}` | inchangé |
| `CalendarCollection` | `/dav/calendars/` | neuf — la collection qui contient les homes |
| `CalendarHome` | `/dav/calendars/{userId}/` | neuf |
| `Calendar` | `/dav/calendars/{userId}/{agenda}/` | neuf ; `CollectionName` = le segment décodé |
| `Event` | `/dav/calendars/{userId}/{agenda}/{nom}` | neuf |

Les renommages `Home`/`Collection`/`BookCollection` sont assumés : un genre nommé `Home` dans un
arbre qui en a deux ment. C'est un renommage mécanique au sens de la décision 1.

**Le segment d'agenda est un nom DAV.** Il est décodé **une seule fois** depuis le chemin brut
(`RawTarget`), comme le nom d'une carte, puis jugé par `DavName.IsValid` (non vide, 255
caractères, pas de `/`, `\`, caractère de contrôle, espace de bord, `.` ni `..`). Un segment
invalide ne désigne rien en lecture (`404`) ; en écriture il est refusé par une réponse
réfléchie et non par un `404` de routage, la règle de 4c décision 5 : `403 CALDAV:valid-calendar-data`
sur un `PUT` (le jumeau de `valid-address-data`), `403` nu sur un `MKCALENDAR`/`MKCOL` — le RFC 4918
§ 9.3.1 réserve `403` à « la création n'est pas permise à cet endroit », sans précondition, et
`calendar-collection-location-ok` est réservé à un emplacement **hors** du home. Dans tout `href`
écrit, le segment est ré-encodé par `Uri.EscapeDataString`, comme `dav_name`. `default` est un
segment comme un autre pour le parseur ; c'est le store qui sait qu'il ne se supprime pas.

`DavPaths` gagne `CalendarCollection`, `CalendarHome(userId)`, `Calendar(userId, name)`,
`Event(userId, calendarName, davName)`. La borne `MaxPathLength` passe de 2560 à **4864** : deux
segments de 255 caractères à neuf octets chacun, plus le préfixe.

**Les gabarits de route.** `CalDavController` déclare
`calendars/{userId:guid}/{calendarName}` et `calendars/{userId:guid}/{calendarName}/{*davName}`.
ASP.NET Core décode les valeurs de route ; le contrôleur ne les repasse jamais au parseur, il les
juge avec `DavName.IsValid` directement. Un `{calendarName}` invalide répond `404` avant toute
lecture, comme un nom de carte invalide aujourd'hui.

### 3. L'identité porte deux drapeaux, et c'est la ressource qui juge

`DavIdentity(Guid UserId, bool CardDavEnabled)` devient
`DavIdentity(Guid UserId, bool CardDavEnabled, bool CalDavEnabled)`. Le schéma d'authentification
ne refuse en `403` que si **les deux** sont à 0 — toujours après la comparaison du condensat, pour
la raison de 4c décision 2 (un `403` avant le condensat est un oracle d'énumération). Le cache
d'authentification porte les deux ; `DavCredentialsController` l'oublie à chaque bascule, comme
aujourd'hui.

Puis chaque contrôleur applique le sien, **avant toute lecture** : `CardDavController` refuse en
`403` (sans corps, comme 4c) quand `CardDavEnabled` est faux ; `CalDavController` quand
`CalDavEnabled` l'est. `DavPrincipalController` répond dès qu'un des deux est allumé, et son
principal ne rend `addressbook-home-set` que si CardDAV l'est, `calendar-home-set` que si CalDAV
l'est — l'autre sort en `propstat 404`. DAVx⁵ lit un home-set absent comme « pas de ce service
ici » et ne cherche pas plus loin ; c'est ce qui permet d'éteindre l'agenda sans que les contacts
cessent de se synchroniser.

`OPTIONS` reste anonyme et répond sur toute URL, allumé ou non.

### 4. La découverte et l'en-tête `DAV:`

`WellKnownController` gagne `[Route(".well-known/caldav")]` sur la **même** action : toute
méthode, anonyme, `301` vers `/dav/`, `Cache-Control: max-age=86400`.

`DavHeaders.ComplianceClasses` devient `1, 3, addressbook, calendar-access, extended-mkcol`, servi
partout sous `/dav` — sur `OPTIONS` et, comme 4c le fait exprès, sur les réponses `PROPFIND` et les
écritures acceptées. Un seul en-tête pour les deux arbres : sabre fait de même, et un client qui
lit `calendar-access` sur un carnet n'en déduit rien. `extended-mkcol` est un MUST de RFC 5689
§ 3.1 dès qu'on sert le `MKCOL` étendu, et c'est sur lui que `ccs-caldavtester` décide si la
fonctionnalité existe. **Ni `calendar-auto-schedule` ni `calendar-schedule`** (cadrage,
décision 8).

Les listes `Allow`, depuis le cadrage, dans `DavHeaders` :

```
racine, principal, home de contacts   OPTIONS, PROPFIND, PROPPATCH, REPORT
carnet                                OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT
fiche                                 OPTIONS, HEAD, GET, PUT, DELETE, PROPFIND, PROPPATCH, REPORT
/dav/calendars/                       OPTIONS, PROPFIND, PROPPATCH, REPORT
home d'agendas                        OPTIONS, PROPFIND, PROPPATCH, REPORT, MKCALENDAR, MKCOL
agenda                                OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT
événement                             OPTIONS, HEAD, GET, PUT, DELETE, PROPFIND, PROPPATCH, REPORT
```

Tout verbe absent répond `405` avec l'en-tête, par un `[Route]` fourre-tout par forme, comme 4c.
`MKCALENDAR` est routé par `[AcceptVerbs("MKCALENDAR")]` ; Kestrel accepte un verbe inconnu sans
configuration, `PROPFIND` l'a déjà prouvé.

### 5. Le principal : deux homes et toutes les adresses

À la table du principal de 4c s'ajoutent :

| Propriété | Valeur |
|---|---|
| `CALDAV:calendar-home-set` | `href` de `/dav/calendars/{userId}/`, **seulement** si CalDAV est allumé (§ 3) |
| `CALDAV:calendar-user-address-set` | un `href` `mailto:` par adresse, la principale en tête |
| `CALDAV:schedule-inbox-URL`, `schedule-outbox-URL`, `schedule-default-calendar-URL` | absents, `propstat 404` — pas d'ordonnancement |

**Les adresses.** Dans l'ordre : l'adresse principale du compte (`AccountInfo.Mailbox`), les
adresses du même nom sur les autres domaines du compte (ce que l'onglet Identité affiche sous
« Other domains », via `AliasExtensions.ToAddresses`), puis les identités d'envoi
(`ISendingIdentityStore.GetAllAsync`, colonne `address`), sans doublon, comparées en minuscules.
Le cadrage dit pourquoi les alias y sont : c'est par là qu'un client reconnaît le participant
qu'il est, et une invitation reçue sur un alias absent ferait de l'utilisateur un étranger dans
ses propres invitations. `DavResourceContext` gagne `IReadOnlyList<string> Addresses`, lue **une
fois** par requête sur le principal, jamais par propriété.

`principal-collection-set`, `alternate-URI-set`, `group-membership`, `supported-report-set`
(`expand-property`) : inchangés.

### 6. Le home et l'agenda : ce qu'un `PROPFIND` rend

**`/dav/calendars/`** est une collection intermédiaire, comme `/dav/addressbooks/` : en `Depth: 1`
elle rend le seul home de ce compte. `displayname` : `Calendar Homes`.

**Le home** (`/dav/calendars/{userId}/`), `Depth: 0` : `resourcetype` `collection`, `displayname`
`Calendars`, `current-user-principal`, `supported-report-set` (`expand-property`). `Depth: 1` :
le home, puis **un `response` par agenda**, tous les agendas de l'utilisateur, masqués compris —
`is_visible` est une case d'écran, jamais projetée (cadrage, décision 2) —, dans l'ordre de
`sort_order`. Si l'utilisateur n'a aucun agenda (une base restaurée à la main, cadrage
décision 6), la liste est vide : on n'invente pas `default` dans un fuseau deviné. Les états de
synchro des agendas listés sont lus **avant** les agendas, dans le même instantané, pour la
raison que 4c donne au ctag : un compteur lu après la liste couvrirait une écriture que la liste
n'a pas.

**L'agenda** (`/dav/calendars/{userId}/{agenda}/`), la table complète, depuis le cadrage
décision 8 :

| Propriété | Source |
|---|---|
| `DAV:resourcetype` | `collection` + `CALDAV:calendar` |
| `DAV:displayname` | `display_name` |
| `CALDAV:calendar-description` | `description` (vide autorisé) |
| `apple:calendar-color` | `color` en `#RRGGBB` — espace `http://apple.com/ns/ical/` |
| `apple:calendar-order` | `sort_order` |
| `CALDAV:calendar-timezone` | un `VCALENDAR` contenant le seul `VTIMEZONE` de `time_zone`, sérialisé par `IcsTimeZones.Emit` (borne basse : le 1ᵉʳ janvier de l'année courante moins un an) |
| `CALDAV:supported-calendar-component-set` | `<comp name="VEVENT"/>` seul |
| `CALDAV:supported-calendar-data` | `<calendar-data content-type="text/calendar" version="2.0"/>` |
| `CALDAV:supported-collation-set` | `i;ascii-casemap`, `i;octet` |
| `CALDAV:max-resource-size` | `IcsGuards.MaxIcsBytes` (1 048 576), la constante et jamais un littéral |
| `CALDAV:max-instances` | `IcsGuards.MaxInstancesPerYear` (10 000) |
| `calendarserver:getctag` | `DavSyncToken.Ctag(état de l'agenda)` |
| `DAV:sync-token` | `DavSyncToken.Token(état de l'agenda)` |
| `DAV:supported-report-set` | `calendar-multiget`, `calendar-query`, `free-busy-query`, `sync-collection`, `expand-property` |
| `DAV:current-user-privilege-set` | les sept privilèges de 4c, tous |
| `DAV:owner` | `href` du principal |
| `DAV:current-user-principal` | idem |
| `CALDAV:min-date-time`, `max-date-time` | **absents**, `propstat 404` (cadrage) |

`Depth: 1` sur un agenda : la collection puis un `response` par événement, en flux, bornés par le
compteur lu d'abord (`MemberBound`), dans une transaction-instantané — la mécanique de 4c, avec
`calendar_id` pour clé.

**L'événement** (`…/{agenda}/{nom}`) :

| Propriété | Source |
|---|---|
| `DAV:getetag` | `"ics_hash"` |
| `DAV:getcontenttype` | `text/calendar; charset=utf-8; component=VEVENT` |
| `DAV:getcontentlength` | octets UTF-8 de `ics_raw` |
| `DAV:getlastmodified` | `updated_at` en HTTP-date |
| `DAV:resourcetype` | vide |
| `DAV:current-user-privilege-set` | les sept |
| `DAV:supported-report-set` | `calendar-multiget`, `calendar-query` |
| `CALDAV:calendar-data` | `ics_raw` verbatim — servie dans `PROPFIND` aussi, comme `address-data` l'est en 4c |

`allprop` verse tout sauf `sync-token` et `current-user-privilege-set` (règle de 4c), et **sauf
`calendar-data`** : RFC 4791 § 9.6 en fait une propriété qui ne s'obtient qu'en la nommant.

**Une table par genre, dans `CalDavProperties`**, avec le même mécanisme que `CardDavProperties`
(`Set`, `Resolve`, `Asked`). Les deux classes partagent, dans `Services/Dav/DavPropertyTables`, ce
qui n'a pas de protocole : `Href`, `PrivilegeSet`, `ReportSet`, `HttpDate`, `EntityTag`,
`CurrentUserPrincipal`, `PrincipalUrl`, `IntermediateCollection`. Le principal, lui, est résolu par
`DavPrincipalController` avec une table qui vit dans `Services/Dav/DavPrincipalProperties`.

### 7. Le lecteur d'agendas et les rapports de lecture

`IDavCalendarReader` (`Repositories/`), jumeau d'`IDavContactReader` clé par agenda :

```csharp
public interface IDavCalendarReader
{
    Task<IReadOnlyList<DavCalendar>> ListAsync(Guid userId, CancellationToken ct);
    Task<DavCalendar?> FindCalendarAsync(Guid userId, string calendarName, CancellationToken ct);
    IAsyncEnumerable<DavEvent> StreamAsync(Guid calendarId, ulong upTo, CancellationToken ct);
    Task<DavEvent?> FindAsync(Guid calendarId, string davName, CancellationToken ct);
    Task<IReadOnlyList<DavEvent>> FindManyAsync(Guid calendarId, IReadOnlyList<string> davNames, CancellationToken ct);
    IAsyncEnumerable<DavEvent> ChangedAsync(Guid calendarId, ulong after, ulong upTo, CancellationToken ct);
    Task<IReadOnlyList<CalendarTombstone>> TombstonesAsync(Guid calendarId, ulong after, ulong upTo, CancellationToken ct);
    IAsyncEnumerable<DavEvent> CandidatesAsync(Guid calendarId, DateTime? fromUtc, DateTime? toUtc, EventColumnFilter columns, ulong upTo, CancellationToken ct);
}
```

`DavCalendar(Guid Id, Guid UserId, string DavName, string DisplayName, string Description,
string Color, int Order, string TimeZone)` et `DavEvent(Guid EventId, Guid CalendarId, string
DavName, string Uid, string IcsRaw, string IcsHash, DateTime UpdatedAt, ulong SyncSequence)`. Un
agenda d'un autre utilisateur, un événement d'un autre agenda : `null`, le même `404`.

`CandidatesAsync` est la présélection du `calendar-query` : `first_occurrence < toUtc AND
last_occurrence > fromUtc` (une borne nulle est ouverte), plus `EventColumnFilter(string? Status,
string? Transparency, string? Class)` — trois égalités optionnelles sur les colonnes que le
cadrage décision 1 a posées pour ça. Ce n'est qu'une présélection : chaque candidat est ensuite
jugé sur son fichier (§ 8).

**`calendar-multiget`** : `MultigetReport` générique sur `EventMemberSource`. Bornes de 4c
décision 15 : 1 Mo de corps (`413`), 5000 `href` (`507 number-of-matches-within-limits`). Un `href`
qui désigne un autre agenda, un autre utilisateur ou un nom inconnu : `404` dans le multistatus.
Servi sur l'agenda et sur un événement (scopé à lui).

**`sync-collection`** : `SyncCollectionReport` générique, l'état lu sur `calendar_sync_state` de
l'agenda, les tombes de `calendar_tombstones` ; jeton, ctag, filigrane, troncature, `403
valid-sync-token` — tout de 4c décisions 7 et 8, par agenda. Servi sur l'agenda seul.

**`expand-property`** : servi sur la racine, le principal, le home et l'agenda ; il résout les
`href` de `owner`, `current-user-principal`, `calendar-home-set`, `principal-URL`. Générique déjà,
il ne change que de tables.

**`calendar-data` dans une réponse.** Trois formes, RFC 4791 § 9.6 :

- nue : `ics_raw` verbatim ;
- avec `<C:expand start end/>` : un `VEVENT` par instance de la fenêtre, chacun avec son
  `RECURRENCE-ID` (**en UTC**, forme `Z`) et son `DTSTART`/`DTEND` en UTC, sans `RRULE`, `RDATE`,
  `EXDATE` ni `VTIMEZONE` ; un `VALARM` du maître est recopié dans chaque instance ; les
  surcharges (`RECURRENCE-ID` existants) remplacent l'instance qu'elles nomment. Une journée
  entière sort en `DATE`, son `RECURRENCE-ID` aussi. Le plafond de 10 000 instances par année de
  fenêtre s'applique ; au-delà, `403 max-instances`, jamais une réponse tronquée en silence. C'est
  `OccurrenceExpander` qui déroule et `IcsComposer` qui écrit chaque instance ; rien n'est
  recalculé à la main ;
- avec `<C:comp>`/`<C:prop>` (récupération partielle, § 9.6.1) : **non servie**, la ressource
  entière sort. Aucun des clients visés ne la demande (DAVx⁵, Thunderbird, iOS, Etar lisent le
  fichier entier) ; sabre la sert, Radicale non. Rendre plus que demandé n'est pas une erreur pour
  un client, et la moitié du travail de découpe d'un fichier iCalendar tient dans les
  `VTIMEZONE` à garder ou non — un travail que 5d ne mesurerait qu'avec `ccs-caldavtester`, dont
  les suites concernées sont éteintes dans les `<features>` à cet effet ;
- `limit-recurrence-set` et `limit-freebusy-set` : ignorés (cadrage, décision 8).

### 8. `calendar-query` : le filtre est évalué, ou refusé — jamais ignoré

La règle de 4c décision 11, mot pour mot : un filtre qu'on ne sait pas évaluer répond `403
supported-filter`, pas un résultat qui fait semblant.

**La forme acceptée.** Un `<C:filter>` contenant exactement un `<C:comp-filter name="VCALENDAR">`,
contenant zéro ou un `<C:comp-filter name="VEVENT">` (zéro = tout l'agenda), et dans celui-ci, dans
l'ordre du RFC § 9.7.1 : soit `<C:is-not-defined/>` (la collection n'ayant que des `VEVENT`, la
réponse est vide), soit un `<C:time-range>` optionnel puis des `<C:prop-filter>` et des
`<C:comp-filter name="VALARM">` (ceux-ci avec `is-not-defined` ou `time-range`). Tout est
conjonctif (« ET »). Un `comp-filter` d'un autre nom que `VEVENT`/`VALARM`, un `comp-filter`
`VEVENT` sous un autre `VEVENT`, un `test="anyof"` sur `filter` (RFC 4791 ne le définit pas ;
c'est un attribut CardDAV) : `403 supported-filter`.

**Le `time-range` porte sur les occurrences** (RFC § 9.9) : un événement correspond dès qu'**une**
instance chevauche `[start, end[`. Présélection par colonnes (§ 7), puis `OccurrenceExpander.Expand`
sur le candidat, dans le fuseau de l'agenda pour ce que le fichier laisse flottant (cadrage,
décision 6), en s'arrêtant à la première instance trouvée. Une borne absente est **fermée à cinq
ans de l'autre** (`OccurrenceExpander.MaxYears`), et si les deux manquent, à cinq ans autour de
maintenant : le RFC permet au serveur de borner, l'API webmail vit déjà avec cette borne, et une
série sans fin se juge alors sur ses cinq prochaines années — ce qu'aucun client n'a de raison de
contester. `start` et `end` sont des `DATE-TIME` UTC ; une autre forme est `400`. Une fenêtre où
`end ≤ start` est `400`.

**Le `time-range` d'un `VALARM`** (Apple l'exerce) : un événement correspond si l'une de ses
instances de la fenêtre étendue porte une alarme dont l'instant de déclenchement tombe dans la
fenêtre. L'instant se calcule depuis le `TRIGGER` du `VALARM` (relatif au début ou à la fin de
l'instance selon `RELATED`, ou absolu) — `Ical.Net` le fournit (`GetOccurrences` des alarmes) ;
la fenêtre d'expansion est élargie d'un jour de chaque côté pour attraper un déclencheur relatif
qui précède l'instance.

**`prop-filter`** sur un `VEVENT` : `name` est celui d'une propriété du composant. Trois formes :
`is-not-defined`, `text-match`, `param-filter` (lui-même `is-not-defined` ou `text-match`). Le
`text-match` prend `collation` (`i;ascii-casemap` par défaut, `i;octet`), `negate-condition`, et
la sémantique « contient » du RFC ; `i;unicode-casemap` n'est pas annoncé sur les agendas
(RFC 4791 § 7.5 n'impose que les deux autres) et répond `403 supported-collation`. Le filtre
s'évalue sur le **modèle objet Ical.Net** du fichier — jamais sur les colonnes, sauf la
présélection de `STATUS`, `TRANSP` et `CLASS` quand le `text-match` est une égalité simple sans
négation, que le fichier confirme ensuite. Un `prop-filter` correspond si **un** composant de la
ressource (maître ou surcharge) y satisfait, ce qui est la lecture de sabre. Les valeurs `DATE-TIME`
se comparent comme texte iCalendar (`20260906T120000Z`), ce qui est aussi ce que le RFC demande.

**Ce que la réponse porte.** Les propriétés demandées, `calendar-data` comprise avec ses trois
formes (§ 7). Un `calendar-query` sur un événement est scopé à lui ; un nom qui n'existe plus est
`404` sur la ressource, pas un multistatus vide.

**La grille de refus**, dans `CalDavQueryReport` :

| Cas | Réponse |
|---|---|
| filtre absent, ou plus d'un `comp-filter` racine, ou racine autre que `VCALENDAR` | `400` |
| `comp-filter` inattendu, `test="anyof"`, `prop-filter` sur un `VCALENDAR` | `403 CALDAV:supported-filter` |
| collation inconnue | `403 CALDAV:supported-collation` |
| `time-range` mal formé | `400` |
| `expand` avec plus de 10 000 instances par année de fenêtre | `403 CALDAV:max-instances` |
| plus de 5000 ressources dans la réponse | troncature : `507 number-of-matches-within-limits` en fin de multistatus, comme 4c |

### 9. `free-busy-query` : un `VFREEBUSY`, pas un multistatus

RFC 4791 § 7.10, servi parce que c'est un MUST (cadrage). Le corps porte un `time-range` avec
`start` et `end` obligatoires (`400` sinon, même règle de cinq ans). La réponse est `200`,
`Content-Type: text/calendar; charset=utf-8`, un `VCALENDAR` avec un seul `VFREEBUSY` : `DTSTAMP`,
`DTSTART`/`DTEND` = la fenêtre, et un `FREEBUSY` par plage, en UTC, chacune avec son `FBTYPE` :

| L'instance | Sort en |
|---|---|
| `TRANSP:TRANSPARENT`, ou `STATUS:CANCELLED` | rien |
| `STATUS:TENTATIVE` | `FBTYPE=BUSY-TENTATIVE` |
| le reste | `FBTYPE=BUSY` |

Une journée entière suit son `TRANSP` comme les autres. Les plages contiguës ou chevauchantes de
même type sont **fusionnées** (§ 7.10 : « the server SHOULD coalesce ») ; deux types différents ne
le sont pas. Les instances viennent d'`OccurrenceExpander` sur les candidats de la fenêtre, dans le
fuseau de l'agenda. Servi sur l'agenda seul ; sur un événement, `403 supported-report`. Il ne
répond qu'à l'utilisateur lui-même sur ses propres agendas ; rien n'est partagé.

### 10. `PUT` : le fichier tel quel, et chaque refus nommé

**Le chemin.** `DavCalendarWriter` (`Repositories/`), jumeau de `DavContactWriter` :

```csharp
public interface IDavCalendarWriter
{
    Task<DavWriteOutcome> PutAsync(Guid userId, Guid calendarId, string davName, string ics,
        CancellationToken ct, bool createOnly = false, string? ifMatch = null);
    Task<DavWriteOutcome> DeleteAsync(Guid userId, Guid calendarId, string davName,
        CancellationToken ct, string? ifMatch = null);
    Task<DavWriteOutcome> DeleteAllAsync(Guid userId, Guid calendarId, CancellationToken ct);
    Task<bool> ArchiveRejectedAsync(Guid userId, Guid calendarId, string davName, string ics,
        CancellationToken ct);
}
```

`PutAsync` : `IcsGuards.CheckSize` avant tout, puis `IcsDocument.TryLoad`, puis `IcsGuards.Check`,
puis la porte transactionnelle : rang par `ICalendarSyncStore.NextSequenceAsync(calendarId)` en
premier, relecture de la ligne sous le verrou, `If-Match` recomparé, `createOnly` rejugé, le
porteur de l'UID cherché **dans le même agenda** (`no-uid-conflict` avec l'`href`), le plafond
`CalendarStore.MaxPerCalendar` (5000) compté dans la transaction, archivage de l'ancien fichier
(`put`), `CalendarEventStore.ApplyIcsAsync` (qui projette et hache), tombe levée. **Le fichier est
stocké verbatim** : pas de `VTIMEZONE` ajouté, pas de `DTSTAMP` réécrit, pas d'UID inséré — un
client écrit toujours l'UID, et la ligne d'`ApplyIcsAsync` qui donne un `dav_name` par défaut ne
joue pas, le nom venant de l'URL. Corollaire du verbatim : **l'ETag est toujours renvoyé**, à
l'inverse de 4c décision 9, parce que les octets stockés sont les octets envoyés.

`DavWriteStatus` gagne ce que le carnet n'avait pas : `UnsupportedComponent`, `TooManyInstances`,
et `InvalidCard` se lit désormais comme « fichier invalide » quel que soit le protocole — la
traduction en XML est par protocole (§ 12), l'énumération commune. `DavWriteOutcome` ne change pas.

**La grille**, depuis le cadrage décision 8, avec le code et sa cause dans `IcsGuards` :

| Cas | Réponse |
|---|---|
| plus d'un mébioctet | `403 CALDAV:max-resource-size` |
| pas de l'iCalendar, un UID ou une adresse trop longs, une récurrence que le moteur ne déroule pas | `403 CALDAV:valid-calendar-data` |
| `VERSION` autre que `2.0` | `403 CALDAV:supported-calendar-data` |
| un `VTODO`/`VJOURNAL`/`VFREEBUSY` seul | `403 CALDAV:supported-calendar-component` |
| aucun `VEVENT`, un `VTODO` à côté d'un `VEVENT`, deux UID, deux maîtres, un composant sans UID | `403 CALDAV:valid-calendar-object-resource` |
| plus de 10 000 instances dans l'année qui suit `DTSTART` | `403 CALDAV:max-instances` |
| l'UID existe sous un autre nom **du même agenda** | `403 CALDAV:no-uid-conflict` + `href` |
| le même UID sous le même nom, l'UID qui change sous son nom | accepté, comme 4c aujourd'hui |
| l'agenda plein (5000) | `507 Insufficient Storage` |
| l'agenda n'existe pas | `404` |
| le nom est invalide (§ 2) | `403 CALDAV:valid-calendar-data`, jamais un `404` de routage |
| la cible n'est pas directement dans un agenda (`/dav/calendars/{userId}/x`) | `403 CALDAV:calendar-collection-location-ok` |
| `If-Match` / `If-None-Match` en désaccord | `412`, révision `rejected` |
| `Content-Type` | jamais consulté ; le corps est le seul juge (4c décision 10) |
| corps au-delà de deux fois le plafond | `413` par Kestrel (`PutBodyBytes`), comme 4c |

`201` à la création, `204` au remplacement, `ETag` sur les deux, `DAV:` posé. Une ligne de journal
par requête (`DavRequestLog`), la précondition dans `Condition`.

**Le webmail relit ce qu'un client a écrit** sans rien y changer tant que l'utilisateur ne
l'édite pas ; s'il l'édite, `IcsComposer` fusionne (le verrou `keepRepeat` de 5b) — c'est le
contrat que `calendar-5b-residuals.md` demande de ne pas casser, et une route DAV qui stocke
verbatim ne le touche pas.

### 11. Créer, régler et supprimer un agenda depuis un client

**Deux portes, une création** (cadrage, décision 2). `MKCALENDAR` avec un corps
`<C:mkcalendar><D:set><D:prop>…` ou sans corps ; `MKCOL` avec un corps `<D:mkcol><D:set><D:prop>…`
dont le `resourcetype` déclare `collection` + `calendar`. Les deux lisent les mêmes cinq
propriétés — `displayname`, `calendar-description`, `apple:calendar-color`, `apple:calendar-order`,
`calendar-timezone` — et **ignorent tout le reste** sans le refuser, sauf
`supported-calendar-component-set` qui, s'il nomme autre chose que `VEVENT`, fait échouer la
création (`207` avec un `propstat 403` sur cette seule propriété, rien n'est créé).

`ICalendarStore` gagne :

```csharp
Task<Result<Guid>> CreateNamedAsync(Guid userId, string davName, CalendarWrite write,
    string? timeZone, CancellationToken ct);
```

Le nom DAV est le segment de l'URL ; `write.DisplayName` vaut le segment quand le client n'en
donne pas ; couleur suivante de la palette, rang dernier, fuseau de `default` quand `timeZone`
est nul ; la ligne d'état naît dans la même transaction. Refus : `CalendarStore.CapReached` (vingt
agendas, `507`), `NameTaken` (`405`), `NotDeletable` inchangé. Tous les codes de la table du cadrage
décision 2 sont rendus par `CalDavController` depuis ces `Result` et les vérifications de forme :

| Cas | Réponse |
|---|---|
| succès | `201 Created`, `Cache-Control: no-cache` |
| nom d'URL déjà pris | `405` |
| cible hors du home (`/dav/calendars/{userId}/a/b/`, ou sous `/dav/addressbooks/`) | `403 CALDAV:calendar-collection-location-ok` |
| `supported-calendar-component-set` demande `VTODO`/`VJOURNAL` | `207`, `propstat 403`, rien créé |
| `MKCOL` sans corps, ou `resourcetype` qui ne dit pas `calendar` ; `MKCALENDAR` dont le `resourcetype` dit autre chose que `collection` + `calendar` | `403 DAV:valid-resourcetype` |
| `calendar-timezone` sans `VTIMEZONE` unique, ou dont le `TZID` ne se résout pas | `403 CALDAV:valid-calendar-data` |
| nom d'URL invalide (§ 2) | `403` sans précondition, la validation de 4c décision 5 |
| vingt agendas | `507` |
| CalDAV éteint | `403`, comme tout le reste de l'arbre |

**Le `resourcetype` du `MKCALENDAR`** est optionnel (DAVx⁵ l'écrit, Apple non) ; s'il est là, il
doit dire `collection` + `calendar` et rien d'autre — le `calendarserver:subscribed` d'iCal est
un refus, pas un abonnement.

**`PROPPATCH`** sur un agenda : `207` à statut mixte — `200` sur les cinq propriétés, `403` sur les
autres (`default-alarm-*` d'iOS compris). Les cinq s'écrivent par `ICalendarStore.UpdateAsync`
étendu d'un `TimeZone` optionnel dans `CalendarWrite` (nul = inchangé) ; la couleur accepte
`#RRGGBBFF` et range six chiffres ; le fuseau se lit dans le `TZID` du `VTIMEZONE` reçu, résolu
par `IcsTimeZones.ResolveIana` (les `TZID` Windows d'Outlook compris), et un `TZID` inconnu fait
`403` sur cette propriété seule. Une propriété dont la valeur est invalide (couleur qui n'est pas
`#RRGGBB`, ordre non entier) fait `403` sur elle et **200 sur les autres** — sabre applique
propriété par propriété ; le RFC voudrait tout défaire, mais un client qui a écrit quatre valeurs
justes et une fausse préfère les quatre. `DAV:remove` sur `calendar-description` la vide ; sur
`displayname` ou `calendar-timezone`, `403` (un agenda a toujours un nom et un fuseau). Rien
n'avance ctag ni jeton (cadrage). `PROPPATCH` sur le home, sur `/dav/calendars/` et sur un
événement : tout à `403`, comme 4c décision 16.

**`DELETE`** sur un agenda secondaire : `ICalendarStore.DeleteAsync` (archivage par lots de cent,
tombes et état supprimés), `204`, puis `404`. Sur `default` : `DavCalendarWriter.DeleteAllAsync`,
qui vide l'agenda par lots avec une tombe par événement et répond `204` sans le faire disparaître.
`If-Match` ignoré. Sur le home : `405`.

### 12. Les refus en XML, code par code

`Services/CalDav/CalDavError` porte la table `IcsPrecondition` → `XName` :

| `IcsPrecondition` | Élément |
|---|---|
| `SupportedCalendarData` | `CALDAV:supported-calendar-data` |
| `ValidCalendarData` | `CALDAV:valid-calendar-data` |
| `ValidCalendarObjectResource` | `CALDAV:valid-calendar-object-resource` |
| `SupportedCalendarComponent` | `CALDAV:supported-calendar-component` |
| `MaxResourceSize` | `CALDAV:max-resource-size` |
| `MaxInstances` | `CALDAV:max-instances` |

et, hors énumération : `CALDAV:no-uid-conflict` (avec `href`), `CALDAV:calendar-collection-location-ok`,
`DAV:valid-resourcetype`, `CALDAV:supported-filter`, `CALDAV:supported-collation`,
`CALDAV:valid-filter`, `DAV:number-of-matches-within-limits`, `DAV:valid-sync-token`. `DavXml`
gagne `CalDav = "urn:ietf:params:xml:ns:caldav"` et `Apple = "http://apple.com/ns/ical/"`. Le
`<D:error>` est écrit par le `DavError` existant ; la traduction d'un `DavWriteOutcome` en statut
et élément vit dans `CalDavOutcomeTranslator`, jumeau de celui du carnet, avec les mêmes
`1205`/`1213` MariaDB en `503 Retry-After: 1`.

Le message anglais d'`IcsProblem` n'est pas mis dans le corps XML (le RFC ne le prévoit pas) ; il
va dans la ligne de journal.

### 13. `caldav_enabled` et le second interrupteur

**DDL**, ajouté à `webmail-carddav-tables.md` (§ nouveau « Tranche 5c ») et à rejouer à la main sur
`snoopy_webmail` et `snoopy_webmail_dev` :

```sql
ALTER TABLE `dav_credentials`
  ADD COLUMN `caldav_enabled` TINYINT(1) NOT NULL DEFAULT 0
    COMMENT 'Né à 0 : la ligne existe déjà pour des comptes qui n''ont rien demandé (5c)'
  AFTER `carddav_enabled`;
```

`DavCredential.CalDavEnabled`, `DavCredentialState`/`DavCredentialRecord` gagnent le drapeau ;
`IDavCredentialStore.EnableAsync`/`DisableAsync` prennent un `DavProtocol` (`CardDav`, `CalDav`),
et **le code pose toujours les deux colonnes explicitement** à la création d'une ligne (cadrage
§ Paramètres) : allumer CalDAV en premier crée la ligne avec `carddav_enabled = 0`.

**API.** `PUT /api/DavCredentials/CalDav` avec `{ "enabled": true, "timeZone": "Europe/Brussels" }`
(`DavCalDavToggle`) : à l'allumage, `ICalendarStore.EnsureDefaultAsync(userId, timeZone)` dans la
même transaction que la bascule, `timeZone` obligatoire et validé (`IcsTimeZones.IsKnownIana`,
`400` sinon) ; à l'extinction, `timeZone` ignoré. Le secret est frappé si la ligne n'existait pas,
et rendu une fois, exactement comme `SetCardDav` ; le cache d'authentification et le throttle sont
traités de même. `DavCredentialsView` gagne `CalDavEnabled`. Ligne d'audit
`Audit: caldav_sync user=… enabled=… created=…`.

**Écran.** `SyncPage` gagne un second `ToggleRow` sous le premier : `sync.caldav` = « Calendar
(CalDAV) », `sync.caldavHint` = « Sync your calendars with your phone or Thunderbird. Turning this
off stops every device; your password is kept. » ; en/fr, parité et typographie française testées.
`api.setDavCalDav(enabled)` envoie le fuseau du navigateur
(`Intl.DateTimeFormat().resolvedOptions().timeZone`). L'onglet garde son gate (`capabilities.dav`,
compte principal, `Dav__PublicUrl`). L'adresse affichée ne change pas.

### 14. Ce que la tranche tranche des résidus

| Résidu | Sort |
|---|---|
| `IcsPrecondition` → XML (5a) | § 12 |
| routage `/dav` à généraliser (5a) | § 2 |
| `caldav_enabled` (5a) | § 13 |
| `RANGE=THISANDFUTURE` non appliqué à l'expansion (5a) | **assumé et documenté** : Ical.Net lit le paramètre sans l'appliquer ; sabre ne l'applique pas non plus ; aucun des clients visés ne l'écrit (iOS, DAVx⁵, Thunderbird passent par une coupure de série). Un `PUT` qui en porte un est stocké verbatim et ses instances suivantes sont rendues sans la surcharge. Écrit dans `calendar-5c-residuals.md` et dans le commentaire d'`OccurrenceExpander` |
| `RDATE;VALUE=PERIOD` perdu par `Split` (5a) | reste un résidu **webmail** : le `PUT` DAV ne passe pas par `Split`, et l'expansion lit les périodes correctement |
| `instanceId` orphelin, `Narrowed` sans maître, `DURATION`→`DTEND` (5a) | chemins de l'éditeur webmail, pas du `PUT` DAV ; restent dans `calendar-5a-residuals.md` |
| prédicat `commit` d'`InTransactionAsync` dupliqué (5a) | **laissé** : `DavCalendarWriter` ouvre sa propre transaction explicite comme `DavContactWriter`, et n'a pas de `Result` à juger ; un troisième appelant n'apparaît pas |
| `ifHash` exposé en ETag (5b) | § 6 et § 10 : `"ics_hash"` des deux côtés |
| fenêtre plus large que l'écran (5b) | sans objet côté serveur : le `time-range` est celui du client, et l'expansion a sa propre marge d'un jour |
| `keepRepeat` (5b) | § 10 : le `PUT` DAV stocke verbatim, la fusion n'est que côté éditeur |
| `SyncStateConsistencyCheck` ne connaît que les contacts | il gagne la même comparaison **par agenda** (`MAX(calendar_events.sync_sequence)` contre `calendar_sync_state.seq`) et une entrée d'`assets/calendar-sync-epoch-rotate.sql` pour l'agenda concerné ; `carddav-restore-prerequisite.md` gagne le paragraphe agenda |

## La surface HTTP

```
*         /.well-known/caldav                          301 → /dav/ · anonyme · toute méthode
PROPFIND  /dav/principals/{userId}/                    + calendar-home-set (si allumé), calendar-user-address-set
PROPFIND  /dav/calendars/                              depth 0 et 1 → le home
PROPFIND  /dav/calendars/{userId}/                     depth 0 ; depth 1 → un response par agenda
MKCALENDAR /dav/calendars/{userId}/{agenda}/           201 · 405 · 403 · 507 (décision 11)
MKCOL     /dav/calendars/{userId}/{agenda}/            idem, corps DAV:mkcol obligatoire
PROPFIND  /dav/calendars/{userId}/{agenda}/            depth 0 → la table de la décision 6 ; depth 1 → + un response par événement
PROPPATCH /dav/calendars/{userId}/{agenda}/            207 mixte : 200 sur cinq propriétés, 403 sur le reste
DELETE    /dav/calendars/{userId}/{agenda}/            204 ; default se vide, les autres disparaissent
REPORT    /dav/calendars/{userId}/{agenda}/            calendar-multiget · calendar-query · free-busy-query · sync-collection · expand-property
REPORT    /dav/calendars/{userId}/{agenda}/{nom}       calendar-multiget · calendar-query
GET/HEAD  /dav/calendars/{userId}/{agenda}/{nom}       200, le fichier verbatim, ETag, text/calendar; charset=utf-8; component=VEVENT
PUT       /dav/calendars/{userId}/{agenda}/{nom}       201 / 204 · ETag · If-Match / If-None-Match
DELETE    /dav/calendars/{userId}/{agenda}/{nom}       204 · If-Match · tombe
*         /dav/calendars/{userId}/{agenda} (sans barre) 308 → …/{agenda}/
PROPPATCH /dav/calendars/ · …/{userId}/ · …/{nom}      207, tout à 403
autres verbes                                          405 + Allow
```

Les `href` sont des chemins absolus, jamais des URL ; une collection porte sa barre finale, une
ressource jamais ; le segment d'agenda et le nom sont encodés segment par segment.

## Le schéma

Une colonne (§ 13). Aucune autre table ne change : tout ce que le protocole lit et écrit est déjà
posé par 5a. La procédure d'atomicité du compteur de `webmail-calendar-tables.md`
(§ Prérequis avant d'ouvrir toute route) est à rejouer avant le premier déploiement de 5c, sur
`calendar_id`.

## Fichiers

**Backend — déplacés (commit mécanique)** : les 27 fichiers de la décision 1 de `Services/CardDav`
vers `Services/Dav` ; `Authentication/CardDav` → `Authentication/Dav` avec renommage des classes ;
`Models/Contacts/DavWriteStatus.cs`, `DavWriteOutcome.cs` → `Models/Dav/`.

**Backend — modifiés** : `Controllers/CardDavController.cs` (aminci sur `DavControllerBase`),
`Controllers/WellKnownController.cs`, `Services/Dav/DavPaths.cs`, `DavResourceKind.cs`,
`DavResource.cs`, `DavResourceContext.cs`, `DavHeaders.cs`, `DavXml.cs`, `MultigetReport.cs`,
`SyncCollectionReport.cs`, `ExpandPropertyReport.cs`, `Authentication/Dav/*` (deux drapeaux),
`Data/Preferences/DavCredential.cs`, `Repositories/DavCredentialStore.cs` (+ interface),
`Controllers/DavCredentialsController.cs`, `Models/DavCredentialsView.cs`,
`Repositories/CalendarStore.cs` (+ interface : `CreateNamedAsync`, `UpdateAsync` avec fuseau),
`Models/Calendar/CalendarWrite.cs`, `Services/CardDav/SyncStateConsistencyCheck.cs`,
`Services/Calendar/OccurrenceExpander.cs` (le commentaire `RANGE`, et une entrée « première
instance seulement » pour le `time-range`), `Services/Calendar/IcsComposer.cs` (l'écriture d'une
instance expansée).

**Backend — créés** : `Controllers/Dav/DavControllerBase.cs`, `Controllers/DavPrincipalController.cs`,
`Controllers/CalDavController.cs`, `Services/Dav/IDavMemberSource.cs`, `DavTombstone.cs`,
`DavPropertyTables.cs`, `DavPrincipalProperties.cs`, `Services/CardDav/CardMemberSource.cs`,
`Services/CalDav/CalDavProperties.cs`, `CalDavError.cs`, `CalDavOutcomeTranslator.cs`,
`EventMemberSource.cs`, `CalendarQueryFilter.cs` (+ `CalendarQuerySpec`), `CalendarQueryReport.cs`,
`CalendarDataRequest.cs` (nue / `expand`), `ExpandedCalendarData.cs`, `FreeBusyReport.cs`,
`MkCalendarRequest.cs` (les deux portes, un seul modèle), `CalendarPropertyUpdate.cs`,
`Repositories/IDavCalendarReader.cs`, `DavCalendarReader.cs`, `IDavCalendarWriter.cs`,
`DavCalendarWriter.cs`, `Models/Calendar/DavCalendar.cs`, `DavEvent.cs`, `EventColumnFilter.cs`,
`Models/DavCalDavToggle.cs`, `Models/DavProtocol.cs`, `assets/calendar-sync-epoch-rotate.sql`.

**Frontend** : `src/modules/settings/sync/SyncPage.tsx` (+ test), `src/api.js`,
`src/locales/{en,fr}/settings.json`.

**Docs** : ce fichier ; `docs/superpowers/webmail-carddav-tables.md` (DDL 5c) ;
`docs/superpowers/carddav-restore-prerequisite.md` (paragraphe agenda) ;
`docs/superpowers/calendar-5c-residuals.md` (créé en fin de tranche) ;
`src/frontend/docs/architecture-calendar.md` (une section « ce que CalDAV voit ») ;
les tables de résidus 5a et 5b (les lignes reprises sont marquées « 5c »).

## Tests

**Le refactor** (tâche 1) : la suite CardDAV existante, renommages exclus, **sans autre
modification** ; `dotnet test` complet vert avant et après.

**Chemins et identité** : `DavPathsTests` étendu aux onze genres, au double décodage, aux segments
invalides, à la borne de longueur ; `DavAuthenticationHandlerTests` : `403` si les deux drapeaux
sont à 0, passage sinon ; `CalDavSurfaceTests` : `403` sur `/dav/calendars/` quand CalDAV est
éteint et CardDAV allumé, `200` sur `/dav/addressbooks/` dans le même état, et l'inverse ;
`DavPrincipalTests` : les deux home-sets selon les drapeaux, `calendar-user-address-set` avec
comptes, domaines et identités, sans doublon ; `WellKnownControllerTests` : `/.well-known/caldav`
sur `PROPFIND`.

**Lecture** : `CalDavPropfindTests` (home vide, home à trois agendas dans l'ordre, table de
l'agenda propriété par propriété, `calendar-timezone` parsable avec le bon `TZID`, `allprop` sans
`calendar-data`, `Depth: 1` borné par le compteur, `Depth: infinity` refusé) ;
`CalDavGetTests` (verbatim, `ETag`, `Content-Type` avec `component`, `HEAD`, `404`) ;
`CalDavReportTests` (multiget sur deux agendas, `href` étranger en `404`, `expand-property`,
`507` au-delà de 5000, `403 supported-report` sur un rapport hors forme) ;
`CalDavSyncCollectionTests` (par agenda : un jeton d'un agenda présenté à un autre est refusé ;
tombes ; troncature ; filigrane) ; `CalDavQueryTests` (chaque ligne de la grille de refus ;
`time-range` sur une série dont seule la troisième instance chevauche ; une journée entière
flottante jugée dans le fuseau de l'agenda ; `VALARM` avec `TRIGGER` relatif ; `prop-filter`
`STATUS` par colonne puis confirmé ; `text-match` `i;octet` sensible à la casse ; `expand` avec
surcharge et `RECURRENCE-ID` en UTC ; `expand` au-delà du plafond) ; `FreeBusyReportTests`
(fusion, `BUSY-TENTATIVE`, `TRANSPARENT` exclu, `CANCELLED` exclu, journée entière) ;
`DavCalendarReaderTests` (candidats par colonnes, agenda étranger).

**Écriture** : `CalDavPutTests` (chaque ligne de la grille du § 10 avec son élément XML ;
verbatim byte pour byte ; ETag présent ; `If-None-Match: *` sur un nom pris ; `no-uid-conflict`
avec `href` ; le même UID dans un **autre** agenda accepté ; `507` à 5000 ; `413`) ;
`DavCalendarWriterTests` (rang pris avant toute ligne, tombe levée, archivage `put`, `rejected`,
`Busy`) ; `CalDavDeleteTests` (tombe, `If-Match`, `default` vidé par lots, agenda secondaire
supprimé puis `404`) ; `CalDavMkcalendarTests` (les deux portes ; les cinq propriétés ; défauts ;
`resourcetype` abusif ; `VTODO` demandé ; `405` ; `507` ; `403 location-ok` ; fuseau invalide) ;
`CalDavProppatchTests` (mixte ; couleur `#RRGGBBFF` ; fuseau Windows résolu ; `remove` ; ctag
inchangé). `CalDavNoFiveHundredTests` : le fuzz de 4c sur les verbes et corps d'agenda.

**Paramètres** : `DavCredentialStoreTests` (les deux colonnes posées à la création par chaque
porte), `DavCredentialsControllerTests` (`SetCalDav` crée `default` avec le fuseau ; `400` sans
fuseau ; audit), `SyncPage.test.tsx` (second interrupteur, appel avec fuseau), parité et
typographie des catalogues.

**Consistance** : `SyncStateConsistencyCheckTests` étendu aux agendas.

## Ce que la tranche ne fait pas

Tout ce que le cadrage exclut (§ Ce que le projet ne fait pas), plus :

- pas de récupération partielle de `calendar-data` (`comp`/`prop`), ni `limit-recurrence-set`,
  ni `limit-freebusy-set` — la ressource entière sort (§ 7) ;
- pas d'application de `RANGE=THISANDFUTURE` à l'expansion (§ 14) ;
- pas de `MOVE`, `COPY`, `POST` (pièces jointes), `ACL`, `LOCK` : `405` ;
- pas de `calendar-proxy`, pas de partage, pas de `calendarserver:subscribed` ;
- pas de vérification contre un client réel ni `ccs-caldavtester` : 5d ;
- pas de renommage de la colonne `is_visible` en propriété DAV, ni de `calendarserver:*`
  propriétaires au-delà de `getctag`.

## Risques

- **Le refactor casse une réponse CardDAV en silence.** Le filet est la suite existante, dont
  aucune assertion ne change, et le rejeu de 4d en 5d. Une tâche à part, relue à part.
- **Un `time-range` coûteux.** Une série sans fin est candidate à toute fenêtre (résidu 5a) ; le
  déroulement s'arrête à la première instance qui chevauche, et le plafond d'instances borne le
  pire cas. À mesurer en 5d sur un agenda de 5000 ressources.
- **`expand` et les surcharges hors fenêtre.** Une surcharge qui déplace une instance **dans** la
  fenêtre depuis l'extérieur doit sortir ; c'est `OccurrenceExpander` qui le sait déjà (5a l'a
  posé pour l'écran), donc le rapport ne le recalcule pas.
- **Le double décodage.** Un segment d'agenda décodé par ASP.NET Core puis repassé au parseur
  serait le traversal que 4c a chassé ; la règle est écrite au § 2 et testée.
