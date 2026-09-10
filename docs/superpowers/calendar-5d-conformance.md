# Agenda 5d — rapport de conformité

Rapport de la tranche [5d](specs/2026-09-07-webmail-calendar-5d-conformance-design.md). Les chiffres
viennent de `tools/caldavtester/results/` (sortie épurée) ; rien ici ne se régénère, chaque passage
est recopié une fois et daté. La ligne finale de l'outil porte quatre compteurs dès qu'il y a un
échec — `FAILED (ok=, ignored=, failed=, errors=)` — et deux seulement quand il n'y en a aucun.

## 0. Repère CardDAV, après la tâche zéro

Date : 2026-09-08 · commit déployé : `c0192a48` · fichier : `results/20260908-215646-carddav.txt` ·
`suites-carddav.txt` amputé de `CardDAV/limits.xml`. Repère de l'étape 1 : le passage final de
l'étape 4 se compare à **lui**, et non au `ok=107, failed=72` de 4d, mesuré avant que la tâche zéro
ne touche au socle.

**`FAILED (ok=108, ignored=21, failed=71, errors=0)`** — 200 tests en 88 s, quinze fichiers, aucun
fichier tué au `<start>`. Épuration vérifiée : aucune ligne `Authorization` en clair.

Le passage final de 4d valait `ok=107, ignored=22, failed=72, errors=0` sur 201 tests. Les listes
d'échecs des deux sorties ont été comparées nom à nom : **aucun échec nouveau**, et exactement deux
écarts, tous deux attendus.

| Écart | Effet | Cause |
|---|---|---|
| `CardDAV/limits.xml` retiré de `suites-carddav.txt` | −1 test, −1 ignoré | `caldav` étant désormais allumée, son `require-feature` serait satisfait et le fichier se réveillerait sur deux propriétés CalendarServer que nous n'avons pas (« Le harnais ») |
| `current-user-principal.xml`, suite `/principals/`, test 1 : échec → OK | +1 OK, −1 échec | `$principals_users:` repointée sur `/dav/principals/` : c'est la dette que le rapport de 4d § 4 laissait ouverte en toutes lettres (« `$principaluri1:` resté à la forme `__uids__` de CalendarServer (harnais) ») |

La suite `/principals/` passe donc de 1 OK / 2 échecs à **2 OK / 1 échec** ; l'échec restant est son
test 3, qui s'authentifie en `$userid2:` — le bruit multi-utilisateurs assumé (décision 2), et
l'unique échec d'authentification que ce passage a consommé.

**Ce que ce repère établit** : la tâche zéro et le repointage du gabarit n'ont introduit **aucune
régression sur le carnet**, et le socle `Services/Dav` sert CardDAV exactement comme avant.

Un premier passage, à 19:47 GMT, avait rendu `ok=9, ignored=4, failed=71, errors=5` : le secret DAV
du compte de test avait été régénéré à la clôture de 4d (`carddav-4d-conformance.md` § 6) et
`serverinfo.local.json` portait encore l'ancien. Dix `401` puis cinquante-cinq `429` — la signature
d'`AuthAttemptThrottle`, pas une mesure. Consigné ici parce que c'est le mode de panne que toute
reprise de campagne rencontrera en premier.

## 1. Passage initial — avant tout correctif

Date : 2026-09-08 · commit serveur déployé : `c0192a48` · fichier :
`results/20260908-221019-caldav.txt`

**`FAILED (ok=188, ignored=138, failed=212, errors=0)`** — 538 tests en 206 s, vingt-trois fichiers.

**`errors=0`, et aucune ligne « Start items failed » : pas un seul fichier n'est mort à son
`<start>`.** C'est le premier résultat de la campagne, et il valide la décision 10 : les deux copies
locales ont joué, et les cinq autres blocs `<start>` sous surveillance — `encodedURIs.xml` et ses
noms d'agenda à espace, les deux `PROPPATCH` d'`aclreports.xml`, le `MKCALENDAR` de `floating.xml`
porteur d'un `TZID:US/Eastern`, et les quatre `PUT` d'UID dupliqué entre agendas de `copymove.xml`
et d'`errors.xml` — sont tous passés. Le scope RFC de `CALDAV:no-uid-conflict`, seule mesure
positive de la campagne sur ce point, est donc vérifié.

