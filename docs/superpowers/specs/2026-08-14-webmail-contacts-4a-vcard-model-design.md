# Contacts 4a — modèle complet et moteur vCard

Première tranche du projet CardDAV, à la suite de
[3a/3b](2026-07-27-webmail-contacts-3a3b-design.md), [3c](2026-07-27-webmail-contacts-3c-design.md)
et [3d](2026-07-27-webmail-contacts-3d-design.md). Backend seul : aucun écran ne change.

## Le projet dont c'est la première pièce

Le module Contacts modélise aujourd'hui un nom, un pseudo, un drapeau favori et des adresses
e-mail. Tout le reste — téléphones, adresses postales, société, anniversaire, notes, photo — vit
dans `vcard_raw`, que rien ne lit. Ouvrir le carnet à CardDAV signifie donc d'abord le compléter,
faute de quoi une fiche venue d'un téléphone s'afficherait amputée.

Le projet se décompose en quatre tranches, aux dépendances strictes :

| | Tranche | Dépend de |
|---|---|---|
| **4a** | Modèle de données complet + moteur d'aller-retour vCard *(ce document)* | — |
| 4b | Éditeur et fiche webmail étendus | 4a |
| 4c | Serveur CardDAV (découverte, collection, rapports, verbes, ETags, pierres tombales) | 4a |
| 4d | Conformité clients (`ccs-caldavtester`, Thunderbird, DAVx⁵, iPhone emprunté) | 4c |

L'ordre retenu est **4a → 4b → 4c → 4d** : le carnet devient complet et utile avant qu'aucun
protocole ne s'appuie dessus, ce qui met le moteur vCard à l'épreuve d'une édition réelle avant
qu'un client externe n'en dépende.

## Ce que fait la tranche

Les tables du module accueillent les propriétés vCard que le carnet ignorait. `vcard_raw` cesse
d'être un dépôt inerte et devient **la donnée** : les colonnes en sont une projection recalculée à
chaque écriture. Un lecteur et un écrivain de vCard apparaissent, et l'import accepte le `.vcf` en
plus du CSV.

Aucun écran ne bouge. Les nouveaux champs sont stockés, projetés, exportés — pas encore affichés.

## Décisions

**1. La carte est souveraine ; les colonnes sont une projection.** `vcard_raw` porte la vérité ;
les colonnes et les tables filles n'existent que pour l'affichage, la recherche, le tri,
l'autocomplétion et l'export. Deux portes d'entrée, une seule sortie :

```
   édition webmail ──▶ carte stockée + champs modifiés ──▶ nouvelle carte ─┐
                                                                          ├──▶ vcard_raw ──parse──▶ projection
   PUT CardDAV (4c) ─────────── carte reçue, verbatim ────────────────────┘
```

L'inverse — colonnes souveraines, carte régénérée à la lecture — a été écarté sur une conséquence
qui ne se paie qu'en 4c : une carte reconstruite n'est jamais octet-pour-octet identique à celle
qui est arrivée (repliement des lignes, ordre des propriétés, casse des paramètres), donc son ETag
change sans qu'aucune donnée n'ait changé, et un client re-télécharge le carnet entier à chaque
passe. Le socle posé en 3a/3b pointait déjà dans cette direction : `vcard_raw` a été créé pour « ne
pas détruire ce qu'on ne modélise pas » et `uid` pour être « l'identité sur laquelle un client
CardDAV se synchronise ».

**2. On ne re-sérialise jamais une carte qu'on n'a pas modifiée.** C'est la règle d'or de la
tranche, et elle est ce qui rend l'ETag stable en 4c. Elle a une raison technique précise, donnée
en décision 3.

**3. La projection est totale et destructrice, jamais incrémentale.** À chaque écriture les lignes
filles sont effacées et réécrites depuis la carte. Une projection qui « met à jour ce qui a changé »
diverge silencieusement de la carte, et rien ne peut détecter la divergence.

