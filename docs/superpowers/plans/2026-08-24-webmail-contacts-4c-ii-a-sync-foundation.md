# Contacts 4c-ii-a — le socle de synchronisation : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `4c-ii-a-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-08-23-webmail-contacts-4c-carddav-design.md`](../specs/2026-08-23-webmail-contacts-4c-carddav-design.md) — toute décision citée ici (« décision N ») y renvoie. En cas de doute, la spec fait foi.

**Périmètre.** La spec se découpe en **deux** plans (§ « Découpage ») : 4c-i, livré et poussé, et 4c-ii, le serveur DAV. 4c-ii s'est révélé trop gros pour un plan — une vingtaine de fichiers neufs, une vingtaine de tâches — et se découpe donc lui-même en trois, dans cet ordre strict :

| | Plan | Contenu | Livrable seul |
|---|---|---|---|
| **a** | **ce document** | le socle : tables, entités, rattrapage, `IContactSyncStore`, `ContactStore` transactionnel, le `409` du webmail, les balayeurs, les résidus vCard | le webmail gagne le contrôle de concurrence et **cesse de perdre des données** |
| b | à écrire | le serveur DAV en lecture : chemins, XML, `PROPFIND`, `GET`, `multiget`, `expand-property`, découverte | un client s'appaire et tire le carnet |
| c | à écrire | l'écriture et la synchro : `PUT`, `DELETE`, `addressbook-query`, `sync-collection`, jeton et ctag | la fonction complète |

**Rien de ce plan ne crée de route `/dav`.** C'est délibéré et c'est ce qui le rend sûr : le § « Prérequis d'infrastructure » de la spec exige que les tables, le backend et le **rattrapage** soient passés **avant** qu'un client puisse se connecter, faute de quoi ce client voit un carnet vide et **efface ses propres copies**. Ce plan est exactement la fenêtre où ce risque se ferme, et il la ferme avant qu'aucune route n'existe pour l'ouvrir.

**Goal :** donner au carnet une séquence, des pierres tombales et un historique des cartes remplacées, et faire passer les trois portes d'écriture existantes du webmail par eux — de sorte qu'aucune suppression ne soit plus invisible et qu'aucun écrasement ne soit plus définitif.

**Architecture :** une ligne d'état par utilisateur (`contact_sync_state`) portant un compteur incrémenté **sous le verrou de sa propre ligne, dans la même transaction que l'écriture qu'il numérote** ; une pierre tombale par nom disparu, élaguée à 180 jours derrière un filigrane qui rend les jetons périmés détectables ; un journal de contenus (`contact_revisions`) élagué à 30 jours, écrit par toute écriture qui remplace et toute suppression ; et `ContactStore` réécrit pour ouvrir une transaction explicite à travers l'`IExecutionStrategy`, toujours dans le même ordre de verrous.

**Tech stack :** .NET 10, EF Core (InMemory pour les tests), Pomelo/MySQL, xUnit 2.9.3, Moq 4.20.72 ; React 18 + TypeScript, Vitest + Testing Library, i18next.

## Global constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; `cd src && dotnet build` doit rester à zéro avertissement.
- Frontend : `cd src/frontend && npm test` ; `npx tsc --noEmit` et `npm run lint` doivent rester propres.
- `src/snoopy.microservice/ApiDocumentation.xml` : artefact versionné que `dotnet test` régénère avec des centaines de lignes sans rapport — le réverter avant chaque commit (`git checkout -- src/snoopy.microservice/ApiDocumentation.xml`).
- `Assert.IsType<T>` vérifie le type **exact** : `ConflictObjectResult` pour `Conflict(body)`, `NotFoundObjectResult` pour `NotFound(body)`, `OkObjectResult` pour `Ok(body)`, jamais `ObjectResult`.
- Style C# : file-scoped namespaces, un type par fichier, constructeurs primaires pour l'injection, records pour les DTO, `sealed`, `internal` par défaut, `CancellationToken` sur tout `async`, `ILogger` en journalisation structurée (jamais d'interpolation).
- Style TS : pas de `any` ; l'API omet les champs `null` (`WhenWritingNull`), donc côté client un champ optionnel se déclare `champ?: T`, jamais `T | null`.
- i18n : toute clé neuve existe dans `src/frontend/src/locales/en/*.json` **et** `fr/*.json` ; l'UI du site est en anglais ; la parité et la typographie française (U+00A0 avant `; : ? !`, apostrophe `’`) sont vérifiées par `src/frontend/src/locales/parity.test.ts`. **L'outil `Edit` écrit une espace ordinaire là où le français veut U+00A0** — poser ces chaînes par script et vérifier en lançant `parity.test.ts`, jamais à l'œil.
- **Le projet n'a pas de migrations EF.** Tout DDL est manuel et consigné dans `docs/superpowers/`.
- **Toute nouvelle entité déclare son arête vers `WebmailUser` dans `OnModelCreating`**, sans propriété de navigation. Sans arête déclarée, EF ordonne les `INSERT` par nom de table et casse la clé étrangère ; **le fournisseur InMemory n'applique aucune FK, donc aucun test ne peut l'attraper — seule la déclaration le peut.**
- Commits : concis, sujet + ligne vide + corps de 2 lignes max, jamais commencer ni finir par `@`, terminer par `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`. **Ne jamais écrire un message de commit avec un here-string PowerShell dans l'outil Bash** — utiliser `git commit -F -` avec un heredoc.

## Valeurs fixées une fois, à ne pas réinventer

| Constante | Valeur | Où |
|---|---|---|
| Lot par transaction | **100 fiches** | `ContactStore.BatchSize` |
| Plafond par utilisateur | 5000 fiches (existant) | `ContactStore.MaxPerUser` |
| Plafond d'une carte | 1 Mo (existant) | `ContactStore.MaxCardBytes` |
| Rang de départ du rattrapage | `1` pour toutes les fiches d'un utilisateur | le `.sql` de rattrapage |
| Rang « invisible du protocole » | `0` | `contacts.sync_sequence DEFAULT 0` |
| Durée de vie d'une tombe | **180 jours** | `ContactTombstoneSweeper` |
| Durée de vie d'une révision | **30 jours** | `ContactTombstoneSweeper` |
| Période du balayeur | 24 h, gigue de démarrage 5 min | `ContactTombstoneSweeper` |
| Fenêtre de dédoublonnage d'une révision `rejected` | **24 heures** sur `(user_id, dav_name, card_hash, cause)` | `ContactSyncStore.ArchiveAsync` |
| Longueur max d'un `dav_name` | 255 caractères | `DavName.Validate` |
| Convention de nom d'une fiche née ici | `{id}.vcf` | `DavName.ForContact` |

## La clause de visibilité, écrite une fois

Trois conditions, jamais une seule. Elle apparaît telle quelle dans les plans b et c, et le présent plan la pose en constante pour qu'elle ne soit pas réécrite quatre fois :

```csharp
c.DavName != null && c.VCardRaw != null && c.CardHash != ""
```

`dav_name IS NOT NULL` est la plus visible, mais elle laisse passer deux voisines nées du rattrapage de 4a : `contacts.vcard_raw` est `DEFAULT NULL` et `contacts.card_hash` est `NOT NULL DEFAULT ''`. Une fiche que **ce** rattrapage-là aurait manquée sortirait avec un corps vide et un `ETag: ""` — syntaxiquement valide, sémantiquement faux, et rangé par le client comme n'importe quelle autre valeur, pour toujours. Un ETag vide est précisément le genre de valeur qu'aucune assertion ne regarde, parce qu'elle a l'air d'une valeur.

## Découpage en paquets

| | Paquet | Tâches | Vérifiable par |
|---|---|---|---|
| 1 | Le schéma, les entités et le rattrapage | 1–2 | la suite .NET ; rien ne bouge à l'écran |
| 2 | Le dépôt de synchronisation | 3–4 | la suite .NET |
| 3 | `ContactStore` et ses portes | 5–7 | la suite .NET ; toute suppression laisse une trace |
| 4 | Le `409` de bout en bout et les balayeurs | 8–10 | la suite .NET + la suite frontend + l'écran |
| 5 | Les résidus que le protocole rend routiniers | 11–12 | la suite .NET |

## Les fabriques de test que ce plan suppose

Les extraits de test ci-dessous appellent des fabriques et des assertions partagées. Elles
n'existent pas encore : **la tâche 5 les crée**, dans
`snoopy.microservice.Tests/Fixtures/ContactStoreTestFactory.cs` et
`snoopy.microservice.Tests/Fixtures/LoggerAssertions.cs`, et les tâches suivantes s'en servent
telles quelles plutôt que de les redéclarer — la règle du projet interdit le doublon, et trois
copies d'une fabrique divergent au premier changement de signature.

```csharp
// Fixtures/ContactStoreTestFactory.cs
internal static class ContactStoreTestFactory
{
    // ConfigureWarnings ignores TransactionIgnoredWarning: the InMemory provider has no
    // transactions and BeginTransactionAsync throws there by default. The transaction is real in
    // production and ignored here — one more reason the atomicity of Task 3 is checked by hand.
    internal static PreferencesDbContext NewContext();

    /// A doubled IContactSyncStore answering `rank` and accepting every archive.
    internal static Mock<IContactSyncStore> NewSync(ulong rank = 1);

    /// A minimal valid ContactWrite. Read Models/Contacts/ContactWrite.cs for the real parameter
    /// names before writing this — a badly built write fails every test for an unrelated reason.
    internal static ContactWrite Write(string first, string last);

    /// `count` distinct import rows, and one that merges onto an existing contact.
    internal static IReadOnlyList<ContactImportRow> ImportRows(int count);
    internal static IReadOnlyList<ContactImportRow> MergeRowFor(string first, string last);

    /// A ContactDetail carrying the given hash, for the controller tests of Task 8.
    internal static ContactDetail DetailWithHash(string cardHash);
}

// Fixtures/LoggerAssertions.cs — Moq cannot verify an extension method, so every one of these
// goes through ILogger.Log with its LogLevel, which is what the mock actually sees.
internal static class LoggerAssertions
{
    internal static void VerifyNoErrorLogged<T>(this Mock<ILogger<T>> logger);
    internal static void VerifyErrorLoggedContaining<T>(this Mock<ILogger<T>> logger, string fragment);
    internal static void VerifyInformationLogged<T>(this Mock<ILogger<T>> logger);
}
```

Les tâches 8, 10 et 11 nomment en outre quelques aides locales à leur propre fichier de tests —
`NewController`, `ValidRequest`, `NewSweeper`, `NewContextWith`, `WithinAnHourOf`, `Unfold`,
`WriteWithOnePhone`, `WriteWithNote`, `WriteNamed`. Elles restent locales : elles ne servent qu'à
un fichier, et les remonter dans les fixtures partagées ferait porter à tout le projet de tests une
forme qu'un seul endroit utilise.

---

## Paquet 1 — le schéma, les entités et le rattrapage

### Task 1 : le DDL, le rattrapage, la rotation d'epoch et les trois notes

Rien ici n'est du C#. C'est ce qu'un opérateur rejoue, et l'**ordre** en est la substance : les
tables d'abord, le backend ensuite, le rattrapage immédiatement après. Entre le déploiement et le
rattrapage, les fiches existantes n'ont ni `dav_name` ni rang — un client qui se connecterait dans
cette fenêtre verrait un carnet vide et **effacerait ses propres copies** en les croyant supprimées
côté serveur. Aucune route `/dav` n'existe encore, donc la fenêtre est théorique aujourd'hui ; elle
cesse de l'être au plan c, et c'est maintenant qu'elle se documente.

**Files :**
- Modify : `docs/superpowers/webmail-carddav-tables.md`
- Create : `assets/contacts-dav-backfill.sql`
- Create : `assets/contacts-sync-epoch-rotate.sql`
- Create : `docs/superpowers/carddav-restore-prerequisite.md`
- Modify : `docs/superpowers/reverse-proxy-prerequisite.md`

**Interfaces :**
- Produit, consommé par la tâche 2 : les noms de colonnes exacts des quatre tables, qui doivent
  correspondre aux `[Column("…")]` des entités au caractère près.

- [ ] **Step 1 : Étendre `webmail-carddav-tables.md`**

Ajouter, après la section « Tranche 4c-i — `dav_credentials` », une section
« Tranche 4c-ii — la synchronisation » portant les trois tables et les deux colonnes. Le fichier
existant ouvre par « À rejouer **avant** le déploiement du backend » et se termine par une section
de désinstallation ; les nouvelles tables entrent dans les deux.

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
  `deleted_at`    TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`user_id`, `dav_name`),
  INDEX `ix_contact_tombstones_seq` (`user_id`, `sync_sequence`),
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
  `replaced_at` TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
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

Et écrire, sous le bloc, les quatre paragraphes qu'un relecteur redemanderait :

- **`seq` et non `sequence`** : `SEQUENCE` est un mot-clé MariaDB depuis 10.3, et une colonne qui
  n'existe qu'entre back-quotes est une erreur de production en attente, dans un projet où le SQL se
  passe à la main.
- **`dav_name` est nullable, `sync_sequence` ne l'est pas.** L'unicité MySQL ignore les `NULL`, donc
  la colonne peut rester vide sur les fiches que le rattrapage n'a pas encore atteintes sans que le
  premier `PUT` d'un client bute sur un doublon de vide. `sync_sequence` part de `0`, la valeur
  qu'un jeton ne réclame jamais : une fiche non rattrapée est invisible du protocole plutôt que
  servie sous un nom absent.
- **`contact_revisions` porte une clé technique, à l'inverse de ses voisines.** Les tombes sont un
  état — une par nom, la plus récente écrase la précédente — les révisions sont un journal :
  plusieurs lignes coexistent pour un même `dav_name` et rien ne les distingue qu'un ordre. Mettre
  `(user_id, dav_name, replaced_at)` en clé ferait de deux écritures dans la même seconde une
  collision, sur la table dont le rôle est précisément de ne rien perdre.
- **`vcard_raw` en `MEDIUMTEXT`, comme celui de `contacts`.** Les deux colonnes sont identiques pour
  que la donnée ne traverse aucune conversion entre elles : une carte lue dans une révision doit
  pouvoir être renvoyée telle quelle, sinon l'historique ne restitue pas ce qu'il a archivé.

Ajouter les trois tables et les deux colonnes à la section de désinstallation existante, dans
l'ordre inverse (`contacts` d'abord — les `DROP INDEX` puis les `DROP COLUMN` —, puis les trois
`DROP TABLE`).

- [ ] **Step 2 : Écrire le rattrapage**

Créer `assets/contacts-dav-backfill.sql`, sur le modèle en-tête-commenté de
`contacts-display-name-backfill.sql` :

```sql
-- Rattrapage 4c-ii : donne un nom de ressource et un rang aux fiches existantes.
--
-- À passer IMMÉDIATEMENT APRÈS le déploiement du backend, et l'ordre n'est pas une commodité :
-- entre le déploiement et ce script, les fiches existantes n'ont ni dav_name ni rang. Un client
-- CardDAV qui se connecterait dans cette fenêtre verrait un carnet vide et effacerait ses propres
-- copies en les croyant supprimées côté serveur. Aucune route /dav n'existe avant la tranche
-- 4c-ii-c ; ce fichier est écrit maintenant parce que le DDL qu'il complète l'est aussi.
--
-- IDEMPOTENT : chaque instruction ne touche que les lignes encore à NULL ou à 0. Un opérateur qui
-- le rejoue ne réattribue aucun nom et ne remet aucun compteur en arrière.
--
-- Rang 1 pour toutes les fiches d'un même utilisateur, et non un rang par fiche : elles arrivent
-- ensemble dans la première synchronisation, et aucun client n'existe encore pour distinguer leurs
-- rangs. Un rang par fiche coûterait un balayage ordonné pour un ordre que personne ne lit.

START TRANSACTION;

-- 1. La ligne d'état, une par utilisateur qui a au moins une fiche, avec son epoch.
INSERT INTO `contact_sync_state` (`user_id`, `epoch`, `seq`, `pruned_below`)
SELECT DISTINCT c.`user_id`, UUID(), 1, 0
FROM `contacts` c
ON DUPLICATE KEY UPDATE `seq` = GREATEST(`contact_sync_state`.`seq`, 1);

-- 2. Le nom de ressource. La convention {id}.vcf est celle des fiches nées dans le webmail ; les
--    clients l'affichent dans leurs journaux et il n'y a aucune raison de les dérouter.
UPDATE `contacts`
SET `dav_name` = CONCAT(`id`, '.vcf')
WHERE `dav_name` IS NULL;

-- 3. Le rang. La clause = 0 est ce qui rend le script rejouable : une fiche déjà rattrapée, ou
--    écrite depuis par le store, porte un rang > 0 et n'est pas touchée.
UPDATE `contacts`
SET `sync_sequence` = 1
WHERE `sync_sequence` = 0;

COMMIT;

-- CONTRÔLE — doit rendre 0. Tant qu'il ne rend pas 0, le carnet DAV est incomplet et ne doit pas
-- être ouvert aux clients.
SELECT COUNT(*) AS `restantes`
FROM `contacts`
WHERE `dav_name` IS NULL OR `sync_sequence` = 0;

-- CONTRÔLE — doit rendre 0. Une fiche que le rattrapage de 4a n'a pas atteinte n'a ni carte ni
-- condensat : elle est invisible du protocole par la clause de visibilité, ce qui est correct,
-- mais l'opérateur doit savoir qu'elles existent avant d'ouvrir le carnet.
SELECT COUNT(*) AS `sans_carte`
FROM `contacts`
WHERE `vcard_raw` IS NULL OR `card_hash` = '';
```

