# Prérequis base de données — tables CalDAV (agenda)

À rejouer **avant** le déploiement du backend, sur `snoopy_webmail` **et** `snoopy_webmail_dev`.
Création manuelle : ce projet n'utilise pas les migrations EF.

Les FK exigent que `users` existe déjà (voir `webmail-users-table.md`).

Aucun rattrapage ne suit, à l'inverse des tables CardDAV : les six tables naissent vides et rien
n'existait avant elles. L'ordre des `CREATE` compte en revanche, chaque table référençant la
précédente ; le bloc ci-dessous est déjà dans le bon ordre.

## Tranche 5a — les six tables

```sql
CREATE TABLE `calendars` (
  `id`           CHAR(36)     NOT NULL,
  `user_id`      CHAR(36)     NOT NULL,
  `dav_name`     VARCHAR(255) NOT NULL COLLATE utf8mb4_bin COMMENT 'Dernier segment de l''URL CalDAV ; fixé à la création, jamais renommé',
  `display_name` VARCHAR(255) NOT NULL COLLATE utf8mb4_unicode_ci,
  `description`  TEXT         NOT NULL COLLATE utf8mb4_unicode_ci,
  `color`        CHAR(7)      NOT NULL COMMENT '#RRGGBB ; le canal alpha d''Apple est retiré à l''écriture',
  `sort_order`   INT          NOT NULL DEFAULT 0 COMMENT 'Rang dans la barre latérale ; ORDER est un mot réservé, d''où sort_order',
  `time_zone`    VARCHAR(64)  NOT NULL COMMENT 'Identifiant IANA ; celui du navigateur à la création (décision 6)',
  `is_visible`   TINYINT(1)   NOT NULL DEFAULT 1 COMMENT 'Case de la barre latérale ; jamais projetée vers DAV',
  `created_at`   DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  `updated_at`   DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_calendars_user_dav_name` (`user_id`, `dav_name`),
  CONSTRAINT `fk_calendars_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `calendar_events` (
  `id`               CHAR(36)     NOT NULL,
  `calendar_id`      CHAR(36)     NOT NULL,
  `user_id`          CHAR(36)     NOT NULL COMMENT 'Redondant avec calendars.user_id : la fenêtre de l''API interroge tous les agendas d''un coup',
  `uid`              VARCHAR(255) NOT NULL COLLATE utf8mb4_bin COMMENT 'Unique par agenda, pas par utilisateur (RFC 4791 § 4.1)',
  `dav_name`         VARCHAR(255) NOT NULL COLLATE utf8mb4_bin,
  `summary`          VARCHAR(255) NULL COLLATE utf8mb4_unicode_ci,
  `location`         VARCHAR(255) NULL COLLATE utf8mb4_unicode_ci,
  `description`      TEXT         NULL COLLATE utf8mb4_unicode_ci,
  `starts_at`        DATETIME     NOT NULL COMMENT 'UTC ; une date sans heure ou une heure flottante est posée dans le fuseau de l''agenda',
  `ends_at`          DATETIME     NOT NULL,
  `is_all_day`       TINYINT(1)   NOT NULL DEFAULT 0,
  `time_zone`        VARCHAR(64)  NULL COMMENT 'IANA, UTC, ou NULL = flottant',
  `is_recurring`     TINYINT(1)   NOT NULL DEFAULT 0,
  `first_occurrence` DATETIME     NOT NULL,
  `last_occurrence`  DATETIME     NOT NULL COMMENT '2100-01-01 pour une règle sans fin (décision 1)',
  `status`           VARCHAR(16)  NULL,
  `transparency`     VARCHAR(16)  NOT NULL DEFAULT 'OPAQUE',
  `class`            VARCHAR(16)  NULL,
  `ics_raw`          MEDIUMTEXT   NOT NULL COMMENT 'La ressource CalDAV entière, souveraine ; les colonnes en sont un index',
  `ics_hash`         CHAR(64)     NOT NULL DEFAULT '' COMMENT 'SHA-256 hex de ics_raw ; base de l''ETag',
  `sync_sequence`    BIGINT UNSIGNED NOT NULL DEFAULT 0,
  `updated_at`       TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_calendar_events_uid` (`calendar_id`, `uid`),
  UNIQUE KEY `ux_calendar_events_dav_name` (`calendar_id`, `dav_name`),
  KEY `ix_calendar_events_window` (`user_id`, `first_occurrence`, `last_occurrence`),
  KEY `ix_calendar_events_seq` (`calendar_id`, `sync_sequence`),
  CONSTRAINT `fk_calendar_events_calendar` FOREIGN KEY (`calendar_id`) REFERENCES `calendars` (`id`) ON DELETE CASCADE,
  CONSTRAINT `fk_calendar_events_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `calendar_attendees` (
  `event_id`      CHAR(36)     NOT NULL,
  `position`      INT          NOT NULL,
  `recurrence_id` VARCHAR(64)  NULL COMMENT 'Valeur littérale du RECURRENCE-ID du composant d''origine ; NULL = le maître',
  `email`         VARCHAR(320) NOT NULL,
  `name`          VARCHAR(255) NULL COLLATE utf8mb4_unicode_ci,
  `role`          VARCHAR(32)  NULL,
  `partstat`      VARCHAR(32)  NULL,
  `is_organizer`  TINYINT(1)   NOT NULL DEFAULT 0,
  PRIMARY KEY (`event_id`, `position`),
  CONSTRAINT `fk_calendar_attendees_event` FOREIGN KEY (`event_id`) REFERENCES `calendar_events` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `calendar_sync_state` (
  `calendar_id`  CHAR(36)        NOT NULL,
  `epoch`        CHAR(36)        NOT NULL COMMENT 'GUID ; ne bouge que sur restauration',
  `seq`          BIGINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Compteur ; nommé seq car SEQUENCE est un mot-clé MariaDB depuis 10.3',
  `pruned_below` BIGINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Filigrane : un jeton strictement < cette valeur est irrécupérable (403 valid-sync-token) ; à cette valeur exacte il est encore lu',
  PRIMARY KEY (`calendar_id`),
  CONSTRAINT `fk_calendar_sync_state_calendar` FOREIGN KEY (`calendar_id`) REFERENCES `calendars` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `calendar_tombstones` (
  `calendar_id`   CHAR(36)        NOT NULL,
  `dav_name`      VARCHAR(255)    NOT NULL COLLATE utf8mb4_bin,
  `sync_sequence` BIGINT UNSIGNED NOT NULL,
  `deleted_at`    DATETIME        NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`calendar_id`, `dav_name`),
  KEY `ix_calendar_tombstones_seq` (`calendar_id`, `sync_sequence`),
  KEY `ix_calendar_tombstones_time` (`deleted_at`),
  CONSTRAINT `fk_calendar_tombstones_calendar` FOREIGN KEY (`calendar_id`) REFERENCES `calendars` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `calendar_revisions` (
  `id`          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id`     CHAR(36)     NOT NULL,
  `calendar_id` CHAR(36)     NULL COMMENT 'Sans FK : survit à l''agenda (décision 2)',
  `event_id`    CHAR(36)     NULL COMMENT 'Sans FK : survit à l''événement',
  `uid`         VARCHAR(255) NULL,
  `dav_name`    VARCHAR(255) NULL COLLATE utf8mb4_bin,
  `ics_hash`    CHAR(64)     NOT NULL,
  `ics_raw`     MEDIUMTEXT   NOT NULL COMMENT 'Les octets remplacés ou refusés — même type que calendar_events.ics_raw',
  `cause`       ENUM('put','webmail','import','delete','rejected') NOT NULL,
  `replaced_at` DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`id`),
  KEY `ix_calendar_revisions_user_time` (`user_id`, `replaced_at`),
  KEY `ix_calendar_revisions_time` (`replaced_at`),
  KEY `ix_calendar_revisions_uid` (`calendar_id`, `uid`),
  CONSTRAINT `fk_calendar_revisions_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Aucun `GRANT` à rejouer : les utilisateurs `snoopy_webmail`/`snoopy_webmail_dev` ont déjà
`SELECT, INSERT, UPDATE, DELETE` sur toute la base.

## Trois écarts par rapport au DDL du plan

Écrits ici plutôt que passés sous silence : ce sont les seules différences entre ce bloc et celui
de la conception, et un relecteur doit pouvoir les rejeter en connaissance de cause.

- **`sort_order` et non `order`.** `ORDER` est un mot réservé SQL : la colonne n'existerait
  qu'entre back-quotes, et le premier `SELECT ... ORDER BY order` écrit à la main rendrait une
  erreur de syntaxe. Le nom du champ C# reste `Order`, seule la colonne change.
- **Les six tables sont en `utf8mb4_bin`, et ce sont les colonnes de texte humain qui portent
  `utf8mb4_unicode_ci`** — l'inverse du plan, qui mettait trois tables en `utf8mb4_unicode_ci`.
  Ce n'est pas un goût : une FK exige que sa colonne et la colonne référencée aient **la même**
  collation, `users`.`id` est en `utf8mb4_bin` comme toutes les tables de cette base, et le plan
  n'imposait `utf8mb4_bin` qu'à `uid` et `dav_name`, jamais aux `CHAR(36)` porteurs de clé.
  Rejoué tel quel, `CREATE TABLE calendars` échouait sur `fk_calendars_user` et rien de la tranche
  n'existait. La forme retenue est exactement celle de `contacts` (voir
  `webmail-contacts-tables.md`, « Pourquoi la collation est mixte »).
- **`calendar_tombstones.deleted_at` et `calendar_revisions.replaced_at` sont des `DATETIME`, pas
  des `TIMESTAMP`.** Le code pose ces deux valeurs lui-même, en UTC ; une `TIMESTAMP` les ferait
  traverser le fuseau de la session, ce qu'explique déjà `webmail-carddav-tables.md`, et leurs
  jumelles `contact_tombstones.deleted_at` / `contact_revisions.replaced_at` sont des `DATETIME`
  pour cette raison. S'y ajoute un piège propre à MariaDB : la première colonne `TIMESTAMP` d'une
  table reçoit un `DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP` implicite, donc une
  tombe dont on avance le `sync_sequence` verrait sa date de suppression réécrite à l'instant de
  la mise à jour.

`calendar_events.updated_at` reste en revanche une `TIMESTAMP ... ON UPDATE CURRENT_TIMESTAMP`,
comme `contacts.updated_at` : c'est un témoin que le schéma doit suivre à **toute** écriture, et
l'ETag ne s'en déduit pas — il vient de `ics_hash`.

## Les « Pourquoi »

- **`user_id` est redondant sur `calendar_events`.** Il se déduirait de `calendars.user_id`, mais
  l'écran de l'agenda pose toujours la même question : « tous les événements de cet utilisateur
  entre le 1er et le 30 septembre », tous agendas confondus. Sans la colonne, chaque affichage
  d'un mois passe par une jointure ; avec elle, `ix_calendar_events_window` répond seul.
- **`uid` est unique par agenda, pas par utilisateur.** C'est la RFC 4791 § 4.1 : deux agendas
  peuvent légitimement contenir le même événement — une invitation acceptée et rangée deux fois —
  et l'unicité par utilisateur refuserait la seconde copie. C'est la différence avec `contacts`,
  où le carnet est unique et l'unicité par utilisateur correcte.
- **`dav_name` est NOT NULL ici, nullable sur `contacts`.** Là-bas la colonne est arrivée après
  coup sur des fiches déjà écrites, et l'unicité MySQL ignorant les `NULL`, elle pouvait rester
  vide en attendant le rattrapage. Ici aucune ligne ne préexiste : tout événement naît avec son
  nom de ressource, et laisser la colonne nullable n'autoriserait qu'une seule chose, l'oubli.
- **`last_occurrence` et sa date-butoir.** Une règle « tous les lundis, sans fin » n'a pas de
  dernière occurrence. Mettre `NULL` la ferait tomber hors de tout `BETWEEN`, donc disparaître de
  la vue mensuelle ; on écrit donc `2100-01-01` (décision 1). L'événement reste dans la plage,
  l'index reste un simple parcours d'intervalle, et personne n'a besoin de connaître la règle
  pour écrire la requête.
- **`seq` et non `sequence`.** `SEQUENCE` est un mot-clé MariaDB depuis 10.3, et une colonne qui
  n'existe qu'entre back-quotes est une erreur de production en attente, dans un projet où le SQL
  se passe à la main. Même raison que `sort_order`, même remède.
- **`calendar_revisions` ne porte aucune FK vers `calendars` ni `calendar_events`** (décision 2).
  Une révision est écrite précisément au moment où l'on efface ce qu'elle archive : une FK
  `ON DELETE CASCADE` supprimerait l'archive dans la foulée de la suppression qui vient de la
  créer, et une FK `RESTRICT` interdirait la suppression. Les deux colonnes restent renseignées
  tant que la cible existe et deviennent des identifiants orphelins ensuite — c'est voulu. Seule
  la FK vers `users` subsiste : un compte supprimé emporte son historique.
- **`calendar_sync_state` est par agenda, `contact_sync_state` par utilisateur.** CalDAV
  synchronise chaque collection séparément : chaque agenda a son jeton, donc son compteur et son
  epoch.

## Vérification

La collation est ce qui échoue réellement ici — c'est l'objet du deuxième écart ci-dessus — donc
elle se lit, elle ne se suppose pas. Le schéma est nommé plutôt que laissé à `DATABASE()`, qui
vaut NULL sur un client sans base sélectionnée et rend alors 0 ligne sans rien signaler.

```sql
SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'snoopy_webmail'
   AND TABLE_NAME IN ('calendars', 'calendar_events', 'calendar_attendees',
                      'calendar_sync_state', 'calendar_tombstones', 'calendar_revisions');
