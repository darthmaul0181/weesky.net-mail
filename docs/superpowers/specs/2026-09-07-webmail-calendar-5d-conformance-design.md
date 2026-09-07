# Agenda 5d — la conformité clients

Quatrième et dernière tranche du projet Agenda tel qu'il est cadré, à la suite de
[5a](2026-09-04-webmail-calendar-5-overview-design.md#découpage),
[5b](2026-09-05-webmail-calendar-5b-screens-design.md) et
[5c](2026-09-06-webmail-calendar-5c-caldav-design.md).

## Où en est le projet

| | Tranche | État |
|---|---|---|
| 5a | Fondations : modèle, moteur iCalendar, stores, API | livrée |
| 5b | Les écrans de l'agenda dans le webmail | livrée |
| 5c | Serveur CalDAV : découverte, agendas multiples, cinq rapports, filtres, tombes, historique | livrée, poussée sur dev |
| **5d** | **Conformité : `ccs-caldavtester`, Thunderbird, DAVx⁵ + Agenda Samsung, Apple sans appareil** | *ce document* |

5c a écrit le serveur au RFC, et sept revues de code l'ont confronté au texte, à sabre et à
Radicale. Ce qu'aucune revue ne remplace : un outil tiers qui envoie ce que le serveur n'attend pas,
et des clients réels qui ne lisent ni les RFC ni nos specs. Le bug corrigé le 7 septembre — un
événement créé depuis un téléphone qu'on ne pouvait pas rouvrir dans le webmail — en est
l'illustration exacte : il vivait entre deux tranches, aucune revue ne le voyait, et c'est un
appareil réel qui l'a trouvé.

## Ce que fait la tranche

Quatre choses, dans cet ordre.

**Un nettoyage préalable.** Les résidus de 5c qui coûtent quelques lignes et qu'aucune mesure
n'éclairera — un fichier au mauvais endroit, un refus manquant, cinq formes HTTP sans test — sont
refermés **avant** le premier passage, pour que le chiffre de départ porte sur un serveur propre et
que la vague de correctifs qui suit ne mélange pas dette connue et défauts trouvés.

**Une mesure.** `ccs-caldavtester`, la suite de conformité de CalendarServer, tourne contre dev avec
ses suites CalDAV, et son premier résultat est consigné brut, avant tout correctif. C'est le chiffre
de départ ; tout ce qui suit se lit par rapport à lui.

**Un triage.** Chaque échec reçoit un verdict écrit, avec la ligne du RFC qui le justifie : défaut
du serveur, divergence nommée, ou défaut de l'outil. Les défauts du serveur se corrigent dans la
tranche, en une vague, et un test unitaire fige chacun. Les divergences attendues sont écrites
**ici**, avant la mesure, pour être reconnues plutôt que découvertes.

**Des clients.** Thunderbird puis DAVx⁵ + Agenda Samsung sont appairés contre dev par l'adresse de
l'onglet Sync et jouent des scénarios fixés d'avance, dont le « cinquième cas » que 5a puis 5b
renvoient depuis deux tranches. Ce qu'ils font de travers reçoit le même triage. Pour Apple, sans
appareil disponible, deux couches de rejeu — et le rapport écrit « non branché », jamais « couvert ».

L'ordre est celui que 4c puis 4d ont fixé, et pour la raison qu'elles donnaient : un défaut trouvé
par l'outil sur un serveur qui suit le RFC est un défaut du serveur ; trouvé d'abord contre un
client, il serait indiscernable d'une bizarrerie de ce client. L'outil passe donc avant les clients,
et une décision de 5c ne se rouvre que devant un client réel, jamais devant l'outil seul.

## Décisions

### 1. Le harnais de 4d est réutilisé, pas dupliqué

`tools/caldavtester/` existe depuis 4d : il clone `ccs-caldavtester` à
`bed21e5924275552c1561febc8203a9f194cf737` et `ccs-pycalendar` à
`a12dd4e1ce8822b022d4abf2cfe6cc93902ff03f` (les derniers commits de deux dépôts archivés), installe
`pycalendar` dans un virtualenv local, engendre `serverinfo.xml` depuis un gabarit versionné et
trois valeurs d'un fichier ignoré, lance `testcaldav.py` et épure sa sortie dans
`results/<horodatage>.txt`.

Rien de tout cela ne change. Ce qui change tient en trois points :

- `suites.txt` devient `suites-carddav.txt`, et `suites-caldav.txt` apparaît à côté.
- `run.ps1` gagne `-Protocol CalDAV|CardDAV|Both` (défaut `CalDAV`), qui choisit le fichier de
  suites, et `-Purge` (décision 9).
- `serverinfo.template.xml` est repointé et ses `<features>` révisées (« Le harnais »).

Un second harnais aurait dupliqué le clonage, le virtualenv, l'épuration et le README pour un
fichier de suites et huit substitutions. La règle « pas de doublon » vaut aussi pour l'outillage.

### 2. La cible est dev, avec le compte de 4d

L'outil tourne contre `https://api-dev.mail.weesky.net`, pas contre un Kestrel local, pour les deux
raisons de 4d décision 2 : c'est le serveur que les clients réels verront ensuite, et c'est la seule
façon de savoir ce qu'une chaîne TLS et un proxy font de `PROPFIND`, `REPORT`, `PROPPATCH`,
`MKCALENDAR`, `MKCOL` et `DELETE`. Le prix — un push et un déploiement par vague de correctifs —
est accepté ; la boucle courte reste celle des tests unitaires, qui figent chaque correctif avant
qu'il ne parte.

**Le compte est celui que 4d a créé**, avec CalDAV allumé en plus de CardDAV. Il ne sert à rien
d'autre, et c'est plus vrai ici que pour le carnet : les blocs `<start>` vident le home, et un
`DELETE` sur un agenda secondaire ne le vide pas — il le **supprime** (5c décision 11). Le secret
est régénéré à la clôture de la campagne.

L'utilisateur d'administration que l'outil demande (`$useradmin:`) est ce même compte : un agenda à
un seul propriétaire n'a pas d'administrateur distinct. Le second utilisateur (`$userid2:`) n'est
pas défini ; les suites qui l'exercent (partage, ACL entre principaux, ordonnancement) sont exclues
ou attendues en échec nommé.

### 3. La tâche zéro : les résidus 5c bon marché, avant la mesure

Cinq points, tous relevés par les revues de 5c et écrits dans `calendar-5c-residuals.md`, corrigés
en une tâche mécanique poussée et déployée **avant** le premier passage :

1. **`SyncState` quitte `Models/Contacts` pour `Models/Dav`**, à côté de `DavWriteStatus` et
   `DavWriteOutcome` déjà migrés. Trois fichiers du socle partagé l'importent, et depuis que 5c a
   rendu `ICalendarSyncStore` publique, il traverse une **API publique** sous un nom qui ment.
2. **Un `supported-calendar-component-set` vide refuse la création** au lieu de la laisser passer :
   la boucle actuelle ne refuse que les `comp` nommant autre chose que `VEVENT`, et zéro `comp` ne
   nomme rien. Refus en `403 CALDAV:supported-calendar-component-set`, comme le composant inconnu.
3. **Les cinq formes HTTP sans test** reçoivent le leur : `PROPFIND Depth: 0` sur `/dav/calendars/`
   et sur le home, le `405 + Allow` de la forme événement, le `308` éprouvé sur `PROPFIND` seul, le
   `supported-report-set` de l'événement.
4. **Les deux constantes `Margin`** (`OccurrenceExpander.Margin`, `CalendarEventStore.Margin`) n'en
   font plus qu'une : la justesse d'une réponse dépend de leur égalité, et `Shift`, `CapFor` et
   `Span` ont chacune été unifiées en un point durant 5c — celles-ci ne l'ont pas été.
5. **L'ordre des `propstat` d'un `PROPPATCH` mixte** (200 avant 403) est asserté par rang, plus par
   compte : aujourd'hui, inverser les deux blocs laisserait la suite verte.

Rien d'autre. Les résidus qui coûtent une décision — le `param-filter` négatif qui correspond sur un
paramètre absent, la présélection d'une ressource à plusieurs surcharges sans maître, `calendar-data`
servie dans un `PROPFIND` — attendent le verdict de l'outil ou d'un client. Les refermer d'avance
serait décider sans mesure, ce que cette tranche existe précisément pour éviter.

### 4. Trois verdicts, et la ligne du RFC pour chacun

Chaque test en échec — de l'outil comme d'un client — reçoit exactement un verdict, écrit dans le
rapport avec la ligne du RFC qui le justifie :

| Verdict | Ce qu'il engage |
|---|---|
| **Défaut du serveur** | Corrigé dans la tranche, dans la vague unique. Un test unitaire fige la correction, et le rapport cite ce test à côté du verdict. |
| **Divergence nommée** | Écrite dans le rapport avec ce qu'elle coûte à un client réel, et ajoutée à `calendar-5c-residuals.md` si elle n'y est pas. Une divergence sans coût écrit est une divergence non instruite. |
| **Défaut de l'outil** | Python 2, propriété CalendarServer, `<start>` qui saute, second utilisateur non défini. Consigné, la suite comptée sautée. Ce n'est jamais un verdict par défaut : il faut pouvoir dire *quoi* dans l'outil. |

**Une seule vague** de correctifs serveur entre le passage initial et le passage final. Mesurer,
corriger, remesurer une fois : c'est ce qui rend le chiffre lisible, et c'est la règle de process du
projet.

### 5. Les divergences attendues sont écrites avant le premier passage

Ce que 5c a assumé et que l'outil va nécessairement refuser. L'écrire ici plutôt que de le découvrir
dans une sortie de 4000 lignes est la différence entre un triage et une fouille :

| Ce que l'outil refusera | Où | Pourquoi c'est assumé |
|---|---|---|
| `reports.xml` / `filtereddata` : `comp` et `prop` dans `calendar-data` | RFC 4791 § 9.6.1 | Lus puis ignorés ; la ressource entière sort (spec 5c § 7). Aucun client visé ne les envoie. |
| `reports.xml` / `limitexpand` : `limit-recurrence-set`, `limit-freebusy-set` | § 9.6.6, § 9.6.7 | Lus nulle part. |
| Tout test `VTODO` (`reports.xml` en porte plusieurs) | § 4.2 | Un agenda ne sert que `VEVENT` (`supported-calendar-component-set`, 5a). |
| `sync-report.xml` sur `$calendarhome1:/` | RFC 6578 § 3 | Le home ne sert pas `REPORT` : la surface 5c ne l'ouvre que sur l'agenda et l'événement. Le RFC ne l'impose pas, et aucun client visé ne synchronise le home — DAVx⁵ et iOS synchronisent agenda par agenda. |
| `copymove.xml` | RFC 4918 § 9.8, § 9.9 | `COPY`/`MOVE` non servis : `405`, `Allow` le dit. |
| `aclreports.xml` | RFC 3744 | Pas d'ACL, et `access-control` n'est pas annoncé dans l'en-tête `DAV:`. |
| Le corps d'un refus de `MKCOL` étendu | RFC 5689 § 3 | `403` + `<D:error>` là où l'exemple du RFC montre un `DAV:mkcol-response` nommant la propriété refusée. Le statut est juste ; c'est la forme du corps que 5c § 11 a tranchée autrement, sciemment. À revisiter seulement si l'outil s'en plaint — ce qui est justement ce que ce passage mesure. |
| Une borne de `time-range` ou d'`expand` absente, fermée à cinq ans | § 9.9 | `OccurrenceExpander.MaxSpan`. Refuser était l'autre choix ; celui-ci est écrit. |
| `test="anyof"` sur un `prop-filter` | § 9.7.2 | Refusé (`valid-filter`) : ce moteur ne l'évalue pas exactement, et rendre un surensemble sans le dire serait pire. |

Une divergence de cette table qui **passe** est aussi un résultat : elle se consigne, et la ligne
correspondante de `calendar-5c-residuals.md` se referme.

### 6. Le rapport garde la mesure brute ; le secret ne le traverse pas

Le rapport est `docs/superpowers/calendar-5d-conformance.md`, sur le plan de son jumeau
`carddav-4d-conformance.md`. Ce qui y entre est copié du fichier de `results/`, jamais de la
console : un chiffre recopié de mémoire est un chiffre inventé. Les totaux y sont bruts — tant de
tests, tant d'échecs, tant d'ignorés, tant de fichiers sautés —, et la distinction entre « ignoré »
(un test conditionné à une `<feature>` éteinte) et « échoué » est préservée partout, parce que c'est
elle qui dit si le serveur a été mesuré ou contourné.

`run.ps1` épure les en-têtes `Authorization` avant que le fichier ne touche le disque, et
`serverinfo.xml` comme `serverinfo.local.json` restent ignorés. Le secret DAV du compte de test
n'apparaît ni dans le dépôt, ni dans `results/`, ni dans le rapport, et il est régénéré à la
clôture.

### 7. Les clients réels : Thunderbird et DAVx⁵ + Agenda Samsung, scénarios fixés d'avance

Thunderbird d'abord : son client CalDAV est celui de Mozilla, le plus bavard des trois, et il fait
tourner la découverte, le `PROPPATCH` de la couleur et le `sync-collection` dans une seule session.
DAVx⁵ + Agenda Samsung ensuite, et le rapport **sépare** ce qui vient de DAVx⁵ (la synchronisation)
de ce qui vient de Samsung (l'interface) : ce sont deux logiciels distincts, donc deux sources
d'écart, et le bug du 7 septembre est venu de leur couple.

Les treize scénarios, joués dans cet ordre pour chacun :

1. **Appairage par la seule adresse** de l'onglet Sync. Pas de `SRV`, pas de `.well-known` DNS sur
   `mail.weesky.net` : si ça ne suffit pas, c'est un défaut serveur, pas une raison d'ajouter de la
   configuration DNS.
2. **Découverte** : les deux agendas apparaissent, avec le nom et la couleur du webmail.
3. **Création côté client** → visible dans le webmail, fichier conservé verbatim.
4. **Création côté webmail** → visible côté client.
5. **Modification des deux côtés**, dont deux appareils sur la même ressource : `If-Match`, `412`.
6. **Le cinquième cas** — un récurrent écrit par le webmail avec un « cette occurrence seulement »,
   relu par Thunderbird **et** par le téléphone : mêmes heures, mêmes exceptions, même bloc de
   fuseau. Puis l'inverse, écrit par le client et relu par le webmail. C'est la procédure que 5a
   puis 5b renvoient depuis deux tranches, et que 5c a rendue possible sans la jouer.
7. **Rappel** posé côté client → conservé au retour dans le webmail (le chemin `foreignAlarms`).
8. **Couleur et ordre d'agenda** changés côté client (`PROPPATCH`) → visibles dans le webmail.
9. **Création d'un agenda depuis le client** — `MKCALENDAR` pour Thunderbird, `MKCOL` étendu pour
   DAVx⁵ — puis sa suppression, qui le fait disparaître là où `default` se vide.
10. **Suppression** des deux côtés → l'autre appareil la voit au poll suivant sous forme de tombe,
    jamais d'un `403 valid-sync-token`.
11. **Journée entière, événement sur plusieurs jours, fuseau différent de celui de l'agenda, et un
    événement flottant.**
12. **Régénération du secret** → `401`, puis ré-appairage.
13. **Non-régression du 7 septembre** : un événement créé sur le téléphone s'ouvre à l'édition dans
    le webmail.

C'est le seul endroit de la tranche où une décision de 5c peut se rouvrir. Un client qui exige une
forme que le RFC n'impose pas ouvre un arbitrage écrit dans le rapport, pas un correctif silencieux.

### 8. Apple sans appareil : `ical-client.xml`, puis un rejeu de traces in-process

Aucun iPhone ni Mac n'est disponible pour la campagne. La troisième voie du cadrage — le rejeu de
traces — est retenue, en deux couches dont le rapport dira ce que chacune prouve.

**La première est déjà dans la liste des suites.** `ical-client.xml` est le rejeu, par Apple, de ce
que fait son propre client : un tiers qui envoie ce que nous n'attendons pas, plutôt que nous qui
devinons. Elle tourne au même titre que les autres et son résultat est compté avec elles.

**La seconde entre dans le dépôt** : `AppleDiscoveryReplayTests`, en tests d'intégration in-process
sur `DavTestServer`, rejoue verbatim la séquence d'appairage d'iOS —

- `GET /.well-known/caldav` → `301`,
- `PROPFIND Depth: 0` sur la racine pour `current-user-principal`,
- `PROPFIND Depth: 0` sur le principal pour `calendar-home-set`, `calendar-user-address-set`,
  `principal-collection-set`, `displayname`, `supported-report-set`,
- `PROPFIND Depth: 1` sur le home pour le jeu complet qu'iOS demande : `resourcetype`,
  `displayname`, `calendar-color`, `calendar-order`, `supported-calendar-component-set`,
  `sync-token`, `getctag`, `current-user-privilege-set`, `calendar-description`,
  `calendar-timezone`, `default-alarm-vevent-datetime`, `quota-available-bytes`,
  `quota-used-bytes`, `owner`,
- `REPORT sync-collection` sur un agenda, puis `calendar-multiget` sur ce qu'il rend,
- `calendar-query` avec un `time-range` sur un `comp-filter VALARM`,
- `PROPPATCH` de `calendar-color` et de `calendar-order`.

Chaque corps porte en commentaire **d'où il vient** — sabre, `ccs-caldavtester`, la documentation
d'Apple. Un corps sans provenance est une supposition déguisée en test, et c'est exactement ce que
cette couche existe pour ne pas être.

Ce qu'elle prouve : que nos réponses ne font pas tomber la séquence, et qu'une propriété qu'iOS
demande mais que nous ne servons pas (`getctag`, `quota-*`, `default-alarm-vevent-datetime`) sort en
`404` propstat plutôt qu'en `500` ou en silence. Ce qu'elle ne prouve pas : qu'un iPhone en fasse
quelque chose. Le rapport l'écrit en ces termes, comme 4d l'a fait pour Contacts.app.

### 9. Le plafond de vingt agendas : `-Purge` avant chaque campagne

`CalendarStore.MaxPerUser` vaut 20. Les suites créent des agendas — `synccalendar1`,
`synccalendar2`, ceux de `mkcalendar.xml` et de `propfind.xml` — et les détruisent dans leur bloc
`<end>`. Mais un `<end>` qui saute les laisse, et trois campagnes suffisent alors à remplir le
compte : tout `MKCALENDAR` répond `507`, et un passage entier ment sans qu'on sache pourquoi.

`run.ps1 -Purge` fait un `PROPFIND Depth: 1` sur le home et `DELETE` chaque agenda sauf `default`
avant de lancer. C'est du nettoyage d'environnement de test, pas une fonctionnalité serveur : rien
n'est ajouté à l'API pour le servir, il n'utilise que la surface que 5c ouvre déjà.

## Le harnais

`serverinfo.template.xml` porte déjà toutes les clés agenda — c'est le gabarit amont — mais avec les
valeurs de CalendarServer. Elles sont repointées :

| Clé | Valeur 5d |
|---|---|
| `$calendars:` | `/dav/calendars/` |
| `$calendarhome1:` | `/dav/calendars/{guid}` |
| `$calendar:` | `default` |
| `$calendarpath1:` | `/dav/calendars/{guid}/default` |
| `$email1:` | `{email}` |
| `$cuaddr1:` | `mailto:{email}` |
| `$calendar_home_items_initial_sync:` | `[]` |
| `$calendar_sync_extra_items:` | `[]` |
| `$calendar_sync_extra_count:` | `1` (la ressource de la Request-URI, rendue quand aucun jeton n'est passé) |

`$email1:` et `$cuaddr1:` corrigent un défaut hérité du gabarit amont, qui vaut
`$userid1:@example.com` : notre `$userid1:` **est** déjà l'adresse, et la valeur amont donnait
`a@b.tld@example.com`. Sans ça, aucune vérification de `calendar-user-address-set` ne peut passer.

Sont **retirées** : `$tasks:`, `$polls:`, `$inbox:`, `$outbox:`, `$dropbox:`, `$notification:`,
`$freebusy:`, `$timezoneservice:`, `$timezonestdservice:`, `$directory:`, `$add-member:`. Aucune
suite retenue ne les touche, et une clé laissée là ferait passer un échec pour du bruit.

**Les `<features>` allumées** : `caldav`, `sync-report`, `well-known`, `current-user-principal`,
`expand-property`, `Extended MKCOL`, `no-duplicate-uids`, `supported-component-sets-one`. Les
CardDAV de 4d (`carddav`, `limits`) restent, le fichier servant les deux protocoles.

Éteintes, chacune avec sa raison en commentaire dans le fichier : `sync-report-home` (le home ne
sert pas `REPORT`), `regular-collection` (un `MKCOL` nu dans un home d'agendas est refusé, 5c
décision 11), `ctag`, `quota`, `prefer`, `brief`, `resource-id`, `json-data`, `add-member`,
`auth-on-root`, `own-root`, `timezones-by-reference`, `timezone-service`, `query-extended`,
`timerange-low-limit`, `timerange-high-limit`, `COPY Method`, `MOVE Method`, `ACL Method` et tous
les `REPORT` de principaux, plus le bloc entier de l'ordonnancement, du partage, des pièces jointes
gérées et de `vpoll` — c'est 5e ou ce n'est pas ce projet.

**`suites-caldav.txt`**, lancées :

```
CalDAV/propfind.xml          CalDAV/proppatch.xml       CalDAV/put.xml
CalDAV/get.xml               CalDAV/delete.xml          CalDAV/reports.xml
CalDAV/sync-report.xml       CalDAV/errors.xml          CalDAV/mkcalendar.xml
CalDAV/options.xml           CalDAV/nonascii.xml        CalDAV/well-known.xml
CalDAV/current-user-principal.xml                       CalDAV/expandproperty.xml
CalDAV/recurrenceput.xml     CalDAV/floating.xml        CalDAV/duplicate_uids.xml
CalDAV/bad-ical.xml          CalDAV/encodedURIs.xml     CalDAV/conditional.xml
CalDAV/copymove.xml          CalDAV/aclreports.xml      CalDAV/timezones.xml
CalDAV/caldavIOP.xml         CalDAV/ical-client.xml
```

`copymove.xml` et `aclreports.xml` tournent **en sachant** qu'elles échoueront, comme `mkcol.xml` en
4d : leur échec est la mesure d'une divergence nommée, et un fichier qu'on ne lance pas ne mesure
rien.

Exclues, avec leur raison en commentaire : tout `implicit*` et `schedule*` (ordonnancement, 5e),
`freebusy.xml` (c'est un `POST` sur l'outbox, pas le rapport `free-busy-query`), `sharing-*`,
`managed-attachments*`, `polls.xml`, `partitioning-*`, `dropbox.xml`, `trash*`, `json.xml`,
`rscale.xml`, `vtodos.xml`, `timezoneservice.xml`, `timezonestdservice.xml`, `webcal.xml`,
`depthreports*.xml` (exige `regular-collection`), `directory*.xml`, `bulk.xml`, `add-member.xml`,
`quota.xml`, `prefer.xml`, `brief.xml`, `ctag.xml`, `resourceid.xml`, `attachments.xml`,
`availability.xml`, `extended-freebusy.xml`, `freebusy-url.xml`, `default-alarms.xml`,
`alarm-dismissal.xml`, `privateevents.xml`, `privatecomments.xml`.

`duplicate_uids.xml` est en revanche **retenue**, et sa `<feature>` allumée : l'index unique
`(calendar_id, uid)` de 5a fait de nous un serveur qui refuse deux fois le même UID dans un agenda,
ce que la suite mesure exactement.

Le `free-busy-query` de 5c § 9 n'a **aucune suite dédiée** dans l'outil : `freebusy.xml` teste le
`POST` d'ordonnancement, et le rapport en tant que tel n'apparaît que dans `aclreports.xml` et
`schedulepost.xml`, toutes deux hors périmètre. Il reste donc couvert par les seuls tests unitaires
de 5c, et le rapport le dit — c'est une zone mesurée par nous seuls, et ça doit se voir.

## Fichiers

**Tâche zéro (décision 3)** :

- `Models/Contacts/SyncState.cs` → `Models/Dav/SyncState.cs`, et les `using` des fichiers qui
  l'importent, des deux protocoles.
- `Services/CalDav/MkCalendarRequest.cs` — un `supported-calendar-component-set` sans aucun `comp`
  est refusé.
- `Services/Calendar/OccurrenceExpander.cs` / `Repositories/CalendarEventStore.cs` — une seule `Margin`,
  `internal`, avec le commentaire qui dit que les deux marcheurs en dépendent solidairement.
- Tests : les cinq formes HTTP, l'ordre des `propstat`, le composant vide.

**Harnais** :

- `tools/caldavtester/run.ps1` — `-Protocol`, `-Purge`.
- `tools/caldavtester/serverinfo.template.xml` — substitutions et `<features>`.
- `tools/caldavtester/suites-caldav.txt` — nouveau ; `suites.txt` renommé en `suites-carddav.txt`.
- `tools/caldavtester/README.md` — le mode d'emploi CalDAV à côté du CardDAV, et l'avertissement sur
  le `DELETE` d'agenda.

**Rejeu Apple** :

- `snoopy.microservice.Tests/Controllers/AppleDiscoveryReplayTests.cs` — nouveau.

**Correctifs du triage** : le fichier que chaque verdict désigne, avec son test. Nommés dans le plan
au fur et à mesure, pas ici : les nommer d'avance serait prédire la mesure.

**Documentation** :

- `docs/superpowers/calendar-5d-conformance.md` — le rapport.
- `docs/superpowers/calendar-5c-residuals.md` — chaque ligne que la campagne referme ou confirme.
- `docs/superpowers/calendar-5d-residuals.md` — le tri de fin de tranche, comme ses trois aînés.

## Tests

**La tâche zéro**, figée par mutation : chacun des cinq points a un test qui rougit quand la garde
saute. Le déplacement de `SyncState` n'en a pas — c'est un déplacement de fichier, et la compilation
est sa preuve.

**Chaque correctif du triage** : un test qui rougit quand la garde saute, dans la classe qui couvre
déjà la forme concernée. Le rapport cite le test à côté du verdict. C'est la règle de 4d, et c'est
ce qui empêche un correctif de conformité de repartir à la tranche suivante.

**Le rejeu Apple** : chaque requête de la séquence a son test, et chacun assert sur ce qui compte —
le statut, la présence de la propriété attendue dans le bon `propstat`, et le `404` propstat pour
celles que nous ne servons pas. Un test qui vérifie seulement « ça n'a pas fait 500 » ne serait pas
un test.

**Le harnais lui-même** n'a pas de test : c'est un script de lancement, et sa preuve est le passage
initial consigné. `-Purge` fait exception dans un sens : il utilise la surface DAV que les tests de
5c couvrent déjà, donc rien de neuf à figer.

## Ordre d'exécution

1. **Tâche zéro** (décision 3), tests, push, déploiement sur dev.
2. **Harnais** : `-Protocol`, `-Purge`, substitutions, `<features>`, `suites-caldav.txt`, README.
   **Premier passage**, consigné brut — y compris les fichiers qui sautent : c'est la mesure de
   départ.
3. **Triage** de tout ce qui échoue, verdict par verdict, contre la table de la décision 5.
4. **Une seule vague** de correctifs serveur, chacun avec son test ; push, déploiement, **passage
   final** en `-Protocol Both` — le socle est partagé avec le carnet, et le rapport porte les deux
   chiffres.
5. **Thunderbird**, scénarios 1 à 13. Puis **DAVx⁵ + Agenda Samsung**, 1 à 13. Triage, et une vague
   de correctifs seulement si un client en impose.
6. **Rejeu Apple** : `AppleDiscoveryReplayTests`, et lecture du résultat d'`ical-client.xml` du
   passage final.
7. **Clôture** : rapport, `calendar-5c-residuals.md` mis à jour ligne à ligne,
   `calendar-5d-residuals.md` écrit, secret du compte de test régénéré.

## Ce que la tranche ne fait pas

- **Aucun ordonnancement.** Invitations, `POST` sur l'outbox, `implicit-scheduling`, `freebusy.xml`,
  `schedule-*` : c'est 5e, et elle n'est pas conçue. `calendar-user-address-set` reste servie sans
  que rien ne soit ordonnancé, et l'absence de `calendar-auto-schedule` dans l'en-tête `DAV:` le dit.
- **Ni `VTODO` ni `VJOURNAL`.** Un agenda ne sert que `VEVENT`.
- **Pas de mesure de charge.** Les cinq mille événements que 5c nommait sont un autre outil et une
  autre tranche.
- **Pas de portage de l'outil.** Ce qui ne s'exécute pas sous Python 2.7.18 tel quel est un défaut
  de l'outil (décision 4), consigné, la suite comptée sautée.
- **Pas de `SRV` ni de `.well-known` DNS sur `mail.weesky.net`.** L'adresse à saisir reste celle de
  l'API.
- **Pas d'appareil Apple.** Décision 8 : deux couches de rejeu, et le rapport écrit « non branché ».
- **Ni partage, ni `webcal`, ni pièce jointe gérée, ni `vpoll`.**
- **Pas de nouvelle fonctionnalité serveur** en dehors de ce qu'un verdict « défaut du serveur »
  impose. Une suite qui échoue parce que nous n'avons pas une extension CalendarServer n'est pas une
  raison de l'écrire.

## Risques

- **Le plafond de vingt agendas.** Décision 9. Sans `-Purge`, trois campagnes suffisent à remplir le
  compte et à faire mentir un passage entier.
- **Le socle est partagé avec le carnet.** Un correctif dans `Services/Dav` touche CardDAV, et un
  test vert côté agenda ne le dit pas. Le passage final relance les deux protocoles ; c'est le seul
  garde-fou qui vaille, et il est dans l'ordre d'exécution.
- **La sortie de `reports.xml` est massive** — plusieurs centaines de tests dans un fichier. Le
  triage doit se faire fichier par fichier, sur le fichier de `results/`, jamais en lisant la
  console défiler.
- **L'outil plante sur ce qu'il ne sait pas lire.** Une réponse XML qu'il n'attend pas peut faire
  sauter un fichier entier avec une trace Python plutôt qu'un échec de test. Ça se lit « fichier
  sauté », se consigne comme tel, et se rejoue avec `-PrintResponses`.
- **TLS**, risque nº 1 de 4d, est levé : la même chaîne a déjà parlé au Python 2.7.18 en août.
- **Le compte de test n'est pas un compte personnel.** Répété ici parce qu'un `DELETE` d'agenda
  secondaire ne le vide pas — il le supprime.
