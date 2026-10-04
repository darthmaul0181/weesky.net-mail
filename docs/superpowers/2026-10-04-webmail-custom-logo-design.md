# Logo personnalisé — conception

Date : 2026-10-04 · Branche : `custom-logo`

## Le but

Laisser l'administrateur remplacer le logo Scotty par celui de son organisation, pour toute
l'installation, partout où ce logo apparaît.

Critères de réussite :

- un réglage **Logo** dans Administration › Application permet d'envoyer une image et de revenir
  au logo Scotty ;
- le nouveau logo apparaît aux quatre endroits qui portent aujourd'hui le logo Scotty : barre du
  haut (masquée sur téléphone, où aucun logo ne s'affiche), onglet du navigateur avec sa pastille « non lu »,
  notifications de bureau, application installée (manifeste et icône iOS) ;
- le logo s'affiche tel qu'envoyé, sans découpe ronde, transparence conservée ;
- le serveur ne décode jamais une image venue de l'extérieur ;
- pas de clignotement Scotty → logo de l'organisation au chargement d'une page.

Hors champ : la mascotte de l'onglet À propos (elle présente le produit, pas l'organisation),
un logo par utilisateur.

## Les décisions

### 1. Un réglage d'instance, réservé aux administrateurs

Même nature que le nom de l'application : une décision de l'administrateur, valable pour tous.

### 2. Le navigateur de l'admin prépare les tailles

L'admin choisit un fichier (PNG, JPEG, WebP ou SVG). Son navigateur le décode, le centre dans un
carré transparent, mis à l'échelle pour y tenir entier, proportions conservées, et produit trois PNG :
**32, 192 et 512 px**. C'est la technique de `contactPhoto.ts`, sans le fond blanc.

Écartés :

- **redimensionnement serveur** (ImageSharp) : nouvelle dépendance à licence conditionnelle, et
  un serveur qui décode des images non fiables ;
