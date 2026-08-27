# Prérequis base de données — tables CardDAV

À rejouer **avant** le déploiement du backend, sur `snoopy_webmail` **et** `snoopy_webmail_dev`.
Création manuelle : ce projet n'utilise pas les migrations EF.

Les FK exigent que `users` existe déjà (voir `webmail-users-table.md`).

L'ordre n'est pas une commodité d'exploitation : le backend refuse de lire une table absente, et
un déploiement qui précède son DDL rend `500` sur l'onglet « Sync ».

Une fois le backend déployé, deux rattrapages se suivent, et cet ordre-là est **obligatoire** :
d'abord celui de la tranche 4a — `POST /api/Contacts/Backfill` rejoué jusqu'à `remaining = 0`, voir
`contacts-4a-backfill.md` —, **achevé**, et seulement ensuite `assets/contacts-dav-backfill.sql`.
Dans l'autre sens le dégât ne se rattrape pas : le script DAV pose `sync_sequence = 1` sur toutes
les fiches, celles sans carte comprises, et le rattrapage 4a leur donne ensuite leur carte sans
prendre de rang — c'est un balayage d'exploitation, pas une porte d'écriture. Elles se mettent alors
à satisfaire la clause de visibilité à un rang **déjà publié**, donc aucun client détenant un jeton
`>= 1` ne les recevra jamais, sans erreur nulle part ; et le script DAV ne peut plus les réparer, sa
clause `WHERE sync_sequence = 0` ne les trouvant plus. Le seul remède est alors une rotation
d'epoch, qui force une resynchronisation complète.

## Tranche 4c-i — `dav_credentials`

Une ligne par utilisateur, et c'est la forme qui dit qu'il n'y a qu'un secret par personne
(décision 1). Une clé technique et un index sur `user_id` laisseraient la table accepter une
deuxième ligne que rien dans le code ne crée — jusqu'au jour où une reprise l'y mettrait.

```sql
CREATE TABLE `dav_credentials` (
  `user_id`         CHAR(36)      NOT NULL,
  `carddav_enabled` TINYINT(1)    NOT NULL DEFAULT 1
    COMMENT 'Interrupteur par protocole ; CalDAV aura sa propre colonne, pas une migration',
  `secret_hash`     CHAR(64)      NOT NULL
    COMMENT 'SHA-256 hexadécimal minuscule de (salt || secret UTF-8)',
  `salt`            VARBINARY(16) NOT NULL,
  `created_at`      DATETIME      NOT NULL
    COMMENT 'UTC ; posée par le code, jamais par le schéma',
  `last_used_at`    DATETIME      NULL DEFAULT NULL
    COMMENT 'UTC ; posée par le code — amortie à l''heure côté service, rendue en relatif',
  PRIMARY KEY (`user_id`),
  CONSTRAINT `fk_dav_credentials_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Aucun `GRANT` à rejouer : les utilisateurs `snoopy_webmail`/`snoopy_webmail_dev` ont déjà
`SELECT, INSERT, UPDATE, DELETE` sur toute la base.

**Ni `DEFAULT CURRENT_TIMESTAMP` ni `TIMESTAMP` sur les deux dates**, pour la même raison que
`users.creation_date` n'en a pas : la valeur appartient au code, qui l'écrit en UTC, et un
`TIMESTAMP` la ferait traverser le fuseau de session — décalée, dans la même base, à côté d'une
`DATETIME` posée par le même code.

## L'adresse publiée — `Dav__PublicUrl`

La table ne suffit pas : sans adresse publiée, l'onglet « Sync » n'existe pas. Elle se pose
dans l'`EnvironmentFile` du service sous `Dav__PublicUrl` (`Dav:PublicUrl` dans `appsettings.json`,
où elle est livrée vide) et vaut l'origine que le reverse proxy sert :
`https://api.mail.weesky.net`. Origine nue, exactement : pas de chemin, pas de barre finale,
pas de port, pas d'identifiants — les clients y concatènent `/.well-known/carddav`, et le service
**refuse de démarrer** sur toute autre forme plutôt que de laisser la valeur atteindre l'écran.

