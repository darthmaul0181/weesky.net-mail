# Prérequis serveur — table `trusted_senders`

**À appliquer sur les deux bases** (`snoopy_webmail` et `snoopy_webmail_dev`) avant de déployer
le backend qui expose `/api/TrustedSenders`.

Le projet n'utilise pas les migrations EF : la création des tables est manuelle, comme pour
`sending_identities` (voir `webmail-users-table.md`).

## Pourquoi cette table

Les expéditeurs dont l'utilisateur a accepté les images distantes une fois pour toutes. La liste
se construit un clic à la fois depuis le lecteur et se révoque au même endroit ; aucun écran de
gestion ne l'expose.

`last_used` est rafraîchie à l'ouverture d'un message de cet expéditeur, au plus une fois par
jour. Un balayage quotidien supprime les entrées dépassant `TrustedSenders:RetentionDays`
(365 par défaut). Ce n'est pas ce qui borne la table : c'est le plafond de 1 000 lignes par
compte, appliqué par `TrustedSenderStore`.

## Script

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`trusted_senders` (
  `user_id`   CHAR(36)     NOT NULL,
  `address`   VARCHAR(320) NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `last_used` DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`user_id`, `address`),
  CONSTRAINT `fk_trusted_senders_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`trusted_senders` (
  `user_id`   CHAR(36)     NOT NULL,
  `address`   VARCHAR(320) NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `last_used` DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`user_id`, `address`),
  CONSTRAINT `fk_trusted_senders_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Aucun `GRANT` à rejouer : les utilisateurs `snoopy_webmail`/`snoopy_webmail_dev` ont déjà
`SELECT, INSERT, UPDATE, DELETE` sur toute la base.

**Pas de `DEFAULT CURRENT_TIMESTAMP` sur `last_used`**, pour la même raison que
`users.creation_date` n'en a pas : la valeur appartient au code, donc une lecture ne peut jamais
la déplacer.

## Vérification

```sql
SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'snoopy_webmail' AND TABLE_NAME = 'trusted_senders';
-- attendu : trusted_senders | utf8mb4_bin
```

## Désinstallation

```sql
DROP TABLE IF EXISTS `snoopy_webmail`.`trusted_senders`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`trusted_senders`;
```

La perdre remet chaque compte au blocage par message. Aucun message n'est concerné.
