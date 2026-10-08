# Image du jour — conception

Date : 2026-10-08 · Branche : `daily-image` ·
Maquettes : https://claude.ai/artifact/WiiRPaCXMqEQPGzXfVRHUH

## Le but

Afficher l'image du jour de Bing en fond, pour le plaisir : sur la page de connexion et dans le
panneau de lecture quand aucun message n'est ouvert.

Critères de réussite :

- un réglage administrateur active la fonction pour l'instance ; désactivé par défaut, le serveur ne
  contacte jamais Bing ;
- activée, la page de connexion montre l'image du jour (planche « Connexion ») avec son titre et son
  crédit ;
- dans Apparence, chaque utilisateur choisit l'affichage du panneau vide : **None · Full bleed ·
  Postcard · Watermark** (planches A, B, C), *None* par défaut, choix enregistré sur le compte ;
- le navigateur ne charge que des images servies par Scotty : la CSP (`img-src 'self' data:`) ne
  change pas, et Bing ne voit pas les adresses des visiteurs ;
- Bing en panne ou injoignable : le fond actuel reste, sans message d'erreur.

Hors champ : autres sources (Wikimedia, NASA, dossier local), image dans le reste de l'application,
archive des jours passés.

## Les décisions

### 1. Bing seul, servi par l'API

L'API interroge `https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=<marché>`, puis
télécharge l'image 1920×1080 et la sert elle-même. Bing n'a pas d'API officielle : si le format
change, la fonction s'éteint proprement (§ Pannes), rien d'autre ne casse.

### 2. Le marché suit la langue de l'interface

`en` → `en-US`, `fr` → `fr-FR` : la légende est dans la langue du lecteur. Une entrée de cache par
marché, donc deux au plus.

### 3. Deux réglages

- **Instance** — `app.dailyImage`, `true`/`false`, défaut `false`, dans le registre `AppSettings`.
  Écran : Administration › Application, un interrupteur « Image of the day (Bing) » et une phrase
  qui dit que le serveur contactera Bing une fois par jour.
- **Compte** — `ui.dailyImage`, `none`/`fullBleed`/`postcard`/`watermark`, défaut `none`, dans le
  registre `UserPreferences`. Section « Image of the day » d'Apparence, rangée `.seg` comme Thème et
  Langue, sous-titre « Shown in the reading pane when no message is open ». La section n'apparaît que
  si `app.dailyImage` vaut `true` ; le choix stocké est conservé si l'admin coupe la fonction.

Libellés FR : « Image du jour » · Aucune · Plein cadre · Carte postale · Filigrane · « Affichée dans
le panneau de lecture quand aucun message n'est ouvert ».

## Le serveur

### `DailyImageService` (singleton)

- `HttpClient` typé, `AllowAutoRedirect = false`, délai 5 s.
- Métadonnées : `images[0].urlbase` doit correspondre à `^/th\?id=[A-Za-z0-9._-]+$`, sinon la
  réponse est refusée ; l'URL de l'image est reconstruite sur l'hôte fixe
  `https://www.bing.com` + `urlbase` + `_1920x1080.jpg`. Aucune URL reçue n'est suivie telle quelle.
- Image : `Content-Type` `image/jpeg` exigé, lecture plafonnée à 5 Mo (au-delà, refus).
- Cache mémoire par marché : `{ version, title, copyright, bytes, expiresAt }`. `version` = `hsh` de
  Bing ; `expiresAt` = `fullstartdate` + 24 h (UTC), le moment où Bing change d'image.
- Un seul téléchargement à la fois par marché (`SemaphoreSlim`) : cent visiteurs à minuit, un appel.
- Échec : on garde l'entrée précédente si elle existe, et on ne réessaie pas avant 15 min.

### Routes (contrôleur dédié `DailyImageController`, anonymes)

- `GET /api/appsettings/daily-image?lang=en|fr` → `{ version, title, copyright }`, `Cache-Control:
  no-cache` ; 404 si `app.dailyImage` vaut `false` ou si aucune image n'est disponible.
- `GET /api/appsettings/daily-image/{version}?lang=…` → le JPEG, `public, max-age=31536000,
  immutable` (l'URL porte la version, comme le logo) ; 404 si la version n'est pas celle en cache.

Même préfixe d'adresse que les réglages ; un contrôleur à part laisse `AppSettingsController` et ses tests
intacts.

Anonymes parce que la page de connexion en a besoin. Elles ne déclenchent jamais un appel à Bing
quand la fonction est coupée, et le cache borne le reste.

## L'interface

### `useDailyImage()`

Requête TanStack sur `/daily-image`, activée seulement si `app.dailyImage` vaut `true`, langue de
l'interface en paramètre, `staleTime` 1 h. Renvoie `{ src, title, copyright }` ou `null`. L'image est
préchargée (`new Image()`) avant d'être montrée ; elle apparaît en fondu (200 ms, aucun fondu si
`prefers-reduced-motion`).

### Page de connexion

`.page-center` garde `login-background.webp` ; une image du jour chargée le remplace via une variable
CSS (`--login-bg`). Voile et carte inchangés. Crédit en bas à droite : titre en gras, copyright
dessous, pastille sombre (`--scrim` renforcé), texte blanc.

### Panneau vide (`MessageReader`, `uid === null`)

Composant `DailyImagePane`, choisi par `ui.dailyImage` :

| Variante | Rendu (planches A, B, C) |
|---|---|
| `fullBleed` | photo en `object-fit: cover` ; « Select a message » dans une pastille claire centrée ; crédit dans une pastille sombre en bas à gauche |
| `postcard` | fond `--surface-sunken` ; texte au-dessus ; carte `--surface`, bordure `--border`, photo 16:9 arrondie, max 560 px de large et 60 % de la hauteur, titre + crédit sous la photo |
| `watermark` | photo en cover sous un voile `color-mix(var(--surface) 60%, transparent)` ; texte au centre ; crédit discret en bas à droite |

Couleurs uniquement par tokens : en sombre, le voile et les pastilles suivent la palette. Rien en
plus si l'image manque : c'est le `.mail-empty` actuel. Volet `none` (pas de panneau de lecture) et
téléphone : pas de panneau vide, donc rien à faire. Volet `bottom` : mêmes variantes, la carte
postale se règle sur la hauteur.

## Tests

- **Service** (faux `HttpMessageHandler`) : réponse normale ; `urlbase` hors motif ; redirection ;
  mauvais `Content-Type` ; image de plus de 5 Mo ; cache servi jusqu'à `expiresAt` puis rafraîchi ;
  appels simultanés → un seul téléchargement ; échec → ancienne entrée gardée et pas de nouvel essai
  avant 15 min ; marchés séparés.
- **Contrôleur** : anonyme ; 404 fonction coupée (aucun appel au service) ; 404 version périmée ;
  en-têtes de cache.
- **Registres** : `app.dailyImage` et `ui.dailyImage`, valeurs refusées.
- **Front** : section Apparence masquée/montrée selon l'admin ; les trois variantes et `none` ;
  image en échec → texte seul ; page de connexion avec et sans image ; interrupteur admin.
