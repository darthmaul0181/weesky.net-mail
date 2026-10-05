-- =============================================================================
--  Scotty webmail - database installation
-- =============================================================================
--
--  Creates Scotty's database and the two MySQL accounts the service uses. It
--  creates no table: the service creates and updates its tables itself, on
--  its first start and after every update (install/README.md, step 3).
--
--  It does not touch the mail server's own `dovecot` database.
--
--  BEFORE RUNNING, replace the three placeholders:
--
--    __HOST__             the address the service connects FROM: 127.0.0.1
--                         when the database and the service share a server
--    __PASSWORD__         a new password for the service's account
--    __SCHEMA_PASSWORD__  a second new password, for the account that
--                         creates the tables
--
--  Then, as a database administrator (MySQL 8.0+ or MariaDB 10.5+):
--
--    mysql -u root -p < install/install.sql       (or: mariadb -u root -p ...)
--
--  Running it again changes nothing: the database and the accounts are only
--  created when they do not exist yet.
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


-- -----------------------------------------------------------------------------
--  2. The two accounts
-- -----------------------------------------------------------------------------
--  Every request the service answers goes through the first account, which can
--  read and write data and nothing else: a fault in a query can never change
--  or destroy the tables themselves. The second account creates and changes
--  the tables; the service uses it at start, then lets it go.

CREATE USER IF NOT EXISTS 'scotty_webmail'@'__HOST__'
  IDENTIFIED BY '__PASSWORD__';

GRANT SELECT, INSERT, UPDATE, DELETE
  ON `scotty_webmail`.*
  TO 'scotty_webmail'@'__HOST__';

CREATE USER IF NOT EXISTS 'scotty_webmail_schema'@'__HOST__'
  IDENTIFIED BY '__SCHEMA_PASSWORD__';

GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES
  ON `scotty_webmail`.*
  TO 'scotty_webmail_schema'@'__HOST__';

FLUSH PRIVILEGES;


-- -----------------------------------------------------------------------------
--  3. Verification
-- -----------------------------------------------------------------------------

SHOW GRANTS FOR 'scotty_webmail'@'__HOST__';
-- expected: GRANT SELECT, INSERT, UPDATE, DELETE ON `scotty_webmail`.*

SHOW GRANTS FOR 'scotty_webmail_schema'@'__HOST__';
-- expected: the same, plus CREATE, DROP, REFERENCES, INDEX, ALTER

--  Next: put both passwords in the service's settings file (install/README.md, step 3).


-- -----------------------------------------------------------------------------
--  Uninstall
-- -----------------------------------------------------------------------------
--  DROP DATABASE IF EXISTS `scotty_webmail`;
--  DROP USER IF EXISTS 'scotty_webmail'@'__HOST__';
--  DROP USER IF EXISTS 'scotty_webmail_schema'@'__HOST__';
--  FLUSH PRIVILEGES;
