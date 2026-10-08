# Image Docker, compose et workflow GHCR — conception

Date : 2026-10-08 · Branche : `docker-image`

## Le but

Troisième et dernier lot de l'image Docker publique du webmail (plateforme `generic`) :

1. configuration lue au démarrage — livré (#107) ;
2. migrations de schéma automatiques — livré (#108) ;
3. **image, `docker-compose.yml` et workflow GHCR** — ce document.

Critères de réussite :

- un inconnu passe d'un serveur vide à « je lis mes mails » avec `docker/docker-compose.yml`, un
  fichier `.env` et le proxy HTTPS qu'il a déjà ;
- une mise à jour se fait par `docker compose pull && docker compose up -d`, sans SQL à la main ;
- le propriétaire teste une image sur son serveur avant de la publier, et publie exactement
  l'image testée ;
- **le serveur du propriétaire, déployé par `deploy.yml`, ne voit aucune différence** : aucun des
  comportements ci-dessous ne s'active sans un réglage que seule l'image pose.

Décisions des lots précédents, reprises telles quelles : un seul conteneur, celui du webmail ; la
base (MariaDB ou MySQL) est toujours fournie par l'installateur ; pas de wizard ; l'image pose
`Logs__Output=console` et `STATE_DIRECTORY` ; elle migre au démarrage grâce à
`ConnectionStrings__WebmailSchema`.

Hors champ :

- le HTTPS : l'installateur met son propre proxy devant le conteneur (le compose ne contient pas de
  Caddy) ;
- retirer le préfixe `/api` des routes quand l'API a son propre sous-domaine : la question touche
  le déploiement actuel, pas l'image ; elle sera traitée à part ;
- le test de démarrage de l'image `arm64` : elle est construite à chaque fois, mais seule l'image
  `amd64` est démarrée en CI ;
- la plateforme `weesky`.

## L'état de départ

- Les pages du front sont servies par un serveur web (Apache dans le guide), l'API par Kestrel
  derrière un proxy. Le front appelle `${apiBase}/api/...` (`src/api.ts`) ; l'API répond aussi à
  `/dav/...`, `/.well-known/caldav|carddav` (synchronisation des téléphones) et `/health`.
- `npm run build -- --mode container` produit un `dist` sans `config.js` (lot 1).
- `UseSecurityHeaders` envoie `Content-Security-Policy: default-src 'none'` à toute réponse hors
  `/swagger` : une page servie avec cette règle resterait blanche.
- `AddFrontendCors` refuse de démarrer sans `Cors__AllowedOrigins__0`.
- Le cookie de session est `Secure`, `HttpOnly`, `SameSite=Strict` (`SessionCookies`).
- `/api/Version` renvoie `ProductVersion(Version, Commit)`, lu dans la version informationnelle de
  l'assembly ; le front lit son commit dans `GITHUB_SHA` ou `git rev-parse`.
- Le trousseau Data Protection et la clé de session vivent dans le dossier d'état. Le trousseau
  chiffre le cookie des identifiants de messagerie, les secrets OAuth des fournisseurs externes et
  le mot de passe du compte de service de l'agenda.
- `Program.cs` traite déjà une commande, `migrate`, avant de construire l'application.

## Les décisions

### 1. L'image

Construite en trois étapes par `docker/Dockerfile`, contexte à la racine du dépôt :

1. Node 24 construit le front : `npm ci`, puis `npm run build -- --mode container` ;
2. le SDK .NET 10 publie l'API pour l'architecture cible (`dotnet publish -a $TARGETARCH`,
   dépendante du runtime, sans symboles ni ressources satellites). Ces deux étapes tournent sur
   l'architecture de la machine de build (`--platform=$BUILDPLATFORM`) : l'image `arm64` est
   compilée sans émulation ;
3. l'image finale part de `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`, épinglée
   par son empreinte (digest) : Ubuntu réduit, sans shell ni gestionnaire de paquets, utilisateur
   non administrateur (`app`, uid 1654). La variante `-extra` embarque les fuseaux horaires et
   l'ICU, dont l'agenda a besoin.

Arguments de build : `GIT_COMMIT` (commit court, passé au front par `GITHUB_SHA` et à l'API par
`-p:SourceRevisionId`), `RELEASE_BUILD` (`true` seulement quand le commit est sur `master` ; sinon
les versions web et serveur sont suffixées `-dev`, comme aujourd'hui), `IMAGE_VERSION` (contenu de
`docker/VERSION`).

Réglages posés par l'image :

| Variable | Valeur |
|---|---|
| `ASPNETCORE_HTTP_PORTS` | `8080` |
| `Logs__Output` | `console` |
| `STATE_DIRECTORY` | `/var/lib/scotty`, créé dans l'image et appartenant à `app` |
| `Platform` | `generic` |
| `Frontend__Path` | `/app/frontend`, le `dist` de l'étape 1 |
| `SCOTTY_IMAGE_VERSION` | `IMAGE_VERSION` |

Étiquettes OCI : `source` (le dépôt, ce qui rattache le paquet GHCR au dépôt), `revision`
(commit complet), `version`, `licenses` (`AGPL-3.0`).

`HEALTHCHECK` : `scotty.microservice healthcheck` (décision 6), toutes les 30 s, délai 5 s.
`VOLUME /var/lib/scotty`, `EXPOSE 8080`, `USER app`.

Le compose (`docker/docker-compose.yml`) déclare un seul service : l'image, le port `8080` publié
sur `127.0.0.1` par défaut, le volume nommé `scotty-state` sur `/var/lib/scotty`,
`restart: unless-stopped` (la base pas encore joignable au démarrage : l'API s'arrête, Docker la
relance), `env_file: .env`, `read_only: true` avec un `tmpfs` sur `/tmp` (tampons des envois de
pièces jointes), `cap_drop: [ALL]` et `security_opt: [no-new-privileges:true]`.

`docker/.env.example` liste ce que l'installateur fournit : les deux chaînes de connexion, les
serveurs IMAP, SMTP et ManageSieve, `Generic__Administrators`,
`ForwardedHeaders__KnownNetworks__0`, et en commentaire `Frontend__ApiBase`,
`Cors__AllowedOrigins__0`, `Dav__PublicUrl`. L'image ne fixe aucune plage de proxy par défaut :
« tous les réseaux privés » laisserait un appareil du réseau local mentir sur son adresse et
contourner le limiteur de connexions. Sans plage, l'API refuse de démarrer avec le message actuel.

### 2. L'API sert les pages

Un fichier à part, `Configuration/FrontendHosting.cs`, que le reste de l'API ignore. Il n'est
actif que si `Frontend:Path` est réglé ; réglé vers un dossier sans `index.html`, l'API refuse de
démarrer en citant le chemin.

| Requête | Réponse |
|---|---|
| `/`, `/mail/inbox`, toute adresse sans fichier hors des préfixes ci-dessous | `index.html`, `Cache-Control: no-cache` |
| `/assets/<fichier existant>` | le fichier, `Cache-Control: public, max-age=31536000, immutable` |
| `/assets/<absent>` | 404 |
| un autre fichier existant (`icon-192.png`, `third-party-licenses.html`) | le fichier, `no-cache` |
| `/config.js` | décision 3 |
| `/api/…`, `/dav/…`, `/.well-known/…`, `/health` | l'API, comme aujourd'hui ; jamais `index.html` |

Les fichiers sont servis par `UseStaticFiles` (types, `ETag`, requêtes conditionnelles). La
compression est laissée au proxy (le guide l'active dans chaque exemple).

En-têtes de sécurité : les réponses du front gardent `X-Content-Type-Options`, `X-Frame-Options`
et `Referrer-Policy`, sans `Content-Security-Policy` — le même comportement que le guide Apache
aujourd'hui. `default-src 'none'` reste sur toute autre réponse.

### 3. `/config.js` produit par l'API

Servi depuis `Frontend:ApiBase` : `window.SCOTTY_CONFIG = {"apiBase":"…"}`, la valeur sérialisée
par `System.Text.Json` (aucune injection par une valeur contenant des guillemets ou `</script>`),
`Content-Type: text/javascript`, `Cache-Control: no-cache`.

| `Frontend:ApiBase` | Comportement |
|---|---|
| Absent ou vide | `apiBase: ""` : le front appelle l'API sur sa propre adresse |
| Origine `http://` ou `https://`, sans chemin (`https://api.monmail.net`, barre finale tolérée) | Utilisée, barre finale retirée |
| Toute autre valeur (sans schéma, autre schéma, avec un chemin) | Refus de démarrer, en citant la valeur |

Le chemin est refusé, contrairement au front : l'API ne sert que des routes à la racine, et la
séparation par adresse (décision 4) compare des noms d'hôte.

### 4. Deux montages, séparés par l'adresse demandée

**Montage « une adresse »** (`Frontend:ApiBase` vide) : `monmail.net` sert les pages et
`monmail.net/api/…` l'API. Rien n'est filtré.

**Montage « deux sous-domaines »** (`Frontend:ApiBase=https://api.monmail.net`) : l'API compare
l'hôte de la requête (`Host`, sans le port, sans tenir compte de la casse) à celui de
`Frontend:ApiBase`.

| Hôte demandé | Pages, `/config.js` | `/api/…`, `/dav/…`, `/.well-known/…` | `/health` |
|---|---|---|---|
| celui de `Frontend:ApiBase` | 404 | servis | servi |
| tout autre | servies | 404 | servi |

`/health` répond sur tout hôte : le contrôle de santé interne appelle `127.0.0.1`.

Conséquences documentées dans le guide : le proxy doit transmettre l'en-tête `Host` d'origine
(sinon tout répond 404 dès la page d'accueil) ; `Dav__PublicUrl` est l'adresse de l'API ; les deux
sous-domaines doivent partager le même domaine, car le cookie `SameSite=Strict` n'est pas envoyé
entre deux sites (symptôme : la connexion semble réussir, puis chaque appel est refusé).

### 5. CORS

| `Frontend:Path` | `Frontend:ApiBase` | `Cors:AllowedOrigins` |
|---|---|---|
| absent (installation classique) | — | obligatoire, comme aujourd'hui |
| réglé | vide | facultatif |
| réglé | rempli | obligatoire ; le message de refus cite les deux réglages |

### 6. La commande `healthcheck`

`scotty.microservice healthcheck`, traitée en tête de `Program.cs` sur le modèle de `migrate` :
`GET http://127.0.0.1:<port>/health`, le port étant le premier de `ASPNETCORE_HTTP_PORTS` (8080 à
défaut). Code de sortie 0 si la réponse est 200, 1 sinon (erreur, refus de connexion ou délai de
5 s dépassé), avec une ligne sur la sortie d'erreur qui dit pourquoi.

Au démarrage, l'API écrit une ligne `Version …, commit …, image …` (`none` pour une valeur absente) : `docker compose logs` dit quelle image tourne, et le test de démarrage vérifie par elle que le numéro et le commit sont bien arrivés dans l'image.

### 7. Le numéro de l'image

- `docker/VERSION` (départ : `1.0.0`) numérote l'image ; `src/frontend/VERSION` et
  `src/scotty.microservice/VERSION` gardent leur rôle. L'image est un instantané publié des deux.
- `ProductVersion` gagne un champ `Image`, lu dans `SCOTTY_IMAGE_VERSION` ; absent ou vide, il est
  `null` et le champ disparaît de la réponse de `/api/Version` (les champs `null` sont omis).
- La page À propos ajoute la ligne « Docker image 1.0.0 » seulement quand le champ est présent
  (clé de traduction en anglais et en français).
- L'image affiche `1.0.0`, jamais `-rc.N` : le suffixe n'est qu'une étiquette sur GHCR, et la
  release promeut l'image candidate telle quelle.

### 8. Le workflow `.github/workflows/image.yml`

Image : `ghcr.io/darthmaul0181/scotty-webmail`. `deploy.yml` ne change que d'une entrée : `image-v*` rejoint ses `tags-ignore`.

```
① push d'une branche qui modifie docker/, .dockerignore ou image.yml
     → build amd64 + arm64, test de démarrage (④) ; rien n'est publié

② « Run workflow », mode candidate, sur n'importe quelle branche
     → build, test (④), publication sous <docker/VERSION>-rc.<N>
       N = 1 + le plus grand N déjà publié pour ce numéro

③ « Run workflow », mode release, entrée : l'étiquette candidate (ex. 1.0.0-rc.2)
     → contrôles, puis étiquettes 1.0.0, 1.0, 1, latest ajoutées à la même image
       (docker buildx imagetools create, sans reconstruction) ; tag Git image-v1.0.0
```

Contrôles de ③, chacun refusant la release avec un message qui dit pourquoi :

- l'étiquette candidate existe sur GHCR ;
- son commit (étiquette OCI `revision`) est un ancêtre de `origin/master` : rien de non fusionné
  n'est publié ;
- `docker/VERSION` à ce commit vaut le numéro de l'étiquette : le numéro publié est celui du
  fichier ;
- le tag `image-v<numéro>` n'existe pas : un numéro ne désigne jamais deux images.

④ Test de démarrage, sur l'image `amd64`, avant toute publication : une MariaDB 11.4 jetable,
`install/install.sql` joué avec ses trois valeurs remplacées, puis le conteneur démarré avec le
`read_only` et le `tmpfs` du compose. Attendu :

- la base est migrée (`schema_migrations` contient `0001_initial.sql`) ;
- l'état du conteneur passe à `healthy` en moins de 60 s ;
- `/` renvoie `index.html`, `/config.js` renvoie `apiBase: ""`, `/api/Version` renvoie 401.

Le conteneur ne démarre pas, ou un contrôle échoue : le job échoue, rien n'est publié, et les logs
du conteneur sont imprimés.

Permissions : `contents: read` partout, `packages: write` pour ② et ③, `contents: write` pour le
tag de ③. Les modes ② et ③ partagent un groupe de concurrence : deux candidates lancées ensemble
ne prennent pas le même N. Cache de build GitHub Actions (`type=gha`).

`.github/dependabot.yml` surveille l'image de base de `docker/Dockerfile` (chaque semaine) :
Microsoft publie un correctif, une PR arrive ; le propriétaire fusionne, construit une candidate,
teste et publie un numéro correctif. Aucune image publiée ne change sans sa décision.

### 9. La documentation

`docker/README.md`, en anglais, écrit pour un inconnu, sans mention des serveurs du propriétaire :

1. prérequis : Docker avec le plugin compose, les bases déjà listées par le guide classique (MariaDB 10.5 à 11.x, MySQL 8.4), un nom de domaine
   et un proxy HTTPS ; création de la base et des deux comptes avec `install/install.sql` ;
2. `.env`, ligne par ligne, depuis `.env.example` ;
3. le proxy : exemples Caddy, Traefik et nginx pour les deux montages, chacun transmettant `Host`,
   `X-Forwarded-For`, `X-Forwarded-Proto` et compressant les réponses ; la plage
   `KnownNetworks` à choisir selon que le proxy est sur l'hôte ou dans un réseau Docker ;
4. démarrer, puis vérifier : `docker compose ps` affiche `healthy`, `docker compose logs` ;
5. mettre à jour : `docker compose pull && docker compose up -d`, les migrations se font seules ;
6. sauvegarder : la base et le volume `scotty-state`, avec ce que coûte sa perte (tout le monde
   déconnecté, secrets OAuth et mot de passe du compte de service de l'agenda à ressaisir) ;
7. quand ça ne démarre pas : le tableau du guide classique, plus les cas propres à Docker — `Host`
   non transmis (tout en 404), deux domaines différents (la connexion ne tient pas), volume
   oublié (déconnexion à chaque mise à jour), dossier d'état monté sans les droits de l'uid 1654,
   `Frontend:ApiBase` refusé, base injoignable (redémarrages en boucle dans `docker compose ps`).

Le `README.md` racine et `install/README.md` renvoient vers ce guide.
`docs/operations/docker-image-release.md` : la marche à suivre du propriétaire, en français
(candidate, test sur son serveur, release ; rendre le paquet GHCR public après la première
publication, car GHCR crée tout nouveau paquet en privé).

## Tests

API (xUnit, application en mémoire) :

- pages : chaque ligne du tableau de la décision 2, en-têtes de cache compris ; pas de
  `default-src 'none'` sur une page, présent sur une réponse d'API ; sans `Frontend:Path`, `/`
  répond 404 comme aujourd'hui ; `Frontend:Path` sans `index.html` refuse de démarrer ;
- `config.js` : vide, origine, barre finale retirée, valeur avec guillemets bien échappée ; sans
  schéma, autre schéma, avec un chemin refusés au démarrage ;
- séparation : chaque case du tableau de la décision 4, casse et port de `Host` ignorés ;
- CORS : les trois lignes de la décision 5 ;
- `healthcheck` : 0 sur 200, 1 sur 503, sur refus de connexion et sur délai dépassé ;
- version : `Image` présent avec la variable, champ absent du JSON sans elle.

Front (Vitest) : la ligne « Docker image » s'affiche seulement quand `image` est présent.

Image : le test de démarrage ④ du workflow, à chaque construction.

## Versions

`src/scotty.microservice/VERSION` passe à 1.4.0, `src/frontend/VERSION` à 2.3.0 (les tags
`api-v…` et `web-v…` de `deploy.yml`). `docker/VERSION` démarre à 1.0.0.
