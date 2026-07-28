# Webmail installable (PWA) — design

Rendre le webmail installable comme application : Edge et Chrome affichent alors l'icône
d'installation dans la barre d'adresse, et l'application s'ouvre dans une fenêtre sans onglets.
S'y ajoutent deux comportements que seule une application installée peut offrir — les raccourcis
du menu contextuel de l'icône, et l'enregistrement comme gestionnaire des liens `mailto:`.

Le nom affiché est piloté depuis Administration, par un nouvel onglet **Application** portant un
interrupteur et deux champs.

## Ce que la mesure a tranché

Deux hypothèses ont été vérifiées dans un vrai Chromium avant d'écrire ce document, sur un banc
d'essai servi en local (l'origine `127.0.0.1` est traitée comme sûre, donc les critères
d'installabilité s'y appliquent réellement) :

| Hypothèse | Résultat |
|---|---|
| Un manifest généré en `blob:` et injecté à l'exécution satisfait les critères | Oui — `beforeinstallprompt` déclenché |
| Un service worker est requis pour l'installabilité | **Non** — l'événement est déclenché sans |

La seconde mesure sort le service worker du périmètre. C'est le gain principal : aucun cache
côté client, donc aucune version périmée servie depuis le disque, et rien à purger le jour d'un
déploiement fautif. Ne pas en réintroduire un « pour faire propre » : il ne rendrait aucun
service ici et rouvrirait cette classe de bugs. Un webmail hors ligne n'affiche rien de toute
façon, ses données venant toutes de l'API.

## Pourquoi le manifest est généré côté client

La spécification exige que `start_url` soit de même origine que le manifest. Un manifest servi
par `api.mail.weesky.net` ne peut donc pas désigner `account.mail.weesky.net` : le navigateur le
rejette. L'API est hors-jeu.

Et le frontend est un tas de fichiers statiques déposés sur nginx — le déploiement pousse `dist/`
et rien d'autre — donc aucune route serveur ne peut composer un manifest à la volée sans une
modification de configuration hors dépôt.

Reste une seule voie : l'application lit les réglages, construit le JSON en mémoire, et pose
`<link rel="manifest" href="blob:…">`. Un `blob:` hérite de l'origine du document, la
vérification d'origine passe donc. C'est ce que la mesure ci-dessus confirme.

**Injection à la demande, pas de manifest statique de repli.** Aucun `<link rel="manifest">` dans
`index.html` : le lien n'existe que si les réglages disent « activé ». L'alternative — un manifest
statique posé d'emblée puis surchargé — donnait l'icône plus tôt, mais l'interrupteur en position
fermée devait alors retirer un lien déjà posé, et l'icône apparaissait puis disparaissait au
chargement, ce qui se lit comme un défaut d'affichage. Le prix payé est que l'icône arrive une
fraction de seconde après le chargement, et pas du tout si l'API ne répond pas.

**Toutes les URL du manifest sont absolues** — `id`, `start_url`, `scope`, `icons[].src`. Un
`blob:` a un chemin opaque ; y résoudre une référence relative n'est pas fiable. Elles sont
construites depuis `window.location.origin`, ce qui rend au passage le manifest correct sur
`account` comme sur `account-dev` sans réglage de build.

## Icônes

`public/icon-192.png` et `public/icon-512.png`. Le dossier `public/` est nouveau dans ce projet :
Vite y copie les fichiers verbatim, sans empreinte dans le nom. C'est la condition — un manifest
ne peut pas désigner `icon-512-B6BKEni0.png`, dont le nom change à chaque build.

Les deux sont régénérées depuis le logo source de 3808 px récupéré de l'historique git
(`git show 1ba476c^:src/frontend/src/assets/logo_circle.jpg`), jamais par agrandissement du 192
existant, et avec l'alpha prémultipliée pour que le bord transparent ne bave pas en sombre —
la même méthode que celle qui a produit les icônes déjà livrées.

