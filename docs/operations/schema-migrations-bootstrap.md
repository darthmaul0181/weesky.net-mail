# Migrations de schéma — mise en route d'une base existante

À jouer **une fois** sur chaque base déjà en service — prod et dev —, **avant** le premier
déploiement de l'API 1.3.0. Une base créée après cette version n'en a pas besoin : `install.sql`
crée les comptes, et le service crée ses tables.

Prérequis : les deux fiches historiques (`birthdays-calendar-migration.md`, `app-logo-migration.md`)
ont été appliquées sur cette base. La base a alors exactement le schéma de `0001_initial.sql`.

## 1. Le compte de schéma

En administrateur de la base, en adaptant l'hôte (celui du compte `scotty_webmail` existant), le
mot de passe et le nom de la base :

```sql
CREATE USER 'scotty_webmail_schema'@'<hôte>' IDENTIFIED BY '<mot de passe>';
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES
  ON `<base>`.* TO 'scotty_webmail_schema'@'<hôte>';
FLUSH PRIVILEGES;
```

## 2. Inscrire l'état actuel comme `0001`

La table que DbUp tient, dans la forme exacte où il la crée, avec la migration initiale marquée
comme appliquée (même contenu que
`src/scotty.microservice/scotty.microservice.Tests/Data/schema-migrations-bootstrap-journal.sql`, que
les tests jouent sur une base créée par l'ancien `install.sql`) :

```sql
USE `<base>`;

CREATE TABLE `schema_migrations` (
  `schemaversionid` int(11) NOT NULL AUTO_INCREMENT,
  `scriptname` varchar(255) NOT NULL,
  `applied` timestamp NOT NULL,
  PRIMARY KEY (`schemaversionid`)
) ENGINE=InnoDB;

INSERT INTO `schema_migrations` (`scriptname`, `applied`) VALUES ('0001_initial.sql', UTC_TIMESTAMP());
```

Vérifier : `SELECT scriptname FROM schema_migrations;` → une ligne, `0001_initial.sql`.

## 3. Le secret GitHub

GitHub › dépôt › Settings › Environments › `prod`, puis `dev` › secret
`WEBMAIL_SCHEMA_CONNECTION` :

```
Server=<hôte de la base>;Port=3306;Database=<base>;User=scotty_webmail_schema;Password=<mot de passe>;
```

`deploy.yml` le passe à `scotty.microservice migrate` avant chaque redémarrage. Le fichier de
réglages du serveur ne le reçoit pas.

## 4. Les droits `sudo` du compte de déploiement

Le job lance `sudo <chemin>/scotty.microservice migrate`. Si les droits `sudo` du compte de
déploiement sont limités à une liste de commandes, y ajouter, pour la prod et pour dev :

```
<DEPLOY_BASE_PATH>/scotty.microservice/scotty.microservice migrate
<DEPLOY_BASE_PATH>-dev/scotty.microservice/scotty.microservice migrate
```

## 5. Après le déploiement

Le log du job affiche `Schema is current.` ; le service redémarre comme avant.