La laisser vide est un état légal et c'est le défaut : le déploiement ne sert aucun /dav,
`GET /api/DavCredentials` répond `404` et l'onglet ne s'affiche pas. Rien ne le signale au
démarrage — c'est la panne à connaître : une tranche entière qui se tait parce qu'une variable
manque.

## Pourquoi le hachage n'est pas un KDF

C'est l'inverse de la règle habituelle et la raison est écrite ici pour que personne ne
« corrige » le hachage plus tard. Un KDF lent existe pour rendre coûteuse l'attaque par
dictionnaire d'un secret que l'humain a choisi. Ici l'entropie vient de nous : 20 caractères
base32, ≈100 bits, hors de portée d'une recherche exhaustive quelle que soit la vitesse du
hachage. Et un client DAV se ré-authentifie à **chaque** requête — un PBKDF2 à 100 000 itérations
y serait un déni de service que nous nous infligerions nous-mêmes, déclenchable à volonté par des
requêtes non authentifiées.

Le sel reste par ligne : il empêche qu'une même chaîne engendrée deux fois se reconnaisse dans la
table, et il ne coûte rien — la ligne se retrouve par sa clé, jamais par l'empreinte.

## Deux états distincts, et ils ne se confondent pas

- **Aucune ligne** = jamais activé. L'utilisateur n'a pas de secret, et le `401` est la seule
  réponse du bord.
- **`carddav_enabled = 0`** = éteint mais configuré. Le secret survit, rallumer ne reconfigure
  aucun appareil, et le bord répond `403` — mais seulement après une comparaison **réussie** du
  condensat (décision 2), sans quoi la réponse serait un oracle d'énumération de comptes.

Le défaut à `1` décrit l'état dans lequel la ligne naît — elle n'existe que si l'utilisateur a
allumé l'interrupteur —, pas une politique appliquée à qui n'a rien demandé.

## Tranche 4c-ii — la synchronisation

```sql
CREATE TABLE `contact_sync_state` (
  `user_id`      CHAR(36)        NOT NULL,
  `epoch`        CHAR(36)        NOT NULL
    COMMENT 'GUID ; ne bouge que sur restauration — voir carddav-restore-prerequisite.md',
  `seq`          BIGINT UNSIGNED NOT NULL DEFAULT 0
    COMMENT 'Compteur ; nommé seq car SEQUENCE est un mot-clé MariaDB depuis 10.3',
  `pruned_below` BIGINT UNSIGNED NOT NULL DEFAULT 0
    COMMENT 'Filigrane : un jeton <= cette valeur est irrécupérable (403 valid-sync-token)',
  PRIMARY KEY (`user_id`),
  CONSTRAINT `fk_contact_sync_state_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_tombstones` (
  `user_id`       CHAR(36)        NOT NULL,
  `dav_name`      VARCHAR(255)    NOT NULL COLLATE utf8mb4_bin,
  `sync_sequence` BIGINT UNSIGNED NOT NULL,
  `deleted_at`    DATETIME        NOT NULL
    COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`user_id`, `dav_name`),
  INDEX `ix_contact_tombstones_seq` (`user_id`, `sync_sequence`),
  INDEX `ix_contact_tombstones_time` (`deleted_at`),
  CONSTRAINT `fk_contact_tombstones_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_revisions` (
  `id`          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id`     CHAR(36)        NOT NULL,
  `contact_id`  CHAR(36)        NULL DEFAULT NULL
    COMMENT 'La fiche quand elle existe encore ; une révision delete survit à la sienne',
  `uid`         VARCHAR(255)    NULL DEFAULT NULL
    COMMENT 'UID de la carte archivée ; NULL quand un corps rejeté ne se parse pas',
  `dav_name`    VARCHAR(255)    NULL DEFAULT NULL COLLATE utf8mb4_bin,
  `card_hash`   CHAR(64)        NOT NULL,
  `vcard_raw`   MEDIUMTEXT      NOT NULL
    COMMENT 'Les octets remplacés ou refusés — même type que contacts.vcard_raw',
  `cause`       ENUM('put','webmail','import','delete','rejected') NOT NULL,
  `replaced_at` DATETIME        NOT NULL
    COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`id`),
  INDEX `ix_contact_revisions_user_time` (`user_id`, `replaced_at`),
  INDEX `ix_contact_revisions_time` (`replaced_at`),
  INDEX `ix_contact_revisions_uid` (`user_id`, `uid`),
  INDEX `ix_contact_revisions_name` (`user_id`, `dav_name`),
  CONSTRAINT `fk_contact_revisions_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

ALTER TABLE `contacts`
  ADD COLUMN `dav_name`      VARCHAR(255)    NULL DEFAULT NULL COLLATE utf8mb4_bin
    COMMENT 'Le nom de ressource choisi par le client ; {id}.vcf pour une fiche née ici',
  ADD COLUMN `sync_sequence` BIGINT UNSIGNED NOT NULL DEFAULT 0
    COMMENT '0 = jamais rattrapée, donc invisible du protocole (un jeton demande > n, n >= 0)',
  ADD UNIQUE INDEX `ux_contacts_dav_name` (`user_id`, `dav_name`),
  ADD INDEX `ix_contacts_sync_sequence` (`user_id`, `sync_sequence`);
```

