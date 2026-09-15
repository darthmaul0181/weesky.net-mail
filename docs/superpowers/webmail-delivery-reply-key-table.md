# Prérequis serveur — table `delivery_reply_key`

**À appliquer sur les deux bases** (`snoopy_webmail` et `snoopy_webmail_dev`) avant de déployer
le backend qui expose `/api/DeliveryReplyKey` et `/api/Delivery/CalendarReplies`
(spec 5e3). Le projet n'utilise pas les migrations EF : la création est manuelle, comme pour
`scheduling_service_account`.

## Pourquoi cette table

La clé que le serveur mail présente pour faire appliquer une réponse d'invité à la livraison, et
le réglage qui active ce mécanisme. Une seule ligne (`id = 1`). Pas dans `app_settings`, qui se
lit sans connexion : le réglage lui-même dirait à tout Internet que la porte est ouverte.

- **`key_hash`** : SHA-256 de la clé, jamais la clé. Une fuite de la base n'ouvre pas la porte.
- **`last_call_at`** : dernier appel dont la clé était valide ; remis à `NULL` à chaque génération.
- **`enabled`** : le réglage. Désactivé, la porte répond 404 à tout le monde.
- **`updated_at`** en `DATETIME(6)` : le jeton de concurrence des écritures de l'écran ;
  `last_call_at` s'écrit sans le faire bouger.

## Script

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`delivery_reply_key` (
  `id`           TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Toujours 1 : une seule ligne',
  `key_hash`     VARBINARY(32)    NOT NULL COMMENT 'SHA-256 de la clé — jamais la clé',
  `created_at`   DATETIME(6)      NOT NULL COMMENT 'UTC',
  `last_call_at` DATETIME(6)      NULL     COMMENT 'UTC ; dernier appel accepté',
  `enabled`      TINYINT(1)       NOT NULL DEFAULT 0,
  `updated_at`   DATETIME(6)      NOT NULL COMMENT 'UTC ; posée par le code',
  PRIMARY KEY (`id`),
  CONSTRAINT `ck_delivery_reply_key_single` CHECK (`id` = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`delivery_reply_key` (
  `id`           TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Toujours 1 : une seule ligne',
  `key_hash`     VARBINARY(32)    NOT NULL COMMENT 'SHA-256 de la clé — jamais la clé',
  `created_at`   DATETIME(6)      NOT NULL COMMENT 'UTC',
  `last_call_at` DATETIME(6)      NULL     COMMENT 'UTC ; dernier appel accepté',
  `enabled`      TINYINT(1)       NOT NULL DEFAULT 0,
  `updated_at`   DATETIME(6)      NOT NULL COMMENT 'UTC ; posée par le code',
  PRIMARY KEY (`id`),
  CONSTRAINT `ck_delivery_reply_key_single` CHECK (`id` = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Passer aussi l'`ALTER` de `calendar_revisions` du § « Tranche 5e3 » de `webmail-calendar-tables.md`.

## Vérification

1. `SHOW CREATE TABLE delivery_reply_key;` montre les six colonnes et la contrainte.
2. Administration > Application montre la section « Réponses aux invitations à la livraison »,
   sans clé, l'interrupteur grisé.
3. « Générer une clé », puis `SELECT id, created_at, last_call_at, enabled FROM delivery_reply_key;`
   rend une ligne, `enabled = 0`.

## Retour arrière

La table peut rester : la version précédente ne la lit pas. La règle Sieve peut rester aussi,
ses appels répondent 404. Un `DROP TABLE` oblige à régénérer la clé et à la recopier sur le
serveur mail.