**4. Tout ce que l'éditeur réécrit doit être projeté intégralement.** C'est le corollaire dangereux
de la décision 1, et il commande la forme du schéma. Le composeur remplace les propriétés qu'il
modélise ; si la projection d'une propriété est partielle, l'édition détruit la part non projetée.
Trois conséquences concrètes :

- **Les sept composantes de `ADR` sont modélisées**, boîte postale comprise. L'écrivain actuel
  écrit `null` en composante 0 ; ne pas la stocker signifierait qu'une édition webmail efface la
  boîte postale d'une adresse venue d'un téléphone.
- **Le paramètre `TYPE` est stocké verbatim**, tel qu'il figure sur la carte, et non traduit en
  énumération. `TYPE=WORK,VOICE,PREF` doit ressortir tel quel ; une énumération maison en perdrait
  la moitié. `contact_emails`, qui n'a pas de colonne de type aujourd'hui, en reçoit une pour la
  même raison.
- **Le nom de groupe est stocké lui aussi.** Apple associe ses libellés personnalisés à leur
  propriété par un groupe (`item1.TEL` et `item1.X-ABLabel:Bureau de Lyon`). Réécrire la propriété
  sans son groupe orpheline le libellé, qui reste dans la carte en désignant plus rien — la
  propriété est préservée et l'information détruite quand même.

**5. Une propriété répétable modélisée comme valeur unique n'est remplacée qu'en première
occurrence.** `URL` peut figurer plusieurs fois ; la colonne `website` n'en porte qu'une. Le
composeur remplace la première et laisse les suivantes en place, plutôt que de les écraser toutes.
La règle vaut pour toute propriété dans ce cas ; `EMAIL`, `TEL` et `ADR`, projetées intégralement,
sont remplacées en bloc.

**6. `FolkerKinzel.VCards` plutôt qu'un parseur maison.** Elle lit et écrit 2.1, 3.0 et 4.0 et
préserve les propriétés non standard, ce qui n'est pas accessoire : une carte iPhone est truffée de
`X-AB*` groupées, et un moteur qui les perd détruit la moitié de son information à la première
édition. Un parseur maison a été envisagé et écarté — dépliage des lignes, échappement RFC 6868,
quoted-printable en 2.1, propriétés groupées, photos base64 : le volume d'une bibliothèque, et
chacun de ces cas rate en silence.

**La réserve, et elle fonde la règle d'or.** La bibliothèque convertit toute carte lue en 4.0 en
interne et la reconvertit à l'écriture ; un aller-retour n'est donc jamais octet-pour-octet. Sous
la décision 1 ce n'est pas un problème, puisqu'on ne sérialise que lorsqu'une modification a eu
lieu et que l'ETag doit alors changer de toute façon. C'est aussi pourquoi l'aller-retour
octet-pour-octet **n'est pas testé : il n'est pas promis**.

**7. On émet du vCard 3.0.** C'est la version qu'Apple et Google produisent et attendent. Une carte
arrivée par DAV est stockée dans la version où elle est arrivée — on ne la convertit pas, on ne la
touche pas.

**8. Le lecteur est tolérant ; il ne refuse jamais une carte.** Une propriété illisible est ignorée,
jamais fatale. Une carte refusée en projection est une carte qu'un client re-poussera indéfiniment,
et le refus se manifesterait en 4c par une boucle de synchronisation qu'aucun journal client ne
saurait expliquer.

**9. `card_hash` remplace `updated_at` comme base de l'ETag.** Le document de schéma de 3a/3b
désignait `updated_at`, faute de mieux, avant que la carte ne soit souveraine. Un SHA-256 de la
carte est exact — deux écritures dans la même seconde ne collisionnent pas, et une écriture qui ne
change rien ne change pas l'ETag. La colonne est posée ici parce qu'elle est gratuite maintenant et
qu'une migration de plus en 4c ne l'est pas.

**10. `display_name` est stocké.** `FN` est obligatoire en vCard et le frontend le devine
aujourd'hui (prénom + nom, sinon pseudo, sinon première adresse). Une carte portant
`FN:Dr. John Smith Jr.` s'afficherait « John Smith ». La colonne le capture ; `displayNameOf` la
préférera en 4b, en gardant la chaîne de repli actuelle pour les fiches qui n'en portent pas.

