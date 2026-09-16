-- =============================================================================
--  Scotty webmail - database installation
-- =============================================================================
--
--  Creates Scotty's database, the MySQL account the service uses, and the 26
--  tables. It adds no rows: every setting starts at its default value.
--
--  It does not touch the mail server's own `dovecot` database.
--
--  BEFORE RUNNING, replace the two placeholders:
--
--    __HOST__      the address the service connects FROM: 127.0.0.1 when the
--                  database and the service share a server
--    __PASSWORD__  a new password, made up for this; the settings file needs it
--
--  Then, as a database administrator (MySQL 8.0+ or MariaDB 10.5+):
--
--    mysql -u root -p < install/install.sql       (or: mariadb -u root -p ...)
--
--  For a NEW installation only. Run on a database that already holds these
--  tables, it stops with "Multiple primary key defined": nothing is damaged,
--  the database was simply installed already.
--
-- =============================================================================

SET NAMES utf8mb4;


-- -----------------------------------------------------------------------------
--  1. The database
-- -----------------------------------------------------------------------------
--  utf8mb4_bin throughout: IMAP folder paths are case-sensitive and must compare
--  byte for byte. A case-insensitive collation would treat 'Archive' and
--  'archive' as one folder when they are two. utf8mb4 rather than utf8 because
--  folder and contact names carry accents and may carry emoji.

CREATE DATABASE IF NOT EXISTS `scotty_webmail`
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_bin;

USE `scotty_webmail`;


-- -----------------------------------------------------------------------------
--  2. The service account
-- -----------------------------------------------------------------------------
--  Data rights only. No CREATE, DROP or ALTER: the service never migrates its
--  own schema, so it has no reason to be able to change it - nor to destroy it.

CREATE USER IF NOT EXISTS 'scotty_webmail'@'__HOST__'
  IDENTIFIED BY '__PASSWORD__';

GRANT SELECT, INSERT, UPDATE, DELETE
  ON `scotty_webmail`.*
  TO 'scotty_webmail'@'__HOST__';

FLUSH PRIVILEGES;


-- -----------------------------------------------------------------------------
--  3. Tables
-- -----------------------------------------------------------------------------
--  No foreign key points at the `dovecot` database. That is deliberate: a
--  cross-database constraint would recreate the coupling this separate database
--  exists to avoid. Purging a deleted account's rows is the application's job.

