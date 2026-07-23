# Design — Actions bulk : bande liste plus haute (option B)

Date : 2026-07-23
Statut : approuvé (design)

## Contexte

La tranche 2b3 a introduit la multi-sélection avec une bande d'actions (`SelectionToolbar`) en
tête de la colonne liste : checkbox maître, compteur, boutons directs (Archive / Report as junk /
Delete / Move to…) et un kebab (Mark read/unread, Copy to…, Empty folder).

Deux emplacements ont été maquettés pour les actions groupées : (A) les remonter dans le TopBar
applicatif, ou (B) les **garder dans la bande liste mais rendre la bande plus haute** pour des
boutons plus grands. **L'option B est retenue** : actions au plus près des messages (proximité,
convention Gmail/Outlook), cibles plus grandes, pas d'action mail destructive dans le chrome global,
et implémentation nettement plus simple (rien à sortir de `MessageList`).

## Décisions (issues du brainstorming)

1. **Emplacement** : les actions **restent dans `SelectionToolbar`** (bande en tête de la colonne
   liste). Rien ne monte dans le TopBar.
2. **Hauteur** : la bande est **toujours haute** (boutons toujours affichés, grisés quand rien n'est
   coché) — pas de saut de mise en page à la première coche.
3. **Boutons directs conservés** : **Archive**, **Spam** (report as junk), **Delete**.
4. **Move to…** quitte les boutons directs et passe dans le **kebab**.
5. **Taille des boutons adaptée** à la bande plus haute (icônes plus grandes, cibles plus
   confortables).

## Portée réelle

Changement volontairement contenu, sans frontière architecturale traversée :

- `SelectionToolbar.tsx` : retirer `Move to…` des boutons directs et l'ajouter en tête du kebab ;
  appliquer la taille de bouton « bande haute ».
- `mail.css` : `.selection-toolbar` plus haute ; boutons d'action plus grands. **Tokens uniquement,
  aucune couleur littérale.** La hauteur exacte est **mesurée dans un vrai navigateur** (jsdom ne
  fait pas de layout) — cible ~64–66 px, à valider visuellement.
- `MessageList.tsx` : **inchangé** — il passe déjà `move`/`copy`/… en props à `SelectionToolbar` ;
  seul l'endroit où `SelectionToolbar` rend `move` change (kebab au lieu d'un bouton direct).

Ne changent pas : `useSelection`, les handlers, les modales (`MoveMessagesModal`,
`DeleteConfirmModal`), la logique de rôles/enablement (`selectionState`), l'`EmptyFolderBanner`, la
checkbox maître et le compteur (restent dans la bande).

## Détail des actions

- **Boutons directs** (bande) : Archive · Spam · Delete. Grisés quand `count === 0`, au-delà du cap
  (> 200) ou rôle interdit — logique `selectionState` **inchangée** (le cap gagne le tooltip, puis la
  raison de rôle). Icônes seules avec `aria-label`/`title`.
- **Kebab** (ordre exact, deux séparateurs) :
  1. `Mark as read`
  2. `Mark as unread`
  3. —séparateur—
  4. `Move to…`
  5. `Copy to…`
  6. —séparateur—
  7. `Empty folder`

  `Mark…`, `Move to…`, `Copy to…` suivent `selectionState` (grisés sans sélection). Le **trigger du
  kebab reste actif même sans sélection** (car `Empty folder` ne dépend pas de la sélection) ;
  `Empty folder` reste grisé par sa propre raison (dossier déjà vide / pas de corbeille).

## Style

- `.selection-toolbar` : hauteur augmentée (~64–66 px, mesurée en navigateur). Alignement vertical
  centré des trois zones (checkbox maître, titre/compteur, cluster d'actions poussé à droite).
- Boutons d'action agrandis : classe dédiée dans `mail.css` (ne pas gonfler `.row-btn`, partagée avec
  les boutons de ligne). Icônes ~20–21 px. Tokens uniquement.
- Le kebab utilise `DropdownMenu` comme aujourd'hui.

## Comportement

- Actions toujours présentes dans la bande, grisées quand `count === 0`.
- Clic bouton/kebab → handler de `MessageList` → ouvre la modale correspondante, exactement comme la
  2b3.
- Réinitialisation de la sélection (changement de dossier/page, Escape) **inchangée**.
- `EmptyFolderBanner` **inchangé** (reste pinné pour trash/junk, sous la bande).

## Tests

- `SelectionToolbar.test.tsx` : `Move to…` est désormais **dans le kebab**, pas un bouton direct.
  Assertions : boutons directs = Archive/Spam/Delete ; kebab dans l'ordre défini plus haut ; états
  grisés via `selectionState` pour Move/Copy/Mark ; trigger kebab actif sans sélection ; indeterminate
  sur la checkbox maître (inchangé).
- `MessageList.test.tsx` : les tests qui cliquaient `Move to…` en bouton direct ouvrent d'abord le
  kebab (`More actions` → `Move to…`). Le reste inchangé.
- Règles `settle()` **inchangées**.
- Hauteur/taille des boutons : non testables en jsdom (pas de layout) — vérification visuelle en
  navigateur au déploiement.

## Hors périmètre (YAGNI)

- Pas de changement du TopBar ni de portal/slot/contexte (l'option A est abandonnée).
- Pas de changement des sémantiques d'action ni des modales.
- Pas de déplacement de la checkbox maître ni du compteur.
- Pas de raccourcis clavier nouveaux.
