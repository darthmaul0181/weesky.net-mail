# Découplage du webmail et plugin weesky.net — design

Le webmail est aujourd'hui soudé à la stack mail weesky.net : authentification contre la base
MariaDB de Dovecot, quota via l'API doveadm, règles Sieve via un master user, administration des
comptes/domaines/aliases en dur. L'objectif : qu'un opérateur possédant une stack mail standard
(IMAP/SMTP, et idéalement ManageSieve) puisse déployer ce webmail devant la sienne — et que la
stack weesky.net devienne un **plugin activable** qui, lorsqu'il est présent, offre l'expérience
intégrale actuelle. À terme, un container docker fournira la stack complète avec le webmail ; ce
design le permet mais ne le construit pas.

Le terrain était préparé : la règle « rien du serveur mail n'est configuré », le résolveur
`IAccountConnectionResolver` et la base webmail séparée (`snoopy_webmail`) font que le cœur mail
(les quatre contrôleurs `api/Mail`, Contacts, Identities, les préférences) est déjà générique. Le
couplage restant se concentre sur cinq points : l'authentification, la base `dovecot` (aliases,
domaines, admin, profil, mot de passe), le quota doveadm, le master user Sieve, et les modules
frontend admin/aliases.

## Les décisions

| Décision | Retenu | Pourquoi |
|---|---|---|
| Authentification | **Login IMAP dans les deux modes** | Un seul chemin, dogfoodé par la prod weesky ; la preuve porte sur le protocole que le cookie servira ensuite ; les tentatives passent par l'auth penalty de Dovecot et fail2ban, que le chemin DB contournait. Le port `IUserAuthenticator` disparaît de la couture. |
| Packaging du plugin | **Ports fins + projet `snoopy.providers.weesky`** | Petites interfaces par préoccupation, implémentées dans une class library qui référence le cœur — jamais l'inverse, le compilateur garde la frontière. Un binaire, activation par `"Platform"` dans appsettings. |
| Quota utilisateur | **IMAP `GETQUOTAROOT`, les deux modes** | La session IMAP de l'utilisateur existe déjà ; RFC 2087 standard ; marche aussi pour les comptes connectés à terme. Doveadm n'apportait rien de plus (STORAGE/MESSAGE identiques). |
| Quota admin (onglet Accounts) | **Doveadm, provider weesky** | Un admin n'a pas les credentials IMAP de la cible ; la clé API doveadm est l'outil d'administration. La section `Dovecot` (ApiUrl/ApiKey) migre sous le bloc `Weesky`. |
| Règles Sieve | **ManageSieve avec les credentials de l'utilisateur, les deux modes** | Ils sont déjà dans le cookie chiffré ; RFC 5804 standard. L'unique usage du master user (`RulesController.cs:52`, impersonation pour l'utilisateur courant) disparaît — `Sieve:MasterUser`/`MasterPassword` sont supprimés. Si un besoin admin d'éditer les règles d'autrui naît un jour, l'impersonation sera réintroduite à ce moment-là. |
| Identités en générique | **Libres** | Sans annuaire d'aliases, la propriété d'une adresse est invérifiable. Modèle Roundcube : l'utilisateur déclare, le SMTP tranche à l'envoi (`SenderNotAccepted` nomme déjà l'adresse refusée). Le provider weesky garde la validation stricte contre les aliases live. |
| Libellé du primaire | **Chaîne uniforme : ligne stockée → nom de profil du provider → adresse** | En weesky, rien ne change : l'onglet Account reste la seule surface d'édition (aucune ligne stockée créée pour le primaire, le FullName Dovecot gagne). En générique, sans FullName, l'onglet Identities édite le libellé du primaire comme celui d'un connected account. Une seule règle de résolution ; c'est la capacité qui choisit la surface. |
| Base webmail | **MariaDB/MySQL requis, les deux modes** | Un seul provider EF Core à tester ; Roundcube et Snappymail exigent aussi une base ; le docker-compose futur l'embarquera. EF Core laisse SQLite/PostgreSQL ouverts si la demande existe. |
| Format appsettings | **Breaking accepté** | Format cible propre, sans mapping legacy ; mise à jour manuelle des appsettings dev/prod au déploiement ; le service refuse de démarrer sur un format incomplet en nommant la clé manquante. |
| Changement de mot de passe | **Weesky uniquement (`IPasswordChanger`)** | Aucun protocole standard côté générique — l'utilisateur change son mot de passe là où sa stack le prévoit ; un cookie devenu invalide donne 401 → re-login, le cas « changed outside the app » déjà géré. Le flux weesky actuel (écriture DB, re-keying des connected accounts, rotation du stamp, réémission des deux cookies) est inchangé et reste correct avec le login IMAP : Dovecot lit la même table. |

## Architecture

```
snoopy.microservice            le cœur : hôte ASP.NET + tout le webmail générique
    ▲
    │ référence
snoopy.providers.weesky        class library : tout ce qui touche la stack weesky.net
```

**Le cœur** : les quatre contrôleurs `api/Mail`, Contacts, Identities, Rules (credentials
utilisateur), Login (IMAP), quota utilisateur (IMAP), `PreferencesDbContext`/`snoopy_webmail`,
auth JWT/cookies, sanitiseurs, connected accounts, et les **ports**.

**Le provider weesky** : `ApplicationDbContext` (base `dovecot`), `UsersRepository` (réduit —
voir plus bas), `AliasesRepository`, `AdminRepository`, `AdminController`, `AliasesController`,
`DovecotQuotaClient`, et les implémentations des ports.

**L'hôte** référence les deux et câble le DI selon `"Platform": "weesky" | "generic"`. Les
contrôleurs du provider ne sont mappés que si le provider est actif (`ApplicationPart`
conditionnelle) : en générique, `api/Admin` et `api/aliases` répondent 404.

### Les ports

| Port | Générique | Weesky |
|---|---|---|
| `IAliasDirectory` (aliases live pour la validation d'identités) | Répertoire vide → identités libres | `AliasesRepository` |
| `IProfileReader` (nom de profil du compte) | `null` | FullName de la base `dovecot` |
| `IProfileWriter` (édition du FullName, onglet Account) | Absent — capacité éteinte | Écriture base `dovecot` |
| `IPasswordChanger` (`PATCH /api/account/changesecret`) | Absent — capacité éteinte | Flux actuel inchangé |
| Module admin (contrôleur + doveadm) | Absent — routes non mappées | `AdminController` + `DovecotQuotaClient` |

`IUserAuthenticator` ne fait pas partie de la couture : le login IMAP est du code cœur, identique
partout. `UsersRepository.VerifyCredentialsAsync` perd son unique appelant et est supprimé ; le
repository ne garde que ce que les ports weesky consomment (profil, mot de passe) et ce que
l'admin utilise.

### Configuration cible

```json
{
  "Platform": "weesky",
  "ConnectionStrings": { "WebmailPreferencesDatabase": "..." },
  "TokenConstants": { ... },
  "Mail": { ... },
  "Sieve": { "Host": "...", "Port": 4190, "ScriptName": "...", "TimeoutSeconds": 10, "AllowInvalidCertificate": false },
  "TrustedSenders": { ... },
  "Weesky": {
    "ConnectionStrings": { "MailUserAccountsDatabase": "..." },
    "Dovecot": { "ApiUrl": "...", "ApiKey": "..." }
  }
}
```

Disparaissent : `Sieve:MasterUser`, `Sieve:MasterPassword`. Migrent sous `Weesky` :
`MailUserAccountsDatabase`, la section `Dovecot`. Avec `Platform=weesky` et un bloc `Weesky`
incomplet, le démarrage échoue en nommant la clé manquante (même modèle que le refus actuel sans
`WebmailPreferencesDatabase`). En `generic`, le bloc `Weesky` est ignoré.

## Capacités et frontend

`GET /api/Capabilities` (authentifié), agrégé par le cœur :

```json
{
  "platform": "weesky",
  "admin": true,
  "aliases": true,
  "passwordChange": true,
  "profileEditing": true,
  "strictIdentities": true,
  "quota": true,
  "rules": true
}
```

- La moitié haute dérive du câblage DI (mode) ; `admin` combine le câblage et `IsAdminAsync`
- `quota` et `rules` sont **découverts, jamais configurés** — capability IMAP `QUOTA` annoncée,
  ManageSieve joignable — cohérent avec « rien du serveur mail n'est configuré »
- Le frontend charge les capacités au boot de session et gate les surfaces : onglets
  Admin/Aliases/Rules, jauge de quota, section mot de passe, éditabilité du libellé du primaire
  dans Identities (`!profileEditing`)
- Le gating UI est du confort ; la défense reste backend (routes non mappées, policy Admin
  per-request)
- L'API omettant les champs null (`WhenWritingNull`), les flags côté client se lisent
  `undefined` → falsy ; les types frontend les déclarent optionnels

## Flux détaillés

**Login.** `POST /api/login` → connexion via `ImapConnectionFactory`, LOGIN, LOGOUT. Succès → flux
existant (upsert `RegisterLoginAsync`, JWT + cookie de credentials). Échec → réponse opaque
actuelle, audit `reason=imap_no` (la granularité unknown/deactivated/bad_password part dans les
logs Dovecot, l'endroit canonique). Rate limiting partition `login` conservé.

**Quota utilisateur.** `GET /api/account/quota` lit `GETQUOTAROOT INBOX` dans la session IMAP du
compte. Sans capability `QUOTA` : **204 No Content** — la jauge est masquée (et `quota:false` dans
les capacités), un 204 n'étant pas une erreur il ne déclenche aucun toast.

**Règles.** `RulesController` construit `SieveConnection` avec le username et le mot de passe de la
`MailAccountConnection` résolue (SASL PLAIN direct). Host/port : config `Sieve`. Le chemin marche
désormais pour tout serveur exposant ManageSieve.

**Identités.** Le chargeur d'`IdentitiesController` passe par `IAliasDirectory` + `IProfileReader`.
`Validate` générique : toute adresse bien formée. `LabelFor` : ligne stockée → profil → adresse,
dans les deux modes.

**Erreurs.** Le modèle 401/404/409/502 ne bouge pas. Échec LOGIN au login → réponse opaque du
login. ManageSieve refusant les credentials → 502. Quota non supporté → réponse typée, pas une
erreur.

## Tests

- Les tests d'`Admin`/`Aliases`/`UsersRepository`/`DovecotQuotaClient` migrent vers
  `snoopy.providers.weesky.Tests` ; le reste ne bouge pas
- Nouveaux : `ImapAuthenticator` (succès, refus, injoignable, opacité), Capabilities dans les deux
  modes, quota IMAP avec/sans capability, `RulesController` en credentials utilisateur, `LabelFor`
  uniformisé, `Validate` générique
- **Deux tests de démarrage DI** — l'hôte boote en `weesky` et en `generic`, chaque port résolu ou
  proprement absent : la classe d'erreur la plus probable de ce refactor est le câblage oublié
- Surface de routes par mode : `api/Admin`/`api/aliases` présents en weesky, 404 en générique
- Frontend : fixture de capacités, chaque flag à `false` masque sa surface ; la fixture **omet**
  les flags plutôt que d'y mettre `null`

## Migration au déploiement

1. Mise à jour manuelle des appsettings dev puis prod (ajout `Platform`, déplacements sous
   `Weesky`, suppression du master user Sieve)
2. Le démarrage refuse un format incomplet en nommant la clé manquante
3. Nettoyage serveur optionnel ensuite : retirer le master user ManageSieve de la config Dovecot,
   plus rien ne l'utilise
4. Aucune migration de données : `dovecot` et `snoopy_webmail` gardent leurs schémas

**Hypothèse à vérifier au passage** : un compte désactivé en base est bien refusé par le passdb
SQL de Dovecot (sinon un compte désactivé lirait déjà son mail via n'importe quel client IMAP —
trou préexistant, hors périmètre, mais à constater).

**Point d'exploitation, préexistant** : si `auth_cache` est activé côté Dovecot, un changement de
mot de passe peut mettre quelques minutes à être vu par le passdb.

## Non-objectifs

- Le container docker « stack complète » : ce design le permet (un binaire, deux modes), il ne le
  construit pas
- SQLite/PostgreSQL pour la base webmail
- Plugins tiers chargés dynamiquement : la couture est des interfaces et un csproj, pas un
  écosystème
- Édition admin des règles Sieve d'autres utilisateurs
- Serveur IMAP choisi par l'utilisateur au login : le déploiement générique vise un opérateur qui
  met le webmail devant **sa** stack, host fixé en config