-- attendu : les six en utf8mb4_bin
```

Et les six FK, qui n'existent que si les collations s'accordent :

```sql
SELECT CONSTRAINT_NAME, TABLE_NAME, REFERENCED_TABLE_NAME
  FROM information_schema.REFERENTIAL_CONSTRAINTS
 WHERE CONSTRAINT_SCHEMA = 'snoopy_webmail'
   AND CONSTRAINT_NAME LIKE 'fk_calendar%';
-- attendu : 6 lignes -- calendars->users, calendar_events->calendars, calendar_events->users,
--           calendar_attendees->calendar_events, calendar_sync_state->calendars,
--           calendar_tombstones->calendars. Aucune depuis calendar_revisions sauf ->users.
```

### Prérequis avant d'ouvrir toute route `/caldav` — l'atomicité du compteur

Même propriété, même vérification manuelle que pour CardDAV : `seq` avance par un
`INSERT ... ON DUPLICATE KEY UPDATE seq = seq + 1` que ni le fournisseur InMemory ni SQLite ne
savent exécuter à l'identique, donc aucun test ne la couvre. La procédure à deux sessions `mysql`
est écrite dans `webmail-carddav-tables.md`, section « l'atomicité du compteur, vérifiée à la
main » ; ici la clé est `calendar_id` et non `user_id`. À rejouer avant d'ouvrir la première
route CalDAV.

## Désinstallation

L'ordre est l'inverse de la création, chaque table étant référencée par la suivante.

```sql
DROP TABLE IF EXISTS `snoopy_webmail`.`calendar_revisions`;
DROP TABLE IF EXISTS `snoopy_webmail`.`calendar_tombstones`;
DROP TABLE IF EXISTS `snoopy_webmail`.`calendar_sync_state`;
DROP TABLE IF EXISTS `snoopy_webmail`.`calendar_attendees`;
DROP TABLE IF EXISTS `snoopy_webmail`.`calendar_events`;
DROP TABLE IF EXISTS `snoopy_webmail`.`calendars`;

DROP TABLE IF EXISTS `snoopy_webmail_dev`.`calendar_revisions`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`calendar_tombstones`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`calendar_sync_state`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`calendar_attendees`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`calendar_events`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`calendars`;
```

La perte de ces six tables efface tous les agendas, tous les événements et tout l'historique :
rien n'en est reconstructible depuis une autre table, `ics_raw` étant la seule copie de la
ressource.