Et les quatre paragraphes qu'un relecteur redemanderait :

- **`seq` et non `sequence`** : `SEQUENCE` est un mot-clé MariaDB depuis 10.3, et une colonne
  qui n'existe qu'entre back-quotes est une erreur de production en attente, dans un projet où le
  SQL se passe à la main.
- **`dav_name` est nullable, `sync_sequence` ne l'est pas.** L'unicité MySQL ignore les `NULL`,
  donc la colonne peut rester vide sur les fiches que le rattrapage n'a pas encore atteintes sans
  que le premier `PUT` d'un client bute sur un doublon de vide. `sync_sequence` part de `0`, la
  valeur qu'un jeton ne réclame jamais : une fiche non rattrapée est invisible du protocole
  plutôt que servie sous un nom absent.
- **`contact_revisions` porte une clé technique, à l'inverse de ses voisines.** Les tombes sont un
  état — une par nom, la plus récente écrase la précédente — les révisions sont un journal :
  plusieurs lignes coexistent pour un même `dav_name` et rien ne les distingue qu'un ordre. Mettre
  `(user_id, dav_name, replaced_at)` en clé ferait de deux écritures dans la même seconde une
  collision, sur la table dont le rôle est précisément de ne rien perdre.
- **`vcard_raw` en `MEDIUMTEXT`, comme celui de `contacts`.** Les deux colonnes sont identiques
  pour que la donnée ne traverse aucune conversion entre elles : une carte lue dans une
  révision doit pouvoir être renvoyée telle quelle, sinon l'historique ne restitue pas ce qu'il a
  archivé.

## Vérification

La collation est ce qui échoue réellement ici : chaque FK exige que sa colonne `user_id` et
`users.id` s'accordent, donc elle se lit, elle ne se suppose pas. Le schéma est nommé plutôt que
laissé à `DATABASE()`, qui vaut NULL sur un client sans base sélectionnée et rend alors 0 ligne
sans rien signaler. Les quatre tables de la base, `dav_credentials` comme les trois de 4c-ii,
passent par le même contrôle.

```sql
SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'snoopy_webmail'
   AND TABLE_NAME IN ('contact_revisions', 'contact_sync_state', 'contact_tombstones', 'dav_credentials');
-- attendu :
--   contact_revisions   | utf8mb4_bin
--   contact_sync_state  | utf8mb4_bin
--   contact_tombstones  | utf8mb4_bin
--   dav_credentials     | utf8mb4_bin
```

### Prérequis avant d'ouvrir toute route `/dav` — l'atomicité du compteur, vérifiée à la main