- **simple URL externe** : dépendance à un site tiers, fuite des visites, et pastille « non lu »
  impossible (canvas « sali » par une image d'un autre domaine sans CORS).

Effet de bord voulu : un SVG porteur de script devient un PNG inerte avant de quitter le
navigateur.

### 3. Affiché tel qu'envoyé

Le `border-radius: 50%` de `.topbar-logo` disparaît. Le logo Scotty par défaut est déjà un disque
sur fond transparent : son rendu ne change pas.

## Stockage et API

### Table `app_logo`

| Colonne      | Type          | Rôle                                   |
|--------------|---------------|----------------------------------------|
| `size`       | `smallint`    | 32, 192 ou 512 — clé primaire          |
| `image`      | `mediumblob`  | le PNG                                 |
| `updated_at` | `datetime(3)` | UTC, posé par le code ; les millisecondes distinguent deux envois dans la même seconde |

Ajoutée à `install/install.sql` ; un script SQL à lancer à la main pour les serveurs existants.
Table vide = logo Scotty. `app_settings` n'est pas utilisée : `setting_value` est un
`varchar(255)`.

### Routes (contrôleur `AppSettings`)

- `PUT /api/AppSettings/logo` — administrateurs. Reçoit les trois PNG en une requête
  multipart. Pour chacun : signature PNG, dimensions lues dans l'en-tête IHDR égales à la taille
  annoncée, poids plafonné (32 px : 16 Ko ; 192 px : 256 Ko ; 512 px : 1 Mo). Taille de requête
  plafonnée sur la route. Les trois sont remplacés dans une même transaction, ou aucun.
  400 avec un message lisible sur tout refus.
- `DELETE /api/AppSettings/logo` — administrateurs. Vide la table.
- `GET /api/AppSettings/logo/{size}` — anonyme (page de login, manifeste). `image/png`,
  `X-Content-Type-Options: nosniff`, `Cache-Control: public, max-age=31536000, immutable`.
  404 pour une taille inconnue ou une table vide. L'en-tête CORS doit être présent pour les
  origines autorisées (voir § Onglet).

### Version et cache

`GET /api/AppSettings` renvoie une clé calculée de plus, `app.logo` : l'`updated_at` des images
(format compact, ex. `20261004T101500`), ou chaîne vide pour le logo par défaut. Elle n'est pas
inscriptible par le `PUT` générique. Le client construit `…/logo/{size}?v=<app.logo>` : une
nouvelle image change l'adresse, l'ancienne peut rester en cache un an.

## L'écran d'admin

Rubrique **Logo** dans Administration › Application, sous les champs du nom (libellés en
anglais) :

- **au repos** : le logo courant à la taille de la barre du haut, bouton **Change logo…** ;
  **Restore default** en plus quand un logo personnalisé est en place, avec confirmation ;
- **après choix d'un fichier** : rien n'est envoyé. Aperçu dans une barre du haut sur la palette
  courante et à 32 px comme dans l'onglet ; boutons **Save** et **Cancel**. Save produit les trois
  PNG, les envoie, toast de succès, invalidation de la requête `appSettings` ;
- **erreurs** :
  - fichier illisible : « This file isn't a readable image », rien n'est envoyé ;
  - plus grand côté < 512 px : accepté, avertissement que l'icône de l'application installée sera
    floue ;
  - refus du serveur : toast d'erreur via `apiErrorMessage`, comme les autres réglages de
    l'onglet.

## Le branchement

### Une seule source : `useAppLogo()`

Lit `app.logo` dans `useAppSettings()` et renvoie `{ src32, src192, src512 }` : les adresses de
l'API versionnées, ou les images Scotty livrées si la clé est vide. Aucun autre code ne connaît
ces adresses.

**Pas de clignotement** : la dernière valeur vue de `app.logo` est gardée en `localStorage`
(lecture et écriture sous try/catch) et sert tant que les réglages ne sont pas arrivés. L'icône
écrite en dur dans `index.html` reste Scotty jusqu'au démarrage du JavaScript : accepté.

### Les quatre usages

1. **Barre du haut** (`TopBar.tsx`) : `src192`.
2. **Onglet** (`favicon.ts`) : une fonction pour changer l'icône de base, qui remplace
   `originalHref`, vide le cache `drawn` et redessine la pastille si elle est voulue. L'image est
   chargée en `crossOrigin = 'anonymous'` : sans cela, un logo servi par l'API sur un autre domaine
   rend le canvas inexploitable et la pastille disparaît sans erreur.
3. **Notifications de bureau** (`channels.ts`, hors React) : l'adresse courante vient d'un petit
   module partagé, alimenté par le composant qui appelle `useAppLogo()`.
4. **Application installée** : `buildManifest` prend `src192` et `src512` au lieu de
   `/icon-192.png` et `/icon-512.png` ; le `<link rel="apple-touch-icon">` est mis à jour de même.

Les points 2 à 4 sont posés par un composant sans rendu monté dans `App.tsx`, hors du routeur,
à côté de `InstallManifest`, pour couvrir aussi la page de login.

## Tests

- **API (xUnit)** : refus d'un non-PNG, d'une dimension fausse, d'un poids excessif, d'un envoi
  incomplet ; remplacement tout-ou-rien ; écriture réservée aux administrateurs ; lecture anonyme
  avec les en-têtes attendus ; `app.logo` vide puis renseigné.
- **Client (Vitest)** : `useAppLogo` (défaut, personnalisé, valeur mémorisée) ; parcours de l'écran
  d'admin (choix, aperçu, Save, Cancel, Restore default, erreurs) ; changement d'icône de base de
  l'onglet ; icônes du manifeste.
- **Navigateur, hôte de dev** : pastille « non lu » sur le nouveau logo, notification de bureau,
  aperçu à 32 px, retour au logo Scotty.
