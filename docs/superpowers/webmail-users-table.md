# Table `users` et refonte des clés — DDL manuel

À rejouer d'un bloc sur `snoopy_webmail` **et** `snoopy_webmail_dev`. Table rase assumée
(webmail hors production). Ordre imposé par InnoDB : `DROP` des tables filles d'abord, puis
`CREATE users`, puis recréation des filles avec la FK.

Aucun `GRANT` à rejouer : les utilisateurs `snoopy_webmail`/`snoopy_webmail_dev` ont déjà
`SELECT, INSERT, UPDATE, DELETE` sur toute la base.

## Script

```sql
DROP TABLE IF EXISTS `sending_identities`;
DROP TABLE IF EXISTS `folder_role_overrides`;
DROP TABLE IF EXISTS `user_preferences`;
DROP TABLE IF EXISTS `users`;

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

Note InnoDB : les FK exigent que `users` existe avant les tables filles, et que le `DROP` des
filles précède un éventuel `DROP` de `users`.

Note dates : `creation_date` et `last_login_date` n'ont **ni** `DEFAULT CURRENT_TIMESTAMP` **ni**
`ON UPDATE` — leurs valeurs sont posées explicitement par le code (voir `RegisterLoginAsync`).
Cela garantit que `creation_date` ne bouge jamais et que `last_login_date` ne change qu'au login,
jamais lors d'un renommage (`UPDATE email`).

## Mode opératoire (renommage / suppression)

- **Renommage** (geste d'exploitation, il déplace aussi le maildir) : dans le même geste,
  `UPDATE snoopy_webmail.users SET email='<nouveau canonique>' WHERE email='<ancien canonique>';`
  Le GUID ne bouge pas → identités, rôles, préférences suivent. **Oublier cet UPDATE** laisse
  l'ancienne ligne orpheline et recrée une ligne vide à la reconnexion : effet accepté, documenté.
- **Suppression via l'admin** : automatique (`dovecot` d'abord, puis la ligne `users` en
  best-effort ; la cascade FK emporte les trois tables filles).
- **Suppression directe en base** (hors admin) :
  `DELETE FROM snoopy_webmail.users WHERE email='<canonique>';` — sinon le webmail recrée à la
  volée et laisse un orphelin.
