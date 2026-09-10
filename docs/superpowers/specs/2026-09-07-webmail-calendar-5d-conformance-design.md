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
n'éclairera — un fichier au mauvais endroit, un refus manquant, cinq formes HTTP sans test, un
en-tête que le RFC exige et qu'aucune suite ne vérifie — sont refermés **avant** le premier
passage, pour que le chiffre de départ porte sur un serveur propre et que la vague de correctifs
qui suit ne mélange pas dette connue et défauts trouvés.

**Une mesure.** `ccs-caldavtester`, la suite de conformité de CalendarServer, tourne contre dev avec
ses suites CalDAV, et son premier résultat est consigné brut, avant tout correctif. C'est le chiffre
de départ ; tout ce qui suit se lit par rapport à lui.

**Un triage.** Chaque échec reçoit un verdict écrit, avec la ligne du RFC qui le justifie : défaut
du serveur, divergence nommée, ou défaut de l'outil. Les défauts du serveur se corrigent dans la
tranche, en une vague, et un test unitaire fige chacun. Les divergences attendues sont écrites
**ici**, avant la mesure, pour être reconnues plutôt que découvertes — et chacune annonce laquelle des cinq
issues l'outil lui donnera, parce qu'une suite sautée n'a rien mesuré du tout.

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
`a12dd4e1ce8822b022d4abf2cfe6cc93902ff03f` (les derniers commits de deux dépôts archivés), rend
`pycalendar` importable par `PYTHONPATH` — la spec 4d disait « virtualenv », il n'y en a jamais eu —,
engendre `serverinfo.xml` depuis un gabarit versionné et
trois valeurs d'un fichier ignoré, lance `testcaldav.py` et épure sa sortie dans
`results/<horodatage>.txt`.

Rien de tout cela ne change. Ce qui change tient en quatre points :

- `suites.txt` devient `suites-carddav.txt` — moins `CardDAV/limits.xml` (« Le harnais ») —, et
  `suites-caldav.txt` apparaît à côté.
- `run.ps1` gagne `-Protocol CalDAV|CardDAV|Both` (défaut `CalDAV`), qui choisit le fichier de
  suites, et `-NoPurge` — le nettoyage d'environnement étant fait par défaut (décision 9).
- `serverinfo.template.xml` est repointé et ses `<features>` révisées (« Le harnais »).
- `suites/CalDAV/` reçoit deux copies locales de fichiers amont (décision 10).

Un second harnais aurait dupliqué le clonage, le virtualenv, l'épuration et le README pour un
fichier de suites et dix substitutions. La règle « pas de doublon » vaut aussi pour l'outillage.

### 2. La cible est dev, avec le compte de 4d

L'outil tourne contre `https://api-dev.mail.weesky.net`, pas contre un Kestrel local, pour les deux
raisons de 4d décision 2 : c'est le serveur que les clients réels verront ensuite, et c'est la seule
façon de savoir ce qu'une chaîne TLS et un proxy font de `PROPFIND`, `REPORT`, `PROPPATCH`,
`MKCALENDAR`, `MKCOL` et `DELETE`. Le prix — un push et un déploiement par vague de correctifs —
est accepté ; la boucle courte reste celle des tests unitaires, qui figent chaque correctif avant
qu'il ne parte.

**Le compte est celui que 4d a créé**, avec CalDAV allumé en plus de CardDAV. Il ne sert à rien
d'autre, et c'est plus vrai ici que pour le carnet : les suites créent et suppriment des agendas
à leur guise (`end-delete`, décision 9), et un `DELETE` sur un agenda secondaire ne le vide pas —
il le **supprime** (5c décision 11). Le secret est régénéré à la clôture de la campagne.

L'utilisateur d'administration que l'outil demande (`$useradmin:`) est ce même compte : un agenda à
un seul propriétaire n'a pas d'administrateur distinct. Le second et le troisième utilisateur
(`$userid2:`, `$userid3:`) ne sont pas définis ; les suites qui les exercent sont soit exclues —
`caldavIOP.xml`, tout le partage, tout l'ordonnancement —, soit retenues avec **le bruit multi-utilisateurs
nommé fichier par fichier dans `suites-caldav.txt`** (« Le harnais »). « Bruité » n'est pas un
verdict : ces tests ne comptent ni pour ni contre le serveur, et le rapport les déduit du total
qu'il commente.

Ce bruit-là n'est pas gratuit, et c'est le point que 4d n'avait pas eu à voir. `$userid2:` ne sert
pas qu'à composer une URL : il sert aussi d'**identifiant**, dans `<request user="$userid2:"
pswd="$pswd2:">`, et une clé absente reste en texte littéral (« Le harnais »). Chacune de ces
requêtes est donc un échec d'authentification Basic, facturé au plafond d'`api-dev` — neuf par
passage CalDAV, dix en `-Protocol Both`, pour un plafond de dix. C'est le risque « le compte de test
peut se bloquer », et il se compte plutôt qu'il ne s'estime.

### 3. La tâche zéro : les résidus 5c bon marché, avant la mesure

Sept points corrigés en une tâche mécanique, poussée et déployée **avant** le premier passage.
Les quatre premiers viennent des revues de 5c et sont écrits dans `calendar-5c-residuals.md` ; les
trois derniers viennent des lectures faites pour cette spec — celle des `<start>` de l'outil pour le
cinquième, celle des textes de RFC 4791 § 5.3.1, RFC 4918 § 9.3 et RFC 5689 § 3 pour les deux
derniers — et ils sont ici parce qu'ils coûtent deux lignes chacun, et que le cinquième conditionne
ce que huit fichiers mesureront :

1. **`SyncState` quitte `Models/Contacts` pour `Models/Dav`**, à côté de `DavWriteStatus` et
   `DavWriteOutcome` déjà migrés. Trois fichiers du socle partagé l'importent, et depuis que 5c a
   rendu `ICalendarSyncStore` publique, il traverse une **API publique** sous un nom qui ment.
2. **Un `supported-calendar-component-set` vide refuse la création** au lieu de la laisser passer :
   la boucle actuelle ne refuse que les `comp` nommant autre chose que `VEVENT`, et zéro `comp` ne
   nomme rien. Même réponse que le composant inconnu : un `propstat 403` sur cette seule propriété,
   sous `CALDAV:mkcalendar-response` (`207`) ou `DAV:mkcol-response` (`403`) selon le verbe
   (5c § 11).
3. **Les cinq formes HTTP sans test** reçoivent le leur : `PROPFIND Depth: 0` sur `/dav/calendars/`
   et sur le home — la suite n'y va qu'en `Depth: 1` —, le `405 + Allow` de la forme **événement**
   (celui de l'agenda est couvert, pas le sien), le `308` sur un verbe **autre** que `PROPFIND`,
   seul éprouvé aujourd'hui, et le **contenu** du `supported-report-set` de l'événement, dont seule
   la présence est assertée là où l'agenda et le home ont chacun leur liste figée.
4. **Les deux constantes `Margin`** (`OccurrenceExpander.Margin`, `CalendarEventStore.Margin`) n'en
   font plus qu'une : la justesse d'une réponse dépend de leur égalité, et `Shift`, `CapFor` et
   `Span` ont chacune été unifiées en un point durant 5c — celles-ci ne l'ont pas été. Même
   traitement pour les « cinq ans » : `OccurrenceExpander.MaxSpan` vaut 366 × `MaxYears` jours
   et borne les fenêtres client ; `CalendarEventStore.Nearest` cherche la prochaine occurrence
   jusqu'à 365 × `MaxYears`. Les deux s'alignent sur `MaxSpan`, ce qui déplace l'horizon de
   recherche de cinq jours, sans effet visible. Une **troisième** écriture existe et ne bouge
   pas : `CalendarEventsController.MaxWindow` vaut 365,2425 × `MaxYears` et borne la fenêtre de
   l'API du webmail, pas une réponse DAV — l'aligner élargirait cette borne de quatre jours et
   ferait passer `CalendarEventsControllerTests.Window_RefusesMoreThanFiveYears` de `400` à `200`.
   Trois écritures, deux surfaces : les deux du moteur s'unifient, la troisième est nommée ici
   pour qu'on ne la « corrige » pas.
5. **Les TZID hérités que l'outil écrit dans ses `<start>` résolvent** : `US/Eastern`,
   `US/Mountain`, `US/Pacific`, `GMT` — des **alias** TZDB, pas des identifiants canoniques, que
   NodaTime rend mais qu'aucun test ne fige. Huit fichiers retenus en dépendent (`reports.xml`,
   `copymove.xml`, `sync-report.xml`, `errors.xml`, `ctag.xml`, `floating.xml`, `get.xml`,
   `delete.xml`), et de deux façons : `floating.xml` **meurt à son `<start>`** si `US/Eastern` ne
   résout pas, puisque c'est un `calendar-timezone` que `CalendarPropertyValue.Zone` refuse en
   bloc ; les sept autres survivent — le `PUT` n'a pas de garde de fuseau et le marcheur retombe
   sur le `VTIMEZONE` du fichier —, mais chaque instant y serait lu dans la mauvaise zone et des
   dizaines de vérifications de `time-range` et d'`expand` échoueraient pour une raison étrangère
   au rapport mesuré. Deux assertions sur `IcsTimeZones.ResolveIana` valent mieux que ce triage-là.
   Elles figent aussi ce que la valeur **devient** : `ResolveIana` rend `IsKnownIana(id) ? id : …`,
   donc `US/Eastern` ressort `US/Eastern` et non `America/New_York`. C'est l'alias qui est stocké
   en base et l'alias que `IcsTimeZones.Emit` réémet dans le `calendar-timezone` d'un `PROPFIND` —
   ça marche, et c'est exactement pour ça qu'il faut l'asserter plutôt que le supposer.
6. **Le `Cache-Control: no-cache` d'un `MKCALENDAR` et d'un `MKCOL` est posé sur les refus aussi**,
   pas seulement sur le `201`. RFC 4791 § 5.3.1, Marshalling, sans condition : « The response MUST
   include a Cache-Control:no-cache header » ; RFC 4918 § 9.3 pour le `MKCOL` : « Responses to this
   method MUST NOT be cached », et l'exemple de RFC 5689 § 3.5 pose l'en-tête sur un `403`.
   `CalDavController.MakeCalendarAsync` sert les deux verbes et ne l'écrit aujourd'hui que sur le
   chemin créé ; le `403`, le `207` + `propstat`, le `405` et le `507` en sortent sans. C'est un
   MUST qu'**aucune suite de l'outil ne vérifie** — il ne serait donc jamais trouvé par la mesure,
   ce qui est précisément la raison de le refermer ici plutôt que d'attendre un verdict.
7. **Le refus de `resourcetype` ou de `calendar-timezone` sur un `MKCOL` étendu sort en
   `DAV:mkcol-response` à `propstat`**, comme celui de `supported-calendar-component-set`, et non
   plus en `403` + `<D:error>` nu. `DavHeaders.ComplianceClasses` annonce `extended-mkcol`, et RFC
   5689 § 3.1 impose ce jeton à tout serveur qui sert les features du document, et l'annoncer sans
   les tenir dit donc une chose fausse ; § 3 dit du refus de propriété que la réponse « MUST be an
   XML document containing a single DAV:mkcol-response XML element, which MUST contain DAV:propstat
   XML elements », et l'exemple § 3.5 y place `DAV:valid-resourcetype`. Une non-conformité sous
   jeton annoncé, pas une divergence assumée ; `WriteCreationRefusalAsync` existe déjà et choisit le
   conteneur selon le verbe, il ne manque que l'aiguillage de ces deux refus vers lui — et l'élément
   `valid-resourcetype` que l'exemple montre. `WritePropstatsAsync`, en revanche, ne sait **pas**
   porter un `<D:error>` dans un `propstat` : elle est privée, ne prend que ses deux listes de
   propriétés, et les deux seuls endroits du writer qui écrivent un `<error>` le posent en frère du
   `<status>` sous `<response>`. Ce point est donc aussi une extension du writer, pas un simple
   aiguillage. `MKCALENDAR` ne change pas : RFC 4791 § 5.3.1 nomme `valid-calendar-data` en
   précondition et `403` + `<D:error>` est la forme de RFC 4918 § 16. Aucun `MKCOL` de l'outil ne
   porte de corps (décision 5) : lui non plus ne serait jamais trouvé par la mesure.

Un huitième résidu se referme sans une ligne de code : `calendar-5c-residuals.md` annonce que
l'ordre des `propstat` d'un `PROPPATCH` mixte n'est asserté que par leur compte. Il l'est par leur
rang depuis 5c — `CalDavProppatchTests.TheFiveWritableOnes_Answer200AndTheRest403_InThatOrder`
compare la séquence `["HTTP/1.1 200 OK", "HTTP/1.1 403 Forbidden"]`, qu'une inversion des deux
blocs rougit. La ligne du résidu est périmée : elle se barre, elle ne s'implémente pas.

Rien d'autre. Les résidus qui coûtent une décision — le `param-filter` négatif qui correspond sur un
paramètre absent, la présélection d'une ressource à plusieurs surcharges sans maître, `calendar-data`
servie dans un `PROPFIND` — attendent le verdict de l'outil ou d'un client. Les refermer d'avance
serait décider sans mesure, ce que cette tranche existe précisément pour éviter.

### 4. Quatre verdicts, et la ligne du RFC pour chacun

Chaque test en échec — de l'outil comme d'un client — reçoit exactement un verdict, écrit dans le
rapport avec la ligne du RFC qui le justifie :

| Verdict | Ce qu'il engage |
|---|---|
| **Défaut du serveur** | Corrigé dans la tranche, dans la vague unique. Un test unitaire fige la correction, et le rapport cite ce test à côté du verdict. Trois exceptions, nommées d'avance plutôt que découvertes : `COPY`/`MOVE`, la lecture de `comp`/`prop` et celle des `limit-*` (décision 5), dont la correction est à chaque fois une fonctionnalité et non un correctif — **différées avec leur arbitrage écrit**, jamais tues. |
| **Divergence nommée** | Écrite dans le rapport avec ce qu'elle coûte à un client réel, et ajoutée à `calendar-5c-residuals.md` si elle n'y est pas. Une divergence sans coût écrit est une divergence non instruite. |
| **Défaut de l'outil** | Python 2, propriété CalendarServer, `<start>` qui saute, second utilisateur non défini, données absentes du dépôt, vérification impossible à satisfaire. Consigné, la suite comptée sautée. Ce n'est jamais un verdict par défaut : il faut pouvoir dire *quoi* dans l'outil. |
| **Harnais** | Une valeur de `serverinfo.template.xml`, une `<feature>` mal posée, ou un bloc `<start>` que le serveur ne peut pas satisfaire (décision 10) — pas le serveur. C'est le verdict de `$calendar_sync_extra_items:` en 4d, et il se distingue du précédent parce qu'il se **corrige** : un correctif de harnais entre dans la vague au même titre qu'un correctif serveur, et le passage final le mesure. |

**Une seule vague** de correctifs serveur entre le passage initial et le passage final. Mesurer,
corriger, remesurer une fois : c'est ce qui rend le chiffre lisible, et c'est la règle de process du
projet. Une seconde vague reste possible **après** les clients, et seulement si un client en impose
(étape 5) : elle ne se mêle pas au chiffre de l'outil, qui est déjà figé quand elle commence.

### 5. Les divergences attendues sont écrites avant le premier passage

Ce que 5c a assumé et que l'outil va nécessairement rejeter. L'écrire ici plutôt que de le découvrir
dans une sortie de 4000 lignes est la différence entre un triage et une fouille.

Trois mots de l'outil reviennent partout et valent d'être posés une fois. Un **`<start>`** est le
bloc de préparation en tête d'un fichier : les requêtes qui posent les agendas et les événements
dont les tests auront besoin ensuite. Un **`require-feature`** dit « ne joue ceci que si le serveur
a telle capacité », un **`exclude-feature`** dit l'inverse — et comme c'est nous qui déclarons ces
capacités dans `serverinfo.xml`, ce sont nos propres déclarations qui décident de ce que l'outil
enverra. Une capacité éteinte fait donc sauter les tests qui la requièrent **et** réveille ceux qui
l'excluent : c'est tout le raisonnement de `no-duplicate-uids` plus bas.

