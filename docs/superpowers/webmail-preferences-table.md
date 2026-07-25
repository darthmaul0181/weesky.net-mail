# Prérequis serveur — table `user_preferences`

**À appliquer sur les deux bases** (`snoopy_webmail` et `snoopy_webmail_dev`) avant de déployer
le backend qui expose `/api/Preferences`.

Le projet n'utilise pas les migrations EF : la création des tables est manuelle, comme pour
`folder_role_overrides` (voir `mail-2a5-database-prerequisite.md`).

## Pourquoi une table clé/valeur

Une colonne par option obligerait à un `ALTER TABLE` **joué à la main sur le serveur** à chaque
nouveau réglage — et il y en aura d'autres, dans ce nouvel onglet comme ailleurs. Ici, ajouter
une préférence est un changement de code seul : une entrée dans `Models/UserPreferences.cs`.

Le prix assumé : la base ne peut vérifier ni la clé ni la valeur. C'est le registre qui le fait,
et lui seul — une clé inconnue est refusée en 400 avant d'atteindre la table, et une ligne dont
la valeur n'est plus acceptée retombe sur le défaut au lieu d'être servie au client.

**L'absence de ligne vaut défaut.** Un compte qui n'a jamais ouvert les réglages n'a aucune
ligne, et les valeurs par défaut vivent dans le code, pas dans le schéma.

## Script

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`user_preferences` (
  `account_id`       VARCHAR(255) NOT NULL
                     COMMENT 'Forme canonique : minuscules, sans espaces',
  `preference_key`   VARCHAR(64)  NOT NULL
                     COMMENT 'Pointé et stable, ex. mail.pageSize — jamais localisé',
  `preference_value` VARCHAR(255) NOT NULL
                     COMMENT 'Toujours une chaîne ; le registre sait la relire',
  `updated_at`       TIMESTAMP    NOT NULL
                     DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`account_id`, `preference_key`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_bin;

-- Idem sur la base de développement.
CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`user_preferences` (
  `account_id`       VARCHAR(255) NOT NULL,
  `preference_key`   VARCHAR(64)  NOT NULL,
  `preference_value` VARCHAR(255) NOT NULL,
  `updated_at`       TIMESTAMP    NOT NULL
                     DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`account_id`, `preference_key`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_bin;
```

Aucun `GRANT` à rejouer : les utilisateurs `snoopy_webmail` et `snoopy_webmail_dev` ont déjà
`SELECT, INSERT, UPDATE, DELETE` sur toute la base.

**Pas de contrainte `CHECK` sur `preference_key`** — elle annulerait le bénéfice, chaque nouvelle
clé redevenant un `ALTER`. C'est le registre qui tient ce rôle.

## Vérification

```sql
SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'snoopy_webmail' AND TABLE_NAME = 'user_preferences';
-- attendu : user_preferences | utf8mb4_bin
```

## Désinstallation

```sql
DROP TABLE IF EXISTS `snoopy_webmail`.`user_preferences`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`user_preferences`;
```

La perdre fait retomber chaque compte sur les valeurs par défaut. Aucun message n'est concerné.
