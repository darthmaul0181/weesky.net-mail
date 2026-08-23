# Prérequis base de données — tables CardDAV

À rejouer **avant** le déploiement du backend, sur `snoopy_webmail` **et** `snoopy_webmail_dev`.
Création manuelle : ce projet n'utilise pas les migrations EF.

Les FK exigent que `users` existe déjà (voir `webmail-users-table.md`).

L'ordre n'est pas une commodité d'exploitation : le backend refuse de lire une table absente, et
un déploiement qui précède son DDL rend `500` sur l'onglet « Sync ».

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

## Ce que la tranche 4c-ii ajoutera

`contact_sync_state`, `contact_tombstones`, `contact_revisions`, deux colonnes sur `contacts`
(`dav_name`, `sync_sequence`) et leur rattrapage. Elles ne sont **pas** dans ce fichier tant que
4c-ii n'est pas écrite : un DDL rejoué en avance créerait des tables que rien ne lit et un
rattrapage que rien ne vérifie.

## Vérification

La collation est ce qui échoue réellement ici : la FK exige que `dav_credentials.user_id` et
`users.id` s'accordent, donc elle se lit, elle ne se suppose pas. Le schéma est nommé plutôt que
laissé à `DATABASE()`, qui vaut NULL sur un client sans base sélectionnée et rend alors 0 ligne
sans rien signaler.

```sql
SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'snoopy_webmail' AND TABLE_NAME = 'dav_credentials';
-- attendu : dav_credentials | utf8mb4_bin
```

## Désinstallation

```sql
DROP TABLE IF EXISTS `snoopy_webmail`.`dav_credentials`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`dav_credentials`;
```

La perdre coupe chaque appareil déjà configuré : les secrets partent avec la table, et rallumer la
synchronisation en engendre de nouveaux, à ressaisir sur chaque appareil. Aucun contact n'est
concerné.
