SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- Database: `dovecot`
--

-- --------------------------------------------------------

--
-- Table structure for table `aliases`
--

CREATE TABLE `aliases` (
  `id` int(11) NOT NULL,
  `source_addr` varchar(30) NOT NULL COMMENT 'the "local part" (the part before the ''@'' char) of the alias',
  `source_domain` char(3) NOT NULL DEFAULT 'WSY' COMMENT 'the domain of the alias',
  `destination_user` int(11) NOT NULL COMMENT 'the identifier of a known user in ''users'' table'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `domains`
--

CREATE TABLE `domains` (
  `id` char(3) NOT NULL,
  `name` varchar(30) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `domains_ownerships`
--

CREATE TABLE `domains_ownerships` (
  `domainId` varchar(3) NOT NULL,
  `userId` int(11) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `users`
--

CREATE TABLE `users` (
  `id` int(11) NOT NULL,
  `username` varchar(128) NOT NULL COMMENT 'the "local part" (the part before the ''@'' char)',
  `domain` char(3) NOT NULL DEFAULT 'WSY' COMMENT 'the domain in which the mailbox is stored',
  `password` varchar(128) NOT NULL COMMENT 'the user password',
  `quota_mb` int(11) NOT NULL DEFAULT 1024 COMMENT 'mailbox quota in mb (0=unlimited)',
  `fullname` varchar(100) NOT NULL COMMENT 'user''s fullname',
  `lastupdate` datetime NOT NULL COMMENT 'last password update',
  `active` enum('Y','N') DEFAULT 'Y' COMMENT 'indicates whether the user is active or not',
  `admin` enum('Y','N') NOT NULL DEFAULT 'N' COMMENT 'Indicate whether the user is an administrator or no'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Triggers `users`
--
DELIMITER $$
CREATE TRIGGER `INSERT_PASSWORD` BEFORE INSERT ON `users` FOR EACH ROW SET NEW.password = ENCRYPT(NEW.password, CONCAT('$6$', SUBSTRING(SHA(RAND()), -16)))
$$
DELIMITER ;
DELIMITER $$
CREATE TRIGGER `UPDATE_PASSWORD` BEFORE UPDATE ON `users` FOR EACH ROW IF NEW.password <> OLD.password THEN
	SET NEW.password = ENCRYPT(NEW.password, CONCAT('$6$', SUBSTRING(SHA(RAND()), -16)));
    SET NEW.lastupdate = now();
END IF
$$
DELIMITER ;

--
-- Indexes for dumped tables
--

--
-- Indexes for table `aliases`
--
ALTER TABLE `aliases`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `source_addr_2` (`source_addr`,`source_domain`),
  ADD KEY `source_addr` (`source_addr`),
  ADD KEY `destination_user` (`destination_user`),
  ADD KEY `source_domain_foreign_key` (`source_domain`);

--
-- Indexes for table `domains`
--
ALTER TABLE `domains`
  ADD PRIMARY KEY (`id`);

--
-- Indexes for table `domains_ownerships`
--
ALTER TABLE `domains_ownerships`
  ADD PRIMARY KEY (`domainId`,`userId`),
  ADD KEY `fk_ownership_user` (`userId`);

--
-- Indexes for table `users`
--
ALTER TABLE `users`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `username` (`username`,`domain`),
  ADD KEY `users_domain_foreign_key` (`domain`);

--
-- AUTO_INCREMENT for dumped tables
--

--
-- AUTO_INCREMENT for table `aliases`
--
ALTER TABLE `aliases`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `users`
--
ALTER TABLE `users`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- Constraints for dumped tables
--

--
-- Constraints for table `aliases`
--
ALTER TABLE `aliases`
  ADD CONSTRAINT `desination_user_foreign_key` FOREIGN KEY (`destination_user`) REFERENCES `users` (`id`) ON DELETE CASCADE,
  ADD CONSTRAINT `source_domain_foreign_key` FOREIGN KEY (`source_domain`) REFERENCES `domains` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `domains_ownerships`
--
ALTER TABLE `domains_ownerships`
  ADD CONSTRAINT `fk_ownership_domain` FOREIGN KEY (`domainId`) REFERENCES `domains` (`id`) ON DELETE CASCADE,
  ADD CONSTRAINT `fk_ownership_user` FOREIGN KEY (`userId`) REFERENCES `users` (`id`) ON DELETE CASCADE,
  ADD CONSTRAINT `user_id` FOREIGN KEY (`userId`) REFERENCES `users` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `users`
--
ALTER TABLE `users`
  ADD CONSTRAINT `users_domain_foreign_key` FOREIGN KEY (`domain`) REFERENCES `domains` (`id`);
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