La colonne du milieu est celle qui compte, et elle est le pendant de la décision 6 : une divergence
sort soit en **échec** — l'outil a envoyé la requête et notre réponse ne lui a pas plu, donc le
serveur a été mesuré —, soit en **ignoré** — une `<feature>` éteinte lui a fait sauter la suite,
donc le serveur n'a rien vu du tout —, soit en **non lancée** — le fichier est hors de
`suites-caldav.txt`, rien n'en part —, soit en **non mesuré** — l'outil n'écrit nulle part la forme
en cause —, soit en **fichier tué** — une requête du bloc `<start>` a répondu autre chose qu'un
2xx, et l'outil a écrit « Start items failed - tests will not be run » : une erreur comptée dans
`errors=` de la ligne finale, **pas dans `failed=`**, et zéro test joué (décision 10). Prédire
« échec » là où le harnais produira « ignoré » ou « fichier tué » revient à croire mesuré ce qui a
été contourné, ce qui est exactement l'erreur que le rapport existe pour ne pas commettre.

Une dernière chose à savoir avant de lire la table : `DavHeaders.ComplianceClasses` annonce
`1, 3, addressbook, calendar-access, extended-mkcol`. RFC 4791 § 5.1 : « A value of
"calendar-access" in the DAV response header MUST indicate that the server supports all MUST level
requirements specified in this document » — et RFC 4918 § 18.1 dit la même chose de la classe `1`.
Tout MUST de ces deux textes que 5c ne tient pas est donc une **non-conformité connue**, pas une
divergence assumée : c'est le cas de `COPY`/`MOVE`, des `limit-*` et de la borne de `time-range`
fermée à cinq ans. `comp`/`prop` n'en est **pas** une, contrairement à ce que cette spec écrivait
d'abord : ni § 9.6 (« need to be returned », « will be returned in their entirety ») ni § 9.6.1 ne
portent de mot-clé RFC 2119, là où § 9.6.6 et § 9.6.7 en disent chacun un — c'est une divergence
assumée. La table marque les unes et les autres, et le rapport porte les trois non-conformités dans
cette catégorie. Ce qui les distingue entre elles n'est pas le RFC : les deux
premières ont une correction qui est une fonctionnalité, différée avec son arbitrage (décision 4) ;
la borne de `time-range`, elle, est exercée par un client visé et se corrige dans la tranche
(décision 11) :

| Ce que 5c a assumé | Où | Ce que l'outil en fera | Pourquoi c'est assumé |
|---|---|---|---|
| `reports.xml`, suite `query reports with filtered data` : `comp` et `prop` dans `calendar-data` | RFC 4791 § 9.6.1 | **échec** — suite non gardée | **Divergence assumée**, et non une non-conformité : ni § 9.6 ni § 9.6.1 ne portent de mot-clé RFC 2119 (« need to be returned », « will be returned in their entirety »). § 9.6 ne rend la ressource entière qu'« if the CALDAV:calendar-data XML element doesn't contain any CALDAV:comp element ». Lus puis ignorés ; la ressource entière sort (spec 5c § 7), un surensemble. Aucun client visé ne les envoie — vérifié dans le code de Thunderbird et de DAVx⁵, et dans la seule trace publiée d'un `calendar-query` iOS (sabre), dont le `prop` ne demande d'ailleurs que `getetag` et `resourcetype` — pas même un `calendar-data`. |
| `reports.xml`, suite `limit/expand recurrence in reports` : `limit-recurrence-set`, `limit-freebusy-set` | § 9.6.6, § 9.6.7 | **échec** — suite non gardée (t1 et t2 sont `ignore="yes"`, t9b gardé par `timezones-by-reference` : onze tests joués sur quatorze) | **Non-conformité connue** : § 9.6.6 dit « the server MUST return … only » et § 9.6.7 « the server MUST only return the FREEBUSY property values » — deux MUST. Lus nulle part. |
| Les tests `VTODO` de `put.xml` (22 lectures de `$taskspath1:`), `recurrenceput.xml` (7), `delete.xml` (t2) et les **sept non gardés de `reports.xml`** (`basic query` t15, t16, t21 ; `time-range` t9-t11 ; `alarm` t5) | § 5.2.3 | **échec** — `404` sur `$calendarhome1:/tasks`, dans des tests | Un agenda ne sert que `VEVENT` (`supported-calendar-component-set`, 5a). |
| Les `PUT` `VTODO` du **`<start>`** de `reports.xml` (`$taskspath1:/101.ics`…`106.ics`) et de `delete.xml` (`$taskspath1:/1todo.ics`), **et le `PUT` `VFREEBUSY` s15 de `reports.xml`** (`$calendarpath1:/15.ics` ← `reports/put/15.txt`, gardé `exclude-feature split-calendars`, feature éteinte, donc envoyé) | § 5.2.3 | **fichier tué** sur les copies amont — donc **échec** nommable seulement sur les copies locales de la décision 10 | Sans correctif de harnais, les deux fichiers entiers sortent en « Start items failed » : les huit suites de `reports.xml` — multiget, query, `time-range`, `VALARM`, **`free-busy`**, `limit`/`expand` — ne seraient jamais jouées. `IcsGuards` refuse un `VFREEBUSY` en `403 supported-calendar-component` exactement comme un `VTODO`. Une fois le fichier joué, `free-busy reports` t2 échoue encore : il attend un intervalle `unavailable` issu de ce `VFREEBUSY` absent. |
| Les tests `VTODO` de `mkcalendar.xml` suite **`MKCALENDAR supported-component-set`** : t4 (`MKCALENDAR vtodo-only`) et t5-t6 qui écrivent dedans, puis t7 (`MKCALENDAR vevent-vtodo`) et t8-t9 | § 5.2.3 | **échec** — **six**, la suite n'étant gardée par rien | `CalendarPropertyValue.OnlyEvents` refuse tout `comp` autre que `VEVENT` : les deux créations sortent en `207` + `propstat 403`, les quatre `PUT` qui suivent en `404`. Attention à t7 : il **n'est pas** gardé par `split-calendars` — il porte deux `<verify>`, l'un en `exclude-feature split-calendars` qui attend `201`, l'autre en `require-feature` ; la feature étant éteinte, c'est la branche `201` qui s'applique. t8 et t9 portent, eux, un `exclude-feature split-calendars` au niveau **test**, donc ils tournent aussi. Six échecs prévisibles, d'une ligne de triage chacun. |
| Les tests `VTODO` de `mkcalendar.xml` suite **`Single component calendars`** (ses trois lectures de `$taskspath1:`) | § 5.2.3 | **ignoré** — suite gardée par `split-calendars`, éteinte | Même divergence, mais l'outil ne l'enverra pas. |
| `sync-report.xml` sur `$calendarhome1:/` | RFC 6578 § 3 | **ignoré** — `sync-report-home` éteinte : dans `sync-report.xml`, onze suites sautées au niveau suite **et vingt tests isolés** dans les autres | Le home ne sert pas `sync-collection` : de tous les `REPORT`, il ne sert qu'`expand-property` (`CalDavController.ServeReportAsync`, et sa table l'annonce dans `supported-report-set`) — `expandproperty.xml` le mesure là. RFC 6578 § 3.2 n'impose la synchronisation sur aucune collection en particulier, et aucun client visé ne synchronise le home — Thunderbird, DAVx⁵ et iOS synchronisent agenda par agenda. |
| Un `REPORT` autre qu'`expand-property` sur le home : `floating.xml` (suite `collection` t1-t3, `expand` t1, `free-busy` t3), `encodedURIs.xml` t8 des deux suites d'agenda, `sync-report.xml` `limited reports` t1 | RFC 4791 § 7.2 (« Servers MAY support the reports defined in this document on ordinary collections ») | **échec** — huit tests, aucun gardé ; le t1 de `limited reports` tourne parce que `sync-report-limit` n'est lue qu'en `exclude-feature` | Même divergence que la ligne précédente, vue par d'autres fichiers. L'`expand` de `floating.xml` ne mesure donc **pas** l'expansion, et `limited reports` t1 ne mesure pas `DAV:limit` — que 5c honore, avec le `507 number-of-matches-within-limits` que RFC 6578 § 3.6 place dans un `<D:response>` du multistatus (§ 3.7 y renvoie, et l'`<D:error>` y est un SHOULD) : c'est un échec pour la mauvaise raison, à trier « home », pas « limite ». |
| `copymove.xml`, et les tests `COPY`/`MOVE` d'`errors.xml`, `encodedURIs.xml` et `ctag.xml` | RFC 4918 § 9.8 (« All WebDAV-compliant resources MUST support the COPY method »), § 9.9 (la même phrase pour `MOVE`) et § 18.1 | **échec** — `COPY Method` et `MOVE Method` allumées exprès (« Le harnais »), 39 tests | **Non-conformité connue**, l'une des trois de cette table dont la correction est une fonctionnalité (décision 4). `DavHeaders.ComplianceClasses` annonce `1` et `3`, et § 18.1 dit qu'une ressource de classe 1 « MUST meet all "MUST" requirements in all sections of this document » (§ 18.3 le redit pour la classe 3, verrous exceptés) : les deux verbes en font partie. Un `405` + `Allow` est donc faux au sens du RFC, pas seulement pauvre. **Ce que ça coûte à un client** : rien aux trois clients visés — Thunderbird n'a ni `COPY` ni `MOVE` dans son fournisseur, DAVx⁵ n'appelle jamais le `move()` de dav4jvm pour un événement, et aucun des trois n'a de chemin qui envoie un `MOVE` — DAVx⁵ n'a même aucun code qui traite un changement d'agenda, ce que l'application Android en fait ensuite n'étant pas de son ressort — et tout à un outil WebDAV générique (cadaver, un montage `davfs`, l'explorateur Windows), à qui l'en-tête `DAV: 1` a promis une opération que le serveur refuse. Les servir est une écriture de ressource complète (sémantique du `PUT`, `Destination`, `Overwrite`, destination hors agenda), donc une fonctionnalité et non un correctif d'une ligne : la tranche ne la fait pas, l'arbitrage est écrit dans « Ce que la tranche ne fait pas », et le rapport porte la non-conformité en toutes lettres plutôt que de la ranger parmi les divergences. |
| `aclreports.xml`, six suites sur sept | RFC 3744 | **ignoré** — les features `*-principal-* REPORT` restent éteintes | Pas d'ACL, et `access-control` n'est pas annoncé dans l'en-tête `DAV:` : aucun client n'envoie ces rapports, contrairement à `COPY`/`MOVE`. La septième, `supported-report-set property`, n'est gardée par rien et c'est pour elle que le fichier reste lancé — mais elle se trie d'avance plutôt qu'elle ne s'attend : ses deux tests visent l'**agenda** (`$calendarpath1:/`) et la **collection de principaux** (`$principals_users:`), jamais le home, et chacun exige que la réponse **contienne** `acl-principal-prop-set`, `principal-match` et `principal-property-search` — plus, sur l'agenda, `expand-property`, `calendar-query` et `calendar-multiget`, que nous servons, et sur la collection de principaux ces six-là plus `free-busy-query` et `principal-search-property-set`. Les trois jetons ACL manquent et nous n'avons pas d'ACL : les deux tests sont **incapables de passer** même si une partie de ce qu'ils réclament est bien dans notre `supported-report-set`, et ce qu'ils mesurent est la convention CalendarServer d'annoncer les rapports RFC 3744 partout, pas une exigence du RFC. Deux échecs prévisibles, verdict « défaut de l'outil » ; la vraie mesure de notre `supported-report-set` reste celle de la tâche zéro, point 3. |
| Le même UID dans deux agendas d'un même home | RFC 4791 § 5.3.2.1 | **non mesuré** par un test en échec — `duplicate_uids.xml`, seul fichier qui **requiert** `no-duplicate-uids`, est retiré — mais **mesuré positivement** par quatre `PUT` de `<start>` qui doivent passer | `CALDAV:no-uid-conflict` est scopée par le RFC « in the targeted calendar collection », et c'est exactement ce que fait l'index `(calendar_id, uid)` de 5a. La règle de l'outil est celle de CalendarServer — refus **par home** —, une extension qu'il déclare lui-même en `<feature>`. Dans les fichiers retenus, cette feature n'apparaît qu'en **`exclude-feature`** (`errors.xml` ×3 — dont une dans son `<start>`, les deux autres sur `COPY`/`MOVE` —, `ctag.xml` ×3, `encodedURIs.xml` ×2, `copymove.xml` ×6 — dont **trois `PUT` de son `<start>`**, s4, s6, s8, qui écrivent dans `calendar2` un UID déjà présent dans `default`) : éteinte, les tests `COPY`/`MOVE` tournent et échouent avec les autres, et les quatre `PUT` de `<start>` (celui d'`errors.xml` et les trois de `copymove.xml`) partent et **doivent réussir** — c'est la seule mesure du scope RFC dans la campagne, et un échec de l'un d'eux tuerait son fichier. Allumée, tout cela sauterait. Éteinte, donc, pour que le serveur soit mesuré tel qu'il est. |
| Le corps du refus de `resourcetype` et de `calendar-timezone` **à un `MKCOL` étendu** | RFC 5689 § 3, § 3.1 et son exemple § 3.5 | **non mesuré** — aucun `MKCOL` de l'outil ne porte de corps — et **corrigé d'avance** (tâche zéro, point 7) | C'était une non-conformité sous jeton annoncé : `extended-mkcol` est dans l'en-tête `DAV:`, et RFC 5689 § 3 veut un `DAV:mkcol-response` à `propstat` pour un échec de propriété, son exemple § 3.5 y plaçant `DAV:valid-resourcetype` — une **précondition** — plutôt qu'un `<D:error>` nu. 5c répondait `403` + `<D:error>` ; la tâche zéro l'aligne, pour `MKCOL` seul. **Sur `MKCALENDAR`, il n'y a rien à corriger** : RFC 4791 § 5.3.1 nomme `CALDAV:valid-calendar-data` dans sa liste de préconditions (« The time zone specified in the CALDAV:calendar-timezone property MUST be a valid iCalendar object containing a single valid VTIMEZONE component »), et `403` + `<D:error>` est exactement la forme que RFC 4918 § 16 donne à une précondition nommée. Le `MKCALENDAR $calendarhome1:/calendar-us/` du `<start>` de `floating.xml` est donc conforme, et il ne peut tuer son fichier que si le TZID `US/Eastern` ne résout pas — d'où la tâche zéro, point 5, et rien d'autre. Le corps `MKCOL`, lui, ne sera mesuré par personne : les **cinq** `MKCOL` de l'outil (`put.xml`, `errors.xml`, `mkcalendar.xml`, `nonascii.xml`, `encodedURIs.xml`) créent une collection nue **sans corps** — vérifié un par un — et **quatre** d'entre eux sont gardés par `regular-collection`, éteinte (`put.xml` t4, le `<start>` d'`errors.xml`, celui de `nonascii.xml`, et la suite `regular resource` entière d'`encodedURIs.xml`). Le seul qui parte est `mkcalendar.xml` t4, un `MKCOL` **dans** un agenda, qui attend le `403` que nous rendons. Seuls les tests in-process de 5c couvrent le corps. |
| Une borne de `time-range` absente, fermée à cinq ans | § 9.9 : `start` et `end` sont `#IMPLIED`, « at least one attribute MUST always be present », et « If either the "start" or "end" attribute is not specified …, assume "-infinity" and "+infinity" as their value, respectively » ; § 7.8 : la réponse « MUST contain a DAV:response element for each iCalendar object that matched the search filter » | **ignoré** par l'outil — `timerange-low-limit` et `timerange-high-limit` éteintes — mais **joué par un client réel** dès le scénario 2 | **Non-conformité connue**, et **la décision de 5c est rouverte dans cette tranche** : `OccurrenceExpander.MaxSpan` ferme la borne à `start + 1830 jours`. DAVx⁵ avec son réglage par défaut (« 90 derniers jours », `DEFAULT_TIME_RANGE_PAST_DAYS = 90`) ne fait **pas** de `sync-collection` : il envoie à chaque synchro un `calendar-query` `time-range` à `start` seul, **sans `end`** (dav4jvm n'écrit l'attribut que s'il est non nul), et la trace iOS publiée par sabre a la même forme. **Ce que ça coûte à un client** : un événement unique au-delà de cinq ans disparaît du téléphone sans que rien ne le dise. Trois réponses étaient possibles : refuser (`valid-filter`), fermer, ou servir la borne ouverte telle quelle avec un garde-fou sur les séries. 5c a écrit la deuxième, la seule des trois à répondre faux sans le dire. La règle de la décision 7 — ne se rouvre que devant un client réel — s'applique, et le client réel est là : **la troisième réponse est retenue d'avance pour le `calendar-query`, décision 11**, livrée dans la vague unique de l'étape 4 et vérifiée au scénario 11. Ce qui reste fermé ou refusé après elle (le `time-range` d'un `VALARM`, celui de `free-busy-query`, et la fenêtre à deux bornes de plus de cinq ans) est porté en résidu 5d comme non-conformité connue, exercée par aucun client. Les deux features de l'outil ne mesurent d'ailleurs pas ça : leurs sept tests attendent un `CALDAV:min-date-time`/`max-date-time`, la limite **configurée** de CalendarServer, que nous n'avons pas — les allumer mesurerait un comportement que nous n'avons pas choisi d'avoir. |
| Une borne d'`expand`, de `limit-recurrence-set` ou de `limit-freebusy-set` absente | § 9.6.5, § 9.6.6 et § 9.6.7 (`start` et `end` `#REQUIRED`) | **non mesuré** — les quinze `CALDAV:expand` des fichiers retenus (onze dans `reports/limitexpand/`, trois dans `reports/timerangequery/`, un dans `floating/6.xml`) et les trois `limit-*` portent tous leurs deux bornes | Rien à corriger, et surtout pas de verdict à écrire d'avance : `CalendarDataRequest.Parse` refuse **déjà** en `valid-filter` un `expand` dont une borne manque ou dont `start >= end`, ce que le RFC demande. `limit-recurrence-set` et `limit-freebusy-set`, eux, ne sont lus nulle part — c'est la ligne 2 de cette table, une divergence de lecture et pas de bornes. La ligne de `calendar-5c-residuals.md` qui rangeait l'`expand` avec le `time-range` « fermé à cinq ans » a été corrigée en écrivant cette spec. |
| `test="anyof"` sur un `prop-filter` | extension `calendar-query-extended`, **pas** RFC 4791 : § 9.7.1 et § 9.7.2 ne donnent à `comp-filter` et `prop-filter` que l'attribut `name` | **non mesuré** — la chaîne `test="anyof"` n'apparaît dans aucun fichier de l'outil, et les suites qui l'exerceraient sont gardées par `query-extended` | Refusé en `CALDAV:supported-filter` (`CalendarQueryFilter.Unsupported`), comme un `match-type` inconnu : ce moteur ne l'évalue pas exactement, et rendre un surensemble sans le dire serait pire. Refus d'autant plus défendable que l'attribut ne vient pas du RFC. |
| `well-known.xml` | RFC 7231 § 7.1.2, RFC 8615 | **échec, et déjà trié** | 4d a mesuré le fichier CardDAV jumeau à 0/10 et l'a classé « défaut de l'outil » : notre `Location` relatif est licite (RFC 7231 § 7.1.2), et le `200` exigé sur `/.well-known/` nu ne sort d'aucun RFC — RFC 8615 § 3 dit même l'inverse (« clients should not expect a resource to exist at that location »). Le `301` de 5c est par ailleurs nommément prévu par RFC 6764 § 5 (« 301, 303, or 307 »). Le fichier CalDAV a la même forme — il attend un `Location` égal à `$hostssl:/`, la racine absolue, là où nous rendons `/dav/` — et le même verdict ; il se recopie, il ne se retrie pas. |
| `mkcalendar.xml`, suites `MKCALENDAR without body` t2 et `with body` t2 et t4 : un `MKCALENDAR` sur un agenda **existant** | RFC 4791 § 5.3.1, précondition `DAV:resource-must-be-null` ; § 5.3.1.1 | **échec** — trois tests, non gardés, qui attendent `403` + `<D:error><D:resource-must-be-null/>` | Nous répondons `405` + `Allow` — l'`Allow` venant de RFC 7231 § 6.5.5 et non de RFC 4918 § 9.3, qui n'impose que « the MKCOL MUST fail » et ne propose le `405` qu'en § 9.3.1. Le seul MUST engagé est donc tenu ; ce qui manque est le **corps**, que RFC 4918 § 16 recommande (SHOULD) et dont la liste de préconditions de § 5.3.1.1 est « by no means exhaustive ». Verdict attendu **défaut du serveur** au titre de ce SHOULD, à confirmer par la mesure et à corriger dans la vague — en notant que le `prepostcondition` de ces trois tests n'a pas d'argument `status` et accepte donc `403`, `409` ou `507`. `without body` t3 (parent = un agenda) attend `calendar-collection-location-ok`, que nous rendons : il passe. |
| `mkcalendar.xml`, suite `MKCALENDAR with body` t3 : un corps portant `DAV:getetag`, propriété protégée | RFC 4791 § 5.3.1.1 (`403` ou `409` par propriété — « trying to set read-only properties » vaut `409` —, `207` global), RFC 4918 § 15.6 | **échec** — un test à deux requêtes : t3 attend un `propstat` d'échec sur `getetag`, `displayname` et `calendar-description`, puis un `404` au `GET` qui suit | `MkCalendarRequest.Parse` ignore toute propriété qu'il ne connaît pas et l'agenda est créé (`201`, puis `405` au `GET`). C'est le résidu 5c « une couleur hors forme est ignorée, pas refusée », vu par l'outil sur une propriété protégée. Verdict à écrire après mesure ; le mécanisme `WriteCreationRefusalAsync(refused: […])` est en place si la conformité stricte est retenue. À noter : DAVx⁵ envoie `calendar-timezone-id` (RFC 7809) dans son `MKCALENDAR` — ignorée de même, et c'est ce qu'il faut, pas un refus : RFC 7809 n'impose cette propriété (§ 3.1.5) qu'à un serveur qui met en œuvre la spécification, laquelle s'annonce par le jeton `calendar-no-timezone` (§ 3.1.1) — ce que nous ne faisons pas (décision 7, scénario 9). |
| `options.xml`, suite `PROPFIND - no DAV` : l'en-tête `DAV:` ne doit **pas** figurer sur un `207` | RFC 4918 § 10.1 : « All DAV-compliant resources MUST return the DAV header … on all OPTIONS responses » — rien sur les autres réponses | **échec** — deux tests, non gardés | `MultiStatusWriter` pose `DAV:` sur tout `207`, ce que sabre recommande pour les clients Apple, mais sous condition (« If calendar-proxy support is required, this header must also be set in other responses ») et que le RFC n'interdit pas. Verdict **défaut de l'outil** : il mesure une convention CalendarServer. Deux nuances de lecture : son `statusCode` est sans argument, donc tout 2xx lui convient, et son t2, que son nom dit « on principal », vise en fait `$calendarhome1:/`. |
| `proppatch.xml` : les propriétés mortes (`DAV:details`, `DAV:details2`, `xml:lang`, `remove` d'une propriété absente) | RFC 4918 § 9.2 (« DAV-compliant resources SHOULD support the setting of arbitrary dead properties »), § 14.23 (« Specifying the removal of a property that does not exist is not an error ») | **échec** — le fichier a huit tests, **six** dans la suite `prop patches` et deux dans la seconde, où vit `xml:lang`. Les six premiers touchent une propriété morte, t4 excepté : celui-là attend précisément que le `PROPPATCH` **échoue** (`badprops` = `details`, `details2`, `resourcetype`) et ne rougit que sur son `PROPFIND` de contrôle, qui veut `details2` en `200`. t5 (`displayname`, dont l'objet réel est l'échappement XML) et t6 (`404`) passent | `CalendarPropertyUpdate.Judge` refuse en `403` tout ce qui n'est pas une des cinq propriétés inscriptibles. Ne pas stocker de propriété morte est une **divergence assumée** face à un SHOULD (5c § 11 : cinq propriétés, pas de stockage libre). Le `remove`, lui, n'est pas uniforme, et le décrire comme un refus général serait faux : `Judge` n'accepte l'effacement que de `calendar-description` — les quatre autres inscriptibles répondent `403` —, et un `remove` d'une `calendar-description` jamais renseignée répond `200` en réécrivant `null`. Le `403` sur le `remove` d'une propriété **morte** reste un candidat **défaut du serveur** au sens de § 14.23, à trancher après mesure ; l'asymétrie entre les cinq inscriptibles est un résidu 5d, qu'aucun test de l'outil n'atteint. |
| `conditional.xml` : `Last-Modified` puis `304` sur un `PROPFIND` conditionnel | aucun RFC (RFC 4918 ne définit ni `Last-Modified` ni `304` pour `PROPFIND`) | **échec** — trois tests, dont un `DELAY` de deux secondes entre les deux qui comptent : t1 fait un `grabheader Last-Modified`, qui échoue sur l'en-tête absent, et t3 attend le `304` sur un `PROPFIND If-Modified-Since` | **Défaut de l'outil**, convention CalendarServer. |
| `timezones.xml` | RFC 4791 § 5.2.2, RFC 7809 | **échec** — t1 (`calendar-timezone` attendue en `404` sur `default`, que nous servons toujours), t6 (`PROPPATCH calendar-timezone-id`, non gardé par `timezones-by-reference`), t-1 (`remove calendar-timezone`, que `Judge` refuse), et t4/t7 dont les regex `.*TZID:Etc/GMT+1.*` et `.*TZID:Etc/GMT+2.*` ne peuvent matcher sur aucun serveur (`+` non échappé, `.` sans DOTALL sur une valeur multi-lignes) | t4/t7 : **défaut de l'outil**. t1 aussi, et c'est un verdict que cette spec avait d'abord écrit à l'envers : § 5.2.2 dit « This property SHOULD be defined on all calendar collections », donc servir toujours `calendar-timezone` est ce que le RFC recommande, et c'est l'attente d'un `404` qui est la convention CalendarServer. t-1 : `Judge` refuse l'effacement, **divergence assumée** ; t6 : RFC 7809 non servi, divergence assumée. Corollaire à trier plutôt qu'à supposer : le même § 5.2.2 ajoute « SHOULD NOT be returned by a PROPFIND DAV:allprop request », et notre table `allprop` de l'agenda n'a jamais été relue sous cet angle. Les deux suites `Timezone cache` et `Timezone cache - aliases` (quatre tests chacune, `PUT` à `VTIMEZONE` tronqué puis `free-busy-query` sur l'année) mesurent réellement notre `free-busy` et n'ont pas de verdict d'avance. |
| `propfind.xml` : `getcontentlength` attendu en `200` sur le home et sur l'agenda | RFC 4918 § 15.4 (`getcontentlength` est définie « on any DAV-compliant resource that returns the Content-Length header in response to a GET ») | **échec** — deux tests ; t4 sur `$calendars_users:` n'en est pas un, la clé étant repointée (« Le harnais ») et le test mesurant alors `propfind-finite-depth` comme t2 et t3 | `getcontentlength` n'est servie que sur l'événement, la seule ressource qui a un `GET` : `404` propstat sur les collections, **défaut de l'outil**. |
| `sync-report.xml` t1 « Not on calendars » | — | **échec** ou **passe**, selon ce que le routeur fait de `/dav/calendars//` : `$calendars:` finit déjà par `/` et le test en ajoute une | Bruit d'outil, verdict d'avance ; à consigner, pas à corriger. |
| `bad-ical.xml` | — | **non lancée** — le fichier est exclu (« Le harnais ») ; lancé, il sortirait en « No iteration data - ignored » | Son unique test itère sur `Resource/CalDAV/bad-ical/`, qui ne contient dans le dépôt qu'un `.gitignore` vide. Le fichier ne mesure rien et n'est pas lancé (« Le harnais ») ; le `PUT` invalide reste couvert par les tests in-process de 5c. |

