# Prérequis serveur — table `scheduling_service_account`

**À appliquer sur les deux bases** (`snoopy_webmail` et `snoopy_webmail_dev`) avant de déployer
le backend qui expose `/api/SchedulingAccount`.

Le projet n'utilise pas les migrations EF : la création des tables est manuelle, comme pour
`app_settings` (voir `webmail-app-settings-table.md`).

## Pourquoi cette table

Le compte SMTP technique qui envoie les invitations d'agenda quand un rendez-vous avec invités est
modifié depuis un téléphone ou Thunderbird (CalDAV), au nom de l'organisateur. Il se configure
**uniquement** dans Administration > Application : la section `Scheduling` de `appsettings.json`
n'existe plus, et une variable `Scheduling__Smtp__*` restée dans l'EnvironmentFile n'est plus lue
(ne la retirer qu'une fois la mise en production confirmée, voir « Retour arrière »).
Après le déploiement, le compte est à saisir dans l'écran d'administration ; tant qu'il ne l'est
pas, les modifications faites depuis un téléphone n'envoient aucune invitation (le journal le dit).

- **Une seule ligne** pour l'instance : `id` vaut toujours 1 (contrainte `CHECK`). Pas de
  `user_id`, aucune clé étrangère.
- **Pas dans `app_settings`** : cette table-là se lit sans authentification.
- **`password_cipher`** contient des octets protégés par Data Protection, sous un objectif propre
  distinct de celui du secret client OAuth — jamais du texte. Il s'écrit par l'écran
  d'administration, jamais directement dans la colonne. Si l'anneau de clés est perdu, le mot de
  passe devient illisible : l'écran le signale, le compte est traité comme absent pour l'envoi, et
  il suffit de ressaisir le mot de passe.
- **`last_test_at` / `last_test_ok`** : le verdict du dernier « Tester » sur le compte enregistré.
  Chaque enregistrement les remet à `NULL`.
- **`updated_at` en `DATETIME(6)`** : un test n'inscrit son verdict que si la ligne n'a pas été
  réenregistrée pendant qu'il tournait, et c'est sur cette valeur que la comparaison se fait.

## Script

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`scheduling_service_account` (
  `id`              TINYINT UNSIGNED  NOT NULL DEFAULT 1 COMMENT 'Toujours 1 : une seule ligne',
  `host`            VARCHAR(255)      NOT NULL,
  `port`            SMALLINT UNSIGNED NOT NULL,
  `security`        VARCHAR(16)       NOT NULL COMMENT 'None | StartTls | SslOnConnect',
  `login`           VARCHAR(320)      NOT NULL,
  `password_cipher` VARBINARY(1024)   NOT NULL COMMENT 'Protégé par Data Protection — jamais du texte',
  `last_test_at`    DATETIME          NULL     COMMENT 'UTC ; dernier test du compte enregistré',
  `last_test_ok`    TINYINT(1)        NULL,
  `updated_at`      DATETIME(6)       NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`id`),
  CONSTRAINT `ck_scheduling_service_account_single` CHECK (`id` = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`scheduling_service_account` (
  `id`              TINYINT UNSIGNED  NOT NULL DEFAULT 1 COMMENT 'Toujours 1 : une seule ligne',
  `host`            VARCHAR(255)      NOT NULL,
  `port`            SMALLINT UNSIGNED NOT NULL,
  `security`        VARCHAR(16)       NOT NULL COMMENT 'None | StartTls | SslOnConnect',
  `login`           VARCHAR(320)      NOT NULL,
  `password_cipher` VARBINARY(1024)   NOT NULL COMMENT 'Protégé par Data Protection — jamais du texte',
  `last_test_at`    DATETIME          NULL     COMMENT 'UTC ; dernier test du compte enregistré',
  `last_test_ok`    TINYINT(1)        NULL,
  `updated_at`      DATETIME(6)       NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`id`),
  CONSTRAINT `ck_scheduling_service_account_single` CHECK (`id` = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

## Vérification

Après le déploiement, sur chaque base :

1. `SHOW CREATE TABLE scheduling_service_account;` montre les neuf colonnes et la contrainte
   `ck_scheduling_service_account_single`. Si la table manque, l'écran d'administration affiche
   « Impossible de charger le compte d'envoi. », l'API répondant par une erreur 500.
2. Administration > Application montre « Aucun compte configuré : les modifications faites depuis
   un téléphone n'envoient pas d'invitations. »
3. Saisir le compte, « Tester la connexion » (le résultat s'affiche sous le formulaire), puis
   « Enregistrer ». La carte montre l'identifiant ; son bouton « Tester » ajoute la pastille verte
   « Connexion testée le … ».
4. `SELECT id, host, port, security, login, last_test_at, last_test_ok FROM scheduling_service_account;`
   rend une seule ligne, `id = 1`.
5. Modifier depuis un téléphone ou Thunderbird un rendez-vous qui a un invité : l'invité reçoit le
   mail, et le journal du service porte `Invitation mail sent by the service account`.

## Retour arrière

- **Garder les variables `Scheduling__Smtp__*` de l'EnvironmentFile jusqu'à ce que la mise en
  production soit confirmée.** La version précédente du service lit son compte d'envoi là : si on
  la redéploie, elle envoie de nouveau les invitations sans autre geste. Les retirer avant, c'est
  un retour arrière qui n'envoie plus rien.
- **La table peut rester en place.** La version précédente ne la lit pas, et la garder évite de
  ressaisir le compte si la nouvelle version est redéployée ensuite. Un `DROP TABLE` efface le
  compte enregistré : il faudrait le saisir de nouveau.
- Une fois la mise en production confirmée, retirer les variables `Scheduling__Smtp__*` : la
  nouvelle version les ignore, leur retrait ne demande pas de redémarrage.
