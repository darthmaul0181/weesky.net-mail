# Agenda 5d — la conformité clients : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `5d-task-N-…`.

**Spec :** [la conception 5d](../specs/2026-09-07-webmail-calendar-5d-conformance-design.md) —
toute décision citée ici (« décision N ») y renvoie, et elle fait foi en cas de doute.
[La conception 5c](../specs/2026-09-06-webmail-calendar-5c-caldav-design.md) porte ce dont 5d
hérite ; [`calendar-5c-residuals.md`](../calendar-5c-residuals.md) porte les lignes que la campagne
referme.

**Goal :** un serveur CalDAV mesuré par `ccs-caldavtester` puis par deux clients réels, avec les
résidus 5c bon marché refermés avant la mesure, une borne de `time-range` servie ouverte, et un
rapport qui distingue ce qui a été mesuré de ce qui a été contourné.

**Architecture :** rien de neuf côté produit sauf deux gardes de réponse (le composant vide, le
`Cache-Control` des refus), une forme de corps (le refus d'un `MKCOL` étendu en `DAV:mkcol-response`
à `propstat`) et une sémantique de filtre (une borne de `time-range` absente reste absente au lieu
d'être fermée à cinq ans). Tout le reste est de l'outillage — le harnais `tools/caldavtester/` de 4d
gagne un protocole — et des tests : la surface HTTP que 5c a laissée sans assertion, et un rejeu
in-process de la séquence d'appairage des clients Apple, faute d'appareil.

**Tech stack :** .NET 10, ASP.NET Core, EF Core (InMemory en test), Ical.Net 5.2.3, NodaTime TZDB,
xUnit 2.9.3, Moq 4.20.72, `DavTestServer` ; PowerShell 7 et Python 2.7.18 pour le harnais.

## Ce que ce plan suppose fait

5c livrée, poussée et déployée sur dev (branche `caldav`). Rien d'autre : les tâches 1 à 9 sont
exécutables sans Python 2, sans le compte de test et sans appareil.

La partie interactive, elle, en a besoin. **Le compte de test est celui que 4d a créé**, avec CalDAV
allumé en plus de CardDAV, et `tools/caldavtester/serverinfo.local.json` renseigné de son `guid`, de
son `email` et de son secret DAV (décision 2). Il ne sert à rien d'autre, et c'est plus vrai ici que
pour le carnet : les suites créent et suppriment des agendas à leur guise, et un `DELETE` sur un
agenda secondaire ne le vide pas — il le **supprime**. Le second et le troisième utilisateur
(`$userid2:`, `$userid3:`) ne sont **pas** définis : c'est délibéré, et c'est ce qui produit le
bruit multi-utilisateurs nommé dans `suites-caldav.txt`.

## Ce que ce plan ne couvre pas — l'exécution interactive

Les passages de l'outil, le triage, la vague de correctifs qui en sort et les deux clients réels
(spec, « Ordre d'exécution », étapes 1 à 7) se font **en session avec l'utilisateur** : ils
demandent Python 2.7.18, un compte dev dédié, des déploiements et deux appareils. La section « Après
les tâches » ci-dessous les décrit dans l'ordre où ils doivent tomber.

## L'ordre des tâches n'est pas négociable

Un push déploie dev, et la mesure de départ doit porter sur un serveur propre **et** sur la
sémantique de 5c :

| Tâches | Poussées quand | Pourquoi |
|---|---|---|
| 1 à 4 — la tâche zéro (décision 3) | **avant** le passage initial | c'est leur raison d'être : que le chiffre de départ ne mélange pas dette connue et défauts trouvés |
| 5 à 7 — le harnais | jamais déployées | `tools/` ne part pas sur dev |
| 8 — le rejeu Apple | avec la vague, ou après | des tests, aucune réponse ne change |
| 9 — rapport et résidus | à la clôture | des documents |
| **10 — la borne ouverte (décision 11)** | **avec la vague de l'étape 4, jamais avant** | elle change une réponse que le passage initial mesure ; la livrer d'avance rendrait le chiffre de départ inutilisable |

La tâche 10 est écrite ici en entier — elle ne dépend d'aucun verdict (décision 11) — mais elle
**s'exécute à l'étape 4**, pas avant.

## Global constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ;
  `cd src && dotnet build` doit rester à zéro avertissement.
- `src/snoopy.microservice/ApiDocumentation.xml` : le réverter avant chaque commit — `dotnet test`
  le régénère avec des centaines de lignes sans rapport.
- **La suite CardDAV ne change pas de sens.** Le socle `Services/Dav` est partagé : après chaque
  tâche, `dotnet test` complet est vert et **aucune assertion existante ne change de valeur
  attendue**. Une valeur attendue qui bouge est une régression à corriger dans le produit, à une
  exception nommée : la tâche 10 inverse les deux tests de `CalendarQueryFilterTests` qui figent la
  fermeture à `MaxSpan`, et c'est le seul cas.
- Style C# : file-scoped namespaces, un type par fichier, constructeurs primaires, records pour les
  DTO, `sealed`, `internal` par défaut, `CancellationToken` sur tout `async`, `ILogger` structuré.
  Commentaires : jamais pour paraphraser le code ; trois lignes au plus.
- **Aucune réponse de la surface DAV n'est un `500`.** `CalDavNoFiveHundredTests` le garde ; aucune
  tâche ne l'affaiblit.
- **Constantes, jamais de littéraux** : `DavHeaders.NoCache`, `DavHeaders.ComplianceClasses`,
  `OccurrenceExpander.MaxSpan`, `CalendarStore.DefaultDavName`, `CalDavError.*`. Un test qui recopie
  une valeur au lieu de lire la constante est refusé en revue.
- **Le secret n'est jamais journalisé ni versionné** : ni dans le dépôt (`serverinfo.local.json`,
  `serverinfo.xml` ignorés), ni dans `results/` (lignes `Authorization` épurées avant écriture), ni
  dans le rapport. Il est régénéré à la clôture.
- Commits : concis, sujet + corps de deux lignes au plus, jamais commencer ni finir par `@`,
  terminer par `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>` puis
  `Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR`. Écrire le message avec
  `git commit -F -` et un heredoc (outil Bash), jamais un here-string PowerShell.
- Ne jamais pousser sans demander : l'utilisateur pousse lui-même vers `origin/webmail`.

## Valeurs fixées une fois, à ne pas réinventer

| Constante | Valeur |
|---|---|
| Commit épinglé `ccs-caldavtester` | `bed21e5924275552c1561febc8203a9f194cf737` |
| Commit épinglé `ccs-pycalendar` | `a12dd4e1ce8822b022d4abf2cfe6cc93902ff03f` |
| Hôte cible | `api-dev.mail.weesky.net`, port `443`, `--ssl`, `authtype` `basic` |
| Features allumées (CalDAV) | `caldav`, `carddav`, `sync-report`, `well-known`, `current-user-principal`, `expand-property`, `ctag`, `supported-component-sets-one`, `COPY Method`, `MOVE Method` |
| Fichiers retenus | 23, listés dans `suites-caldav.txt`, **un par ligne** |
| Tests `COPY`/`MOVE` qui partent | 39 (16 `copymove` + 5 `errors` + 14 `encodedURIs` + 4 `ctag`) |
| `reports.xml` | 115 tests, **68 joués** avec ces features |
| Échecs Basic par passage | 9 en CalDAV, 1 en CardDAV, **10 en `-Protocol Both`** — plafond `AuthAttemptThrottle` : 10 / 15 min |
| Purge | fait **par défaut** avant chaque passage ; `-NoPurge` seul l'en dispense |
| Borne de cinq ans du moteur | `OccurrenceExpander.MaxSpan` (= `366 × MaxYears` jours), source unique après la tâche 1 |
| `CalendarEventsController.MaxWindow` | **ne change pas** — autre surface (tâche 1, étape 5) |

---

## File Structure

Ce que chaque fichier touché porte, et pourquoi il est touché.

**Produit**

| Fichier | Responsabilité | Tâche |
|---|---|---|
| `src/snoopy.microservice/Models/Dav/SyncState.cs` | le record d'état de synchro, sous le dossier du socle qui l'utilise | 1 |
| `src/snoopy.microservice/Services/Calendar/OccurrenceExpander.cs` | source unique de `Margin` et de la borne de cinq ans ; le plafond d'occurrences borné même sur une fenêtre ouverte | 1, 10 |
| `src/snoopy.microservice/Repositories/CalendarEventStore.cs` | délègue `Margin` et la borne de cinq ans au moteur | 1 |
| `src/snoopy.microservice/Services/CalDav/CalendarPropertyValue.cs` | un `supported-calendar-component-set` sans `comp` ne nomme rien : refus | 2 |
| `src/snoopy.microservice/Controllers/CalDavController.cs` | `Cache-Control: no-cache` sur tous les chemins de création ; le refus `MKCOL` aiguillé vers le bon corps | 2, 3 |
| `src/snoopy.microservice/Services/Dav/MultiStatusWriter.cs` | sait porter une précondition dans le `propstat` d'un refus de création | 3 |
| `src/snoopy.microservice/Services/CalDav/TimeRangeSpec.cs` | une fenêtre dont chaque borne peut être absente | 10 |
| `src/snoopy.microservice/Services/CalDav/CalendarQueryFilter.cs` | décide, par appelant, ce qu'une borne absente devient | 10 |
| `src/snoopy.microservice/Services/CalDav/FreeBusyReport.cs` | exige ses deux bornes, comme avant | 10 |

**Tests**

| Fichier | Ce qu'il fige | Tâche |
|---|---|---|
| `snoopy.microservice.Tests/Controllers/CalDavMkcalendarTests.cs` | le composant vide, le `Cache-Control` de chaque refus, le corps `mkcol-response` | 2, 3 |
| `snoopy.microservice.Tests/Controllers/CalDavSurfaceTests.cs` | les quatre formes HTTP sans test | 4 |
| `snoopy.microservice.Tests/Services/CalDav/CalDavPropertiesTests.cs` | le contenu du `supported-report-set` de l'événement | 4 |
| `snoopy.microservice.Tests/Services/IcsTimeZonesTests.cs` | les quatre alias TZDB que l'outil écrit | 4 |
| `snoopy.microservice.Tests/Controllers/AppleDiscoveryReplayTests.cs` | la séquence d'appairage Apple, corps verbatim | 8 |
| `snoopy.microservice.Tests/Services/CalDav/CalendarQueryFilterTests.cs` | une borne absente reste absente | 10 |
| `snoopy.microservice.Tests/Controllers/CalDavQueryTests.cs` | l'événement à plus de cinq ans sort ; une série sans fin correspond | 10 |

**Harnais et documents**

| Fichier | Responsabilité | Tâche |
|---|---|---|
| `tools/caldavtester/run.ps1` | `-Protocol`, purge par défaut, `-NoPurge`, chemins absolus | 5 |
| `tools/caldavtester/README.md` | mode d'emploi des deux protocoles, l'avertissement `DELETE`, le purge, les copies locales | 5, 7 |
| `tools/caldavtester/serverinfo.template.xml` | substitutions et `<features>` | 6 |
| `tools/caldavtester/suites-caldav.txt` | les 23 fichiers, un par ligne, avec leurs quatre commentaires | 6 |
| `tools/caldavtester/suites-carddav.txt` | `suites.txt` renommé, moins `CardDAV/limits.xml` | 6 |
| `tools/caldavtester/suites/CalDAV/{reports,delete}.xml` | copies amont amputées de leurs préparatifs impossibles | 7 |
| `tools/caldavtester/suites/CalDAV/README.md` + `*.diff` | ce que les copies changent, et rien d'autre | 7 |
| `docs/superpowers/calendar-5d-conformance.md` | le rapport | 9 |
| `docs/superpowers/calendar-5d-residuals.md` | le tri de fin de tranche | 9 |
| `docs/superpowers/calendar-5c-residuals.md` | les lignes que la campagne referme | 9 |

---

### Task 1 : `SyncState` sous `Models/Dav`, et une seule borne de cinq ans

**Files:**
- Move: `src/snoopy.microservice/Models/Contacts/SyncState.cs` →
  `src/snoopy.microservice/Models/Dav/SyncState.cs`
- Modify: les neuf fichiers de production qui l'importent — `Services/Dav/DavResourceContext.cs`,
  `Services/Dav/DavSyncToken.cs`, `Services/Dav/SyncCollectionReport.cs`,
  `Controllers/Dav/DavControllerBase.cs`, `Controllers/CalDavController.cs`,
  `Repositories/CalendarSyncStore.cs`, `Repositories/ICalendarSyncStore.cs`,
  `Repositories/ContactSyncStore.cs`, `Repositories/IContactSyncStore.cs` — plus les fichiers de
  test que le compilateur désignera
- Modify: `src/snoopy.microservice/Services/Calendar/OccurrenceExpander.cs:34`
- Modify: `src/snoopy.microservice/Repositories/CalendarEventStore.cs:67` et `:618`

**Interfaces:**
- Consumes : rien.
- Produces : `weesky.Snoopy.Microservice.Models.Dav.SyncState(Guid Epoch, ulong Seq, ulong
  PrunedBelow)` — le namespace change, la forme non. `OccurrenceExpander.Margin` devient `internal
  static readonly TimeSpan` : c'est désormais la seule déclaration de la marge d'un jour, et
  `CalendarEventStore.Margin` la relaie.

- [ ] **Step 1 : déplacer le fichier et changer son namespace**

```bash
cd src/snoopy.microservice
git mv Models/Contacts/SyncState.cs Models/Dav/SyncState.cs
```

Dans `Models/Dav/SyncState.cs`, remplacer la ligne de namespace :

```csharp
namespace weesky.Snoopy.Microservice.Models.Dav;
```

- [ ] **Step 2 : compiler pour obtenir la liste exacte des importateurs**

Run: `cd src && dotnet build 2>&1 | grep -E "CS0246|CS0234" | sort -u` Expected: FAIL — une erreur
par fichier qui importait `Models.Contacts` pour ce type seul, plus ceux qui l'importaient pour
d'autres types aussi (ceux-là ne bougent pas).

- [ ] **Step 3 : ajouter le `using` manquant fichier par fichier**

Dans chaque fichier désigné, ajouter `using weesky.Snoopy.Microservice.Models.Dav;` — et **retirer**
`using weesky.Snoopy.Microservice.Models.Contacts;` seulement si plus rien d'autre de ce namespace
n'y est employé (un `using` inutile est un avertissement, et la contrainte globale exige zéro
avertissement).

