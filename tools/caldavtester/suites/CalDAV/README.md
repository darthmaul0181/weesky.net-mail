# Copies locales : `reports.xml`, `delete.xml` et `floating.xml`

Copies de `scripts/tests/CalDAV/reports.xml`, `delete.xml` et `floating.xml`
du clone amont `https://github.com/apple/ccs-caldavtester.git`, épinglé au
commit `bed21e5924275552c1561febc8203a9f194cf737`. Voir la spec 5d, décisions
10 et 11, pour le pourquoi complet ; ce fichier résume ce qui change dans
chacune. Les deux premières **retirent** un préparatif impossible ;
`floating.xml` est la première qui **ajoute** une requête.

## Pourquoi

Une requête d'un bloc `<start>` est vérifiée de force en 2xx, et le premier
échec tue le fichier entier : « Start items failed - tests will not be run »,
une erreur comptée, zéro test joué. Un agenda ne sert que des `VEVENT`
(`IcsGuards` refuse tout le reste en `403 supported-calendar-component`) ; les
deux `<start>` amont posent des `VTODO`, et `reports.xml` en plus un
`VFREEBUSY`.

## Ce qui est retiré, et pourquoi

**`reports.xml`** — sept requêtes de son `<start>`, et rien d'autre :

- les **six** `PUT` non gardés `$taskspath1:/101.ics` … `106.ics`
  (`reports/put/101.txt` … `106.txt`) : chacun un `VTODO`, mais ce n'est pas le
  composant qui les tue — c'est l'agenda `tasks`, qui n'existe pas chez nous.
  Les six tombent en `404` avant qu'une ligne du corps ne soit lue ;
- la **septième**, s15, `PUT $calendarpath1:/15.ics` ← `reports/put/15.txt` :
  celle-là vise `default`, qui existe, et son unique composant est un
  `VFREEBUSY` — elle tombe donc en `403 supported-calendar-component`. Deux
  raisons, pas une. Elle porte `exclude-feature split-calendars` — feature
  éteinte chez nous, donc la requête part quand même.

**`delete.xml`** — une requête de son `<start>` : le `PUT
$taskspath1:/1todo.ics` (`todo/1.txt`, un `VTODO`).

## Ce que ces copies ne sont pas

Ce n'est **pas** un portage de l'outil : pas une ligne de Python ne change.
Les copies ne modifient **aucun** test — elles retirent des préparatifs
impossibles. Les tests qui en dépendaient échouent ensuite un par un et
nommément : `404` pour chaque `VTODO` restant dans le corps des fichiers
(`delete.xml` test 2, et les tests `VTODO` de `reports.xml`), et `free-busy
reports` t2 en attendant l'intervalle `unavailable` que le `VFREEBUSY` absent
aurait produit. C'est la mesure, pas un dommage.

Le diff exact contre l'amont est versionné à côté : `reports.xml.diff` et
`delete.xml.diff`. Aucun des deux ne contient de ligne ajoutée — seulement des
lignes retirées, toutes à l'intérieur du bloc `<start>`.

## Ce que retirer les sept ne garantit pas

`reports.xml` peut encore mourir avant son premier test : son `<start>` garde
**vingt** `PUT` `VEVENT` non gardés, dont trois portent un `RECURRENCE-ID` et
deux d'entre eux deux `VEVENT` du même UID. Un seul refusé le tue comme les
sept retirés. C'est pourquoi ce bloc est rejoué seul en `-PrintResponses`
avant que la mesure de départ de la campagne ne soit figée, et son résultat
consigné à côté du passage.

## `floating.xml`

`floating.xml` crée `calendar-none` par un `MKCALENDAR` sans corps : un
agenda créé sans zone hérite de celle de `default` (`CalendarStore`, décision
de 5c). Or `errors.xml`, joué **avant** `floating.xml` dans
`suites-caldav.txt`, pose `US/Eastern` sur `default` par un `PROPPATCH` (sa
suite « Invalid CalDAV:timezone »). `calendar-none` naît donc en heure de
New York, et le test `free-busy` t1 de la suite « REPORT free-busy floating
behaviour » — le seul qui suppose UTC — échoue sur un état qu'un autre
fichier a laissé derrière lui, et non sur son propre défaut.

Contrairement à `reports.xml` et `delete.xml`, le correctif **ajoute** une
requête plutôt que d'en retirer une : en tête du `<start>`, une `PROPPATCH`
de `$calendarpath1:/` (soit `default`) remet `calendar-timezone` à UTC avant
la première requête du fichier. Le corps de la `PROPPATCH` est un
`DAV:propertyupdate` posant un `VCALENDAR` au seul `VTIMEZONE` `UTC`, dans un
fichier séparé (`floating-default-utc.xml`, à côté de celui-ci) plutôt qu'en
ligne : le schéma `<data>` de l'outil (`caldavtest.dtd`) ne connaît que
`filepath` ou `generator`, jamais de texte inline.

**Le chemin de ce `<filepath>` n'est pas `Resource/CalDAV/...`** comme dans
les deux copies voisines — celles-là visent une ressource du clone amont,
déjà sous le répertoire de travail du tester (`ccs-caldavtester/`). La nôtre
vit dans ce dépôt. Le tester ne reçoit jamais `--basedir` (voir `run.ps1`),
donc `self.data.filepath` est ouvert tel quel, relatif au répertoire de
travail au moment de l'exécution — `ccs-caldavtester/`, où `run.ps1` se place
avant de lancer `testcaldav.py`. Le chemin posé est donc
`../../suites/CalDAV/floating-default-utc.xml`, qui remonte de
`ccs-caldavtester/` à `tools/caldavtester/` puis redescend dans `suites/` —
vérifié sur disque (`ls` depuis ce répertoire de travail atteint bien ce
fichier), mais **pas rejouable hors ligne** : la partie harnais ne se vérifie
qu'au passage final de l'outil contre un vrai serveur.

Rien des tests propres du fichier ne change ; le diff exact est
`floating.xml.diff`, et c'est le seul des trois diffs de ce dossier qui
contient des lignes **ajoutées**.
