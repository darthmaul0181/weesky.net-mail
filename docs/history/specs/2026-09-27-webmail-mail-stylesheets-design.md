# Les feuilles de style des mails

Un mail « responsive » s'adapte au téléphone par ses propres règles `@media`, écrites dans un
`<style>`. Le lecteur les jette aujourd'hui : le serveur ne garde que le `<body>` et supprime tout
`<style>` (`MailHtmlSanitizer.DropWithContent`), puis le client le supprime une seconde fois
(`sanitizePolicy.ts`, `FORBID_TAGS`). Le mail retombe sur sa mise en page de bureau, 600 à 700 px
de large, et le lecteur défile de côté sur un écran de 360 px. Outlook garde ces règles : le même
mail y tient dans l'écran.

Relevé sur deux mails réels (Claude, Loterie Nationale, 2026-09-24) :

- les deux portent `@media only screen and (max-width: 499px | 600px)` : largeurs à 100 %, marges
  réduites, titres ramenés, bandeau en `width: 100%; height: auto` ;
- ces règles visent des **classes** et un **id** (`.full-width`, `.wrapper`, `#wrapper-td`), que le
  serveur supprime aussi ;
- elles reposent sur `!important` pour passer devant les styles en ligne ;
- les deux déclarent `<meta name="color-scheme" content="light dark">` et un vrai design sombre dans
  `@media (prefers-color-scheme: dark)` — la Loterie y échange même son logo clair contre un sombre ;
- aucun n'utilise `@import` ni `@font-face`.

## Décisions

| # | Question | Décision |
|---|---|---|
| 1 | Garder les `<style>` du mail | **Oui**, à la lecture, filtrés côté serveur par la même liste de propriétés que les styles en ligne. Ceux du `<head>` sont ramenés dans le corps avant le filtrage. |
| 2 | `class` et `id` | **Autorisés à la lecture.** L'éditeur n'est pas concerné : une réponse relit le mail sur le serveur (`PrepareQuote`, filtre des mails sortants), jamais le corps affiché par le lecteur, et Squire se sert lui-même de classes pour ses mises en forme. |
| 3 | Règles `@` acceptées | `@media` et `@font-face`. Tout le reste disparaît, `@import` compris. |
| 4 | `@import` | **Refusé.** La feuille importée n'arrive qu'à l'affichage : le serveur ne l'a jamais vue et ne peut pas la filtrer, elle pourrait rétablir un `position: fixed` que la liste interdit. Gmail et Outlook l'ignorent aussi. |
| 5 | `@font-face` | **Accepté sous consentement**, comme une image : bloqué par la CSP de l'iframe tant que les images distantes ne sont pas affichées, chargé ensuite. Le mail garde sa police de secours d'ici là. |
| 6 | `url()` dans une feuille de style | Admis **uniquement** dans le `src` d'un `@font-face`, en `https` ou `http` (Ganss tronque une adresse `data:` au premier `;`). Partout ailleurs la déclaration est supprimée, sans restauration possible — une image de fond déclarée en feuille de style est rare, et le mécanisme `data-blocked-bg` est attaché à un élément, pas à une règle. |
| 7 | Mode sombre de l'expéditeur | **Honoré** quand le mail le déclare **et** qu'une de ses feuilles le dessine (ci-dessous). |
| 8 | Filet indépendant du serveur | **Une CSP** posée dans le document de l'iframe : rien de distant ne se charge sans consentement, quoi que le nettoyage ait laissé passer. |
| 9 | Hors périmètre | La réécriture heuristique des largeurs pour les mails **sans** règles mobiles ; la restauration des images de fond déclarées en feuille de style. |

## Serveur — `MailHtmlSanitizer`

1. **Les `<style>` du `<head>` sont déplacés en tête du `<body>`** dans la passe qui précède Ganss
   (celle qui fait déjà `UnwrapDisallowedTags`), dans leur ordre d'apparition. `style` quitte
   `DropWithContent` ; `head`, `title` et le reste y restent.
2. **`style`, `class` et `id` rejoignent les listes autorisées.** Ganss nettoie alors le contenu du
   `<style>` : chaque déclaration passe par `AllowedCssProperties`, la même liste que les styles en
   ligne — donc toujours ni `position`, ni `z-index`, ni `float`. `!important` doit survivre : c'est
   la **première vérification** du plan, avant tout le reste, car sans lui les règles mobiles ne
   s'appliquent pas.
3. **Règles `@` : `@media` et `@font-face` seulement** (`AllowedAtRules`). Les descripteurs d'un
   `@font-face` ne passent pas par la liste des propriétés : aucun ne charge quoi que ce soit, sauf
   `src`, jugé au point 4. `font-display` ne survit pas, mais par l'analyseur (AngleSharp.Css ne le
   connaît pas et le retire), pas par une liste.
