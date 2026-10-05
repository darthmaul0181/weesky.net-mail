-- =============================================================================
--  Scotty webmail - adopt a database installed before version 1.3.0
-- =============================================================================
--
--  Before 1.3.0, install.sql created the tables itself. This marks them as the
--  first schema migration, so the service does not try to create them again.
--  Run it once, on that database only, as a database administrator:
--
--    mysql -u root -p scotty_webmail < install/adopt-existing-database.sql
--
--  Running it again changes nothing.
--
-- =============================================================================

CREATE TABLE IF NOT EXISTS `schema_migrations` (
  `schemaversionid` int(11) NOT NULL AUTO_INCREMENT,
  `scriptname` varchar(255) NOT NULL,
  `applied` timestamp NOT NULL,
  PRIMARY KEY (`schemaversionid`)
) ENGINE=InnoDB;

INSERT INTO `schema_migrations` (`scriptname`, `applied`)
  SELECT '0001_initial.sql', UTC_TIMESTAMP() FROM DUAL
   WHERE NOT EXISTS (SELECT 1 FROM `schema_migrations` WHERE `scriptname` = '0001_initial.sql');