**11. `birthday` est du texte, pas une `DATE`.** vCard admet les dates partielles — `--0315`, un
anniversaire sans année — et du texte libre en 4.0. Une colonne `DATE` refuserait des cartes
parfaitement valides. La forme vCard est stockée telle quelle ; l'interprétation est un problème
d'affichage, donc de 4b.

**12. La photo est une table de projection, et ne descend jamais dans la liste.** `PHOTO` est du
base64 en ligne, couramment 50 à 300 Ko. Or `GET /api/Contacts` rend le carnet entier en une
réponse — c'est le choix documenté, la recherche et le tri étant côté client. Deux mille contacts
avec photo dans une seule réponse est un chiffre qu'on ne veut pas écrire. La photo sort par une
route dédiée ; la liste ne porte qu'un booléen. La duplication avec `vcard_raw` est voulue et
cohérente avec la décision 1 : une projection est dérivée par définition, et sans elle servir un
avatar signifie charger la carte entière.

**13. La liste reste maigre ; la fiche complète devient une route.** `GET /api/Contacts` ne gagne ni
téléphones ni adresses postales : la liste n'affiche que le nom et l'adresse, et le carnet entier y
descend d'un coup. `GET /api/Contacts/{id}` apparaît pour la fiche. C'est le changement de forme le
plus visible pour 4b, où la carte de droite passe d'un rendu depuis le cache à un appel dédié.

**14. L'import `.vcf` entre dans le périmètre.** Le lecteur existe désormais ; l'import vCard est
quasi gratuit, et c'est le format que les gens exportent réellement de leur téléphone. Il emprunte
la route et les règles de fusion de l'import CSV de 3d — fusion sur l'adresse, à défaut sur le nom
exact, jamais d'écrasement.

**15. Le rattrapage est un endpoint admin idempotent.** Les fiches saisies à la main portent
`vcard_raw = NULL` ; sous la décision 1 toute fiche doit avoir une carte. La génération passe par
le composeur, donc c'est du code et non du SQL, et l'idempotence permet de le rejouer après un
correctif du moteur.

## Schéma

À rejouer sur `snoopy_webmail` **et** `snoopy_webmail_dev`. Création manuelle : ce projet n'utilise
pas les migrations EF. Le document de prérequis
[`webmail-contacts-tables.md`](../webmail-contacts-tables.md) reçoit ces blocs comme il a reçu ceux
de 3c.

La collation suit la règle déjà posée pour ces tables : `utf8mb4_bin` par défaut, et
`utf8mb4_unicode_ci` sur les seules colonnes de texte humain, pour qu'un `LIKE` serveur y reste
utilisable si une recherche apparaît un jour.

