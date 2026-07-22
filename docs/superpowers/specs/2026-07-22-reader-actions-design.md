# Zone d'actions du lecteur — design

Date : 2026-07-22. Maquette validée : https://claude.ai/code/artifact/c6757379-f5a9-41ff-a21c-7d36b1e93997

## Objectif

Remplacer le bandeau de choix des couleurs (`.reader-colour-note` — une ligne de texte plus un
bouton, affichée en thème sombre au-dessus du corps du message) par une **zone d'actions calée
en bas à droite de l'en-tête du lecteur** :

```
Titre                                    ┐
Sender  [✓]  (date)                      │ pile existante
To: … / Cc: …                            │
Spam score: [jauge]          [☀|☽] | [⋮] ┘  ← zone d'actions, alignée bas-droite
```

Deux boutons : le toggle couleurs (soleil/lune) et un bouton kebab (trois points **verticaux**)
qui provisionne le futur menu d'actions du message.

## Comportement du toggle couleurs

L'icône montre **l'action à venir**, pas l'état courant — la convention des toggles de thème.
Le tooltip décrit ce que le clic donne :

| État courant | Icône | Tooltip | Clic |
|---|---|---|---|
| Couleurs adaptées au thème (défaut sombre) | ☀ soleil | Showing the colours the sender chose | → couleurs d'origine |
| Couleurs d'origine | ☽ lune | Colours are adapted to your dark theme | → retour au thème |

- Visible seulement quand `isDark && data.htmlBody` — la condition actuelle du bandeau :
  recolorer un mail texte n'a pas de sens, et en thème clair rien n'est recoloré.
- `aria-label` = l'action, reprenant les libellés des anciens boutons : `Original colours`
  (soleil) / `Match my theme` (lune). Les tests existants s'y raccrochent.
- La logique d'état ne bouge pas : `originalColours` reste dans `MessageReader`, remis à zéro à
  chaque message comme le consentement aux images.

## Le bouton kebab

- Trois points **verticaux** (`KebabIcon`), toujours visible, dans les deux thèmes.
- Inerte pour l'instant — pas de handler, comme le bouton sender : le jour où le menu existe,
  il ne manquera qu'un `onClick`. `aria-label` : `Message actions`.
- Tooltip : aucun pour l'instant (un tooltip qui dit « menu à venir » est du bruit ; l'aria-label
  suffit aux lecteurs d'écran).

## Le séparateur

Un filet vertical (1px × 18px, `--border`) entre les deux boutons — rendu **seulement quand le
toggle l'est** : un filet à côté d'un bouton seul lirait comme un défaut de rendu, la même règle
que le `<hr>` du folder tree.

## Structure et composants

**`reader/ReaderActions.tsx`** — la zone entière :

```tsx
interface Props {
  showColourToggle: boolean
  originalColours: boolean
  onToggleColours: () => void
}
```

Rend `<div className="reader-actions">` avec le toggle (enveloppé dans `Tooltip`
`placement="bottom-right"`), le filet, le kebab. Composant pur ; c'est `MessageReader` qui
calcule `showColourToggle = isDark && !!data.htmlBody` et possède l'état.

**`MessageReader.tsx`** — l'en-tête passe en flex :

```tsx
<header className="reader-header">
  <div className="reader-stack">   {/* h1 + .reader-meta, l'existant, inchangé */}
  <ReaderActions … />
</header>
```

Le bloc `{isDark && data.htmlBody && <div className="reader-colour-note">…}` est **supprimé**.

**Icônes** — `src/icons/SunIcon.tsx`, `MoonIcon.tsx`, `KebabIcon.tsx`, sur le pattern existant
(un fichier par icône, SVG inline, prop `size`, `stroke="currentColor"` pour soleil/lune,
`fill` pour le kebab). Kebab : trois cercles sur l'axe vertical.

**`Tooltip`** — troisième placement `bottom-right` (`top: calc(100% + 8px); right: 0`,
`width: max-content; max-width: 320px`) : pour un déclencheur collé au bord droit de la
colonne, les deux placements existants ouvriraient la bulle dans l'`overflow: hidden`.
Union de types élargie, un cas de test ajouté.

## CSS (`mail.css`)

- `.reader-header` : `display: flex` (le padding et la bordure existants restent).
- `.reader-stack` : `flex: 1; min-width: 0` — la pile ne peut jamais passer sous les boutons,
  pas de position absolue.
- **La hauteur de l'en-tête est variable et la zone suit** : sans jauge spam (réglage
  désactivé ou message sans header antispam), sans Cc, ou sans To, `align-self: flex-end`
  cale les boutons sur la dernière ligne réellement présente. Rien n'est ancré à une hauteur
  fixe — c'est la raison du choix flex contre l'absolu.
- `.reader-actions` : `align-self: flex-end; display: flex; align-items: center; gap: 2px`.
- `.action-btn` : 30×30, `border-radius: var(--radius-sm)`, fond transparent,
  `color: var(--text-muted)` ; hover → `color: var(--action-primary)` +
  `background: var(--surface-sunken)`.
- `.actions-rule` : `width: 1px; height: 18px; background: var(--border)`.
- Les blocs `.reader-colour-note` et `.reader-colour-note .btn` sont **supprimés**.
- Aucun token nouveau, aucune couleur littérale.

## Tests

- `ReaderActions.test.tsx` — soleil + tooltip + aria-label en état adapté ; lune + son tooltip
  en état original ; clic appelle `onToggleColours` ; toggle et filet absents quand
  `showColourToggle` est faux, kebab toujours là ; kebab sans handler ne jette pas au clic.
- `MessageReader.test.tsx` — les trois tests dark-mode existants adaptés : le bandeau et son
  texte disparaissent, les boutons se trouvent par leur `aria-label` (inchangés : `Original
  colours` / `Match my theme`) ; nouveau : pas de toggle en thème clair ni sur un mail
  texte-seul ; le kebab présent dans les deux thèmes.
- `Tooltip.test.tsx` — le placement `bottom-right` pose `is-bottom-right`.
- `icons.test.tsx` — suivre ce que le fichier existant fait pour les autres icônes.

## Hors périmètre

- Le menu du kebab (contenu, ouverture, actions) — une tranche future ; seul le bouton existe.
- Aucun changement backend, aucun changement de préférences.
