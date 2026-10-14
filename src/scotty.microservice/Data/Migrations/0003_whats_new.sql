-- What's new: one row per account and novelty, and the version each existing account is signalled from.
-- Both statements run again safely after a failure: the table is created only if absent, rows already there are kept.
CREATE TABLE IF NOT EXISTS `whats_new_items` (
  `user_id` char(36) NOT NULL,
  `item_id` varchar(64) NOT NULL,
  `first_shown_at` datetime DEFAULT NULL COMMENT 'UTC; the 30-day expiry counts from here',
  `listed_at` datetime DEFAULT NULL COMMENT 'UTC',
  `seen_at` datetime DEFAULT NULL COMMENT 'UTC',
  PRIMARY KEY (`user_id`, `item_id`),
  CONSTRAINT `fk_whats_new_items_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- Accounts that predate the feature see the whole catalogue; later ones get their version at first load.
INSERT IGNORE INTO `user_preferences` (`user_id`, `preference_key`, `preference_value`)
  SELECT `id`, 'whatsNew.baseline', '0.0.0' FROM `users`;
