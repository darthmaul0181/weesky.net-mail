# Copies locales : `reports.xml` et `delete.xml`

Copies de `scripts/tests/CalDAV/reports.xml` et `delete.xml` du clone amont
`https://github.com/apple/ccs-caldavtester.git`, épinglé au commit
`bed21e5924275552c1561febc8203a9f194cf737`. Voir la spec 5d, décision 10, pour
le pourquoi complet ; ce fichier résume ce qui est retiré.

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