- [ ] **Step 4 : la marge d'un jour n'a plus qu'une déclaration**

Dans `Services/Calendar/OccurrenceExpander.cs`, ligne 34 :

```csharp
    /// <summary>The walker's margin and the preselection's are the one margin: a right answer
    /// depends on their being equal, never on their looking alike.</summary>
    internal static readonly TimeSpan Margin = TimeSpan.FromDays(1);
```

Dans `Repositories/CalendarEventStore.cs`, ligne 67, la déclaration devient un relais — la forme que
`Shift` a déjà :

```csharp
    internal static readonly TimeSpan Margin = OccurrenceExpander.Margin;
```

- [ ] **Step 5 : la borne de cinq ans du moteur n'a plus qu'une écriture**

Dans `Repositories/CalendarEventStore.cs`, ligne 618, remplacer `TimeSpan.FromDays(365 *
OccurrenceExpander.MaxYears)` par `OccurrenceExpander.MaxSpan`.

**Ne pas toucher `Controllers/CalendarEventsController.cs:17`** (`MaxWindow = 365.2425 × MaxYears`).
C'est une troisième écriture, mais d'une autre surface : elle borne la fenêtre de l'API du webmail,
pas une réponse DAV. L'aligner élargirait cette borne de quatre jours et ferait passer
`CalendarEventsControllerTests.Window_RefusesMoreThanFiveYears` de `400` à `200` — vérifié.

- [ ] **Step 6 : la suite complète, qui est la preuve de cette tâche**

Run: `cd src && dotnet test` Expected: PASS, même nombre de tests qu'avant. Aucun test n'est ajouté
: un déplacement de fichier se prouve par la compilation, et une constante relayée par la suite
existante restée verte (spec, « Tests »).

- [ ] **Step 7 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
refactor(dav): SyncState sous Models/Dav, une seule marge et une seule borne

