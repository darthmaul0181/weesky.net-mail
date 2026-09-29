# Palette perso — conception

Date : 2026-09-28 · Branche : `colors-palette` · Maquette validée :
https://claude.ai/artifact/UYeF1GXz1UgiGwTa4iZgpp

## Le but

Laisser chaque utilisateur composer **sa** palette sans qu'il puisse produire une interface
illisible ou étrangère au site. Il choisit des teintes ; le webmail décide de tout le reste.

Critères de réussite :

- une 9e carte « My palette » s'ajoute aux 8 palettes prédéfinies de l'onglet Apparence ;
- quatre réglages suffisent : teinte de structure, intensité de structure, teinte d'accent, couleur des boutons en mode clair ;
- quelle que soit la combinaison, tous les couples texte/fond respectent les seuils du site
  (décision 3), en clair comme en sombre ;
- la palette perso suit le compte d'un appareil à l'autre ; le **choix** de la palette active
  reste propre à chaque appareil, comme aujourd'hui ;
- aucune couleur de la mauvaise palette ne s'affiche au chargement (pas de flash).

## Les décisions

### 1. Retrait de Bordeaux, Mocha et Graphite

Les trois palettes ajoutées sur cette branche (commit `f854a595`) sont retirées : elles
n'apportent pas assez face aux 8 existantes, et la palette perso couvre ce besoin. Tout revient
à l'état d'avant ce commit (fichiers `theme-*.css`, listes de `ThemeContext`, `main.tsx`,
`index.html`, `AppearancePage`, compte du test des bandes de balayage, textes « sixteen »).
Une préférence locale qui nommait l'une d'elles retombe sur `night`, comme le prévoit déjà la
résolution.

### 2. Quatre réglages, pas de pipette

| Réglage | Valeurs | Rôle |
|---|---|---|
| Teinte de structure | entier 0–359 (OKLCH) | barre du haut, rail, boutons en clair, teinte des fonds |
| Intensité de structure | `neutral`, `muted`, `vivid` | gris anthracite (Ink), couleur éteinte (Night, Slate), couleur franche (Sea breeze) |
| Teinte d'accent | entier 0–359 | non-lus, compteurs, icône active, boutons en sombre |
| Boutons en mode clair | `structure`, `accent` | la couleur des boutons principaux en mode clair |

