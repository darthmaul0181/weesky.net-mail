# Webmail — table `users` et clé de compte par GUID

**Date :** 2026-07-24
**Statut :** conception validée, prête pour le plan d'implémentation

## Problème

Trois tables de `snoopy_webmail` — `folder_role_overrides`, `user_preferences`, `sending_identities` — sont aujourd'hui clés par `account_id`, une chaîne `nom@domaine` qui est en réalité l'adresse mail principale de l'utilisateur, recalculée à chaque requête par `FolderRoleStore.CanonicalAccountId(email)` (**14 sites d'appel de production dans 5 fichiers**, plus la définition dans `FolderRoleStore`). Le même helper sert aussi de clé de compte au store de pièces jointes staged (2 sites dans `MailController`) — ce store n'est pas une des trois tables SQL, mais il partage le helper, donc il bascule lui aussi sur le GUID.

Deux conséquences :

1. **Suppression.** `DELETE /api/Admin/users/{id}` supprime dans `dovecot` uniquement ; rien ne nettoie `snoopy_webmail`. Chaque compte supprimé laisse des lignes orphelines, définitivement.
2. **Renommage.** Un utilisateur qui change d'adresse perd silencieusement toutes ses préférences : la clé `account_id` change, aucune ligne ne suit. Le renommage compte pour ce projet.

Une clé de substitution stable (un GUID) résout les deux : la suppression devient une cascade FK interne, le renommage un simple `UPDATE` de l'adresse sans toucher aux données rattachées.

## Contrainte cardinale

**Ce changement n'introduit aucune écriture nouvelle dans `dovecot`.** `dovecot` est la base du service mail ; `snoopy_webmail` est la base du webmail. La table `users`, sa clé, sa cascade sont **entièrement contenues dans `snoopy_webmail`** : aucune structure `dovecot` n'est créée ni modifiée, aucune FK ne référence `dovecot`, la cascade de suppression est interne à `snoopy_webmail`. On lit `dovecot` pour authentifier, comme aujourd'hui.

La seule écriture `dovecot` du périmètre est celle que l'admin **fait déjà** : `DELETE /api/Admin/users/{id}` supprime le compte mail. Ce comportement est préexistant et inchangé ; on lui ajoute seulement, à sa suite, la suppression de la ligne miroir côté `snoopy_webmail`.

Corollaire : le renommage d'une boîte, sur ce serveur, déplace des fichiers sur disque (le chemin maildir dérive du nom). C'est donc un geste d'exploitation externe ; le webmail ne le provoque pas, il le constate.

## Décisions