```sql
ALTER TABLE `contacts`
  ADD COLUMN `display_name` VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL
    COMMENT 'La propriété FN de la carte ; devinée côté client jusqu''ici' AFTER `nickname`,
  ADD COLUMN `middle_name`  VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `display_name`,
  ADD COLUMN `name_prefix`  VARCHAR(50)  COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `middle_name`,
  ADD COLUMN `name_suffix`  VARCHAR(50)  COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `name_prefix`,
  ADD COLUMN `organization` VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `name_suffix`,
  ADD COLUMN `department`   VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL
    COMMENT 'Composantes 2..n de ORG, jointes par ; comme sur la carte' AFTER `organization`,
  ADD COLUMN `job_title`    VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `department`,
  ADD COLUMN `birthday`     VARCHAR(32)  DEFAULT NULL
    COMMENT 'Forme vCard telle quelle : une date partielle (--0315) est valide' AFTER `job_title`,
  ADD COLUMN `website`      VARCHAR(512) DEFAULT NULL
    COMMENT 'Première occurrence de URL ; les suivantes restent dans la carte' AFTER `birthday`,
  ADD COLUMN `notes`        TEXT COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `website`,
  ADD COLUMN `card_hash`    CHAR(64) NOT NULL DEFAULT ''
    COMMENT 'SHA-256 hex de vcard_raw ; base de l''ETag CardDAV' AFTER `vcard_raw`;

ALTER TABLE `contact_emails`
  ADD COLUMN `type`       VARCHAR(64) NOT NULL DEFAULT ''
    COMMENT 'Paramètre TYPE verbatim ; vide = sans type',
  ADD COLUMN `group_name` VARCHAR(64) NOT NULL DEFAULT ''
    COMMENT 'Groupe de la propriété (item1.EMAIL) ; ce qui rattache un X-ABLabel Apple';

CREATE TABLE `contact_phones` (
  `contact_id` CHAR(36)          NOT NULL,
  `position`   SMALLINT UNSIGNED NOT NULL COMMENT '0 = numéro principal',
  `number`     VARCHAR(64)       NOT NULL COMMENT 'Tel que porté par la carte ; aucune canonicalisation',
  `type`       VARCHAR(64)       NOT NULL DEFAULT '',
  `group_name` VARCHAR(64)       NOT NULL DEFAULT '',
  PRIMARY KEY (`contact_id`, `position`),
  CONSTRAINT `fk_contact_phones_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_addresses` (
  `contact_id`  CHAR(36)          NOT NULL,
  `position`    SMALLINT UNSIGNED NOT NULL,
  `type`        VARCHAR(64)       NOT NULL DEFAULT '',
  `group_name`  VARCHAR(64)       NOT NULL DEFAULT '',
  `po_box`      VARCHAR(64)  COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `extended`    VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `street`      VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `locality`    VARCHAR(128) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `region`      VARCHAR(128) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `postal_code` VARCHAR(32)  DEFAULT NULL,
  `country`     VARCHAR(128) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`contact_id`, `position`),
  CONSTRAINT `fk_contact_addresses_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_photos` (
  `contact_id` CHAR(36)    NOT NULL,
  `media_type` VARCHAR(64) NOT NULL,
  `bytes`      MEDIUMBLOB  NOT NULL,
  PRIMARY KEY (`contact_id`),
  CONSTRAINT `fk_contact_photos_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

**Les tables filles sont clés sur `(contact_id, position)` et non sur leur valeur**, à la différence
de `contact_emails`, dont la clé porte l'adresse. Un même numéro peut légitimement figurer deux fois
sous deux types, et deux adresses postales peuvent partager toutes leurs composantes sauf le type.

**`card_hash` est `NOT NULL DEFAULT ''`** plutôt que nullable : la chaîne vide dit « pas encore
calculé », ce qui est exactement l'état des lignes existantes avant le rattrapage, et évite un
troisième état à raisonner.

**Les relations EF doivent être déclarées** dans `PreferencesDbContext`. Sans arête déclarée, EF
ordonne les `INSERT` par nom de table et les tests InMemory, qui n'appliquent aucune clé étrangère,
ne peuvent pas attraper l'inversion.

## Le moteur

Deux composants purs, dans `Services/Contacts/`, testables sans base :

**`VCardProjector`** — carte → `ContactProjection`, un record portant les noms, les adresses e-mail,
les téléphones, les adresses postales, les scalaires et la photo. Ne touche à rien : c'est le store
qui écrit.

**`VCardComposer`** — `(carte existante | rien) + ContactWrite` → nouvelle carte. Remplace `N`,
`FN`, `NICKNAME`, `EMAIL`, `TEL`, `ADR`, `ORG`, `TITLE`, `BDAY`, `NOTE` et la première `URL` ; laisse
tout le reste intact, groupes compris.

`ContactVCardWriter`, qui fabrique aujourd'hui une carte depuis une ligne CSV, **est absorbé par
`VCardComposer`**. Deux écrivains de vCard dans le même projet, c'est le doublon qui diverge.

Le calcul de `card_hash` vit dans le store, à l'endroit unique où `vcard_raw` est écrit — un hash
calculé par les appelants est un hash qu'un appelant oubliera.