Le 4e réglage a été ajouté le 2026-09-29, après comparaison sur la maquette : la structure garde le comportement de Night, Forest et Plum, l'accent celui de Slate. Il ne vaut qu'en mode clair, puisqu'en sombre les boutons portent l'accent de toute façon (une structure foncée s'y dissout) ; le libellé le dit, sans quoi « Structure » en mode sombre semblerait sans effet. Choisir l'accent sur des teintes jaunes ou orangées donne des boutons plus ternes en clair, l'accent étant assombri pour porter du texte blanc : l'aperçu le montre avant l'enregistrement.

Pas de barre du haut claire, pas d'intensité pour l'accent (un accent doit rester vif), pas de
couleur exacte : une pipette obligerait à afficher une autre couleur que celle choisie.

### 3. Le générateur garantit la lisibilité par construction

`src/frontend/src/lib/customPalette.ts`, fonctions pures, sans dépendance :

- `generateCustomPalette(def)` rend deux jeux de tokens, `light` et `dark`, qui couvrent
  **exactement** les rôles de `theme-night.css` (43 en clair, 37 en sombre) ;
- les couleurs sont calculées en OKLCH : la clarté est fixée par rôle, la chroma est réduite
  jusqu'à rester dans sRGB, et une recherche sur la clarté impose chaque seuil au lieu de
  l'espérer ;
- seuils imposés : texte et texte secondaire ≥ 4,5:1 sur leurs fonds réels (dont
  `--folders-bg`) ; `--topbar-fg` sur la barre ≥ 7:1 ; `--badge-count-fg`, `--rail-item-active-fg`
  et `--list-row-selected-fg` ≥ 4,5:1 ; `--accent-unread` ≥ 3:1 sur `--surface` ;
- `--action-primary-fg` reste `#ffffff` dans les deux modes (décision existante) : l'accent
  sombre qui porte les boutons est tenu à ≥ 2,8:1 sous le blanc, le niveau du corail de `night` ;
- les rôles sémantiques (`--danger*`, `--success`, `--warning`, `--status-fg`, `--scrim`,
  `--swipe-*`, `--icon-hover-danger`) reprennent les valeurs de `night` : ils ne dépendent pas
  de la palette ;
- `customPaletteCss(tokens)` rend le texte CSS des deux blocs
  `[data-palette='custom']` et `[data-palette='custom'][data-theme='dark']`, **non ancrés** à
  `html` comme les autres palettes, pour que les vignettes puissent l'afficher.

Les constantes (clartés, chromas, seuils) sont celles de la maquette, vérifiées sur les 15 552
combinaisons (teintes par pas de 5°, 3 intensités).

### 4. Stockage : la définition sur le compte, le choix sur l'appareil

- **Définition** : une préférence serveur `ui.customPalette`, valeur `""` (aucune palette) ou
  `"<structure>,<intensité>,<accent>,<boutons>"`, par exemple `"265,muted,35,structure"`. Défaut `""`.
  Le 4e champ est facultatif à la lecture : une palette enregistrée sur 3 champs garde des boutons
  en structure, et le client écrit toujours les 4.
- Le registre `UserPreferences` ne connaît que des listes fermées ; `PreferenceDefinition` gagne
  un validateur optionnel. Celui de `ui.customPalette` accepte `""` ou le motif strict
  `^(0|[1-9][0-9]{0,2}),(neutral|muted|vivid),(0|[1-9][0-9]{0,2})(,structure|,accent)?$` avec deux
  teintes entre 0 et 359, sans zéro de tête. Une ligne stockée devenue invalide retombe sur le défaut, comme les autres clés.
- **Choix** : `appearance_palette` en `localStorage`, comme aujourd'hui, accepte en plus
  `custom`. Le thème ne change pas.
- **Copie locale** : `appearance_custom_palette` garde la définition et
  `appearance_custom_palette_css` le CSS généré. Seul le code du webmail les écrit, et
  uniquement à partir d'une définition validée.

### 5. Chargement sans flash

- Le script de pré-affichage d'`index.html` accepte `custom` : s'il trouve le CSS en copie
  locale, il l'insère dans un `<style id="custom-palette">` et pose `data-palette="custom"` ;
  sinon il pose `night`. Il ne génère rien lui-même.
- Un composant `CustomPaletteSync`, monté sous `AuthProvider`, lit la préférence dès que les
  préférences arrivent. Il régénère le CSS, met à jour le `<style>` et la copie locale.
- Si le compte n'a pas de palette perso (`""`), il retire le `<style>` et la copie locale, et
  si l'appareil avait choisi `custom`, il repasse sur `night`. C'est le cas d'un autre compte
  qui se connecte sur le même appareil.
- Une palette modifiée sur un autre appareil arrive au chargement suivant des préférences.

### 6. L'onglet Apparence

- **9e carte** après les 8 prédéfinies :
  - **sans palette perso** : un bouton en pointillés « Create my palette » ;
  - **avec** : vignette et nom « My palette », sélectionnables comme les autres. Un bouton crayon
    « Edit my palette » s'ajoute à la loupe, avec le même comportement : il apparaît au survol
    ou au focus, et reste visible sur écran tactile.
- **Éditeur** dans une `Modal` :
  - deux curseurs de teinte, `<input type="range">` étiquetés, sur un dégradé de teintes ;
  - l'intensité en contrôle `.seg` ;
  - deux vignettes `PalettePreview` en grand, « Light » et « Dark », qui suivent le brouillon en
    direct ;
  - « Cancel » et « Save ».
- `PalettePreview` gagne une prop optionnelle `tokens` qui pose les rôles en propriétés CSS
  inline : le brouillon s'affiche sans toucher au `<style>` de la palette enregistrée.
- **Point de départ** : la palette perso si elle existe, sinon les teintes de la palette
  active (une table de graines par palette prédéfinie, celle de la maquette).
- **Avertissement** sous les curseurs quand l'accent est à moins de 8° de la teinte du rouge
  d'erreur (27°) : « les compteurs pourraient ressembler à une alerte ». Il ne bloque pas.
  Il n'y a pas d'avertissement « teintes trop proches » : Sea breeze, Azure et Indigo sont
  monochromes par choix.
- **Enregistrer** écrit la préférence. En cas de succès, la palette devient `custom` sur cet
  appareil et la fenêtre se ferme. En cas d'échec, la fenêtre reste ouverte avec un message
  d'erreur, et rien n'est sélectionné.
- Pas de bouton « Supprimer » : on ne sort de la palette perso qu'en choisissant une autre carte.
- Textes en anglais et en français dans `settings.json`.

## Hors périmètre

Barre du haut claire, intensité de l'accent, plusieurs palettes perso, synchronisation du
choix de palette ou du thème entre appareils, couleur du manifeste d'application (elle reste
celle de `night`).

## Les tests

- **Backend** : le validateur de `ui.customPalette` (valeurs limites 0 et 359, 360, zéro de
  tête, intensité inconnue, champs manquants, vide accepté) ; `Effective` qui retombe sur `""`
  pour une ligne invalide.
- **Générateur** :
  - les jeux clair et sombre ont exactement les rôles de `theme-night.css` ;
  - un balayage de toutes les combinaisons (teintes par pas de 5°, 3 intensités) vérifie chaque
    seuil de la décision 3 ;
  - la sortie est déterministe ;
  - `customPaletteCss` produit deux blocs non ancrés.
- **Pré-affichage** : la liste du script reste égale à `PALETTE_IDS` plus `custom` ; le script
  retombe sur `night` quand le CSS manque.
- **`CustomPaletteSync`** :
  - la définition arrive → le `<style>` et la copie locale sont écrits ;
  - la définition est vide → ils sont retirés, et `custom` repasse sur `night`.
- **`AppearancePage`** :
  - la carte « Create » ouvre l'éditeur sur les graines de la palette active ;
  - « Save » écrit la préférence et sélectionne `custom` ;
  - un échec garde la fenêtre ouverte ;
  - le crayon rouvre l'éditeur sur la palette enregistrée ;
  - l'avertissement du rouge apparaît et disparaît ;
  - l'accessibilité suit la suite a11y existante.

## Documentation

`.claude/rules/frontend-theming.md` (la palette `custom`, son générateur, sa copie locale, le
script de pré-affichage) et `src/frontend/docs/architecture-settings.md` (l'éditeur).