| Sujet | Décision |
|---|---|
| Clé de la table `users` | GUID en `CHAR(36)` (lisible en SQL, pour le renommage/suppression à la main) |
| Colonnes | `id` (GUID PK), `email` (VARCHAR(255)), `creation_date` (DATETIME, posée à l'INSERT, immuable), `last_login_date` (DATETIME NULL, mise à jour à chaque login) |
| Unicité | `email` porte un index `UNIQUE` — sinon la création à la volée fabriquerait deux GUID pour une même adresse |
| Canonicalisation | `email` stocké en forme canonique (minuscules) ; la table collationne en binaire (`utf8mb4_bin`) |
| Génération du GUID | côté application (`Guid.NewGuid()`), au login — le code tient le GUID immédiatement pour l'estampiller dans le jeton |
| Naissance de la ligne | à la première connexion webmail, créée à la volée si absente |
| Transport du GUID | estampillé dans le JWT à l'émission (revendication dédiée), lu par requête |
| Renommage | **manuel** : `UPDATE users SET email=… WHERE email=…` (email canonique), dans le même geste d'exploitation que la ligne `dovecot` + le déplacement du maildir |
| Suppression | **automatique** depuis notre admin (`dovecot` d'abord, `snoopy_webmail` en best-effort) ; geste manuel documenté pour une suppression faite directement en base |
| Données existantes | **aucune migration** : le webmail n'est pas en production (un seul utilisateur de test). On `DROP` et recrée les trois tables filles sous leur forme finale |

## Schéma (DDL neuf)

À rejouer d'un bloc sur `snoopy_webmail` et `snoopy_webmail_dev`. Table rase assumée.

```sql
DROP TABLE IF EXISTS `sending_identities`;
DROP TABLE IF EXISTS `folder_role_overrides`;
DROP TABLE IF EXISTS `user_preferences`;

CREATE TABLE `users` (
  `id`              CHAR(36)     NOT NULL COMMENT 'GUID généré côté application au login',
  `email`           VARCHAR(255) NOT NULL COMMENT 'Forme canonique (minuscules) ; identité mail principale',
  `creation_date`   DATETIME     NOT NULL COMMENT 'Posée à l''INSERT (UTC) ; jamais modifiée ensuite',
  `last_login_date` DATETIME     DEFAULT NULL COMMENT 'Mise à jour (UTC) à chaque login, pas à chaque requête',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_users_email` (`email`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `user_preferences` (
  `user_id`          CHAR(36)     NOT NULL,
  `preference_key`   VARCHAR(64)  NOT NULL,
  `preference_value` VARCHAR(255) NOT NULL,
  `updated_at`       TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`user_id`, `preference_key`),
  CONSTRAINT `fk_user_preferences_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `folder_role_overrides` (
  `user_id`      CHAR(36)              NOT NULL,
  `role`         VARCHAR(16)           NOT NULL,
  `folder_path`  VARCHAR(1024)         NOT NULL,
  `uid_validity` BIGINT(20) UNSIGNED   NOT NULL,
  `mailbox_id`   VARCHAR(255)          DEFAULT NULL,
  `updated_at`   TIMESTAMP             NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`user_id`, `role`),
  CONSTRAINT `fk_folder_role_overrides_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `sending_identities` (
  `user_id`      CHAR(36)     NOT NULL,
  `address`      VARCHAR(320) NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `display_name` VARCHAR(100) NOT NULL,
  `is_default`   TINYINT(1)   NOT NULL DEFAULT 0,
  `updated_at`   TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`user_id`, `address`),
  CONSTRAINT `fk_sending_identities_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Note InnoDB : les FK exigent que `users` existe avant les tables filles, et que le `DROP` des filles précède un éventuel `DROP` de `users`.

Note dates : `creation_date` et `last_login_date` n'ont **ni** `DEFAULT CURRENT_TIMESTAMP` **ni** `ON UPDATE` — leurs valeurs sont posées explicitement par le code (voir `RegisterLoginAsync`). Cela garantit que `creation_date` ne bouge jamais et que `last_login_date` ne change qu'au login, jamais lors d'un renommage (`UPDATE email`).

## Cycle de vie de la ligne

| Événement | Qui | Effet |
|---|---|---|
| 1ʳᵉ connexion webmail | le service, **automatique** | ligne absente → `INSERT (Guid.NewGuid(), email canonique, creation_date=now, last_login_date=now)`, puis le jeton est émis avec le GUID |
| Login suivant | le service, **automatique** | ligne présente → `UPDATE last_login_date=now` ; `creation_date` immuable. Aucune écriture par requête, seulement au login |
| Renommage | l'exploitation, **manuel** | `UPDATE users SET email='nouveau' WHERE email='ancien'` (canonique), dans le même geste que la ligne `dovecot` + le maildir. Le GUID ne bouge pas → identités, rôles, préférences suivent |
| Suppression via admin | notre admin, **automatique** | `dovecot` supprimé d'abord ; puis `DELETE FROM users WHERE email=…` en best-effort. La cascade FK emporte les trois tables filles |
| Suppression directe en base | l'exploitation, **manuel** | `DELETE FROM users WHERE email=…` documenté, pour ne pas laisser d'orphelin quand la suppression contourne l'admin |

**Risque assumé du modèle manuel de renommage.** Si un renommage oublie l'`UPDATE`, la reconnexion sous la nouvelle adresse recrée une ligne neuve (préférences vides) et l'ancienne devient orpheline. Décision prise en connaissance de cause ; le mode opératoire le documente noir sur blanc.

## Flux d'authentification

### Enregistrement du login (`IWebmailUserStore.RegisterLoginAsync`)

```
RegisterLoginAsync(string canonicalEmail, CancellationToken) : Guid
```

Une seule opération, appelée dans le flux de login **avant** l'émission du jeton — donc **une fois par login**, jamais par requête. Elle garantit l'existence de la ligne et estampille le login :

1. `SELECT id FROM users WHERE email = @canonicalEmail`.
2. **Trouvé** → `UPDATE users SET last_login_date = @nowUtc WHERE id = @id`, renvoie le GUID. `creation_date` n'est pas touchée.
3. **Absent** → `INSERT (Guid.NewGuid(), @canonicalEmail, creation_date = @nowUtc, last_login_date = @nowUtc)`, renvoie le GUID généré. La création étant elle-même déclenchée par un login, `last_login_date` vaut `creation_date` dès la naissance — elle n'est donc jamais NULL en pratique (le `DEFAULT NULL` du schéma ne couvre qu'une ligne insérée hors de ce chemin).
4. **Concurrence** (deux onglets, première connexion simultanée) : l'`INSERT` peut violer `uq_users_email`. Encaisser proprement — rattrapage sur violation d'unicité suivi d'un `SELECT` (+ l'`UPDATE last_login_date`), ou `INSERT … ON DUPLICATE KEY UPDATE last_login_date`. Un test pinne ce cas.

`@nowUtc` est `DateTime.UtcNow`, généré côté application comme le GUID — cohérent avec le reste du code (`AdminRepository` estampille déjà `DateTime.UtcNow`).

### Le jeton

`TokenManager.Generate` ajoute une revendication portant le GUID (en plus des `Upn`/`Dns` existants). `SlidingSessionMiddleware`, s'il renouvelle le jeton, **recopie la revendication depuis le jeton courant** — le GUID est stable, on ne re-résout pas la base à chaque requête.

### La lecture

Un accessor unique — sur le modèle de `ControllerBaseExtensions.GetUser()` — lit la revendication GUID. Les contrôleurs cessent d'appeler `CanonicalAccountId`.

## Impact code

Net, le changement **simplifie** le code courant : `CanonicalAccountId(email)` recalculé sur 19 sites disparaît au profit d'une revendication lue une fois.

| Zone | Changement |
|---|---|
| **Socle auth** (en premier) | `IWebmailUserStore` + `WebmailUserStore` (`RegisterLoginAsync`) ; DI ; appel dans le login ; revendication `webmail_uid` dans `TokenManager` ; recopie dans `SlidingSessionMiddleware` ; accessor `GetWebmailUserId()` |
| **Les 3 stores** | `FolderRoleStore`, le store de préférences, `SendingIdentityStore` : clé `string account_id` → `Guid userId`. Entités EF : colonne `AccountId` → `UserId`. Chaque store bascule indépendamment une fois le GUID disponible |
| **Contrôleurs** | `IdentitiesController`, `MailController`, `PreferencesController` : lisent le GUID au lieu de `CanonicalAccountId(GetUser().Email)`. `MailFolderRepository` et `MailSender` ne reçoivent qu'un `Models.User` (pas le `ClaimsPrincipal`) : le GUID est porté par une nouvelle propriété `User.WebmailUid`, peuplée depuis la revendication par `GetUser()` |
| **`IdentityResolver`** | Logique **inchangée** (elle travaille sur des adresses, pas sur la clé de compte). Seul l'appelant lui fournit désormais le GUID pour charger/écrire les lignes |
| **Store de pièces jointes staged** | Ses deux sites dans `MailController` passaient `CanonicalAccountId(email)` comme clé de compte ; ils passent désormais `WebmailUid.ToString()`. L'interface du store (clé opaque `string`) ne change pas |
| **Suppression admin** | `AdminRepository` reçoit le store webmail ; `DeleteUserAsync` charge le domaine pour reconstruire l'email canonique `user.Name + "@" + domain.Name`, supprime `dovecot`, puis `DELETE` webmail best-effort |

## Ordre d'implémentation

Big-bang, une seule tranche, pas de compat ascendante (on recrée tout) :

1. **Socle auth** — table `users`, `WebmailUserStore.RegisterLoginAsync`, revendication au login, recopie middleware, accessor. Rien ne peut basculer vers le GUID tant que le jeton ne le porte pas.
2. **Les trois stores + leurs contrôleurs** — chacun passe de `account_id` à `user_id`.
3. **Suppression admin** — `AdminRepository` propage la suppression en best-effort.

## Gestion d'erreur

- **Création concurrente au login** : encaissée (voir `RegisterLoginAsync`), pinnée par un test.
- **Jeton encore valide, utilisateur supprimé** : une écriture de préférence/identité échoue sur la contrainte FK (`user_id` absent de `users`). Défaut retenu : **401**, le compte n'existant plus, l'utilisateur doit se ré-authentifier (et un nouveau login échouera côté `dovecot`). Le plan confirme le point exact de détection ; 401 est la valeur par défaut, pas un 500 brut ni un 404.
- **Suppression best-effort** : `dovecot` d'abord (source de vérité du compte) ; un échec de la suppression webmail est journalisé sans faire échouer la suppression du compte. **0 ligne supprimée = succès silencieux** (compte jamais venu sur le webmail — la ligne n'a jamais existé) ; ce cas ne journalise même pas d'avertissement. Seule une erreur réelle (base injoignable, erreur SQL) est journalisée.

## Tests

- `WebmailUserStore` : création si absente, idempotence si présente, `INSERT` concurrent, canonicalisation de l'email à l'écriture. `creation_date` posée à l'`INSERT` et **inchangée** au login suivant ; `last_login_date` **avancée** à chaque login ; un renommage (`UPDATE email`) ne touche ni l'une ni l'autre.
- Login : la revendication GUID est présente dans le jeton émis ; `SlidingSessionMiddleware` la reporte au renouvellement.
- Chaque store : lecture/écriture keyées sur le GUID, isolation entre deux GUID distincts.
- Suppression admin : ligne présente (supprimée + cascade), ligne absente (succès silencieux), base injoignable (compte supprimé quand même, erreur journalisée).
- Renommage (au niveau SQL/store) : un `UPDATE` de l'email conserve le GUID, donc les lignes filles restent rattachées.

## Hors périmètre

- **Renommage depuis l'admin** : `UpdateUserAsync` ne sait pas renommer aujourd'hui (il ignore `UserName`/`DomainId`), et un renommage déplace le maildir — c'est un geste d'exploitation externe. Non couvert ici.
- **Migration de données** : aucune, table rase.
- **FK inter-schémas vers `dovecot`** : explicitement écartée — elle imposerait les deux schémas sur le même serveur et ferait dépendre le webmail de la structure d'une base qui ne lui appartient pas.