| Fichier | Tests | OK | Échecs | Ignorés | Suites ignorées |
|---|---|---|---|---|---|
| propfind.xml | 27 | 24 | 2 | 1 | — |
| proppatch.xml | 8 | 2 | 6 | 0 | — |
| put.xml | 42 | 10 | 27 | 5 | 2 |
| get.xml | 2 | 1 | 0 | 1 | 2 |
| delete.xml (copie) | 3 | 2 | 1 | 0 | — |
| reports.xml (copie) | 115 | 30 | 38 | 47 | — |
| sync-report.xml | 52 | 26 | 6 | 20 | 11 |
| errors.xml | 73 | 21 | 25 | 27 | 2 |
| mkcalendar.xml | 19 | 7 | 12 | 0 | 2 |
| options.xml | 4 | 2 | 2 | 0 | — |
| nonascii.xml | 12 | 3 | 9 | 0 | 4 |
| well-known.xml | 10 | 0 | 10 | 0 | — |
| current-user-principal.xml | 3 | 2 | 1 | 0 | 1 |
| expandproperty.xml | 16 | 6 | 8 | 2 | — |
| recurrenceput.xml | 19 | 12 | 7 | 0 | — |
| floating.xml | 14 | 2 | 12 | 0 | — |
| ctag.xml | 23 | 14 | 9 | 0 | — |
| encodedURIs.xml | 20 | 7 | 13 | 0 | 1 |
| conditional.xml | 3 | 1 | 2 | 0 | — |
| copymove.xml | 17 | 0 | 16 | 1 | — |
| aclreports.xml | 2 | 0 | 2 | 0 | 6 |
| timezones.xml | 17 | 10 | 4 | 3 | — |
| ical-client.xml | 6 | 6 | 0 | 0 | — |

**Le dénominateur, à côté du total** : 138 des 538 tests n'ont rien mesuré — une `<feature>` éteinte
les a fait sauter. Le serveur a été vu par 400 tests, pas par 538. `reports.xml` à lui seul apporte
47 des ignorés, comme la spec l'avait calculé.

**Ce que la mesure confirme des prédictions écrites avant elle** (décision 5), au test près :

| Prédiction | Mesure |
|---|---|
| `copymove.xml` : 16 tests partent et échouent, `COPY`/`MOVE` en `405` | 0 OK, **16** échecs |
| `aclreports.xml` : six suites sautées, deux tests incapables de passer | **6** suites ignorées, **2** échecs |
| `get.xml` ne joue qu'**un** test | **1** test |
| `nonascii.xml` joue douze tests | **12** tests |
| `conditional.xml` en joue trois | **3** tests |
| `sync-report.xml` : onze suites sautées par `sync-report-home` | **11** suites ignorées |
| `well-known.xml` : 0/10, même verdict que son jumeau CardDAV | **0** OK, **10** échecs |
| `reports.xml` : 68 joués sur 115 | **68** joués (30 + 38) |
| `ical-client.xml` : six `PROPFIND`, statut seul | **6** OK, aucun échec |

`reports.xml` rend exactement les mêmes chiffres qu'en rejeu isolé (30/38/47) : les cinq fichiers
joués avant lui ne polluent pas son `default`, la réserve écrite plus haut est levée.

**Rejeu de `reports.xml` seul** (`-PrintResponses`, sur un `default` purgé), 2026-09-08,
`results/20260908-220418-caldav.txt` : **`FAILED (ok=30, ignored=47, failed=38, errors=0)`**, 115
tests en 37 s.

- **Le `<start>` passe.** Aucune ligne « Start items failed » : les vingt `PUT` `VEVENT` que la
  copie locale conserve — dont trois à `RECURRENCE-ID`, deux d'entre eux portant deux `VEVENT` du
  même UID — sont tous acceptés. C'est ce que la décision 10 ne pouvait pas garantir, et c'est
  vérifié plutôt que supposé.
- **Le dénominateur annoncé est exact** : 115 tests, 47 ignorés, **68 joués**, au test près ce que
  la spec avait calculé. Le comptage des `<features>` est donc juste.
- Les échecs se groupent d'abord sur les ressources que la copie ne crée plus — `101.ics` à
  `106.ics` et `15.ics` —, conséquence nommée d'avance de la décision 10 : les tests qui
  dépendaient de ces préparatifs échouent un par un. Vérifié : la copie retire exactement sept
  requêtes du `<start>`, et `101.ics` n'existait qu'à `$taskspath1:`.
- **Ce rejeu n'est pas le passage initial** et ses chiffres ne s'y substituent pas : il a tourné
  seul, sur un `default` que le purge venait de vider, là où le passage complet joue `reports.xml`
  en sixième position, après cinq fichiers qui peuvent y laisser des ressources. Un compte de
  `multiget` peut donc différer entre les deux ; c'est le passage complet qui fait foi.

## 2. Triage