**`uid` et `source` restent intouchés par l'édition**, comme aujourd'hui : le premier est l'identité
sur laquelle un client se synchronise et le réécrire dupliquerait la fiche à sa prochaine passe ; le
second enregistre une origine que modifier une fiche ne change pas. La règle est déjà écrite dans
`ContactStore.UpdateAsync` ; elle survit à cette tranche telle quelle, `vcard_raw` mis à part, qui
cesse précisément d'être intouchable.

## Le contrat d'API

| Route | Changement |
|---|---|
| `GET /api/Contacts` | Inchangée en substance ; gagne `hasPhoto`. Ni téléphones ni postales. |
| `GET /api/Contacts/{id}` | **Nouvelle.** La fiche complète. 200 / 404. |
| `GET /api/Contacts/{id}/Photo` | **Nouvelle.** Binaire, `nosniff`, disposition attachement. 200 / 404. |
| `POST /api/Contacts` | Le corps accueille les nouveaux champs ; la réponse reste la fiche validée. |
| `PUT /api/Contacts/{id}` | Idem. Remplace la fiche entière, nouveaux champs compris. |
| `POST /api/Contacts/Import` | Accepte le `.vcf` en plus du CSV, distingués sur le type MIME puis sur le contenu. |
| `GET /api/Contacts/Export` | Le CSV puise maintenant dans les colonnes ; les champs qu'il écrivait à vide sortent remplis. |

**Un contact d'autrui répond 404 et jamais 403**, sur les deux routes nouvelles comme sur les
existantes : le store est scopé par utilisateur, un id étranger ne résout rien, et 403 confirmerait
son existence.

## Le rattrapage

Un endpoint admin idempotent parcourt les contacts, génère la carte manquante depuis les colonnes,
puis projette tout le monde. Il est documenté dans `docs/superpowers/` comme les autres prérequis
de déploiement, et journalise son avancement — c'est un balayage sur l'ensemble des utilisateurs,
et une opération silencieuse dont personne ne sait si elle a fini est une opération qu'on rejoue
dans le doute.

Ordre imposé : les tables et colonnes d'abord, le déploiement du backend ensuite, le rattrapage en
dernier. Un backend qui projette avant que les tables n'existent tombe à la première écriture.

## Limites et validation

`ContactValidator` s'étend : `MaxPhonesPerContact` et `MaxPostalAddressesPerContact` à 10 chacun, et
les longueurs des nouvelles colonnes miroitées comme le sont déjà celles des noms — non bornée ici,
une valeur trop longue atteint une MariaDB en mode strict et revient en 500.

**Nouveau plafond : 1 Mo par carte.** C'est aussi le `max-resource-size` que 4c devra annoncer aux
clients ; l'écrire ici évite qu'il soit choisi deux fois. Le plafond de 5000 contacts par
utilisateur tient tel quel.

## Les tests

Le vrai test est un **corpus de cartes réelles** : un export iPhone avec ses groupes `item1.`, un
export Google, un Thunderbird, un DAVx⁵. Ces fichiers valent plus que n'importe quelle fixture
écrite à la main, et c'est le seul endroit de la tranche où le comportement réel des clients est
observable avant 4d.

Deux familles d'assertions :

- **Projection** — telle carte produit telles colonnes et telles lignes filles, dans tel ordre.
- **Survie** — parse, on modifie un seul champ, on sérialise, on re-parse : toute propriété non
  modélisée est encore là, et tout libellé groupé désigne encore sa propriété. C'est l'assertion qui
  protège les `X-AB*` d'Apple, et c'est celle qui doit rougir en premier si le moteur régresse.

L'aller-retour octet-pour-octet n'est pas testé, parce qu'il n'est pas promis — décision 6.

## Hors périmètre

Les groupes de contacts (`KIND:group`, `CATEGORIES`), les carnets multiples, une corbeille des
contacts, l'affichage des nouveaux champs (4b) et tout DAV (4c). Chacun mérite sa tranche.