4. **Les `url()` d'une feuille de style** sont jugés dans une passe après Ganss, sur le texte des
   règles (AngleSharp.Css) : admis dans le `src` d'un `@font-face` si **chacune** de ses adresses
   est en `https` ou `http` — sinon la règle `@font-face` entière disparaît —, supprimés avec leur
   déclaration partout ailleurs. Les sélecteurs ne sont jamais touchés : un sélecteur échappé
   (`.sm\:px-4`, courant dans les mails générés) est légitime. Un `<style>` dont le texte final
   contient `</` est retiré entier : c'est la seule façon d'en sortir. **Un antislash dans le texte
   d'une feuille** en retire le `<style>` entier, avant Ganss (`CullEscapedStyleSheets`/
   `HoldsUnsafeEscape`), sauf s'il est immédiatement suivi d'un des signes de ponctuation qu'un mail
   généré échappe dans un nom de classe (`: / . % @ ! # , + [ ] ( ) = ~ ^ $ | &`, comme dans
   `.sm\:px-4` ou `.w-1\/2`) : aucun de ces signes ne peut épeler une lettre, un chiffre hexadécimal,
   un guillemet ou une accolade, donc aucun ne peut ouvrir un `url(`, une chaîne ou un bloc (`*` en a
   été retiré : `\/\*` ressortait en vrai `/*` une fois l'échappement réécrit sans antislash par
   Ganss). Trois analyseurs CSS écrits à la main pour ne supprimer que la déclaration fautive ont
   chacun fermé un contournement et ouvert un autre — un `url(` échappé, un guillemet ouvrant une
   chaîne dans un `url()` non guillemeté, un commentaire recollant deux jetons, un `@font-face` pris
   pour une règle qui imbrique — donc l'analyseur est abandonné plutôt que rapiécé une quatrième
   fois : une feuille portant un antislash imprévu retombe sur l'absence de feuille, exactement le
   rendu d'avant ce point. **Cet argument ne vaut que pour la feuille brute** : Ganss peut réécrire
   un échappement sûr sans antislash (`a\(` devient `a(`), donc `VetStyleSheets` réanalyse son propre
   texte vétu (`ToCss()`) et le réécrit une seconde fois, ne gardant la feuille que si cet
   aller-retour reproduit exactement le même texte — la garantie que la feuille écrite se relit à
   l'identique, donc que le navigateur lit exactement ce que `VetRules` a jugé.
5. **Si Ganss ne sait pas faire l'un de ces points** (conserver `!important`, filtrer une règle
   `@font-face`), le plan écrit la passe manquante sur AngleSharp.Css plutôt que de renoncer au
   point. Le résultat attendu est celui décrit ici, pas un outil en particulier.
6. **Le mail dit s'il a un mode sombre.** `SanitizedHtml` et `MailMessageDetail` gagnent
   `DeclaresDarkScheme` (booléen) : vrai quand le mail porte un `<meta name="color-scheme">` ou
   `<meta name="supported-color-schemes">` dont le contenu nomme `dark`, ou une déclaration
   `color-scheme` qui le nomme dans une de ses feuilles de style. Ces `<meta>` sont lus avant d'être
   jetés avec le `<head>`. Côté client le champ est optionnel (`declaresDarkScheme?: boolean`) :
   le frontend et l'API se déploient séparément, et une API plus ancienne ne l'envoie pas, ce qui
   doit se lire « pas de mode sombre déclaré ».

La règle 6 de `src/scotty.microservice/CLAUDE.md` s'enrichit des points 1 à 4 : c'est là qu'un
lecteur futur cherchera pourquoi un `url()` passe dans un `@font-face` et nulle part ailleurs.

## Client — le lecteur

1. **Deux politiques de filtrage au lieu d'une** (`sanitizePolicy.ts`). La lecture autorise
   `style` (DOMPurify garde déjà `class` et `id`) ; la rédaction (`SquireEditor`) garde exactement
   sa politique actuelle, `style` compris. DOMPurify reçoit `FORCE_BODY: true` côté lecture : sans lui, un
   `<style>` placé en tête d'un fragment est rangé dans le `<head>` et perdu.
2. **La CSP de l'iframe** (`renderBodyDocument`), en `<meta http-equiv>` :
   `default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src 'none'` sans
   consentement, `img-src data: https: http:; font-src https: http:` avec — le même
   `showImages` que `MessageReader` calcule déjà (bouton, réglage, expéditeur approuvé, contact).
   Les images intégrées (`cid:`) sont des URI `data:` et passent toujours. Le plan vérifie
   qu'aucun rendu actuel n'en souffre (images révélées, fonds restaurés, images intégrées).