Un verdict par échec (décision 4) : **défaut du serveur** (corrigé dans la vague, un test le fige),
**divergence nommée** (avec ce qu'elle coûte à un client réel — **non-conformité connue** quand un
MUST du RFC n'est pas tenu sous un jeton que l'en-tête `DAV:` annonce), **défaut de l'outil** (le
*quoi* nommé, jamais un verdict par défaut), **harnais** (corrigé, et le passage final le mesure).
Aucun fichier n'a été tué au `<start>` : cette catégorie est vide.

Les 212 échecs ont été triés fichier par fichier sur le fichier de `results/`, jamais sur la console.

| Verdict | Échecs | Part |
|---|---|---|
| Défaut du serveur | **41** | 19 % |
| Divergence nommée, dont non-conformité connue | **111** | 52 % |
| Défaut de l'outil | **59** | 28 % |
| Harnais | **1** | — |

### Les 41 défauts du serveur, par cause racine

Quarante et un échecs, **douze causes** mesurées et une treizième trouvée par lecture. Trois d'entre
elles ont été trouvées deux fois, par des triages indépendants portant sur des fichiers différents —
c'est la meilleure garantie qu'elles sont réelles et non des artefacts de lecture.

| Cause | Échecs | Où | Ce que dit le RFC |
|---|---|---|---|
| **L'élément `<C:timezone>` d'une requête n'est analysé nulle part.** Aucun fichier du service ne référence ce nom ; `CalendarQueryReport` passe toujours la zone de l'agenda. Un tout-journée flottant est donc résolu dans la mauvaise zone, et un `VCALENDAR` de requête malformé est accepté | 9 | `reports.xml` t12a, t9a, t10 ; `floating.xml` ×6 | RFC 4791 § 9.8, **MUST** : « the server MUST rely on the specified VTIMEZONE component instead of the CALDAV:calendar-timezone property … to resolve "date" values and "date with local time" values ». Sous `calendar-access`, c'est une non-conformité — et elle n'était nommée ni dans la décision 5 ni dans `calendar-5c-residuals.md` |
| **`IcsGuards` accepte des `PUT` que le RFC interdit** : `METHOD:PUBLISH`, un UID changé à l'écrasement, un `TZID` sans `VTIMEZONE`, un échappement TEXT invalide, un `RECURRENCE-ID` sans occurrence correspondante, un `VCALENDAR` de fuseau portant un `VEVENT` surnuméraire | 7 | `errors.xml` | RFC 4791 § 4.1 (**MUST NOT** contenir `METHOD`), § 5.3.2.1 (`no-uid-conflict`), RFC 5545 |
| **Préconditions mal nommées** : `valid-calendar-data` là où `supported-calendar-data` est due, `supported-filter` là où `valid-filter` l'est | 8 | `errors.xml` `PUT` t1, `REPORT-filter` t2-t8 | RFC 4791 § 5.3.2.1 et § 7.8 : le statut est bon, l'élément de précondition ne l'est pas |
| **La marque d'ordre d'octets UTF-8 n'est pas retirée** du corps d'un `PUT` : `EF BB BF` empêche Ical.Net de reconnaître `BEGIN:` | 5 | `nonascii.xml` t1 et ses quatre cascades | RFC 3629 § 6. **Défaut partagé avec le `PUT` CardDAV** — il touche déjà le carnet |
| **Un tout-journée est posé à minuit UTC** en ignorant la zone reçue (`OccurrenceExpander.Span`, branche `IsAllDay`) | ~4 | `floating.xml` `free-busy` t2 l'isole seul | Même MUST de § 9.8 que la première ligne, autre moitié du défaut |
| **Un filtre invalide est accepté** au lieu d'être refusé — l'exemple littéral du RFC | 1 | `errors.xml` `REPORT-filter` t9 | RFC 4791 § 7.8.9 |
| **Un `param-filter` négatif correspond sur un paramètre absent** — `MatchesParam` n'a pas le garde `Count == 0` que `MatchesPropFilter` porte deux lignes plus haut | 1 | `reports.xml` `basic query` t8 | RFC 4791 § 9.7.3. **Résidu 5c explicitement laissé « en attente du verdict de l'outil ou d'un client » : le verdict est rendu** |
| **Les répétitions d'une `VALARM` (`REPEAT`/`DURATION`) ne sont jamais déroulées** : `AlarmFires` ne lit que le `Trigger` | 1 | `reports.xml` `alarm` t2 | RFC 4791 § 9.9 : chaque répétition est un instant de déclenchement à part entière |
| **`time-range` et `prop-filter` sont évalués sur des composants différents** du même fichier, sans être liés | 1 | `reports.xml` `time-range` t7 | RFC 4791 § 9.7.1 : « the targeted calendar component » aux deux clauses |
| **Un `MKCALENDAR` sur un agenda existant ne porte pas de corps de précondition** (`405` + `Allow` seuls) | 3 | `mkcalendar.xml` `without body` t2, `with body` t2 et t4 | RFC 4918 § 16, **SHOULD** : le `405` tient le seul MUST engagé, c'est le corps qui manque. Le `prepostcondition` de ces tests accepte `403`, `409` ou `507` |
| **Un `remove` de propriété absente est refusé en `403`** au lieu d'être accepté : `CalendarPropertyUpdate.Judge` refuse tout `DAV:remove` hors `calendar-description`, y compris d'une propriété jamais posée | 3 | `proppatch.xml` `prop patches` t2, t3, `prop patch property attributes` t2 | RFC 4918 § 14.23 : « Specifying the removal of a property that does not exist is not an error » |
| **Un corps de `MKCALENDAR` nommant une propriété protégée est ignoré** au lieu de faire échouer la création tout entière : `MkCalendarRequest.Parse` ne lit que les cinq propriétés inscriptibles et laisse tomber le reste | 2 | `mkcalendar.xml` `with body` t3, et `read-free-busy` t1 qui en cascade | RFC 4918 § 15.6 fait de `DAV:getetag` une propriété protégée (**MUST NOT** être posée) et RFC 4791 § 5.3.1 fait traiter ce corps comme un `PROPPATCH`, dont RFC 4918 § 9.2 impose l'atomicité. C'est le résidu 5c « une couleur hors forme est ignorée, pas refusée », vu sur une propriété protégée |
| **`calendar-timezone` est servie en `allprop`** | 0 mesuré | trouvé par lecture de `CalDavProperties.CalendarTable` | RFC 4791 § 5.2.2, **SHOULD NOT** : « SHOULD NOT be returned by a PROPFIND DAV:allprop request ». Aucun test de la campagne n'envoie d'`allprop` — consigné pour ne pas être perdu |