CREATE TABLE IF NOT EXISTS `app_settings` (
  `setting_key` varchar(64) NOT NULL COMMENT 'Dotted and stable, e.g. app.name',
  `setting_value` varchar(255) NOT NULL,
  `updated_at` datetime NOT NULL COMMENT 'UTC; set by the code, never by the schema'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `calendars` (
  `id` char(36) NOT NULL,
  `user_id` char(36) NOT NULL,
  `dav_name` varchar(255) NOT NULL COMMENT 'Last segment of the CalDAV URL; set at creation, never renamed',
  `display_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `color` char(7) NOT NULL COMMENT '#RRGGBB; Apple''s alpha channel is stripped on write',
  `sort_order` int NOT NULL DEFAULT 0 COMMENT 'Rank in the sidebar; ORDER is a reserved word, hence sort_order',
  `time_zone` varchar(64) NOT NULL COMMENT 'IANA identifier; the browser''s at creation (decision 6)',
  `is_visible` tinyint(1) NOT NULL DEFAULT 1 COMMENT 'Sidebar checkbox; never projected to DAV',
  `created_at` datetime NOT NULL COMMENT 'UTC; set by the code, never by the schema',
  `updated_at` datetime NOT NULL COMMENT 'UTC; set by the code, never by the schema'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `calendar_attendees` (
  `event_id` char(36) NOT NULL,
  `position` int NOT NULL,
  `recurrence_id` varchar(64) DEFAULT NULL COMMENT 'Literal RECURRENCE-ID value of the originating component; NULL = the master',
  `email` varchar(320) NOT NULL,
  `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `role` varchar(32) DEFAULT NULL,
  `partstat` varchar(32) DEFAULT NULL,
  `is_organizer` tinyint(1) NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `calendar_events` (
  `id` char(36) NOT NULL,
  `calendar_id` char(36) NOT NULL,
  `user_id` char(36) NOT NULL COMMENT 'Redundant with calendars.user_id: the API window queries every calendar at once',
  `uid` varchar(255) NOT NULL COMMENT 'Unique per calendar, not per user (RFC 4791 section 4.1)',
  `dav_name` varchar(255) NOT NULL,
  `summary` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `location` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `starts_at` datetime NOT NULL COMMENT 'UTC; a date without a time, or a floating time, is set in the calendar''s time zone',
  `ends_at` datetime NOT NULL,
  `is_all_day` tinyint(1) NOT NULL DEFAULT 0,
  `time_zone` varchar(64) DEFAULT NULL COMMENT 'IANA, UTC, or NULL = floating',
  `is_recurring` tinyint(1) NOT NULL DEFAULT 0,
  `first_occurrence` datetime NOT NULL,
  `last_occurrence` datetime NOT NULL COMMENT '2100-01-01 for an endless rule (decision 1)',
  `status` varchar(16) DEFAULT NULL,
  `transparency` varchar(16) NOT NULL DEFAULT 'OPAQUE',
  `class` varchar(16) DEFAULT NULL,
  `ics_raw` mediumtext NOT NULL COMMENT 'The whole CalDAV resource, authoritative; the columns are an index over it',
  `ics_hash` char(64) NOT NULL DEFAULT '' COMMENT 'SHA-256 hex of ics_raw; basis of the ETag',
  `sync_sequence` bigint UNSIGNED NOT NULL DEFAULT 0,
  `scheduling_owner` varchar(16) DEFAULT NULL,
  `scheduling_hash` char(64) DEFAULT NULL,
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `calendar_revisions` (
  `id` bigint UNSIGNED NOT NULL,
  `user_id` char(36) NOT NULL,
  `calendar_id` char(36) DEFAULT NULL COMMENT 'No FK: outlives the calendar (decision 2)',
  `event_id` char(36) DEFAULT NULL COMMENT 'No FK: outlives the event',
  `uid` varchar(255) DEFAULT NULL,
  `dav_name` varchar(255) DEFAULT NULL,
  `ics_hash` char(64) NOT NULL,
  `ics_raw` mediumtext NOT NULL COMMENT 'The bytes replaced or rejected - same type as calendar_events.ics_raw',
  `cause` enum('put','webmail','import','delete','rejected','scheduling','delivery') NOT NULL,
  `replaced_at` datetime NOT NULL COMMENT 'UTC; set by the code, never by the schema'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `calendar_sync_state` (
  `calendar_id` char(36) NOT NULL,
  `epoch` char(36) NOT NULL COMMENT 'GUID; only changes on a restore',
  `seq` bigint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Counter; named seq because SEQUENCE is a MariaDB keyword since 10.3',
  `pruned_below` bigint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Watermark: a token strictly < this value is unrecoverable (403 valid-sync-token); at exactly this value it is still read'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `calendar_tombstones` (
  `calendar_id` char(36) NOT NULL,
  `dav_name` varchar(255) NOT NULL,
  `sync_sequence` bigint UNSIGNED NOT NULL,
  `deleted_at` datetime NOT NULL COMMENT 'UTC; set by the code, never by the schema'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `connected_accounts` (
  `id` char(36) NOT NULL COMMENT 'GUID - the value of the X-Account-Id header',
  `user_id` char(36) NOT NULL,
  `domain_id` char(36) DEFAULT NULL COMMENT 'NULL = home server (local shared mailbox)',
  `email` varchar(255) NOT NULL COMMENT 'IMAP/SMTP/Sieve login, and the address of the default identity',
  `cipher` varbinary(8192) NOT NULL,
  `creation_date` datetime NOT NULL COMMENT 'UTC, set by the code',
  `auth_mode` varchar(16) NOT NULL DEFAULT 'Password'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `contacts` (
  `id` char(36) NOT NULL COMMENT 'GUID generated by the application',
  `user_id` char(36) NOT NULL,
  `uid` varchar(255) NOT NULL COMMENT 'Original vCard UID; = id when the source carried none',
  `first_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `last_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `nickname` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `display_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT 'The card''s FN property; guessed client-side until now',
  `middle_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `name_prefix` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `name_suffix` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `organization` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `department` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT 'Components 2..n of ORG, joined by ; as on the card',
  `job_title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `birthday` varchar(64) DEFAULT NULL COMMENT 'vCard form as-is: a partial date (--0315) or free text is valid',
  `website` varchar(512) DEFAULT NULL COMMENT 'First URL occurrence; the later ones stay in the card',
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `is_favorite` tinyint(1) NOT NULL DEFAULT 0,
  `source` enum('manual','captured','imported','carddav') NOT NULL DEFAULT 'manual' COMMENT 'Where the contact came from; written at creation only',
  `kind` enum('individual','group') NOT NULL DEFAULT 'individual' COMMENT 'Kind of card; group = KIND:group / X-ADDRESSBOOKSERVER-KIND:group',
  `vcard_raw` mediumtext DEFAULT NULL COMMENT 'Source vCard as-is; never served to the UI',
  `card_hash` char(64) NOT NULL DEFAULT '' COMMENT 'SHA-256 hex of vcard_raw; basis of the CardDAV ETag',
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `dav_name` varchar(255) DEFAULT NULL COMMENT 'The resource name the client chose; {id}.vcf for a contact born here',
  `sync_sequence` bigint UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = never backfilled, hence invisible to the protocol (a token asks for > n, n >= 0)'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `contact_addresses` (
  `contact_id` char(36) NOT NULL,
  `position` smallint UNSIGNED NOT NULL COMMENT 'Rank of the ADR in the card; the composer''s handle',
  `type` varchar(64) NOT NULL DEFAULT '',
  `pref` smallint UNSIGNED NOT NULL DEFAULT 101,
  `params` varchar(512) NOT NULL DEFAULT '' COMMENT 'Verbatim, LABEL included - the 4.0 formatted address can be long',
  `group_name` varchar(64) NOT NULL DEFAULT '',
  `po_box` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `extended` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `street` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `locality` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `region` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `postal_code` varchar(32) DEFAULT NULL,
  `country` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `contact_emails` (
  `contact_id` char(36) NOT NULL,
  `address` varchar(320) NOT NULL COMMENT 'Canonical lowercase form; 320 = RFC 5321 maximum',
  `position` smallint UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = primary address',
  `type` varchar(64) NOT NULL DEFAULT '' COMMENT 'TYPE extracted from params, for display; empty = no type',
  `pref` smallint UNSIGNED NOT NULL DEFAULT 101 COMMENT 'Normalised PREF (1..100); 101 = the card says nothing. Sort: (pref, position)',
  `params` varchar(255) NOT NULL DEFAULT '' COMMENT 'Verbatim parameter block (TYPE=WORK;PREF=1); display only, never re-emitted',
  `group_name` varchar(64) NOT NULL DEFAULT '' COMMENT 'The property''s group (item1.EMAIL); what an Apple X-ABLabel attaches to'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `contact_group_members` (
  `group_id` char(36) NOT NULL,
  `member_uid` varchar(255) NOT NULL COMMENT 'Member UID without its urn:uuid: prefix; not its id - a client may PUT the group before its members',
  `position` smallint UNSIGNED NOT NULL COMMENT 'Rank of the MEMBER in the card; a plain attribute'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `contact_phones` (
  `contact_id` char(36) NOT NULL,
  `position` smallint UNSIGNED NOT NULL COMMENT 'Rank of the TEL in the card; the composer''s handle',
  `number` varchar(64) NOT NULL COMMENT 'Exactly as the card carries it; no canonicalisation',
  `type` varchar(64) NOT NULL DEFAULT '',
  `pref` smallint UNSIGNED NOT NULL DEFAULT 101,
  `params` varchar(255) NOT NULL DEFAULT '',
  `group_name` varchar(64) NOT NULL DEFAULT ''
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `contact_photos` (
  `contact_id` char(36) NOT NULL,
  `media_type` varchar(64) NOT NULL,
  `bytes` mediumblob NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `contact_revisions` (
  `id` bigint UNSIGNED NOT NULL,
  `user_id` char(36) NOT NULL,
  `contact_id` char(36) DEFAULT NULL COMMENT 'The contact while it still exists; a delete revision outlives its own',
  `uid` varchar(255) DEFAULT NULL COMMENT 'UID of the archived card; NULL when a rejected body does not parse',
  `dav_name` varchar(255) DEFAULT NULL,
  `card_hash` char(64) NOT NULL,
  `vcard_raw` mediumtext NOT NULL COMMENT 'The bytes replaced or rejected - same type as contacts.vcard_raw',
  `cause` enum('put','webmail','import','delete','rejected') NOT NULL,
  `replaced_at` datetime NOT NULL COMMENT 'UTC; set by the code, never by the schema'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `contact_sync_state` (
  `user_id` char(36) NOT NULL,
  `epoch` char(36) NOT NULL COMMENT 'GUID; only changes on a restore - see carddav-restore-prerequisite.md',
  `seq` bigint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Counter; named seq because SEQUENCE is a MariaDB keyword since 10.3',
  `pruned_below` bigint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Watermark: a token strictly < this value is unrecoverable (403 valid-sync-token); at exactly this value it is still read'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `contact_tombstones` (
  `user_id` char(36) NOT NULL,
  `dav_name` varchar(255) NOT NULL,
  `sync_sequence` bigint UNSIGNED NOT NULL,
  `deleted_at` datetime NOT NULL COMMENT 'UTC; set by the code, never by the schema'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `dav_credentials` (
  `user_id` char(36) NOT NULL,
  `carddav_enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT 'One switch per protocol; CalDAV gets its own column, not a migration',
  `caldav_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT 'Born at 0: the row already exists for accounts that asked for nothing (5c)',
  `secret_hash` char(64) NOT NULL COMMENT 'Lowercase hex SHA-256 of (salt || secret UTF-8)',
  `salt` varbinary(16) NOT NULL,
  `created_at` datetime NOT NULL COMMENT 'UTC; set by the code, never by the schema',
  `last_used_at` datetime DEFAULT NULL COMMENT 'UTC; set by the code - coalesced to the hour service-side, rendered as a relative time'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `delivery_reply_key` (
  `id` tinyint UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Always 1: a single row',
  `key_hash` varbinary(32) NOT NULL COMMENT 'SHA-256 of the key - never the key',
  `created_at` datetime(6) NOT NULL COMMENT 'UTC',
  `last_call_at` datetime(6) DEFAULT NULL COMMENT 'UTC; last accepted call',
  `enabled` tinyint(1) NOT NULL DEFAULT 0,
  `updated_at` datetime(6) NOT NULL COMMENT 'UTC; set by the code'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `external_domains` (
  `id` char(36) NOT NULL COMMENT 'GUID',
  `name` varchar(100) NOT NULL COMMENT 'Display name ("Gmail")',
  `imap_host` varchar(255) NOT NULL,
  `imap_port` smallint UNSIGNED NOT NULL,
  `imap_security` varchar(16) NOT NULL COMMENT 'None | StartTls | SslOnConnect',
  `smtp_host` varchar(255) NOT NULL,
  `smtp_port` smallint UNSIGNED NOT NULL,
  `smtp_security` varchar(16) NOT NULL,
  `sieve_host` varchar(255) DEFAULT NULL COMMENT 'NULL = the domain does not support Sieve',
  `sieve_port` smallint UNSIGNED DEFAULT NULL,
  `creation_date` datetime NOT NULL COMMENT 'UTC, set by the code',
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `auth_mode` varchar(16) NOT NULL DEFAULT 'Password',
  `oauth_authorization_url` varchar(512) DEFAULT NULL,
  `oauth_token_url` varchar(512) DEFAULT NULL,
  `oauth_scopes` varchar(1024) DEFAULT NULL,
  `oauth_client_id` varchar(255) DEFAULT NULL,
  `oauth_client_secret` varbinary(1024) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `folder_role_overrides` (
  `user_id` char(36) NOT NULL,
  `role` varchar(16) NOT NULL,
  `folder_path` varchar(1024) NOT NULL,
  `uid_validity` bigint UNSIGNED NOT NULL,
  `mailbox_id` varchar(255) DEFAULT NULL,
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `account_id` varchar(36) NOT NULL DEFAULT '' COMMENT ''''' = main account, otherwise a connected_accounts GUID'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `scheduling_service_account` (
  `id` tinyint UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Always 1: a single row',
  `host` varchar(255) NOT NULL,
  `port` smallint UNSIGNED NOT NULL,
  `security` varchar(16) NOT NULL COMMENT 'None | StartTls | SslOnConnect',
  `login` varchar(320) NOT NULL,
  `password_cipher` varbinary(1024) NOT NULL COMMENT 'Protected by Data Protection - never plaintext',
  `last_test_at` datetime DEFAULT NULL COMMENT 'UTC; last test of the saved account',
  `last_test_ok` tinyint(1) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL COMMENT 'UTC; set by the code, never by the schema'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `sending_identities` (
  `user_id` char(36) NOT NULL,
  `address` varchar(320) NOT NULL COMMENT 'Canonical lowercase form; 320 = RFC 5321 maximum',
  `display_name` varchar(100) NOT NULL,
  `is_default` tinyint(1) NOT NULL DEFAULT 0,
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `account_id` varchar(36) NOT NULL DEFAULT '' COMMENT ''''' = main account, otherwise a connected_accounts GUID'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `trusted_senders` (
  `user_id` char(36) NOT NULL,
  `address` varchar(320) NOT NULL COMMENT 'Canonical lowercase form; 320 = RFC 5321 maximum',
  `last_used` datetime NOT NULL COMMENT 'UTC; set by the code, never by the schema'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `users` (
  `id` char(36) NOT NULL COMMENT 'GUID generated by the application at login',
  `email` varchar(255) NOT NULL COMMENT 'Canonical form (lowercase); primary mail identity',
  `security_stamp` char(36) NOT NULL COMMENT 'Rotates on every revocation; a JWT no longer carrying it is refused',
  `creation_date` datetime NOT NULL COMMENT 'Set on INSERT (UTC); never modified afterwards',
  `last_login_date` datetime DEFAULT NULL COMMENT 'Updated (UTC) on every login, not on every request',
  `kdf_salt` binary(16) DEFAULT NULL COMMENT 'PBKDF2 salt for the connected-accounts KEK; pre-filled by the migration, otherwise set by GetOrCreateKdfSaltAsync at login'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;


CREATE TABLE IF NOT EXISTS `user_preferences` (
  `user_id` char(36) NOT NULL,
  `preference_key` varchar(64) NOT NULL,
  `preference_value` varchar(255) NOT NULL,
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- -----------------------------------------------------------------------------
--  4. Keys and indexes
-- -----------------------------------------------------------------------------

ALTER TABLE `app_settings`
  ADD PRIMARY KEY (`setting_key`);

ALTER TABLE `calendars`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `ux_calendars_user_dav_name` (`user_id`,`dav_name`);

ALTER TABLE `calendar_attendees`
  ADD PRIMARY KEY (`event_id`,`position`);

ALTER TABLE `calendar_events`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `ux_calendar_events_uid` (`calendar_id`,`uid`),
  ADD UNIQUE KEY `ux_calendar_events_dav_name` (`calendar_id`,`dav_name`),
  ADD KEY `ix_calendar_events_window` (`user_id`,`first_occurrence`,`last_occurrence`),
  ADD KEY `ix_calendar_events_seq` (`calendar_id`,`sync_sequence`);

ALTER TABLE `calendar_revisions`
  ADD PRIMARY KEY (`id`),
  ADD KEY `ix_calendar_revisions_user_time` (`user_id`,`replaced_at`),
  ADD KEY `ix_calendar_revisions_time` (`replaced_at`),
  ADD KEY `ix_calendar_revisions_uid` (`calendar_id`,`uid`);

ALTER TABLE `calendar_sync_state`
  ADD PRIMARY KEY (`calendar_id`);

ALTER TABLE `calendar_tombstones`
  ADD PRIMARY KEY (`calendar_id`,`dav_name`),
  ADD KEY `ix_calendar_tombstones_seq` (`calendar_id`,`sync_sequence`),
  ADD KEY `ix_calendar_tombstones_time` (`deleted_at`);

ALTER TABLE `connected_accounts`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_connected_accounts_target` (`user_id`,`domain_id`,`email`),
  ADD KEY `fk_connected_accounts_domain` (`domain_id`);

ALTER TABLE `contacts`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_contacts_user_uid` (`user_id`,`uid`),
  ADD UNIQUE KEY `ux_contacts_dav_name` (`user_id`,`dav_name`),
  ADD KEY `ix_contacts_user` (`user_id`),
  ADD KEY `ix_contacts_sync_sequence` (`user_id`,`sync_sequence`);

ALTER TABLE `contact_addresses`
  ADD PRIMARY KEY (`contact_id`,`position`);

ALTER TABLE `contact_emails`
  ADD PRIMARY KEY (`contact_id`,`position`);

ALTER TABLE `contact_group_members`
  ADD PRIMARY KEY (`group_id`,`member_uid`),
  ADD KEY `ix_group_members_uid` (`member_uid`);

ALTER TABLE `contact_phones`
  ADD PRIMARY KEY (`contact_id`,`position`);

ALTER TABLE `contact_photos`
  ADD PRIMARY KEY (`contact_id`);

ALTER TABLE `contact_revisions`
  ADD PRIMARY KEY (`id`),
  ADD KEY `ix_contact_revisions_user_time` (`user_id`,`replaced_at`),
  ADD KEY `ix_contact_revisions_time` (`replaced_at`),
  ADD KEY `ix_contact_revisions_uid` (`user_id`,`uid`),
  ADD KEY `ix_contact_revisions_name` (`user_id`,`dav_name`);

ALTER TABLE `contact_sync_state`
  ADD PRIMARY KEY (`user_id`);

ALTER TABLE `contact_tombstones`
  ADD PRIMARY KEY (`user_id`,`dav_name`),
  ADD KEY `ix_contact_tombstones_seq` (`user_id`,`sync_sequence`),
  ADD KEY `ix_contact_tombstones_time` (`deleted_at`);

ALTER TABLE `dav_credentials`
  ADD PRIMARY KEY (`user_id`);

ALTER TABLE `delivery_reply_key`
  ADD PRIMARY KEY (`id`);

ALTER TABLE `external_domains`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_external_domains_name` (`name`);

ALTER TABLE `folder_role_overrides`
  ADD PRIMARY KEY (`user_id`,`account_id`,`role`);

ALTER TABLE `scheduling_service_account`
  ADD PRIMARY KEY (`id`);

ALTER TABLE `sending_identities`
  ADD PRIMARY KEY (`user_id`,`account_id`,`address`);

ALTER TABLE `trusted_senders`
  ADD PRIMARY KEY (`user_id`,`address`);

ALTER TABLE `users`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_users_email` (`email`);

ALTER TABLE `user_preferences`
  ADD PRIMARY KEY (`user_id`,`preference_key`);


--  Auto-increment

ALTER TABLE `calendar_revisions`
  MODIFY `id` bigint UNSIGNED NOT NULL AUTO_INCREMENT;

ALTER TABLE `contact_revisions`
  MODIFY `id` bigint UNSIGNED NOT NULL AUTO_INCREMENT;


--  Foreign keys

ALTER TABLE `calendars`
  ADD CONSTRAINT `fk_calendars_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `calendar_attendees`
  ADD CONSTRAINT `fk_calendar_attendees_event` FOREIGN KEY (`event_id`) REFERENCES `calendar_events` (`id`) ON DELETE CASCADE;

ALTER TABLE `calendar_events`
  ADD CONSTRAINT `fk_calendar_events_calendar` FOREIGN KEY (`calendar_id`) REFERENCES `calendars` (`id`) ON DELETE CASCADE,
  ADD CONSTRAINT `fk_calendar_events_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `calendar_revisions`
  ADD CONSTRAINT `fk_calendar_revisions_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `calendar_sync_state`
  ADD CONSTRAINT `fk_calendar_sync_state_calendar` FOREIGN KEY (`calendar_id`) REFERENCES `calendars` (`id`) ON DELETE CASCADE;

ALTER TABLE `calendar_tombstones`
  ADD CONSTRAINT `fk_calendar_tombstones_calendar` FOREIGN KEY (`calendar_id`) REFERENCES `calendars` (`id`) ON DELETE CASCADE;

ALTER TABLE `connected_accounts`
  ADD CONSTRAINT `fk_connected_accounts_domain` FOREIGN KEY (`domain_id`) REFERENCES `external_domains` (`id`),
  ADD CONSTRAINT `fk_connected_accounts_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `contacts`
  ADD CONSTRAINT `fk_contacts_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `contact_addresses`
  ADD CONSTRAINT `fk_contact_addresses_contact` FOREIGN KEY (`contact_id`) REFERENCES `contacts` (`id`) ON DELETE CASCADE;

ALTER TABLE `contact_emails`
  ADD CONSTRAINT `fk_contact_emails_contact` FOREIGN KEY (`contact_id`) REFERENCES `contacts` (`id`) ON DELETE CASCADE;

ALTER TABLE `contact_group_members`
  ADD CONSTRAINT `fk_group_members_group` FOREIGN KEY (`group_id`) REFERENCES `contacts` (`id`) ON DELETE CASCADE;

ALTER TABLE `contact_phones`
  ADD CONSTRAINT `fk_contact_phones_contact` FOREIGN KEY (`contact_id`) REFERENCES `contacts` (`id`) ON DELETE CASCADE;

ALTER TABLE `contact_photos`
  ADD CONSTRAINT `fk_contact_photos_contact` FOREIGN KEY (`contact_id`) REFERENCES `contacts` (`id`) ON DELETE CASCADE;

ALTER TABLE `contact_revisions`
  ADD CONSTRAINT `fk_contact_revisions_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `contact_sync_state`
  ADD CONSTRAINT `fk_contact_sync_state_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `contact_tombstones`
  ADD CONSTRAINT `fk_contact_tombstones_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `dav_credentials`
  ADD CONSTRAINT `fk_dav_credentials_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `folder_role_overrides`
  ADD CONSTRAINT `fk_folder_role_overrides_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `sending_identities`
  ADD CONSTRAINT `fk_sending_identities_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `trusted_senders`
  ADD CONSTRAINT `fk_trusted_senders_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

ALTER TABLE `user_preferences`
  ADD CONSTRAINT `fk_user_preferences_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

-- -----------------------------------------------------------------------------
-- -----------------------------------------------------------------------------
--  5. Verification
-- -----------------------------------------------------------------------------
-- -----------------------------------------------------------------------------

--  All 26 tables exist, every one of them utf8mb4_bin:
SELECT COUNT(*) AS tables_created,
       SUM(TABLE_COLLATION = 'utf8mb4_bin') AS correctly_collated
  FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'scotty_webmail';
-- expected: 26 | 26

--  The service account can read and write its data, and nothing else:
SHOW GRANTS FOR 'scotty_webmail'@'__HOST__';
-- expected: GRANT SELECT, INSERT, UPDATE, DELETE ON `scotty_webmail`.* - and nothing more

--  Next: put the password in the service's settings file (install/README.md, step 3).


-- -----------------------------------------------------------------------------
--  Uninstall
-- -----------------------------------------------------------------------------
--  DROP DATABASE IF EXISTS `scotty_webmail`;
--  DROP USER IF EXISTS 'scotty_webmail'@'__HOST__';
--  FLUSH PRIVILEGES;