C'est la seule propriété de correction de toute la tranche 4c-ii qui n'a pas de test :
`ContactSyncStore.NextSequenceAsync` avance le compteur par un
`INSERT ... ON DUPLICATE KEY UPDATE seq = seq + 1` que ni le fournisseur InMemory ni SQLite
ne savent exécuter à l'identique.
Elle se vérifie donc à la main, une fois, contre `snoopy_webmail_dev`, avec deux sessions `mysql`
côte à côte :

```sql
-- Session A                              -- Session B
START TRANSACTION;
INSERT INTO contact_sync_state
  (user_id, epoch, seq, pruned_below)
VALUES ('<un user réel>', UUID(), 1, 0)
ON DUPLICATE KEY UPDATE seq = seq + 1;
                                          START TRANSACTION;
                                          INSERT INTO contact_sync_state
                                            (user_id, epoch, seq, pruned_below)
                                          VALUES ('<le même>', UUID(), 1, 0)
                                          ON DUPLICATE KEY UPDATE seq = seq + 1;
                                          -- ↑ DOIT BLOQUER ici, et non rendre la main
SELECT seq FROM contact_sync_state
 WHERE user_id = '<le même>';             -- (toujours bloquée)
COMMIT;
                                          -- ↑ se débloque maintenant
                                          SELECT seq FROM contact_sync_state
                                           WHERE user_id = '<le même>';
                                          COMMIT;
```

Ce qu'il faut observer :

| Observation | Attendu |
|---|---|
| La session B au moment de son `INSERT` | **bloque**, elle ne rend pas la main |
| B se débloque | au `COMMIT` de A, pas avant |
| Le `seq` lu par A puis par B | deux valeurs **distinctes** et consécutives |
| Après les deux `COMMIT` | `seq` a avancé de exactement 2 |
| L'`epoch` | **inchangé** entre les deux, malgré le `UUID()` dans chaque `VALUES` |
| Le `SELECT` sur `user_id = '<le même>'` | trouve **exactement une ligne** — un paramètre `Guid` lié dans un format que la colonne `CHAR(36)` ne reconnaît pas créerait une seconde ligne d'état au lieu de mettre à jour la première |

Si B ne bloque pas, l'incrément n'est pas sous verrou et rien de la synchronisation n'est sûr :
arrêter et le signaler, ne pas continuer le plan.

**Exécutée le 27 août 2026** sur `snoopy_webmail_dev`, conforme sur les six observations :
l'atomicité du compteur est désormais observée, et non plus seulement argumentée. À rejouer
après toute restauration et après toute reprise du schéma.

## Désinstallation

```sql
ALTER TABLE `snoopy_webmail`.`contacts`
  DROP INDEX `ix_contacts_sync_sequence`,
  DROP INDEX `ux_contacts_dav_name`,
  DROP COLUMN `sync_sequence`,
  DROP COLUMN `dav_name`;
ALTER TABLE `snoopy_webmail_dev`.`contacts`
  DROP INDEX `ix_contacts_sync_sequence`,
  DROP INDEX `ux_contacts_dav_name`,
  DROP COLUMN `sync_sequence`,
  DROP COLUMN `dav_name`;

DROP TABLE IF EXISTS `snoopy_webmail`.`contact_revisions`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`contact_revisions`;
DROP TABLE IF EXISTS `snoopy_webmail`.`contact_tombstones`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`contact_tombstones`;
DROP TABLE IF EXISTS `snoopy_webmail`.`contact_sync_state`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`contact_sync_state`;

DROP TABLE IF EXISTS `snoopy_webmail`.`dav_credentials`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`dav_credentials`;
```

La perte des trois tables efface l'état de synchronisation et l'historique des fiches :
chaque client CardDAV repart d'une synchronisation complète à la prochaine requête, et les trente
jours de révisions ne sont plus consultables. Celle des deux colonnes sur `contacts` n'efface
aucun contact — seuls `dav_name` et `sync_sequence`, et les index qui les portent, disparaissent.

La perdre coupe chaque appareil déjà configuré : les secrets partent avec la table, et rallumer la
synchronisation en engendre de nouveaux, à ressaisir sur chaque appareil. Aucun contact n'est
concerné.