**Une symétrie qui mérite d'être dite** : la porte du `PUT` est trop **stricte** à un endroit par
accident (ci-dessous, les `Problem VEVENTs`) et trop **laxiste** à sept autres. Les deux se corrigent
au même endroit, `IcsGuards`, et la vague devrait les traiter ensemble plutôt qu'un par un.

**Deux verdicts retournés à la relecture, et la même cause pour les deux.** Le triage donnait pour
défauts le `getctag` qui ne bouge pas après un `PUT` d'écrasement (`ctag.xml` t5) et la modification
que `sync-collection` perd (`sync-report.xml` `diff token` t5, ×2) — cette dernière annoncée comme
« la trouvaille principale », sur le chemin que Thunderbird emprunte à chaque poll. Les deux tombent
à la lecture des suites : **le `PUT` que ces trois tests envoient est octet pour octet celui qui est
déjà stocké**. `ctag.xml` t5 rejoue `Resource/CalDAV/delete/1.txt`, que son t4 vient de poser ;
`sync-report.xml` t5 rejoue `Resource/CalDAV/reports/put/4.txt`, que le `<start>` a posé sur
`synccalendar2/1.ics`. `DavCalendarWriter.GateAsync` court-circuite ce cas sans transaction, sans
rang et sans réveiller personne — « the shape every idempotent DAVx5 retry takes », écrit en 5c — et
rend l'`ETag` inchangé, qui reste donc vrai. Rien n'a changé : ne rien signaler est juste, et
signaler serait réveiller tous les appareils à chaque réémission. **Verdict : divergence assumée**,
la convention CalendarServer étant d'incrémenter à chaque `PUT` quel qu'en soit le corps. Trois
échecs quittent la colonne des défauts, et le poll de Thunderbird n'est pas en cause.

### Trois arbitrages, rendus avant d'écrire la vague

1. **`<C:timezone>` est corrigé dans la vague**, et non différé. Son correctif est une lecture de
   `VTIMEZONE` de requête — donc une fonctionnalité au sens de la décision 4, comme `COPY`/`MOVE`,
   `comp`/`prop` et les `limit-*`. La différence qui emporte la décision : ces trois-là ont été
   nommés **avant** la mesure et arbitrés en connaissance de cause ; celui-ci ne l'a pas été, il
   casse un MUST annoncé, et il fausse silencieusement des instants — un client ne voit pas un refus,
   il voit une mauvaise heure.
