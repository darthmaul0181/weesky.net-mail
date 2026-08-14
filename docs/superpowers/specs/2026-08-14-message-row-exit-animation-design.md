# Sortie animée d'une ligne de message — design

Une ligne supprimée depuis les actions rapides au survol disparaît sèchement : rien ne relie ce qui
était sous le curseur à la liste qui se referme. Ce document décrit la sortie qui remplace ce saut,
et surtout le mécanisme qui la rend possible — car l'obstacle n'est pas l'absence d'animation, c'est
qu'il n'existe aujourd'hui **aucun instant où la ligne est encore montée et déjà en train de
partir**.

## Le constat qui gouverne tout le reste

`MessageList.moveTo()` appelle `useMoveMessages`, dont l'`onMutate` (`queries.ts`) retire l'uid des
caches de la liste **avant** que la requête ne parte — c'est le pari optimiste que tout le module
prend. React démonte le nœud au rendu suivant. Le clic et le démontage sont dans le même tour de
boucle.

Il en découle une seule architecture possible : **la mutation doit partir à la fin de l'animation,
pas au clic.** L'inverse — animer une ligne que le cache a déjà retirée — supposerait de garder
localement une copie complète du message et de la réinsérer à son ancien index dans les groupes
calculés par `threading.ts`. Deux sources de vérité pour une ligne, pour 300 ms de rendu.

Le prix est que la requête réseau part 300 ms après le clic. Il est nul : la mutation est optimiste,
l'écran a déjà répondu, et rien à l'écran n'attend la réponse.

## Les décisions

| Décision | Retenu | Pourquoi |
|---|---|---|
| Forme de la sortie | **Fondu sur place, puis repli** (variante A) | Choix du propriétaire sur maquettes. La plus sobre : elle ne raconte rien, elle évite le saut. |
| Découpage | 140 ms de fondu, puis 160 ms de repli — 300 ms | Le repli ne démarre qu'une fois la ligne invisible : c'est un trou qui se referme, pas un contenu qu'on écrase. |
| Moment de la mutation | À la fin de l'animation | Voir ci-dessus : son `onMutate` est ce qui démonte la ligne. |
| Où vit l'état | Un hook dans `MailLayout` | C'est le seul composant qui voit à la fois la liste, le drop sur un dossier et le lecteur. |
| Support du repli | Un conteneur autour de la ligne | Il faut un box qui atteigne vraiment zéro. Sur `.message-row` seule, `box-sizing: border-box` la plancherait à son propre padding. |
| Forme du repli | Une piste de grille `1fr → 0fr` | Une hauteur devrait être **mesurée** — on n'anime ni vers ni depuis `auto` — donc chaque appelant (la ligne, le lot, le drop) devrait aller chercher les nœuds et y écrire des pixels au clic. La grille replie sans rien mesurer. |
| Suppression en lot | **Tout d'un bloc** | Choix du propriétaire. Une cascade sur cinquante lignes est deux secondes d'attente. |
| Mouvement réduit | Suppression instantanée | C'est le comportement d'aujourd'hui, et c'est le bon repli. |
| Démontage avec des départs en attente | On **vide la file**, on n'annule pas | Une suppression demandée ne doit pas se perdre parce qu'on a cliqué Contacts dans la foulée. |

## Le mécanisme

### `list/useRowExit.ts` (nouveau)

```
useRowExit(): { departing: ReadonlySet<number>, depart(uids: number[], fire: () => void): void }
```

`depart` marque les uids comme partants, attend `ROW_EXIT_MS`, puis appelle `fire()` — c'est-à-dire
le `mutate` existant, inchangé, passé en clôture par l'appelant. Le hook ne connaît aucune mutation :
il ne sait que retarder un effet et dire quelles lignes sont en train de partir.

Quatre règles :

1. **`prefers-reduced-motion: reduce`** — `fire()` est appelé de façon synchrone et aucun uid n'entre
   dans `departing`. C'est exactement le comportement actuel.
2. **Un uid déjà en partance est ignoré.** Un double-clic sur la corbeille ne doit pas armer deux
   fois la même mutation.
3. **À l'échéance, le retrait du set et `fire()` tombent dans le même lot de rendu**, donc leur ordre
   ne change aucune image. Le set est libéré en premier pour qu'un `fire` qui lèverait ne puisse pas
   y laisser un uid coincé — invisible et non cliquable. Sur un `onError`, le rollback du cache
   ramène la ligne à sa taille normale sans animation inverse, avec le toast existant.
4. **Au démontage, les départs en attente sont tirés immédiatement** (`clearTimeout` puis appel),
   jamais annulés.

`ROW_EXIT_MS` (300) est exporté par le hook et est **la seule durée écrite en TypeScript**. Elle est
injectée dans le DOM en variable CSS ; la répartition 140/160 vit en pourcentages dans les keyframes
et suit donc toute retouche de la durée totale.

### Câblage

`MailLayout` instancie le hook et distribue :

- à `MessageList` : `departing` (pour poser la classe) **et** `depart` ;
- à `MessageReader` : `depart` seul.

`MessageList` remplace chacun de ses appels de mutation « départante » par `depart(uids, () =>
…mutate(…))`. `reportDeparted` / `onDeparted` **restent appelés au clic** : le lecteur avance tout de
suite, seule la liste prend son temps. `nextUidOf` saute déjà le lot, donc la ligne encore montée
mais partante ne perturbe pas le calcul du message suivant.

