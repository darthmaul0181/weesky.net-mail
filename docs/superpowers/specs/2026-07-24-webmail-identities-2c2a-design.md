# Webmail — Tranche 2c2a : identités d'envoi

**Date :** 2026-07-24
**Statut :** design validé, prêt pour la planification d'implémentation
**Amont :** tranche 2c1 (rédaction & envoi). La tranche 2c2 annoncée en 2c1 se scinde en deux :

| Sous-tranche | Contenu | Dépend de |
|---|---|---|
| **2c2a** | ce document — identités d'envoi : table, endpoints, écran Settings, sélecteur From | 2c1 |
| **2c2b** | réponse / répondre à tous / transfert (citation + threading) | 2c2a |
| **2c3** | brouillons, signatures | 2c1 |

---

## 1. Le problème

2c1 fige le `From` sur l'adresse principale. L'ouvrir aux alias semble immédiat — la liste
existe déjà (`GET /api/aliases`) — mais elle ne convient pas telle quelle : un compte porte
couramment **30 alias, parfois plus de 100**. Un menu déroulant de cent entrées n'est pas un
choix, c'est un obstacle. Et un alias n'a pas de nom : envoyer depuis `michel.dubois@weesky.be`
sous le libellé du compte principal est faux.

Deux notions se cachaient sous le mot « alias » :

| | Sert à | Taille | Visible |
|---|---|---|---|
| **Alias de réception** | répondre à « est-ce moi ? » — dédupe du reply-all (2c2b), présélection d'identité | 30 à 100+ | jamais affiché comme choix |
| **Identités d'envoi** | ce que le sélecteur From propose | 1 à 5 en pratique | c'est *la* liste |

La première reste `GET /api/aliases`, autorité inchangée sur « quelles adresses sont à moi ».
La seconde est une **préférence webmail** à curer, avec un libellé par adresse.

---

## 2. Décisions validées

| Sujet | Décision |
|---|---|
| Modèle | Sous-ensemble curé de {adresse principale} ∪ {alias}, un libellé par adresse, un défaut. Modèle Rainloop |
| Stockage | Table `sending_identities` dans `snoopy_webmail` — préférence webmail, jamais dans `dovecot` |
| Contrôleur | **`/api/Identities`**, hors `MailController` : aucune session IMAP, aucun cookie de credentials, deux lectures de base |
| Écriture | **`PUT` de l'ensemble**, pas de CRUD par ligne : l'ordre et le défaut deviennent atomiques |
| Résolution | Une seule fusion, **côté backend**, servie à `GET` comme à l'envoi — la règle n'est écrite qu'une fois |
| Libellé à l'envoi | **Résolu serveur**, jamais transmis par le client |
| Identité périmée | Conservée, signalée, retirée du menu From — **jamais supprimée en silence** (même loi que les surcharges de rôles de dossiers) |
| Tri | Défaut d'abord, puis alphabétique sur le libellé. **Pas de réordonnancement manuel** en 2c2a |
| Adresse principale | Toujours présente, non supprimable ; son libellé suit le `FullName` du compte tant qu'il n'est pas surchargé |

---