Pas d'icône `maskable`. Elle sert les lanceurs Android, pas Edge desktop qui est la cible ; et
l'enso remplit tout le carré, donc une variante masquable demanderait un fond opaque à choisir
alors que le logo est transparent partout ailleurs. À reprendre le jour où le mobile compte.

## Contenu du manifest

- `id` et `start_url` : `<origin>/`. La route racine redirige déjà vers `/mail`, donc l'application
  installée ouvre la boîte, et un visiteur non connecté est renvoyé vers `/login` par `RequireAuth`
  comme partout ailleurs.
- `scope` : `<origin>/` — tout le site.
- `display` : `standalone`.
- `theme_color` / `background_color` : **valeurs statiques**, prises sur la palette `night` en
  mode clair, celle que reçoit un compte sans préférence. Un manifest ne porte qu'une couleur ;
  il ne peut pas suivre les huit palettes × deux modes. C'est une limite acceptée, pas un oubli.
- `shortcuts` : *New message* → `/mail/compose`, *Contacts* → `/contacts`. Sans icônes : elles
  sont facultatives, et le menu contextuel les rend lisiblement sans.
- `protocol_handlers` : `mailto` → `/mail/compose?mailto=%s`.

Le nom (`name`, `short_name`) vient des réglages d'instance, décrits plus bas.

## Frontend — découpage

**`src/lib/webAppManifest.ts`** — pur : `(settings, origin) → manifest | null`, `null` quand
l'application est désactivée. Toute la forme du manifest tient là, testable sans navigateur.

**`src/hooks/useWebAppManifest.ts`** — l'effet, et lui seul : lit `GET /api/AppSettings`, appelle
le constructeur, et pose ou retire le `<link>`. Il révoque l'URL d'objet précédente à chaque
changement et au démontage — sans quoi chaque passage laisserait un Blob vivant pour la durée du
document.

Les deux appels passent par `api.js` comme tout le reste — `getAppSettings()` et
`setAppSetting()` — plutôt que par un `fetch` local : c'est là que vivent l'origine de l'API,
`credentials: 'include'` et la traduction d'un échec en `ApiError`.

Il est monté une fois dans `App.tsx`, **au-dessus de `RequireAuth`** : c'est la page de login que
voit d'abord un nouvel utilisateur, et c'est là que l'installation doit être proposée. C'est aussi
la raison pour laquelle le `GET` est anonyme.

## Le client `mailto:`

**`src/modules/mail/compose/mailtoSeed.ts`** — pur : une URL `mailto:` (RFC 6068) devient un
`ComposeSeed`, la forme que le composeur sait déjà ouvrir. `action: 'editAsNew'`, dont l'en-tête
affiche « New message ».

Deux règles ne sont pas négociables, parce qu'un lien `mailto:` vient du monde extérieur et que
le système d'exploitation nous le passe tel quel :

- **le corps arrive en texte brut et est échappé en HTML**, jamais inséré tel quel. Le composeur
  édite du HTML ; y injecter une chaîne non échappée serait une injection à l'entrée du produit ;
- **les adresses passent par `isValidAddress`** avant d'entrer dans les champs, celles qui échouent
  sont écartées silencieusement.

Les autres en-têtes que `to`, `cc`, `bcc`, `subject` et `body` sont ignorés.

Le raccordement tient en une ligne dans `ComposeView` :
`const seed = state?.seed ?? mailtoSeedFrom(location.search)`. Le seed d'un `mailto:` porte donc
du contenu qui n'existe nulle part ailleurs, et ouvre le composeur à l'état « modifié » : en
sortir propose d'enregistrer un brouillon, exactement comme un transfert. C'est voulu.

## Backend — réglages d'instance

### Table `app_settings`

Dans la base *preferences*, clé/valeur, **sans `user_id`** : ce n'est pas une préférence de compte
mais un réglage de l'instance, et l'y rattacher voudrait dire que chaque utilisateur nomme
l'application à sa façon. La table ne dépendant de rien, aucune arête de relation n'est à déclarer
dans `PreferencesDbContext` — le piège qui a coûté `ContactEmail → Contact` ne s'applique pas ici.

