# Prérequis base de données — tables des comptes connectés

À rejouer **avant** tout déploiement testé, sur `snoopy_webmail` **et** `snoopy_webmail_dev`.
Création manuelle : ce projet n'utilise pas les migrations EF.

Les FK exigent que `users`, `folder_role_overrides` et `sending_identities` existent déjà (voir
`webmail-users-table.md`).

## Contexte

Tranche 2d : un utilisateur peut relier d'autres boîtes à sa session (un domaine externe autorisé
par l'admin, ou une boîte partagée locale du même serveur). `external_domains` est le registre de
ces domaines ; `connected_accounts` relie un utilisateur à chacune de ses boîtes connectées, mot de
passe chiffré (AES-256-GCM) sous une clé dérivée par utilisateur — `users.kdf_salt` porte le sel de
cette dérivation. `folder_role_overrides` et `sending_identities` se re-scopent par compte via une
colonne `account_id` à valeur sentinelle (voir plus bas).

## DDL

```sql
ALTER TABLE users
  ADD COLUMN IF NOT EXISTS kdf_salt BINARY(16) NULL
    COMMENT 'Sel PBKDF2 du KEK des comptes connectés ; pré-rempli par la migration, sinon posé par GetOrCreateKdfSaltAsync au login';

-- Pré-remplit chaque ligne existante : aucun login ne peut plus courir sur un sel NULL, donc
-- deux connexions simultanées ne risquent plus d'en générer chacune un et d'en perdre une.
UPDATE users SET kdf_salt = RANDOM_BYTES(16) WHERE kdf_salt IS NULL;

CREATE TABLE IF NOT EXISTS external_domains (
  id            CHAR(36)     NOT NULL COMMENT 'GUID',
  name          VARCHAR(100) NOT NULL COMMENT 'Nom d''affichage (« Gmail »)',
  imap_host     VARCHAR(255) NOT NULL,
  imap_port     SMALLINT UNSIGNED NOT NULL,
  imap_security VARCHAR(16)  NOT NULL COMMENT 'None | StartTls | SslOnConnect',
  smtp_host     VARCHAR(255) NOT NULL,
  smtp_port     SMALLINT UNSIGNED NOT NULL,
  smtp_security VARCHAR(16)  NOT NULL,
  sieve_host    VARCHAR(255) NULL COMMENT 'NULL = le domaine ne supporte pas Sieve',
  sieve_port    SMALLINT UNSIGNED NULL,
  creation_date DATETIME     NOT NULL COMMENT 'UTC, posée par le code',
  updated_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE KEY uq_external_domains_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS connected_accounts (
  id            CHAR(36)     NOT NULL COMMENT 'GUID — la valeur du header X-Account-Id',
  user_id       CHAR(36)     NOT NULL,
  domain_id     CHAR(36)     NULL COMMENT 'NULL = serveur maison (boîte partagée locale)',
  email         VARCHAR(255) NOT NULL COMMENT 'Login IMAP/SMTP/Sieve et adresse de l''identité par défaut',
  cipher        VARBINARY(512) NOT NULL COMMENT 'nonce(12) + tag(16) + AES-256-GCM(mot de passe)',
  creation_date DATETIME     NOT NULL COMMENT 'UTC, posée par le code',
  PRIMARY KEY (id),
  UNIQUE KEY uq_connected_accounts_target (user_id, domain_id, email),
  CONSTRAINT fk_connected_accounts_user   FOREIGN KEY (user_id)   REFERENCES users(id)            ON DELETE CASCADE,
  CONSTRAINT fk_connected_accounts_domain FOREIGN KEY (domain_id) REFERENCES external_domains(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- '' = compte principal. Pas de FK vers connected_accounts : la valeur sentinelle l'empêche,
-- la purge est applicative (suppression d'un compte connecté = ses lignes partent avec).
ALTER TABLE folder_role_overrides
  ADD COLUMN IF NOT EXISTS account_id VARCHAR(36) NOT NULL DEFAULT ''
    COMMENT ''''' = compte principal, sinon GUID connected_accounts';

ALTER TABLE sending_identities
  ADD COLUMN IF NOT EXISTS account_id VARCHAR(36) NOT NULL DEFAULT ''
    COMMENT ''''' = compte principal, sinon GUID connected_accounts';
```

### Reprise des clés primaires

`DROP PRIMARY KEY` / `ADD PRIMARY KEY` n'acceptent **pas** de clause `IF [NOT] EXISTS` : rejoués tels
quels, ils échouent (« Can't DROP 'PRIMARY' » sur une table qui n'en a plus, ou « Multiple primary key
defined » sur une table qui en a déjà une). Ils sont donc sortis du bloc ci-dessus et conditionnés à la
composition réelle de la clé, ce qui rend le script rejouable dans son ensemble :

```sql
-- Ne recompose la clé que si `account_id` n'y est pas déjà. Un second passage ne fait rien.
SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'folder_role_overrides'
      AND INDEX_NAME = 'PRIMARY' AND COLUMN_NAME = 'account_id') = 0,
  'ALTER TABLE folder_role_overrides DROP PRIMARY KEY, ADD PRIMARY KEY (user_id, account_id, role)',
  'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'sending_identities'
      AND INDEX_NAME = 'PRIMARY' AND COLUMN_NAME = 'account_id') = 0,
  'ALTER TABLE sending_identities DROP PRIMARY KEY, ADD PRIMARY KEY (user_id, account_id, address)',
  'DO 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
```