## 3. Modèle de données

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
```

`utf8mb4_bin` comme les deux tables voisines, et les adresses sont **canonisées en minuscules
avant écriture** — la comparaison octet à octet ne pardonne pas, la canonisation est donc la
seule défense contre `Mick@…` et `mick@…` vus comme deux identités. `account_id` réutilise
`FolderRoleStore.CanonicalAccountId`.

Pas de colonne `sort_order` : avec trois ou quatre identités, des flèches ↑↓ coûtent une
colonne, une UI et une DDL pour un gain mince. Si le besoin vient, c'est une colonne et un
champ dans le `PUT`.

**La création est manuelle**, comme `folder_role_overrides` et `user_preferences` : le projet
n'utilise pas les migrations EF. La tranche produit
`docs/superpowers/webmail-identities-table.md` sur le modèle des deux existants — script
idempotent, prod **et** dev, exécuté avant déploiement.

L'entité `SendingIdentity` rejoint `Data/Preferences/`, le `DbSet` et sa clé composite
rejoignent `PreferencesDbContext`.

---

## 4. Backend

### 4.1 `IdentityResolver` — la fusion, écrite une fois

Pure, sans I/O, testable seule. Entrées : les lignes stockées, l'adresse principale, le
`FullName`, la liste des alias vivants. Sortie : la liste des identités affichables.

- L'**adresse principale** est toujours produite, même sans ligne. Son libellé vient de la
  ligne si elle existe, sinon du `FullName`, sinon de l'adresse elle-même.
- Une ligne dont l'adresse n'est ni la principale ni un alias vivant sort avec `stale: true`.
- `isDefault` : au plus une identité. Si aucune ligne n'est marquée — ou si celle qui l'était
  est périmée — le défaut retombe sur l'adresse principale.
- Tri : défaut d'abord, puis libellé, comparaison ordinale insensible à la casse.

Deux appelants, une seule règle : `GET /api/Identities` et `MailSender` (§ 4.4).

### 4.2 `ISendingIdentityStore`

Calqué sur `IFolderRoleStore` : `GetAsync(accountId, ct)` et `ReplaceAsync(accountId,
identities, ct)`. `ReplaceAsync` remplace l'ensemble des lignes du compte en une transaction —
la sémantique du `PUT`.

### 4.3 `IdentitiesController` — `/api/Identities`

**`GET`** → `200`

```jsonc
{ "identities": [
  { "address": "mick@weesky.be",            "displayName": "Mick Dubois",    "isDefault": true,  "isPrimary": true,  "stale": false, "labelIsCustom": false },
  { "address": "michel.dubois@weesky.be",   "displayName": "Michel Dubois",  "isDefault": false, "isPrimary": false, "stale": false, "labelIsCustom": true  },
  { "address": "vieux@weesky.be",           "displayName": "Ancien",         "isDefault": false, "isPrimary": false, "stale": true,  "labelIsCustom": true  }
] }
```

`labelIsCustom` : `true` si le libellé vient d'une ligne stockée, `false` seulement pour la
principale synthétisée depuis `FullName` — c'est ce qui dit au client si la principale doit
figurer dans le corps du `PUT`.

**`PUT`** body `{ "identities": [ { address, displayName, isDefault } ] }` → `204`.
Remplace l'ensemble. Validation, chaque échec étant un `400` qui **nomme l'adresse fautive** :

1. Chaque adresse ∈ {principale} ∪ {alias vivants} ∪ **{adresses déjà stockées}**. La
   troisième branche est ce qui laisse une identité périmée survivre à un enregistrement sans
   qu'une adresse nouvelle et inconnue puisse entrer — sans elle, la loi « jamais supprimée en
   silence » serait contredite par le premier `PUT` venu.
2. Adresse syntaxiquement valide (`MailboxAddress.TryParse`), canonisée en minuscules, sans
   doublon après canonisation.
3. `displayName` : 1 à 100 caractères, **sans CR ni LF** — MimeKit encode les en-têtes, mais on
   ne stocke pas une valeur dont la seule utilité serait une tentative d'injection.
4. Au plus un `isDefault`.

`401` sans authentification. Aucun `502` possible : rien ne parle au serveur de mail.

### 4.4 `POST /api/Mail/Send` — le champ `fromAddress`

`SendMessageRequest` gagne `FromAddress` (optionnel ; absent ⇒ adresse principale, comportement
2c1 inchangé). Le contrôleur/`MailSender` :

1. **Revalide** l'adresse contre {principale} ∪ {alias vivants} — la liste d'alias, pas la table
   d'identités : elle seule dit ce que l'utilisateur possède vraiment. Adresse étrangère ⇒
   `400` qui la nomme. Une identité devenue périmée échoue donc ici, avec le bon message.
2. **Résout le libellé** via `IdentityResolver` — le client n'en transmet aucun. Une seule
   source de vérité, et pas de `From` fabriqué côté navigateur.
3. `message.From` reçoit `MailboxAddress(libellé, adresse)`. L'authentification SMTP reste celle
   de l'adresse principale (`_smtpFactory.OpenAsync(user.Email, …)`) : seul le `From` change.

**Prérequis serveur.** MailKit dérive le `MAIL FROM` de l'enveloppe du `From` du message :
envoyer depuis un alias suppose que Postfix l'autorise (`smtpd_sender_login_maps`). Sans cette
configuration, le serveur refuse la soumission. Le refus reste un `502` — le serveur de mail a
dit non — mais son message **nomme l'adresse** (« The mail server refused to send from
michel.dubois@weesky.be ») pour que l'utilisateur comprenne que c'est une règle du serveur et
pas une panne du webmail. La vérification de cette configuration figure dans le document de
prérequis produit par la tranche.

---

## 5. Frontend

### 5.1 Settings → Identities

Nouvelle entrée de navigation dans `SettingsLayout`, **entre *Aliases* et *Rules*** : on vient
de curer ses alias, on choisit ceux qui servent à écrire.

```
┌ Identities ─────────────────────────────────────────────┐
│  ★  Mick Dubois        mick@weesky.be        (primary) ✎ │
│  ☆  Michel Dubois      michel.dubois@weesky.be     ✎  🗑 │
│  ☆  Support weesky     support@weesky.be           ✎  🗑 │
│  ☆  Ancien             vieux@weesky.be   (unavailable) 🗑│
│                                                          │
│  [ + Add identity ]                                      │
└──────────────────────────────────────────────────────────┘
```

- L'étoile désigne le défaut ; un clic la déplace.
- ✎ édite le libellé en place. 🗑 retire l'identité (l'alias, lui, n'est pas touché — le texte
  du bouton et son aide le disent, sinon l'utilisateur croira supprimer son alias).
- L'adresse principale porte la mention `(primary)` et **n'a pas de 🗑**. Son ✎ surcharge le
  libellé ; vider le champ **retire sa ligne** du `PUT`, ce qui la ramène au `FullName` — le
  `PUT` ne transporte donc jamais un libellé vide, que la validation refuserait.
- La principale n'est incluse dans le `PUT` que si elle porte une surcharge de libellé. Elle
  n'a **pas besoin d'une ligne pour être le défaut** : aucune ligne marquée `isDefault` signifie
  précisément « le défaut est l'adresse principale » (§ 4.1). Désigner la principale comme
  défaut revient donc à démarquer celle qui l'était.
- Une identité périmée est grisée, marquée `(unavailable)`, et garde son 🗑 : c'est la seule
  action qui ait du sens sur elle.
- Chaque modification enregistre la liste entière (`PUT`), avec le toast d'erreur habituel en
  cas d'échec et un retour à l'état serveur — la page ne garde jamais un état que le serveur a
  refusé.

**`AddIdentityDialog`** réutilise la coquille modale existante (`.modal-overlay` / `.modal` /
`.modal-header`) : un champ de recherche filtrant la liste d'alias (`GET /api/aliases`, déjà
utilisée par la page Aliases), les adresses déjà prises masquées, le compte de résultats
affiché, et un champ *Display name* pré-rempli avec le `FullName`. **C'est ici que vivent les
cent alias** — jamais dans le menu From.

Fichiers : `modules/settings/identities/IdentitiesPage.tsx` et `AddIdentityDialog.tsx`,
`api.js` gagne `getIdentities` / `putIdentities`, `queries.ts` la query et la mutation.

### 5.2 `IdentitySelect` dans le composeur

`ComposeView` remplace sa ligne `From` figée par `compose/IdentitySelect.tsx` :

- **Une seule identité** ⇒ texte simple, exactement le rendu 2c1. Qui n'a rien curé ne voit
  aucun changement.
- **Plusieurs** ⇒ menu déroulant (`DropdownMenu`, pour rester dans le système visuel des autres
  menus), entrées `Libellé <adresse>`, l'identité par défaut présélectionnée.
- Les identités périmées ne sont pas proposées.
- Changer d'identité **salit** le brouillon, comme tout autre champ : la garde de sortie 2c1
  s'applique sans modification.

`sendMessage` transmet `fromAddress`.

---

## 6. Tests

**Backend (xUnit).** `IdentityResolver` porte le gros, et se teste sans base : principale
toujours produite, libellé `FullName` par défaut puis surcharge, alias disparu ⇒ `stale`,
défaut retombant sur la principale quand la ligne marquée est périmée, tri. Le store : remplacement
transactionnel, canonisation de la casse. Le contrôleur : `GET` fusionné, `PUT` valide → `204`,
et un `400` par règle de validation (adresse étrangère, doublon après canonisation, libellé
vide / trop long / avec CRLF, deux défauts), `401` sans authentification. `MailSender` : `From`
= identité choisie avec son libellé résolu, `fromAddress` absent ⇒ principale, adresse étrangère
⇒ `400` avant toute connexion SMTP.

**Frontend (Vitest + RTL).** `IdentitiesPage` : rendu de la liste, déplacement du défaut, édition
de libellé, suppression, identité périmée grisée et non proposée, échec du `PUT` ramenant l'état
serveur. `AddIdentityDialog` : filtrage, exclusion des adresses déjà prises, libellé pré-rempli.
`IdentitySelect` : texte simple à une identité, menu au-delà, défaut présélectionné, sélection
transmise à l'envoi et brouillon marqué sale.

---

## 7. Ce que 2c2a prépare sans l'implémenter

- **2c2b** — réponse / répondre à tous / transfert. Décisions déjà arrêtées lors de ce
  brainstorming, à reprendre telles quelles dans sa spec :
  - citation en **`<blockquote>` visible**, ligne d'attribution au-dessus, curseur au-dessus
    d'elle ; bloc `---------- Forwarded message ----------` pour le transfert ;
  - les pièces jointes d'un transfert sont **re-stagées côté serveur** (nouvel endpoint), sans
    transiter par le navigateur ;
  - architecture **hybride** : le backend transcrit (`messageId`, `references`, `replyTo` sur
    `MailMessageDetail`) et sert un corps citable assaini par la politique **sortante** ; le
    frontend décide (destinataires, préfixe d'objet, attribution) dans des fonctions pures ;
  - trois boutons icônes **Reply / Reply all / Forward** dans l'en-tête du lecteur ;
  - la présélection d'identité d'une réponse se branchera sur la liste livrée ici : première de
    mes adresses trouvée dans `To` puis `Cc`, sinon l'identité par défaut.
- **2c3** — une signature par identité se rattachera à cette table ; aucune colonne n'est
  ajoutée en avance.

---

## 8. Vérification

- Suites complètes vertes des deux côtés, `build` et `eslint` propres.
- Vérification manuelle après application de la DDL sur `dev` : ajout d'une identité, envoi
  depuis elle, contrôle du `From` reçu et de la copie Sent.
- Un compte sans aucune identité curée voit le composeur strictement identique à 2c1.
