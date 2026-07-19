# Prérequis serveur — base des préférences webmail (tranche 2a.5)

**À appliquer avant tout déploiement de la tranche 2a.5**, sur les deux environnements.

Le projet n'utilise pas les migrations EF : `ApplicationDbContext` mappe un schéma existant,
géré hors EF. La création de la base et de la table est donc manuelle, comme
`StateDirectory=` l'était pour le key ring.

---

## 1. Pourquoi une base à part

La base `dovecot` appartient à Dovecot. Elle peut être reconstruite par le provisionnement du
serveur mail, et nos préférences utilisateur partiraient avec ; les politiques de sauvegarde et
de rétention diffèrent également. `last_login`, déjà présente, est une table du **plugin
Dovecot** — ce n'est pas un précédent pour y poser les nôtres.

**Deux bases distinctes, une par environnement.** Le développement déploie la branche `webmail`
en continu ; une écriture douteuse ou un essai de schéma ne doit jamais atteindre les
préférences de production.

**Un utilisateur MySQL dédié**, distinct de celui qui lit `dovecot`. Si l'un des deux jeux
d'identifiants fuit ou tourne, l'autre n'est pas concerné.

---

## 2. Script

À exécuter en tant qu'administrateur MySQL. Le script est **idempotent** : le rejouer ne casse
rien.

Avant de l'exécuter, remplace les trois valeurs suivantes :

| Marqueur | Valeur à mettre |
|---|---|
| `__HOST__` | l'hôte depuis lequel le service se connecte — `localhost` si le service et MySQL sont sur la même machine, sinon l'IP du service (par ex. `10.0.0.%`) |
| `__PASSWORD_PROD__` | un mot de passe généré, jamais réutilisé |
| `__PASSWORD_DEV__` | un autre mot de passe généré, différent du précédent |

```sql
-- ============================================================================
--  Webmail weesky — base des préférences utilisateur
--  Tranche 2a.5 : affectation des dossiers systèmes
-- ============================================================================

-- ---------------------------------------------------------------------------
--  PRODUCTION
-- ---------------------------------------------------------------------------

CREATE DATABASE IF NOT EXISTS `snoopy_webmail`
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_bin;

-- utf8mb4_bin sur toute la table : les chemins IMAP sont sensibles à la casse
-- et doivent se comparer octet à octet. Une collation insensible ferait
-- correspondre 'Archive' et 'archive', qui sont deux dossiers différents.
-- utf8mb4 (et non utf8) parce que les noms de dossiers contiennent des
-- accents et peuvent contenir des emoji : 'Bonsaïs', 'Séries-Films'.

CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`folder_role_overrides` (
  `account_id`   VARCHAR(255)  NOT NULL
                 COMMENT 'Aujourd''hui user@domain ; en 2d un identifiant de compte lié',
  `role`         VARCHAR(16)   NOT NULL
                 COMMENT 'Enum stable, jamais localisé',
  `folder_path`  VARCHAR(1024) NOT NULL
                 COMMENT 'Seul identifiant garanti par IMAP sur tout serveur',
  `uid_validity` BIGINT UNSIGNED NOT NULL
                 COMMENT 'Garde-fou : détecte la réutilisation d''un chemin',
  `mailbox_id`   VARCHAR(255)  NULL
                 COMMENT 'RFC 8474 OBJECTID, appoint facultatif — jamais la clé',
  `updated_at`   TIMESTAMP     NOT NULL
                 DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`account_id`, `role`),

  CONSTRAINT `chk_folder_role`
    CHECK (`role` IN ('sent', 'drafts', 'trash', 'junk', 'archive'))
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_bin;

-- Aucun index secondaire : cinq lignes au maximum par compte, la clé primaire
-- suffit à tout, y compris à la mise à jour d'un sous-arbre au renommage.

-- Aucune clé étrangère vers `dovecot`.`users` : une contrainte inter-bases
-- recréerait exactement le couplage que cette base sert à éviter. La purge
-- des lignes d'un compte supprimé est à la charge de l'application.

CREATE USER IF NOT EXISTS 'snoopy_webmail'@'__HOST__'
  IDENTIFIED BY '__PASSWORD_PROD__';

-- Droits sur les données seulement. Pas de CREATE, DROP ni ALTER :
-- l'application ne migre jamais son schéma, elle n'a donc aucune raison de
-- pouvoir le modifier — ni de pouvoir le détruire.
GRANT SELECT, INSERT, UPDATE, DELETE
  ON `snoopy_webmail`.*
  TO 'snoopy_webmail'@'__HOST__';

-- ---------------------------------------------------------------------------
--  DÉVELOPPEMENT — base et utilisateur distincts
-- ---------------------------------------------------------------------------

CREATE DATABASE IF NOT EXISTS `snoopy_webmail_dev`
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_bin;

CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`folder_role_overrides` (
  `account_id`   VARCHAR(255)  NOT NULL,
  `role`         VARCHAR(16)   NOT NULL,
  `folder_path`  VARCHAR(1024) NOT NULL,
  `uid_validity` BIGINT UNSIGNED NOT NULL,
  `mailbox_id`   VARCHAR(255)  NULL,
  `updated_at`   TIMESTAMP     NOT NULL
                 DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`account_id`, `role`),

  CONSTRAINT `chk_folder_role_dev`
    CHECK (`role` IN ('sent', 'drafts', 'trash', 'junk', 'archive'))
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_bin;

CREATE USER IF NOT EXISTS 'snoopy_webmail_dev'@'__HOST__'
  IDENTIFIED BY '__PASSWORD_DEV__';

GRANT SELECT, INSERT, UPDATE, DELETE
  ON `snoopy_webmail_dev`.*
  TO 'snoopy_webmail_dev'@'__HOST__';

FLUSH PRIVILEGES;
```