2. **Les neuf `Problem VEVENTs` de `put.xml` ne sont pas un défaut** — verdict rectifié en
   **divergence assumée**. RFC 5545 § 3.8.2.2 fait de l'identité de type entre `DTSTART` et `DTEND`
   un MUST, et RFC 4791 § 5.3.2.1 autorise à refuser en `valid-calendar-data` ce qui n'est pas un
   objet iCalendar valide : notre refus est **juste**, et l'outil mesure la tolérance historique de
   CalendarServer envers les vieux bugs d'iCal. Aucun client visé n'envoie de types dépareillés.
   **Reste un vrai travail, plus petit** : le refus est aujourd'hui accidentel — un
   `catch (Exception)` générique d'`IcsDocument.TryLoad` — et son message dit « the body is not
   iCalendar text », ce qui est faux : le corps **est** de l'iCalendar, seulement invalide. Le rendre
   délibéré et correctement libellé entre dans la vague ; l'accepter, non.
3. **Le volume.** La décision 4 prévoyait « une seule vague » de finition. Douze causes, dont une
   partagée avec le carnet et une qui est une fonctionnalité, ce n'en est pas une. La règle « une
   seule vague » est **tenue** — il n'y en aura pas deux — mais elle sera longue, et le passage final
   la mesurera d'un bloc. Elle est écrite dans son propre plan,
   [2026-09-08-webmail-calendar-5d-fix-wave](plans/2026-09-08-webmail-calendar-5d-fix-wave.md), onze
   tâches, la tâche 10 de la tranche comprise.

### Divergences nommées et non-conformités connues : 111 échecs

Confirmées telles que la décision 5 les avait écrites, sauf mention :