- [ ] **Step 3 : Écrire la rotation d'epoch**

Créer `assets/contacts-sync-epoch-rotate.sql`. **Elle est livrée comme un fichier versionné et non
comme une phrase dans un document de conception** : une consigne qu'il faut retrouver dans une spec
au moment d'une restauration est une consigne qui ne sera pas jouée.

```sql
-- À JOUER APRÈS TOUTE RESTAURATION D'UNE SAUVEGARDE de la base du webmail.
--
-- Une restauration rembobine contact_sync_state.seq. Le refus du jeton postérieur à la séquence
-- courante n'attrape que les clients les plus en avance : un jeton resté sous la séquence
-- restaurée passe, et couvre des rangs dont le contenu a changé — divergence silencieuse et
-- permanente, sur des téléphones qui continuent de synchroniser sans rien signaler.
--
-- Cette ligne rend étrangers au carnet tous les jetons émis par la base d'avant, et le ctag change
-- avec eux. Elle reste juste si elle est jouée deux fois.
--
-- POURQUOI L'EPOCH ET NON pruned_below = seq : les deux invalident les jetons, mais le second le
-- fait en déplaçant un filigrane dont le sens est « ces tombes-là n'existent plus », c'est-à-dire
-- en mentant sur autre chose pour obtenir l'effet voulu. L'epoch ne dit qu'une chose et la dit
-- entièrement : ce carnet n'est plus celui qui a émis vos jetons.

UPDATE `contact_sync_state` SET `epoch` = UUID();

-- LA REPRISE N'EST PAS UNIFORME CÔTÉ CLIENT, et il faut le savoir avant de prévenir les
-- utilisateurs :
--   * DAVx5 lit le 403 valid-sync-token et repart d'une synchronisation complète tout seul.
--   * Thunderbird ne retombe en synchronisation complète que sur un 400 : son code rejoue un jeton
--     refusé en 403 à chaque cycle, indéfiniment. Après cette rotation, un carnet Thunderbird est à
--     RÉ-APPAIRER À LA MAIN — supprimer le carnet et le recréer.
```

- [ ] **Step 4 : Écrire la note de restauration**

Créer `docs/superpowers/carddav-restore-prerequisite.md`, sur le modèle de
`reverse-proxy-prerequisite.md`. Elle porte : la commande ci-dessus et son fichier, la divergence
DAVx⁵/Thunderbird, et **ce que le contrôle de démarrage de la tâche 9 ne voit pas** — une
restauration *cohérente*, les deux tables rembobinées ensemble, le laisse muet, l'inégalité
`MAX(contacts.sync_sequence) <= contact_sync_state.seq` restant vraie. C'est le seul endroit de la
tranche où un incident n'a aucun symptôme côté client, et le contrôle n'en attrape que la moitié
détectable. Écrire cette limite dans la note, pour que personne ne s'y fie.

Y écrire aussi le compromis, parce qu'il est le seul endroit où ce serveur demande un geste humain
là où Radicale n'en demande aucun : le ctag de Radicale est dérivé du contenu de la collection, donc
une restauration le change toute seule — auto-réparant, au prix d'un recalcul à chaque
interrogation d'état. Le nôtre est un compteur, donc `O(1)` sur le chemin qu'un téléphone emprunte
toutes les quinze minutes, mais rembobinable. C'est le bon échange pour 5000 fiches, et il ne l'est
qu'à la condition que la ligne soit jouée.

- [ ] **Step 5 : Étendre la note de proxy inverse**

Ajouter à `docs/superpowers/reverse-proxy-prerequisite.md` une section « CardDAV » disant quoi
vérifier **avant** que le plan c n'ouvre les routes :

- que le proxy laisse passer `PROPFIND`, `PROPPATCH`, `REPORT`, `OPTIONS`, `HEAD`, `PUT` et
  `DELETE` ;
- qu'il ne retire ni `Depth`, ni `If-Match`, ni `If-None-Match`, ni `Authorization` — certaines
  configurations avalent l'en-tête `Authorization` sur les routes qu'elles croient publiques ;
- qu'il n'impose pas de plafond de corps inférieur au nôtre (1 Mo) ;
- **qu'il ne répond pas lui-même sur `/.well-known/`** — c'est le mode de panne le plus courant des
  CDN et des pare-feux applicatifs devant un serveur DAV : le chemin est intercepté au bord, le
  `301` n'atteint jamais le client, et l'appairage échoue sur un `404` avant la première requête
  authentifiée. Le contrôle tient en un `curl -X PROPFIND` depuis l'extérieur.

Écrire aussi le symptôme, parce que c'est lui qui coûte cher : `limit_except` ou pare-feu
applicatif refusent silencieusement, et **ce que voit le client est un carnet vide, sans erreur**.

- [ ] **Step 6 : Vérifier le SQL**

Il n'y a pas de test automatique ici. Relire les quatre blocs et vérifier à la main :

| Point | Attendu |
|---|---|
| Les noms de colonnes | identiques au caractère près à ceux du § « Le schéma » de la spec |
| Chaque table | `ENGINE=InnoDB`, `CHARSET=utf8mb4`, `COLLATE=utf8mb4_bin` |
| Chaque FK | `ON DELETE CASCADE` vers `users(id)` |
| `contact_tombstones` | PK `(user_id, dav_name)`, index `(user_id, sync_sequence)` |
| `contact_revisions` | PK technique `id`, et les **quatre** index |
| `contacts` | l'unique est `(user_id, dav_name)`, pas `dav_name` seul |
| Le rattrapage | rejoué deux fois, il ne change rien la seconde fois |
| Les deux contrôles | rendent `0` sur une base rattrapée |

- [ ] **Step 7 : Commit**

Message de commit — sujet, ligne vide, deux lignes de corps, puis le trailer. À poser avec
`git commit -F -` et un heredoc, jamais avec un here-string PowerShell :

- sujet : `docs(carddav): le DDL de la synchro, son rattrapage et la rotation d'epoch`
- corps : `Trois tables, deux colonnes sur contacts, et le .sql a jouer apres toute` /
  `restauration — versionne plutot qu'ecrit dans un paragraphe.`

`git add docs/superpowers/webmail-carddav-tables.md assets/contacts-dav-backfill.sql assets/contacts-sync-epoch-rotate.sql docs/superpowers/carddav-restore-prerequisite.md docs/superpowers/reverse-proxy-prerequisite.md`

---

### Task 2 : les trois entités, les deux colonnes et leurs arêtes

Le piège de cette tâche est celui que le paquet 1 de 4c-i a déjà payé une fois : **sans arête
déclarée dans `OnModelCreating`, EF ordonne les `INSERT` par nom de table** — `contact_revisions`
et `contact_sync_state` trient tous deux avant `users` — **et casse la clé étrangère à la première
création.** Le fournisseur InMemory n'applique aucune FK, donc **aucun test ne peut l'attraper** :
seule la déclaration le peut, et seule une relecture du contexte la vérifie.

**Files :**
- Create : `src/snoopy.microservice/Data/Preferences/ContactSyncState.cs`
- Create : `src/snoopy.microservice/Data/Preferences/ContactTombstone.cs`
- Create : `src/snoopy.microservice/Data/Preferences/ContactRevision.cs`
- Create : `src/snoopy.microservice/Data/Preferences/RevisionCause.cs`
- Modify : `src/snoopy.microservice/Data/Preferences/Contact.cs`
- Modify : `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Data/ContactSyncEntitiesTests.cs`

**Interfaces :**
- Produit, consommé par les tâches 3 à 9 :

```csharp
public enum RevisionCause { Put, Webmail, Import, Delete, Rejected }

// ContactSyncState : UserId (Guid, clé), Epoch (Guid), Seq (ulong), PrunedBelow (ulong)
// ContactTombstone : UserId (Guid), DavName (string), SyncSequence (ulong), DeletedAt (DateTime)
// ContactRevision  : Id (ulong, identité), UserId (Guid), ContactId (Guid?), Uid (string?),
//                    DavName (string?), CardHash (string), VCardRaw (string),
//                    Cause (RevisionCause), ReplacedAt (DateTime)
// Contact          : + DavName (string?), + SyncSequence (ulong)
```

`Seq`, `PrunedBelow` et `SyncSequence` sont des `ulong` parce que la colonne est
`BIGINT UNSIGNED` : un `long` y passerait aussi, mais rendrait négatif ce que le SQL n'écrira
jamais, et la comparaison de jeton `> n` est le seul endroit du protocole où un signe se paierait.

- [ ] **Step 1 : Écrire les tests, rouges**

Créer `snoopy.microservice.Tests/Data/ContactSyncEntitiesTests.cs`. Les trois premiers tests
vérifient ce que l'InMemory sait vérifier — les colonnes, la clé, la conversion de l'énumération —
et le quatrième vérifie **la seule chose qui compte vraiment** : que les arêtes sont déclarées.
Il les lit sur le modèle EF, pas sur une insertion, précisément parce qu'une insertion ne les
verrait pas.

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace snoopy.microservice.Tests.Data;

public sealed class ContactSyncEntitiesTests
{
    private static PreferencesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PreferencesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public void TheSyncState_IsKeyedOnTheUserAlone()
    {
        using var context = NewContext();

        var key = context.Model.FindEntityType(typeof(ContactSyncState))!.FindPrimaryKey()!;

        // One row per user and not two: a technical key plus an index would let the table accept a
        // second state row that nothing in the code creates — until a restore puts one there.
        Assert.Equal(["UserId"], key.Properties.Select(p => p.Name));
    }

    [Fact]
    public void ATombstone_IsKeyedOnTheUserAndTheName()
    {
        using var context = NewContext();

        var key = context.Model.FindEntityType(typeof(ContactTombstone))!.FindPrimaryKey()!;

        Assert.Equal(["UserId", "DavName"], key.Properties.Select(p => p.Name));
    }