Une divergence de cette table qui **passe**, ou qui sort en « ignoré » là où on l'attendait en
« échec », est aussi un résultat : elle se consigne, et la ligne correspondante de
`calendar-5c-residuals.md` se referme.

### 6. Le rapport garde la mesure brute ; le secret ne le traverse pas

Le rapport est `docs/superpowers/calendar-5d-conformance.md`, sur le plan de son jumeau
`carddav-4d-conformance.md`. Ce qui y entre est copié du fichier de `results/`, jamais de la
console : un chiffre recopié de mémoire est un chiffre inventé. Les totaux y sont bruts — tant de
tests, tant d'échecs, tant d'ignorés, tant de fichiers sautés —, et la distinction entre « ignoré »
(un test conditionné à une `<feature>` éteinte) et « échoué » est préservée partout, parce que c'est
elle qui dit si le serveur a été mesuré ou contourné. La ligne finale de l'outil a **quatre**
compteurs dès qu'il y a un échec — `FAILED (ok=, ignored=, failed=, errors=)` — et **deux**
seulement quand il n'y en a aucun — `PASSED (ok=, ignored=)`. Un fichier tué à son `<start>` ou
sauté sur une trace Python compte dans `errors=`, pas dans `failed=`, et le rapport recopie ce que
la ligne porte. Le `ok=107, failed=72` de 4d abrège cette ligne dans son bilan ; son § 4 en recopie
bien les quatre.

`run.ps1` épure les en-têtes `Authorization` avant que le fichier ne touche le disque, et
`serverinfo.xml` comme `serverinfo.local.json` restent ignorés. Le secret DAV du compte de test
n'apparaît ni dans le dépôt, ni dans `results/`, ni dans le rapport, et il est régénéré à la
clôture.

### 7. Les clients réels : Thunderbird et DAVx⁵ + Agenda Samsung, scénarios fixés d'avance

Ce que chaque client envoie est lu dans son code, pas supposé — `calendar/providers/caldav/` de
comm-central pour Thunderbird, `davx5-ose` et `dav4jvm` pour DAVx⁵ —, parce qu'un scénario écrit sur
une idée fausse du client trie ensuite un écart qui n'existe pas.