### DOM et CSS

`renderRow` enveloppe chaque `Row` dans un `<div className="message-row-slot">`. Vérifié avant
d'écrire : aucun sélecteur de `mail.css` ne dépend de `.message-row` étant enfant direct du `<li>`.
Les quatre seuls combinateurs qui touchent la ligne (`mail.css:662-663` et leur copie du bloc
téléphone, `mail.css:1986-1987`) portent sur ses **enfants** — la réserve de largeur du cluster — pas
sur ses ancêtres. Aucun sélecteur frère (`+`, `~`) n'existe sur `.message-row`.

Le slot est une grille à une piste (`grid-template-rows: 1fr`) au repos comme en partance, pour que
l'image où la classe arrive ne soit pas aussi un changement de `display` ; en partance il replie
`1fr → 0fr` sous `overflow: hidden` et porte `pointer-events: none`. La ligne à l'intérieur porte le
fondu d'`opacity`.

Sa colonne est `minmax(0, 1fr)` et non `1fr`. Le minimum automatique d'un item de grille est son
min-content, ce qu'un enfant de bloc ne pouvait pas imposer : **mesuré dans Chrome, une ligne à
l'expéditeur insécable passe à 743 px dans une colonne de 378** sans cette garde. C'est le piège que
la grille de détails du lecteur documente déjà.

Le padding vertical de la ligne devient `--pad-y` (10 px, 8 px dans le skin étroit) pour que les
keyframes puissent le retenir jusqu'à la fin du fondu puis le faire partir avec la piste. **Sans
cela le repli plafonne à 17 px** — 8 + 8 de padding et 1 de bordure sous `border-box` — et le trou ne
se referme jamais : constaté au navigateur, pas déduit.

Le CSS va dans `mail.css`, sous les règles de la ligne.

## Périmètre

| Chemin | Traité |
|---|---|
| Ligne au survol : Archive, Report as junk, Delete | oui |
| Ligne dans la corbeille : Delete permanently, après confirmation | oui — la sortie part à la confirmation, pas au clic |
| Barre de sélection : Archive, Junk, Delete, Delete permanently | oui, **tout d'un bloc** : un seul `depart(uids, fire)` |
| Drop sur un dossier (`MailLayout.dropMessages`) | oui |
| Lecteur : Delete, Archive, Report as junk sur le message ouvert | oui — la ligne correspondante s'anime dans la liste d'à côté |

Un fil replié supprimé emporte tout le lot : `rowUids` est déjà l'ensemble des membres, et une seule
ligne est à l'écran. Un membre d'un fil déplié a son propre slot et part seul.

`Move to…` suit, sans code en plus : il passe par le même `runBulk` que les trois autres actions de
la barre. Le retenir aurait demandé un drapeau pour le distinguer de ses voisins immédiats, ce qui
est exactement l'écart que le reste du document cherche à éviter.

**Hors périmètre**, et délibérément : `Copy to…` (une copie ne fait rien partir — la branche `copy`
ne passe pas par `runBulk`, et le lecteur la court-circuite explicitement), « Empty folder » (la
liste entière se vide, il n'y a pas de trou à refermer), et le module contacts, dont la suppression
de tuiles pose la même question mais dans un autre écran.

## Tests

`list/useRowExit.test.ts` (nouveau) :

- `departing` contient les uids pendant l'attente, `fire` n'a pas encore été appelé ;
- à l'échéance, `fire` est appelé **une seule fois** et le set est vide ;
- sous `prefers-reduced-motion: reduce`, `fire` est synchrone et le set reste vide ;
- un second `depart` sur un uid déjà en partance n'arme pas une deuxième mutation ;
- le démontage tire les départs en attente au lieu de les perdre.

`MessageList.test.tsx` : le hook entre par une prop, donc le fichier passe un `rowExit` qui tire
`fire` sur-le-champ. **Aucune assertion existante ne bouge** — la douzaine de `waitFor` qu'un délai
en dur aurait imposée n'a pas lieu d'être, et ce que ces tests vérifient (quel clic arme quelle
mutation, avec quels uids) reste vérifié au même endroit. Deux cas s'ajoutent pour la seule chose que
jsdom peut voir de la sortie : que `departing` atteigne le **slot** — et non la ligne — et qu'aucun
slot ne soit marqué quand rien ne part.

`probes/row-exit.html` (nouveau) est la vérification de ce que jsdom ne voit pas. Il ne s'échantillonne
pas au chronomètre — un onglet en arrière-plan bride `setTimeout` à la seconde et rend tout relevé
faux, ce qui a d'abord produit une mesure incohérente — mais en pilotant `currentTime` via
`getAnimations()`, ce qui est déterministe. Relevé dans Chrome, palette night :

| t | hauteur du slot | opacité | padding | hauteur de la liste |
|---|---|---|---|---|
| 0 | 80 | 1,00 | 8px | 320 |
| 141 | 80 | 0,00 | 8px | 320 |
| 220 | 8,3 | 0,00 | 1,6px | 248,3 |
| 300 | 0 | 0,00 | 0 | 240 |

La ligne s'efface sans que rien ne bouge, puis les 80 px partent en entier. `escapeRight` reste à 0
sur toute la durée. Restent hors de portée de ce relevé et vérifiés à l'œil : les deux thèmes, le
skin `is-line`, un membre de fil déplié, un lot, un drop.

## Questions ouvertes

Aucune.