| Divergence | Échecs | Statut |
|---|---|---|
| `COPY` et `MOVE` en `405` + `Allow`, et leurs dommages collatéraux inter-suites | 38 | **Non-conformité connue** (RFC 4918 § 9.8, § 9.9, § 18.1), différée avec son arbitrage écrit — c'est le prix payé pour la mesurer |
| Un agenda ne sert que `VEVENT` : `404` sur `$taskspath1:`, `403 supported-filter` sur un `comp-filter VTODO`/`VJOURNAL`/`VFREEBUSY` | ~29 | Divergence assumée (RFC 4791 § 5.2.3). La décision 5 n'avait compté que les tests visant `$taskspath1:` et sous-comptait de douze |
| Le home ne sert que `expand-property` parmi les `REPORT` | ~10 | Divergence assumée. RFC 4791 § 7.2 : « Servers **MAY** support the reports … on ordinary collections » — un MAY, donc pas une non-conformité |
| Les `Problem VEVENTs` refusés (arbitrage 2 ci-dessus) | 9 | Divergence assumée, à documenter et à rendre délibérée |
| Bornes de `time-range` fermées à cinq ans | 2 | **Non-conformité connue**, décision 11 — corrigée dans la vague |
| `RANGE=THISANDFUTURE` jamais appliqué à l'expansion | 3 | Divergence assumée, résidu 5a déjà écrit mais **absent de la table de la décision 5** |
| Aucun ordonnancement : pas d'`inbox`, pas d'`outbox`, `POST` en `405` | ~7 | Divergence assumée (RFC 6638 hors périmètre, 5e) |
| `getcontenttype` fixé à `text/calendar; charset=utf-8; component=VEVENT` | 1 | Divergence nommée en 5c ; coût nul, DAVx⁵ lit ce paramètre |
| Un `PUT` octet pour octet identique ne bouge ni `getctag` ni `sync-token` | 3 | Divergence assumée, **verdict retourné à la relecture** (ci-dessus) : rien n'a changé, l'`ETag` rendu est inchangé et reste vrai. La convention CalendarServer incrémente à chaque `PUT` |
| Autres divergences de lecture (`comp`/`prop`, `DURATION` réécrit en `DTEND`, `RECURRENCE-ID` sur chaque instance étendue, palier de résolution d'un alias tzdb) | ~9 | Divergences assumées |

### Défauts de l'outil : 59 échecs

`well-known.xml` (10, déjà trié en 4d : notre `Location` relatif est licite, notre `301` est prévu
par RFC 6764 § 5, et RFC 8615 § 3 dit qu'un client **ne doit pas** attendre de ressource à
`/.well-known/`) · `propfind.xml` `getcontentlength` sur des collections (2, RFC 4918 § 15.4 ne
l'impose pas) · `options.xml` qui exige l'**absence** de l'en-tête `DAV:` sur un `207` (2, RFC 4918
§ 10.1 ne parle que d'`OPTIONS`) · `conditional.xml` qui attend un `Last-Modified` et un `304` sur un
`PROPFIND` (2, aucun RFC ne les définit) · `aclreports.xml` (2, convention CalendarServer d'annoncer
les rapports RFC 3744 partout) · `timezones.xml` t1 (**verdict retourné par la revue de spec** :
RFC 4791 § 5.2.2 dit « SHOULD be defined on all calendar collections », donc servir toujours
`calendar-timezone` est ce que le RFC recommande, et c'est l'attente d'un `404` qui est la
convention) · le bruit du second compte non défini (`$userid2:`, `$principaluri21:`) · et le reste
des attentes propres à CalendarServer.

### Harnais : 1 échec

`floating.xml` `free-busy` t1 : l'agenda `calendar-none` hérite la zone de `default`, qu'`errors.xml`
a mise à `US/Eastern` plus tôt dans le même passage. Avec un `default` en UTC au départ de
`floating.xml`, ce test passe entièrement. **La décision 9 n'avait prévu que la fuite d'état du
`<end>` de `ctag.xml` ; en voici une seconde, en sens inverse.** À traiter dans la vague de harnais.

### Ce que la mesure corrige de la spec

La table de la décision 5 annonce ce que l'outil enverra ; les `<features>` décident de ce qu'il
enverra vraiment. Cinq écarts, tous dans le sens d'une mesure **plus large** que prévu.

| Ligne de la décision 5 | Prédit | Observé |
|---|---|---|
| Bornes de `time-range` fermées à cinq ans | « **ignoré** par l'outil » — `timerange-low-limit` et `timerange-high-limit` éteintes | **Mesuré** : vrai des sept tests que ces features gardent, faux de `time-range` t13 et t14, gardés par rien. **La campagne mesure donc la décision 11**, qui n'était fondée jusqu'ici que sur la lecture du code de DAVx⁵ |
| Les `limit-*` (§ 9.6.6, § 9.6.7), MUST non tenus | « échec — suite non gardée » | **Non mesurés du tout** : `limit-recurrence-set` n'est envoyé que par t1/t2, `ignore="yes"` en amont, et le seul test qui envoie `limit-freebusy-set` échoue avant, sur son `comp-filter VFREEBUSY`. Le passage final ne les mesurera pas davantage : le rapport ne doit pas compter ce test comme leur mesure |
| `comp`/`prop` dans `calendar-data` | une suite entière en échec | **Deux tests d'assiette seulement** : `multiget` t2 et t3 en envoient aussi et **passent**, leur vérification ne regardant que les `href` |
| Tests `VTODO` de `reports.xml` | sept tests non gardés | **Dix-neuf** : la décision 5 n'avait compté que ceux visant `$taskspath1:`, pas les `comp-filter VTODO`/`VJOURNAL`/`VFREEBUSY` refusés en `403 supported-filter` |
| `timezones.xml` t4 et t7, regex inertes | « échec — défaut de l'outil » | **Verts, et faussement** : le vérificateur lit l'argument `props` alors que les tests passent `okprops`, donc rien n'est évalué et n'importe quel `207` passe. Défaut de l'outil confirmé, mais il se manifeste en vert et non en rouge |

Deux résidus 5c reçoivent enfin le verdict que la décision 3 leur réservait : le `param-filter`
négatif est un **défaut** (corrigé dans la vague), et la présélection d'une ressource à plusieurs
surcharges sans maître n'a été exercée par aucun test — elle reste ouverte.

## 3. Passage final — après la vague de correctifs

Date : 2026-09-09 · commit déployé : `13d32d1a` · fichier : `results/20260909-154150-both.txt` ·
`-Protocol Both`

**`FAILED (ok=324, ignored=159, failed=255, errors=0)`** — 738 tests en 273 s, trente-huit fichiers.
**`errors=0`, aucune ligne « Start items failed », et zéro `500` dans tout le passage.**

**Il a fallu deux passages, et le premier est celui qui a servi.** Le passage du 2026-09-09 à 13:55
(`results/20260909-135513-both.txt`, commit `a54676c9`) a trouvé ce qu'aucun des 4 935 tests unitaires
n'avait vu : un `500` et un refus de trop. Les deux sont corrigés, et c'est le second passage qui
fait foi. Le premier est consigné ici parce qu'un rapport qui ne montrerait que le passage propre
mentirait sur ce que la mesure a coûté.

| Repère | ok | ignorés | échecs |
|---|---|---|---|
| CalDAV, passage initial (§ 1), commit `c0192a48` | 188 | 138 | 212 |
| **CalDAV, passage final**, commit `13d32d1a` | **215** | **138** | **185** |
| CardDAV, repère de l'étape 1 (§ 0), commit `c0192a48` | 108 | 21 | 71 |
| **CardDAV, passage final** | **109** | **21** | **70** |

**Vingt-sept échecs CalDAV de moins, et le carnet ne régresse pas** — c'était le risque principal,
cinq fichiers du socle partagé ayant été touchés. Le repère commenté ici est celui du § 0, pas le
`ok=107` de 4d : les deux diffèrent de la tâche zéro, et comparer au mauvais ferait apparaître un
gain qui n'existe pas. Les ignorés sont identiques des deux côtés : aucune `<feature>` n'a dérivé,
donc le dénominateur est le même et les deux colonnes se comparent ligne à ligne.

### CalDAV

| Fichier | OK | Échecs | Ignorés | Suites ignorées |
|---|---|---|---|---|
| propfind | 24 | 2 | 1 | — |
| proppatch | 3 | 5 | 0 | — |
| put | 10 | 27 | 5 | 2 |
| get | 1 | 0 | 1 | 2 |
| delete.xml (copie) | 2 | 1 | 0 | — |
| reports.xml (copie) | 37 | 31 | 47 | — |
| sync-report | 26 | 6 | 20 | 11 |
| errors | 33 | 13 | 27 | 2 |
| mkcalendar | 8 | 11 | 0 | 2 |
| options | 2 | 2 | 0 | — |
| nonascii | 6 | 6 | 0 | 4 |
| well-known | 0 | 10 | 0 | — |
| current-user-principal | 2 | 1 | 0 | 1 |
| expandproperty | 6 | 8 | 2 | — |
| recurrenceput | 9 | 10 | 0 | — |
| floating.xml (copie) | 8 | 6 | 0 | — |
| ctag | 14 | 9 | 0 | — |
| encodedURIs | 7 | 13 | 0 | 1 |
| conditional | 1 | 2 | 0 | — |
| copymove | 0 | 16 | 1 | — |
| aclreports | 0 | 2 | 0 | 6 |
| timezones | 10 | 4 | 3 | — |
| ical-client | 6 | 0 | 0 | — |

### CardDAV

| Fichier | OK | Échecs | Ignorés | Suites ignorées |
|---|---|---|---|---|
| propfind | 15 | 1 | 0 | — |
| proppatch | 1 | 6 | 0 | — |
| put | 14 | 4 | 0 | — |
| get | 1 | 2 | 0 | 2 |
| reports | 33 | 6 | 0 | 2 |
| sync-report | 23 | 3 | 8 | 3 |
| errors | 5 | 5 | 0 | — |
| errorcondition | 6 | 6 | 0 | 1 |
| nonascii | 3 | 4 | 0 | — |
| well-known | 0 | 10 | 0 | — |
| current-user-principal | 2 | 1 | 0 | 1 |
| mkcol | 2 | 1 | 1 | — |
| copymove | 0 | 3 | 0 | 1 |
| aclreports | 1 | 18 | 2 | — |
| ab-client | 3 | 0 | 0 | — |

### Ce que la vague a déplacé, fichier par fichier

| Fichier | Initial | Final | Ce qui a bougé |
|---|---|---|---|
| `errors.xml` | 21 / 25 | **33 / 13** | Les huit préconditions mal nommées, `METHOD`, l'échappement TEXT, le `RECURRENCE-ID` sans occurrence, le filtre invalide de § 7.8.9. **−1 sur `PUT` 10**, coût accepté du relâchement de la garde des fuseaux (ci-dessous) |
| `reports.xml` | 30 / 38 | **37 / 31** | `<C:timezone>`, le `param-filter` négatif, le `time-range` lié au composant, les répétitions de `VALARM` |
| `floating.xml` | 2 / 12 | **8 / 6** | Le tout-journée posé dans la zone où il est jugé, et la copie locale qui remet `default` en UTC — le harnais, seule pièce qu'aucun test hors ligne ne pouvait valider |
| `nonascii.xml` | 3 / 9 | **6 / 6** | La marque d'ordre d'octets UTF-8, plus ses cascades |
| `recurrenceput.xml` | 12 / 7 | **9 / 10** | **−3 net.** Trois refus **justes** que le durcissement du `PUT` a produits (voir plus bas), et la forme Apple récupérée après relâchement |
| `put.xml` | 10 / 27 | **10 / 27** | Le `500` corrigé rend un test ; les neuf `Problem VEVENTs` restent la divergence assumée de l'arbitrage 2 |
| `proppatch.xml` | 2 / 6 | **3 / 5** | Le `remove` d'une propriété absente |
| `mkcalendar.xml` | 7 / 12 | **8 / 11** | Le corps de création refusant une propriété protégée |

### Les deux défauts que seule la mesure a trouvés

**Un `500`, unique dans 738 tests.** `put.xml` `Problem VEVENTs` 11 envoie un `VCALENDAR` dont **la
surcharge précède la maîtresse** en ordre de document — RFC 5545 n'impose aucun ordre, un client réel
peut l'écrire. La cause n'était pas dans la vague : `IcsComposer.Detach` retirait un enfant par
l'indice de la liste **plate** d'Ical.Net, or la bibliothèque ne chaîne jamais ses listes par groupe,
si bien que chaque groupe repart de zéro et que le premier `VEVENT` devient inatteignable dès qu'un
`VTIMEZONE` le précède. **Latent depuis 5a**, réveillé par le garde des surcharges de cette vague.
Et le plantage n'était que sa moitié visible : dans un fichier ordinaire, la même ligne retirait **le
mauvais `VEVENT`** et ne marchait que par chance. Corrigé en `a8e95d8f`, sans `catch` — un `catch`
aurait changé un plantage en réponse silencieusement fausse. `CalDavNoFiveHundredTests` ne portait
aucun corps de cette forme : c'est pourquoi onze tâches sont restées vertes pendant que le serveur
plantait. La forme y est désormais.

**Un export Apple refusé.** `recurrenceput.xml` `VEVENTs` 12 est un fichier
`PRODID:-//Apple Inc.//Mac OS X 10.11//EN` qui référence `TZID=America/Los_Angeles` **sans joindre de
`VTIMEZONE`**. Le durcissement de RFC 5545 § 3.2.19 le refusait, sur le `PUT` **et** sur l'import
`.ics` du webmail, alors que notre propre base résout cette zone. Décision prise sur cette preuve :
**accepter quand l'identifiant se résout**, refuser seulement ce que rien ne résout — le MUST existe
pour qu'un lecteur puisse résoudre la zone, et quand nous le pouvons, refuser coûte un événement à
l'utilisateur pour une pureté dont nous n'avons pas besoin. RFC 7809 existe parce que cette forme est
courante. Corrigé en `13d32d1a` ; `errors.xml` `PUT` 10 repasse au rouge, et c'est le prix.

**Ce que l'épisode établit** : l'arbitrage qui avait accordé ce durcissement le faisait « en
connaissance de cause », en nommant son risque et en désignant l'étape des clients réels comme filet.
Le filet a servi **avant** cette étape, et sur du matériel Apple plutôt que sur l'exportateur exotique
annoncé. Un arbitrage rendu sans mesure reste un pari.

### Ce que ce passage ne mesure pas, et qu'aucun chiffre ne dira

- **Les trois `resource-must-be-null` de `mkcalendar.xml` sont rouges par décision.** RFC 4918
  § 9.3.1 prescrit le `405` que nous rendons ; l'outil attend `403`, `409` ou `507`. La tâche 9 a
  corrigé le SHOULD — le corps qui nomme la précondition — et non le statut. Ces trois-là passent de
  « défaut du serveur » à **divergence nommée**.
- **Les MUST `limit-recurrence-set` et `limit-freebusy-set` ne sont mesurés par aucun test joué.**
  Le seul qui les envoie échoue avant, sur son `comp-filter VFREEBUSY`. Ne pas le compter comme leur
  mesure : ils restent non mesurés, et différés à 5e.
- **`timezones.xml` t4 et t7 sont verts faussement** — leur vérificateur lit l'argument `props` là où
  le test passe `okprops`, donc rien n'est évalué. Deux verts qui ne mesurent rien.
- **Trois refus de `recurrenceput.xml` sont justes.** `VEVENTs` 2 : un `UID` qui change sous une
  adresse existante, ce que RFC 4791 § 5.3.2.1 interdit mot pour mot (« or overwrite an existing
  calendar object resource with one that has a different UID property value »). `VEVENTs` 10 et 11 :
  une surcharge dont le `RECURRENCE-ID` nomme le 2 janvier sur une série hebdomadaire partie du
  1er — un créneau que la règle ne produit pas. L'outil attend l'acceptation ici et le refus dans
  `errors.xml` pour la même forme : c'est lui qui est incohérent. Divergences nommées.

### Ce que le passage confirme des prédictions écrites avant lui

Les cinq prédictions posées avant de lire le fichier, au fichier près :

| Prédiction | Mesure |
|---|---|
| `put.xml` +1, le `500` disparaît | **+1**, et zéro `500` dans tout le passage |
| `recurrenceput.xml` +1, la forme Apple passe | **+1** |
| `errors.xml` −1, `PUT` 10 repasse au rouge | **−1** |
| CardDAV inchangé, `ok=109 / failed=70` | **inchangé** |
| CalDAV net **+27** contre le passage initial | **+27** (188 → 215) |

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
