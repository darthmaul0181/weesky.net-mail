# Webmail — tranche 2d : multi-comptes

**Date** : 2026-07-29
**Dépend de** : 2a (connexion IMAP, session), 2c (rédaction, envoi, identités)
**Références** : `2026-07-18-webmail-shell-design.md` (§ 4 et § 11), `2026-07-18-webmail-mail-2a-design.md` (structure `MailAccountConnection`)

L'utilisateur connecte des comptes additionnels à sa session : des boîtes du serveur
maison (boîtes partagées) et des comptes hébergés sur des **domaines externes définis par
l'administrateur**. La bascule se fait dans le menu d'identité ; elle change le contexte
mail, jamais l'identité de session. L'utilisateur ne choisit jamais un serveur librement :
il choisit dans la liste des domaines autorisés.

## 1. Décisions structurantes

| Décision | Choix retenu |
|---|---|
| Découpage | Une seule spec pour la tranche ; le plan d'implémentation découpe en phases livrables |
| Chiffrement des credentials | AES-256-GCM, clé dérivée du mot de passe principal (jamais de clé serveur seule) |
| Transport du compte actif | Header `X-Account-Id` sur les routes sensibles au compte ; routes inchangées |
| Portée des données | Minimale : préférences, expéditeurs de confiance, contacts restent au niveau utilisateur |
| Notifications & poll | Compte actif seul (modèle Rainloop) |
| Sieve | **Par compte** — revirement assumé sur la spec shell (« pas de Sieve ») : un domaine externe peut déclarer un serveur ManageSieve optionnel ; les boîtes locales l'ont toujours (master user existant) |
| Identités d'un compte connecté | Multiples ; adresses saisies librement (pas de liste d'alias, le SMTP externe est l'autorité) ; identité par défaut = l'adresse du compte, verrouillée ; **pas de signature** (feature ultérieure possible) |
| Libellés UI | Onglet utilisateur « Connected accounts », onglet admin « External domains » |

Deux hypothèses de la spec shell sont infirmées par cette tranche, décisions utilisateur
du 2026-07-28 : les règles Sieve ne sont plus réservées au serveur maison (tableau
ci-dessus), et la navigation Settings devient dépendante du compte actif (§ 6).

## 2. Modèle de données

Base `snoopy_webmail`, DDL manuel sur le moule existant (deux bases, idempotent,
`utf8mb4_bin`, grants inchangés) : nouveau document
`docs/superpowers/webmail-connected-accounts-tables.md`, à jouer avant déploiement.

### `external_domains` — domaines autorisés par l'admin