---

## 3. Chaînes de connexion

À renseigner dans la configuration de chaque service — **jamais dans un fichier versionné.**
Elles suivent le même chemin que `MailUserAccountsDatabase`, qui est vide dans
`appsettings.json` du dépôt et remplie au déploiement.

```
WebmailPreferencesDatabase =
  Server=<hôte>;Port=3306;Database=snoopy_webmail;User=snoopy_webmail;Password=<...>;
```

```
WebmailPreferencesDatabase =
  Server=<hôte>;Port=3306;Database=snoopy_webmail_dev;User=snoopy_webmail_dev;Password=<...>;
```

Le service **refuse de démarrer** si cette chaîne est absente hors Development, avec un message
nommant ce document — sur le modèle du contrôle existant pour le key ring. Une fonctionnalité
silencieusement inerte est pire qu'un échec au démarrage.

---

## 4. Vérification

```sql
-- La table existe et porte la bonne collation
SELECT TABLE_NAME, TABLE_COLLATION
  FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'snoopy_webmail';
-- attendu : folder_role_overrides | utf8mb4_bin

-- Les droits sont bien limités aux données
SHOW GRANTS FOR 'snoopy_webmail'@'__HOST__';
-- attendu : GRANT SELECT, INSERT, UPDATE, DELETE ON `snoopy_webmail`.* — et rien d'autre
```

Test d'écriture depuis le compte de service, à exécuter **connecté en tant que
`snoopy_webmail`** :

```sql
INSERT INTO folder_role_overrides
       (account_id, role, folder_path, uid_validity)
VALUES ('probe@example.invalid', 'trash', 'Deleted Items', 1);

-- La contrainte doit refuser un rôle inconnu
INSERT INTO folder_role_overrides
       (account_id, role, folder_path, uid_validity)
VALUES ('probe@example.invalid', 'poubelle', 'X', 1);
-- attendu : ERROR ... CONSTRAINT `chk_folder_role` failed

-- La casse doit distinguer deux chemins
SELECT COUNT(*) FROM folder_role_overrides WHERE folder_path = 'deleted items';
-- attendu : 0

DELETE FROM folder_role_overrides WHERE account_id = 'probe@example.invalid';

-- Le compte de service ne doit pas pouvoir toucher au schéma
DROP TABLE folder_role_overrides;
-- attendu : ERROR 1142 ... command denied
```

---

## 5. Désinstallation

```sql
DROP DATABASE IF EXISTS `snoopy_webmail`;
DROP DATABASE IF EXISTS `snoopy_webmail_dev`;
DROP USER IF EXISTS 'snoopy_webmail'@'__HOST__';
DROP USER IF EXISTS 'snoopy_webmail_dev'@'__HOST__';
FLUSH PRIVILEGES;
```

Perdre cette base fait perdre les affectations de dossiers systèmes, rien d'autre : la chaîne de
résolution retombe sur `SPECIAL-USE` puis sur la correspondance par nom. Aucun message n'est
concerné.

---

## 6. Ce qui reste à la charge de l'application

- **Purger les surcharges d'un compte supprimé.** Il n'y a pas de clé étrangère vers `dovecot`
  — c'est délibéré — donc la suppression d'un utilisateur depuis l'écran Administration doit
  supprimer ses lignes ici.
- **Normaliser `account_id`.** La collation est binaire : `User@Weesky.be` et
  `user@weesky.be` seraient deux comptes distincts. L'application écrit une forme canonique,
  toujours la même.
- **Ajouter un rôle est un changement de schéma.** La contrainte `CHECK` attrape les fautes de
  frappe, au prix d'un `ALTER TABLE` le jour où un sixième rôle apparaît. C'est un arbitrage
  assumé : un rôle nouveau est une décision délibérée, pas un accident.
