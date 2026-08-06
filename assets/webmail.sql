-- phpMyAdmin SQL Dump
-- version 5.2.2
-- https://www.phpmyadmin.net/
--
-- Host: saturn.weesky.net:3306
-- Generation Time: Aug 06, 2026 at 06:34 PM
-- Server version: 12.3.2-MariaDB-deb13
-- PHP Version: 8.3.33

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- Database: `snoopy_webmail`
--

-- --------------------------------------------------------

--
-- Table structure for table `app_settings`
--

CREATE TABLE `app_settings` (
  `setting_key` varchar(64) NOT NULL COMMENT 'Pointée et stable, p. ex. app.name',
  `setting_value` varchar(255) NOT NULL,
  `updated_at` datetime NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- --------------------------------------------------------

--
-- Table structure for table `connected_accounts`
--

CREATE TABLE `connected_accounts` (
  `id` char(36) NOT NULL COMMENT 'GUID — la valeur du header X-Account-Id',
  `user_id` char(36) NOT NULL,
  `domain_id` char(36) DEFAULT NULL COMMENT 'NULL = serveur maison (boîte partagée locale)',
  `email` varchar(255) NOT NULL COMMENT 'Login IMAP/SMTP/Sieve et adresse de l''identité par défaut',
  `cipher` varbinary(8192) NOT NULL,
  `creation_date` datetime NOT NULL COMMENT 'UTC, posée par le code',
  `auth_mode` varchar(16) NOT NULL DEFAULT 'Password'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- --------------------------------------------------------

--
-- Table structure for table `contacts`
--

CREATE TABLE `contacts` (
  `id` char(36) NOT NULL COMMENT 'GUID généré côté application',
  `user_id` char(36) NOT NULL,
  `uid` varchar(255) NOT NULL COMMENT 'UID vCard d''origine ; = id quand la source n''en portait pas',
  `first_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `last_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `nickname` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `is_favorite` tinyint(1) NOT NULL DEFAULT 0,
  `source` enum('manual','captured','imported') NOT NULL DEFAULT 'manual' COMMENT 'Origine de la fiche ; écrite à la création seulement',
  `vcard_raw` mediumtext DEFAULT NULL COMMENT 'vCard source tel quel ; jamais servi à l''UI',
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- --------------------------------------------------------

--
-- Table structure for table `contact_emails`
--

CREATE TABLE `contact_emails` (
  `contact_id` char(36) NOT NULL,
  `address` varchar(320) NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `position` smallint(5) UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = adresse principale'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- --------------------------------------------------------

--
-- Table structure for table `external_domains`
--

CREATE TABLE `external_domains` (
  `id` char(36) NOT NULL COMMENT 'GUID',
  `name` varchar(100) NOT NULL COMMENT 'Nom d''affichage (« Gmail »)',
  `imap_host` varchar(255) NOT NULL,
  `imap_port` smallint(5) UNSIGNED NOT NULL,
  `imap_security` varchar(16) NOT NULL COMMENT 'None | StartTls | SslOnConnect',
  `smtp_host` varchar(255) NOT NULL,
  `smtp_port` smallint(5) UNSIGNED NOT NULL,
  `smtp_security` varchar(16) NOT NULL,
  `sieve_host` varchar(255) DEFAULT NULL COMMENT 'NULL = le domaine ne supporte pas Sieve',
  `sieve_port` smallint(5) UNSIGNED DEFAULT NULL,
  `creation_date` datetime NOT NULL COMMENT 'UTC, posée par le code',
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `auth_mode` varchar(16) NOT NULL DEFAULT 'Password',
  `oauth_authorization_url` varchar(512) DEFAULT NULL,
  `oauth_token_url` varchar(512) DEFAULT NULL,
  `oauth_scopes` varchar(1024) DEFAULT NULL,
  `oauth_client_id` varchar(255) DEFAULT NULL,
  `oauth_client_secret` varbinary(1024) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- --------------------------------------------------------

--
-- Table structure for table `folder_role_overrides`
--

CREATE TABLE `folder_role_overrides` (
  `user_id` char(36) NOT NULL,
  `role` varchar(16) NOT NULL,
  `folder_path` varchar(1024) NOT NULL,
  `uid_validity` bigint(20) UNSIGNED NOT NULL,
  `mailbox_id` varchar(255) DEFAULT NULL,
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `account_id` varchar(36) NOT NULL DEFAULT '' COMMENT ''''' = compte principal, sinon GUID connected_accounts'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- --------------------------------------------------------

--
-- Table structure for table `sending_identities`
--

CREATE TABLE `sending_identities` (
  `user_id` char(36) NOT NULL,
  `address` varchar(320) NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `display_name` varchar(100) NOT NULL,
  `is_default` tinyint(1) NOT NULL DEFAULT 0,
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `account_id` varchar(36) NOT NULL DEFAULT '' COMMENT ''''' = compte principal, sinon GUID connected_accounts'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- --------------------------------------------------------

--
-- Table structure for table `trusted_senders`
--

CREATE TABLE `trusted_senders` (
  `user_id` char(36) NOT NULL,
  `address` varchar(320) NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `last_used` datetime NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- --------------------------------------------------------

--
-- Table structure for table `users`
--

CREATE TABLE `users` (
  `id` char(36) NOT NULL COMMENT 'GUID généré côté application au login',
  `email` varchar(255) NOT NULL COMMENT 'Forme canonique (minuscules) ; identité mail principale',
  `security_stamp` char(36) NOT NULL COMMENT 'Tourne à chaque révocation ; un JWT qui ne le porte plus est refusé',
  `creation_date` datetime NOT NULL COMMENT 'Posée à l''INSERT (UTC) ; jamais modifiée ensuite',
  `last_login_date` datetime DEFAULT NULL COMMENT 'Mise à jour (UTC) à chaque login, pas à chaque requête',
  `kdf_salt` binary(16) DEFAULT NULL COMMENT 'Sel PBKDF2 du KEK des comptes connectés ; pré-rempli par la migration, sinon posé par GetOrCreateKdfSaltAsync au login'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- --------------------------------------------------------

--
-- Table structure for table `user_preferences`
--

CREATE TABLE `user_preferences` (
  `user_id` char(36) NOT NULL,
  `preference_key` varchar(64) NOT NULL,
  `preference_value` varchar(255) NOT NULL,
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

--
-- Indexes for dumped tables
--

--
-- Indexes for table `app_settings`
--
ALTER TABLE `app_settings`
  ADD PRIMARY KEY (`setting_key`);

--
-- Indexes for table `connected_accounts`
--
ALTER TABLE `connected_accounts`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_connected_accounts_target` (`user_id`,`domain_id`,`email`),
  ADD KEY `fk_connected_accounts_domain` (`domain_id`);

--
-- Indexes for table `contacts`
--
ALTER TABLE `contacts`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_contacts_user_uid` (`user_id`,`uid`),
  ADD KEY `ix_contacts_user` (`user_id`);

--
-- Indexes for table `contact_emails`
--
ALTER TABLE `contact_emails`
  ADD PRIMARY KEY (`contact_id`,`address`);

--
-- Indexes for table `external_domains`
--
ALTER TABLE `external_domains`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_external_domains_name` (`name`);

--
-- Indexes for table `folder_role_overrides`
--
ALTER TABLE `folder_role_overrides`
  ADD PRIMARY KEY (`user_id`,`account_id`,`role`);

--
-- Indexes for table `sending_identities`
--
ALTER TABLE `sending_identities`
  ADD PRIMARY KEY (`user_id`,`account_id`,`address`);

--
-- Indexes for table `trusted_senders`
--
ALTER TABLE `trusted_senders`
  ADD PRIMARY KEY (`user_id`,`address`);

--
-- Indexes for table `users`
--
ALTER TABLE `users`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_users_email` (`email`);

--
-- Indexes for table `user_preferences`
--
ALTER TABLE `user_preferences`
  ADD PRIMARY KEY (`user_id`,`preference_key`);

--
-- Constraints for dumped tables
--

--
-- Constraints for table `connected_accounts`
--
ALTER TABLE `connected_accounts`
  ADD CONSTRAINT `fk_connected_accounts_domain` FOREIGN KEY (`domain_id`) REFERENCES `external_domains` (`id`),
  ADD CONSTRAINT `fk_connected_accounts_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `contacts`
--
ALTER TABLE `contacts`
  ADD CONSTRAINT `fk_contacts_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `contact_emails`
--
ALTER TABLE `contact_emails`
  ADD CONSTRAINT `fk_contact_emails_contact` FOREIGN KEY (`contact_id`) REFERENCES `contacts` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `folder_role_overrides`
--
ALTER TABLE `folder_role_overrides`
  ADD CONSTRAINT `fk_folder_role_overrides_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `sending_identities`
--
ALTER TABLE `sending_identities`
  ADD CONSTRAINT `fk_sending_identities_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `trusted_senders`
--
ALTER TABLE `trusted_senders`
  ADD CONSTRAINT `fk_trusted_senders_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `user_preferences`
--
ALTER TABLE `user_preferences`
  ADD CONSTRAINT `fk_user_preferences_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE;
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