Le projet n'a pas de migrations EF : la création est manuelle, documentée dans
`docs/superpowers/webmail-app-settings-table.md` sur le modèle des tables précédentes, et à
appliquer sur `snoopy_webmail` et `snoopy_webmail_dev`.

### Registre

`Models/AppSettings.cs`, même forme que `UserPreferences` — `Effective` et `IsValid`. Les défauts
vivent de ce côté-ci et le `GET` répond toujours toutes les clés connues, déjà remplies : le client
n'en garde donc aucune copie et les deux ne peuvent pas diverger.

| Clé | Valeurs | Défaut |
|---|---|---|
| `app.installable` | `'true'` \| `'false'` | `'false'` |
| `app.name` | 1 à 60 caractères après élagage | `Snoopy mail` |
| `app.shortName` | 1 à 12 caractères après élagage | `Snoopy` |

Les deux noms sont élagués avant validation et avant stockage : une valeur réduite à des espaces
est refusée, et un espace de tête saisi par mégarde ne se retrouve pas sous l'icône.

Le défaut est **désactivé** : le déploiement ne change donc rien tant que l'administrateur n'est
pas allé activer, et le nom est choisi avant d'être visible de quiconque.

### `AppSettingsController`

- `GET` en `[AllowAnonymous]` — un nom d'application n'est pas un secret, et l'icône doit vivre
  sur `/login`, où il n'y a pas de session.
- `PUT` sous `[Authorize(Policy = AdminRequirement.PolicyName)]`, comme `AdminController`. Une
  clé inconnue ou une valeur que le registre refuse est rejetée en 400, la table n'ayant aucun
  moyen de vérifier ni l'une ni l'autre.

## Onglet Administration

`ApplicationTab.tsx` — quatrième onglet après *Virtual domains*, en TypeScript comme tout code
neuf. Il porte l'interrupteur, les deux champs et un bouton d'enregistrement, dans la langue des
onglets existants : lignes `.field-h`, `.toggle-switch` pour le booléen, un seul `.btn-primary`.
Chaque champ reçoit sa paire `htmlFor`/`id` — `.field-h` place le libellé *à côté* du contrôle,
donc sans elle le contrôle n'a pas de nom accessible. Une entrée est ajoutée à `ADMIN_HELP`.

Les champs sont désactivés quand l'interrupteur est fermé : nommer une application qu'on
n'expose pas n'a pas de sens, et les griser le dit sans retirer les valeurs de l'écran.

## Tests

- `webAppManifest.test.ts` — désactivé donne `null` ; URL absolues ; nom repris des réglages.
- `useWebAppManifest.test.tsx` — le `<link>` est posé quand c'est activé, retiré quand ça ne
  l'est pas, et l'URL d'objet précédente est révoquée. jsdom ne juge pas l'installabilité : ce
  test porte sur le DOM produit, pas sur la décision du navigateur.
- `mailtoSeed.test.ts` — analyse des cinq en-têtes retenus, échappement du corps, rejet des
  adresses invalides, URL vide.
- `ApplicationTab.test.tsx` — rendu, enregistrement, refus serveur laissant l'écran sur l'état
  du serveur.
- Côté microservice : le registre (défauts, validation), le store, le contrôleur y compris le
  403 d'un non-administrateur sur le `PUT` et le 200 anonyme sur le `GET`.

## Réserves

**Couper l'interrupteur ne désinstalle personne.** Une application déjà installée le reste ; elle
cesse seulement d'être proposée aux autres. Aucun navigateur n'offre de désinstallation à
distance, et il ne faut pas laisser croire l'inverse dans le libellé de l'onglet.

**Mesuré sur Chrome, pas sur Edge.** Même moteur et mêmes critères d'installabilité, mais la
vérification sur Edge reste à faire une fois la branche déployée sur `account-dev`.

**Le `protocol_handlers` demande un accord de l'utilisateur.** Edge propose l'enregistrement
comme client `mailto:` après l'installation ; il ne se fait pas d'office, et l'utilisateur peut
le refuser sans que l'installation en pâtisse.
