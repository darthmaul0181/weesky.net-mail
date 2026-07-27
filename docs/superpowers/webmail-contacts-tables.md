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
  `position`   SMALLINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = adresse principale',
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
jamais, `contacts.updated_at` doit suivre **toute** écriture : il est la base d'un futur ETag
CardDAV. D'où `DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP`.