| Colonne | Type | Note |
|---|---|---|
| `id` | CHAR(36) PK | GUID |
| `name` | VARCHAR(100) UNIQUE | nom d'affichage (« Gmail ») |
| `imap_host` | VARCHAR(255) | |
| `imap_port` | SMALLINT UNSIGNED | |
| `imap_security` | VARCHAR(16) | `None` \| `StartTls` \| `SslOnConnect` (miroir de l'enum existante) |
| `smtp_host` / `smtp_port` / `smtp_security` | idem | |
| `sieve_host` | VARCHAR(255) NULL | NULL = le domaine ne supporte pas Sieve |
| `sieve_port` | SMALLINT UNSIGNED NULL | STARTTLS obligatoire (comportement ManageSieve standard) |
| `creation_date` / `updated_at` | | convention existante |

### `connected_accounts` — comptes connectés d'un utilisateur

| Colonne | Type | Note |
|---|---|---|
| `id` | CHAR(36) PK | GUID — la valeur du header `X-Account-Id` |
| `user_id` | CHAR(36) FK → `users` ON DELETE CASCADE | |
| `domain_id` | CHAR(36) NULL FK → `external_domains` ON DELETE **RESTRICT** | NULL = serveur maison (boîte partagée locale) |
| `email` | VARCHAR(255) | login IMAP/SMTP/Sieve et adresse de l'identité par défaut |
| `cipher` | VARBINARY(512) | nonce (12) ‖ tag (16) ‖ AES-256-GCM(mot de passe) |
| `creation_date` | | |

Unicité `(user_id, domain_id, email)` + contrôle applicatif (MariaDB ne fait pas
collisionner les NULL d'un index unique). Le RESTRICT interdit de supprimer un domaine
tant que des comptes y sont connectés — cohérent avec le refus existant côté domaines
Dovecot. Refus applicatif de connecter sa propre adresse principale en local.

### Évolutions de tables existantes

- **`users`** : colonne `kdf_salt BINARY(16) NULL`, générée par
  `WebmailUserStore.RegisterLoginAsync` au premier login suivant la migration.
- **`folder_role_overrides`** : colonne `account_id CHAR(36) NULL`
  (NULL = compte principal, sinon FK → `connected_accounts` ON DELETE CASCADE) ;
  l'unicité passe de `(user_id, role)` à `(user_id, account_id, role)`. Les lignes
  existantes gardent `account_id = NULL` — aucune migration de données. C'est le
  re-scoping anticipé par la spec 2a5.
- **`sending_identities`** : colonne `account_id CHAR(36) NULL` (NULL = primaire, sinon
  FK → `connected_accounts` ON DELETE CASCADE), unicité `(account_id, email)`.
  À la connexion d'un compte (POST, même transaction), une **identité par défaut** est
  créée : adresse = celle du compte, verrouillée, non supprimable ; son display name
  alimente la bande du menu. Les identités additionnelles d'un compte connecté sont
  libres (adresse + display name), sans signature.
- `user_preferences`, `trusted_senders`, `contacts` : inchangées.

## 3. Chiffrement des credentials

Le mot de passe d'un compte connecté est saisi une fois, à la connexion du compte, et
persiste chiffré en base. Le serveur seul ne peut pas le déchiffrer : la clé dérive du
mot de passe principal, que seul le cookie de session transporte.

- **KDF** : PBKDF2-SHA256, 600 000 itérations (`Rfc2898DeriveBytes.Pbkdf2`, natif .NET),
  sel = `users.kdf_salt`, sortie = KEK 256 bits.
- **Le coût du KDF est payé au login, pas à chaque requête** : le cookie `MailCredentials`
  passe à un payload versionné `{ v: 2, password, kek }` — enveloppe inchangée
  (Data Protection, HttpOnly, Secure, SameSite=Strict), renouvellement glissant inchangé.
  Un cookie v1 encore en circulation reste accepté : le KEK est dérivé à la volée à la
  première requête qui en a besoin (sel généré à ce moment s'il manque encore), et le
  cookie est réémis en v2 dans la même réponse.
- **Par mot de passe stocké** : AES-256-GCM, nonce aléatoire 12 octets, stockage
  `nonce ‖ tag ‖ ciphertext` dans `cipher`.
- **Changement de mot de passe via l'app** (`ChangeSecret`) : l'ancien KEK vient du
  cookie, le nouveau est dérivé du nouveau mot de passe (même sel) ; tous les `cipher`
  de l'utilisateur sont re-chiffrés **dans une transaction**, puis le cookie v2 est réémis.
- **Reset externe** (admin, doveadm) : au login suivant, le KEK diffère, le tag GCM
  échoue → le compte passe en état « Password needed » côté UI (§ 5, code d'erreur § 4).
  Pas de récupération : l'utilisateur re-saisit le mot de passe du compte connecté.
  L'échec GCM est le seul signal — il suffit, le remède est identique quelle que soit
  la cause.

Aucun secret (mot de passe, KEK, cipher) ne sort jamais dans une réponse API ni dans
les logs.

## 4. Backend

### `AccountConnectionResolver`, pièce centrale

Service scoped. Entrée : header `X-Account-Id` (absent ou `primary` = compte principal),
utilisateur authentifié, cookie credentials. Sortie : la structure unique prévue par la
spec 2a —

```
MailAccountConnection {
  AccountId,                       // 'primary' ou GUID — alimente le store de pièces jointes
  ImapHost, ImapPort, ImapSecurity,
  SmtpHost, SmtpPort, SmtpSecurity,
  SieveHost?, SievePort?,          // null = pas de Sieve pour ce compte
  Username, Password               // email du compte + mot de passe déchiffré
}
```

- `primary` → endpoints depuis `MailOptions` (appsettings), mot de passe du cookie,
  username = email de session. Chemin identique à aujourd'hui.
- GUID → chargement du `connected_account` **avec contrôle de propriété** (`user_id` =
  utilisateur de session, sinon 404 indistinguable d'un id inconnu) ; config du domaine
  externe depuis la base, ou `MailOptions` si `domain_id` est NULL ; déchiffrement
  AES-GCM avec le KEK.

Les factories IMAP/SMTP perdent leur `IOptionsMonitor` interne : `OpenAsync(connection, ct)`
reçoit la connexion résolue. `ScopedImapSessionProvider` garde sa sémantique (une session
par requête), clé `(host, username, password)`. Un seul chemin de code pour serveur maison
et serveurs externes — aucun `if (isExternal)` en aval du résolveur.

### Codes d'erreur

Un 401 brut déclenche la redirection `/login` côté frontend ; les échecs propres au
compte connecté n'en sont pas :

| Situation | Réponse |
|---|---|
| Cookie credentials absent/illisible (session) | 401 `credentials_unavailable` — inchangé |
| `X-Account-Id` inconnu ou appartenant à un autre utilisateur | 404 |
| Tag GCM invalide (mot de passe principal changé hors app) | **409 `connected_credentials_invalid`** |
| Serveur distant injoignable ou refus d'authentification | 502 chemin existant, message serveur jamais relayé |

### Endpoints

**`ConnectedAccountsController`** (nouveau, `[Authorize]`, scope = utilisateur de session) :

- `GET /api/ConnectedAccounts` → `{ id, email, displayName, domainId, domainName,
  sieveSupported, credentialsValid, creationDate }` (`displayName` = celui de
  l'identité par défaut). `credentialsValid` = tentative de
  déchiffrement GCM avec le KEK courant (microsecondes, aucune connexion réseau) — c'est
  ce qui alimente la pastille « Password needed » dès l'ouverture du menu.
  `sieveSupported` = boîte locale, ou domaine avec `sieve_host`.
- `POST /api/ConnectedAccounts` `{ domainId?, email, password }` → **vérification par
  connexion IMAP réelle avant tout enregistrement** (l'auth SMTP est identique par
  construction ; Sieve non vérifié à la connexion), puis chiffrement et insertion.
- `PUT /api/ConnectedAccounts/{id}/Password` `{ password }` → même vérification,
  re-chiffrement — le remède au 409.
- `DELETE /api/ConnectedAccounts/{id}` → suppression (cascade sur les
  `folder_role_overrides` du compte).

Sécurité : l'utilisateur ne fournit jamais de host (pas de SSRF par construction) ;
POST et PUT passent sous le rate limiter existant (chaque tentative coûte une connexion
IMAP sortante).

**Admin** (`AdminController`, politique admin existante) : CRUD
`GET/POST/PUT/DELETE /api/Admin/domains/external` avec validation stricte (hosts au
format hôte, ports 1–65535, security dans l'enum, bloc Sieve tout-ou-rien). Le DELETE
renvoie un refus explicite si des comptes sont connectés au domaine.

**Routes existantes devenant sensibles à `X-Account-Id`** :

- Toutes les routes `MailController` (déjà prévu) — dossiers, messages, drapeaux,
  déplacements, recherche, envoi, brouillons, pièces jointes staged (le store reçoit
  `connection.AccountId`).
- `IdentitiesController` : `primary` → comportement actuel (identités dérivées des
  alias) ; GUID → CRUD des identités du compte (`{ email, displayName }`) — ajout et
  édition libres, identité par défaut à adresse non modifiable et non supprimable,
  **pas de signature**. Aucune vérification d'adresse possible : le SMTP externe est
  l'autorité, son refus remonte à l'envoi (502).
- `RulesController` : `primary` → chemin actuel (master user) ; boîte locale connectée →
  master user avec l'email du compte connecté ; domaine externe avec Sieve → connexion
  ManageSieve vers `sieve_host:sieve_port` (STARTTLS), authentifiée avec les credentials
  déchiffrés du compte. Domaine sans Sieve → 404. `ManageSieveClient` gagne une surcharge
  paramétrée par endpoint/credentials ; le master user reste réservé au serveur maison.
- `FolderRoleOverride` : le repository gagne `accountId` (NULL = principal).

**Routes restant aveugles au header** (compte principal par définition) :
`Account` (info, quota, mot de passe, full name), `Aliases`, `Admin`, `Preferences`,
`TrustedSenders`, `Contacts`, `AppSettings`, `Login`.

### Envoi depuis un compte connecté

SMTP du domaine avec les credentials du compte ; le From (en-tête et enveloppe) doit
être **une identité du compte connecté** (display name appliqué) — la validation
d'adresse existante s'applique à cet ensemble, comme les alias pour le primaire. Copie « Sent » : APPEND via la
session IMAP du compte, résolution SPECIAL-USE existante. Brouillons : dossier Drafts
IMAP du compte (modèle 2c3) — ils suivent le compte sans code supplémentaire.

## 5. Frontend

### Bascule

- **`AuthContext`** : `ActiveAccount` s'élargit (`id: string`, `isPrimary: boolean`,
  `email`, `displayName`, `domainName?`, `credentialsValid`, `sieveSupported`) ; la liste
  `accounts` = principal + `GET /api/ConnectedAccounts` ; méthode `switchAccount(id)`.
  Compte actif persisté (`localStorage: mail.activeAccount`), validé contre la liste au
  chargement, repli silencieux sur le principal s'il a disparu.
- **Propagation** : `useAccountId()` scope déjà toutes les clés `['mail', accountId, …]`
  et `['contacts', accountId]` — la bascule crée un cache neuf. Les fonctions mail
  d'`api.js` gagnent un paramètre `accountId` explicite (fourni par `queries.ts`) et
  `request()` émet `X-Account-Id` quand il est fourni — pas d'état mutable de niveau
  module.
- **À la bascule** : sélection ramenée sur INBOX, `removeQueries(['mail', ancienId])`,
  claim de notifications scopée par compte (`mail.lastNotifiedUidNext.<accountId>` — fin
  du bâillonnement croisé documenté dans `channels.ts`). Les mutations en vol terminent
  sur les clés de l'ancien compte (comportement correct, testé). Un brouillon ouvert
  reste attaché au compte de son ouverture (`accountId` capturé à l'ouverture du
  composer).
- **Compte connecté en défaut (409)** : le module mail affiche un état plein volet
  « Password needed for this account » avec lien vers Connected accounts ; pas de
  bascule automatique.

### Menu identités

Le composant vit **en bas de la colonne des dossiers (Mail) et en bas de la liste
d'onglets (Settings)**. Menu ouvert vers le haut :

1. Liste des comptes — display name (repli : email), sous-ligne email · domaine, pastille
   du compte actif ; une ligne « Password needed » n'est pas basculable et mène à la
   re-saisie dans Connected accounts.
2. Lien « Connected accounts… » → `/settings/accounts`.
3. Sign out.

La bande d'identité reflète le **compte actif** : pastille couleur accent et nom du
domaine en sous-ligne quand un compte connecté est actif. Les lignes de comptes sont de
vrais boutons (elles remplacent les `<div>` inertes actuels).

### Navigation Settings dépendante du compte actif

| Onglet | Primaire | Compte connecté |
|---|---|---|
| Account | ✓ | — |
| General, Appearance | ✓ | ✓ |
| Folders | ✓ | ✓ (dossiers du compte actif) |
| Aliases | ✓ | — |
| Identities | ✓ (comportement actuel) | ✓ (identité unique, display name seul) |
| Rules | ✓ | ✓ **si** `sieveSupported`, sinon absent |
| Connected accounts | ✓ | ✓ |
| Administration | ✓ (si admin) | — |

Les onglets non applicables disparaissent de la navigation ; une URL profonde vers un
onglet interdit redirige vers `/settings/general`. Basculer de compte depuis Settings
alors qu'on est sur un onglet devenu interdit → même redirection.

### Pages

- **`/settings/accounts` — Connected accounts** (remplace le `ComingSoon`) : liste des
  comptes (pastille, email, domaine, date ; actions en **icônes** conformément à la
  charte — 🗑 supprimer via `DeleteConfirmModal`, 🔑 re-saisir sur un compte en défaut,
  avec bandeau d'explication), formulaire « Connect an account » : sélecteur de serveur
  (« Weesky (local) » + domaines externes), email, mot de passe ; erreur de connexion
  affichée générique. TS + TanStack Query (patron `ApplicationTab`).
- **Administration → onglet « External domains »** : tuiles réduites au nom + icônes
  ✎/🗑 (patron de l'onglet Domains existant) ; modale Add/Edit avec Display name, blocs
  IMAP et SMTP (host/port/security) et bloc « Sieve filters (optional) » (host/port,
  vide = pas de Rules pour ce domaine). TS + TanStack Query.
- **Identities (compte connecté actif)** : même forme que le primaire — une liste.
  L'identité par défaut porte un badge « Account address » (✎ n'édite que le display
  name, pas de 🗑) ; les identités ajoutées sont libres (adresse + display name, ✎ et 🗑
  via `DeleteConfirmModal`). Le formulaire d'ajout avertit que le serveur externe a le
  dernier mot sur l'adresse. Pas de signature.
- **Composer** : le sélecteur de From existe aussi pour un compte connecté, alimenté
  par ses identités (une seule identité → texte figé, comportement existant) ; l'envoi
  part toujours par le SMTP du compte actif.
- **Rules (compte connecté actif)** : la page existante opère sur le script Sieve du
  compte actif ; rien ne change dans l'éditeur ni les providers.

Maquettes validées le 2026-07-29 (compagnon visuel) : menu identités, Connected
accounts, External domains, navigation Settings comparée, Identities compte connecté
(identités multiples, éditions défaut/libre, sélecteur From du composer).

## 6. Cas limites

- Suppression d'un domaine externe avec comptes connectés → refus explicite (RESTRICT +
  message admin).
- Suppression d'un utilisateur → cascade `connected_accounts` (et leurs overrides).
- Logout / « logout everywhere » / rotation du security stamp → sans effet sur les
  credentials au repos ; au login suivant tout redevient déchiffrable (même mot de passe).
- Serveur externe injoignable → 502 existant, l'UI mail affiche l'erreur réseau standard.
- Deux comptes connectés identiques → refus (unicité) ; adresse principale en local →
  refus applicatif.
- Cookie v1 pendant la transition → dérivation à la volée + réémission v2 (§ 3).

## 7. Hors périmètre (assumé)

Signatures des comptes connectés (décision explicite — candidate à une feature
ultérieure), notifications/poll multi-comptes,
boîte de réception unifiée, OAuth (mot de passe uniquement), alias/quota/mot de
passe/admin pour les comptes connectés, Sieve vérifié à la connexion du compte, pastilles
non-lus par compte dans le menu.

## 8. Tests

- **Backend** : round-trip crypto (chiffrer/déchiffrer, tag invalide → erreur typée),
  résolveur (primary, boîte locale, domaine externe, id d'autrui → 404, cookie v1 →
  dérivation + réémission v2), `ChangeSecret` re-chiffre en transaction, contrôleurs
  (connexion vérifie IMAP avant insert et crée l'identité par défaut, aucun secret
  dans les réponses, `credentialsValid` reflète le KEK), identités d'un compte connecté
  (défaut protégé, From d'envoi restreint aux identités du compte), validation admin
  (ports, hosts, Sieve
  tout-ou-rien), Rules par compte (master user local vs endpoint externe). `dotnet test`
  (jamais `--no-build` avec de nouveaux fichiers).
- **Frontend** : `AuthContext` (chargement, switch, persistance, repli), `IdentityMenu`
  interactif (bascule, Password needed non basculable), page Connected accounts
  (connexion, erreur, re-saisie, suppression), navigation Settings par compte
  (+ redirections), Identities compte connecté (liste, défaut protégé, ajout libre),
  sélecteur From du composer, header `X-Account-Id` émis, claim de
  notifications scopée, mutation en vol pendant bascule. Les six suites qui mockent
  `useAuth` avec `activeAccount: { id: 'primary' }` sont mises à jour.
- **Manuel** : connecter une boîte locale et un compte externe réel ; basculer dans les
  deux sens ; envoyer/recevoir depuis le compte connecté ; règles Sieve sur un compte
  externe avec Sieve et absence de l'onglet sans Sieve ; changement du mot de passe
  principal via l'app (les comptes connectés survivent) ; reset externe (état « Password
  needed », re-saisie) ; suppression d'un domaine avec comptes connectés (refus) ;
  4 combinaisons thème × palette sur les nouveaux écrans.

## 9. Vérification

1. `dotnet test` — vert, aucun test perdu sans remplaçant.
2. `npm run lint`, `npm run typecheck`, `npm run test`, `npm run build` — verts,
   couverture sans régression.
3. DDL `webmail-connected-accounts-tables.md` joué sur `snoopy_webmail` et
   `snoopy_webmail_dev` avant déploiement.
4. Checklist manuelle du § 8.
