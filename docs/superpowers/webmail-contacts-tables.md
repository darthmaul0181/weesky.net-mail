# Prérequis base de données — tables du module Contacts

À rejouer **avant** le déploiement du backend, sur `snoopy_webmail` **et** `snoopy_webmail_dev`.
Création manuelle : ce projet n'utilise pas les migrations EF.

Les FK exigent que `users` existe déjà (voir le prérequis de la table `users`).

```sql
CREATE TABLE `contacts` (
  `id`          CHAR(36)     NOT NULL COMMENT 'GUID généré côté application',
  `user_id`     CHAR(36)     NOT NULL,
  `uid`         VARCHAR(255) NOT NULL COMMENT 'UID vCard d''origine ; = id quand la source n''en portait pas',
  `first_name`  VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `last_name`   VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `nickname`    VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `is_favorite` TINYINT(1)   NOT NULL DEFAULT 0,
  `vcard_raw`   MEDIUMTEXT   DEFAULT NULL COMMENT 'vCard source tel quel ; jamais servi à l''UI',
  `updated_at`  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_contacts_user_uid` (`user_id`, `uid`),
  KEY `ix_contacts_user` (`user_id`),
  CONSTRAINT `fk_contacts_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_emails` (
  `contact_id` CHAR(36)          NOT NULL,
  `address`    VARCHAR(320)      NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `position`   SMALLINT UNSIGNED NOT NULL DEFAULT 0
    COMMENT 'Rang de la propriété dans la carte ; l''ordre d''affichage sort de (pref, position)',
  PRIMARY KEY (`contact_id`, `address`),
  CONSTRAINT `fk_contact_emails_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

## Pourquoi la collation est mixte

La table est en `utf8mb4_bin` comme ses sœurs : `uid` est opaque et sensible à la casse, `address`
est stockée canonique — une collation insensible y fusionnerait deux valeurs que le code traite
comme distinctes. Les trois colonnes de nom portent `utf8mb4_unicode_ci` : c'est du texte humain, et
un `LIKE` binaire y serait inutilisable si une recherche serveur apparaissait. Aujourd'hui tri et
filtre sont côté client, donc cette collation ne sert encore à rien — elle évite d'avoir tort plus
tard. `utf8mb4_unicode_ci` et non `utf8mb4_0900_ai_ci` : la base est MariaDB.

## Pourquoi `updated_at` est géré par le schéma

À l'inverse des dates de `users`, que le code pose explicitement pour que `creation_date` ne bouge
jamais, `contacts.updated_at` doit suivre **toute** écriture. Il n'est plus, depuis la tranche 4a
(décision 9), la base de l'ETag CardDAV — c'est `card_hash`, un SHA-256 de `vcard_raw`, qui l'est
désormais : `updated_at` reste un simple témoin. D'où `DEFAULT CURRENT_TIMESTAMP ON UPDATE
CURRENT_TIMESTAMP`.

## Ajout de la tranche 3c

À rejouer sur les deux bases si les tables existent déjà ; les fiches présentes sont toutes des
saisies manuelles, ce que le défaut leur attribue correctement.

```sql
ALTER TABLE `contacts`
  ADD COLUMN `source` ENUM('manual','captured','imported')
    NOT NULL DEFAULT 'manual'
    COMMENT 'Origine de la fiche ; écrite à la création seulement'
    AFTER `is_favorite`;
```

## Ajout de la tranche 4a

À rejouer sur `snoopy_webmail` **et** `snoopy_webmail_dev`, avant tout déploiement du backend :
d'abord ces tables, ensuite le backend, et le rattrapage des fiches `vcard_raw = NULL` en dernier.

```sql
ALTER TABLE `contacts`
  ADD COLUMN `display_name` VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL
    COMMENT 'La propriété FN de la carte ; devinée côté client jusqu''ici' AFTER `nickname`,
  ADD COLUMN `middle_name`  VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `display_name`,
  ADD COLUMN `name_prefix`  VARCHAR(50)  COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `middle_name`,
  ADD COLUMN `name_suffix`  VARCHAR(50)  COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `name_prefix`,
  ADD COLUMN `organization` VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `name_suffix`,
  ADD COLUMN `department`   VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL
    COMMENT 'Composantes 2..n de ORG, jointes par ; comme sur la carte' AFTER `organization`,
  ADD COLUMN `job_title`    VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `department`,
  ADD COLUMN `birthday`     VARCHAR(64)  DEFAULT NULL
    COMMENT 'Forme vCard telle quelle : une date partielle (--0315) ou du texte libre est valide' AFTER `job_title`,
  ADD COLUMN `website`      VARCHAR(512) DEFAULT NULL
    COMMENT 'Première occurrence de URL ; les suivantes restent dans la carte' AFTER `birthday`,
  ADD COLUMN `notes`        TEXT COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `website`,
  ADD COLUMN `card_hash`    CHAR(64) NOT NULL DEFAULT ''
    COMMENT 'SHA-256 hex de vcard_raw ; base de l''ETag CardDAV' AFTER `vcard_raw`;

-- À vérifier avant de jouer le bloc suivant : il doit répondre zéro ligne, sinon le DROP
-- PRIMARY KEY laisse une table qu'ADD PRIMARY KEY refusera.
SELECT `contact_id`, `position`, COUNT(*) FROM `contact_emails`
  GROUP BY `contact_id`, `position` HAVING COUNT(*) > 1;

ALTER TABLE `contact_emails`
  DROP PRIMARY KEY,
  ADD PRIMARY KEY (`contact_id`, `position`),
  ADD COLUMN `type`       VARCHAR(64) NOT NULL DEFAULT ''
    COMMENT 'TYPE extrait de params, pour l''affichage ; vide = sans type',
  ADD COLUMN `pref`       SMALLINT UNSIGNED NOT NULL DEFAULT 101
    COMMENT 'PREF normalisée (1..100) ; 101 = la carte n''en dit rien. Tri : (pref, position)',
  ADD COLUMN `params`     VARCHAR(255) NOT NULL DEFAULT ''
    COMMENT 'Bloc de paramètres verbatim (TYPE=WORK;PREF=1) ; affichage seul, jamais ré-émis',
  ADD COLUMN `group_name` VARCHAR(64) NOT NULL DEFAULT ''
    COMMENT 'Groupe de la propriété (item1.EMAIL) ; ce qui rattache un X-ABLabel Apple';

CREATE TABLE `contact_phones` (
  `contact_id` CHAR(36)          NOT NULL,
  `position`   SMALLINT UNSIGNED NOT NULL COMMENT 'Rang de la TEL dans la carte ; la poignée du composeur',
  `number`     VARCHAR(64)       NOT NULL COMMENT 'Tel que porté par la carte ; aucune canonicalisation',
  `type`       VARCHAR(64)       NOT NULL DEFAULT '',
  `pref`       SMALLINT UNSIGNED NOT NULL DEFAULT 101,
  `params`     VARCHAR(255)      NOT NULL DEFAULT '',
  `group_name` VARCHAR(64)       NOT NULL DEFAULT '',
  PRIMARY KEY (`contact_id`, `position`),
  CONSTRAINT `fk_contact_phones_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_addresses` (
  `contact_id`  CHAR(36)          NOT NULL,
  `position`    SMALLINT UNSIGNED NOT NULL COMMENT 'Rang de l''ADR dans la carte ; la poignée du composeur',
  `type`        VARCHAR(64)       NOT NULL DEFAULT '',
  `pref`        SMALLINT UNSIGNED NOT NULL DEFAULT 101,
  `params`      VARCHAR(512)      NOT NULL DEFAULT ''
    COMMENT 'Verbatim, LABEL compris — l''adresse formatée de 4.0 peut être longue',
  `group_name`  VARCHAR(64)       NOT NULL DEFAULT '',
  `po_box`      VARCHAR(64)  COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `extended`    VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `street`      VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `locality`    VARCHAR(128) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `region`      VARCHAR(128) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `postal_code` VARCHAR(32)  DEFAULT NULL,
  `country`     VARCHAR(128) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`contact_id`, `position`),
  CONSTRAINT `fk_contact_addresses_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_photos` (
  `contact_id` CHAR(36)    NOT NULL,
  `media_type` VARCHAR(64) NOT NULL,
  `bytes`      MEDIUMBLOB  NOT NULL,
  PRIMARY KEY (`contact_id`),
  CONSTRAINT `fk_contact_photos_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

## Ajout de la tranche 4e

À rejouer sur `snoopy_webmail` **et** `snoopy_webmail_dev`, avant tout déploiement du backend.
Aucun rattrapage de données : la requête de sondage du 2026-08-31 ne trouve aucune carte de groupe
en base, et le défaut `individual` classe correctement tout le stock. Une carte de groupe arrivant
par un `PUT` ultérieur est projetée comme telle au moment où elle arrive.

```sql
ALTER TABLE `contacts`
  ADD COLUMN `kind` ENUM('individual','group') NOT NULL DEFAULT 'individual'
    COMMENT 'Espèce de la carte ; group = KIND:group / X-ADDRESSBOOKSERVER-KIND:group'
    AFTER `source`;

CREATE TABLE `contact_group_members` (
  `group_id`   CHAR(36)          NOT NULL,
  `member_uid` VARCHAR(255)      NOT NULL
    COMMENT 'UID du membre sans son préfixe urn:uuid: ; pas son id, un client peut PUT le groupe avant ses membres',
  `position`   SMALLINT UNSIGNED NOT NULL COMMENT 'Rang du MEMBER dans la carte',
  PRIMARY KEY (`group_id`, `position`),
  UNIQUE KEY `uq_group_member` (`group_id`, `member_uid`),
  INDEX `ix_group_members_uid` (`member_uid`),
  CONSTRAINT `fk_group_members_group`
    FOREIGN KEY (`group_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```