Le socle partage le record ; la marge et l'empan du moteur avaient deux
declarations chacun.
EOF
```

---

### Task 2 : un composant vide ne nomme rien, et tout refus de création porte `no-cache`

**Files:**
- Modify: `src/snoopy.microservice/Services/CalDav/CalendarPropertyValue.cs:61-63`
- Modify: `src/snoopy.microservice/Controllers/CalDavController.cs` (`MakeCalendarAsync`, l'en-tête
  posé une fois pour toutes)
- Test: `snoopy.microservice.Tests/Controllers/CalDavMkcalendarTests.cs`

**Interfaces:**
- Consumes : `CalendarPropertyValue.OnlyEvents(XElement)`,
  `MkCalendarRequest.AsksUnsupportedComponent`, `DavHeaders.NoCache`, les aides du fichier de test
  (`Create`, `Displayname`, `Stored`, `ConditionOf`).
- Produces : `OnlyEvents` rend désormais `false` sur un élément sans aucun `comp`. Aucun autre
  appelant que `MkCalendarRequest` et `CalendarPropertyUpdate` — les deux veulent ce sens-là.

- [ ] **Step 1 : écrire les deux tests qui échouent**

Dans `snoopy.microservice.Tests/Controllers/CalDavMkcalendarTests.cs`, à côté de
`AVtodoComponentSet_AnswersUnderTheVerbsOwnRootAndCreatesNothing` :

```csharp
    [Theory]
    [InlineData("MKCALENDAR", 207)]
    [InlineData("MKCOL", 403)]
    public async Task AComponentSetNamingNothing_IsRefusedLikeAnUnknownOne(string method, int status)
    {
        // Zero comp names no component, so it does not name VEVENT either: All() on an empty
        // sequence said yes, and a calendar serving nothing was created.
        var response = await Create(method, "trips",
            new XElement(DavXml.CalDav + "supported-calendar-component-set"));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "supported-calendar-component-set",
            XDocument.Parse(response.Body).Descendants(DavXml.Prop).Single().Elements().Single().Name);
        Assert.Empty(Stored("trips"));
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task EveryCreationAnswer_CarriesNoCache(string method)
    {
        // RFC 4791 § 5.3.1 Marshalling and RFC 4918 § 9.3 put the header on the response, not on
        // the success: a refusal a proxy caches is a calendar a client cannot create twice.
        var refusedComponent = await Create(method, "trips",
            new XElement(DavXml.CalDav + "supported-calendar-component-set",
                new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VTODO"))));
        var alreadyThere = await Create(method, CalendarStore.DefaultDavName, Displayname("Again"));
        var insideACalendar = await server.SendAsync(
            method, DavPaths.Calendar(UserId, CalendarStore.DefaultDavName) + "nested/");

        Assert.Equal(DavHeaders.NoCache, refusedComponent.Header("Cache-Control"));
        Assert.Equal(DavHeaders.NoCache, alreadyThere.Header("Cache-Control"));
        Assert.Equal(DavHeaders.NoCache, insideACalendar.Header("Cache-Control"));
    }
```

- [ ] **Step 2 : les faire échouer**

Run: `cd src && dotnet test --filter "FullyQualifiedName~CalDavMkcalendarTests"` Expected: FAIL —
`AComponentSetNamingNothing…` reçoit `201` au lieu de `207`/`403` ;
`EveryCreationAnswer_CarriesNoCache` reçoit `null` sur les trois.

- [ ] **Step 3 : refuser un ensemble de composants vide**

`Services/CalDav/CalendarPropertyValue.cs` :

```csharp
    /// <summary>Zero comp names no component at all — not "every component", which is what All()
    /// on an empty sequence would have answered.</summary>
    internal static bool OnlyEvents(XElement componentSet)
    {
        var components = componentSet.Elements(DavXml.CalDav + "comp").ToList();
        return components.Count > 0
            && components.All(component => DavXml.Attribute(component, "name") == "VEVENT");
    }
```

- [ ] **Step 4 : poser `no-cache` une fois, en tête de l'action**

Dans `Controllers/CalDavController.cs`, `MakeCalendarAsync`, en **première** instruction de la
lambda passée à `TracedAsync`, avant le contrôle de `DavName.IsValid` :

```csharp
            // RFC 4791 § 5.3.1 Marshalling and RFC 4918 § 9.3, without condition: the header goes
            // on the response, so it is posed once here rather than on each of the nine exits.
            Response.Headers.CacheControl = DavHeaders.NoCache;
```

et **supprimer** la ligne `Response.Headers.CacheControl = DavHeaders.NoCache;` du chemin `201`, qui
la poserait deux fois.

- [ ] **Step 5 : les faire passer**

Run: `cd src && dotnet test --filter "FullyQualifiedName~CalDavMkcalendarTests"` Expected: PASS, y
compris `ACreationOnAFreeSegment_Answers201NoCache` qui lisait déjà l'en-tête sur le `201`.

- [ ] **Step 6 : la suite complète**

Run: `cd src && dotnet test`

Expected: PASS. `OnlyEvents` n'a qu'un seul appelant, `MkCalendarRequest` :
`supported-calendar-component-set` n'est pas une propriété inscriptible, donc aucun `PROPPATCH` ne
passe par cette garde et `CalDavProppatchTests` ne peut pas bouger.

- [ ] **Step 7 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(caldav): un supported-calendar-component-set vide est refuse

Et le Cache-Control: no-cache de RFC 4791 5.3.1 sur tous les chemins de
sortie d'un MKCALENDAR et d'un MKCOL, pas seulement sur le 201.
EOF
```

---

### Task 3 : le refus d'un `MKCOL` étendu sort en `DAV:mkcol-response` à `propstat`

**Files:**
- Modify: `src/snoopy.microservice/Services/Dav/MultiStatusWriter.cs` (`WriteCreationRefusalAsync`,
  `WritePropstatsAsync`, `WritePropstatAsync`)
- Modify: `src/snoopy.microservice/Controllers/CalDavController.cs` (les deux refus de
  `MakeCalendarAsync`)
- Test: `snoopy.microservice.Tests/Controllers/CalDavMkcalendarTests.cs`

**Interfaces:**
- Consumes : `MultiStatusWriter.WriteCreationRefusalAsync(HttpResponse, XName root,
  IReadOnlyList<XName> ok, IReadOnlyList<XName> refused, CancellationToken)` — la signature
  d'aujourd'hui.
- Produces : la même, avec un paramètre `XName? condition` **avant** le `CancellationToken`. Non
  optionnel : les trois appelants le passent explicitement, y compris celui du composant non
  supporté, qui passe `CalDavError.SupportedCalendarComponent`. Un paramètre optionnel laisserait un
  appelant sur l'ancien comportement sans le dire.

**Ce que le RFC demande.** RFC 5689 § 3 scope tout ce qui suit à l'**extended MKCOL**, celui qui
porte un corps de requête (« The WebDAV MKCOL request is extended to allow the inclusion of a
request body » ; « In all other respects, the behavior of the extended MKCOL request follows that of
the standard MKCOL request »). Un `MKCOL` **sans corps** reste sous RFC 4918 § 9.3 et garde son
`403` + `<D:error>` nu — un `propstat` y nommerait une propriété que le client n'a jamais envoyée.
L'aiguillage porte donc sur « verbe `MKCOL` **et** corps présent », jamais sur le verbe seul. Pour
un corps présent, RFC 5689 § 3 : la réponse à un échec de propriété « MUST be an XML
document containing a single DAV:mkcol-response XML element, which MUST contain DAV:propstat XML
elements » ; l'exemple § 3.5 place `DAV:valid-resourcetype` dans l'`error` de ce `propstat`, et non
dans un `<D:error>` nu. `DavHeaders.ComplianceClasses` annonce `extended-mkcol` : c'est une
non-conformité sous jeton annoncé, et aucun `MKCOL` de l'outil ne porte de corps — elle ne serait
jamais trouvée par la mesure (spec, décision 5). **`MKCALENDAR` ne change pas** : RFC 4791 § 5.3.1
nomme `CALDAV:valid-calendar-data` en précondition et `403` + `<D:error>` est la forme de RFC 4918 §
16.

- [ ] **Step 1 : écrire le test qui échoue**

Dans `CalDavMkcalendarTests.cs` :

```csharp
    [Fact]
    public async Task AMkcolRefusedOnAProperty_AnswersMkcolResponseWithThePreconditionInside()
    {
        // RFC 5689 § 3: a property failure answers a single DAV:mkcol-response holding propstat
        // elements — its § 3.5 example puts DAV:valid-resourcetype inside that propstat's error.
        var body = new XElement(DavXml.Dav + "mkcol",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                new XElement(DavXml.Dav + "resourcetype", new XElement(DavXml.Dav + "collection")))));

        var response = await server.SendAsync("MKCOL", DavPaths.Calendar(UserId, "trips"), body.ToString());

        Assert.Equal(403, response.StatusCode);
        var document = XDocument.Parse(response.Body).Root!;
        Assert.Equal(DavXml.Dav + "mkcol-response", document.Name);
        var propstat = document.Descendants(DavXml.Dav + "propstat").Single();
        Assert.Equal(DavXml.Dav + "resourcetype",
            propstat.Element(DavXml.Prop)!.Elements().Single().Name);
        Assert.Equal("HTTP/1.1 403 Forbidden", propstat.Element(DavXml.Status)!.Value);
        Assert.Equal(DavXml.Dav + "valid-resourcetype",
            propstat.Element(DavXml.Dav + "error")!.Elements().Single().Name);
        Assert.Empty(Stored("trips"));
    }

    [Fact]
    public async Task AMkcolWithAnUnreadableTimezone_AnswersMkcolResponseToo()
    {
        var body = new XElement(DavXml.Dav + "mkcol",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                ResourceType,
                new XElement(DavXml.CalDav + "calendar-timezone", "not an iCalendar object"))));

        var response = await server.SendAsync("MKCOL", DavPaths.Calendar(UserId, "trips"), body.ToString());

        Assert.Equal(403, response.StatusCode);
        var propstat = XDocument.Parse(response.Body).Descendants(DavXml.Dav + "propstat").Single();
        Assert.Equal(DavXml.CalDav + "calendar-timezone",
            propstat.Element(DavXml.Prop)!.Elements().Single().Name);
        Assert.Equal(DavXml.CalDav + "valid-calendar-data",
            propstat.Element(DavXml.Dav + "error")!.Elements().Single().Name);
    }

    [Fact]
    public async Task AMkcalendarWithAnUnreadableTimezone_KeepsItsBareError()
    {
        // RFC 4791 § 5.3.1 names CALDAV:valid-calendar-data as a precondition, and RFC 4918 § 16
        // gives a named precondition this very shape. Only the extended MKCOL moves.
        var body = new XElement(DavXml.CalDav + "mkcalendar",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                new XElement(DavXml.CalDav + "calendar-timezone", "not an iCalendar object"))));

        var response = await server.SendAsync("MKCALENDAR", DavPaths.Calendar(UserId, "trips"), body.ToString());

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "valid-calendar-data", ConditionOf(response));
    }
```

- [ ] **Step 2 : les faire échouer**

Run: `cd src && dotnet test --filter "FullyQualifiedName~CalDavMkcalendarTests"` Expected: FAIL sur
les deux premiers — la racine reçue est `{DAV:}error`, pas `mkcol-response`. Le troisième passe déjà
: il fige ce qui **ne doit pas** bouger.

- [ ] **Step 3 : apprendre la précondition au writer**

Dans `Services/Dav/MultiStatusWriter.cs`, `WritePropstatAsync` prend la condition et l'écrit après
le statut, dans le `propstat` — l'ordre de RFC 4918 § 14.22 (`prop`, `status`, `error?`) :

```csharp
    private async Task WritePropstatAsync(int statusCode, IEnumerable<XElement> properties,
        XName? condition, CancellationToken cancellationToken)
    {
        await writer.WriteStartElementAsync(null, "propstat", DavXml.Dav.NamespaceName).ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "prop", DavXml.Dav.NamespaceName).ConfigureAwait(false);

        foreach (var property in properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteElementAsync(property).ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false); // prop
        await WriteStatusElementAsync(statusCode).ConfigureAwait(false);
        // RFC 4918 § 14.22 orders a propstat prop, status, error: RFC 5689 § 3.5's example puts the
        // precondition here rather than beside the response's own status.
        if (condition is not null) await WriteErrorAsync(condition, null).ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false); // propstat
    }
```

`WritePropstatsAsync` relaie la condition sur le seul propstat qui la porte, celui du refus, et
passe `null` sur les deux autres appels :

```csharp
    private async Task WritePropstatsAsync(IReadOnlyList<XName> ok, IReadOnlyList<XName> refused,
        XName? condition, CancellationToken cancellationToken)
    {
        if (ok.Count > 0)
            await WritePropstatAsync(200, ok.Select(name => new XElement(name)), null, cancellationToken)
                .ConfigureAwait(false);
        if (refused.Count > 0)
            await WritePropstatAsync(403, refused.Select(name => new XElement(name)), condition,
                cancellationToken).ConfigureAwait(false);
        if (ok.Count == 0 && refused.Count == 0)
            await WriteStatusElementAsync(200).ConfigureAwait(false);
    }
```

Les deux autres appelants de `WritePropstatAsync` — dans `WriteResourceAsync` — passent `null`.

Enfin `WriteCreationRefusalAsync` prend le paramètre et le transmet :

```csharp
    internal static async Task WriteCreationRefusalAsync(HttpResponse response, XName root,
        IReadOnlyList<XName> ok, IReadOnlyList<XName> refused, XName? condition,
        CancellationToken cancellationToken)
```

(dernière ligne du corps : `await writer.WritePropstatsAsync(ok, refused, condition,
cancellationToken).ConfigureAwait(false);`)

- [ ] **Step 4 : aiguiller les deux refus du `MKCOL` étendu**

Dans `Controllers/CalDavController.cs`, `MakeCalendarAsync`, remplacer les deux blocs `RefuseAsync`
par un aiguillage sur le verbe :

```csharp
            if (request.ResourceTypeRefused)
            {
                await RefuseCreationAsync(trace, extended, DavXml.Dav + "resourcetype",
                    CalDavError.ValidResourceType, cancellationToken);
                return;
            }

            if (request.TimeZoneRefused)
            {
                await RefuseCreationAsync(trace, extended, CalendarPropertyValue.TimeZone,
                    CalDavError.ValidCalendarData, cancellationToken);
                return;
            }
```

et ajouter l'aide privée, à côté de `MakeCalendarAsync` :

```csharp
    /// <summary>RFC 5689 § 3 wants a mkcol-response holding propstat for a property failure; RFC
    /// 4791 § 5.3.1 leaves MKCALENDAR on RFC 4918 § 16's bare error. One property, two shapes.</summary>
    private async Task RefuseCreationAsync(Trace trace, bool extended, XName property, XName condition,
        CancellationToken cancellationToken)
    {
        if (!extended)
        {
            await RefuseAsync(trace, condition, null, cancellationToken);
            return;
        }

        await MultiStatusWriter.WriteCreationRefusalAsync(Response, MkcolResponse, [], [property],
            condition, cancellationToken);
        trace.Responses = 1;
        trace.Condition = condition.LocalName;
    }
```

Le troisième appelant, celui du composant non supporté, passe désormais sa propre précondition :

```csharp
                await MultiStatusWriter.WriteCreationRefusalAsync(Response,
                    extended ? MkcolResponse : MkcalendarResponse, [],
                    [CalendarPropertyValue.ComponentSet], CalDavError.SupportedCalendarComponent,
                    cancellationToken);
```

Si `CalendarPropertyValue.TimeZone` n'existe pas sous ce nom, employer `DavXml.CalDav +
"calendar-timezone"` — le fichier porte déjà `ComponentSet` sur le même modèle, l'ajouter à côté
plutôt que d'écrire un littéral.

- [ ] **Step 5 : les faire passer**

Run: `cd src && dotnet test --filter "FullyQualifiedName~CalDavMkcalendarTests"` Expected: PASS, les
trois nouveaux et les anciens — dont
`AVtodoComponentSet_AnswersUnderTheVerbsOwnRootAndCreatesNothing`, qui gagne une précondition dans
son `propstat` sans que ses assertions actuelles changent de valeur.

- [ ] **Step 6 : la suite complète — le socle est partagé**

Run: `cd src && dotnet test` Expected: PASS. `WritePropstatAsync` sert aussi les `PROPFIND` et les
`PROPPATCH` des deux protocoles : un seul `propstat` gagne un `error`, et seulement quand un
appelant passe une condition.

- [ ] **Step 7 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(dav): le refus d'un MKCOL etendu sort en mkcol-response a propstat

RFC 5689 3 et son exemple 3.5 ; MKCALENDAR garde le 403 + error nu de
RFC 4791 5.3.1.
EOF
```

---

### Task 4 : les formes que 5c a laissées sans assertion

**Files:**
- Modify: `snoopy.microservice.Tests/Controllers/CalDavSurfaceTests.cs`
- Modify: `snoopy.microservice.Tests/Services/CalDav/CalDavPropertiesTests.cs`
- Modify: `snoopy.microservice.Tests/Services/IcsTimeZonesTests.cs`

**Interfaces:**
- Consumes : `DavTestServer.SendAsync`, `DavTestServer.PropfindAsync`,
  `DavPaths.CalendarCollection`, `DavPaths.CalendarHome(Guid)`, `DavPaths.Calendar(Guid, string)`,
  `DavPaths.Event(Guid, string, string)`, `DavHeaders.EventAllow`,
  `IcsTimeZones.ResolveIana(string)`.
- Produces : rien pour les autres tâches. Aucune ligne de produit ne change : si l'un de ces tests
  rougit, c'est un défaut à corriger, et il entre dans le triage de l'étape 3 avec son verdict.

**Pourquoi maintenant.** Ces quatre formes sont dans la surface que la campagne va marteler. Un
échec de l'outil sur une forme qu'aucun test in-process ne couvre coûte une demi-journée de
bissection ; couverte, il se lit d'un coup d'œil.

- [ ] **Step 1 : les quatre formes HTTP**

Dans `CalDavSurfaceTests.cs` :

```csharp
    [Fact]
    public async Task PropfindDepthZero_OnTheCollectionOfHomes_AnswersTheCollectionItself()
    {
        // The suite only ever walks this shape at Depth: 1. A client that asks for the collection
        // alone must get the collection alone.
        var response = await server.PropfindAsync(DavPaths.CalendarCollection, "0", null);

        Assert.Equal(207, response.StatusCode);
        Assert.Single(XDocument.Parse(response.Body).Descendants(DavXml.Dav + "response"));
    }

    [Fact]
    public async Task PropfindDepthZero_OnTheHome_AnswersTheHomeAlone_NotItsCalendars()
    {
        var response = await server.PropfindAsync(DavPaths.CalendarHome(UserId), "0", null);

        Assert.Equal(207, response.StatusCode);
        var responses = XDocument.Parse(response.Body).Descendants(DavXml.Dav + "response").ToList();
        Assert.Single(responses);
        Assert.EndsWith("/", responses[0].Element(DavXml.Href)!.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MOVE")]
    [InlineData("COPY")]
    [InlineData("LOCK")]
    public async Task AMethodWeDoNotServe_OnAnEvent_Answers405WithTheEventsAllow(string method)
    {
        // The calendar's 405 is covered; the event's own Allow was announced by OPTIONS alone.
        var response = await server.SendAsync(method, DavPaths.Event(UserId, "work", "a.ics"));

        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.EventAllow, response.Header("Allow"));
    }

    [Theory]
    [InlineData("PROPPATCH")]
    [InlineData("REPORT")]
    [InlineData("DELETE")]
    public async Task ACollectionUrlWithoutItsSlash_Answers308_WhateverTheVerb(string method)
    {
        // TracedAsync canonicalises before the action, so the redirect is not PROPFIND's alone —
        // only its test was. DAVx5 and Thunderbird both follow it in silence.
        var response = await server.SendAsync(
            method, DavPaths.Calendar(UserId, "work").TrimEnd('/'));

        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.Calendar(UserId, "work"), response.Header("Location"));
    }
```

`GivenCalendar()` du fixture crée déjà l'agenda `work` ; si l'événement `a.ics` n'existe pas, le
`405` sort quand même — la forme de l'URL suffit, la ressource n'est pas lue.

- [ ] **Step 2 : le contenu du `supported-report-set` de l'événement**

Dans `CalDavPropertiesTests.cs`, à côté de `TheCalendarAnnouncesTheFiveReports` :

```csharp
    [Fact]
    public void TheEventAnnouncesTheTwoReportsItServes()
    {
        // The closed set pins that the property is there; nothing pinned what it names — and
        // ServeReportAsync serves exactly these two on an event.
        var reports = Found(EventResource(), DavXml.Dav + "supported-report-set")!
            .Descendants(DavXml.Dav + "report")
            .Select(report => report.Elements().Single().Name);

        Assert.Equal(
            [DavXml.CalDav + "calendar-multiget", DavXml.CalDav + "calendar-query"], reports);
    }
```

`Found(...)` et `EventResource()` sont les aides que ce fichier emploie déjà —
`TheCalendarAnnouncesTheFiveReports` et `TheHomeAnnouncesExpandPropertyAlone` sont bâtis exactement
ainsi.

- [ ] **Step 3 : les alias TZDB que l'outil écrit dans ses `<start>`**

Dans `IcsTimeZonesTests.cs`, à côté de `IsKnownIana_AnswersTheFirstTierOnly` :

```csharp
    [Theory]
    [InlineData("US/Eastern")]
    [InlineData("US/Mountain")]
    [InlineData("US/Pacific")]
    [InlineData("GMT")]
    public void ATzdbBackwardLink_ResolvesToItself_NeverToItsCanonicalName(string id)
    {
        // ccs-caldavtester writes these in its start blocks; floating.xml dies at its own if
        // US/Eastern does not resolve, and seven more files would read every instant in the wrong
        // zone. What is stored, and what Emit writes back, is the alias itself.
        Assert.Equal(id, IcsTimeZones.ResolveIana(id));
        Assert.True(IcsTimeZones.IsKnownIana(id));
    }
```

- [ ] **Step 4 : lancer les trois fichiers**

Run: `cd src && dotnet test --filter
"FullyQualifiedName~CalDavSurfaceTests|FullyQualifiedName~CalDavPropertiesTests|FullyQualifiedName~IcsTimeZonesTests"`
Expected: PASS. **Si l'un rougit, ne pas ajuster le test** : c'est un défaut de 5c que la tâche
vient de trouver, il se corrige dans le produit et le correctif se cite dans le rapport (spec,
décision 4).

- [ ] **Step 5 : la suite complète, puis commit**

Run: `cd src && dotnet test`

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
test(caldav): les quatre formes HTTP sans assertion, et les alias TZDB

Depth 0, le 405 de l'evenement, le 308 hors PROPFIND, le contenu du
supported-report-set de l'evenement, US/Eastern et ses voisins.
EOF
```

---

### Task 5 : `run.ps1` sait choisir un protocole et nettoie avant de mesurer

**Files:**
- Modify: `tools/caldavtester/run.ps1`
- Modify: `tools/caldavtester/README.md`

**Interfaces:**
- Consumes : `serverinfo.local.json` (`guid`, `email`, `secret`), `serverinfo.template.xml`.
- Produces : `run.ps1 -Protocol CalDAV|CardDAV|Both` (défaut `CalDAV`), `-NoPurge`, `-Suites`,
  `-PrintResponses`, `-SetupOnly`. Les fichiers de suites sont résolus en **chemin absolu** avant
  d'être passés à `testcaldav.py`.

**Deux points à ne pas « corriger » plus tard.**
1. `_normPath` (`manager.py:322`) ne prend « tel quel » qu'un chemin commençant par `.` ou `/`. Un
  chemin Windows `D:\…` tombe dans `os.path.join("scripts/tests", f)` — et en ressort **intact**,
  parce que `ntpath.join` se réinitialise sur une lettre de lecteur. C'est ce qui rend les copies
  locales de la tâche 7 utilisables, et c'est pour ça que `run.ps1` résout en absolu.
2. Le purge est le **défaut**. Un README ne fait pas respecter une obligation ; un défaut, si (spec,
  décision 9).

- [ ] **Step 1 : les paramètres**

En tête de `run.ps1` :

```powershell
[CmdletBinding()]
param(
    [switch]$SetupOnly,
    [ValidateSet('CalDAV', 'CardDAV', 'Both')]
    [string]$Protocol = 'CalDAV',
    [string[]]$Suites,
    [switch]$NoPurge,
    [switch]$PrintResponses
)
```

- [ ] **Step 2 : choisir le ou les fichiers de suites, et résoudre en absolu**

Remplacer le bloc `if (-not $Suites) { … suites.txt … }` par :

```powershell
function Read-SuiteList([string]$Name) {
    Get-Content (Join-Path $PSScriptRoot $Name) |
        ForEach-Object { ($_ -split '#')[0].Trim() } | Where-Object { $_ } |
        ForEach-Object {
            # Une entree du dossier local est passee en chemin absolu : _normPath la rend intacte
            # (voir README), la meme entree relative serait cherchee sous scripts/tests du tester.
            if ($_ -like 'suites/*') { (Resolve-Path (Join-Path $PSScriptRoot $_)).Path } else { $_ }
        }
}

if (-not $Suites) {
    $Suites = @()
    if ($Protocol -in 'CalDAV', 'Both') { $Suites += Read-SuiteList 'suites-caldav.txt' }
    if ($Protocol -in 'CardDAV', 'Both') { $Suites += Read-SuiteList 'suites-carddav.txt' }
}
```

- [ ] **Step 3 : le purge, fait par défaut**

Juste avant le lancement de `testcaldav.py`, et seulement si le protocole touche l'agenda :

```powershell
function Clear-CalendarHome([string]$Base, [string]$Guid, [string]$User, [string]$Secret) {
    $auth = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${User}:${Secret}"))
    # Surtout pas $home : c'est une variable automatique en lecture seule.
    $homeUrl = "$Base/dav/calendars/$Guid/"
    # PROPFIND n'est pas dans l'enumeration de -Method : PowerShell 7 veut -CustomMethod.
    $listing = Invoke-WebRequest -Uri $homeUrl -CustomMethod PROPFIND -SkipHttpErrorCheck `
        -Headers @{ Authorization = $auth; Depth = '1' } -ContentType 'text/xml; charset=utf-8'
    if ($listing.StatusCode -ne 207) { throw "purge : PROPFIND du home a repondu $($listing.StatusCode)" }

    # L'accesseur XML de PowerShell ignore le prefixe : .multistatus rend bien <D:multistatus>.
    $hrefs = ([xml]$listing.Content).multistatus.response.href |
        Where-Object { $_ -and $_.TrimEnd('/') -ne $homeUrl.TrimEnd('/') } | Sort-Object -Descending
    # Les secondaires d'abord, `default` en dernier : un DELETE le vide au lieu de le supprimer.
    foreach ($href in $hrefs) {
        $target = if ($href -match '^https?://') { $href } else { "$Base$href" }
        $null = Invoke-WebRequest -Uri $target -Method Delete -SkipHttpErrorCheck `
            -Headers @{ Authorization = $auth }
    }
}

if (-not $NoPurge -and $Protocol -in 'CalDAV', 'Both') {
    Clear-CalendarHome 'https://api-dev.mail.weesky.net' $local.guid $local.email $local.secret
}
```

Le purge ne touche **que** l'agenda : sous `-Protocol CardDAV` il n'a rien à faire, et sous `Both`
il laisse le carnet aux `<start>` des suites CardDAV, qui le nettoient déjà depuis 4d. L'`href` que
le serveur rend est un chemin (`/dav/calendars/{guid}/work/`), pas une URL absolue — le test sur
`^https?://` est un filet, pas le cas courant.

- [ ] **Step 4 : le nom du fichier de sortie porte le protocole**

```powershell
$out = Join-Path $results ("{0:yyyyMMdd-HHmmss}-{1}.txt" -f (Get-Date), $Protocol.ToLowerInvariant())
```

- [ ] **Step 5 : vérifier le script sans toucher au réseau**

Run: `pwsh -NoProfile -File tools/caldavtester/run.ps1 -SetupOnly` Expected: `Prêt :
…serverinfo.xml` — `-SetupOnly` sort avant le purge et avant le lancement. Si
`serverinfo.local.json` manque, le message d'erreur attendu est celui qui le dit ; c'est le cas
normal sur une machine sans le compte de test, et il vaut vérification du script.

Run: `pwsh -NoProfile -Command "& { . tools/caldavtester/run.ps1 -SetupOnly } 2>&1 | Out-Null; $?"`
Expected: aucune erreur de syntaxe PowerShell.

- [ ] **Step 6 : le README**

Ajouter au `README.md` : le mode d'emploi CalDAV à côté du CardDAV ; l'avertissement que **le compte
de test n'est pas un compte personnel** et qu'un `DELETE` d'agenda secondaire le supprime au lieu de
le vider (5c décision 11) ; le purge donné comme fait **par défaut**, avec le seul cas où `-NoPurge`
se justifie — le diagnostic d'un test isolé qu'on veut rejouer sur l'état laissé par le précédent,
**jamais** un passage complet ni un rejeu dont on consigne le résultat ; et la lecture de
`_normPath` du point 1 ci-dessus, pour que personne ne « corrige » la résolution en absolu.

Ajouter aussi la contrainte d'authentification : un passage CalDAV consomme **neuf** des dix échecs
Basic qu'`api-dev` tolère par quart d'heure, un passage `Both` **dix** — un seul passage complet par
fenêtre de quinze minutes.

- [ ] **Step 7 : commit**

```bash
git add tools/caldavtester/run.ps1 tools/caldavtester/README.md
git commit -F - <<'EOF'
chore(caldavtester): -Protocol, le purge par defaut et -NoPurge

Les entrees du dossier suites/ sont resolues en chemin absolu, seule forme
que _normPath rend intacte.
EOF
```

---

### Task 6 : `serverinfo.template.xml` et les deux fichiers de suites

**Files:**
- Modify: `tools/caldavtester/serverinfo.template.xml`
- Rename: `tools/caldavtester/suites.txt` → `tools/caldavtester/suites-carddav.txt`
- Create: `tools/caldavtester/suites-caldav.txt`

**Interfaces:**
- Consumes : `run.ps1` de la tâche 5, qui lit les deux fichiers de suites et substitue `{guid}`,
  `{email}`, `{secret}`.
- Produces : les clés que les tâches suivantes supposent justes, en particulier `$calendarhome1:` =
  `/dav/calendars/{guid}` et `$calendar:` = `default`.

- [ ] **Step 1 : les substitutions**

Dans `<substitutions>`, remplacer les valeurs suivantes. Colonne de gauche : ce qu'elle vaut
aujourd'hui (héritage du gabarit amont) ; colonne de droite : ce qu'elle doit valoir.

| Clé | Aujourd'hui | 5d |
|---|---|---|
| `$calendar:` | `calendar` | `default` |
| `$userguid1:` | `10000000-0000-0000-0000-000000000001` | `{guid}` |
| `$principals_uids:` | `$principalcollection:$uidstype:/` | `/dav/principals/` |
| `$principals_users:` | `$principalcollection:$userstype:/` | `/dav/principals/` |
| `$calendars_uids:` | `$calendars:$uidstype:/` | `/dav/calendars/` |
| `$calendars_users:` | `$calendars:$userstype:/` | `/dav/calendars/` |
| `$calendarhome1:` | `$calendars_uids:$userguid1:` | `/dav/calendars/{guid}` |
| `$email1:` | `$userid1:@example.com` | `{email}` |
| `$cuaddr1:` | `mailto:$email1:` | `mailto:{email}` |

Ne **pas** toucher : `$calendars:` (`$root:calendars/` vaut déjà `/dav/calendars/`), `$principal1:`
(déjà juste depuis 4d), `$calendarpath1:` (`$calendarhome1:/$calendar:` se corrige tout seul),
`$cuaddrurn1:` (`urn:x-uid:$userguid1:`, idem), `$calendar_sync_extra_items:` et
`$calendar_sync_extra_count:` (posés par le correctif H1 de 4d).

`$principaluri1:` se corrige **par** `$principals_uids:`, sur laquelle il compose — c'est pourquoi
cette clé-là est dans la table alors qu'aucun fichier retenu ne la lit directement.

`$calendar_home_items_initial_sync:` **reste à la valeur amont**. Ses vingt-quatre lectures sont
toutes dans des tests de `sync-report.xml` gardés par `sync-report-home`, éteinte : clé inerte, mais
pas clé corrigée — la distinction compte le jour où cette feature s'allumerait. Écrire cette phrase
en commentaire XML au-dessus de la clé.

- [ ] **Step 2 : retirer les clés que rien ne lit**

Supprimer `$polls:`, `$pollspath1:`, `$timezoneservice:`, `$timezonestdservice:`, `$directory:`,
`$add-member:`, `$attachments:`, `$servertoserver:`. Les deux conditions comptent autant l'une que
l'autre : aucune suite retenue ne les lit, **et** aucune clé conservée ne les compose — vérifier la
seconde par `grep` sur le gabarit avant de supprimer.

**Garder** `$tasks:`, `$inbox:`, `$outbox:`, `$dropbox:`, `$notification:` et `$freebusy:`. Une clé
absente n'est pas remplacée par du vide : `ccs-caldavtester` la laisse en **texte littéral**, et la
requête partirait sur `/dav/calendars/{guid}/$tasks:`. Gardée, elle donne un `404` lisible sur une
collection que nous ne servons pas — un échec nommable ; retirée, une URL absurde et un échec
inclassable.

- [ ] **Step 3 : les `<features>`**

Le bloc devient, chaque ligne éteinte gardant sa raison en commentaire :

```xml
	<features>
		<feature>caldav</feature>
		<feature>carddav</feature>						<!-- le gabarit sert les deux protocoles -->
		<feature>current-user-principal</feature>
		<feature>sync-report</feature>
		<feature>well-known</feature>
		<feature>expand-property</feature>
		<feature>ctag</feature>							<!-- getctag est servie : ctag.xml la mesure -->
		<feature>supported-component-sets-one</feature>	<!-- dit ce que nous sommes ; rien ne la lit -->
		<feature>COPY Method</feature>					<!-- allumee expres : sans elle, les 39 tests sautent -->
		<feature>MOVE Method</feature>
	</features>
```

`limits`, allumée en 4d, **s'éteint** : les deux seuls fichiers qui la lisent sont les deux
`limits.xml`, exclus l'un et l'autre, et elle annonce des plafonds CalendarServer que nous n'avons
pas.

`Extended MKCOL` n'est **pas** ajoutée : vérification faite sur tout `scripts/tests/`, aucun fichier
de l'outil ne la lit. `CardDAV/mkcol.xml` cherche le jeton `extended-mkcol` de l'en-tête `DAV:` par
le callback `header`, avec un motif nié, dans un test porté par un `ignore` — ni la feature, ni même
une mesure.

Éteintes et nommées en commentaire au-dessus du bloc, parce qu'une feature éteinte sans être nommée
est une décision non prise : `no-duplicate-uids`, `sync-report-home`, `sync-report-limit`,
`regular-collection`, `supported-component-sets`, `split-calendars`, `limits`, `query-extended`,
`timerange-low-limit`, `timerange-high-limit`, `timezones-by-reference`, `remove-duplicate-alarms`,
`directory listing`, `only-proxy-groups`, `own-root`, `auth-on-root`, `extended-principal-search`,
`ACL Method`, les quatre `* REPORT` de principaux, et tout le bloc ordonnancement / partage / pièces
jointes / `vpoll`.

- [ ] **Step 4 : `suites-carddav.txt`**

```bash
git mv tools/caldavtester/suites.txt tools/caldavtester/suites-carddav.txt
```

et **retirer la ligne `CardDAV/limits.xml`**, en remplaçant le commentaire qui disait qu'il était «
ignoré par l'outil lui-même » : avec `caldav` désormais allumée, son `require-feature caldav` est
satisfait et il se réveillerait — deux `PROPFIND` attendant `CS:max-collections` et
`CS:max-resources`, propriétés CalendarServer, soit deux échecs CardDAV de plus qui ne viendraient
pas du serveur.

- [ ] **Step 5 : `suites-caldav.txt`**

Un fichier par ligne, dans cet ordre :

```
# Suites CalDAV lancees (chemins relatifs a scripts/tests du tester, sauf suites/ qui est local).
#
# ORDRE : le <end> de ctag.xml fait un DELETEALL sur $calendarpath1:, c'est-a-dire qu'il VIDE
# `default` au milieu du passage. Verifie : aucun fichier lance apres lui n'en depend, chacun pose
# le sien dans son <start> ou dans ses tests. Reordonner cette liste demande de le reverifier.
#
# QUATRE FICHIERS MESURENT MOINS QUE LEUR NOM : get.xml ne joue qu'UN test (directory listing garde
# ses deux suites de collection) ; nonascii.xml joue douze tests (trois suites d'URI en ignore, une
# gardee par regular-collection) ; conditional.xml et options.xml ne portent que des conventions
# CalendarServer et restent lances pour que leurs requetes partent.
#
# BRUIT MULTI-UTILISATEURS (un 2e et un 3e compte ne sont pas definis, leurs cles restent en texte
# litteral) : expandproperty.xml suite Membership REPORT, gardee par RIEN, quatre de ses six tests
# partent ; current-user-principal.xml, ses tests $principaluri2:/$userid2: ; nonascii.xml, ses
# tests $userid2: et $i18n* ; mkcalendar.xml, test 2 de MKCALENDAR read-free-busy privilege ;
# ctag.xml, trois des cinq tests de Scheduling (t1 et t4 mesurent, eux) et les tests $userid2: de
# PUT/DELETE/COPY/MOVE ; aclreports.xml, dans des suites deja gardees.
#
# EXCLUS AVEC LEUR RAISON : caldavIOP.xml (trois comptes + ordonnancement), duplicate_uids.xml
# (regle CalendarServer du refus par home, pas la notre), bad-ical.xml (son repertoire de donnees
# n'a jamais ete versionne), CalDAV/limits.xml (plafonds CalendarServer), freebusy.xml (c'est le
# POST d'ordonnancement, pas le rapport), et tout implicit*/schedule*/sharing-*/managed-attachments*
# /partitioning-*/trash*/servertoserver*/depthreports*/directory* plus json, bad-json, rscale,
# vtodos, timezoneservice, timezonestdservice, webcal, bulk, add-member, quota, prefer, brief,
# resourceid, attachments, availability, extended-freebusy, freebusy-url, default-alarms,
# alarm-dismissal, privateevents, privatecomments, acl, calendaruserproxy, proxyauthz,
# recurrence-splitting, server-info, pretest, polls, dropbox, collection-redirects.
#
# copymove.xml et aclreports.xml sont lances EN SACHANT qu'ils echoueront : leur echec mesure une
# divergence nommee, et un fichier qu'on ne lance pas ne mesure rien.
CalDAV/propfind.xml
CalDAV/proppatch.xml
CalDAV/put.xml
CalDAV/get.xml
suites/CalDAV/delete.xml
suites/CalDAV/reports.xml
CalDAV/sync-report.xml
CalDAV/errors.xml
CalDAV/mkcalendar.xml
CalDAV/options.xml
CalDAV/nonascii.xml
CalDAV/well-known.xml
CalDAV/current-user-principal.xml
CalDAV/expandproperty.xml
CalDAV/recurrenceput.xml
CalDAV/floating.xml
CalDAV/ctag.xml
CalDAV/encodedURIs.xml
CalDAV/conditional.xml
CalDAV/copymove.xml
CalDAV/aclreports.xml
CalDAV/timezones.xml
CalDAV/ical-client.xml
```

- [ ] **Step 6 : vérifier que les deux listes couvrent tout le répertoire**

Run:
```bash
py -3 - <<'EOF'
import os, re, fnmatch
tests = "<chemin du clone>/scripts/tests/CalDAV"
kept = {l.strip().split('/')[-1] for l in open('tools/caldavtester/suites-caldav.txt', encoding='utf-8')
        if l.strip() and not l.startswith('#')}
patterns = ['implicit*', 'schedule*', 'sharing-*', 'managed-attachments*', 'partitioning-*',
            'trash*', 'servertoserver*.xml', 'depthreports*.xml', 'directory*.xml']
named = {'caldavIOP.xml','duplicate_uids.xml','bad-ical.xml','freebusy.xml','polls.xml','dropbox.xml',
         'json.xml','bad-json.xml','rscale.xml','vtodos.xml','timezoneservice.xml','webcal.xml',
         'timezonestdservice.xml','bulk.xml','add-member.xml','quota.xml','limits.xml','prefer.xml',
         'brief.xml','resourceid.xml','attachments.xml','availability.xml','extended-freebusy.xml',
         'freebusy-url.xml','default-alarms.xml','alarm-dismissal.xml','privateevents.xml',
         'privatecomments.xml','acl.xml','calendaruserproxy.xml','proxyauthz.xml',
         'recurrence-splitting.xml','server-info.xml','pretest.xml','collection-redirects.xml'}
orphans = [f for f in sorted(os.listdir(tests)) if f.endswith('.xml')
           and f not in kept and f not in named
           and not any(fnmatch.fnmatch(f, p) for p in patterns)]
print("orphelins :", orphans or "aucun")
EOF
```
Expected: `orphelins : aucun`. Un fichier oublié se lancerait un jour par `--all` sans que personne
n'ait décidé de le lancer.

- [ ] **Step 7 : commit**

```bash
git add tools/caldavtester/
git commit -F - <<'EOF'
chore(caldavtester): les substitutions agenda, les features et les suites

suites.txt devient suites-carddav.txt moins CardDAV/limits.xml, que caldav
allumee reveillerait.
EOF
```

---

### Task 7 : les deux copies locales, sans lesquelles le plus gros fichier ne mesure rien

**Files:**
- Create: `tools/caldavtester/suites/CalDAV/reports.xml`
- Create: `tools/caldavtester/suites/CalDAV/delete.xml`
- Create: `tools/caldavtester/suites/CalDAV/reports.xml.diff`,
  `tools/caldavtester/suites/CalDAV/delete.xml.diff`
- Create: `tools/caldavtester/suites/CalDAV/README.md`

**Interfaces:**
- Consumes : le clone épinglé, sous `.caldavtester/ccs-caldavtester/scripts/tests/CalDAV/`.
- Produces : deux fichiers que `suites-caldav.txt` référence par `suites/CalDAV/…`, que la tâche 5
  résout en absolu.

**Pourquoi.** Une requête d'un bloc `<start>` est vérifiée de force en 2xx — et un `<verify>` qu'on
y écrirait serait purement ignoré, l'outil passant `doverify=False` à toutes les requêtes d'un
`<start>`. Le premier échec tue le fichier : « Start items failed - tests will not be run », **une**
erreur comptée dans `errors=` et **zéro** test joué. `reports.xml` est la principale mesure du
`time-range` et du `free-busy-query`, la seule de l'`expand` et du `VALARM` ; tué, il ne mesure
rien.

- [ ] **Step 1 : copier les deux fichiers amont**

Le clone épinglé n'est pas supposé présent : `run.ps1` le fait, mais il exige Python 2.7 avant, et
cette tâche n'en a pas besoin. Elle le pose elle-même, avec `git` seul :

```bash
UP=tools/caldavtester/.caldavtester/ccs-caldavtester
if [ ! -d "$UP" ]; then
  git clone --quiet https://github.com/apple/ccs-caldavtester.git "$UP"
fi
git -C "$UP" -c advice.detachedHead=false checkout --quiet bed21e5924275552c1561febc8203a9f194cf737

mkdir -p tools/caldavtester/suites/CalDAV
cp "$UP/scripts/tests/CalDAV/reports.xml" tools/caldavtester/suites/CalDAV/
cp "$UP/scripts/tests/CalDAV/delete.xml"  tools/caldavtester/suites/CalDAV/
```

`.caldavtester/` doit être ignoré par git — le vérifier, et l'ajouter à `.gitignore` s'il ne l'est
pas : le clone ne se versionne pas, seules les deux copies le sont.

- [ ] **Step 2 : retirer de `reports.xml` les sept préparatifs impossibles, et rien d'autre**

Dans le seul bloc `<start>`, supprimer les `<request>` :
- les **six** `PUT` sur `$taskspath1:/101.ics` … `106.ics` — un agenda ne sert que `VEVENT`, chacun
  reçoit un `404` ;
- le **septième**, `s15`, `PUT $calendarpath1:/15.ics` dont le corps est
  `Resource/CalDAV/reports/put/15.txt` : son unique composant est un `VFREEBUSY`, et `IcsGuards` le
  refuse en `403 supported-calendar-component` exactement comme un `VTODO`. Il porte
  `exclude-feature split-calendars`, feature éteinte — donc il **part**. En oublier un, c'est tuer
  le fichier pour la même raison une ligne plus loin.

Ne toucher à **aucun** test : les tests `VTODO` restés dans le corps échouent ensuite un par un, en
`404` nommable, et `free-busy reports` t2 échoue en attendant l'intervalle `unavailable` que le
`VFREEBUSY` absent aurait produit — c'est la mesure, pas un dommage.

- [ ] **Step 3 : retirer de `delete.xml` le sien**

Dans son `<start>`, supprimer le `PUT $taskspath1:/1todo.ics`. Son test 2, qui lit `$taskspath1:`,
reste et échoue nommément.

- [ ] **Step 4 : versionner le diff contre l'amont**

```bash
cd tools/caldavtester
diff -u .caldavtester/ccs-caldavtester/scripts/tests/CalDAV/reports.xml suites/CalDAV/reports.xml > suites/CalDAV/reports.xml.diff
diff -u .caldavtester/ccs-caldavtester/scripts/tests/CalDAV/delete.xml  suites/CalDAV/delete.xml  > suites/CalDAV/delete.xml.diff
```

- [ ] **Step 5 : vérifier que le diff ne touche que les `<start>`**

Run: `grep -c '^[-+]' tools/caldavtester/suites/CalDAV/reports.xml.diff && grep '^+'
tools/caldavtester/suites/CalDAV/reports.xml.diff | grep -v '^+++'` Expected: **aucune ligne
ajoutée** (le second `grep` ne rend rien) et toutes les lignes retirées à l'intérieur du bloc
`<start>`. C'est la preuve de cette tâche : les copies retirent des préparatifs, elles ne modifient
pas un test. Vérifier de même pour `delete.xml`.

- [ ] **Step 6 : le README des copies**

`tools/caldavtester/suites/CalDAV/README.md` dit : le commit amont épinglé ; ce qui est retiré et
pourquoi (les sept `PUT` et le `PUT` `VFREEBUSY`) ; que ce n'est **pas** un portage de l'outil — pas
une ligne de Python ne change ; et que retirer les sept ne **garantit** pas que `reports.xml` sera
joué, son `<start>` gardant vingt `PUT` `VEVENT` non gardés, dont trois à `RECURRENCE-ID` et deux
d'entre eux portant deux `VEVENT` du même UID. Ce bloc-là est rejoué seul en `-PrintResponses` avant
que la mesure de départ ne soit figée (étape 2 ci-dessous).

- [ ] **Step 7 : commit**

```bash
git add tools/caldavtester/suites/
git commit -F - <<'EOF'
chore(caldavtester): copies locales de reports.xml et delete.xml

Leurs <start> posent des VTODO et un VFREEBUSY qu'un agenda VEVENT refuse ;
sans ca les deux fichiers meurent avant leur premier test.
EOF
```

---

### Task 8 : `AppleDiscoveryReplayTests`, faute d'appareil

**Files:**
- Create: `snoopy.microservice.Tests/Controllers/AppleDiscoveryReplayTests.cs`

**Interfaces:**
- Consumes : `DavTestServer`, `DavPaths`, `DavXml`, `CalDavProperties` (indirectement, par les
  réponses).
- Produces : rien pour les autres tâches.

**Ce que cette couche prouve, et ce qu'elle ne prouve pas.** Elle prouve que nos réponses ne font
pas tomber la séquence d'appairage, et qu'une propriété qu'un client Apple demande mais que nous ne
servons pas sort en `404` propstat plutôt qu'en `500` ou en silence. Elle ne prouve pas qu'un iPhone
en fasse quelque chose : le rapport écrit « non branché », jamais « couvert ».

**Provenance.** Chaque corps porte en commentaire d'où il vient —
`Resource/CalDAV/ical-client/client1/1.xml`, `client2/2.xml`, une page ou une discussion publique
nommée. Un corps sans provenance est une supposition déguisée en test. Les corps `client*/…` se
lisent dans le clone épinglé et se recopient **verbatim**.

**La fixture du fichier**, qui n'emprunte rien aux autres — `GivenCalendar` et `GivenEvent` sont
privés à `CalDavQueryTests` ; seul `Ics` est partagé (`Tests/Fixtures/Ics.cs`) :

```csharp
public sealed class AppleDiscoveryReplayTests : IAsyncLifetime
{
    private DavTestServer server = null!;
    private Guid calendarId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        calendarId = Guid.NewGuid();
        using var db = server.CreateContext();
        db.Calendars.Add(new CalendarRow
        {
            Id = calendarId,
            UserId = server.UserId,
            DavName = CalendarStore.DefaultDavName,
            DisplayName = "Personal",
            Description = "Ce que le webmail pose",
            Color = "#3b82c4",
            Order = 0,
            TimeZone = "Europe/Brussels",
            IsVisible = true,
        });
        db.SaveChanges();
        // A calendar is born with its sync-state row (5a): without it, every REPORT here answers
        // 403 valid-sync-token, which is the server being right about a database that lost a row.
        await server.CreateStateAsync(calendarId);
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    private string Calendar() => DavPaths.Calendar(server.UserId, CalendarStore.DefaultDavName);

    private void GivenEvent(string davName, string ics)
    {
        using var db = server.CreateContext();
        db.CalendarEvents.Add(CalDavQueryTests.Row(server.UserId, calendarId, davName, ics,
            IcsProjector.Project(IcsDocument.TryLoad(ics)!, Ics.Zone)));
        db.SaveChanges();
    }
}
```

`CalDavQueryTests.Row` est privé lui aussi : le rendre `internal static` — une seule projection de
ligne pour les deux fichiers vaut mieux que deux qui divergeront.

- [ ] **Step 1 : les deux racines**

```csharp
    [Theory]
    [InlineData("/")]
    [InlineData("/dav/")]
    public async Task PropfindDepthZero_OnARoot_Answers401WithoutCredentials(string path)
    {
        // The first request iOS sends is a PROPFIND on the bare host, before .well-known and
        // before the SRV lookup (stalwart discussion #3259, a user's trace — the only source).
        var response = await server.SendUnauthenticated("PROPFIND", path);

        Assert.Equal(401, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/dav/")]
    public async Task PropfindDepthZero_OnARoot_AnswersTheCurrentUserPrincipal(string path)
    {
        var response = await server.PropfindAsync(path, "0",
            new XElement(DavXml.Dav + "propfind",
                new XElement(DavXml.Prop, new XElement(DavXml.Dav + "current-user-principal")))
                .ToString());

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(DavPaths.Principal(server.UserId),
            XDocument.Parse(response.Body)
                .Descendants(DavXml.Dav + "current-user-principal").Single()
                .Element(DavXml.Href)!.Value);
    }
```

Si `DavPaths.Principal(Guid)` ne s'appelle pas ainsi, employer l'aide que `DavPrincipalTests`
emploie déjà — jamais un littéral.

- [ ] **Step 2 : `/.well-known/caldav`, avec et sans barre finale, en `PROPFIND`**

```csharp
    [Theory]
    [InlineData("/.well-known/caldav")]
    [InlineData("/.well-known/caldav/")]
    public async Task PropfindOnTheWellKnown_Redirects_TrailingSlashOrNot(string path)
    {
        // PROPFIND, not GET: this is how iOS, DAVx5 and Thunderbird open discovery, and the
        // trailing-slash form is the one ccs-caldavtester sends — untested until now.
        var response = await server.SendUnauthenticated("PROPFIND", path);

        Assert.Equal(301, response.StatusCode);
        Assert.Equal("/dav/", response.Header("Location"));
    }
```

Le `Location` rendu est relatif, ce que RFC 7231 § 7.1.2 autorise ; le `301` est nommément prévu par
RFC 6764 § 5 (« 301, 303, or 307 »).

- [ ] **Step 3 : le principal, avec les deux corps verbatim**

Les corps sont recopiés du clone épinglé, tels quels, préfixes de namespace compris — les réécrire «
proprement » ferait rejouer autre chose que ce qu'Apple envoie :

```csharp
    /// <summary>Resource/CalDAV/ical-client/client1/1.xml — iOS, verbatim.</summary>
    private const string IosPrincipalBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <x0:propfind xmlns:x2="http://calendarserver.org/ns/" xmlns:x1="urn:ietf:params:xml:ns:caldav" xmlns:x0="DAV:">
         <x0:prop>
          <x1:calendar-home-set/>
          <x1:calendar-user-address-set/>
          <x1:schedule-inbox-URL/>
          <x1:schedule-outbox-URL/>
          <x2:dropbox-home-URL/>
          <x2:notifications-URL/>
          <x0:displayname/>
         </x0:prop>
        </x0:propfind>
        """;

    /// <summary>Resource/CalDAV/ical-client/client2/1.xml — iCal 10.6, verbatim. Same list, with
    /// notification-URL in the singular, plus three DAV: properties.</summary>
    private const string ICalPrincipalBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <x0:propfind xmlns:x1="urn:ietf:params:xml:ns:caldav" xmlns:x0="DAV:" xmlns:x2="http://calendarserver.org/ns/">
         <x0:prop>
          <x0:principal-collection-set/>
          <x1:calendar-home-set/>
          <x1:calendar-user-address-set/>
          <x1:schedule-inbox-URL/>
          <x1:schedule-outbox-URL/>
          <x2:dropbox-home-URL/>
          <x2:notification-URL/>
          <x0:displayname/>
          <x0:principal-URL/>
          <x0:supported-report-set/>
         </x0:prop>
        </x0:propfind>
        """;

    /// <summary>The property names of one propstat of one response, in document order.</summary>
    private static List<XName> NamesIn(XElement response, int status) =>
    [
        .. response.Elements(DavXml.Dav + "propstat")
            .Where(propstat => propstat.Element(DavXml.Status)!.Value
                .Contains($" {status} ", StringComparison.Ordinal))
            .SelectMany(propstat => propstat.Element(DavXml.Prop)!.Elements().Select(e => e.Name)),
    ];

    private static XElement TheOnlyResponse(DavTestResponse response) =>
        XDocument.Parse(response.Body).Descendants(DavXml.Dav + "response").Single();

    [Fact]
    public async Task TheIosPrincipalBody_404sWhatWeDoNotServe_RatherThan500OrSilence()
    {
        var response = await server.PropfindAsync(DavPaths.Principal(server.UserId), "0", IosPrincipalBody);

        Assert.Equal(207, response.StatusCode);
        var only = TheOnlyResponse(response);
        Assert.Contains(DavXml.CalDav + "calendar-home-set", NamesIn(only, 200));
        Assert.Contains(DavXml.Dav + "displayname", NamesIn(only, 200));
        // Nothing here schedules, and there is no dropbox nor a notification collection.
        Assert.Equal(
            [DavXml.CalDav + "schedule-inbox-URL", DavXml.CalDav + "schedule-outbox-URL",
             DavXml.CalendarServer + "dropbox-home-URL", DavXml.CalendarServer + "notifications-URL"],
            NamesIn(only, 404));
    }

    [Fact]
    public async Task TheICalPrincipalBody_404sTheSameFour_WithNotificationUrlInTheSingular()
    {
        var response = await server.PropfindAsync(DavPaths.Principal(server.UserId), "0", ICalPrincipalBody);

        Assert.Equal(207, response.StatusCode);
        var only = TheOnlyResponse(response);
        Assert.Equal(
            [DavXml.CalDav + "schedule-inbox-URL", DavXml.CalDav + "schedule-outbox-URL",
             DavXml.CalendarServer + "dropbox-home-URL", DavXml.CalendarServer + "notification-URL"],
            NamesIn(only, 404));
        Assert.Contains(DavXml.Dav + "principal-URL", NamesIn(only, 200));
        Assert.Contains(DavXml.Dav + "supported-report-set", NamesIn(only, 200));
    }
```

`email-address-set` et `resource-id` viennent de la liste **CardDAV** d'iOS chez sabre, pas de ces
corps : ils n'entrent pas.

- [ ] **Step 4 : le home en `Depth: 1`, avec deux tables d'attente**

**Un `Depth: 1` rend deux sortes de `response` et l'attente n'est pas la même sur les deux.** La
table `DavResourceKind.CalendarHome` ne sert que quatre propriétés — `resourcetype`, `displayname`,
`supported-report-set`, `current-user-principal` —, donc sur la `response` du home lui-même tout le
reste, `getctag`, `owner`, `calendar-color` et `sync-token` compris, sort en `404` propstat. C'est
sur les `response` des **agendas** que les propriétés que nous servons répondent `200`. Une attente
unique serait fausse d'un côté ou de l'autre.

```csharp
    /// <summary>Resource/CalDAV/ical-client/client1/2.xml — iOS, verbatim, sept propriétés.</summary>
    private const string IosHomeBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <x0:propfind xmlns:x1="http://calendarserver.org/ns/" xmlns:x0="DAV:" xmlns:x3="http://apple.com/ns/ical/" xmlns:x2="urn:ietf:params:xml:ns:caldav">
         <x0:prop>
          <x1:getctag/>
          <x0:displayname/>
          <x2:calendar-description/>
          <x3:calendar-color/>
          <x3:calendar-order/>
          <x0:resourcetype/>
          <x2:calendar-free-busy-set/>
         </x0:prop>
        </x0:propfind>
        """;

    [Fact]
    public async Task TheIosHomeBody_AnswersSixOfSevenOnACalendar_AndAlmostNothingOnTheHome()
    {
        var response = await server.PropfindAsync(DavPaths.CalendarHome(server.UserId), "1", IosHomeBody);

        Assert.Equal(207, response.StatusCode);
        var responses = XDocument.Parse(response.Body).Descendants(DavXml.Dav + "response")
            .ToDictionary(r => r.Element(DavXml.Href)!.Value);

        var home = responses[DavPaths.CalendarHome(server.UserId)];
        // The home's table serves four properties and none of these is among them — getctag
        // included, which is a calendar's property, never a home's.
        Assert.Equal([DavXml.Dav + "displayname", DavXml.Dav + "resourcetype"], NamesIn(home, 200));
        Assert.Contains(DavXml.CalendarServer + "getctag", NamesIn(home, 404));

        var calendar = responses[DavPaths.Calendar(server.UserId, CalendarStore.DefaultDavName)];
        Assert.Equal(
            [DavXml.CalendarServer + "getctag", DavXml.Dav + "displayname",
             DavXml.CalDav + "calendar-description", DavXml.Apple + "calendar-color",
             DavXml.Apple + "calendar-order", DavXml.Dav + "resourcetype"],
            NamesIn(calendar, 200));
        Assert.Equal([DavXml.CalDav + "calendar-free-busy-set"], NamesIn(calendar, 404));
        // A ctag with no value would pass a false test for a measure.
        Assert.NotEmpty(calendar.Descendants(DavXml.CalendarServer + "getctag").Single().Value);
    }
```

Puis le corps `client2/2.xml`, recopié verbatim de la même façon — **vingt-six** propriétés :
`getctag`, `displayname`, `calendar-description`, `calendar-color`, `calendar-order`,
`supported-calendar-component-set`, `resourcetype`, `owner`, `calendar-free-busy-set`,
`schedule-calendar-transp`, `schedule-default-calendar-URL`, `quota-available-bytes`,
`quota-used-bytes`, `calendar-timezone`, `current-user-privilege-set`, `source`,
`subscribed-strip-alarms`, `subscribed-strip-attachments`, `subscribed-strip-todos`, `refreshrate`,
`push-transports`, `pushkey`, `publish-url`, `allowed-sharing-modes`, `xmpp-server`, `xmpp-uri`. Son
test assert que sur l'agenda tout répond `200` **sauf** `quota-available-bytes`, `quota-used-bytes`,
`calendar-free-busy-set` et les propriétés CalendarServer et Apple que nous ne servons pas,
lesquelles sortent en `404` — et qu'aucune ne fait un `500`.

Un troisième corps, sourcé comme tel, ajoute `sync-token` et `default-alarm-vevent-datetime` : la
liste que sabre attribue à iCal 10.9.2, sur sa page « iCal ». `sync-token` répond `200` sur un
agenda, `default-alarm-vevent-datetime` `404`.

- [ ] **Step 5 : `sync-collection`, `calendar-multiget`, puis le `calendar-query` à `start` seul**

```csharp
    [Fact]
    public async Task AnInitialSyncCollection_ThenAMultigetOnWhatItReturns()
    {
        // RFC 6578 § 3.4's initial synchronisation: an EMPTY sync-token. No Apple sync-collection
        // body is public, so the shape is the one Thunderbird sends; Depth is left off, for want of
        // a source showing Apple's.
        GivenEvent("a.ics", Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T100000Z"));
        var body = new XElement(DavXml.Dav + "sync-collection",
            new XElement(DavXml.Dav + "sync-token"),
            // RFC 6578 § 3 makes sync-level mandatory, and Thunderbird sends 1: a body without it
            // would not be the shape this comment claims to replay.
            new XElement(DavXml.Dav + "sync-level", "1"),
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag")));

        var synced = await server.SendAsync("REPORT", Calendar(), body.ToString());

        Assert.Equal(207, synced.StatusCode);
        var href = XDocument.Parse(synced.Body).Descendants(DavXml.Href)
            .Select(h => h.Value).First(v => v.EndsWith("a.ics", StringComparison.Ordinal));

        var multiget = new XElement(DavXml.CalDav + "calendar-multiget",
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag"),
                new XElement(DavXml.CalDav + "calendar-data")),
            new XElement(DavXml.Href, href));

        var fetched = await server.SendAsync("REPORT", Calendar(), multiget.ToString());

        Assert.Equal(207, fetched.StatusCode);
        Assert.Single(XDocument.Parse(fetched.Body).Descendants(DavXml.CalDav + "calendar-data"));
    }

    [Fact(Skip = "décision 11 — le Skip tombe à la tâche 10")]
    public async Task TheIosInitialLoad_ATimeRangeWithStartAlone_ServesAnEventBeyondFiveYears()
    {
        // The shape of the only published iOS calendar-query (sabre): a time-range on the VEVENT
        // comp-filter, start alone, and a prop asking getetag and resourcetype — nothing else.
        GivenEvent("passeport.ics",
            Ics.Single("DTSTART:20320315T090000Z", "DTEND:20320315T100000Z"));
        var body = new XElement(DavXml.CalDav + "calendar-query",
            new XElement(DavXml.Prop, new XElement(DavXml.Dav + "getetag"),
                new XElement(DavXml.Dav + "resourcetype")),
            new XElement(DavXml.CalDav + "filter",
                new XElement(DavXml.CalDav + "comp-filter", new XAttribute("name", "VCALENDAR"),
                    new XElement(DavXml.CalDav + "comp-filter", new XAttribute("name", "VEVENT"),
                        new XElement(DavXml.CalDav + "time-range",
                            new XAttribute("start", "20260907T000000Z"))))));

        var response = await server.SendAsync("REPORT", Calendar(), body.ToString());

        Assert.Equal(207, response.StatusCode);
        Assert.Contains("passeport.ics", response.Body, StringComparison.Ordinal);
    }
```

**Ce test-là échoue tant que la tâche 10 n'est pas faite**, d'où le `Skip` : un test rouge laissé
rouge rendrait la suite inutilisable pendant les étapes 1 à 3.

La variante à `comp-filter VALARM` n'entre pas : aucune source publique ne l'atteste, et un corps
sans provenance n'a pas sa place ici.

- [ ] **Step 6 : le `PROPPATCH` de macOS**

```csharp
    [Fact]
    public async Task TheMacOsProppatch_ReadsTheColour_KeepsTheOrder_AndRefusesTheDefaultAlarm()
    {
        // calendar-color carries symbolic-color on iOS and macOS (stalwart #1611); 5c ignores the
        // attribute and reads the value. default-alarm-vevent-date is the body macOS sends the
        // moment default alerts are touched (sabre #935) — and the one we refuse.
        var body = new XElement(DavXml.Dav + "propertyupdate",
            new XElement(DavXml.Dav + "set", new XElement(DavXml.Prop,
                new XElement(DavXml.Apple + "calendar-color",
                    new XAttribute("symbolic-color", "green"), "#63DA38"),
                new XElement(DavXml.Apple + "calendar-order", "3"),
                new XElement(DavXml.CalDav + "default-alarm-vevent-date", "BEGIN:VALARM\r\nEND:VALARM"))));

        var response = await server.SendAsync("PROPPATCH", Calendar(), body.ToString());

        Assert.Equal(207, response.StatusCode);
        var only = TheOnlyResponse(response);
        Assert.Equal(
            [DavXml.Apple + "calendar-color", DavXml.Apple + "calendar-order"], NamesIn(only, 200));
        Assert.Equal([DavXml.CalDav + "default-alarm-vevent-date"], NamesIn(only, 403));

        using var db = server.CreateContext();
        Assert.Equal("#63da38", db.Calendars.Single(c => c.DavName == CalendarStore.DefaultDavName).Color);
    }
```

`default-alarm-vevent-datetime` n'est attestée qu'en **lecture** (la liste `PROPFIND` d'iCal 10.9.2)
: elle n'entre pas dans le `PROPPATCH` rejoué. Le coût s'écrit dans le rapport, pas dans le test :
sabre nº 935 rapporte que macOS Calendar, sur un `403` au `PROPPATCH` de
`default-alarm-vevent-date`, « stops after that ».

- [ ] **Step 7 : lancer, puis commit**

Run: `cd src && dotnet test --filter "FullyQualifiedName~AppleDiscoveryReplayTests"` Expected: PASS,
sauf le `calendar-query` à `start` seul, marqué `Skip`.

Run: `cd src && dotnet test` Expected: PASS.

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
test(caldav): rejeu in-process de l'appairage des clients Apple

Corps verbatim de Resource/CalDAV/ical-client et de deux traces publiques,
chacun avec sa provenance en commentaire.
EOF
```

---

### Task 9 : le rapport et les deux fichiers de résidus

**Files:**
- Create: `docs/superpowers/calendar-5d-conformance.md`
- Create: `docs/superpowers/calendar-5d-residuals.md`

**Interfaces:** aucune — deux documents, sur le plan de leurs jumeaux `carddav-4d-conformance.md` et
`calendar-5c-residuals.md`.

**Ce qui y entre est copié du fichier de `results/`, jamais de la console** : un chiffre recopié de
mémoire est un chiffre inventé.

- [ ] **Step 1 : le squelette du rapport**

```markdown
# Agenda 5d — rapport de conformité

Rapport de la tranche [5d](specs/2026-09-07-webmail-calendar-5d-conformance-design.md). Les chiffres
viennent de `tools/caldavtester/results/` (sortie épurée) ; rien ici ne se régénère, chaque passage
est recopié une fois et daté. La ligne finale de l'outil porte quatre compteurs dès qu'il y a un
échec — `FAILED (ok=, ignored=, failed=, errors=)` — et deux seulement quand il n'y en a aucun.

## 0. Repère CardDAV, après la tâche zéro

Date : — · commit : — · fichier : `results/—-carddav.txt` · `suites-carddav.txt` déjà amputé de
`CardDAV/limits.xml`. Repère de l'étape 1 : le passage final de l'étape 4 se compare à **lui**, et
non au `ok=107, failed=72` de 4d, mesuré avant que la tâche zéro ne touche au socle.

## 1. Passage initial — avant tout correctif

Date : — · commit serveur déployé : — · fichier : `results/—-caldav.txt`

| Fichier | Tests | OK | Échecs | Ignorés | Erreurs | Tué au `<start>` |
|---|---|---|---|---|---|---|
| propfind.xml | | | | | | |
| proppatch.xml | | | | | | |
| put.xml | | | | | | |
| get.xml | | | | | | |
| delete.xml (copie) | | | | | | |
| reports.xml (copie) | | | | | | |
| sync-report.xml | | | | | | |
| errors.xml | | | | | | |
| mkcalendar.xml | | | | | | |
| options.xml | | | | | | |
| nonascii.xml | | | | | | |
| well-known.xml | | | | | | |
| current-user-principal.xml | | | | | | |
| expandproperty.xml | | | | | | |
| recurrenceput.xml | | | | | | |
| floating.xml | | | | | | |
| ctag.xml | | | | | | |
| encodedURIs.xml | | | | | | |
| conditional.xml | | | | | | |
| copymove.xml | | | | | | |
| aclreports.xml | | | | | | |
| timezones.xml | | | | | | |
| ical-client.xml | | | | | | |

**Le dénominateur, à côté du total** : `reports.xml` porte 115 tests et n'en joue que 68 avec ces
features ; ses deux plus grosses suites tombent à 21 sur 42 et 19 sur 38. Un fichier à 200 tests
dont 100 sont sautés ne mesure pas deux fois plus qu'un fichier à 100.

**Rejeu du `<start>` de `reports.xml`** (`-PrintResponses`, vingt `PUT` `VEVENT`) : —

## 2. Triage

Un verdict par échec, **et par fichier tué au `<start>`**, **et par divergence sortie en « ignoré »
là où la décision 5 attendait un échec** (décision 4) : **défaut du serveur** (corrigé, test cité),
**divergence nommée** (avec ce qu'elle coûte à un client réel), **défaut de l'outil** (le *quoi*
nommé), **harnais** (corrigé, et le passage final le mesure).

| Fichier / suite / test | Constat | Verdict | Référence | Suite donnée |
|---|---|---|---|---|
| | | | | |

### Divergences prédites qui ne sont pas sorties comme prévu

La table de la décision 5 annonce ce que l'outil enverra, les `<features>` décident ce qu'il enverra
vraiment, et les deux peuvent se contredire en silence. Tout écart est lui-même un constat.

| Ligne de la table | Prédit | Observé |
|---|---|---|
| | | |

## 3. Passage final — après la vague de correctifs

Date : — · commit : — · fichier : `results/—-both.txt` · `-Protocol Both`

(mêmes colonnes que le passage initial, plus les fichiers CardDAV ; le rapport dit à chaque
comparaison lequel des deux repères CardDAV il commente)

## 4. Clients réels

Le rapport **sépare** ce qui vient de DAVx⁵ (la synchronisation) de ce qui vient d'Agenda Samsung
(l'interface) : deux logiciels, deux sources d'écart. Un scénario qu'un client ne peut pas jouer
sort en « non applicable », un statut distinct des quatre verdicts.

### Thunderbird (Windows, version —)

| # | Scénario | Résultat | Écart |
|---|---|---|---|
| 1 | Appairage par la seule adresse (hôte nu, puis adresse complète) | | |
| 2 | Découverte : deux agendas, nom et couleur | | |
| 3 | Création côté client → webmail | | |
| 4 | Création côté webmail → client | | |
| 5 | Modification des deux côtés, `If-Match`, `412` | | |
| 6 | Le cinquième cas — mêmes instants, mêmes exceptions | | |
| 7 | Rappel posé côté client, conservé au retour | | |
| 8 | Couleur et nom d'agenda (`PROPPATCH`) | non applicable | |
| 9 | Création d'un agenda depuis le client | non applicable | |
| 10 | Suppression des deux côtés | | |
| 11 | Journée entière, multi-jours, autre fuseau, flottant, +5 ans | | |
| 12 | Régénération du secret → `401`, ré-appairage | | |
| 13 | Non-régression du 7 septembre | non applicable | |

### DAVx⁵ (Android, version —) + Agenda Samsung (version —)

(mêmes lignes ; 8 « non applicable » ; 9 joué **trois fois** — tout coché, Événements seuls,
Événements + Tâches — et le rapport écrit **ce que le client affiche** dans le troisième cas ;
10 joué **deux fois**, réglage par défaut et « tous les événements »)

| Écart | Colonne | Détail |
|---|---|---|
| couleur d'événement non sauvée | Samsung | |
| `VALARM` sans `ACTION` muet | Samsung | |
| `RRULE` réparée (`UNTIL` en `DATE`) | DAVx⁵ | |
| `VTIMEZONE` régénéré | DAVx⁵ | |

## 5. Apple — non branché

`ical-client.xml` : — (six `PROPFIND`, statut seul, aucune assertion de propriété).
`AppleDiscoveryReplayTests` : — . Ce que ça prouve : la séquence ne tombe pas, et une propriété que
nous ne servons pas sort en `404` propstat. Ce que ça ne prouve pas : qu'un iPhone en fasse quelque
chose. Coût connu et non mesuré : sabre nº 935 rapporte que macOS Calendar, sur un `403` au
`PROPPATCH` de `default-alarm-vevent-date`, « stops after that ».

## 6. Non-conformités connues, portées en toutes lettres

`DavHeaders.ComplianceClasses` annonce `1, 3, addressbook, calendar-access, extended-mkcol`, et
RFC 4791 § 5.1 comme RFC 4918 § 18.1 font de ces jetons la promesse de tous les MUST de leur texte.

| Non-conformité | MUST | Ce que ça coûte à un client | Suite |
|---|---|---|---|
| `COPY` / `MOVE` en `405` | RFC 4918 § 9.8, § 9.9 | rien aux trois clients visés, tout à un outil WebDAV générique | différée, 5e |
| `limit-recurrence-set` / `limit-freebusy-set` non lus | RFC 4791 § 9.6.6, § 9.6.7 | aucun client visé ne les envoie | différée, 5e |
| `time-range` d'un `VALARM` fermé à cinq ans, `free-busy-query` qui refuse une borne absente, fenêtre à deux bornes de plus de cinq ans refusée | § 9.9, § 7.10 | aucun client visé ne l'exerce | résidu 5d |

## 7. Clôture

Secret du compte de test régénéré le — . `calendar-5c-residuals.md` mis à jour ligne à ligne :
— . `calendar-5d-residuals.md` écrit : — .
```

- [ ] **Step 2 : amorcer `calendar-5d-residuals.md`**

Sur le plan de ses trois aînés, avec les trois sections vides à remplir à la clôture — « Ce qu'un
client peut rencontrer, et qui n'est pas corrigé », « Dette de forme », « Ce que les tests n'ont pas
couvert », « Ce dont 5e hérite » — et déjà remplies, parce qu'elles ne dépendent d'aucune mesure :
les trois non-conformités du § 6 du rapport, et l'asymétrie du `remove` de
`CalendarPropertyUpdate.Judge` (seul `calendar-description` accepte l'effacement, les quatre autres
inscriptibles répondent `403`, et un `remove` d'une description jamais renseignée répond `200`) —
qu'aucun test de l'outil n'atteint.

- [ ] **Step 3 : vérifier le huitième résidu, qui ne coûte pas une ligne**

`calendar-5c-residuals.md` annonçait que l'ordre des `propstat` d'un `PROPPATCH` mixte n'était
asserté que par leur compte. Il l'est par leur **rang** depuis 5c :
`CalDavProppatchTests.TheFiveWritableOnes_Answer200AndTheRest403_InThatOrder` compare la séquence
`["HTTP/1.1 200 OK", "HTTP/1.1 403 Forbidden"]`, qu'une inversion des deux blocs rougit. La ligne
est déjà barrée dans le fichier : vérifier qu'elle l'est, ne rien implémenter.

Run: `grep -n "TheFiveWritableOnes" docs/superpowers/calendar-5c-residuals.md` Expected: la ligne
barrée (`~~…~~`) et sa correction, déjà présentes.

- [ ] **Step 4 : commit**

```bash
git add docs/superpowers/calendar-5d-conformance.md docs/superpowers/calendar-5d-residuals.md
git commit -F - <<'EOF'
docs(calendar): squelette du rapport 5d et residus de la tranche

Les tableaux attendent les passages ; rien n'y est rempli de memoire.
EOF
```

---

### Task 10 : une borne de `time-range` absente est servie ouverte

> **À exécuter à l'étape 4, avec la vague de correctifs — jamais avant le passage initial.**

**Files:**
- Modify: `src/snoopy.microservice/Services/CalDav/TimeRangeSpec.cs`
- Modify: `src/snoopy.microservice/Services/CalDav/CalendarQueryFilter.cs`
- Modify: `src/snoopy.microservice/Services/CalDav/CalendarQuerySpec.cs`
- Modify: `src/snoopy.microservice/Services/CalDav/FreeBusyReport.cs`
- Modify: `src/snoopy.microservice/Services/Calendar/OccurrenceExpander.cs`
- Test: `snoopy.microservice.Tests/Services/CalDav/CalendarQueryFilterTests.cs`,
  `snoopy.microservice.Tests/Controllers/CalDavQueryTests.cs`,
  `snoopy.microservice.Tests/Controllers/AppleDiscoveryReplayTests.cs`

**Interfaces:**
- Consumes : `CalendarQueryFilter.ParseTimeRange(XElement, bool bothRequired, XName? refusal)`,
  `TimeRangeSpec(DateTime, DateTime)`, `OccurrenceExpander.Overlaps(IcsCalendar, DateTime, DateTime,
  string)`.
- Produces :
  - `internal enum AbsentBound { Closed, Open, Refused }` dans `Services/CalDav/AbsentBound.cs` ;
  - `CalendarQueryFilter.ParseTimeRange(XElement timeRange, AbsentBound absent, XName? refusal)` —
    `bothRequired` disparaît, absorbé par `AbsentBound.Refused` ;
  - `internal sealed record TimeRangeSpec(DateTime? FromUtc, DateTime? ToUtc)` avec `internal
    (DateTime From, DateTime To) Closed => (FromUtc!.Value, ToUtc!.Value);` ;
  - `OccurrenceExpander.Overlaps(IcsCalendar parsed, DateTime? fromUtc, DateTime? toUtc, string
    calendarTimeZone)`.

**Ce que le client demande, en clair** : « tout ce qui se passe à partir du 9 juin », sans date de
fin. C'est la question que DAVx⁵ pose à chaque synchronisation avec son réglage par défaut, et celle
qu'iOS pose au premier chargement. RFC 4791 § 9.9 dit qu'une borne absente vaut l'infini ; § 7.8 que
la réponse contient **chaque** objet qui correspond. 5c refermait la borne à cinq ans en silence, et
un événement unique posé plus loin n'arrivait jamais sur le téléphone.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `CalendarQueryFilterTests.cs`, **remplacer** les deux tests qui figent la fermeture — c'est la
seule inversion d'assertion de toute la tranche :

```csharp
    [Fact]
    public void AMissingEnd_StaysMissing()
    {
        // RFC 4791 § 9.9: an absent bound is +infinity. Closing it at five years answered wrong
        // without saying so — DAVx5 asks this very question at every sync.
        var spec = CalendarQueryFilter.Parse(VEvent(TimeRange("20260907T090000Z", null)));

        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc), spec.TimeRange!.FromUtc);
        Assert.Null(spec.TimeRange.ToUtc);
    }

    [Fact]
    public void AMissingStart_StaysMissing()
    {
        var spec = CalendarQueryFilter.Parse(VEvent(TimeRange(null, "20260907T090000Z")));

        Assert.Null(spec.TimeRange!.FromUtc);
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc), spec.TimeRange.ToUtc);
    }

    [Fact]
    public void AVAlarmTimeRange_KeepsItsClosure()
    {
        // The alarm walk reads a whole window; leaving it open would reread the calendar at every
        // query. Named in the spec as a known non-conformity no targeted client exercises.
        var spec = CalendarQueryFilter.Parse(VEvent(Alarm(TimeRange("20260907T090000Z", null))));

        var from = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(from + OccurrenceExpander.MaxSpan, spec.AlarmFilters.Single().TimeRange!.ToUtc);
    }