3. **Le mode sombre**, trois cas :

   | Thème du webmail | `declaresDarkScheme` | Rendu |
   |---|---|---|
   | Sombre | vrai, **et** un de ses `<style>` porte une condition `prefers-color-scheme: dark` | **le mode sombre du mail** : ni `darkenColours`, ni atténuation des images ; ses blocs `prefers-color-scheme: dark` s'appliquent |
   | Sombre | faux, ou aucun bloc sombre dans ses `<style>` | `darkenColours`, comme aujourd'hui, **étendu aux feuilles de style** |
   | Clair | — | la version claire : ses blocs `prefers-color-scheme: dark` ne s'appliquent pas |

   **La déclaration seule ne suffit pas** (`hasDarkDesign`, dans `messageStyles.ts`) : bien des
   modèles portent le `<meta name="color-scheme" content="light dark">` par défaut sans dessiner
   aucun mode sombre, et leur texte passait au clair sur leurs propres fonds blancs. Seul le texte
   des `<style>` compte : une lettre d'information qui cite la requête dans son contenu ne dessine
   rien. Un expéditeur dont le mode sombre ne tient qu'en retouches en ligne, sans bloc `@media`,
   reçoit notre recoloration.

   **C'est nous qui tranchons la condition, pas le navigateur** : avant le filtrage, les blocs
   `@media (prefers-color-scheme: dark)` sont rendus inconditionnels quand le premier cas
   s'applique, et retirés sinon ; les blocs `light`, l'inverse. Laisser l'iframe l'évaluer
   dépendrait de la façon dont Blink propage le schéma de couleurs de la page à l'iframe — une
   règle qu'on ne pourrait vérifier qu'en mesurant et qui pourrait changer. Le document de l'iframe
   prend `color-scheme: dark` et le fond sombre de la table actuelle dans le premier cas, pour les
   zones que le mail ne peint pas. Le bouton « couleurs de l'expéditeur » ramène la version claire,
   comme aujourd'hui.
4. **`darkenColours` recolore les feuilles de style** dans le deuxième cas : chaque règle, y compris
   dans un `@media`, voit ses propriétés de couleur passer par `toDarkColour` avec les rôles
   actuels (texte, fond). Lecture et réécriture par le CSSOM, jamais par expression régulière sur
   le texte.
5. **Un `<style>` du mail peut surcharger le cadre posé par `renderBodyDocument`** (marge du
   `body`, fond de `html`). C'est l'intention de l'expéditeur et c'est accepté ; la page de test
   mesure ce qu'il en coûte à l'alignement avec l'en-tête.

## Conséquences à connaître

- **Une police distante ne déclenche pas le bandeau « images bloquées »** : elle n'est pas comptée
  dans `blockedImageCount`. Un mail sans aucune image distante mais avec une police distante garde
  sa police de secours, sauf si « Toujours afficher les images » est actif ou si l'expéditeur est
  approuvé.
- **Les classes d'un mail cité arrivent déjà dans l'éditeur aujourd'hui** (`PrepareQuote` les
  garde, DOMPurify aussi) et pourraient y prendre un style du webmail. C'est antérieur à ce travail
  et indépendant de lui : consigné dans `docs/known-issues/` plutôt que traité ici.

## Vérification

- **Serveur (xUnit, `MailHtmlSanitizerTests`)** : un `<style>` du `<head>` survit dans le corps ;
  `!important` survit ; une règle `@media` survit avec son contenu filtré ; `position`, `z-index`,
  `float` sont retirés d'une règle ; `@import`, `@keyframes`, `@namespace` disparaissent ; un `url()`
  hors `@font-face` disparaît avec sa déclaration ; un `@font-face` `https` survit, un `javascript:`
  ou `data:` la fait disparaître ; `a > b`, `.sm\:px-4` et `.w-1\/2` survivent ; un `</style>` glissé
  dans une chaîne ne fait pas sortir du `<style>` ; un antislash imprévu (non suivi d'une ponctuation
  sûre) retire toute la feuille ; `class` et `id` survivent ; `DeclaresDarkScheme` suit les trois
  sources (deux `<meta>`, une déclaration CSS) et vaut faux sans elles.
- **Client (Vitest)** : la politique de lecture garde `<style>`, `class`, `id` et le `<style>` de
  tête ; celle de la rédaction retire toujours `<style>` ; la CSP change avec `showImages` ; les trois cas du mode
  sombre (blocs rendus inconditionnels, retirés, `darkenColours` appliqué aux règles).
- **Page de test Blink** : les deux mails réels à 360 et 1024 px de large, avant et après, en thème
  clair et sombre — largeur du contenu (plus de défilement horizontal à 360), logo sombre de la
  Loterie en thème sombre. **Les deux `.eml` contiennent des données personnelles** (prénom,
  numéros joués) : ils restent hors du dépôt. La page commitée utilise un mail synthétique qui
  reproduit leur structure (`<style>` de tête, `@media` à `!important`, bloc sombre, largeurs
  fixes).
