-- The journal DbUp keeps, as it creates it, holding the initial schema as already applied.
CREATE TABLE `schema_migrations` (
  `schemaversionid` int(11) NOT NULL AUTO_INCREMENT,
  `scriptname` varchar(255) NOT NULL,
  `applied` timestamp NOT NULL,
  PRIMARY KEY (`schemaversionid`)
) ENGINE=InnoDB;

INSERT INTO `schema_migrations` (`scriptname`, `applied`) VALUES ('0001_initial.sql', UTC_TIMESTAMP());