```

`Alarm(...)` est l'aide que le fichier emploie déjà pour bâtir un `comp-filter VALARM` ; si elle
porte un autre nom, employer le sien.

Dans `CalDavQueryTests.cs` :

```csharp
    [Fact]
    public async Task ATimeRangeWithStartAlone_ServesAnEventBeyondTheFiveYearsTheEngineWalks()
    {
        // "Renouvellement du passeport", March 2032: the event that used to vanish from the phone
        // without anything saying so.
        GivenEvent("passeport.ics",
            Ics.Single("DTSTART:20320315T090000Z", "DTEND:20320315T100000Z"));

        var response = await Report(Calendar(), QueryBody(VEvent(TimeRange("20260907T000000Z", null))));

        Assert.Equal(207, response.StatusCode);
        Assert.Contains(Href("passeport.ics"), HrefsOf(response));
    }

    [Fact]
    public async Task AnEndlessSeries_MatchesAnOpenWindow_WithoutBeingWalkedToTheEnd()
    {
        GivenEvent("standup.ics", Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T093000Z",
            "RRULE:FREQ=DAILY"));

        var response = await Report(Calendar(), QueryBody(VEvent(TimeRange("20260907T000000Z", null))));

        Assert.Equal([Href("standup.ics")], HrefsOf(response));
    }
