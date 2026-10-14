# Nouveautés (« What's new ») — conception

Date : 2026-10-14 · Branche : `whats-new` ·
Maquettes : https://claude.ai/artifact/Gfrqm3uK8aAH6GPuQbTGv9

## Le but

Faire découvrir aux utilisateurs ce qui a changé après une mise à jour, sans les interrompre : un
point de couleur signale la nouveauté et un chemin de points les guide jusqu'à elle.

Critères de réussite :

- après une mise à jour qui apporte une nouveauté, une icône à droite de la barre du haut porte un
  point ; elle ouvre la liste des nouveautés ;
- un chemin de points mène à la nouveauté (ex. engrenage → ligne *Appearance*), et l'élément visé
  s'affiche encadré avec une étiquette *New* ;
- la nouveauté atteinte, ses points disparaissent sur tous les appareils de l'utilisateur ;
- un point jamais suivi disparaît seul 30 jours après son premier affichage pour cet utilisateur ;
- un nouveau compte ne voit aucune nouveauté antérieure à son arrivée ;
- une nouveauté liée à une fonction que l'administrateur a coupée n'est pas signalée.

Hors champ : annonces rédigées par l'administrateur, fenêtre surgissante, visite guidée, images ou
animations dans la liste.

## Les décisions

### 1. Qui écrit les nouveautés, et où

Le développeur, dans le code du site, dans la même branche que la fonctionnalité ; le propriétaire
valide le texte à la relecture. Toutes les instances affichent les mêmes nouveautés, puisqu'elles
voyagent avec l'image. Le `CLAUDE.md` du projet gagne une règle : toute fonctionnalité visible par
l'utilisateur prévoit son entrée « What's new » dans son plan, sauf avis contraire du propriétaire.

### 2. Le catalogue

`src/frontend/src/whatsNew/catalog.ts`, une entrée par nouveauté :

```ts
{ id: 'daily-image', version: '2.4.0',
  path: ['rail:settings', 'settings:appearance'], target: 'appearance:daily-image',
  href: '/settings/appearance',
  when: ({ appSettings }) => dailyImageOf(appSettings) }
```

- `id` : `[a-z0-9-]{1,64}`, unique.
- `version` : version du site (`src/frontend/VERSION`) qui livre l'entrée.
- `path` : repères, dans l'ordre, qui portent un point ; `target` : repère encadré à l'arrivée.
- `href` (facultatif) : où mène *Show me* ; absent (cible dans un contenu quelconque), pas de bouton.
- `when` (facultatif) : condition d'existence, évaluée sur les réglages d'instance, les capacités et
  le compte actif.
- Textes : `locales/{en,fr}/whatsNew.json`, clés `<id>.title` et `<id>.body`.

Première entrée : l'image du jour (`daily-image`, version 2.4.0, condition = activée par
l'administrateur).

### 3. Les repères

Un repère est un identifiant `<zone>:<nom>` : le point se pose **dans** l'élément cliquable, la
cible s'enroule autour de son bloc :

```tsx
<a href="/settings/appearance">Appearance<WhatsNewDot anchor="settings:appearance" /></a>
<WhatsNewTarget id="appearance:daily-image">…</WhatsNewTarget>
```

- Les repères du rail sont déclarés avec les modules, dans `layouts/modules.ts`
  (`anchor: 'rail:settings'`).

- Un repère de `path` porte un point tant qu'une nouveauté active passe par lui.
- Un repère `target` s'encadre (anneau corail, étiquette *New*) tant que sa nouveauté est active.
- Pages, lignes de réglages, boutons qui ouvrent une boîte de dialogue, menus, boutons du composeur
  (`/mail/compose`) : tous peuvent porter un repère.
- Boîte de dialogue ouverte depuis un contenu quelconque (un événement, un message) : le chemin
  s'arrête au module, le texte de la liste précise l'étape (« Ouvrez un événement pour… ») ; la
  cible s'encadre à l'ouverture.
- Les repères de l'engrenage valent dans le rail (bureau) et dans la barre du bas (téléphone).

### 4. Nouveauté active

Une entrée est **active** pour un utilisateur quand :

1. sa condition `when` est vraie (ou absente) ;
2. sa `version` est strictement postérieure à la **référence** de l'utilisateur ;
3. elle n'est pas vue (`seen_at` vide) ;
4. elle n'a pas expiré : `first_shown_at` vide ou il y a moins de 30 jours.

Comparaison de versions : `major.minor.patch` numérique, suffixe `-dev` ignoré.

### 5. La référence

Préférence `whatsNew.baseline` du compte (table `user_preferences`), déclarée « gérée » :
`PUT api/Preferences` la refuse, seul `PUT api/WhatsNew/baseline` l'écrit.

- La migration la pose à `0.0.0` pour tous les comptes existants : ils voient tout le catalogue.
- Un compte créé ensuite n'en a pas ; au premier chargement, le site y écrit sa version courante
  (`WEB_VERSION`) : le nouveau venu ne voit que les nouveautés publiées après son arrivée.
- Elle ne s'écrit qu'une fois (voir § API).

Versions plutôt que dates : une instance mise à jour en retard ne fausse rien. Une préférence
plutôt qu'une colonne de `users` : la migration reste un seul script rejouable, sans `ALTER`.

### 6. Les états par nouveauté