    [Fact]
    public void ARevision_IsKeyedOnItsOwnIdentity()
    {
        using var context = NewContext();

        var entity = context.Model.FindEntityType(typeof(ContactRevision))!;

        // A journal, not a state: several rows coexist for one dav_name and only an order tells
        // them apart. (user_id, dav_name, replaced_at) would make two writes in the same second a
        // collision, on the table whose whole job is to lose nothing.
        Assert.Equal(["Id"], entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }

    [Fact]
    public void TheCause_IsStoredAsItsName()
    {
        using var context = NewContext();

        var cause = context.Model.FindEntityType(typeof(ContactRevision))!.FindProperty("Cause")!;

        // The column is an ENUM of lowercase words; an int conversion would write 0..4 into it and
        // MySQL would take the number as an ordinal — silently off by one, since ENUM is 1-based.
        Assert.Equal(typeof(string), cause.GetProviderClrType());
    }

    [Theory]
    [InlineData(typeof(ContactSyncState))]
    [InlineData(typeof(ContactTombstone))]
    [InlineData(typeof(ContactRevision))]
    public void EveryNewEntity_DeclaresItsEdgeToTheUser(Type entity)
    {
        using var context = NewContext();

        var foreignKeys = context.Model.FindEntityType(entity)!.GetForeignKeys().ToList();

        // The one assertion the InMemory provider cannot make for us: it enforces no foreign key,
        // so an insert-based test passes with the edge missing. Without it EF orders the INSERTs by
        // table name — all three sort before "users" — and breaks the FK on the first create.
        var toUser = Assert.Single(foreignKeys, fk => fk.PrincipalEntityType.ClrType == typeof(WebmailUser));
        Assert.Equal(DeleteBehavior.Cascade, toUser.DeleteBehavior);
    }

    [Fact]
    public async Task AContact_CarriesItsNameAndRank()
    {
        using var context = NewContext();
        var id = Guid.NewGuid();
        context.Contacts.Add(new Contact
        {
            Id = id, UserId = Guid.NewGuid(), Uid = id.ToString(),
            DavName = $"{id}.vcf", SyncSequence = 7
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var stored = await context.Contacts.SingleAsync(CancellationToken.None);

        Assert.Equal($"{id}.vcf", stored.DavName);
        Assert.Equal(7ul, stored.SyncSequence);
    }

    [Fact]
    public async Task AContact_DefaultsToRankZeroAndNoName()
    {
        using var context = NewContext();
        var id = Guid.NewGuid();
        context.Contacts.Add(new Contact { Id = id, UserId = Guid.NewGuid(), Uid = id.ToString() });
        await context.SaveChangesAsync(CancellationToken.None);

        var stored = await context.Contacts.SingleAsync(CancellationToken.None);

        // Rank 0 is the value a sync token never asks for (it asks `> n` with `n >= 0`), so a row
        // the backfill has not reached is invisible to the protocol rather than served nameless.
        Assert.Null(stored.DavName);
        Assert.Equal(0ul, stored.SyncSequence);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactSyncEntitiesTests`
Expected : ne compile pas — `ContactSyncState`, `ContactTombstone`, `ContactRevision` et
`Contact.DavName` n'existent pas.

- [ ] **Step 3 : Écrire l'énumération**

Créer `Data/Preferences/RevisionCause.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// Why a card was archived. Without it one cannot tell an overwrite to undo from a wanted edit,
/// which is the only question anybody asks of this table.
/// </summary>
public enum RevisionCause
{
    /// <summary>A CardDAV PUT replaced the card.</summary>
    Put,

    /// <summary>The webmail editor replaced it.</summary>
    Webmail,

    /// <summary>An import merged over it.</summary>
    Import,

    /// <summary>The card was deleted, by whichever door.</summary>
    Delete,

    /// <summary>
    /// A PUT body refused on a precondition, archived before the 412 leaves. DAVx5 applies
    /// "the server wins" without consulting anyone, so the refused version is otherwise lost.
    /// </summary>
    Rejected
}
```

- [ ] **Step 4 : Écrire les trois entités**

Créer `Data/Preferences/ContactSyncState.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One row per user, holding the counter every sync token and ctag is cut from. Created on demand:
/// a user born after the deployment has none, and a first write with no row to lock has no rank to
/// take.
/// </summary>
[Table("contact_sync_state")]
public sealed class ContactSyncState
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Drawn once and never moved in normal operation. A restore rewinds <see cref="Seq"/>, and
    /// rotating this is what makes every token the old database issued foreign to the book.
    /// </summary>
    [Column("epoch")]
    public Guid Epoch { get; set; }

    /// <summary>
    /// Named <c>seq</c> because SEQUENCE is a MariaDB keyword since 10.3, and a column that only
    /// exists between back-quotes is a production error waiting in a project where SQL is run by
    /// hand.
    /// </summary>
    [Column("seq")]
    public ulong Seq { get; set; }

    /// <summary>
    /// The highest pruned rank. A token at or below it is unrecoverable — the tombstones it would
    /// need are gone — and answers 403 valid-sync-token rather than silently omitting a deletion.
    /// </summary>
    [Column("pruned_below")]
    public ulong PrunedBelow { get; set; }
}
```

Créer `Data/Preferences/ContactTombstone.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// A name that disappeared, and the rank at which it did. A state and not a journal: one row per
/// name, the newest overwriting the previous, because a client of sync-collection never asks for
/// the path travelled — only for the state on arrival.
/// </summary>
[Table("contact_tombstones")]
public sealed class ContactTombstone
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("dav_name")]
    public string DavName { get; set; } = string.Empty;

    [Column("sync_sequence")]
    public ulong SyncSequence { get; set; }

    [Column("deleted_at")]
    public DateTime DeletedAt { get; set; }
}
```

Créer `Data/Preferences/ContactRevision.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// The bytes a write replaced or a deletion removed, kept for thirty days. Bytes and not a diff:
/// vcard_raw is already the sovereign data, and a revision that had to be replayed to be read
/// would not be a backup.
/// </summary>
[Table("contact_revisions")]
public sealed class ContactRevision
{
    [Column("id")]
    public ulong Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>The contact while it still exists; a <c>Delete</c> revision outlives its own.</summary>
    [Column("contact_id")]
    public Guid? ContactId { get; set; }

    /// <summary>
    /// The archived card's UID, the identity arbiter. Null only when a refused body does not parse:
    /// a valid card without a UID must archive rather than die on a constraint, on the table whose
    /// whole job is to lose nothing.
    /// </summary>
    [Column("uid")]
    public string? Uid { get; set; }

    [Column("dav_name")]
    public string? DavName { get; set; }

    [Column("card_hash")]
    public string CardHash { get; set; } = string.Empty;

    [Column("vcard_raw")]
    public string VCardRaw { get; set; } = string.Empty;

    [Column("cause")]
    public RevisionCause Cause { get; set; }

    [Column("replaced_at")]
    public DateTime ReplacedAt { get; set; }
}
```

- [ ] **Step 5 : Ajouter les deux colonnes à `Contact`**

Dans `Data/Preferences/Contact.cs`, à la suite de `CardHash` :

```csharp
    /// <summary>
    /// The resource name a CardDAV client chose, or <c>{id}.vcf</c> for a card born here. Nullable
    /// because MySQL uniqueness ignores NULL: a row the backfill has not reached can stay empty
    /// without the first client PUT tripping on a duplicate of nothing.
    /// </summary>
    [Column("dav_name")]
    public string? DavName { get; set; }

    /// <summary>
    /// The rank of the last write that changed this card. Zero means never backfilled, and zero is
    /// the value a sync token never asks for — such a row is invisible to the protocol rather than
    /// served under a name it does not have.
    /// </summary>
    [Column("sync_sequence")]
    public ulong SyncSequence { get; set; }
```

- [ ] **Step 6 : Déclarer les arêtes**

Dans `PreferencesDbContext.OnModelCreating`, à la suite du bloc `DavCredential` :

```csharp
        modelBuilder.Entity<ContactSyncState>().HasKey(s => s.UserId);
        modelBuilder.Entity<ContactTombstone>().HasKey(t => new { t.UserId, t.DavName });
        modelBuilder.Entity<ContactRevision>().HasKey(r => r.Id);
        modelBuilder.Entity<ContactRevision>()
            .Property(r => r.Cause)
            .HasConversion<string>()
            .HasMaxLength(8);

        // Same mechanism as every table above: all three sort before "users", so without a declared
        // edge EF orders the INSERTs by table name and breaks the FK on any create. Declared without
        // navigation, like their neighbours. The InMemory provider enforces no foreign key, so no
        // test can catch this — only the declaration can.
        modelBuilder.Entity<ContactSyncState>()
            .HasOne<WebmailUser>().WithMany()
            .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContactTombstone>()
            .HasOne<WebmailUser>().WithMany()
            .HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContactRevision>()
            .HasOne<WebmailUser>().WithMany()
            .HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Contact>().HasIndex(c => new { c.UserId, c.DavName }).IsUnique();
```

et les trois `DbSet` à côté des existants :

```csharp
    public DbSet<ContactSyncState> ContactSyncStates => Set<ContactSyncState>();
    public DbSet<ContactTombstone> ContactTombstones => Set<ContactTombstone>();
    public DbSet<ContactRevision> ContactRevisions => Set<ContactRevision>();
```

`Cause` est converti en **chaîne** et non en entier : la colonne est un `ENUM` de mots minuscules,
et une conversion en `int` y écrirait `0..4` que MySQL prendrait pour un ordinal — décalé d'un
rang, l'`ENUM` étant indexé à partir de 1. Pomelo écrit `Put` là où la colonne attend `put` : la
comparaison d'`ENUM` de MariaDB est insensible à la casse sous une collation `_bin` de **colonne**
mais pas de valeur, donc **déclarer les valeurs de l'`ENUM` en minuscules et laisser EF écrire la
casse C# marcherait par accident**. Écrire la conversion explicitement en minuscules :
`.HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<RevisionCause>(v, true))`.

- [ ] **Step 7 : Lancer les tests**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactSyncEntitiesTests`
Expected : les huit cas PASS.

Run : `cd src && dotnet test`
Expected : la suite entière au vert.

Run : `cd src && dotnet build`
Expected : zéro avertissement.

- [ ] **Step 8 : Commit**

Réverter d'abord `git checkout -- src/snoopy.microservice/ApiDocumentation.xml`.

- sujet : `feat(carddav): les trois entites de synchro et leurs aretes`
- corps : `contacts gagne dav_name et sync_sequence ; les aretes vers users sont` /
  `declarees, seul endroit ou l'InMemory ne peut rien verifier.`

---

## Paquet 2 — le dépôt de synchronisation

### Ce que ce paquet doit décider avant d'écrire une ligne, et qui est décidé ici

**Le dépôt n'exécute nulle part de SQL brut, et c'est délibéré.** `TrustedSenderStore.cs:76` porte
le commentaire qui le dit : « Loaded then removed rather than `ExecuteDeleteAsync` : the InMemory
provider the tests run … ». Aucun `ExecuteSqlRaw`, aucun `FromSqlRaw`, aucun `BeginTransaction`
n'existe dans tout `src/snoopy.microservice`.

Or la décision 6 exige exactement cela : `UPDATE … SET seq = seq + 1` **sous le verrou exclusif de
la ligne d'état, tenu jusqu'au `COMMIT`, dans la même transaction que l'écriture qu'il numérote**.
La spec écrit que c'est « l'unique raison pour laquelle le jeton est sûr », et elle a raison : un
`MAX(sync_sequence) + 1`, ou une lecture-modification-écriture au niveau EF, court après deux
écritures simultanées et rend deux fiches au même rang — un client qui synchronise entre les deux
en perd une définitivement, sans erreur nulle part.

Les deux contraintes ne se concilient pas. **Le SQL brut gagne, et voici ce que cela coûte, écrit
pour que personne ne le redécouvre en revue :**

- L'incrément est du SQL MySQL — `INSERT … ON DUPLICATE KEY UPDATE` — donc **le fournisseur
  InMemory ne peut pas l'exécuter, et SQLite non plus** : la syntaxe est `ON CONFLICT DO UPDATE`
  là-bas. Aucun test automatique de ce dépôt ne peut prouver l'atomicité.
- Il est donc confiné à **une seule méthode**, aussi petite que possible, derrière une interface.
  Tout le reste du dépôt — tombes, révisions, élagage — reste en EF et **reste testable**.
- Les tests des appelants (tâches 5 à 7) portent sur `IContactSyncStore` **doublé**, pas sur son
  implémentation : ce qu'ils prouvent est que chaque porte demande son rang, dans le bon ordre, et
  archive avant d'écraser. C'est ce qui est vérifiable, et c'est l'essentiel de ce qui peut casser.
- Ce qui reste invérifiable — que deux transactions concurrentes ne prennent pas le même rang — est
  vérifié **à la main, une fois**, par la procédure de l'étape 6 de la tâche 3, et le résultat est
  consigné. C'est le seul endroit du plan où une propriété de correction n'a pas de test.

**Ne pas essayer de contourner** en écrivant une variante EF « pour les tests » : deux chemins pour
un invariant est la façon dont on croit l'avoir testé.

---

### Task 3 : `IContactSyncStore` — la séquence sous verrou et la ligne d'état à la demande

**Files :**
- Create : `src/snoopy.microservice/Repositories/IContactSyncStore.cs`
- Create : `src/snoopy.microservice/Repositories/ContactSyncStore.cs`
- Create : `src/snoopy.microservice/Models/Contacts/SyncState.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactSyncStoreTests.cs`

**Interfaces :**
- Consomme : `ContactSyncState`, `RevisionCause` (tâche 2).
- Produit, consommé par les tâches 4 à 9 :

```csharp
public sealed record SyncState(Guid Epoch, ulong Seq, ulong PrunedBelow);

internal interface IContactSyncStore
{
    /// Advances the counter under the state row's own exclusive lock and answers the new rank.
    /// Creates the row at seq = 0 with a fresh epoch when it is missing. MUST be called inside a
    /// transaction the caller owns, and FIRST — before any contact row is touched.
    Task<ulong> NextSequenceAsync(Guid userId, CancellationToken cancellationToken);

    /// The state as it stands, creating nothing. Null when the user has never had one — a getctag
    /// on an empty book answers 0 without writing.
    Task<SyncState?> ReadStateAsync(Guid userId, CancellationToken cancellationToken);

    /// The state, created at seq = 0 with a fresh epoch if missing. A sync-collection on an empty
    /// book needs an epoch to form its token, so it creates one; a pure read does not.
    Task<SyncState> ReadOrCreateStateAsync(Guid userId, CancellationToken cancellationToken);
}
```

- [ ] **Step 1 : Écrire `SyncState`**

Créer `Models/Contacts/SyncState.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// The three numbers every token and ctag is cut from, read together. The watermark and the
/// tombstones must be read in the same transaction — the same InnoDB snapshot: a prune slipping in
/// between would make the response miss deletions under a watermark already stale, and reopen by a
/// race the very hole the column closes.
/// </summary>
/// <param name="Epoch">Rotated only by a restore; it makes every token the old database issued foreign.</param>
/// <param name="Seq">The rank of the most recent write.</param>
/// <param name="PrunedBelow">A token at or below this is unrecoverable.</param>
public sealed record SyncState(Guid Epoch, ulong Seq, ulong PrunedBelow);
```

- [ ] **Step 2 : Écrire les tests, rouges**

Créer `snoopy.microservice.Tests/Repositories/ContactSyncStoreTests.cs`. **Ces tests ne couvrent que
ce que l'InMemory sait exécuter** : les deux lectures. `NextSequenceAsync` n'y figure pas — voir le
préambule du paquet — et son absence est déclarée par un test qui l'écrit noir sur blanc, pour
qu'aucune revue ne la lise comme un oubli.

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;

namespace snoopy.microservice.Tests.Repositories;

public sealed class ContactSyncStoreTests
{
    private static PreferencesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PreferencesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task ReadState_AnswersNothingRatherThanCreatingARow()
    {
        using var context = NewContext();
        var store = new ContactSyncStore(context);

        var state = await store.ReadStateAsync(Guid.NewGuid(), CancellationToken.None);

        // A getctag on a book that has never synced must not write: an empty book answers 0, and a
        // read that creates rows makes every poll a write on the busiest path a phone takes.
        Assert.Null(state);
        Assert.Empty(context.ContactSyncStates);
    }

    [Fact]
    public async Task ReadState_AnswersTheThreeNumbersTogether()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userId, Epoch = epoch, Seq = 42, PrunedBelow = 7
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        var state = await store.ReadStateAsync(userId, CancellationToken.None);

        Assert.Equal(new SyncState(epoch, 42, 7), state);
    }

    [Fact]
    public async Task ReadOrCreate_DrawsAnEpochOnTheFirstCall()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var store = new ContactSyncStore(context);

        var state = await store.ReadOrCreateStateAsync(userId, CancellationToken.None);

        // sync-collection on an empty book needs an epoch to form its token, so this one creates.
        Assert.NotEqual(Guid.Empty, state.Epoch);
        Assert.Equal(0ul, state.Seq);
        Assert.Equal(0ul, state.PrunedBelow);
    }

    [Fact]
    public async Task ReadOrCreate_KeepsTheEpochItAlreadyDrew()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var store = new ContactSyncStore(context);

        var first = await store.ReadOrCreateStateAsync(userId, CancellationToken.None);
        var second = await store.ReadOrCreateStateAsync(userId, CancellationToken.None);

        // The epoch is what makes a token belong to this book. Redrawing it on a second read would
        // silently invalidate every client's token on every poll.
        Assert.Equal(first.Epoch, second.Epoch);
        Assert.Single(context.ContactSyncStates);
    }

    [Fact]
    public void TheIncrement_IsRawSqlAndThereforeUntestedHere()
    {
        // Deliberate, and written as a test so a review reads it as a decision rather than a gap.
        // NextSequenceAsync is `INSERT ... ON DUPLICATE KEY UPDATE seq = seq + 1`: MySQL syntax the
        // InMemory provider cannot run and SQLite spells differently. Its atomicity — two
        // concurrent transactions never taking the same rank — is verified by hand against
        // MariaDB, once, by the procedure in Task 3 Step 6, and nowhere else. Writing an EF variant
        // "for the tests" would give two paths for one invariant, which is how one comes to believe
        // an untested thing is tested.
        var method = typeof(ContactSyncStore).GetMethod(nameof(ContactSyncStore.NextSequenceAsync));

        Assert.NotNull(method);
    }
}
```

- [ ] **Step 3 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactSyncStoreTests`
Expected : ne compile pas — `ContactSyncStore` n'existe pas.

- [ ] **Step 4 : Écrire l'interface**

Créer `Repositories/IContactSyncStore.cs` avec les trois signatures du bloc « Interfaces »
ci-dessus, chacune portant sa balise `<summary>`. L'interface est `internal` : elle ne sort pas de
l'assemblage, et rien du frontend ne la voit.

- [ ] **Step 5 : Écrire l'implémentation**