```

- [ ] **Step 2 : les faire échouer**

Run: `cd src && dotnet test --filter
"FullyQualifiedName~CalendarQueryFilterTests|FullyQualifiedName~CalDavQueryTests"` Expected: FAIL —
`spec.TimeRange.ToUtc` est fermé à `MaxSpan`, et `passeport.ics` est absent de la réponse.

- [ ] **Step 3 : la fenêtre peut avoir une borne absente**

`Services/CalDav/AbsentBound.cs`, nouveau :

```csharp
namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>What a time-range does with a bound the client did not write. RFC 4791 § 9.9 reads it
/// as an infinity; only the callers that cannot walk one close or refuse it.</summary>
internal enum AbsentBound
{
    /// <summary>Closed at <see cref="Calendar.OccurrenceExpander.MaxSpan"/> of the other — the
    /// VALARM filter, whose walk reads a whole window rather than stopping at a first hit.</summary>
    Closed,

    /// <summary>Left absent — the VEVENT filter of a calendar-query, what DAVx5 and iOS send.</summary>
    Open,

    /// <summary>Refused — free-busy-query, which composes a VFREEBUSY between two instants.</summary>
    Refused,
}
```

`Services/CalDav/TimeRangeSpec.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>A window <c>[FromUtc, ToUtc[</c> where a null bound is an infinity, as RFC 4791 § 9.9
/// reads an absent one. <see cref="Closed"/> is for the callers that demanded both.</summary>
internal sealed record TimeRangeSpec(DateTime? FromUtc, DateTime? ToUtc)
{
    internal (DateTime From, DateTime To) Closed => (FromUtc!.Value, ToUtc!.Value);
}
```

- [ ] **Step 4 : `ParseTimeRange` décide par appelant**

Dans `Services/CalDav/CalendarQueryFilter.cs` :

```csharp
    internal static TimeRangeSpec ParseTimeRange(XElement timeRange, AbsentBound absent, XName? refusal)
    {
        var start = Bound(timeRange, "start", refusal);
        var end = Bound(timeRange, "end", refusal);
        if (start is null && end is null)
            throw Refusal(refusal, "A time-range names no bound the report can close.");
        if (absent == AbsentBound.Refused && (start is null || end is null))
            throw Refusal(refusal, "A time-range names no bound the report can close.");

        var (from, to) = absent switch
        {
            AbsentBound.Open => (start, end),
            _ => (start ?? OccurrenceExpander.Shift(end!.Value, -OccurrenceExpander.MaxSpan),
                  end ?? OccurrenceExpander.Shift(start!.Value, OccurrenceExpander.MaxSpan)),
        };

        if (from is { } f && to is { } t)
        {
            if (t <= f) throw Refusal(refusal, "A time-range ends before it starts.");
            if (t - f > OccurrenceExpander.MaxSpan)
                throw Refusal(refusal,
                    $"A time-range spans more than the {OccurrenceExpander.MaxYears} years served.");
        }

        return new TimeRangeSpec(from, to);
    }
```

Les trois appelants passent leur intention explicitement — aucun paramètre optionnel, qui laisserait
un appelant sur l'ancien comportement sans le dire :
- le `time-range` du `comp-filter VEVENT` (`:206`) → `AbsentBound.Open` ;
- celui du `comp-filter VALARM` (`:225`) → `AbsentBound.Closed` ;
- `FreeBusyReport.cs:110` → `AbsentBound.Refused`, et le corps lit `range.Closed` au lieu de
  `range.FromUtc` / `range.ToUtc` sur ses trois usages.

- [ ] **Step 5 : le marcheur accepte une fenêtre ouverte**

Dans `Services/Calendar/OccurrenceExpander.cs` :

```csharp
    /// <summary>Whether one instance at least overlaps the window — the very walk of
    /// <see cref="Expand"/>, stopped at the first one found (RFC 4791 § 9.9 on a VEVENT). A null
    /// bound is that side's infinity: an endless series always answers an endless question, and the
    /// walk is lazy, so it stops at the first hit rather than at the end of time.</summary>
    internal static bool Overlaps(IcsCalendar parsed, DateTime? fromUtc, DateTime? toUtc,
        string calendarTimeZone) =>
        Over(Guid.Empty, Guid.Empty, parsed, fromUtc ?? DateTime.MinValue, toUtc ?? DateTime.MaxValue,
                calendarTimeZone, calendarTimeZone)
            .Run(firstOnly: true).Count > 0;
```

et le plafond d'occurrences se calcule sur une fenêtre bornée, pour qu'une série finie très dense
sans aucun hit ne fasse pas marcher jusqu'à la fin des temps. Dans `Expansion` :

```csharp
        // The cap grows with the window, so an open one would not bound it at all: it is computed
        // on at most MaxSpan. A no-op for every closed window, all of which are refused past that.
        private int Cap => CapFor(fromUtc, Min(toUtc, Shift(fromUtc, MaxSpan)));

        private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;
```

`Matches` (`:124`) passe désormais `range.FromUtc` et `range.ToUtc` tels quels ; la ligne `:168`,
qui sert le `VALARM`, lit `range.Closed` — sa fenêtre a toujours ses deux bornes.

- [ ] **Step 6 : la présélection suit sans effort**

`Repositories/DavCalendarReader.CandidatesAsync` accepte **déjà** une borne nulle de chaque côté :
chaque borne est appliquée sous son propre `if (… is { } …)`. `CalendarQueryReport.cs:38` passe déjà
`spec.Preselection?.FromUtc` / `?.ToUtc`, qui restent des `DateTime?`. Une série sans fin porte
`IcsProjector.NoEnd` (2100) en `LastOccurrence`, pas `start + 5 ans` : rien à changer.

`CalendarQuerySpec.Preselection` (`:35`) compose l'enveloppe des fenêtres d'alarme, toutes fermées :
`windows.Min(w => w.FromUtc)` et `windows.Max(w => w.ToUtc)` sur des `DateTime?` rendent des
`DateTime?` non nuls. Vérifier à la compilation, ne rien « corriger » de plus.

- [ ] **Step 7 : les faire passer, retirer le `Skip` du rejeu Apple, lancer tout**

Run: `cd src && dotnet test --filter
"FullyQualifiedName~CalendarQueryFilterTests|FullyQualifiedName~CalDavQueryTests"` Expected: PASS.

Retirer le `Skip` du test `calendar-query` à `start` seul d'`AppleDiscoveryReplayTests` (tâche 8,
étape 5).

Run: `cd src && dotnet test` Expected: PASS. `AWindowWiderThanTheEnginesSpan_IsMalformed` reste vert
: une fenêtre à **deux** bornes plus large que cinq ans reste refusée. C'est une incohérence assumée
et écrite (décision 11) : la requête la plus étroite est la seule refusée.

- [ ] **Step 8 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(caldav): une borne de time-range absente est servie ouverte

RFC 4791 9.9 : une borne absente vaut l'infini. Le VALARM garde sa
fermeture, free-busy-query ses deux bornes.
EOF
```

---

## Après les tâches — l'exécution interactive

Spec, « Ordre d'exécution ». Rien ici ne s'automatise ; tout se consigne.

- [ ] **Étape 1 — tâche zéro déployée, puis le repère CardDAV.** Pousser les tâches 1 à 4 (demander
  avant de pousser), attendre le déploiement sur dev, puis `pwsh tools/caldavtester/run.ps1
  -Protocol CardDAV`. Une minute et demie. C'est le repère auquel le passage final se comparera :
  sans lui, la seule comparaison possible serait celle du passage final contre un chiffre de 4d
  mesuré **avant** que la tâche zéro ne touche au socle. Recopier la ligne finale dans le § 0 du
  rapport.

- [ ] **Étape 2 — le premier passage.** D'abord le rejeu isolé du `<start>` de `reports.xml` : `pwsh
  tools/caldavtester/run.ps1 -Suites 'suites/CalDAV/reports.xml' -PrintResponses`, **avec** le
  purge, vingt requêtes, résultat consigné à côté du passage. Puis, **après quinze minutes pleines**
  (le budget d'authentification), `pwsh tools/caldavtester/run.ps1 -Protocol CalDAV`. Consigner brut
  dans le § 1 : les échecs, les ignorés, les fichiers tués au `<start>`, les fichiers sautés sur une
  trace Python, chacun compté à part. C'est la mesure de départ ; elle ne se refait pas.

- [ ] **Étape 3 — le triage.** Fichier par fichier, **sur le fichier de `results/`, jamais en lisant
  la console défiler**. Lire `errors=` et les lignes « Start items failed » **avant** les échecs :
  un fichier tué se cache dans un total qui a l'air petit. Un verdict par ligne, avec la ligne de
  RFC qui le justifie. Trier aussi ce qui est sorti en « ignoré » là où la décision 5 attendait un
  échec — l'écart est lui-même un constat.

- [ ] **Étape 4 — une seule vague, puis le passage final.** Exécuter la tâche 10 (elle en fait
  partie d'office, quel que soit le triage), plus un correctif par verdict « défaut du serveur » et
  un correctif par verdict « harnais », chacun avec son test. Pousser, attendre le déploiement, puis
  `-Protocol Both` **après quinze minutes pleines** : ce passage-là est exactement au plafond
  d'authentification. Le rapport porte les deux chiffres et dit, à chaque comparaison, lequel des
  deux repères CardDAV il commente.

- [ ] **Étape 5 — les clients.** Thunderbird d'abord, scénarios 1 à 13 (8, 9 et 13 « non applicable
  »). Puis DAVx⁵ + Agenda Samsung, 1 à 13 (8 « non applicable »). Une vague de correctifs
  **seulement si un client en impose** — la borne ouverte n'en fait pas partie, elle est déjà
  livrée. **Si cette vague touche `Services/Dav`, rejouer le passage `-Protocol Both`** : le socle
  est partagé avec le carnet et c'est le seul garde-fou. Le rapport écrit laquelle des deux a eu
  lieu. Le scénario 12 régénère le secret : remettre `serverinfo.local.json` à jour avant tout rejeu
  de l'outil.

- [ ] **Étape 6 — Apple.** Lancer `AppleDiscoveryReplayTests` et lire le résultat
  d'`ical-client.xml` du dernier passage joué. Écrire « non branché », jamais « couvert ».

- [ ] **Étape 7 — clôture.** Rapport complet, `calendar-5c-residuals.md` mis à jour ligne à ligne
  (chaque ligne que la campagne referme ou confirme), `calendar-5d-residuals.md` écrit, secret du
  compte de test régénéré.

---

## Vérification de fin de plan

- [ ] `cd src && dotnet test` : vert, et `cd src && dotnet build` sans un avertissement.
- [ ] `git diff --stat` sur `snoopy.microservice.Tests` : **aucune valeur attendue existante n'a
  changé**, hors les deux tests de `CalendarQueryFilterTests` que la tâche 10 inverse.
- [ ] `src/snoopy.microservice/ApiDocumentation.xml` n'apparaît dans aucun commit.
- [ ] `tools/caldavtester/suites-caldav.txt` compte 23 entrées, une par ligne, et le script de
  l'étape 6 de la tâche 6 ne rend aucun orphelin.
- [ ] Les deux `*.diff` des copies locales n'ont **aucune** ligne ajoutée.
- [ ] Le rapport ne contient aucun chiffre qui ne vienne d'un fichier de `results/`.
- [ ] Aucun message de commit ne commence ni ne finit par `@`.