Table `whats_new_items` :

| colonne | rôle |
|---|---|
| `user_id` | FK `users.id`, `ON DELETE CASCADE` |
| `item_id` | identifiant du catalogue |
| `first_shown_at` | premier affichage d'un point ; départ des 30 jours |
| `listed_at` | première ouverture de la liste qui la montrait |
| `seen_at` | cible atteinte |

Clé primaire (`user_id`, `item_id`). Les lignes ne sont jamais purgées : elles empêchent un point
expiré ou vu de revenir. Une entrée retirée du catalogue laisse des lignes inertes, ignorées.

### 7. L'API

Trois appels authentifiés ; l'utilisateur est celui de la session, jamais un paramètre.

- `GET api/WhatsNew` → `{ baseline, items: [{ id, firstShownAt, listedAt, seenAt }] }`.
- `PUT api/WhatsNew/items` ← `{ ids, state: 'shown' | 'listed' | 'seen' }` → 204. Une seule
  méthode paramétrée par l'état ; chaque état ne remplit sa colonne que si elle est vide
  (horodatage serveur, UTC). Tout état remplit aussi `first_shown_at` s'il est vide.
- `PUT api/WhatsNew/baseline` ← `{ version }` → 204 ; n'écrit que si la référence est vide (409
  sinon).

Validation : identifiants `[a-z0-9-]{1,64}`, 1 à 50 par appel, état connu, version
`\d+\.\d+\.\d+(-dev)?` ; sinon 400. Le serveur ne connaît pas le catalogue : il ne stocke que des
identifiants valides.

### 8. Ce que fait le site

- Un hook unique charge `GET api/WhatsNew`, calcule les nouveautés actives (§ 4) et alimente
  points, liste et cibles.
- Référence vide → `PUT baseline` avec `WEB_VERSION`, une fois.
- Nouveautés actives sans `first_shown_at` → un `PUT items` `shown` groupé.
- **Icône du haut** (étoile scintillante, à droite de la barre) : point s'il reste une nouveauté
  active sans `listed_at`. L'ouvrir envoie `listed` pour les nouveautés actives affichées ; les points
  du chemin restent. Elle disparaît quand plus aucune nouveauté n'est active (toutes vues ou
  expirées), jamais tant que sa liste est ouverte.
- **Liste** (planche A) : nouveautés applicables (`when` vrai, version > référence), plus récentes
  d'abord, dix au plus ; actives avec leur point et un bouton *Show me* plein, les autres estompées.
  *Show me* mène à `href` et fait défiler jusqu'à la cible. Pied : *See all* → `/settings/whats-new`.
- **Page `/settings/whats-new`** : l'historique complet, groupé par version. Sur téléphone (sans barre
  du haut), c'est la ligne *What's new* du groupe *Application*, juste avant *About*, avec son point ; le point de la liste
  se pose sur l'onglet *Settings* de la barre du bas.
- **Arrivée** : la cible visible à au moins 50 % (IntersectionObserver) → `seen`. Le cadre et
  l'étiquette restent jusqu'à ce que l'utilisateur quitte la page ; absents à la visite suivante.
- Erreur réseau : aucun point, aucun message ; les états non envoyés le seront au chargement suivant.

### 9. Couleur et accessibilité

- Quatre jetons dans chaque palette, clair et sombre, palette personnalisée comprise, vérifiés par test :
  - `--new-dot` ≥ 3:1 sur les fonds clairs (`--bg`, `--folders-bg`, `--surface`, `--pane-item-active-bg`) :
    points des lignes de réglages, de la liste, de la page, et cadre de la cible ;
  - `--new-dot-chrome` ≥ 3:1 sur les barres (`--rail-bg`, `--topbar-bg`, `--rail-item` au survol) :
    engrenage, icône du haut, onglet du téléphone. Une seule couleur ne tient pas les deux familles de
    fonds dans toutes les palettes ; teinte chaude (or, corail) là où l'accent a la teinte du rail ;
  - sur un élément actif du rail, le point prend l'encre `--rail-item-active-fg` ;
  - `--new-tag-bg` / `--new-tag-fg` : étiquette *New*, texte blanc ≥ 4.5:1.
- Chaque point porte un texte masqué « New » ; l'icône s'annonce « What's new, N new ».

## Tests

Site :

- calcul des nouveautés actives : référence, version `-dev`, expiration, `when`, vue ;
- points sur les repères, icône et `listed`, liste et *Show me*, `seen` à l'apparition ;
- cohérence du catalogue : identifiants uniques et valides, textes EN/FR présents, chaque repère de
  `path` et chaque `target` porté par un composant du code, `href` résolu par une route.

Serveur :

- les trois appels ; un état ne réécrit jamais une colonne remplie ; référence non écrasable (409) ;
- validation (identifiants, nombre, état, version) ;
- isolation : un utilisateur ne lit ni n'écrit les lignes d'un autre.

Une seule migration, `0003_whats_new.sql` (table + référence `0.0.0` des comptes existants),
rejouable après un échec : `CREATE TABLE IF NOT EXISTS` et `INSERT IGNORE`, aucun `ALTER`. Testée sur
MariaDB comme `0002`. `install/install.sql` ne crée aucune table et `adopt-existing-database.sql` ne
journalise que `0001` : ni l'un ni l'autre ne change.