Créer `Repositories/ContactSyncStore.cs` :

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="IContactSyncStore"/>
internal sealed class ContactSyncStore(PreferencesDbContext context) : IContactSyncStore
{
    public async Task<ulong> NextSequenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Raw SQL, and the only raw SQL in the repository. Three things it must do in one
        // statement: create the row when the user has none, take the row's exclusive lock, and
        // advance the counter — all inside the caller's transaction, so InnoDB holds that lock
        // until COMMIT and a second writer cannot get its rank before the first is visible.
        // Splitting them — take a number, then write in another transaction — reopens the hole from
        // the other end: rank 11 committed before rank 10, and a client syncing in between takes
        // token 11 and never sees 10.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO contact_sync_state (user_id, epoch, seq, pruned_below)
             VALUES ({userId}, {Guid.NewGuid()}, 1, 0)
             ON DUPLICATE KEY UPDATE seq = seq + 1
             """,
            cancellationToken);

        // Re-read inside the same transaction: the statement above cannot return the new value, and
        // LAST_INSERT_ID() would answer the auto-increment of a table that has none.
        var seq = await context.ContactSyncStates
            .Where(s => s.UserId == userId)
            .Select(s => s.Seq)
            .SingleAsync(cancellationToken);

        return seq;
    }

    public async Task<SyncState?> ReadStateAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.ContactSyncStates
            .Where(s => s.UserId == userId)
            .Select(s => new SyncState(s.Epoch, s.Seq, s.PrunedBelow))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SyncState> ReadOrCreateStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var held = await ReadStateAsync(userId, cancellationToken);
        if (held is not null) return held;

        var created = new ContactSyncState
        {
            UserId = userId, Epoch = Guid.NewGuid(), Seq = 0, PrunedBelow = 0
        };
        context.ContactSyncStates.Add(created);
        await context.SaveChangesAsync(cancellationToken);

        return new SyncState(created.Epoch, 0, 0);
    }
}
```

**Le `Guid.NewGuid()` de la ligne `VALUES` est tiré à chaque appel, y compris quand la ligne existe
déjà — et c'est sans conséquence** : la branche `ON DUPLICATE KEY UPDATE` ne touche que `seq`, donc
l'`epoch` déjà en base n'est jamais réécrit. Le porter en commentaire dans le code : un relecteur
qui voit un GUID neuf à chaque incrément soupçonnera exactement l'inverse, et l'epoch qui bouge est
précisément le défaut qui invaliderait tous les jetons à chaque écriture.

Enregistrer le dépôt à côté de ses voisins dans `ApplicationServicesConfiguration` :
`services.AddScoped<IContactSyncStore, ContactSyncStore>();`

- [ ] **Step 6 : Vérifier l'atomicité à la main, une fois**

C'est la seule propriété de correction de tout le plan qui n'a pas de test, et elle se vérifie donc
à la main, contre `snoopy_webmail_dev`. Deux sessions `mysql` côte à côte :

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

Ce qu'il faut observer, et consigner dans le rapport de tâche :

| Observation | Attendu |
|---|---|
| La session B au moment de son `INSERT` | **bloque**, elle ne rend pas la main |
| B se débloque | au `COMMIT` de A, pas avant |
| Le `seq` lu par A puis par B | deux valeurs **distinctes** et consécutives |
| Après les deux `COMMIT` | `seq` a avancé de exactement 2 |
| L'`epoch` | **inchangé** entre les deux, malgré le `UUID()` dans chaque `VALUES` |

Si B ne bloque pas, l'incrément n'est pas sous verrou et **rien de la synchronisation n'est sûr** :
arrêter et le signaler, ne pas continuer le plan.

- [ ] **Step 7 : Lancer les tests**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactSyncStoreTests`
Expected : les cinq cas PASS.

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 8 : Commit**

Réverter d'abord `ApiDocumentation.xml`.

- sujet : `feat(carddav): la sequence avance sous le verrou de sa propre ligne`
- corps : `Le seul SQL brut du depot, confine a une methode : l'InMemory ne sait pas` /
  `l'executer, et une variante EF donnerait deux chemins pour un invariant.`

---

### Task 4 : les tombes, les révisions, et le filigrane qui les rend sûres

Tout est en EF ici, donc tout est testable. Deux invariants portent la tâche, et ils se lisent
comme des règles d'ordre :

1. **Poser une tombe est un remplacement, jamais une insertion nue.** La clé étant
   `(user_id, dav_name)`, un nom supprimé, recréé, puis supprimé à nouveau retombe sur une ligne
   existante — et un `INSERT` nu ferait échouer la seconde suppression sur une violation de clé,
   **en production, sur une donnée que l'utilisateur croit effacée**.
2. **L'élagage écrit le filigrane d'abord, supprime les tombes ensuite, et les deux dans une seule
   transaction.** Les deux erreurs ne se valent pas : un filigrane trop haut refuse un jeton qui
   aurait pu être servi et coûte une resynchronisation complète — un désagrément mesurable ; un
   filigrane trop bas accepte un jeton dont la tombe n'existe plus et **perd la suppression pour de
   bon**. Quand deux écritures ne peuvent pas être simultanées, on ordonne du côté où l'erreur se
   rattrape.

**Files :**
- Modify : `src/snoopy.microservice/Repositories/IContactSyncStore.cs`
- Modify : `src/snoopy.microservice/Repositories/ContactSyncStore.cs`
- Create : `src/snoopy.microservice/Models/Contacts/PruneOutcome.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactSyncStoreTombstoneTests.cs`

**Interfaces :**
- Consomme : `SyncState`, `ContactTombstone`, `ContactRevision`, `RevisionCause` (tâches 2 et 3).
- Produit, consommé par les tâches 5 à 9 :

```csharp
public sealed record PruneOutcome(int Tombstones, int Revisions);

// s'ajoutent à IContactSyncStore :
Task PlaceTombstoneAsync(Guid userId, string davName, ulong sequence, CancellationToken cancellationToken);
Task LiftTombstoneAsync(Guid userId, string davName, CancellationToken cancellationToken);
Task<bool> ArchiveAsync(ContactRevision revision, CancellationToken cancellationToken);
Task<PruneOutcome> PruneAsync(DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken cancellationToken);
```

`ArchiveAsync` rend `bool` : `false` quand la fenêtre de dédoublonnage a écarté la ligne. La tâche 9
en a besoin pour journaliser « archivée » plutôt que « écartée », et un `void` rendrait les deux
cas indiscernables sur le chemin dont le rôle est de ne rien perdre.

- [ ] **Step 1 : Écrire les tests, rouges**

Créer `snoopy.microservice.Tests/Repositories/ContactSyncStoreTombstoneTests.cs` :

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;

namespace snoopy.microservice.Tests.Repositories;

public sealed class ContactSyncStoreTombstoneTests
{
    private static PreferencesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PreferencesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ContactRevision Revision(
        Guid userId, string? davName, string hash, RevisionCause cause, DateTime at) =>
        new()
        {
            UserId = userId, ContactId = Guid.NewGuid(), Uid = "uid-1", DavName = davName,
            CardHash = hash, VCardRaw = "BEGIN:VCARD\r\nEND:VCARD\r\n", Cause = cause,
            ReplacedAt = at
        };

    [Fact]
    public async Task ATombstone_IsWrittenOnce()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var store = new ContactSyncStore(context);

        await store.PlaceTombstoneAsync(userId, "a.vcf", 5, CancellationToken.None);

        var stone = Assert.Single(context.ContactTombstones);
        Assert.Equal("a.vcf", stone.DavName);
        Assert.Equal(5ul, stone.SyncSequence);
    }

    [Fact]
    public async Task ANameDeletedTwice_KeepsOneTombstoneAtTheNewerRank()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var store = new ContactSyncStore(context);

        await store.PlaceTombstoneAsync(userId, "a.vcf", 5, CancellationToken.None);
        await store.LiftTombstoneAsync(userId, "a.vcf", CancellationToken.None);
        await store.PlaceTombstoneAsync(userId, "a.vcf", 9, CancellationToken.None);

        // Deleted, recreated, deleted again lands on the same key. A bare INSERT would fail the
        // second deletion on a duplicate key — in production, on data the user believes gone.
        var stone = Assert.Single(context.ContactTombstones);
        Assert.Equal(9ul, stone.SyncSequence);
    }

    [Fact]
    public async Task ATombstoneReplaced_WithoutBeingLifted_TakesTheNewerRankToo()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var store = new ContactSyncStore(context);

        await store.PlaceTombstoneAsync(userId, "a.vcf", 5, CancellationToken.None);
        await store.PlaceTombstoneAsync(userId, "a.vcf", 9, CancellationToken.None);

        // The same path without the lift: recreation through a door that forgot to lift must not
        // turn the second burial into a crash either.
        var stone = Assert.Single(context.ContactTombstones);
        Assert.Equal(9ul, stone.SyncSequence);
    }

    [Fact]
    public async Task LiftingATombstoneThatIsNotThere_IsQuiet()
    {
        using var context = NewContext();
        var store = new ContactSyncStore(context);

        await store.LiftTombstoneAsync(Guid.NewGuid(), "never-buried.vcf", CancellationToken.None);

        // Every create lifts, and most names were never buried. Throwing here would make the
        // ordinary path carry a try/catch.
        Assert.Empty(context.ContactTombstones);
    }

    [Fact]
    public async Task ARevision_IsArchived()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var store = new ContactSyncStore(context);

        var archived = await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Put, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(archived);
        Assert.Single(context.ContactRevisions);
    }

    [Fact]
    public async Task TheSameRejectedBody_WithinTwentyFourHours_IsArchivedOnce()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new ContactSyncStore(context);

        await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now.AddHours(-1)),
            CancellationToken.None);
        var second = await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now),
            CancellationToken.None);

        // A client in disagreement is not in disagreement once but on every cycle: a phone replaying
        // the same card every quarter hour writes one revision, not ninety-six.
        Assert.False(second);
        Assert.Single(context.ContactRevisions);
    }

    [Fact]
    public async Task TheSameRejectedBody_OnTwoNames_IsArchivedTwice()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new ContactSyncStore(context);

        await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now.AddHours(-1)),
            CancellationToken.None);
        var second = await store.ArchiveAsync(
            Revision(userId, "b.vcf", "h1", RevisionCause.Rejected, now),
            CancellationToken.None);

        // The name is part of the key: the same body refused on two names is two facts.
        Assert.True(second);
        Assert.Equal(2, context.ContactRevisions.Count());
    }

    [Fact]
    public async Task TheSameRejectedBody_BeyondTwentyFourHours_IsArchivedAgain()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new ContactSyncStore(context);

        await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now.AddHours(-25)),
            CancellationToken.None);
        var second = await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Rejected, now),
            CancellationToken.None);

        Assert.True(second);
        Assert.Equal(2, context.ContactRevisions.Count());
    }

    [Fact]
    public async Task TheDeduplicationWindow_DoesNotApplyToAnAcceptedWrite()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new ContactSyncStore(context);

        await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Put, now.AddHours(-1)),
            CancellationToken.None);
        var second = await store.ArchiveAsync(
            Revision(userId, "a.vcf", "h1", RevisionCause.Put, now),
            CancellationToken.None);

        // The window exists for a client looping on a refusal. Two accepted writes that happen to
        // land on the same hash are two facts, and dropping the second would lose an overwrite.
        Assert.True(second);
        Assert.Equal(2, context.ContactRevisions.Count());
    }

    [Fact]
    public async Task Pruning_RaisesTheWatermarkBeforeItRemovesAnything()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userId, Epoch = Guid.NewGuid(), Seq = 20, PrunedBelow = 0
        });
        context.ContactTombstones.Add(new ContactTombstone
        {
            UserId = userId, DavName = "old.vcf", SyncSequence = 4, DeletedAt = now.AddDays(-200)
        });
        context.ContactTombstones.Add(new ContactTombstone
        {
            UserId = userId, DavName = "recent.vcf", SyncSequence = 12, DeletedAt = now.AddDays(-2)
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        var outcome = await store.PruneAsync(now.AddDays(-180), now.AddDays(-30), CancellationToken.None);

        Assert.Equal(1, outcome.Tombstones);
        // The watermark is the highest rank pruned, so a token at or below 4 is now unrecoverable
        // and must answer 403 rather than silently omitting the deletion it can no longer describe.
        var state = await store.ReadStateAsync(userId, CancellationToken.None);
        Assert.Equal(4ul, state!.PrunedBelow);
        Assert.Single(context.ContactTombstones);
    }

    [Fact]
    public async Task Pruning_NeverLowersTheWatermark()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userId, Epoch = Guid.NewGuid(), Seq = 40, PrunedBelow = 30
        });
        context.ContactTombstones.Add(new ContactTombstone
        {
            UserId = userId, DavName = "old.vcf", SyncSequence = 4, DeletedAt = now.AddDays(-200)
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        await store.PruneAsync(now.AddDays(-180), now.AddDays(-30), CancellationToken.None);

        // GREATEST, and it is what makes the sweep safe on several instances at once: the write is
        // commutative, and a DELETE that no longer finds its rows is a DELETE of zero rows.
        var state = await store.ReadStateAsync(userId, CancellationToken.None);
        Assert.Equal(30ul, state!.PrunedBelow);
    }

    [Fact]
    public async Task Pruning_WithNothingToRemove_LeavesTheWatermarkAlone()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userId, Epoch = Guid.NewGuid(), Seq = 8, PrunedBelow = 0
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        var outcome = await store.PruneAsync(now.AddDays(-180), now.AddDays(-30), CancellationToken.None);

        Assert.Equal(new PruneOutcome(0, 0), outcome);
        var state = await store.ReadStateAsync(userId, CancellationToken.None);
        Assert.Equal(0ul, state!.PrunedBelow);
    }

    [Fact]
    public async Task Pruning_TakesRevisionsOnTheirOwnClock()
    {
        using var context = NewContext();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        context.ContactRevisions.Add(Revision(userId, "a.vcf", "h1", RevisionCause.Put, now.AddDays(-40)));
        context.ContactRevisions.Add(Revision(userId, "b.vcf", "h2", RevisionCause.Put, now.AddDays(-10)));
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactSyncStore(context);

        var outcome = await store.PruneAsync(now.AddDays(-180), now.AddDays(-30), CancellationToken.None);

        // Thirty days and not a hundred and eighty, and the asymmetry is meant: the tombstone is
        // what the PROTOCOL must still be able to tell a client gone a long time, the revision is
        // what a HUMAN might still want back.
        Assert.Equal(1, outcome.Revisions);
        Assert.Single(context.ContactRevisions);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactSyncStoreTombstoneTests`
Expected : ne compile pas — les quatre méthodes n'existent pas.

- [ ] **Step 3 : Écrire `PruneOutcome`**

Créer `Models/Contacts/PruneOutcome.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>What one prune removed, for the sweeper's heartbeat line.</summary>
public sealed record PruneOutcome(int Tombstones, int Revisions);
```

- [ ] **Step 4 : Écrire les quatre méthodes**

Ajouter à `ContactSyncStore` :

```csharp
    public async Task PlaceTombstoneAsync(
        Guid userId, string davName, ulong sequence, CancellationToken cancellationToken)
    {
        // Upsert and not insert: the key is (user_id, dav_name), so a name deleted, recreated and
        // deleted again lands on an existing row. A bare INSERT would fail that second deletion on
        // a duplicate key — in production, on data the user believes gone.
        var held = await context.ContactTombstones
            .SingleOrDefaultAsync(t => t.UserId == userId && t.DavName == davName, cancellationToken);

        if (held is null)
        {
            context.ContactTombstones.Add(new ContactTombstone
            {
                UserId = userId, DavName = davName, SyncSequence = sequence, DeletedAt = DateTime.UtcNow
            });
        }
        else
        {
            held.SyncSequence = sequence;
            held.DeletedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task LiftTombstoneAsync(Guid userId, string davName, CancellationToken cancellationToken)
    {
        var held = await context.ContactTombstones
            .SingleOrDefaultAsync(t => t.UserId == userId && t.DavName == davName, cancellationToken);
        if (held is null) return;

        context.ContactTombstones.Remove(held);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ArchiveAsync(ContactRevision revision, CancellationToken cancellationToken)
    {
        // The window guards one shape only: a client looping on a refusal. Two accepted writes that
        // land on the same hash are two facts, and dropping the second would lose an overwrite on
        // the table whose whole job is to lose nothing.
        if (revision.Cause == RevisionCause.Rejected)
        {
            var since = revision.ReplacedAt.AddHours(-24);
            var alreadyKept = await context.ContactRevisions.AnyAsync(
                r => r.UserId == revision.UserId
                     && r.DavName == revision.DavName
                     && r.CardHash == revision.CardHash
                     && r.Cause == revision.Cause
                     && r.ReplacedAt > since,
                cancellationToken);
            if (alreadyKept) return false;
        }

        context.ContactRevisions.Add(revision);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PruneOutcome> PruneAsync(
        DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken cancellationToken)
    {
        var doomed = await context.ContactTombstones
            .Where(t => t.DeletedAt < tombstonesBefore)
            .ToListAsync(cancellationToken);

        // The watermark FIRST, then the removal, and both in one transaction. Written one after the
        // other outside a transaction — the DELETE first, the watermark after — a process killed in
        // between leaves the tombstones gone and pruned_below behind, so stale tokens are ACCEPTED:
        // the response omits the deletion with nothing to signal it, and the client keeps the card
        // for ever. That is the silent failure this column exists to close, reintroduced by the
        // word "then".
        var highest = doomed
            .GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.SyncSequence));

        foreach (var (userId, sequence) in highest)
        {
            var state = await context.ContactSyncStates
                .SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);
            if (state is null) continue;

            // Never downwards. It is also what makes the sweep safe on several instances at once:
            // the write is commutative, and a DELETE that no longer finds its rows removes zero.
            state.PrunedBelow = Math.Max(state.PrunedBelow, sequence);
        }

        context.ContactTombstones.RemoveRange(doomed);

        var staleRevisions = await context.ContactRevisions
            .Where(r => r.ReplacedAt < revisionsBefore)
            .ToListAsync(cancellationToken);
        context.ContactRevisions.RemoveRange(staleRevisions);

        // One SaveChanges, so the watermark and the removal commit together or not at all.
        await context.SaveChangesAsync(cancellationToken);

        return new PruneOutcome(doomed.Count, staleRevisions.Count);
    }
```

**Sur le « une seule transaction ».** `SaveChangesAsync` enveloppe ses commandes dans une
transaction implicite, ce qui suffit ici : le filigrane et les suppressions partent dans le même
appel. L'ordre **à l'intérieur** est le second garde-fou, celui qui survit à ce que la transaction
ne couvre pas — un `P` calculé sur un instantané, une reprise partielle, un élagage futur qui
changerait de borne. Ne pas séparer les deux en deux `SaveChangesAsync`, et écrire pourquoi dans le
code.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactSyncStore`
Expected : les dix-huit cas des deux fichiers PASS.

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

Réverter d'abord `ApiDocumentation.xml`.

- sujet : `feat(carddav): les tombes, les revisions et leur filigrane`
- corps : `L'elagage leve le filigrane AVANT de supprimer : l'ordre inverse accepte les` /
  `jetons perimes et perd la suppression sans rien signaler.`

---

## Paquet 3 — `ContactStore` et ses portes

### Ce que ce paquet change de la forme du store, et pourquoi l'ordre est ce qu'il est

Trois choses, à lire ensemble avant d'ouvrir un fichier.

**1. La transaction explicite est de la mécanique neuve.** Aucun dépôt du projet n'ouvre
aujourd'hui de transaction : ni `ContactStore`, ni `WebmailUserStore` n'appellent
`BeginTransactionAsync`, et `SaveChangesAsync` n'enveloppe qu'un seul appel. Elle s'ouvre par
`Database.BeginTransactionAsync`, **et à travers l'`IExecutionStrategy` du contexte**. Aucune
stratégie de réessai n'est configurée aujourd'hui — `EnableRetryOnFailure` n'apparaît nulle part
dans le dépôt —, donc la traverser est **présentement un geste vide**. Il est fait quand même :
EF refuse une transaction manuelle le jour où la stratégie apparaît, et la contourner au lieu de la
traverser rendrait alors le réessai silencieusement faux. Le motif coûte une ligne et ferme une
régression future ; **une revue qui chercherait la stratégie dans le dépôt ne la trouverait pas et
conclurait autrement** — c'est écrit ici pour cela.

**2. L'ordre de prise de verrou est toujours le même : la ligne d'état d'abord, les fiches
ensuite.** Deux chemins qui verrouillent en ordre inverse s'interbloquent, et les deux existent
déjà : un import de cinq cents fiches et une édition webmail concurrente. Un ordre unique n'est pas
une précaution, c'est la seule raison pour laquelle l'interblocage ne peut pas se produire.
Concrètement : `NextSequenceAsync` s'exécute immédiatement — c'est du SQL, pas du suivi
d'entité — donc l'appeler **avant** tout `SaveChangesAsync` qui écrit des fiches suffit à fixer
l'ordre.

**3. Le rang ne se prend que si la carte a changé, donc la décision se prend avant le verrou.**
`WriteCardAsync` sait déjà répondre « rien n'a changé » : sa garde `SameIgnoringRev` rend
`Result.Success()` sans toucher la carte. Mais elle le sait **après** avoir écrit dans l'entité et
lancé la projection. Ce paquet en extrait la décision, pour que le chemin nominal d'une écriture
qui ne change rien — l'éditeur rouvert et refermé, l'étoile basculée — n'ouvre aucune transaction,
ne prenne aucun verrou et ne réveille aucun client.

**Et `is_favorite` reste invisible du protocole.** Basculer l'étoile ne modifie pas la carte, donc
ne doit réveiller aucun téléphone. `SetFavoriteAsync` et `SetFavoriteManyAsync` ne sont pas touchés
par ce paquet, et un test le dit — c'est le cas piégeux auquel la décision 6 répond en une phrase,
et le seul que personne ne pense à vérifier.

---

### Task 5 : la préparation, la transaction, et les deux portes d'écriture unitaires

**Files :**
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreSyncTests.cs`

**Interfaces :**
- Consomme : `IContactSyncStore.NextSequenceAsync`, `ArchiveAsync`, `LiftTombstoneAsync`
  (tâches 3–4) ; `RevisionCause`.
- Produit, consommé par les tâches 6 et 7 :

```csharp
// ContactStore gagne une dépendance :
internal sealed class ContactStore(PreferencesDbContext context, IContactSyncStore sync) : IContactStore

// et deux membres privés que les tâches 6 et 7 réutilisent tels quels :
private readonly record struct CardBefore(string? VCardRaw, string CardHash, string? Uid, string? DavName);
private async Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken cancellationToken);
internal const int BatchSize = 100;
```

- [ ] **Step 1 : Écrire les tests, rouges**

Créer d'abord les deux fabriques partagées du § « Les fabriques de test que ce plan suppose » —
`Fixtures/ContactStoreTestFactory.cs` et `Fixtures/LoggerAssertions.cs` — puis
`snoopy.microservice.Tests/Repositories/ContactStoreSyncTests.cs`. Les tests portent sur un
`ContactStore` réel avec un `IContactSyncStore` **doublé** (Moq) : ce qui est vérifiable et ce qui
compte est que chaque porte demande son rang, dans le bon ordre, et archive avant d'écraser.

Les extraits ci-dessous déclarent leurs propres `NewContext` / `NewSync` / `Write` pour rester
lisibles ; **les remplacer par des appels à `ContactStoreTestFactory`** en les écrivant, les tâches
6 à 8 s'appuyant dessus.

```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;

namespace snoopy.microservice.Tests.Repositories;

public sealed class ContactStoreSyncTests
{
    private static PreferencesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PreferencesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Mock<IContactSyncStore> NewSync(ulong rank = 1)
    {
        var sync = new Mock<IContactSyncStore>();
        sync.Setup(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rank);
        sync.Setup(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return sync;
    }

    private static ContactWrite Write(string first, string last) =>
        new(first, last, null, null, null, null, null, null, null, null, null, null,
            false, null, [], [], []);

    [Fact]
    public async Task Creating_TakesARankAndNamesTheResource()
    {
        using var context = NewContext();
        var sync = NewSync(rank: 4);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();

        var created = await store.CreateAsync(userId, Write("Ada", "Lovelace"), CancellationToken.None);

        var row = await context.Contacts.SingleAsync(CancellationToken.None);
        Assert.Equal(4ul, row.SyncSequence);
        // {id}.vcf is what clients show in their logs; there is no reason to puzzle them.
        Assert.Equal($"{created.Value}.vcf", row.DavName);
    }

    [Fact]
    public async Task Creating_LiftsATombstoneOfTheSameName()
    {
        using var context = NewContext();
        var sync = NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();

        var created = await store.CreateAsync(userId, Write("Ada", "Lovelace"), CancellationToken.None);

        // A name that comes back must stop being reported as deleted, or a client that syncs after
        // both events sees a creation and a burial at the same rank and picks whichever it likes.
        sync.Verify(s => s.LiftTombstoneAsync(userId, $"{created.Value}.vcf", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Creating_ArchivesNothing()
    {
        using var context = NewContext();
        var sync = NewSync();
        var store = new ContactStore(context, sync.Object);

        await store.CreateAsync(Guid.NewGuid(), Write("Ada", "Lovelace"), CancellationToken.None);

        // There is nothing to archive: no card was replaced.
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Updating_ArchivesTheBytesItReplaces_BeforeTakingItsRank()
    {
        using var context = NewContext();
        var sync = NewSync(rank: 9);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(userId, Write("Ada", "Lovelace"), CancellationToken.None);
        var before = (await context.Contacts.SingleAsync(CancellationToken.None)).VCardRaw;
        sync.Invocations.Clear();

        await store.UpdateAsync(userId, created.Value, Write("Ada", "Byron"), CancellationToken.None);

        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r =>
                r.Cause == RevisionCause.Webmail
                && r.VCardRaw == before
                && r.ContactId == created.Value),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Updating_TakesTheNewRank()
    {
        using var context = NewContext();
        var sync = NewSync(rank: 9);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(userId, Write("Ada", "Lovelace"), CancellationToken.None);

        await store.UpdateAsync(userId, created.Value, Write("Ada", "Byron"), CancellationToken.None);

        var row = await context.Contacts.SingleAsync(CancellationToken.None);
        Assert.Equal(9ul, row.SyncSequence);
    }

    [Fact]
    public async Task AWriteThatChangesNothing_TakesNoRankAndArchivesNothing()
    {
        using var context = NewContext();
        var sync = NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var write = Write("Ada", "Lovelace");
        var created = await store.CreateAsync(userId, write, CancellationToken.None);
        var rankAfterCreate = (await context.Contacts.SingleAsync(CancellationToken.None)).SyncSequence;
        sync.Invocations.Clear();

        await store.UpdateAsync(userId, created.Value, write, CancellationToken.None);

        // The editor reopened and closed again. The sequence advances exactly when card_hash
        // changes: waking every phone for a write that changed nothing is the failure this guards.
        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(rankAfterCreate, (await context.Contacts.SingleAsync(CancellationToken.None)).SyncSequence);
    }

    [Fact]
    public async Task TogglingTheStar_IsInvisibleToTheProtocol()
    {
        using var context = NewContext();
        var sync = NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(userId, Write("Ada", "Lovelace"), CancellationToken.None);
        sync.Invocations.Clear();

        await store.SetFavoriteAsync(userId, created.Value, true, CancellationToken.None);

        // is_favorite is projected from nothing and must not be visible to the protocol either.
        // This is the trap decision 6 answers in one sentence, and the one nobody thinks to check.
        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TogglingTheStarOverABatch_IsInvisibleToo()
    {
        using var context = NewContext();
        var sync = NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(userId, Write("Ada", "Lovelace"), CancellationToken.None);
        sync.Invocations.Clear();

        await store.SetFavoriteManyAsync(userId, [created.Value], true, CancellationToken.None);

        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AContactWithNoCard_IsUpdatedWithoutArchivingAnything()
    {
        using var context = NewContext();
        var sync = NewSync();
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        // The shape the 4a backfill has not reached: no card, no hash, no name.
        context.Contacts.Add(new Contact { Id = id, UserId = userId, Uid = id.ToString() });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactStore(context, sync.Object);

        await store.UpdateAsync(userId, id, Write("Ada", "Lovelace"), CancellationToken.None);

        // No card, no revision. The write path tolerates it rather than breaking on it — and it
        // gives the row the name it lacked, in the same transaction, so a webmail edit during the
        // backfill window cannot create a row with a rank above zero and no name.
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()), Times.Never);
        var row = await context.Contacts.SingleAsync(CancellationToken.None);
        Assert.Equal($"{id}.vcf", row.DavName);
        Assert.NotEqual(0ul, row.SyncSequence);
    }
}
```

**Note pour l'implémenteur sur `ContactWrite`** : la signature positionnelle ci-dessus est
indicative. Lire `Models/Contacts/ContactWrite.cs` et composer l'objet avec les vrais noms de
paramètres — un `ContactWrite` mal construit ferait échouer les neuf tests pour une raison sans
rapport avec ce qu'ils vérifient.

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactStoreSyncTests`
Expected : ne compile pas — `ContactStore` n'a qu'un paramètre de constructeur.

- [ ] **Step 3 : Injecter le dépôt de synchronisation et poser la couture**

Changer la déclaration :

```csharp
internal sealed class ContactStore(PreferencesDbContext context, IContactSyncStore sync) : IContactStore
```

Ajouter, à côté des constantes existantes :

```csharp
    /// <summary>
    /// One transaction, one rank — but not one import, one rank. Every write archives what it
    /// replaces since decision 17, so a whole-book deletion in a single transaction would write up
    /// to five gigabytes of MEDIUMTEXT: a redo log that overflows, and the state row's lock held
    /// long enough for every phone to come back in 503.
    /// </summary>
    internal const int BatchSize = 100;
```

et les deux membres privés :

```csharp
    /// <summary>The card as it stood, snapshotted before anything is written over it.</summary>
    private readonly record struct CardBefore(string? VCardRaw, string CardHash, string? Uid, string? DavName);

    /// <summary>
    /// One transaction, opened THROUGH the context's execution strategy. No retry strategy is
    /// configured today, so going through it is presently a no-op — it is done anyway because EF
    /// refuses a manual transaction the day one appears, and bypassing it instead of traversing it
    /// would then make the retry silently wrong.
    /// </summary>
    private Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken cancellationToken) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var outcome = await body();
            await transaction.CommitAsync(cancellationToken);
            return outcome;
        });
```

**Le fournisseur InMemory n'a pas de transactions**, et `BeginTransactionAsync` y lève par défaut.
Les tests de cette tâche passent donc par un contexte InMemory configuré avec
`.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` — l'ajouter au
`NewContext()` des tests, avec un commentaire disant que la transaction est réelle en production et
ignorée ici, et que **c'est une autre raison pour laquelle l'atomicité de la tâche 3 se vérifie à la
main**.

- [ ] **Step 4 : Extraire la décision « la carte a-t-elle changé »**

`WriteCardAsync` décide déjà, mais après avoir écrit. Ajouter au-dessus d'elle :

```csharp
    /// <summary>
    /// The card as it will be stored, and whether storing it would change anything — decided
    /// before any lock is taken, so a write that changes nothing opens no transaction, takes no
    /// rank and wakes no client. The composer refreshes REV on every output, so a card that
    /// changed nothing is never byte-equal to the stored one; compared without that line it is.
    /// </summary>
    private static Result<(string Card, bool Changed)> PrepareCard(Contact row, string card)
    {
        card = WithUid(card, row.Uid);
        if (Encoding.UTF8.GetByteCount(card) > MaxCardBytes)
            return Result.Failure<(string, bool)>(CardTooLarge);

        var unchanged = row.CardHash.Length > 0 && row.VCardRaw != null
            && SameIgnoringRev(row.VCardRaw, card);
        return Result.Success((card, !unchanged));
    }
```

et réécrire `WriteCardAsync` pour l'appeler plutôt que de refaire le travail — la garde de taille et
la comparaison ne doivent exister qu'à un seul endroit.

- [ ] **Step 5 : Réécrire `CreateAsync`**

Le corps existant reste ; il s'enveloppe. Après la composition et avant le `SaveChangesAsync`,
l'ordre à respecter est : **rang d'abord, fiches ensuite**.

```csharp
    public async Task<Result<Guid>> CreateAsync(
        Guid userId, ContactWrite contact, CancellationToken cancellationToken)
    {
        var stored = await context.Contacts.CountAsync(c => c.UserId == userId, cancellationToken);
        if (stored >= MaxPerUser) return Result.Failure<Guid>(CapReached);

        var id = Guid.NewGuid();
        var davName = $"{id}.vcf";

        return await InTransactionAsync(async () =>
        {
            // The state row's lock FIRST, always, and before any contact row is touched. Two paths
            // locking in the opposite order deadlock, and both already exist: an import of five
            // hundred and a concurrent webmail edit.
            var rank = await sync.NextSequenceAsync(userId, cancellationToken);

            var row = new Contact
            {
                Id = id,
                UserId = userId,
                Uid = id.ToString(),
                IsFavorite = contact.IsFavorite,
                Source = contact.Source,
                UpdatedAt = DateTime.UtcNow,
                DavName = davName,
                SyncSequence = rank
            };
            context.Contacts.Add(row);

            var written = await WriteCardAsync(row, VCardComposer.ComposeNew(row.Uid, contact), cancellationToken);
            if (written.IsFailure)
            {
                context.Entry(row).State = EntityState.Detached;
                return Result.Failure<Guid>(written.Error);
            }

            await context.SaveChangesAsync(cancellationToken);

            // A name that comes back must stop being reported as deleted: a client that syncs after
            // both events would otherwise see a creation and a burial at the same rank.
            await sync.LiftTombstoneAsync(userId, davName, cancellationToken);
            return Result.Success(id);
        }, cancellationToken);
    }
```

- [ ] **Step 6 : Réécrire `UpdateAsync`**

```csharp
    public async Task<Result> UpdateAsync(
        Guid userId, Guid contactId, ContactWrite contact, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        var card = row.VCardRaw == null
            ? VCardComposer.ComposeNew(row.Uid, contact)
            : VCardComposer.Compose(row.VCardRaw, row.Uid, contact);

        var prepared = PrepareCard(row, card);
        if (prepared.IsFailure) return Result.Failure(prepared.Error);

        // The star and the timestamp are not the card, so they never justify a rank.
        if (!prepared.Value.Changed)
        {
            row.IsFavorite = contact.IsFavorite;
            row.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var before = new CardBefore(row.VCardRaw, row.CardHash, row.Uid, row.DavName);

        return await InTransactionAsync(async () =>
        {
            var rank = await sync.NextSequenceAsync(userId, cancellationToken);

            // Archive before overwriting, in the same transaction as the write — so under the same
            // rank, and so never without it. A card whose vcard_raw is NULL — the 4a backfill never
            // reached it — is replaced without a revision: no card, no revision.
            if (before.VCardRaw is not null)
            {
                await sync.ArchiveAsync(new ContactRevision
                {
                    UserId = userId,
                    ContactId = contactId,
                    Uid = before.Uid,
                    DavName = before.DavName,
                    CardHash = before.CardHash,
                    VCardRaw = before.VCardRaw,
                    Cause = RevisionCause.Webmail,
                    ReplacedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            var written = await WriteCardAsync(row, card, cancellationToken);
            if (written.IsFailure) return written;

            row.IsFavorite = contact.IsFavorite;
            row.UpdatedAt = DateTime.UtcNow;
            row.SyncSequence = rank;
            // A write that advances the rank of a nameless row gives it its name in the same
            // transaction: without this, a webmail edit during the backfill window would create a
            // row with a rank above zero and no name, which no report knows how to serve.
            row.DavName ??= $"{contactId}.vcf";

            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }
```

- [ ] **Step 7 : Enregistrer et lancer**

`ApplicationServicesConfiguration` : rien à changer si `IContactSyncStore` a été enregistré à la
tâche 3 ; vérifier que `ContactStore` se résout toujours.

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactStoreSyncTests`
Expected : les neuf cas PASS.

Run : `cd src && dotnet test`
Expected : **toute la suite au vert.** `ContactStore` a beaucoup de tests existants ; s'ils
rougissent, c'est le constructeur qui a changé — les mettre à jour, sans changer ce qu'ils
vérifient.

Run : `cd src && dotnet build`
Expected : zéro avertissement.

- [ ] **Step 8 : Commit**

Réverter d'abord `ApiDocumentation.xml`.

- sujet : `feat(carddav): les ecritures unitaires prennent un rang et archivent`
- corps : `Verrou d'etat d'abord, fiches ensuite ; une ecriture qui ne change rien` /
  `n'ouvre aucune transaction et ne reveille aucun client.`

---

### Task 6 : les suppressions posent une tombe et archivent la carte

**C'est la tâche qui ferme le mode de défaillance silencieux de la tranche**, et elle le ferme sur
la porte la plus fréquentée : les suppressions viennent d'abord de la fiche et de la barre
d'actions groupées du webmail, pas de DAV. Une suppression qui ne pose pas de tombe est invisible
du protocole — le client ne voit ni modification ni disparition, **garde la fiche pour toujours, et
la restitue à l'utilisateur qui vient de l'effacer**.

Deux tolérances à ne pas confondre avec des cas d'erreur :

- Une fiche sans `dav_name` — le rattrapage ne l'a pas atteinte — se supprime **sans tombe** : il
  n'y a pas de nom à enterrer, et la fiche n'a jamais été visible du protocole. La clé de
  `contact_tombstones` refuse le `NULL` ; le chemin doit le tolérer, pas s'y casser.
- Une fiche dont `vcard_raw` est `NULL` se supprime **sans révision**. Pas de carte, pas de
  révision.

**Files :**
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreDeletionTests.cs`

**Interfaces :**
- Consomme : `InTransactionAsync`, `BatchSize`, `CardBefore` (tâche 5) ;
  `IContactSyncStore.NextSequenceAsync`, `PlaceTombstoneAsync`, `ArchiveAsync`.
- Produit : rien de neuf ; `DeleteAsync` et `DeleteManyAsync` gardent leur signature.

- [ ] **Step 1 : Écrire les tests, rouges**

Créer `snoopy.microservice.Tests/Repositories/ContactStoreDeletionTests.cs`, en appelant le
`ContactStoreTestFactory` que la tâche 5 a créé. Ne pas redéclarer les fabriques : la règle du
projet interdit le doublon, et trois copies divergent au premier changement de signature.

```csharp
    [Fact]
    public async Task Deleting_PlacesATombstoneAtTheNewRank()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 12);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        sync.Invocations.Clear();

        await store.DeleteAsync(userId, created.Value, CancellationToken.None);

        // The silent failure this closes: without a tombstone the client sees neither a change nor
        // a disappearance, keeps the card for ever, and hands it back to the user who just erased it.
        sync.Verify(s => s.PlaceTombstoneAsync(userId, $"{created.Value}.vcf", 12,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deleting_ArchivesTheCardUnderTheDeleteCause()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var card = (await context.Contacts.SingleAsync(CancellationToken.None)).VCardRaw;
        sync.Invocations.Clear();

        await store.DeleteAsync(userId, created.Value, CancellationToken.None);

        // The tombstone does NOT carry the card, deliberately: writing the bytes in two places by
        // door would give two pruning paths, two lifetimes and two chances to repair only one.
        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Delete && r.VCardRaw == card),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletingANamelessContact_BuriesNothingAndBreaksNothing()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        context.Contacts.Add(new Contact { Id = id, UserId = userId, Uid = id.ToString() });
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new ContactStore(context, sync.Object);

        var outcome = await store.DeleteAsync(userId, id, CancellationToken.None);

        // No name to bury, and the row was never visible to the protocol (rank 0). The tombstone
        // key refuses NULL: this path must tolerate it, not break on it.
        Assert.True(outcome.IsSuccess);
        sync.Verify(s => s.PlaceTombstoneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ulong>(),
            It.IsAny<CancellationToken>()), Times.Never);
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Empty(context.Contacts);
    }

    [Fact]
    public async Task DeletingABatch_BuriesEveryRowItActuallyRemoved()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var first = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var second = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Grace", "Hopper"), CancellationToken.None);
        sync.Invocations.Clear();

        var removed = await store.DeleteManyAsync(
            userId, [first.Value, second.Value, Guid.NewGuid()], CancellationToken.None);

        // One tombstone PER card actually removed. The bulk action bar is the busiest deletion door
        // in the product, and it is the one a per-row loop is most tempting to skip.
        Assert.Equal(2, removed);
        sync.Verify(s => s.PlaceTombstoneAsync(userId, It.IsAny<string>(), It.IsAny<ulong>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeletingABatch_ArchivesEveryCardItRemoved()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var first = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var second = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Grace", "Hopper"), CancellationToken.None);
        sync.Invocations.Clear();

        await store.DeleteManyAsync(userId, [first.Value, second.Value], CancellationToken.None);

        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Delete), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task DeletingABatch_TakesOneRankPerTransactionAndNotOnePerCard()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var created = await store.CreateAsync(
                userId, ContactStoreTestFactory.Write($"First{i}", "Last"), CancellationToken.None);
            ids.Add(created.Value);
        }
        sync.Invocations.Clear();

        await store.DeleteManyAsync(userId, ids, CancellationToken.None);

        // One transaction, one rank: the state row is locked once, and incrementing it further
        // would distinguish nothing since everything becomes visible at the same COMMIT. Three
        // cards well under the batch size means exactly one rank.
        sync.Verify(s => s.NextSequenceAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletingMoreThanABatch_TakesOneRankPerBatch()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < ContactStore.BatchSize + 5; i++)
        {
            var created = await store.CreateAsync(
                userId, ContactStoreTestFactory.Write($"First{i}", "Last"), CancellationToken.None);
            ids.Add(created.Value);
        }
        sync.Invocations.Clear();

        await store.DeleteManyAsync(userId, ids, CancellationToken.None);

        // "One transaction, one rank" holds; "one bulk action, one rank" does not, and nothing
        // asked for it. Several ranks for one bulk deletion are exactly what a client syncing
        // during it can serve, rank by rank, rather than waiting for the end.
        sync.Verify(s => s.NextSequenceAsync(userId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactStoreDeletionTests`
Expected : les sept cas FAIL — aucune tombe posée, aucune révision archivée.

- [ ] **Step 3 : Réécrire `DeleteAsync`**

```csharp
    public async Task<Result> DeleteAsync(Guid userId, Guid contactId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        var before = new CardBefore(row.VCardRaw, row.CardHash, row.Uid, row.DavName);

        return await InTransactionAsync<Result>(async () =>
        {
            var rank = await sync.NextSequenceAsync(userId, cancellationToken);

            if (before.VCardRaw is not null)
            {
                // ContactId is left NULL: a delete revision outlives the row it describes, and the
                // FK would refuse a value pointing at a contact that is about to disappear.
                await sync.ArchiveAsync(new ContactRevision
                {
                    UserId = userId,
                    ContactId = null,
                    Uid = before.Uid,
                    DavName = before.DavName,
                    CardHash = before.CardHash,
                    VCardRaw = before.VCardRaw,
                    Cause = RevisionCause.Delete,
                    ReplacedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            await ClearProjectionAsync([contactId], cancellationToken);
            context.Contacts.Remove(row);
            await context.SaveChangesAsync(cancellationToken);

            // No name, no tombstone: the row was never visible to the protocol, and the tombstone
            // key refuses NULL.
            if (before.DavName is not null)
                await sync.PlaceTombstoneAsync(userId, before.DavName, rank, cancellationToken);

            return Result.Success();
        }, cancellationToken);
    }
```

- [ ] **Step 4 : Réécrire `DeleteManyAsync` par lots**

```csharp
    public async Task<int> DeleteManyAsync(
        Guid userId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var removed = 0;

        // Batched at a hundred, and it is not an optimisation: since decision 17 each of these
        // deletions ARCHIVES what it erases, so a whole-book deletion in one transaction would
        // write up to five gigabytes of MEDIUMTEXT — a redo log that overflows, and the state row's
        // lock held long enough for every phone to come back in 503.
        foreach (var batch in ids.Chunk(BatchSize))
        {
            var rows = await context.Contacts
                .Where(c => c.UserId == userId && batch.Contains(c.Id))
                .ToListAsync(cancellationToken);
            if (rows.Count == 0) continue;

            var snapshots = rows
                .Select(r => (r.Id, Before: new CardBefore(r.VCardRaw, r.CardHash, r.Uid, r.DavName)))
                .ToList();

            removed += await InTransactionAsync(async () =>
            {
                var rank = await sync.NextSequenceAsync(userId, cancellationToken);

                foreach (var (id, before) in snapshots)
                {
                    if (before.VCardRaw is null) continue;
                    await sync.ArchiveAsync(new ContactRevision
                    {
                        UserId = userId,
                        ContactId = null,
                        Uid = before.Uid,
                        DavName = before.DavName,
                        CardHash = before.CardHash,
                        VCardRaw = before.VCardRaw,
                        Cause = RevisionCause.Delete,
                        ReplacedAt = DateTime.UtcNow
                    }, cancellationToken);
                }

                await ClearProjectionAsync([.. rows.Select(r => r.Id)], cancellationToken);
                context.Contacts.RemoveRange(rows);
                await context.SaveChangesAsync(cancellationToken);

                // One tombstone PER card actually removed.
                foreach (var (_, before) in snapshots)
                {
                    if (before.DavName is null) continue;
                    await sync.PlaceTombstoneAsync(userId, before.DavName, rank, cancellationToken);
                }

                return rows.Count;
            }, cancellationToken);
        }

        return removed;
    }
```

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactStore`
Expected : tous les cas des trois fichiers PASS.

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

Réverter d'abord `ApiDocumentation.xml`.

- sujet : `feat(carddav): toute suppression pose une tombe et archive la carte`
- corps : `La barre d'actions groupees est la porte la plus frequentee : une suppression` /
  `sans tombe laisse la fiche sur le telephone, indefiniment et sans erreur.`

---

### Task 7 : l'import prend ses rangs par lots et archive ce qu'il fusionne

`ImportAsync` est la porte qui écrit le plus de cartes d'un coup, et la seule qui **remplace** des
fiches existantes sans que l'utilisateur les ait ouvertes une à une. C'est donc celle où un
archivage manquant se remarque le plus tard.

**Files :**
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreImportSyncTests.cs`

**Interfaces :**
- Consomme : `InTransactionAsync`, `BatchSize`, `CardBefore`, `PrepareCard` (tâche 5).
- Produit : rien de neuf ; `ImportAsync` garde sa signature et son `ContactImportOutcome`.

- [ ] **Step 1 : Écrire les tests, rouges**

Créer `snoopy.microservice.Tests/Repositories/ContactStoreImportSyncTests.cs` :

```csharp
    [Fact]
    public async Task Importing_GivesEveryNewCardANameAndARank()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync(rank: 3);
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();

        await store.ImportAsync(userId, ContactStoreTestFactory.ImportRows(2), CancellationToken.None);

        var rows = await context.Contacts.ToListAsync(CancellationToken.None);
        Assert.All(rows, r => Assert.Equal(3ul, r.SyncSequence));
        Assert.All(rows, r => Assert.Equal($"{r.Id}.vcf", r.DavName));
    }

    [Fact]
    public async Task Importing_ArchivesTheCardsItMergesOver()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var before = (await context.Contacts.SingleAsync(CancellationToken.None)).VCardRaw;
        sync.Invocations.Clear();

        // A row that merges onto the existing contact — same identity, one field more.
        await store.ImportAsync(
            userId, ContactStoreTestFactory.MergeRowFor("Ada", "Lovelace"), CancellationToken.None);

        sync.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r =>
                r.Cause == RevisionCause.Import && r.VCardRaw == before && r.ContactId == created.Value),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Importing_TakesOneRankPerBatchAndNotOnePerRow()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);

        await store.ImportAsync(
            Guid.NewGuid(), ContactStoreTestFactory.ImportRows(ContactStore.BatchSize + 5),
            CancellationToken.None);

        // Two batches, two ranks. A client syncing during the import gets the beginning rather than
        // waiting for the end — which is exactly what decision 7's rank-boundary cut can serve.
        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task AnImportRowThatChangesNothing_TakesNoRankOfItsOwn()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        await store.CreateAsync(userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var rankBefore = (await context.Contacts.SingleAsync(CancellationToken.None)).SyncSequence;
        sync.Invocations.Clear();

        // The same file imported twice: the second pass fills nothing in.
        await store.ImportAsync(
            userId, ContactStoreTestFactory.MergeRowFor("Ada", "Lovelace"), CancellationToken.None);

        // Re-importing the same file must not wake every phone for a book that did not change.
        var row = await context.Contacts.SingleAsync(CancellationToken.None);
        Assert.Equal(rankBefore, row.SyncSequence);
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

Ajouter à `ContactStoreTestFactory` les deux fabriques `ImportRows(int)` et
`MergeRowFor(string, string)`, construites sur le vrai `ContactImportRow` — lire
`Models/Contacts/ContactImportRow.cs` pour ses champs.

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactStoreImportSyncTests`
Expected : les quatre cas FAIL.

- [ ] **Step 3 : Découper `ImportAsync` en lots et lui donner ses rangs**

`ImportAsync` fait aujourd'hui une passe unique terminée par un `SaveChangesAsync` (ligne ~436). La
réécriture garde **toute** sa logique de fusion — `ApplyMergesAsync`, `Filled`, `Adopt`, les
`ContactImportError` — et n'en change que l'enveloppe :

1. les lignes entrantes sont découpées par `Chunk(BatchSize)` ;
2. chaque lot s'exécute dans son `InTransactionAsync` ;
3. **dans chaque lot, `NextSequenceAsync` d'abord**, avant toute écriture de fiche ;
4. avant chaque fusion sur une fiche existante, un `CardBefore` est pris et archivé sous la cause
   `Import` — sauf si `VCardRaw` est `null` ;
5. une fiche neuve reçoit `DavName = $"{id}.vcf"` et `SyncSequence = rank` ;
6. une fiche fusionnée ne reçoit le rang **que si `PrepareCard` répond `Changed`** ;
7. les `ContactImportError` de tous les lots sont concaténés dans un seul `ContactImportOutcome`,
   comme aujourd'hui.

Le point 6 est celui qu'un implémenteur pressé sautera : réimporter le même fichier ne doit
réveiller aucun téléphone, et c'est le geste le plus courant d'un utilisateur qui doute que son
import ait marché.

**Le plafond par utilisateur se recompte à chaque lot**, et non une fois au début : cinq cents
lignes importées dans un carnet qui en contient déjà 4 800 doivent s'arrêter à 5 000, pas
s'autoriser le total qu'un comptage initial avait cru libre.

- [ ] **Step 4 : Lancer les tests**

Run : `cd src && dotnet test --filter FullyQualifiedName~ContactStore`
Expected : tous les cas PASS, y compris les tests d'import existants — la logique de fusion n'a pas
bougé.

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 5 : Commit**

Réverter d'abord `ApiDocumentation.xml`.

- sujet : `feat(carddav): l'import prend ses rangs par lots et archive ses fusions`
- corps : `Cent fiches par transaction : la porte qui ecrit le plus est celle ou un` /
  `archivage manquant se remarque le plus tard.`

---

## Paquet 4 — le `409` de bout en bout et les balayeurs

### Task 8 : `UpdateAsync` refuse sur un `card_hash` périmé, et l'API répond `409`

**Le webmail cesse d'écrire sans regarder.** `PUT /api/contacts/{id}` écrase aujourd'hui sans
aucune version : le côté DAV gagnera des ETags aux plans b et c, le côté webmail garderait le
dernier-arrivé-gagne, et **un onglet ouvert depuis dix minutes réécrirait en silence la fiche que
le téléphone vient de modifier**. Ce serait le trou de la décision 6 — une porte qui ne respecte
pas l'invariant des autres — sur la porte la plus fréquentée. C'est le même contrôle qu'`If-Match`,
exprimé dans la langue de l'API, et il est dans cette tranche parce que c'est elle qui crée le
second écrivain.

**Le contrôle est facultatif côté client, et c'est délibéré.** Un `ContactRequest` sans
`cardHash` s'écrit comme aujourd'hui : l'import, les scripts et tout appelant qui n'a pas lu la
fiche d'abord ne sont pas cassés par cette tâche. Ce que la tâche garantit, c'est qu'un client qui
**dit** ce qu'il a lu est refusé quand ce n'est plus vrai.

**Files :**
- Modify : `src/snoopy.microservice/Repositories/IContactStore.cs`
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs`
- Modify : `src/snoopy.microservice/Models/Contacts/ContactDetail.cs`
- Modify : `src/snoopy.microservice/Models/Contacts/ContactRequest.cs`
- Modify : `src/snoopy.microservice/Models/Contacts/ContactWrite.cs`
- Modify : `src/snoopy.microservice/Services/Contacts/ContactValidator.cs`
- Modify : `src/snoopy.microservice/Controllers/ContactsController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ContactsControllerConflictTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreConflictTests.cs`

**Interfaces :**
- Produit, consommé par la tâche 9 :

```csharp
// ContactStore
internal static readonly string CardMoved =
    "The contact changed since it was read. Reload it and try again.";

// ContactDetail gagne un champ positionnel final :
string CardHash

// ContactRequest et ContactWrite gagnent un champ optionnel :
string? CardHash

// GET /api/Contacts/{id} rend donc `cardHash` ; PUT l'accepte et répond 409 s'il a bougé.
```

- [ ] **Step 1 : Écrire les tests du store, rouges**

Créer `snoopy.microservice.Tests/Repositories/ContactStoreConflictTests.cs` :

```csharp
    [Fact]
    public async Task Updating_WithTheHashItRead_Succeeds()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var store = new ContactStore(context, ContactStoreTestFactory.NewSync().Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        var held = (await context.Contacts.SingleAsync(CancellationToken.None)).CardHash;

        var saved = await store.UpdateAsync(
            userId, created.Value,
            ContactStoreTestFactory.Write("Ada", "Byron") with { CardHash = held },
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
    }

    [Fact]
    public async Task Updating_WithAStaleHash_IsRefused()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var store = new ContactStore(context, ContactStoreTestFactory.NewSync().Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);

        var saved = await store.UpdateAsync(
            userId, created.Value,
            ContactStoreTestFactory.Write("Ada", "Byron") with { CardHash = "not-the-one-it-read" },
            CancellationToken.None);

        // A tab open for ten minutes must not silently rewrite the card the phone just changed.
        Assert.True(saved.IsFailure);
        Assert.Equal(ContactStore.CardMoved, saved.Error);
    }

    [Fact]
    public async Task Updating_WithoutAHash_StillWrites()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var store = new ContactStore(context, ContactStoreTestFactory.NewSync().Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);

        var saved = await store.UpdateAsync(
            userId, created.Value, ContactStoreTestFactory.Write("Ada", "Byron"), CancellationToken.None);

        // The check is opt-in: a caller that did not read the card first is not broken by it.
        Assert.True(saved.IsSuccess);
    }

    [Fact]
    public async Task AStaleHash_IsRefusedBeforeAnyRankIsTaken()
    {
        using var context = ContactStoreTestFactory.NewContext();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(context, sync.Object);
        var userId = Guid.NewGuid();
        var created = await store.CreateAsync(
            userId, ContactStoreTestFactory.Write("Ada", "Lovelace"), CancellationToken.None);
        sync.Invocations.Clear();

        await store.UpdateAsync(
            userId, created.Value,
            ContactStoreTestFactory.Write("Ada", "Byron") with { CardHash = "stale" },
            CancellationToken.None);

        // A refusal must open no transaction, take no lock and wake no client: the refused path is
        // the one a conflicted tab retries on every save.
        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        sync.Verify(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2 : Écrire les tests du contrôleur, rouges**

Créer `snoopy.microservice.Tests/Controllers/ContactsControllerConflictTests.cs`, sur le modèle des
tests de contrôleur existants (doubler `IContactStore` avec Moq) :

```csharp
    [Fact]
    public async Task AStaleHash_Answers409AndNot404()
    {
        var store = new Mock<IContactStore>();
        store.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ContactStore.CardMoved));
        var controller = NewController(store.Object);

        var answer = await controller.Update(Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        // Exact type: Conflict(body) is a ConflictObjectResult, never a bare ObjectResult. And 409
        // rather than 404 because the contact is very much there — it simply moved.
        Assert.IsType<ConflictObjectResult>(answer);
    }

    [Fact]
    public async Task AMissingContact_StillAnswers404()
    {
        var store = new Mock<IContactStore>();
        store.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ContactStore.NotFound));
        var controller = NewController(store.Object);

        var answer = await controller.Update(Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(answer);
    }

    [Fact]
    public async Task TheDetail_CarriesTheHashTheEditorMustSendBack()
    {
        var store = new Mock<IContactStore>();
        store.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetailWithHash("abc123"));
        var controller = NewController(store.Object);

        var answer = await controller.Get(Guid.NewGuid(), CancellationToken.None);

        // Without it on the way out, the editor has nothing to send back and the whole check is
        // unreachable from the only screen that needs it.
        var ok = Assert.IsType<OkObjectResult>(answer.Result);
        Assert.Equal("abc123", Assert.IsType<ContactDetail>(ok.Value).CardHash);
    }
```

- [ ] **Step 3 : Lancer les deux fichiers pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~Conflict`
Expected : ne compile pas — `CardMoved` et `CardHash` n'existent pas.

- [ ] **Step 4 : Porter le `card_hash` sur les trois modèles**

- `ContactDetail` : ajouter `string CardHash` **en dernier paramètre positionnel**, et le remplir
  dans `ContactStore.Detail(…)` depuis `row.CardHash`.
- `ContactRequest` : ajouter `string? CardHash { get; init; }`, optionnel.
- `ContactWrite` : ajouter `string? CardHash` en dernier paramètre, avec `= null` par défaut, pour
  que tous les sites de construction existants continuent de compiler.
- `ContactValidator.Validate` : reporter `request.CardHash` tel quel dans le `ContactWrite`. **Ne
  pas le valider** — un condensat qui ne ressemble à rien n'est pas une requête malformée, c'est
  une requête périmée, et elle a déjà sa réponse.

- [ ] **Step 5 : Écrire la garde dans `UpdateAsync`**

Dans `ContactStore`, à côté de `NotFound` :

```csharp
    /// <summary>
    /// The editor sent back a hash that is no longer the card's. Its own message because 409 and
    /// 404 are two different stories for the screen: one reloads, the other closes.
    /// </summary>
    internal static readonly string CardMoved =
        "The contact changed since it was read. Reload it and try again.";
```

et, dans `UpdateAsync`, **immédiatement après le `FindAsync` et avant toute composition** :

```csharp
        // Opt-in, and refused before anything else: a client that says what it read is refused when
        // that is no longer true, and the refusal opens no transaction, takes no rank and wakes no
        // client. A caller that says nothing writes as before.
        if (contact.CardHash is not null && contact.CardHash != row.CardHash)
            return Result.Failure(CardMoved);
```

- [ ] **Step 6 : Écrire le `409` dans le contrôleur**

```csharp
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Update(
        Guid id, ContactRequest request, CancellationToken cancellationToken)
    {
        var validated = ContactValidator.Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var saved = await store.UpdateAsync(
            AuthenticatedUser.WebmailUid, id, validated.Value, cancellationToken);
        if (saved.IsSuccess) return NoContent();

        // 409 and not 404: the contact is very much there, it simply moved under the editor.
        return saved.Error == ContactStore.CardMoved
            ? ConflictEnveloppe(saved.Error)
            : NotFoundEnveloppe(saved.Error);
    }
```

Ajouter `ConflictEnveloppe` à la classe de base des contrôleurs, à côté de `NotFoundEnveloppe` et
`BadRequestEnveloppe`, avec la même forme d'enveloppe — **ne pas rendre `Conflict(saved.Error)`
nu** : le frontend lit `message` sur toutes les autres erreurs, et une forme différente sur ce
code-là serait la seule à ne pas s'afficher. Mettre à jour la documentation XML de la méthode avec
une balise `<response code="409">`.

- [ ] **Step 7 : Lancer les tests**

Run : `cd src && dotnet test --filter FullyQualifiedName~Conflict`
Expected : les sept cas PASS.

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement. **Les tests existants de `ContactDetail` vont
rougir** — le record a un paramètre de plus. Les corriger en passant le condensat, sans changer ce
qu'ils vérifient.

- [ ] **Step 8 : Commit**

Réverter d'abord `ApiDocumentation.xml`.

- sujet : `feat(contacts): l'ecriture webmail refuse un card_hash perime`
- corps : `409 et non 404 : la fiche est bien la, elle a bouge. Le controle est` /
  `facultatif, donc aucun appelant qui n'a pas lu la fiche n'est casse.`

---

### Task 9 : l'éditeur renvoie le condensat qu'il a lu et propose de recharger

Sans cet écran, la garde de la tâche 8 est inatteignable depuis la seule porte qui en a besoin.
Et le `409` doit dire quoi faire : un message d'erreur qui annonce un conflit sans offrir de
recharger laisse l'utilisateur avec un formulaire qu'il ne peut ni enregistrer ni abandonner sans
perdre sa saisie.

**Files :**
- Modify : `src/frontend/src/types/contacts.ts`
- Modify : `src/frontend/src/modules/contacts/ContactEditView.tsx`
- Modify : `src/frontend/src/modules/contacts/ContactEditView.test.tsx`
- Modify : `src/frontend/src/locales/en/contacts.json`
- Modify : `src/frontend/src/locales/fr/contacts.json`

**Interfaces :**
- Consomme : `cardHash` sur `GET /api/Contacts/{id}`, le `409` sur `PUT` (tâche 8).

- [ ] **Step 1 : Ajouter les clés**

Dans `en/contacts.json`, au bloc de l'éditeur :

```json
    "conflictTitle": "This contact changed elsewhere",
    "conflictBody": "Someone — or one of your devices — saved this contact while you were editing it. Reload to see their version; your changes here will be lost.",
    "conflictReload": "Reload"
```

Dans `fr/contacts.json`, les mêmes clés. **Insécable U+00A0 avant le `;`** de `conflictBody`, et
apostrophe typographique `’` partout :

```json
    "conflictTitle": "Ce contact a changé ailleurs",
    "conflictBody": "Quelqu’un — ou l’un de vos appareils — a enregistré ce contact pendant que vous le modifiiez. Rechargez pour voir sa version ; vos modifications ici seront perdues.",
    "conflictReload": "Recharger"
```

Poser ces deux valeurs françaises **par script**, pas avec l'outil d'édition : il écrit une espace
ordinaire là où le français veut U+00A0. Vérifier en lançant `parity.test.ts`, jamais à l'œil.

- [ ] **Step 2 : Déclarer le champ**

Dans `src/frontend/src/types/contacts.ts`, ajouter à l'interface de la fiche détaillée :

```ts
  /** The card's hash as it was read. Sent back on save so a stale write is refused rather than
      silently overwriting what another device stored meanwhile. */
  cardHash?: string
```

**Optionnel et jamais `null`** : l'API omet les champs nuls, donc côté client c'est `undefined`.

- [ ] **Step 3 : Écrire les tests, rouges**

Ajouter à `ContactEditView.test.tsx` :

```tsx
  it('sends back the hash it read', async () => {
    vi.mocked(api.getContact).mockResolvedValue({ ...CONTACT, cardHash: 'abc123' })
    render(<ContactEditView />)

    await userEvent.clear(await screen.findByLabelText('First name'))
    await userEvent.type(screen.getByLabelText('First name'), 'Grace')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.updateContact).toHaveBeenCalledWith(
      CONTACT.id, expect.objectContaining({ cardHash: 'abc123' })))
  })

  it('offers to reload when the contact moved under the editor', async () => {
    vi.mocked(api.getContact).mockResolvedValue({ ...CONTACT, cardHash: 'abc123' })
    vi.mocked(api.updateContact).mockRejectedValue(new ApiError('conflict', 409))
    render(<ContactEditView />)

    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))

    // A message that announces a conflict without offering the way out leaves the user with a form
    // they can neither save nor abandon without losing what they typed.
    expect(await screen.findByText('This contact changed elsewhere')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })

  it('does not lose the form when the save is refused', async () => {
    vi.mocked(api.getContact).mockResolvedValue({ ...CONTACT, cardHash: 'abc123' })
    vi.mocked(api.updateContact).mockRejectedValue(new ApiError('conflict', 409))
    render(<ContactEditView />)

    await userEvent.clear(await screen.findByLabelText('First name'))
    await userEvent.type(screen.getByLabelText('First name'), 'Grace')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByText('This contact changed elsewhere')

    // The refusal is not a reason to throw away what the user typed: reloading is their choice.
    expect(screen.getByLabelText('First name')).toHaveValue('Grace')
  })

  it('reloading fetches the contact again', async () => {
    vi.mocked(api.getContact).mockResolvedValue({ ...CONTACT, cardHash: 'abc123' })
    vi.mocked(api.updateContact).mockRejectedValue(new ApiError('conflict', 409))
    render(<ContactEditView />)

    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Reload' }))

    await waitFor(() => expect(api.getContact).toHaveBeenCalledTimes(2))
  })

  it('a save with no hash still goes through', async () => {
    // A contact the 4a backfill never reached answers no cardHash at all. The editor must not send
    // `undefined` as a value nor refuse to save.
    vi.mocked(api.getContact).mockResolvedValue(CONTACT)
    render(<ContactEditView />)

    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.updateContact).toHaveBeenCalled())
    expect(vi.mocked(api.updateContact).mock.calls[0][1]).not.toHaveProperty('cardHash')
  })
```

Lire `ContactEditView.test.tsx` pour ses vraies fabriques et ses vrais libellés avant d'écrire ces
cinq cas : les noms `CONTACT`, `api.getContact`, `api.updateContact` et « First name » / « Save »
sont indicatifs, et un libellé approximatif ferait échouer les cinq pour une raison sans rapport.

- [ ] **Step 4 : Lancer les tests pour les voir échouer**

Run : `cd src/frontend && npm test -- ContactEditView`
Expected : les cinq nouveaux cas FAIL.

- [ ] **Step 5 : Câbler l'éditeur**

Trois changements, et rien d'autre :

1. l'état de l'écran retient le `cardHash` reçu du `GET` ;
2. la charge du `PUT` le porte — **et l'omet quand il est `undefined`**, plutôt que d'envoyer la
   clé à `undefined` ;
3. un `409` ouvre la boîte de conflit — `.modal-overlay` / `.modal` / `.modal-header` /
   `.modal-title` / `.modal-actions`, comme les autres écrans — dont le bouton d'action relance le
   `GET` et repeuple le formulaire. **Le formulaire n'est pas vidé par le refus** : recharger est
   le choix de l'utilisateur, pas une conséquence.

- [ ] **Step 6 : Lancer les trois portes**

Run : `cd src/frontend && npm test`
Expected : tout au vert, `parity.test.ts` et `keys.test.ts` compris.

Run : `cd src/frontend && npx tsc --noEmit && npm run lint`
Expected : propre.

- [ ] **Step 7 : Regarder l'écran**

Run : `cd src/frontend && npm run dev`, ouvrir une fiche dans deux onglets, enregistrer dans le
premier, puis enregistrer dans le second. Vérifier de l'œil : la boîte s'ouvre, elle nomme la
conséquence, le bouton recharge, et **la saisie du second onglet est toujours là tant qu'on n'a pas
rechargé**.

Si le navigateur n'est pas disponible, le dire dans le rapport plutôt que de l'affirmer : jsdom ne
fait aucune mise en page, et aucun des cinq tests ci-dessus ne voit un pixel.

- [ ] **Step 8 : Commit**

- sujet : `feat(contacts): l'editeur renvoie le condensat lu et propose de recharger`
- corps : `Un onglet ouvert depuis dix minutes ne reecrit plus en silence la fiche que` /
  `le telephone vient de modifier.`

---

### Task 10 : le contrôle de démarrage et le balayeur

Deux mécaniques de fond, réunies parce qu'elles partagent leur enveloppe et se relisent ensemble.

Le **contrôle de démarrage** compare, par utilisateur, `MAX(contacts.sync_sequence)` à
`contact_sync_state.seq` : le premier ne peut dépasser le second que si les deux tables ne viennent
pas du même instantané. **Ce qu'il ne voit pas doit être écrit à côté de lui** — une restauration
*cohérente*, les deux tables rembobinées ensemble, le laisse muet, l'inégalité restant vraie. Il
n'attrape que la moitié détectable, et le remède reste le `.sql` de la tâche 1.

Le **balayeur** est un troisième `PeriodicSweeper` : la mécanique existe, deux l'utilisent déjà.

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/SyncStateConsistencyCheck.cs`
- Create : `src/snoopy.microservice/Services/ContactTombstoneSweeper.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/SyncStateConsistencyCheckTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactTombstoneSweeperTests.cs`

**Interfaces :**
- Consomme : `IContactSyncStore.PruneAsync`, `PruneOutcome` (tâche 4) ; `PeriodicSweeper`,
  `IStartupValidator`.

- [ ] **Step 1 : Écrire les tests, rouges**

`SyncStateConsistencyCheckTests` :

```csharp
    [Fact]
    public async Task ABookInStep_SaysNothing()
    {
        using var context = NewContextWith(seq: 10, highestContactRank: 10);
        var logger = new Mock<ILogger<SyncStateConsistencyCheck>>();
        var check = new SyncStateConsistencyCheck(context, logger.Object);

        await check.RunAsync(CancellationToken.None);

        logger.VerifyNoErrorLogged();
    }

    [Fact]
    public async Task AContactAheadOfItsState_IsLoggedAsAnError()
    {
        using var context = NewContextWith(seq: 3, highestContactRank: 11);
        var logger = new Mock<ILogger<SyncStateConsistencyCheck>>();
        var check = new SyncStateConsistencyCheck(context, logger.Object);

        await check.RunAsync(CancellationToken.None);

        // A contact cannot outrank its own counter unless the two tables came from different
        // snapshots. Named, with the .sql line to run beside it — an operator reading this line at
        // three in the morning must not have to find the remedy in a design document.
        logger.VerifyErrorLoggedContaining("contacts-sync-epoch-rotate.sql");
    }

    [Fact]
    public async Task AConsistentRestore_IsInvisibleToIt_AndThatIsWhyTheNoteExists()
    {
        // Both tables rewound together: MAX(sync_sequence) <= seq still holds, so this check is
        // silent while every client's token now covers ranks whose content changed. Recorded as a
        // test so nobody comes to rely on the check for the case it cannot see.
        using var context = NewContextWith(seq: 5, highestContactRank: 5);
        var logger = new Mock<ILogger<SyncStateConsistencyCheck>>();
        var check = new SyncStateConsistencyCheck(context, logger.Object);

        await check.RunAsync(CancellationToken.None);

        logger.VerifyNoErrorLogged();
    }

    [Fact]
    public async Task AUserWithNoStateRow_IsNotAnError()
    {
        // Every account created after the deployment is in this shape until its first write.
        using var context = NewContextWithContactsOnly(highestContactRank: 0);
        var logger = new Mock<ILogger<SyncStateConsistencyCheck>>();
        var check = new SyncStateConsistencyCheck(context, logger.Object);

        await check.RunAsync(CancellationToken.None);

        logger.VerifyNoErrorLogged();
    }
```

`ContactTombstoneSweeperTests` :

```csharp
    [Fact]
    public async Task OnePass_PrunesTombstonesAtOneHundredAndEightyDays()
    {
        var sync = new Mock<IContactSyncStore>();
        sync.Setup(s => s.PruneAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PruneOutcome(2, 1));
        var sweeper = NewSweeper(sync.Object);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        sync.Verify(s => s.PruneAsync(
            It.Is<DateTime>(d => WithinAnHourOf(d, DateTime.UtcNow.AddDays(-180))),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnePass_PrunesRevisionsAtThirtyDays()
    {
        var sync = new Mock<IContactSyncStore>();
        sync.Setup(s => s.PruneAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PruneOutcome(0, 0));
        var sweeper = NewSweeper(sync.Object);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        // Thirty and not a hundred and eighty: the tombstone is what the protocol must still be
        // able to tell a client gone a long time, the revision is what a human might still want
        // back. Past thirty days a deleted card stays correctly deleted everywhere — it is simply
        // no longer restorable.
        sync.Verify(s => s.PruneAsync(
            It.IsAny<DateTime>(),
            It.Is<DateTime>(d => WithinAnHourOf(d, DateTime.UtcNow.AddDays(-30))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnePass_LogsItsOutcomeEvenWhenItRemovedNothing()
    {
        var sync = new Mock<IContactSyncStore>();
        sync.Setup(s => s.PruneAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PruneOutcome(0, 0));
        var logger = new Mock<ILogger<ContactTombstoneSweeper>>();
        var sweeper = NewSweeper(sync.Object, logger.Object);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        // Zero included, so the line doubles as the sweeper's heartbeat — the convention the two
        // existing sweepers already follow.
        logger.VerifyInformationLogged();
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~SyncStateConsistencyCheck|FullyQualifiedName~ContactTombstoneSweeper"`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire le contrôle**

Créer `Services/CardDav/SyncStateConsistencyCheck.cs`. Une requête groupée, une comparaison, une
ligne de journal structurée par utilisateur en écart — **jamais d'interpolation** — nommant le
`user_id` et le fichier `.sql` à jouer. Le message porte aussi ce que le contrôle **ne voit pas**,
en une phrase, parce que c'est la ligne qu'un opérateur lira à trois heures du matin.

L'enregistrer comme `IStartupValidator` à côté de ses voisins, ou en `IHostedService` à passe
unique si le projet n'en a pas d'autre — lire `Configuration/ApplicationServicesConfiguration.cs`
et suivre ce qui existe plutôt que d'introduire une troisième forme.

- [ ] **Step 4 : Écrire le balayeur**

Créer `Services/ContactTombstoneSweeper.cs`, sur le modèle exact de `TrustedSenderSweeper` :
`PeriodicSweeper` avec une période de 24 h et une gigue de démarrage de 5 min, un `IServiceScope`
par passe, un appel à `PruneAsync(UtcNow - 180j, UtcNow - 30j)`, et une ligne d'information par
passe portant les deux compteurs.

L'enregistrer : `services.AddHostedService<ContactTombstoneSweeper>();`

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

Réverter d'abord `ApiDocumentation.xml`.

- sujet : `feat(carddav): le controle de coherence au demarrage et le balayeur`
- corps : `Tombes a 180 jours, revisions a 30. Le controle n'attrape que la` /
  `restauration incoherente, et sa ligne de journal le dit.`

---

## Paquet 5 — les résidus que le protocole rend routiniers

Cinq défauts connus, tous consignés dans `docs/superpowers/contacts-4a-residuals.md`, tous
inatteignables ou cosmétiques jusqu'ici. Ce qui change n'est pas leur nature mais leur fréquence :
deux d'entre eux sont des **pertes de données** que le protocole rend routinières, et le document
des résidus désigne le premier comme « celui qui compte ».

### Task 11 : les quatre défauts du composeur et du projecteur

**Files :**
- Modify : `src/snoopy.microservice/Services/Contacts/VCardComposer.cs`
- Modify : `src/snoopy.microservice/Services/Contacts/VCardProjector.cs`
- Modify : `src/snoopy.microservice/snoopy.microservice.Tests/Services/Contacts/VCardCorpusTests.cs`
- Modify : `docs/superpowers/contacts-4a-residuals.md`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/Contacts/VCardComposerResidualTests.cs`

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public void AFamilyFallingBackToOneOccurrence_KeepsItsXParameters()
    {
        // "The one that counts": 4b made this reachable from the editor, 4c makes it reachable from
        // any phone, and what disappears — an Apple X-ABLabel, say — is found nowhere else.
        var stored =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\n" +
            "item1.TEL;TYPE=CELL;X-ABLabel=Perso:+3210\r\n" +
            "item2.TEL;TYPE=WORK:+3211\r\n" +
            "END:VCARD\r\n";

        var composed = VCardComposer.Compose(stored, "u1", WriteWithOnePhone("+3210"));

        Assert.Contains("X-ABLabel=Perso", composed);
    }

    [Fact]
    public void Folding_NeverSplitsASurrogatePair()
    {
        // Until this slice a folded card only went to the database; it is now served to third-party
        // clients, and a card cut in the middle of a character is an invalid card delivered by the
        // protocol. An emoji in a NOTE is enough — and 4b's editor writes free text.
        var note = new string('a', 70) + "\U0001F600" + new string('b', 40);

        var folded = VCardComposer.ComposeNew("u1", WriteWithNote(note));

        // The pair survives: decoding the folded card and unfolding it gives the note back whole.
        Assert.Contains("\U0001F600", Unfold(folded));
        Assert.DoesNotContain('�', folded);
    }

    [Fact]
    public void AUidThatLooksLikeAUri_IsNotRelabelledValueText()
    {
        var stored = "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:urn:uuid:aaaa\r\nFN:X\r\nEND:VCARD\r\n";

        var composed = VCardComposer.Compose(stored, "urn:uuid:aaaa", WriteNamed("X"));

        // The UID's value does not turn on the production path — the column comes from
        // VCardImportMapper.UidOf, a textual scan that keeps the prefix. Only a VALUE=TEXT label is
        // added on a URI-shaped value: cosmetically non-conforming, and now served to real clients.
        Assert.Contains("UID:urn:uuid:aaaa", composed);
        Assert.DoesNotContain("VALUE=TEXT", composed);
    }

    [Fact]
    public void TheProjector_StopsAtTheFirstEndVcard()
    {
        // Unreachable while the splitter guaranteed one card per chunk. The PUT of plan c becomes a
        // second producer of vcard_raw, so the guarantee stops holding at the entrance.
        var two =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:First\r\nEND:VCARD\r\n" +
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u2\r\nFN:Second\r\nEND:VCARD\r\n";

        var projected = VCardProjector.Project(two);

        Assert.Equal("First", projected.DisplayName);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~VCardComposerResidual`
Expected : les quatre cas FAIL.

- [ ] **Step 3 : Corriger `SpliceFamily`**

La garde `if (model.Count < 2) return;` (`VCardComposer.cs:504`) sort avant d'avoir regardé les
lignes d'origine, donc une famille qui retombe à une occurrence est ré-rendue seule par la
bibliothèque et perd ses paramètres `X-`. Faire entrer le cas `model.Count == 1` dans le chemin de
recollement quand une ligne brute existe pour ce rang — les paramètres viennent de la ligne
stockée, la valeur du modèle.

- [ ] **Step 4 : Corriger `Fold`**

`Fold` avance de 75 puis de 74 **unités UTF-16**, donc une paire de substitution à cheval sur une
frontière est coupée en deux moitiés qui ne sont plus des caractères. Reculer d'une unité quand
l'index de coupe tombe entre un substitut haut et un substitut bas — `char.IsHighSurrogate` sur le
caractère qui précède la coupe suffit.

**Ruling, écrit ici pour qu'une revue ne le rouvre pas :** `Fold` compte des unités UTF-16 là où le
RFC 6350 compte des **octets**, donc une ligne de texte français peut dépasser 75 octets après ce
correctif. **On ne corrige pas ce point-là**, et pour une raison qui tient : le pliage est un
`SHOULD` et une ligne trop longue est tolérée par tous les clients connus, tandis qu'une paire de
substitution coupée produit de l'UTF-8 invalide, que rien ne tolère. Corriger la coupe est
obligatoire, passer aux octets est une amélioration — et elle change la sortie de toutes les cartes
non-ASCII déjà en base, ce qui est un travail à part avec son propre rattrapage. Consigner la
divergence dans les résidus, avec cette raison.

- [ ] **Step 5 : Corriger l'étiquette `VALUE=TEXT` de l'`UID`**

Retirer le libellé quand la valeur émise **est déjà** celle de la colonne : ne rien ré-étiqueter
d'une valeur qu'on n'a pas transformée. Le plus simple et le plus sûr est de recoller la ligne
`UID` d'origine quand elle existe et que sa valeur égale la colonne.

- [ ] **Step 6 : Corriger `VCardProjector.RawCard`**

Faire s'arrêter la lecture au premier `END:VCARD`. Un corps portant plus d'une carte est refusé
plus haut au plan c (`403 valid-address-data`) ; ici il ne s'agit que de ne pas projeter la seconde
par-dessus la première si jamais il en arrive une.

- [ ] **Step 7 : Corriger le test du corpus**

Dans `VCardCorpusTests.Corpus_SurvivesASingleFieldEdit`, composer avec la forme rendue par
`VCardImportMapper.UidOf` plutôt qu'avec l'`Uid` du projecteur : le projecteur retire le préfixe
`urn:uuid:`, la production ne l'utilise jamais comme source, et le test modélisait donc un chemin
qui n'existe pas.

- [ ] **Step 8 : Mettre les résidus à jour**

Dans `docs/superpowers/contacts-4a-residuals.md`, marquer les quatre lignes comme fermées en 4c-ii-a
et ajouter la divergence de comptage de `Fold` avec sa raison. **Ne pas supprimer les lignes** : un
résidu fermé se lit, un résidu disparu se redécouvre.

- [ ] **Step 9 : Lancer les tests**

Run : `cd src && dotnet test`
Expected : suite verte. **Les tests du corpus vCard vont bouger** — c'est le but ; vérifier
qu'aucun ne change de sens.

Run : `cd src && dotnet build`
Expected : zéro avertissement.

- [ ] **Step 10 : Commit**

- sujet : `fix(vcard): les quatre defauts que le protocole rend routiniers`
- corps : `Les parametres X- survivent a une famille effondree, le pliage ne coupe plus` /
  `une paire de substitution, et l'UID cesse d'etre re-etiquete.`

---

### Task 12 : la vraie sémantique d'ETag sur la route d'avatar

`ContactsController.GetPhoto` compare aujourd'hui `If-None-Match` **par égalité exacte de chaîne**.
Cela ignore `*`, les ETags faibles (`W/"…"`) et les listes à plusieurs valeurs — les trois formes
que le RFC 7232 autorise et que les clients envoient. La route est interne aujourd'hui ; 4c porte
la vraie sémantique d'ETag, et une route qui la contredit dans le même service est une divergence
qu'une revue de 4d prendra pour un bug du serveur.

**Files :**
- Modify : `src/snoopy.microservice/Controllers/ContactsController.cs`
- Create : `src/snoopy.microservice/Services/CardDav/EntityTagMatcher.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/EntityTagMatcherTests.cs`

**Interfaces :**
- Produit, **consommé par les plans b et c** — `GET`, `PUT` et `DELETE` de DAV s'en serviront
  tels quels :

```csharp
internal static class EntityTagMatcher
{
    /// True when the If-None-Match header matches the resource's tag: `*` matches anything that
    /// exists, a comma-separated list matches on any member, and the weak prefix is ignored (RFC
    /// 7232 § 2.3.2 — If-None-Match uses the weak comparison function).
    internal static bool NoneMatch(string? header, string entityTag);

    /// True when the If-Match header matches. `*` matches anything that exists, and the comparison
    /// is STRONG — a weak tag never satisfies If-Match.
    internal static bool Match(string? header, string entityTag);
}
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Theory]
    [InlineData("\"abc\"", "\"abc\"", true)]
    [InlineData("*", "\"abc\"", true)]
    [InlineData("W/\"abc\"", "\"abc\"", true)]
    [InlineData("\"xyz\", \"abc\"", "\"abc\"", true)]
    [InlineData("\"xyz\" , W/\"abc\"", "\"abc\"", true)]
    [InlineData("\"xyz\"", "\"abc\"", false)]
    [InlineData("", "\"abc\"", false)]
    [InlineData(null, "\"abc\"", false)]
    public void NoneMatch_UsesTheWeakComparison(string? header, string tag, bool expected) =>
        Assert.Equal(expected, EntityTagMatcher.NoneMatch(header, tag));

    [Theory]
    [InlineData("\"abc\"", "\"abc\"", true)]
    [InlineData("*", "\"abc\"", true)]
    [InlineData("\"xyz\", \"abc\"", "\"abc\"", true)]
    [InlineData("W/\"abc\"", "\"abc\"", false)]
    [InlineData("\"xyz\"", "\"abc\"", false)]
    [InlineData(null, "\"abc\"", false)]
    public void Match_UsesTheStrongComparison(string? header, string tag, bool expected) =>
        // If-Match guards a write. A weak tag says "semantically equivalent", which is not a
        // promise the byte-for-byte replacement of a card can rest on.
        Assert.Equal(expected, EntityTagMatcher.Match(header, tag));

    [Fact]
    public void AMalformedHeader_MatchesNothingRatherThanThrowing()
    {
        // A header is client input. The worst it may do is fail to match; a throw here would be a
        // 500 on a conditional GET, which a DAV client retries for ever.
        Assert.False(EntityTagMatcher.NoneMatch("not a tag at all", "\"abc\""));
        Assert.False(EntityTagMatcher.Match("\"unterminated", "\"abc\""));
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~EntityTagMatcher`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire le comparateur**

Créer `Services/CardDav/EntityTagMatcher.cs`. Découper sur la virgule, élaguer les blancs, traiter
`*` en premier, retirer le préfixe `W/` pour `NoneMatch` et le refuser pour `Match`, comparer le
reste littéralement guillemets compris. **Ne jamais lever** : un en-tête est une entrée client.

- [ ] **Step 4 : Câbler `GetPhoto` dessus**

Remplacer la comparaison exacte par `EntityTagMatcher.NoneMatch(Request.Headers.IfNoneMatch, tag)`.
Ne rien changer d'autre à la route : elle garde son `Content-Disposition`, son `nosniff` et son
`304` nu.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement. Les tests existants de `GetPhoto` doivent rester verts
**sans être modifiés** : la comparaison exacte est un cas particulier de la faible.

- [ ] **Step 6 : Commit**

Réverter d'abord `ApiDocumentation.xml`.

- sujet : `fix(contacts): la route d'avatar honore la vraie semantique d'ETag`
- corps : `Etoile, valeurs multiples et prefixe faible ; le comparateur servira aux` /
  `verbes DAV des plans b et c.`

---

## Vérification de fin de plan

- [ ] `cd src && dotnet test` — les deux suites au vert.
- [ ] `cd src && dotnet build` — zéro avertissement.
- [ ] `cd src/frontend && npm test && npx tsc --noEmit && npm run lint` — propre.
- [ ] `git status` — `src/snoopy.microservice/ApiDocumentation.xml` non modifié.
- [ ] Le DDL de la tâche 1 est joué sur `snoopy_webmail_dev`, **puis** le rattrapage, **puis** les
      deux requêtes de contrôle rendent `0`.
- [ ] La procédure manuelle de la tâche 3 étape 6 a été jouée une fois, et son résultat est écrit
      dans le rapport de tâche.

## Ce que ce plan ne fait pas, et qui appartient aux plans b et c

Écrit ici pour qu'aucune revue de 4c-ii-a ne le lise comme un manque :

- **Aucune route `/dav`, aucun XML, aucun verbe.** La séquence, les tombes et les révisions
  existent et personne ne les lit encore — c'est voulu, et c'est ce qui rend le rattrapage sûr à
  jouer avant que le premier client n'existe.
- `DavPaths`, la validation de `dav_name`, l'encodage et le décodage des segments : plan b. Ce plan
  n'écrit que `{id}.vcf`, une forme qui ne pose aucune des questions que la décision 5 tranche.
- Le jeton, le ctag et leur epoch : le compteur et l'epoch sont écrits ici, leur **format** et leur
  refus (`403 valid-sync-token`) appartiennent au plan c.
- La traduction des plafonds et des attentes de verrou en `507`, `503` et `Retry-After` : c'est un
  travail de bord, et il n'y a pas de bord avant le plan b. `ContactStore` rend toujours ses
  `Result.Failure` d'aujourd'hui.
- L'écran de restauration d'une révision : hors tranche, et délibérément. La donnée est écrite
  parce qu'elle ne se retrouve pas après coup ; l'interface s'ajoute le jour où un premier cas réel
  dira si le geste est « rendre cette version » ou « comparer les deux ». En attendant, la reprise
  se fait par requête.
- Le passage de `Fold` à un comptage en octets, et les trois résidus 4a que la spec laisse au
  backlog (`URL;TYPE=PREF` perdu sur un aller-retour 3.0, la troncature des scalaires, le `?` d'une
  composante `N` à plusieurs valeurs).