Thunderbird d'abord : son client CalDAV est celui de Mozilla, le plus bavard des trois en `PROPFIND`
— un `getctag` à chaque poll **tant que le serveur n'annonce pas `sync-collection`**, ce qui n'est
pas notre cas : chez nous ce `PROPFIND`-là ne part pas ; un `OPTIONS` sur la collection parente de
l'agenda, avec repli sur l'agenda en `404` ; et une relecture de
`resourcetype`/`supported-report-set` à chaque démarrage. Il fait tourner la découverte, le
`sync-collection` (avec `Depth: 1` et un `<sync-token/>` vide au premier passage) et le
`calendar-multiget` ; sans `sync-collection` annoncé il retombe sur un `PROPFIND Depth: 1` de
l'agenda. Il **n'envoie jamais de `calendar-query`** ni de `REPORT free-busy-query` — il fait bien
du free-busy, mais par `POST` d'un `VFREEBUSY` sur le `schedule-outbox`, et seulement si l'`OPTIONS`
annonce `calendar-schedule`/`calendar-auto-schedule`, ce que nous ne faisons pas. Il ne sait pas non
plus créer un agenda sur le serveur ni écrire une propriété d'agenda : son fournisseur n'envoie
aucun `MKCALENDAR`, aucun `MKCOL`, aucun `PROPPATCH`, et ne lit `calendar-color` qu'à la découverte.
Il ajoute `X-MOZ-GENERATION` à chaque événement qu'il modifie, `X-MOZ-LASTACK` et
`X-MOZ-SNOOZE-TIME` quand un rappel est fermé ou reporté, ouvre un **dialogue** à l'utilisateur
quand l'URL de l'agenda répond par une redirection à son `PROPFIND` de contrôle (les autres requêtes
suivent en silence) et sur un `409` ou un `412` en **modification ou suppression** — un `412` sur un
ajout (`If-None-Match: *`) part, lui, dans sa branche « statut inattendu », sans dialogue —, et
passe un agenda en lecture seule si `current-user-privilege-set` est servi sans `write`, ni
`write-content`, ni `bind`, ni `all` — nous en servons sept (`read`, `write`, `write-content`,
`write-properties`, `bind`, `unbind`, `read-current-user-privilege-set`), dont trois des quatre
qu'il cherche. DAVx⁵ + Agenda Samsung ensuite, et le rapport **sépare** ce qui vient de DAVx⁵ (la
synchronisation) de ce qui vient de Samsung (l'interface) : ce sont deux logiciels distincts, donc
deux sources d'écart, et le bug du 7 septembre est venu de leur couple. DAVx⁵ ne sait pas non plus
écrire une propriété d'agenda — aucun `PROPPATCH` dans son code, et son mainteneur ne le prévoit pas
—, crée un agenda par `MKCALENDAR` toujours, et, avec son réglage par défaut, synchronise par
`calendar-query` à borne ouverte plutôt que par `sync-collection` (décision 5, ligne `time-range`).

Un scénario qu'un client ne peut pas jouer sort en **« non applicable »**, un statut distinct des
quatre verdicts : ce n'est ni un défaut ni une divergence, et le rapport dit quel client l'a joué.

Les treize scénarios, joués dans cet ordre pour chacun :

1. **Appairage par la seule adresse** de l'onglet Sync. Pas de `SRV`, pas de `.well-known` DNS sur
   `mail.weesky.net` : si ça ne suffit pas, c'est un défaut serveur, pas une raison d'ajouter de la
   configuration DNS. Les deux clients essaient **d'abord l'URL saisie** par un `PROPFIND`
   (Thunderbird le saute si le chemin est `/`, et y demande sept propriétés, pas seulement
   `current-user-principal`) et ne vont à `/.well-known/caldav` qu'en repli, après un `SRV` qui
   échouera. La séquence exacte de Thunderbird est : recherche `MX`, puis l'URL saisie, puis le
   `SRV` — qui tente lui-même un `.well-known` sur l'hôte qu'il aurait rendu —, puis `.well-known`,
   puis la racine. Le scénario se joue donc
   **deux fois** : l'hôte nu, et l'adresse complète de l'onglet Sync, qui ne mesure pas
   `.well-known`. L'hôte nu ne le mesure que chez **Thunderbird** ; DAVx⁵ fait son `PROPFIND` sur
   `/`, y trouve `current-user-principal` (décision 8), puis envoie un `OPTIONS` **sur le principal**
   et n'accepte celui-ci que si l'en-tête `DAV:` y porte `calendar-access` — vérifié :
   `DavControllerBase.Capabilities` pose l'en-tête sur toute route `OPTIONS`, principal compris —,
   sans quoi il repartirait vers `.well-known`. Le rapport dit laquelle a été saisie, et note si
   Thunderbird a ouvert son
   dialogue de redirection — une URL d'agenda saisie sans barre finale reçoit le `308` de 5c, et
   ce dialogue est le prix visible de cette forme.
2. **Découverte** : les deux agendas apparaissent, avec le nom et la couleur du webmail. Thunderbird
   retire l'alpha d'une couleur `#RRGGBBAA` ; DAVx⁵ demande en plus `calendar-timezone`,
   `calendar-timezone-id`, `source`, `owner` et ses propriétés de push, qui sortent en `404`
   propstat.
3. **Création côté client** → visible dans le webmail, fichier conservé verbatim.
4. **Création côté webmail** → visible côté client.
5. **Modification des deux côtés**, dont deux appareils sur la même ressource : `If-Match`, `412`.
   Les deux clients ne font pas la même chose du `412` : DAVx⁵ l'ignore en silence et retélécharge
   la ressource ; Thunderbird ouvre un dialogue et, si l'utilisateur confirme, **rejoue le `PUT`
   sans `If-Match`**, qui écrase. Le rapport décrit les deux issues, pas seulement le `412`.
6. **Le cinquième cas** — un récurrent écrit par le webmail avec un « cette occurrence seulement »,
   relu par Thunderbird **et** par le téléphone : mêmes instants, mêmes exceptions. Le bloc
   `VTIMEZONE`, lui, n'est comparable avec **aucun** des deux : DAVx⁵ le régénère (ical4j), et
   Thunderbird aussi — son analyseur ignore explicitement les `VTIMEZONE` reçus, son service de
   fuseaux neutralise l'enregistrement d'une définition venue du fichier, et il réémet la sienne à
   la sérialisation. L'attente « même bloc de fuseau » que 5a puis 5b portaient est donc fausse des
   deux côtés : ce qui se compare est l'instant et l'identifiant de fuseau, jamais le bloc. Puis
   l'inverse, écrit par le client et relu par le webmail. C'est la procédure que 5a puis 5b
   renvoient depuis deux tranches, et que 5c a rendue possible sans la jouer.
7. **Rappel** posé côté client → conservé au retour dans le webmail (le chemin `foreignAlarms`).
   Les deux clients **remappent** le `VALARM` (Thunderbird par `CalAlarm`, DAVx⁵ par les
   `Reminders` d'Android) : l'attente est un `VALARM` équivalent — même déclencheur, même action —,
   pas un bloc verbatim.
8. **Couleur et nom d'agenda** changés côté client (`PROPPATCH`) → visibles dans le webmail.
   **« Non applicable » pour les deux** : ni Thunderbird ni DAVx⁵ n'envoient de `PROPPATCH` —
   « Modifier la collection » de DAVx⁵ ne touche que ses réglages locaux. Le seul `PROPPATCH`
   client de la campagne est celui du rejeu Apple (décision 8), qui mesure `calendar-color`,
   `calendar-order` et `default-alarm-vevent-date`.
9. **Création d'un agenda depuis le client** — **DAVx⁵ seul** — puis sa suppression, qui le fait
   disparaître là où `default` se vide. Le verbe est connu :
   `DavCollectionRepository.createCalendar` envoie **`MKCALENDAR`**, toujours — le `MKCOL` étendu ne
   sert qu'aux carnets —, et le rapport le confirme dans le journal de debug plutôt que de le
   supposer. Le corps porte `resourcetype`, `displayname`, `calendar-description`, `calendar-color`
   en `#RRGGBB` et, si un fuseau est choisi, `calendar-timezone-id` (RFC 7809) **et**
   `calendar-timezone` : 5c ignore la première et lit la seconde, et c'est ce qu'il faut. Son
   formulaire laisse cocher Événements / Tâches / Journaux, **tout coché par défaut**, et n'écrit
   `supported-calendar-component-set` que si une case est décochée. Le scénario se joue donc **trois
   fois** : tout coché, qui n'envoie pas la propriété et doit passer (DAVx⁵ relit ensuite `VEVENT`
   seul et s'y range) ; Événements seuls, qui doit passer ; Événements + Tâches, qui doit être
   refusé (`207` + `propstat 403`, `OnlyEvents`) — dav4jvm en fait une `HttpException` et DAVx⁵
   affiche un dialogue « HTTP 207 Multi-Status » dont un bouton « Show details » ouvre les extraits
   de requête et de réponse. Le rapport écrit **ce que le client affiche** dans ce troisième cas :
   c'est le coût réel de cette divergence, mesuré plutôt que supposé. Thunderbird « non applicable
   ». Aucun des clients visés n'exerce le `MKCOL` étendu : sa seule mesure reste les tests
   in-process de 5c.
10. **Suppression** des deux côtés → l'autre appareil la voit au poll suivant. Thunderbird la voit
    sous forme de tombe (`sync-collection`), jamais d'un `403 valid-sync-token`. DAVx⁵ avec son
    réglage par défaut ne demande pas de tombes : il constate l'absence dans son `calendar-query`.
    Le scénario se joue donc **deux fois** chez DAVx⁵, avec le réglage par défaut et avec « tous
    les événements », le seul qui passe par `sync-collection` et voie la tombe.
11. **Journée entière, événement sur plusieurs jours, fuseau différent de celui de l'agenda, un
    événement flottant, et un événement unique posé à plus de cinq ans** (« Renouvellement du
    passeport », mars 2032, créé dans le webmail) : ce dernier doit apparaître sur le téléphone avec
    le réglage DAVx⁵ par défaut — c'est la vérification de la décision 11.
12. **Régénération du secret** → `401`, puis ré-appairage. Elle invalide du même coup
    `serverinfo.local.json` : si un rejeu de l'outil doit suivre (étape 5), le fichier local reçoit
    le nouveau secret avant qu'on relance.
13. **Non-régression du 7 septembre** : un événement créé sur le téléphone s'ouvre à l'édition dans
    le webmail. Les écarts connus du couple DAVx⁵ + Samsung vont dans la colonne Samsung du rapport,
    pas dans celle du serveur : une couleur d'événement que Samsung ne sauve pas, un `VALARM` sans
    `ACTION` qui ne sonne pas sur Android (un `ACTION:DISPLAY` du webmail sonne), une `RRULE`
    réparée par DAVx⁵ (`UNTIL` en `DATE` sur un
    `DTSTART` daté-horaire), un `VTIMEZONE` régénéré.

C'est le seul endroit de la tranche où une décision de 5c peut se rouvrir. Un client qui exige une
forme que le RFC n'impose pas ouvre un arbitrage écrit dans le rapport, pas un correctif silencieux.
Une décision de 5c est déjà rouverte, et tranchée, avant de jouer : la borne de `time-range` fermée
à cinq ans, que DAVx⁵ exerce à chaque synchro avec son réglage par défaut (décision 5), est servie
ouverte à partir de la vague de l'étape 4 (décision 11). La requête part dès le scénario 2 ; le cas
qui la prouve est au scénario 11, et c'est une vérification, pas un arbitrage.

### 8. Apple sans appareil : `ical-client.xml`, puis un rejeu de traces in-process

Aucun iPhone ni Mac n'est disponible pour la campagne. La troisième voie du cadrage — le rejeu de
traces — est retenue, en deux couches dont le rapport dira ce que chacune prouve.

**La première est déjà dans la liste des suites, et elle est mince.** `ical-client.xml` est, selon
sa propre description, là pour « Emulate the common behaviors of the iCal client » — le client
**macOS**, pas iOS — et rien de plus : **six `PROPFIND`**, deux sans authentification sur le
principal (`401` **exigé**), deux authentifiés sur le principal (`207`), deux en `Depth: 1` sur le
home (`207`). Ni `REPORT`, ni `PUT`, ni `PROPPATCH`, et seul le **statut** est vérifié — aucune
assertion sur les propriétés. Elle tourne au même titre que les autres et son résultat est compté
avec elles, mais le rapport ne lui fait pas dire plus que ça : c'est la seconde couche qui porte le
poids. Ce que le fichier apporte de plus précieux, ce sont ses **corps**, dans
`Resource/CalDAV/ical-client/` : cinq requêtes écrites par Apple, la seule provenance de première
main dont nous disposons.

**La seconde entre dans le dépôt** : `AppleDiscoveryReplayTests`, en tests d'intégration in-process
sur `DavTestServer`, rejoue verbatim la séquence d'appairage des clients Apple —

- `PROPFIND Depth: 0` sur `/`, l'hôte nu : c'est la **première** requête d'iOS, avant
  `.well-known` et avant le `SRV` (témoignage d'utilisateur dans la discussion stalwart nº 3259 — la
  seule source, et ce n'est pas une documentation Apple —, où un `302` vers autre chose qu'un
  multistatus fait abandonner le client en silence ; RFC 6764 § 6 **permet** d'ailleurs (`MAY`) le
  repli sur `/`, et seulement après un `404`). Chez nous `/` et `/dav/`
  répondent tous deux en `ServiceRoot` : `401` sans identifiants, `207` avec `current-user-principal`
  sinon. Le test joue **les deux chemins** et les nomme,
- `PROPFIND Depth: 0` sur `/.well-known/caldav` → `301`, et la même chose sur
  `/.well-known/caldav/` avec la barre finale, qui est la forme que `ccs-caldavtester` envoie — et
  qu'**aucun test de `WellKnownControllerTests` n'envoie aujourd'hui** ; le rejeu est le premier.
  **`PROPFIND`, pas `GET`** : c'est par là qu'iOS, DAVx⁵ et Thunderbird ouvrent la découverte, et
  `WellKnownController` est verbless précisément pour ça — un rejeu qui commencerait par un `GET`
  ne rejouerait pas la séquence qu'il prétend rejouer. Le `Location` rendu est `/dav/`, relatif,
  et le test assert que `/dav/` répond comme `/` (premier point),
- `PROPFIND Depth: 0` sur le principal, avec les **corps verbatim** de `ical-client/client1/1.xml`
  (iOS : `calendar-home-set`, `calendar-user-address-set`, `schedule-inbox-URL`,
  `schedule-outbox-URL`, `dropbox-home-URL`, `notifications-URL`, `displayname`) et de
  `client2/1.xml` (iCal 10.6 : les mêmes — `notification-URL` au singulier — plus
  `principal-collection-set`, `principal-URL`, `supported-report-set`). Ce que nous ne servons pas
  — `schedule-*`, `dropbox-home-URL`, `notification(s)-URL` — sort en `404` propstat.
  `email-address-set` et `resource-id` viennent de la liste **CardDAV** d'iOS chez sabre, pas de
  ces corps : ils n'entrent pas,
- `PROPFIND Depth: 1` sur le home, avec les corps verbatim de `client1/2.xml` (iOS, sept
  propriétés : `getctag`, `displayname`, `calendar-description`, `calendar-color`,
  `calendar-order`, `resourcetype`, `calendar-free-busy-set` — pas celles que sabre attribue à
  iOS, le fichier fait foi) et de `client2/2.xml` (iCal, vingt-six, dont
  `supported-calendar-component-set`, `current-user-privilege-set`, `calendar-timezone`, `owner`,
  `quota-available-bytes`, `quota-used-bytes` et une brassée de propriétés CalendarServer). Le
  `sync-token` et `default-alarm-vevent-datetime` que sabre attribue à iCal 10.9.2 s'ajoutent en
  un troisième corps, sourcé comme tel. **Un `Depth: 1` rend deux sortes de `response` et l'attente
  n'est pas la même sur les deux** : la table `DavResourceKind.CalendarHome` de `CalDavProperties`
  ne sert que quatre propriétés — `resourcetype`, `displayname`, `supported-report-set`,
  `current-user-principal` —, donc sur la `response` **du home lui-même** tout le reste, `getctag`,
  `owner`, `calendar-color` et `sync-token` compris, sort en `404` propstat ; c'est sur les
  `response` **des agendas** que les propriétés que nous servons répondent `200` — sur le corps iOS,
  six des sept, `calendar-free-busy-set` en `404` ; sur les corps iCal et sabre, tout sauf
  `quota-*`, `default-alarm-vevent-datetime`, `calendar-free-busy-set` et les propriétés
  CalendarServer. Le test relève les deux séparément, par `href` ; une attente unique serait
  fausse d'un côté ou de l'autre,
- `REPORT sync-collection` sur un agenda, avec un `<sync-token/>` vide — la forme de la
  synchronisation initiale de RFC 6578 § 3.4, celle que Thunderbird envoie ; aucun corps
  `sync-collection` d'un client Apple n'est public — et sans `Depth`, faute de source qui le
  montre chez Apple (le `Depth: 1` de Thunderbird est accepté par 5c et n'a pas à être rejoué
  ici), puis `calendar-multiget` sur ce qu'il rend,
- `calendar-query` avec un `time-range` sur le `comp-filter VEVENT`, la forme du chargement initial
  d'iOS, **à `start` seul** comme dans la trace sabre — dont le `prop` ne demande que `getetag` et
  `resourcetype`, le contenu venant ensuite par `calendar-multiget` —, la forme que la décision 11
  sert ouverte,
  et le test assert qu'un événement unique posé à plus de cinq ans est dans la réponse. La
  variante à `comp-filter VALARM` n'entre que si une trace la montre : aucune des sources
  ci-dessous ne l'atteste, et un corps sans provenance n'a pas sa place ici,
- `PROPPATCH` de `calendar-color` **avec son attribut `symbolic-color`** (forme exacte d'iOS et de
  macOS, stalwart nº 1611 : `<calendar-color symbolic-color="green">#63DA38</calendar-color>`), que
  5c ignore et lit en `200` ; de `calendar-order`, forme sabre ; et de `default-alarm-vevent-date`,
  que macOS envoie dès qu'on touche aux alertes par défaut (corps cité dans sabre nº 935) et que 5c
  refuse en `403` propstat. `default-alarm-vevent-datetime` n'est attestée qu'en lecture (la liste
  `PROPFIND` d'iCal 10.9.2) : elle n'entre pas dans le `PROPPATCH` rejoué.

Chaque corps porte en commentaire **d'où il vient** — `Resource/CalDAV/ical-client/`, sabre, une
discussion publique nommée. Un corps sans provenance est une supposition déguisée en test, et c'est
exactement ce que cette couche existe pour ne pas être.

Ce qu'elle prouve : que nos réponses ne font pas tomber la séquence, et qu'une propriété qu'un
client Apple demande mais que nous ne servons pas — sur le home `quota-available-bytes`,
`quota-used-bytes`, `default-alarm-vevent-datetime`, `calendar-free-busy-set` ; sur le principal
`schedule-inbox-URL`,
`schedule-outbox-URL`, `dropbox-home-URL`, `notifications-URL` — sort en `404` propstat plutôt qu'en
`500` ou en silence. `getctag`, en revanche, **est servie sur un agenda** (`CalDavProperties`, dans
la même table que `sync-token`) : son test assert un `200` et une valeur sur la `response` de
l'agenda, et un `404` propstat sur celle du home, qui ne la sert pas — l'inverse ferait passer un
test faux pour une mesure. Ce qu'elle ne prouve pas : qu'un iPhone en fasse quelque chose. Et ce
qu'elle **coûte**, à écrire dans le rapport : sabre nº 935 rapporte que macOS Calendar, sur un `403`
au `PROPPATCH` de `default-alarm-vevent-date`, « stops after that » — un coût plus lourd que celui de
`calendar-order`, et qui ne se mesure que sur un appareil. Le rapport l'écrit en ces termes, comme
4d l'a fait pour Contacts.app.

### 9. Ce que les suites laissent derrière elles, et le purge comme filet

L'outil nettoie plus qu'il n'y paraît, et la spec doit le décrire tel qu'il est plutôt que tel
qu'on le craint :

- toute requête portant `end-delete="yes"` — dans un `<start>` comme dans un test — est reprise par
  un `DELETE` en fin de fichier (`caldavtest.py`, `doenddelete`). C'est le cas de **tout** ce que
  `mkcalendar.xml` crée (`caltest1-3`, `vevent-only`, `vtodo-only`, `vevent-vtodo` — `nobody`, lui,
  n'est créé que dans `Single component calendars`, gardée par `split-calendars`, donc jamais), de
  `calendar2` (`errors.xml`, `copymove.xml`), de `calendar2`/`calendar3` (`aclreports.xml`), de
  `calendar-none`/`calendar-us` (`floating.xml`), de `synccalendar1`/`synccalendar2`
  (`sync-report.xml`) et de `calendar 2`/`calendar 3` (`encodedURIs.xml`, envoyés en `%20` : l'outil
  encode ses URL par défaut, ce que le serveur voit est un nom d'agenda avec une espace décodée).
  `copymove.xml` ne crée pas de `calendar3` ; sa troisième suite crée `caltest3` puis tente de le
  déplacer en `caltest4` (un `MOVE`, donc `405` chez nous : `caltest4` ne naît pas ;
  `caltest1`/`caltest2` jamais non plus, t1 étant `ignore="yes"`) et son `<end>` fait ses quatre
  `DELETE` de `caltest*`, dont trois sans objet — le quatrième, `caltest3`, existe bel et bien, et
  c'est là qu'il disparaît. Comme un `DELETE` d'agenda secondaire le supprime (5c décision 11), ce
  nettoyage fonctionne chez nous, et il joue même quand la requête marquée `end-delete` a échoué —
  l'outil enregistre le nettoyage **avant** d'envoyer. Une requête écartée par ses propres
  `<feature>`, en revanche, sort avant cet enregistrement : son `end-delete` n'est alors jamais posé
  ;
- trois fichiers retenus ont en plus un bloc `<end>` : `copymove.xml` (`DELETE copy1.ics`,
  `move1.ics` et `caltest1-4`), `nonascii.xml` (`DELETEALL $calendarhome2:/outbox/`, du bruit) et
  `ctag.xml` — **qui n'est pas du bruit** : son `<end>` fait quatre `DELETEALL`, sur
  `$calendarpath2:/`, `$inboxpath1:/` et `$inboxpath2:/` (bruit, en effet) mais **d'abord sur
  `$calendarpath1:/`, c'est-à-dire sur notre `default`, qu'il vide**. Tout fichier lancé après
  `ctag.xml` repart donc d'un `default` vide. Aucun des fichiers qui le suivent dans
  `suites-caldav.txt` ne dépend du contenu de `default` — chacun pose le sien dans son `<start>` —
  mais c'est vérifié plutôt que supposé, et le purge de fin de section ne couvre pas ce cas : il
  agit **avant** le passage, pas au milieu. Si l'ordre des suites change un jour, cette ligne est
  celle qu'il faut relire ;
- `synccalendar3`/`synccalendar4` ne sont créés que dans des tests gardés par `sync-report-home` —
  jamais, donc.

Reste **`movecopy`** (`ctag.xml`, sans `end-delete`, repris par un test `-1` « Clean-up » qui joue
toujours mais **sans `<verify>`** : un `DELETE` qui échouerait compterait OK et laisserait l'agenda)
et les événements que les tests laissent dans `default` : aucun `<start>` retenu ne fait de
`DELETEALL`, et un compte de `multiget` ou de `sync-collection` sur un `default` pollué par la
campagne précédente est un faux échec. Le plafond `CalendarStore.MaxPerUser` (20) n'est pas
approché : au plus une poignée d'agendas coexistent à un instant donné.

Le purge fait un `PROPFIND Depth: 1` sur le home d'agendas, `DELETE` chaque agenda sauf `default`,
puis `DELETE` sur `default` — qui le **vide** — avant de lancer. Il ne touche **que** l'agenda :
sous `-Protocol CardDAV` il n'a rien à faire, sous `Both` il fait ce qu'il fait en CalDAV et laisse
le carnet aux `<start>` des suites CardDAV, qui le nettoient déjà depuis 4d. C'est du nettoyage
d'environnement de test, pas une fonctionnalité serveur : rien n'est ajouté à l'API pour le servir,
il n'utilise que la surface que 5c ouvre déjà. C'est un filet contre ce que le nettoyage de l'outil
ne couvre pas — `movecopy`, `default`, et tout `<end>` qu'une trace Python aura empêché de jouer —,
et comme il est **obligatoire avant chaque passage complet**, il est le **défaut** : `run.ps1` purge
sans qu'on le lui demande, et `-NoPurge` seul l'en dispense. Un README ne fait pas respecter une
obligation ; un défaut, si. Le README dit alors l'inverse — dans quel cas s'en passer : le
diagnostic d'un test isolé qu'on veut rejouer sur l'état laissé par le précédent, jamais un passage
complet ni un rejeu dont on consigne le résultat. Le rejeu du `<start>` de `reports.xml` de l'étape
2 est de la seconde espèce : il se fait **avec** le purge.

### 10. Deux blocs `<start>` que le serveur ne peut pas satisfaire : copies locales

Une requête d'un bloc `<start>` est vérifiée de force en 2xx — et un `<verify>` qu'on y écrirait
serait purement ignoré, l'outil passant `doverify=False` à toutes les requêtes d'un `<start>` —, et
le premier échec tue le fichier — l'outil écrit « Start items failed - tests will not be run »,
compte **une** erreur (`errors=`, décision 6) et ne joue **aucun** test (`caldavtest.py:116-119`,
`751-754`). 4d l'a rencontré deux fois dans son premier passage. Deux fichiers retenus y tombent
chez nous, à coup sûr :

- `reports.xml` : six `PUT` non gardés sur `$taskspath1:/101.ics`…`106.ics` dans son `<start>`,
  **et un septième**, s15, `PUT $calendarpath1:/15.ics` ← `reports/put/15.txt`, dont le seul
  composant est un **`VFREEBUSY`**. Il est gardé par `exclude-feature split-calendars` — feature
  éteinte chez nous, donc il part — et `IcsGuards` le refuse comme un `VTODO`. Les sept se
  retirent ensemble ; en oublier un, c'est tuer le fichier pour la même raison une ligne plus loin ;
- `delete.xml` : un `PUT $taskspath1:/1todo.ics`.

Sans correctif, le plus gros fichier de la campagne — principale mesure de `free-busy-query`
(`floating.xml` et `timezones.xml` en envoient aussi, « Le harnais »), seule mesure du `VALARM` et
de `limit-recurrence-set`, principale mesure du `time-range` et **seule** mesure de l'`expand`,
celui de `floating.xml` visant le home (décision 5) — ne mesure rien, et le rapport ne pourrait que
le constater. Le correctif est un correctif de **harnais** (décision 4) :
`tools/caldavtester/suites/CalDAV/` reçoit une copie de ces deux fichiers, **identique à l'amont à
ceci près** que les requêtes `$taskspath1:` de leur `<start>` et le `PUT` `VFREEBUSY` s15 de
`reports.xml` sont retirés ; le diff contre l'amont est versionné à côté, et `suites-caldav.txt`
référence ces copies par leur chemin. L'outil accepte un chemin hors de son arbre, mais pas pour la
raison qu'on lui prêterait : `_normPath` (`manager.py:322`) ne prend « tel quel » qu'un chemin
commençant par `.` ou `/`, et un chemin Windows `D:\…` tombe dans la branche
`os.path.join("scripts/tests", f)` — qui le rend intact parce que `ntpath.join` se réinitialise sur
une lettre de lecteur. C'est écrit ici pour que personne ne « corrige » `run.ps1` sur la lecture
inverse, et c'est aussi pour cette raison que `run.ps1` résout les entrées de `suites-caldav.txt` en
chemin absolu avant de les passer. Les `<filepath>` `Resource/…` des copies, eux, continuent de se
résoudre parce que `run.ps1` lance l'outil depuis son propre répertoire. Les tests `VTODO` restés
dans le corps de ces fichiers échouent alors un par un, en `404` nommable, comme ceux de `put.xml`.

Ce n'est pas un portage de l'outil : pas une ligne de Python ne change, et les copies ne modifient
aucun test — elles retirent des préparatifs impossibles. Les tests qui dépendaient de ces
préparatifs échouent ensuite un par un et nommément (décision 5 : `free-busy reports` t2 pour le
`VFREEBUSY`, les `404` pour les `VTODO`). **Cinq** autres `<start>` sont surveillés sans être
touchés d'avance :

- `encodedURIs.xml` : **deux** `MKCALENDAR` sur un nom à espace (`$calendarhome1:/calendar 2/` et
  `/calendar 3/`, encodés `%20` sur le fil) ;
- `aclreports.xml` : **deux** `PROPPATCH` dont le `207` est un 2xx, plus deux `MKCALENDAR` ;
- `floating.xml` : un `MKCALENDAR` porteur d'un `calendar-timezone` en `TZID:US/Eastern` — celui
  de la tâche zéro, point 5. Ses six `PUT` à `TZID` **entre guillemets** (`floating/put/5.txt`,
  `6.txt`, `7.txt`, `13.ics`-`15.ics` : `DTSTART;TZID="US/Eastern":…`, `TZID="GMT"`) ne sont pas
  un second guet : la forme, licite par la grammaire générale des paramètres (RFC 5545 § 3.1,
  `param-value = paramtext / quoted-string`) sinon par l'ABNF propre au `TZID` (§ 3.2.19), a été
  **mesurée** sur Ical.Net 5.2.3 par le chemin du `PUT` — acceptée, guillemets retirés, occurrence
  juste ;
- `copymove.xml` : **trois** `PUT` d'un UID déjà présent dans `default` vers `calendar2`, que
  l'index `(calendar_id, uid)` doit laisser passer ;
- `errors.xml` : le même cas, une fois — trois requêtes non gardées et une quatrième portant un
  `exclude-feature no-duplicate-uids`, donc envoyée elle aussi, créent `calendar2` puis y écrivent
  `errors/6.ics` et `errors/7.ics`, dont les UID sont distincts et dont le doublon d'UID avec
  `$calendarpath1:/1.ics` vit dans un **autre** agenda.

Si l'un des cinq tue son fichier au premier passage, il reçoit le même traitement dans la vague,
et le rapport le dit.

Retirer les sept `PUT` ne suffit d'ailleurs pas à **garantir** que `reports.xml` sera joué : son
`<start>` garde **vingt `PUT` `VEVENT`** non gardés — dont trois à `RECURRENCE-ID`, deux d'entre
eux avec deux `VEVENT` du même UID —, et un seul refusé le tue comme les autres. Ce bloc-là est
donc rejoué seul en `-PrintResponses` avant que la mesure de départ ne soit figée — vingt
requêtes —, et son résultat est consigné à côté du passage. « À coup sûr » vaut pour ce que la
décision retire, pas pour ce qu'elle laisse.

### 11. Une borne de `time-range` absente est servie ouverte

Ce que le client demande, en clair : « tout ce qui se passe à partir du 9 juin », sans date de fin.
C'est la question que DAVx⁵ pose à chaque synchronisation avec son réglage par défaut, et celle
qu'iOS pose au premier chargement (décision 5). RFC 4791 § 9.9 dit qu'une borne absente vaut
l'infini, et § 7.8 que la réponse contient **chaque** objet qui correspond. 5c refermait cette borne
à cinq ans en silence (`CalendarQueryFilter.ParseTimeRange`, `OccurrenceExpander.MaxSpan`) ; un
événement unique posé plus loin n'arrivait jamais sur le téléphone, et rien ne le disait.

La réponse retenue est la troisième des trois possibles, la seule qui réponde juste sans cacher sa
limite :

- **Un événement unique** est comparé à son propre intervalle. Aucune fenêtre ne le borne : le
  passeport de 2032 sort.
- **Une série** est retenue dès qu'une de ses occurrences tombe à partir de la borne présente, sans
  être déroulée au-delà — une série sans fin correspond toujours à une question sans fin. Le code
  le fait déjà ainsi : `CalendarQueryFilter.Matches` passe par `OccurrenceExpander.Overlaps`, qui
  marche paresseusement et s'arrête au premier hit. Le plafond d'occurrences (`CapFor`) ne sert
  qu'à protéger une série finie très dense sans aucun hit ; sur une borne ouverte il se calcule sur
  `from + MaxSpan` plutôt que sur l'infini. `expand`, lui, exige déjà ses deux bornes (§ 9.6.5,
  `CalendarDataRequest.Parse`).
- **La présélection en base** suit sans effort : `DavCalendarReader.CandidatesAsync` accepte déjà
  une borne nulle de chaque côté, et une série sans fin porte `IcsProjector.NoEnd` (2100) en
  `LastOccurrence`, pas `start + 5 ans`. C'est `CalendarQueryFilter.ParseTimeRange` qui invente la
  borne (`TimeRangeSpec` à deux dates non nulles) et `CalendarQuerySpec.Preselection` qui la
  transmet : ce sont eux qui changent, et les deux tests de `CalendarQueryFilterTests` qui figent
  aujourd'hui la fermeture à `MaxSpan` s'inversent.
- **Ce qui ne change pas**, et c'est écrit pour ne pas être redécouvert : une fenêtre à deux bornes
  plus large que cinq ans reste refusée en `valid-filter` (aucun client visé n'en envoie —
  « tous les événements » chez DAVx⁵ bascule sur `sync-collection`) ; le `time-range` d'un
  `comp-filter VALARM` garde la fermeture à cinq ans, et `free-busy-query` continue de **refuser**
  une borne absente (`FreeBusyReport` lit son `time-range` avec `bothRequired: true`, `400`).
  Ces trois restes sont des **non-conformités connues** sous `calendar-access` — § 7.10 renvoie
  `free-busy-query` au `time-range` de § 9.9, bornes `#IMPLIED` comprises —, qu'aucun client visé
  n'exerce ; ils sont portés dans `calendar-5d-residuals.md` avec ce statut, pas comme du conforme.
  Le premier des trois est en outre **incohérent avec ce que la décision livre**, et l'écrire vaut
  mieux que le laisser trouver : après elle, « à partir de mars 2026, sans fin » est servi, tandis
  que « mars 2026 → mars 2033 », strictement plus étroit, reste refusé. La machinerie paresseuse que
  la décision met en place rendrait ce second cas presque gratuit ; la tranche ne le fait pas —
  elle ne rouvre que ce qu'un client visé exerce — mais elle ne prétend pas que ce soit cohérent.

Le correctif entre dans la vague unique de l'étape 4, avec un test qui pose un événement unique à
plus de cinq ans et un `calendar-query` à `start` seul, et qui rougit si l'événement manque ; un
second fige qu'une série sans fin correspond sans être déroulée. Le scénario 11 le vérifie ensuite
sur le téléphone, et le rejeu Apple (décision 8) joue la même forme à `start` seul. La ligne des
« bornes ouvertes fermées à cinq ans » de `calendar-5c-residuals.md` se referme à la clôture pour
le `calendar-query`, et renvoie au résidu 5d pour le reste.

## Le harnais

`serverinfo.template.xml` porte déjà toutes les clés agenda — c'est le gabarit amont — mais avec les
valeurs de CalendarServer. Elles sont repointées :

| Clé | Valeur 5d |
|---|---|
| `$calendarhome1:` | `/dav/calendars/{guid}` — l'amont la compose sur `$calendars_uids:` ; posée en littéral plutôt que laissée à la composition, pour qu'un futur retour en arrière sur `$calendars_uids:` ne la déplace pas en silence |
| `$calendar:` | `default` |
| `$calendarpath1:` | `/dav/calendars/{guid}/default` |
| `$userguid1:` | `{guid}` — l'amont laisse `10000000-0000-0000-0000-000000000001` |
| `$principaluri1:` | `/dav/principals/{guid}/` |
| `$principals_uids:` | `/dav/principals/` — l'amont vaut `$principalcollection:__uids__/` ; c'est **elle** qui compose `$principaluri1:`, donc la repointer corrige celui-ci sans qu'il faille le poser en littéral |
| `$principals_users:` | `/dav/principals/` — l'amont vaut `$principalcollection:users/`, une collection de type que nous ne servons pas |
| `$calendars_uids:` | `/dav/calendars/` — l'amont vaut `$calendars:__uids__/`, idem |
| `$calendars_users:` | `/dav/calendars/` — l'amont vaut `$calendars:users/`, idem ; lue par `propfind.xml` t4 seul, pour la même mesure que t2 et t3 |
| `$email1:` | `{email}` |
| `$cuaddr1:` | `mailto:{email}` |

`$email1:` et `$cuaddr1:` corrigent un défaut hérité du gabarit amont, qui vaut
`$userid1:@example.com` : notre `$userid1:` **est** déjà l'adresse, et la valeur amont donnait
`a@b.tld@example.com`. Correction juste, et **mesurée par rien** : aucun fichier retenu ne lit
`$email1:`, `$cuaddr1:` ni `$cuaddrurn1:` — les vérifications de `calendar-user-address-set` sont
dans la suite `extended-principal-search` d'`aclreports.xml`, éteinte. Elle est faite pour que la
clé soit juste le jour où quelque chose la lira, pas pour le premier passage.

`$userguid1:` et `$principaluri1:` referment une dette de 4d, que son rapport § 4 laissait ouverte
en toutes lettres (« `$principaluri1:` resté à la forme `__uids__` de CalendarServer (harnais) ») :
les deux dernières lignes rouges de `CardDAV/current-user-principal.xml` en venaient. Côté agenda,
c'est la suite `/principals/` de `current-user-principal.xml` qui compare la valeur rendue à
`$principaluri1:`. `$principal1:` est déjà juste depuis 4d, et `$cuaddrurn1:`
(`urn:x-uid:$userguid1:`) se corrige tout seul par `$userguid1:`.

**`$principals_users:` et `$calendars_uids:` sont la même dette, restée invisible en 4d, et
repointer `$principaluri1:` seul ne suffit pas** — c'est le point vérifié fichier par fichier plutôt
que supposé. La seule suite mesurante de `current-user-principal.xml` et le test 2 de
`supported-report-set property` d'`aclreports.xml` n'adressent pas `$principaluri1:` : ils envoient
leur `PROPFIND` **à** `$principals_users:`, que l'amont compose en `/dav/principals/users/` — une
collection par type d'enregistrement propre à CalendarServer, quand `DavPrincipalController`
n'expose que `principals` et `principals/{userId:guid}`. Sans repointage, les **quatre** tests
concernés — les trois de la suite `/principals/` et le test 2 de `supported-report-set property` —
répondent `404` et restent inclassables, exactement le mode de panne que 4d avait laissé ouvert. De
même, `propfind.xml` test 3 envoie son `PROPFIND` sur `$calendars_uids:` =
`/dav/calendars/__uids__/` et son test 4 sur `$calendars_users:` = `/dav/calendars/users/` :
repointés, ils mesurent notre `propfind-finite-depth` comme le test 2 le fait déjà sur
`$calendars:`. Aucun des trois ne porte d'en-tête `Depth` — la profondeur infinie n'y est
qu'implicite, par le défaut de RFC 4918 —, et c'est bien cette précondition qu'ils attendent. Une
seule clé conservée compose sur l'une des trois dans les fichiers retenus : `$i18ncalendarpath:`
(sur `$calendars_uids:`), lue deux fois par `nonascii.xml` dans un test à `$i18nid:` qui est du
bruit (`404` avant comme après). `$principal1noslash:`, `$calendarhomealt1:` et `$calendarpathalt1:`
ne sont lus par aucun. Le repointage n'a donc pas d'effet de bord qui compte.

Deux clés que la table listait comme du travail à faire n'en sont plus : le correctif H1 de 4d a
déjà posé `$calendar_sync_extra_items:` à `[]` et `$calendar_sync_extra_count:` à `1`.
`$calendars:` n'a rien à changer non plus — `$root:calendars/` vaut déjà `/dav/calendars/`.

`$calendar_home_items_initial_sync:`, en revanche, porte toujours la valeur amont
(`[-,$calendar:/,$tasks:/,$inbox:/,$outbox:/,$freebusy:,$notification:/]`) et **reste telle
quelle** : ses vingt-quatre lectures sont toutes dans `sync-report.xml`, et toutes dans des tests
gardés par `sync-report-home`, éteinte — vérifié test par test. Clé inerte, donc, mais pas clé
corrigée : la distinction compte le jour où `sync-report-home` s'allumerait.

Sont **retirées** : `$polls:` et `$pollspath1:`, `$timezoneservice:`, `$timezonestdservice:`,
`$directory:`, `$add-member:`, `$attachments:`, `$servertoserver:`. Aucune suite retenue ne les lit
et aucune clé conservée ne les compose — les deux conditions comptent autant l'une que l'autre.

Sont **gardées** : `$tasks:`, `$inbox:`, `$outbox:`,
`$dropbox:`, `$notification:` et `$freebusy:`. Le raisonnement « une clé laissée là ferait passer un
échec pour du bruit » est inversé sur celles-ci, parce que des suites retenues les lisent —
`$taskspath1:` (= `$calendarhome1:/$tasks:`) dans `put.xml` (22), `reports.xml` (30),
`recurrenceput.xml` (7), `mkcalendar.xml` (3) et `delete.xml` (2) ; `$inbox:`, `$outbox:`,
`$dropbox:`, `$freebusy:` et `$notification:` dans `sync-report.xml` seul ; `$inbox:` et
`$outbox:` dans `sync-report.xml` et dans un test de `get.xml` que `regular-collection` garde, donc
jamais joué ; `$inboxpath1:` dans `sync-report.xml` et `ctag.xml` (suite `Scheduling`) ;
`$outboxpath1:` dans `errors.xml` et `nonascii.xml`. Une clé
absente n'est pas remplacée par du vide : `ccs-caldavtester` la laisse en **texte littéral**, et la
requête part sur `/dav/calendars/{guid}/$tasks:`. Gardée, elle donne un `404` lisible sur une
collection que nous ne servons pas — un échec nommable dans un test, ligne § 5 de la table des
divergences ; dans un `<start>`, un fichier tué, que la décision 10 traite. Retirée, elle donne une
URL absurde et un échec inclassable.

**Les `<features>` allumées** : `caldav`, `sync-report`, `well-known`, `current-user-principal`,
`expand-property`, `ctag`, `supported-component-sets-one`, `COPY Method`, `MOVE Method`. `carddav`
reste allumée, le fichier servant les deux protocoles ; `limits`, allumée en 4d, **s'éteint** : les
seuls fichiers de tout `scripts/tests/` qui la lisent sont les deux `limits.xml`, désormais exclus
l'un et l'autre, et une feature que rien ne lit dit une chose fausse pour rien — nous n'avons pas
les plafonds `max-collections`/`max-resources` qu'elle annonce.

Trois de ces lignes ne vont pas de soi :

- **`COPY Method` et `MOVE Method` sont allumées alors que nous ne servons ni l'un ni l'autre.**
  C'est délibéré : elles gardent la totalité de `copymove.xml` (ses trois suites), les deux suites
  `COPY` et `MOVE` d'`errors.xml` (garde au niveau **suite**) et, au niveau **test**, les tests
  concernés d'`encodedURIs.xml` et de `ctag.xml`. Éteintes, tous ces tests sautent et la
  non-conformité « `405`, `Allow` le dit » n'est mesurée nulle part ; allumées, elle est mesurée
  partout où un client pourrait la rencontrer. **Le prix, compté plutôt qu'estimé — `ignore="yes"`
  et gardes de suite déduits : 39 tests** —

  | fichier | tests qui tournent |
  |---|---|
  | `copymove.xml` : suites `COPY` (9), `MOVE` (6), `COPY/MOVE and Properties` (2, dont t1 `ignore="yes"`) | 9 + 6 + 1 = 16 |
  | `errors.xml`, suites `COPY` et `MOVE` (13 chacune, moins les 8 gardées par `regular-collection`, moins `COPY` t9-t11 et `MOVE` t10-t11 en `ignore="yes"`) | 2 + 3 = 5 |
  | `encodedURIs.xml` : t4-t10 de chacune des deux suites d'agenda — la suite `regular resource` entière est gardée par `regular-collection` | 7 + 7 = 14 |
  | `ctag.xml`, t6-t9 de `PUT/DELETE/COPY/MOVE` | 4 |

  Une nuance à préécrire, sans quoi on relira dix échecs qui n'ont rien à dire : les tests **6 à 10**
  des deux suites d'agenda d'`encodedURIs.xml` sont des `REPORT` (multiget, query, free-busy) sur
  des chemins **issus du `MOVE`** qui les précède. Ils échouent en dommage collatéral d'un `405`,
  pas sur l'encodage qu'ils prétendent mesurer, et se trient d'une seule ligne collective — t8 des
  deux suites a une seconde cause, un multiget adressé au home (décision 5).
- **`ctag` est allumée et `ctag.xml` lancée** parce que nous **servons** `getctag`
  (`CalDavProperties`, à côté de `sync-token`). C'est la seule suite qui la mesure ; l'éteindre
  reviendrait à ne pas mesurer une propriété que nous rendons. Ses **quatre** tests `COPY`/`MOVE`
  (t6 à t9 de `PUT/DELETE/COPY/MOVE`) échoueront comme les autres, son `<start>` ajoute `movecopy`
  aux agendas résiduels et son `<end>` vide `default` (décision 9).
- **Deux features ne mesurent rien, et c'est écrit ici pour qu'on ne le redécouvre pas.**
  `Extended MKCOL` d'abord : vérification faite sur tout `scripts/tests/`, **aucun fichier de
  l'outil ne la lit**, CardDAV compris — `CardDAV/mkcol.xml` cherche le jeton `extended-mkcol` de
  l'en-tête `DAV:` par le callback `header`, avec un motif **nié**, dans un test porté par un
  `ignore` : ni la feature, ni même une mesure. Elle n'est donc **pas
  ajoutée** au gabarit, et la ligne de `calendar-5c-residuals.md` qui l'annonçait comme une feature
  à allumer se referme sur ce constat plutôt que sur un résultat. `supported-component-sets-one`
  ensuite : ses seules lectures sont un `<verify>` de la suite `supported-component-sets` de
  `mkcalendar.xml` — elle-même gardée par `supported-component-sets`, éteinte — et `polls.xml`,
  exclue. Elle est quand même **allumée** — elle dit ce que nous sommes, et c'est elle qui
  choisirait la bonne vérification le jour où cette suite s'allumerait —, mais son résultat au
  premier passage sera vide, ce qui n'est pas un défaut à trier.

Éteintes, chacune avec sa raison en commentaire dans le fichier :

- `no-duplicate-uids` — `duplicate_uids.xml`, seul fichier à la **requérir**, mesure le refus d'un
  même UID **entre deux agendas d'un même home**, la règle CalendarServer ; RFC 4791 § 5.3.2.1 scope
  `CALDAV:no-uid-conflict` à la collection visée, et c'est ce que fait l'index `(calendar_id, uid)`
  de 5a. Dans les fichiers retenus elle n'apparaît qu'en `exclude-feature`, sur des tests
  `COPY`/`MOVE` et sur quatre `PUT` de `<start>` (décision 5) : éteinte, les premiers tournent et
  mesurent le `405` avec les autres, les seconds partent et doivent passer.
- `sync-report-home` — le home ne sert pas `sync-collection`.
- `regular-collection` — un `MKCOL` nu dans un home d'agendas est refusé (5c décision 11).
- `supported-component-sets` et `split-calendars` — les agendas mono-composant de CalendarServer,
  et la propriété plurielle qui va avec ; c'est `supported-component-sets-one` qui dit ce que nous
  sommes, allumée pour ça même si aucune suite retenue ne la lit (ci-dessus).
- `limits` — allumée en 4d, éteinte ici : plus rien ne la lit une fois les deux `limits.xml`
  exclus, et elle annonce des plafonds CalendarServer que nous n'avons pas (ci-dessus).
- `sync-report-limit` — **pas parce que `DAV:limit` n'est pas servi** : il l'est, avec le `507
  number-of-matches-within-limits` de RFC 6578 § 3.7. La feature n'est lue qu'en `exclude-feature`,
  sur un seul test (`sync-report.xml`, `limited reports` t1), qui adresse le **home** : éteinte, il
  tourne et échoue pour la raison « home sans `sync-collection` » ; allumée, il saute. Éteinte,
  donc, avec son échec préécrit (décision 5) — aucun des deux réglages ne mesure la limite.
- `remove-duplicate-alarms`, `directory listing`, `only-proxy-groups` — extensions CalendarServer
  qui gardent des tests de `put.xml`, `get.xml` et `expandproperty.xml`.
- `timerange-low-limit`, `timerange-high-limit` — leurs sept tests attendent un
  `CALDAV:min-date-time`/`max-date-time`, la limite de date **configurée** de CalendarServer, que
  nous n'avons pas ; les allumer mesurerait un comportement que nous n'avons pas choisi d'avoir
  (décision 5).
- `quota`, `prefer`, `brief`, `resource-id`, `json-data`, `add-member`, `auth-on-root`, `own-root`,
  `timezones-by-reference`, `timezone-service`, `query-extended`, `ACL Method`, tous les `REPORT`
  de principaux et **`extended-principal-search`** — qui n'en porte pas le nom mais garde à elle
  seule la suite `Extended principal-property-search REPORT` d'`aclreports.xml`, quatorze tests —,
  plus le bloc entier de l'ordonnancement, du partage, des pièces jointes gérées et de `vpoll` —
  c'est 5e ou ce n'est pas ce projet.

Cette liste est **exhaustive pour les features qui gardent une suite retenue** : une feature absente
du gabarit vaut éteinte, mais une feature éteinte sans être nommée ici est une décision non prise.

**Ce que ces features font au carnet.** Le gabarit sert les deux protocoles, et les fichiers
`CardDAV/*.xml` lisent une seule des features que 5d allume : `caldav`, dans **`CardDAV/limits.xml`**
(`require-feature caldav` + `limits`), que `suites.txt` lance depuis 4d en le croyant « ignoré par
l'outil lui-même ». `caldav` allumée, il se réveillerait : deux `PROPFIND` attendant
`CS:max-collections` et `CS:max-resources`, propriétés CalendarServer, soit deux échecs CardDAV de
plus au passage `-Protocol Both` qui ne viendraient pas du serveur. Il est donc **retiré de
`suites-carddav.txt`**, nominativement et avec sa raison, comme son jumeau CalDAV l'est ici — et
c'est fait à l'étape 1, **avant** le repère CardDAV, pour que ce repère et le passage final
comptent les mêmes fichiers. `ctag`, `expand-property`, `COPY Method`, `MOVE Method`,
`supported-component-sets-one`, `well-known`, `current-user-principal` et `sync-report` : les
cinq premières ne sont lues par aucun fichier CardDAV, les trois autres étaient déjà allumées en 4d.

**`suites-caldav.txt`**, lancées :

```
CalDAV/propfind.xml
CalDAV/proppatch.xml
CalDAV/put.xml
CalDAV/get.xml
suites/CalDAV/delete.xml     # copie locale (décision 10)
suites/CalDAV/reports.xml    # copie locale (décision 10)
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
CalDAV/ctag.xml              # son <end> vide `default` : rien de ce qui suit ne doit en dépendre
CalDAV/encodedURIs.xml
CalDAV/conditional.xml
CalDAV/copymove.xml
CalDAV/aclreports.xml
CalDAV/timezones.xml
CalDAV/ical-client.xml
```

Vingt-trois fichiers, **un par ligne** : l'ordre est celui du passage et il porte une contrainte —
le `<end>` de `ctag.xml` vide `default` en cours de route (décision 9) —, qu'une mise en colonnes
rendrait ambiguë. Quatre d'entre eux mesurent moins que leur nom ne le promet, et c'est écrit
dans le commentaire de `suites-caldav.txt` pour que le dénominateur soit lu juste : `get.xml` ne
joue qu'**un** test (`directory listing` garde ses deux suites de collection entières) ;
`nonascii.xml` a ses trois suites d'URI non-ASCII en `ignore="yes"` et sa suite `Copy/Move
Non-utf-8` gardée par `regular-collection` — restent **douze** tests joués : huit sur les données
non-ASCII et non-UTF-8, deux `POST` sur `$outboxpath1:` (de l'ordonnancement, que nous ne servons
pas) et deux `PUT with CN re-write` ; `conditional.xml` et `options.xml` ne portent que des
conventions CalendarServer (décision 5) et restent lancés pour que leurs requêtes partent.

`copymove.xml` tourne **en sachant** qu'elle échouera, comme `mkcol.xml` en 4d : son échec est la
mesure d'une divergence nommée, et un fichier qu'on ne lance pas ne mesure rien. Encore faut-il
qu'il puisse échouer — d'où `COPY Method` et `MOVE Method` allumées, sans lesquelles les trois
suites du fichier sautent et il ne mesure rien non plus. `aclreports.xml` est un cas différent et le
commentaire de `suites-caldav.txt` le dit : six de ses sept suites sont gardées par des features
qu'on n'allume pas (aucun client n'envoie ces rapports, `access-control` n'étant pas annoncé), et le
fichier n'est gardé que pour la septième, `supported-report-set property`, qui n'est gardée par
rien. Ses deux tests ne peuvent pas passer et leur verdict est écrit d'avance (décision 5) ; ce
qu'ils apportent est de faire **partir** les deux requêtes, sur l'agenda et sur la collection de
principaux, plutôt que de laisser cette surface muette.

**Le bruit multi-utilisateurs est nommé, fichier par fichier**, comme `suites.txt` le fait déjà côté
carnet — un second et un troisième compte ne sont pas définis, et leurs clés restent en texte
littéral dans la sortie :

- `expandproperty.xml` : seule la suite `Membership REPORT` est du bruit (`$principaluri21:` — sa
  clé la plus lue, huit fois —, `$gprincipaluri4-6:`, `$username6-10:`, `$groupname2-3:`), et elle
  n'est gardée par **aucune** feature : quatre de ses six tests partent et échouent sur des clés
  restées littérales. `Basic REPORT`, `Access Control` et `Invalid REPORTs`
  tournent sur `$principal1:` et `$calendarhome1:` et mesurent réellement notre `expand-property`.
- `current-user-principal.xml` : la suite sur `/` est gardée par `own-root` (sautée) ; celle sur
  `/principals/` mesure vraiment, à ceci près que ses tests `$principaluri2:`/`$userid2:` sont du
  bruit.
- `nonascii.xml` : les tests `$userid2:` et `$i18n*` sont du bruit. Les deux `POST` sur
  `$outboxpath1:` ne le sont pas : la clé est définie, la requête part, et le `404` sur une
  collection que nous ne servons pas est un échec nommable de la même famille que les
  `$taskspath1:`. Le reste mesure l'encodage des données (ci-dessus).
- `mkcalendar.xml` : le test 2 de la suite `MKCALENDAR read-free-busy privilege` (`$userid2:`).
- `ctag.xml` : trois des cinq tests de la suite `Scheduling` (`$inboxpath1:`) — ses t1 et t4
  mesurent, eux, `$calendarpath1:` et ne sont pas du bruit —, les tests en `$userid2:` /
  `$calendarpath2:` de la suite `PUT/DELETE/COPY/MOVE`, et les deux `DELETEALL` de son `<end>`
  adressés à `$userid2:`.
- `aclreports.xml` : les références à `$calendarhome2:` et `$principaluri2:` vivent dans des suites
  déjà gardées par des features éteintes ; la septième suite n'en porte aucune.

`caldavIOP.xml` est **exclue** pour cette raison : c'est un scénario d'interopérabilité à trois comptes (`$userid2:`, `$userid3:`, `$pswd2:`,
`$pswd3:`, `$calendarpath2:`, `$calendarpath3:`) qui passe en plus par `$inboxpath1:` à
`$inboxpath3:`, donc par l'ordonnancement. Aucun de ses tests ne peut mesurer quoi que ce soit ici.

`duplicate_uids.xml` est **exclue** avec sa raison : elle mesure la règle CalendarServer du refus
par home, pas la nôtre, qui est celle du RFC (décision 5, et `<features>` ci-dessus).

`bad-ical.xml` est **exclue** avec sa raison : son unique test itère sur le répertoire
`Resource/CalDAV/bad-ical/`, qui ne contient dans le dépôt qu'un `.gitignore` vide — les fichiers
invalides n'ont jamais été versionnés. L'outil écrirait « No iteration data - ignored » : un ignoré,
zéro test. Le `PUT` invalide reste couvert par les tests in-process de 5c.

Exclues, avec leur raison en commentaire : tout `implicit*` et `schedule*` (ordonnancement, 5e),
`freebusy.xml` (c'est un `POST` sur l'outbox, pas le rapport `free-busy-query`), `sharing-*`,
`managed-attachments*`, `polls.xml`, `partitioning-*`, `dropbox.xml`, `trash*`, `json.xml`,
`bad-json.xml`, `rscale.xml`, `vtodos.xml`, `timezoneservice.xml`, `timezonestdservice.xml`,
`webcal.xml`, `depthreports*.xml` (exige `regular-collection`), `directory*.xml`, `bulk.xml`,
`add-member.xml`, `quota.xml`, `limits.xml` — les plafonds `max-collections` et
`max-resource-size` de CalendarServer, exclue **nominativement** comme sa jumelle CardDAV, leur
`<feature>` `limits` s'éteignant avec elles —, `prefer.xml`, `brief.xml`, `resourceid.xml`,
`attachments.xml`,
`availability.xml`, `extended-freebusy.xml`, `freebusy-url.xml`, `default-alarms.xml`,
`alarm-dismissal.xml`, `privateevents.xml`, `privatecomments.xml`, `acl.xml`,
`calendaruserproxy.xml`, `proxyauthz.xml`, `servertoserver*.xml`, `recurrence-splitting.xml`
(extension de découpe CalendarServer), `server-info.xml`, `pretest.xml`, et
`collection-redirects.xml` — celle-ci malgré son nom : elle exige `directory listing` et teste un
`GET` de collection, pas le `308` de 5c.

Les deux listes couvrent la **totalité** de `scripts/tests/CalDAV/` : aucun fichier du répertoire
n'est ni retenu ni exclu, et c'est vérifié plutôt que supposé. Un fichier oublié se lancerait un
jour par `--all` sans que personne n'ait décidé de le lancer.

Le `free-busy-query` de 5c § 9 **est** mesuré par l'outil, sans `<feature>` pour l'annoncer :
`reports.xml` porte une suite `free-busy reports`, gardée par aucune feature, qui envoie un `REPORT`
sur `$calendarpath1:/` et vérifie dix-neuf intervalles avec le callback `freeBusy` — douze en t1,
sept de plus en t2 —, sur la ressource exacte où `CalDavController` sert ce rapport ; `floating.xml`
en envoie deux autres, sur `calendar-none/` et `calendar-us/`, et les huit tests des deux suites
`Timezone cache` de `timezones.xml` en font autant après des `PUT` à `VTIMEZONE` tronqué.
`freebusy.xml`, elle, teste bien le `POST` d'ordonnancement et reste exclue. Ces tests se trient
comme les autres ; ce n'est pas une zone mesurée par nous seuls.

Le `403 valid-sync-token` de 5c — la réponse à un jeton qu'une purge de tombes a périmé, et le seul
chemin par lequel un client resté trop longtemps hors ligne se rattrape — **est** mesuré lui aussi,
par un seul test : la suite `simple reports - valid token` de `sync-report.xml`, gardée par rien,
envoie un `sync-collection` à jeton invalide sur `$calendarpath1:/` et attend cette précondition.
C'est la seule mesure de ce chemin dans toute la campagne : aucun scénario client ne force
l'expiration d'un jeton, et un échec là se trie en défaut du serveur, jamais en bruit.

Corollaire pour `calendar-5c-residuals.md` : sa ligne « features à allumer » en demandait **trois**.
`Extended MKCOL` existe dans le gabarit amont mais n'est lue par aucun fichier (ci-dessus) ;
`free-busy-query` et « `calendar-query` avec `expand` » n'y **existent pas du tout**
(`scripts/server/serverinfo-template.xml`, dont notre `serverinfo.template.xml` est tiré). Le
rapport consigne les trois constats et referme la ligne entière, la couverture venant de
`reports.xml` et non d'un interrupteur. Encore faut-il que `reports.xml` tourne : c'est la décision
10.

## Fichiers

**Tâche zéro (décision 3)** :

- `Models/Contacts/SyncState.cs` → `Models/Dav/SyncState.cs`, et les `using` des fichiers qui
  l'importent, des deux protocoles.
- `Services/CalDav/MkCalendarRequest.cs` — un `supported-calendar-component-set` sans aucun `comp`
  est refusé.
- `Controllers/CalDavController.cs` — le `Cache-Control: no-cache` de RFC 4791 § 5.3.1 et de
  RFC 4918 § 9.3 posé sur **tous** les chemins de sortie d'un `MKCALENDAR` et d'un `MKCOL`, pas
  seulement sur le `201`. Ils sont **neuf**, là où cette spec n'en nommait d'abord que cinq :
  s'y ajoutent le `400` d'un corps mal formé, le `403 valid-resourcetype`, le
  `403 valid-calendar-data` du fuseau et celui de repli. Et le refus de
  `resourcetype`/`calendar-timezone` d'un `MKCOL` étendu aiguillé vers `WriteCreationRefusalAsync`
  (RFC 5689 § 3) — `Services/Dav/MultiStatusWriter.cs` aussi : `WritePropstatsAsync` doit apprendre
  à porter le `<D:error>` dans le `propstat`, ce qu'elle ne sait pas faire aujourd'hui.
- `Services/Calendar/OccurrenceExpander.cs` / `Repositories/CalendarEventStore.cs` — une seule
  `Margin`, `internal`, avec le commentaire qui dit que les deux marcheurs en dépendent
  solidairement ; et une seule borne de cinq ans côté moteur là où `MaxSpan` et le
  `365 × MaxYears` de `CalendarEventStore` en font deux ; `CalendarEventsController.MaxWindow` ne
  change pas (point 4).
- `snoopy.microservice.Tests/Services/IcsTimeZonesTests.cs` — les alias `US/Eastern`,
  `US/Mountain`, `US/Pacific` et `GMT` résolvent, à côté de `IsKnownIana_AnswersTheFirstTierOnly`.
- Tests : les cinq formes HTTP, le composant vide, le `Cache-Control` sur un refus de chaque verbe,
  et le corps `mkcol-response` du refus `MKCOL`.

**Harnais** :

- `tools/caldavtester/run.ps1` — `-Protocol`, le purge d'environnement fait par défaut et son
  `-NoPurge`, et la résolution en chemin absolu des entrées de `suites/` (décision 10).
- `tools/caldavtester/serverinfo.template.xml` — substitutions (dont `$principals_users:`,
  `$calendars_uids:` et `$calendars_users:`, sans lesquelles quatre tests restent inclassables et
  deux en mesurent un autre) et `<features>`.
- `tools/caldavtester/suites-caldav.txt` — nouveau, **un fichier par ligne**, avec les quatre
  commentaires que le corps de cette spec rend obligatoires : le bruit multi-utilisateurs nommé
  fichier par fichier, les quatre fichiers qui mesurent moins que leur nom, les raisons d'exclusion,
  et la contrainte d'ordre que le `<end>` de `ctag.xml` impose (décision 9). `suites.txt` est
  renommé `suites-carddav.txt`, **moins `CardDAV/limits.xml`** (« Le harnais »).
- `tools/caldavtester/suites/CalDAV/reports.xml` et `delete.xml` — copies locales de l'amont sans
  les `PUT` `$taskspath1:` de leur `<start>` ni le `PUT` `VFREEBUSY` s15, avec `README.md` et le
  diff contre le commit épinglé (décision 10).
- `tools/caldavtester/README.md` — le mode d'emploi CalDAV à côté du CardDAV, l'avertissement sur le
  `DELETE` d'agenda, le purge donné comme fait **par défaut** avec sa raison et le seul cas où
  `-NoPurge` se justifie (décision 9), et le pourquoi des copies locales (décision 10).

**Rejeu Apple** :

- `snoopy.microservice.Tests/Controllers/AppleDiscoveryReplayTests.cs` — nouveau.

**Correctifs du triage** : le fichier que chaque verdict désigne, avec son test. Nommés dans le plan
au fur et à mesure, pas ici : les nommer d'avance serait prédire la mesure. Un seul est connu
d'avance parce qu'il ne vient pas de la mesure (décision 11) :

- `Services/CalDav/CalendarQueryFilter.cs` (`ParseTimeRange`), `Services/CalDav/TimeRangeSpec.cs`
  — une borne absente reste absente au lieu d'être fermée à `MaxSpan` ;
  `Repositories/DavCalendarReader.cs` (`CandidatesAsync`) et le marcheur de
  `Services/Calendar/OccurrenceExpander.cs` — la fenêtre ouverte d'un côté, l'arrêt à la première
  occurrence trouvée ; `CalendarQueryFilterTests` et `CalDavQueryTests` pour les deux tests.

**Documentation** :

- `docs/superpowers/calendar-5d-conformance.md` — le rapport.
- `docs/superpowers/calendar-5c-residuals.md` — chaque ligne que la campagne referme ou confirme.
- `docs/superpowers/calendar-5d-residuals.md` — le tri de fin de tranche, comme ses trois aînés.

## Tests

**La tâche zéro**, figée par mutation là où c'est possible : les trois points qui changent une
réponse — le composant vide (2), le `Cache-Control` des refus (6), le corps `mkcol-response` du
`MKCOL` (7) — ont chacun un test qui rougit quand la garde saute. Les points 3 et 5 **sont** des
tests, ils n'en demandent pas d'autre. Les deux qui restent, 1 et 4, n'admettent pas de test de
mutation, et leur preuve est ailleurs : le déplacement de `SyncState` est un déplacement de fichier
que la compilation prouve ; l'unification des constantes est sans effet visible par construction, et
sa preuve est la suite existante restée verte.

**Chaque correctif du triage** : un test qui rougit quand la garde saute, dans la classe qui couvre
déjà la forme concernée. Le rapport cite le test à côté du verdict. C'est la règle de 4d, et c'est
ce qui empêche un correctif de conformité de repartir à la tranche suivante.

**Le rejeu Apple** : chaque requête de la séquence a son test, et chacun assert sur ce qui compte —
le statut, la présence de la propriété attendue dans le bon `propstat`, et le `404` propstat pour
celles que nous ne servons pas. Un test qui vérifie seulement « ça n'a pas fait 500 » ne serait pas
un test. Le partage entre les deux `propstat` se relève **dans le code et par ressource**, pas
dans la liste que le client demande : sur un **agenda**, `getctag` est servie et va en `200` tandis
que `quota-available-bytes`, `quota-used-bytes`, `default-alarm-vevent-datetime`,
`calendar-free-busy-set` et les propriétés CalendarServer vont en `404` ; sur le **home**, la table ne sert que `resourcetype`, `displayname`,
`supported-report-set` et `current-user-principal`, et tout le reste — `getctag` inclus — va en
`404`. Deux tables d'attente, donc, une par `href` du multistatus. Les corps viennent de
`Resource/CalDAV/ical-client/` et le test dit lequel il rejoue.

**Le harnais lui-même** n'a pas de test : c'est un script de lancement, et sa preuve est le passage
initial consigné. Le purge fait exception dans un sens : il n'utilise que la surface DAV que les
tests de 5c couvrent déjà, donc rien de neuf à figer. Les copies locales de la décision 10 ont une
preuve d'un autre ordre : le diff versionné contre l'amont, qui ne doit toucher que le bloc
`<start>`, et la ligne du passage initial qui montre `reports.xml` et `delete.xml` **joués** et non
tués.

## Ordre d'exécution

1. **Tâche zéro** (décision 3), tests, push, déploiement sur dev, puis un passage
   `-Protocol CardDAV` seul — une minute et demie — sur `suites-carddav.txt` **déjà amputé de
   `CardDAV/limits.xml`** (« Le harnais »), pour que ce repère et le passage final de l'étape 4
   comptent les mêmes fichiers. La tâche zéro touche le socle (`SyncState`, `Margin`) : sans ce
   repère, la seule comparaison possible en fin de tranche serait celle du passage final contre un
   chiffre de 4d mesuré avant qu'elle n'y touche.
2. **Harnais** : `-Protocol`, le purge par défaut, substitutions, `<features>`,
   `suites-caldav.txt`, les deux copies locales et le rejeu `-PrintResponses` du `<start>` de
   `reports.xml` (décision 10), README. **Premier passage** consigné brut — y compris les fichiers
   qui sautent, les fichiers tués au `<start>` et les suites ignorées, comptés chacun à part des
   échecs : c'est la mesure de départ. **Un seul passage complet par quart d'heure** — un passage
   consomme neuf des dix échecs d'authentification qu'`api-dev` tolère sur cette fenêtre (Risques) —,
   et tout rejeu ciblé attend la fin de la fenêtre ouverte par le passage précédent.
3. **Triage** de tout ce qui échoue, **de tout fichier tué au `<start>`**, **et de tout ce qui sort
   en ignoré là où la décision 5 attendait un échec**, verdict par verdict.
4. **Une seule vague** de correctifs serveur, chacun avec son test — la borne de `time-range`
   ouverte (décision 11) en fait partie d'office, quel que soit le triage ; push, déploiement,
   **passage final** en `-Protocol Both`, lancé après quinze minutes pleines depuis le passage
   précédent (Risques : celui-là est exactement au plafond) — le socle est partagé avec le carnet,
   et le rapport porte les deux chiffres. Côté carnet il a deux repères : le passage de l'étape 1,
   pris après la tâche zéro, et le passage final de 4d (`ok=107, failed=72`) derrière lui. Le
   rapport dit lequel des deux il commente à chaque fois qu'il compare.
5. **Thunderbird**, scénarios 1 à 13 (8, 9 et 13 « non applicable » — le treizième part d'un
   événement créé sur le téléphone). Puis **DAVx⁵ + Agenda Samsung**,
   1 à 13 (8 « non applicable »). Triage, et une vague de correctifs seulement si un client en
   impose — la borne de `time-range` ouverte n'en fait pas partie, elle est déjà livrée à l'étape 4
   (décision 11) et le scénario 11 la vérifie. **Si cette vague touche `Services/Dav`**, le
   passage `-Protocol Both` de l'étape 4 est rejoué après elle : le risque « le socle est partagé
   avec le carnet » nomme ce passage comme le seul garde-fou, et un correctif livré après lui
   partirait sans. Une vague qui ne touche que `Services/CalDav` s'en dispense, et le rapport écrit
   laquelle des deux a eu lieu.
6. **Rejeu Apple** : `AppleDiscoveryReplayTests`, et lecture du résultat d'`ical-client.xml` du
   dernier passage joué.
7. **Clôture** : rapport, `calendar-5c-residuals.md` mis à jour ligne à ligne,
   `calendar-5d-residuals.md` écrit, secret du compte de test régénéré.

## Ce que la tranche ne fait pas

- **Aucun ordonnancement.** Invitations, `POST` sur l'outbox, `implicit-scheduling`, `freebusy.xml`,
  `schedule-*` : c'est 5e, et elle n'est pas conçue. `calendar-user-address-set` reste servie sans
  que rien ne soit ordonnancé, et l'absence de `calendar-auto-schedule` dans l'en-tête `DAV:` le
  dit — avec la nuance que RFC 6638 § 2.4.1 fait dire l'inverse à la propriété seule
  (`calendar-5c-residuals.md`) ; un client qui lit les deux suit le jeton.
- **Ni `COPY` ni `MOVE`.** RFC 4918 § 9.8 et § 9.9 en font chacun un MUST, et l'en-tête `DAV:`
  annonce les classes 1 et 3 : le `405` actuel n'est pas une divergence assumée mais une
  **non-conformité** (décision 5). Les servir demande une écriture de ressource complète —
  sémantique du `PUT`, en-têtes `Destination` et `Overwrite`, destination hors de l'agenda source,
  et la précondition `CALDAV:no-uid-conflict` que RFC 4791 § 5.3.2.1 étend explicitement au `COPY`
  et au `MOVE` —, c'est-à-dire une fonctionnalité et non un correctif de conformité. Différé, écrit
  comme tel dans le rapport et dans `calendar-5d-residuals.md`, et à cadrer en 5e. Ce qui est fait
  ici, en revanche, c'est de **le mesurer** : les deux features restent allumées pour que les 39
  tests concernés partent réellement (« Le harnais »).
- **Pas de lecture de `comp`/`prop`, ni de `limit-*`.** Les `limit-*` sont une autre
  non-conformité sous `calendar-access` — § 9.6.6 et § 9.6.7 disent chacun un MUST. `comp`/`prop`
  n'en est pas une : § 9.6 et § 9.6.1 ne portent aucun mot-clé RFC 2119, c'est une divergence
  assumée (décision 5). Aucun client visé n'exerce ni l'une ni l'autre, la correction des deux est
  la même fonctionnalité de sérialisation partielle, et elles suivent `COPY`/`MOVE` dans
  `calendar-5d-residuals.md`. La borne de `time-range` ouverte, elle, **n'est pas dans cette
  liste** : un client visé l'exerce, et elle est corrigée (décision 11).
- **Ni `VTODO` ni `VJOURNAL`.** Un agenda ne sert que `VEVENT`.
- **Pas de mesure de charge.** Les cinq mille événements que 5c nommait sont un autre outil et une
  autre tranche.
- **Pas de portage de l'outil.** Ce qui ne s'exécute pas sous Python 2.7.18 tel quel est un défaut
  de l'outil (décision 4), consigné, la suite comptée sautée. Les deux copies locales de la
  décision 10 ne touchent pas une ligne de Python ni un seul test : elles retirent d'un `<start>`
  des préparatifs que le serveur ne peut pas satisfaire.
- **Pas de `SRV` ni de `.well-known` DNS sur `mail.weesky.net`.** L'adresse à saisir reste celle de
  l'API.
- **Pas d'appareil Apple.** Décision 8 : deux couches de rejeu, et le rapport écrit « non branché ».
- **Ni partage, ni `webcal`, ni pièce jointe gérée, ni `vpoll`.**
- **Pas de nouvelle fonctionnalité serveur** en dehors de ce qu'un verdict « défaut du serveur »
  impose et de la décision 11, prise sans verdict parce qu'un client visé l'exerce. Une suite qui
  échoue parce que nous n'avons pas une extension CalendarServer n'est pas une raison de l'écrire.
- **Ni le `time-range` ouvert d'un `VALARM`, ni celui de `free-busy-query`, ni une fenêtre à
  deux bornes de plus de cinq ans.** La décision 11 ne couvre que le `calendar-query` à borne
  absente ; ces trois formes restent — fermée à cinq ans, refusée, refusée en `valid-filter` — et
  sont portées dans `calendar-5d-residuals.md` comme non-conformités connues qu'aucun client visé
  n'exerce.

## Risques

- **Un fichier tué au `<start>` mesure zéro test et compte une erreur — dans `errors=`, pas dans
  `failed=`.** Décision 10 : `reports.xml` et `delete.xml` sont traités d'avance — sans que ça
  garantisse les vingt `PUT` `VEVENT` que le premier garde —, et cinq autres sont surveillés :
  `encodedURIs.xml` (nom d'agenda à espace), `aclreports.xml` (`PROPPATCH` dont le `207` est un
  2xx), `floating.xml` (`MKCALENDAR` porteur d'un `calendar-timezone` en `TZID:US/Eastern`),
  `copymove.xml` et `errors.xml` (UID dupliqué entre deux agendas). Le triage lit `errors=` et les lignes « Start
  items failed » avant les échecs, parce qu'un fichier tué se cache dans un total qui a l'air petit.
- **Ce que la campagne précédente laisse.** Décision 9 : `movecopy`, les événements de `default`,
  et tout `<end>` qu'un plantage Python a empêché de jouer. Le purge avant chaque passage n'est pas
  une commodité — d'où le défaut plutôt que l'option.
- **Ce que la campagne se laisse à elle-même, en cours de route.** Le `<end>` de `ctag.xml` fait un
  `DELETEALL` sur `$calendarpath1:/`, c'est-à-dire qu'il **vide `default`** au milieu du passage
  (décision 9). Le purge ne l'attrape pas : il agit avant, pas pendant. Aucun fichier lancé après
  ne dépend du contenu de `default`, vérifié ; l'ordre de `suites-caldav.txt` porte donc une
  contrainte qu'un commentaire doit dire, sans quoi un réordonnancement futur produira des faux
  échecs sans que personne ne sache d'où ils viennent.
- **Une divergence prédite en échec qui sort en ignoré.** C'est le mode de panne propre à ce
  harnais : la table de la décision 5 annonce ce que l'outil enverra, les `<features>` décident ce
  qu'il enverra vraiment, et les deux se contredisent silencieusement. Le triage lit donc les deux
  colonnes du rapport — échecs *et* ignorés — et tout écart avec la table est lui-même un constat.
- **Le socle est partagé avec le carnet.** Un correctif dans `Services/Dav` touche CardDAV, et un
  test vert côté agenda ne le dit pas. Le passage `-Protocol Both` relance les deux protocoles ;
  c'est le seul garde-fou qui vaille, et il est dans l'ordre d'exécution — y compris après une
  vague de correctifs venue des clients, étape 5.
- **La sortie de `reports.xml` est massive** — plusieurs centaines de tests dans un fichier. Le
  triage doit se faire fichier par fichier, sur le fichier de `results/`, jamais en lisant la
  console défiler.
- **Le dénominateur de `reports.xml` est très inférieur à son nombre de tests.** Ses deux plus
  grosses suites — `basic query reports` (42 tests) et `time-range query reports` (38) — sont
  gardées test par test, massivement, par `query-extended` et `split-calendars`, toutes deux
  éteintes, plus sept tests par `timerange-low-limit`/`timerange-high-limit`, tous les sept dans la
  seconde. Recompté sur les features de cette tranche : `basic query reports` tombe à 21 tests joués
  sur 42, `time-range query reports` à 19 sur 38, et le fichier entier à **68 joués sur 115**.
  Quarante-sept tests du plus gros fichier de la campagne sortiront donc en **ignoré**, sans que le
  serveur ait rien vu. Le
  rapport annonce ce dénominateur à côté du total, faute de quoi le chiffre de départ flatte : un
  fichier à 200 tests dont 100 sont sautés ne mesure pas deux fois plus qu'un fichier à 100.
- **Le compte de test se bloque si deux passages se suivent de trop près.** `api-dev` applique un
  plafond d'échecs d'authentification : `AuthAttemptThrottle` compte **dix** échecs Basic par
  fenêtre glissante de quinze minutes, par identifiant **et** par IP, et répond ensuite `429` avec
  `Retry-After` ; un refus coûte déjà de 0,5 à 1,5 s. Un passage CalDAV complet en consomme
  **neuf**, comptés un par un plutôt qu'estimés : `errors.xml`, suite `Unauthenticated versus
  Forbidden`, t1 (mauvais mot de passe sur le vrai compte) et t2 (`user="bogus"`, qui compte pour
  l'IP) ; `mkcalendar.xml`, suite `MKCALENDAR read-free-busy privilege`, t2 ; `nonascii.xml`, suite
  `PUT with CN re-write`, t2 ; `current-user-principal.xml`, suite `/principals/`, t3 ; `ctag.xml`,
  suite `Scheduling`, t3 **et les deux `DELETEALL` de son `<end>`** — tous portent
  `user="$userid2:"`, resté en texte littéral (décision 2). Ne comptent pas : les requêtes
  `auth="no"`, et celles que `own-root` ou `principal-match REPORT` font sauter. Un passage
  `-Protocol Both` en ajoute un dixième (`CardDAV/current-user-principal.xml`) : **il est
  exactement au plafond**. Conséquence opératoire, à porter dans l'ordre d'exécution : un seul
  passage complet par quart d'heure, et tout rejeu ciblé après un passage complet attend la fin de
  la fenêtre. Sinon tout ce qui suit devient rouge pour une raison qui n'a rien à voir avec CalDAV —
  à relire en tête de triage si une série d'échecs commence brutalement au milieu d'un fichier.
- **La suite `Root resource` d'`errors.xml` ne part pas** : elle exige `own-root`, éteinte. Ses
  `DELETE /`, `COPY /`, `MOVE /` et `GET //` ne toucheront pas `api-dev` — écrit ici pour qu'on ne
  les cherche pas dans la sortie.
- **L'outil plante sur ce qu'il ne sait pas lire.** Une réponse XML qu'il n'attend pas peut faire
  sauter un fichier entier avec une trace Python plutôt qu'un échec de test. Ça se lit « fichier
  sauté », se consigne comme tel, et se rejoue avec `-PrintResponses`.
- **TLS**, risque nº 1 de 4d, est levé : la même chaîne a déjà parlé au Python 2.7.18 en août.
- **Le compte de test n'est pas un compte personnel.** Répété ici parce qu'un `DELETE` d'agenda
  secondaire ne le vide pas — il le supprime.
