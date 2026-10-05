# Configuration lue au démarrage — conception

Date : 2026-10-05 · Branche : `runtime-config`

## Le but

Premier des trois lots qui mènent à une image Docker publique du webmail (plateforme `generic`) :

1. **configuration lue au démarrage** — ce document ;
2. migrations de schéma automatiques ;
3. image, `docker-compose.yml` et workflow GHCR.

Ce lot rend l'application configurable sans reconstruire quoi que ce soit, et sans rien imposer
que la plateforme d'un conteneur ne puisse fournir. Il sert aussi l'installation classique.

Critères de réussite :

- un même `dist` du front fonctionne avec n'importe quelle adresse d'API, donnée après le build ;
- l'API démarre sans `TokenConstants__Key`, et ses sessions survivent à un redémarrage ;
- l'API peut écrire ses logs sur la sortie standard, sans créer `/var/log/scotty.microservice` ;
- l'API accepte un proxy dont l'adresse change, désigné par une plage ;
- **le serveur du propriétaire, déployé par `deploy.yml`, ne voit aucune différence** : même
  workflow, même fichier de réglages, même comportement.

Hors champ : le Dockerfile, le compose, l'écriture de `config.js` par le conteneur, le guide
Docker, le workflow GHCR (lot 3) ; les migrations (lot 2). **`deploy.yml` n'est pas modifié.**

## L'état de départ

- L'adresse de l'API est gravée dans le bundle au build : `import.meta.env.VITE_API_BASE`, lu
  séparément dans `src/api.ts:43` et `src/lib/appLogo.ts:14`. Les routes sont toutes de la forme
  `${BASE}/api/...`.
- `vite.config.js` arrête le build quand `VITE_API_BASE` est vide, pour qu'un `.env.production`
  oublié ne produise pas un site pointé n'importe où.
- `deploy.yml` écrit `.env.production` et `.env.dev`, construit, puis **vide** le dossier du front
  sur le serveur avant d'y extraire `dist`.
- `TokenConstants:Key` est obligatoire et validée au démarrage (≥ 32 octets,
  `ApplicationServicesConfiguration.AddScottyOptions`). Elle est lue via `IOptions<TokenConstants>`
  par `AuthorizationExtension` (validation) et `TokenManager` (émission).
- `AddCredentialKeyRing` (`SecurityConfiguration`) calcule le dossier d'état depuis
  `STATE_DIRECTORY` (ou `ContentRoot/keys` en développement) et y range le trousseau Data
  Protection.
- `UseScottyLogging` écrit uniquement dans deux fichiers roulants sous
  `/var/log/scotty.microservice` (requêtes HTTP / reste), 31 jours, et crée ce dossier au
  démarrage.
- `AddProxyForwardedHeaders` n'accepte que des IP exactes (`ForwardedHeaders:KnownProxies`) et
  refuse de démarrer hors développement quand la liste est vide. `ForwardLimit = 1`.

## Les décisions

### 1. Le front lit son adresse d'API dans `/config.js`

`index.html` charge `<script src="/config.js"></script>` avant le module de l'application. Le
fichier contient une ligne :

```js
window.SCOTTY_CONFIG = { apiBase: "https://api.example.net" }
```

Un module unique, `src/lib/runtimeConfig.ts`, lit et valide cette valeur une fois. `api.ts` et
`appLogo.ts` en importent la base au lieu de lire chacun `import.meta.env`.

| `apiBase` | Base retenue |
|---|---|
| URL absolue `http://` ou `https://`, avec ou sans chemin | L'URL, barre finale retirée |
| `""` ou propriété absente | `""` — appels relatifs à l'origine de la page (`/api/...`) |
| Toute autre valeur (`api.example.net`, `ftp://…`) | Erreur « adresse de l'API invalide », qui cite la valeur |
| `window.SCOTTY_CONFIG` absent | Erreur « configuration absente : /config.js » |

Le cas « absent » couvre le serveur web qui répond `index.html` à `/config.js` (repli SPA) : le
navigateur ne peut pas l'exécuter, et l'objet n'est jamais défini.

