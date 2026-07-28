# Prérequis serveur — table `app_settings`

**À appliquer sur les deux bases** (`snoopy_webmail` et `snoopy_webmail_dev`) avant de déployer
le backend qui expose `/api/AppSettings`.

Le projet n'utilise pas les migrations EF : la création des tables est manuelle, comme pour
`trusted_senders` (voir `webmail-trusted-senders-table.md`).

## Pourquoi cette table

Les réglages de l'instance, pas d'un compte : elle ne porte donc **pas** de `user_id` et aucune
clé étrangère vers `users`. Aujourd'hui trois lignes au plus — l'activation de l'installation en
application et les deux noms qu'affiche le manifest.

Une clé absente signifie que le défaut du registre (`Models/AppSettings.cs`) s'applique, donc une
instance qui n'a jamais ouvert l'onglet Administration n'a aucune ligne.

## Script

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`app_settings` (
  `setting_key`   VARCHAR(64)  NOT NULL COMMENT 'Pointée et stable, p. ex. app.name',
  `setting_value` VARCHAR(255) NOT NULL,
  `updated_at`    DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`setting_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`app_settings` (
  `setting_key`   VARCHAR(64)  NOT NULL COMMENT 'Pointée et stable, p. ex. app.name',
  `setting_value` VARCHAR(255) NOT NULL,
  `updated_at`    DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`setting_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```