Ce que ce garde-fou ne couvre pas, honnêtement : il teste la **composition** de la clé, pas son
existence. Une table dont la clé primaire aurait été supprimée à la main entre deux passages passe le
test comme si elle n'avait jamais été migrée, puis échoue sur son `DROP PRIMARY KEY`. C'est le seul
état qui reste à rattraper manuellement ; le reste du script est rejouable tel quel (`CREATE TABLE IF
NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`, et l'`UPDATE` déjà borné par son `WHERE kdf_salt IS NULL`).

Ordre imposé par InnoDB : `external_domains` avant `connected_accounts` (FK `fk_connected_accounts_domain`),
et `users` doit déjà exister avant les deux (FK `fk_connected_accounts_user`).

## Vérification

```sql
SELECT COLUMN_NAME FROM information_schema.COLUMNS
 WHERE TABLE_SCHEMA = 'snoopy_webmail' AND TABLE_NAME = 'users' AND COLUMN_NAME = 'kdf_salt';

SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'snoopy_webmail' AND TABLE_NAME IN ('external_domains', 'connected_accounts');
-- attendu : les deux lignes en utf8mb4_bin

SELECT COLUMN_NAME FROM information_schema.COLUMNS
 WHERE TABLE_SCHEMA = 'snoopy_webmail' AND TABLE_NAME IN ('folder_role_overrides', 'sending_identities')
   AND COLUMN_NAME = 'account_id';
-- attendu : une ligne par table

SELECT TABLE_NAME, SEQ_IN_INDEX, COLUMN_NAME FROM information_schema.STATISTICS
 WHERE TABLE_SCHEMA = 'snoopy_webmail' AND INDEX_NAME = 'PRIMARY'
   AND TABLE_NAME IN ('folder_role_overrides', 'sending_identities')
 ORDER BY TABLE_NAME, SEQ_IN_INDEX;
-- attendu : (user_id, account_id, role) et (user_id, account_id, address)
```

À rejouer à l'identique avec `TABLE_SCHEMA = 'snoopy_webmail_dev'`.

## Désinstallation

> **Perte de données, à lire avant de lancer le bloc.** Désinstaller **supprime définitivement les
> surcharges de rôles de dossier et les identités d'expédition de *tous* les comptes connectés**, pour
> tous les utilisateurs. Ce n'est pas un effet de bord évitable : ces lignes sont précisément ce qui
> rend `(user_id, role)` et `(user_id, address)` non uniques, donc l'ancienne clé primaire ne peut pas
> revenir tant qu'elles sont là. Seul le compte principal de chaque utilisateur est préservé.

```sql
-- Obligatoire, et avant toute recomposition de clé : sans ce ménage, les ADD PRIMARY KEY ci-dessous
-- échouent en « Duplicate entry » sur la première boîte qui a un compte connecté. Une fois ces
-- lignes parties, il ne reste que account_id = '', donc les anciennes clés redeviennent uniques.
DELETE FROM folder_role_overrides WHERE account_id <> '';
DELETE FROM sending_identities    WHERE account_id <> '';

ALTER TABLE folder_role_overrides
  DROP PRIMARY KEY,
  ADD PRIMARY KEY (user_id, role),
  DROP COLUMN IF EXISTS account_id;

ALTER TABLE sending_identities
  DROP PRIMARY KEY,
  ADD PRIMARY KEY (user_id, address),
  DROP COLUMN IF EXISTS account_id;

DROP TABLE IF EXISTS connected_accounts;
DROP TABLE IF EXISTS external_domains;

ALTER TABLE users DROP COLUMN IF EXISTS kdf_salt;
```

Même limite qu'à l'installation : les deux recompositions de clé primaire ne sont pas rejouables,
donc la désinstallation ne se joue qu'une fois, et seulement sur un schéma effectivement installé —
les deux `DELETE` échouent si la colonne `account_id` n'existe pas.

Ordre inverse de la création, et il compte : `connected_accounts` porte `fk_connected_accounts_domain`
vers `external_domains`, donc la table référençante part en premier — l'inverse échouerait sur la
contrainte. `folder_role_overrides` et `sending_identities` ne portent aucune FK vers
`connected_accounts` (la sentinelle `''` l'interdit), donc leur ménage peut précéder les `DROP TABLE`
comme il le fait ici. `users` n'est touchée qu'en dernier, une fois sa table référençante partie ;
`kdf_salt` n'entre dans aucune contrainte, sa suppression ne dépend que de cela.

## Ce qui reste à la charge de l'application

- **Le trou multi-NULL de `uq_connected_accounts_target`** : deux comptes locaux identiques
  (`domain_id` NULL) pour le même utilisateur et le même `email` ne collisionnent pas dans l'index
  unique — MariaDB ne fait jamais collisionner deux NULL. C'est le code qui doit refuser la
  création d'un doublon local avant l'INSERT.
- **La purge en cascade des lignes `account_id`** : `folder_role_overrides` et `sending_identities`
  ne portent aucune FK sur `account_id` (la valeur sentinelle `''` l'interdit). Supprimer un compte
  connecté doit donc supprimer explicitement, côté application, ses lignes dans ces deux tables.
- **La sentinelle `''`** : `account_id NOT NULL DEFAULT ''` signifie « compte principal » ; sa
  cohérence (jamais une valeur qui n'est ni `''` ni un GUID `connected_accounts` existant) n'est
  garantie par aucune contrainte de schéma, seulement par le code qui écrit ces tables.