Une erreur de configuration rend un écran d'erreur minimal à la place de l'application (texte
seul, sans dépendre des traductions ni de l'API), avec le message ci-dessus. Jamais d'écran blanc.

### 2. Qui produit `config.js`

| Contexte | `config.js` |
|---|---|
| Build classique (`production`, `dev` — ce que lance `deploy.yml`) | Émis dans `dist/` par un plugin Vite, depuis `VITE_API_BASE` |
| Build `--mode container` | Non émis ; le conteneur l'écrit au démarrage (lot 3) |
| `npm run dev` | Servi par le serveur de développement, depuis `VITE_API_BASE` du mode courant |
| Tests (Vitest) | `test-setup.ts` pose `window.SCOTTY_CONFIG` avec `https://api.example.test` |

Le garde-fou du build est conservé : hors mode `container`, un `VITE_API_BASE` vide arrête le
build comme aujourd'hui. Le mode `container` est la seule exemption, et il est explicite.

La valeur est sérialisée par `JSON.stringify` dans le fichier émis : aucune injection possible
par une adresse contenant des guillemets.

### 3. La clé de session est générée quand elle n'est pas fournie

| `TokenConstants__Key` | Comportement |
|---|---|
| Fournie, ≥ 32 octets | Utilisée, comme aujourd'hui |
| Fournie, < 32 octets | Refus de démarrer, message actuel |
| Vide ou absente | Lue dans `<dossier d'état>/session-signing.key` ; créée si le fichier n'existe pas |

- Génération : 64 octets aléatoires (`RandomNumberGenerator`), encodés en base64.
- Création en `FileMode.CreateNew`, droits `0600` sous Unix. Si le fichier apparaît entre-temps
  (deux démarrages simultanés), on relit celui qui existe au lieu d'en écrire un autre.
- Un fichier présent mais illisible, vide ou trop court : refus de démarrer, avec le chemin. Jamais
  de régénération par-dessus, qui déconnecterait tout le monde sans explication.
- Le journal note une fois « nouvelle clé de session générée dans … », jamais la valeur.
- Le calcul du dossier d'état sort de `AddCredentialKeyRing` dans une fonction partagée, utilisée
  par le trousseau et par la clé. Le refus actuel sans `STATE_DIRECTORY` hors développement est
  conservé.
- La clé est résolue une fois au démarrage, puis injectée dans `TokenConstants` avant la
  validation existante. `AuthorizationExtension` et `TokenManager` ne changent pas.

### 4. `Logs:Output` choisit fichiers ou console

| `Logs__Output` | Comportement |
|---|---|
| `file` ou absent | Inchangé : deux fichiers roulants sous `/var/log/scotty.microservice` |
| `console` | Une seule sortie standard, mêmes niveaux, mêmes filtres, même ligne de requête (avec l'adresse du client) ; aucun dossier de logs créé |
| Autre valeur | Refus de démarrer : « Logs:Output vaut "…" ; valeurs possibles : file, console » |

La comparaison ignore la casse. La détection automatique d'un conteneur
(`DOTNET_RUNNING_IN_CONTAINER`) est écartée : un réglage explicite, que l'image du lot 3 posera
elle-même, ne surprend personne qui construit sa propre image.

### 5. `ForwardedHeaders:KnownNetworks` accepte des plages

- Nouveau réglage, liste de plages CIDR (`172.16.0.0/12`, `fd00::/8`), versé dans
  `ForwardedHeadersOptions.KnownIPNetworks`. `KnownProxies` reste réservé aux IP exactes.
- Il faut au moins une entrée dans l'un des deux, sinon le refus de démarrer actuel s'applique ;
  son message cite les deux réglages.
- Refus de démarrer, en citant la valeur, pour une plage mal écrite et pour une plage de longueur
  zéro (`0.0.0.0/0`, `::/0`) : croire n'importe quelle adresse annulerait le limiteur de connexions.
- `ForwardLimit = 1` et le reste de la configuration ne changent pas.

## Documentation

`install/README.md` et `install/scotty.microservice.env` :

- étape 1.2 : l'adresse de l'API finit dans `dist/config.js`, qu'on peut corriger sur le serveur
  sans reconstruire ;
- étape 3.3 : `TokenConstants__Key` devient facultatif — vide, une clé est générée et gardée dans
  `/var/lib/scotty.microservice` ;
- étape 4 : `Cache-Control: no-cache` sur `/config.js` dans les exemples Apache et nginx ;
  `KnownNetworks` dans l'encadré « serveur web sur une autre machine » ;
- tableau « When the service won't start » : les nouveaux messages (clé illisible, `Logs:Output`,
  plage refusée).

`Logs__Output` n'est pas documenté dans le guide classique, qui garde les fichiers : il le sera
dans le guide Docker (lot 3).

## Tests

Front :

- `runtimeConfig` : URL valide, barre finale retirée, chemin conservé, valeur vide, propriété
  absente, valeur sans schéma, schéma non HTTP, objet absent ;
- l'écran d'erreur s'affiche pour une configuration absente et pour une adresse invalide ;
- le plugin Vite émet `config.js` avec la valeur sérialisée en build classique, rien en mode
  `container`, et le garde-fou refuse toujours une valeur vide hors `container` ;
- les tests existants qui comparent des URL passent sans modification de leurs attentes.

API :

- clé : fournie, trop courte, absente puis générée, relue au démarrage suivant, fichier corrompu
  refusé, fichier existant jamais réécrit ; droits `0600` vérifiés sous Linux seulement ;
- `Logs:Output` : `file`, `console`, casse différente, valeur inconnue ;
- `KnownNetworks` : plage IPv4 et IPv6 valides, plage mal écrite, `/0` refusé, ni proxy ni plage
  refusé, IP exacte seule toujours acceptée.

## Versions

`src/frontend/VERSION` et `src/scotty.microservice/VERSION` montent d'une version mineure, pour
que `deploy.yml` pose ses tags `web-v…` et `api-v…` sans avertissement.
