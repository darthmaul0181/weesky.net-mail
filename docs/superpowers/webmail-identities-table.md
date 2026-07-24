# Prérequis serveur — table `sending_identities`

**À appliquer sur les deux bases** (`snoopy_webmail` et `snoopy_webmail_dev`) avant de déployer
le backend qui expose `/api/Identities`.

Le projet n'utilise pas les migrations EF : la création des tables est manuelle, comme pour
`folder_role_overrides` (voir `mail-2a5-database-prerequisite.md`) et `user_preferences` (voir
`webmail-preferences-table.md`).

## Pourquoi cette table

Une liste courte d'identités d'envoi — l'adresse principale du compte, plus ses alias choisis —
que l'utilisateur curatele lui-même dans les réglages, avec une adresse marquée par défaut. Le
backend l'expose telle quelle en `GET/PUT /api/Identities` ; `POST /api/Mail/Send` la consulte
pour valider le `From` choisi à la composition.

**Remplacement complet, pas de fusion.** `PUT` remplace tout l'ensemble du compte en une seule
transaction : pas de PATCH ligne à ligne, pas d'état intermédiaire à réconcilier côté client.

## Script

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`sending_identities` (
  `account_id`   VARCHAR(255) NOT NULL
                 COMMENT 'Forme canonique : minuscules, sans espaces',
  `address`      VARCHAR(320) NOT NULL
                 COMMENT 'Forme canonique minuscule ; 320 = longueur max RFC 5321',
  `display_name` VARCHAR(100) NOT NULL,
  `is_default`   TINYINT(1)   NOT NULL DEFAULT 0,
  `updated_at`   TIMESTAMP    NOT NULL
                 DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`account_id`, `address`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_bin;

-- Idem sur la base de développement.
CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`sending_identities` (
  `account_id`   VARCHAR(255) NOT NULL
                 COMMENT 'Forme canonique : minuscules, sans espaces',
  `address`      VARCHAR(320) NOT NULL
                 COMMENT 'Forme canonique minuscule ; 320 = longueur max RFC 5321',
  `display_name` VARCHAR(100) NOT NULL,
  `is_default`   TINYINT(1)   NOT NULL DEFAULT 0,
  `updated_at`   TIMESTAMP    NOT NULL
                 DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`account_id`, `address`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_bin;
```

Aucun `GRANT` à rejouer : les utilisateurs `snoopy_webmail` et `snoopy_webmail_dev` ont déjà
`SELECT, INSERT, UPDATE, DELETE` sur toute la base.

**Pas de contrainte `CHECK` sur `is_default`** — le store persiste les lignes telles qu'on les lui
donne ; un seul défaut par compte (et la canonicalisation des adresses) est une règle applicative,
tenue par la couche API avant l'appel au store, pas par le schéma. Sans ligne marquée par défaut,
l'adresse principale du compte fait office de défaut, résolue à la lecture et jamais stockée.

**Effet de bord assumé sur `is_default` d'une ligne périmée** : le client renvoie le `isDefault`
*résolu*, toujours faux pour une ligne périmée (alias supprimé), donc la sauvegarde suivante remet
son `is_default` à 0 en base — un alias supprimé alors qu'il portait le défaut ne le retrouve pas
s'il est recréé après une sauvegarde intermédiaire. Cohérent avec « une ligne périmée ne peut pas
porter le défaut », mais c'est bien une valeur stockée réécrite au passage.

## Vérification

```sql
SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'snoopy_webmail' AND TABLE_NAME = 'sending_identities';
-- attendu : sending_identities | utf8mb4_bin
```

## Désinstallation

```sql
DROP TABLE IF EXISTS `snoopy_webmail`.`sending_identities`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`sending_identities`;
```

La perdre fait retomber chaque compte sur l'unique identité par défaut (l'adresse du compte).
Aucun message n'est concerné.

## Prérequis Postfix — envoi depuis un alias

Envoyer avec un `From` égal à un alias exige que `smtpd_sender_login_maps` autorise
l'utilisateur authentifié à utiliser ses alias comme expéditeur d'enveloppe ; sans cela Postfix
répond 553 et le webmail affiche « The mail server refused to send from _address_ ».

À vérifier sur le serveur :

```
postconf smtpd_sender_login_maps
```

(une requête sur la table des alias en est la valeur habituelle), et que
`reject_sender_login_mismatch` (ou `reject_authenticated_sender_login_mismatch`) apparaît bien
dans `smtpd_sender_restrictions`.
