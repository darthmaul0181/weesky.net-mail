# Contacts 4c — le serveur CardDAV

Troisième tranche du projet CardDAV, à la suite de
[4a](2026-08-14-webmail-contacts-4a-vcard-model-design.md) et
[4b](2026-08-22-webmail-contacts-4b-editor-design.md).

## Où en est le projet

| | Tranche | État |
|---|---|---|
| 4a | Modèle de données complet + moteur d'aller-retour vCard | livrée |
| 4b | Éditeur et fiche webmail étendus | livrée |
| **4c** | **Serveur CardDAV (découverte, collection, rapports, verbes, ETags, pierres tombales)** | *ce document* |
| 4d | Conformité clients (`ccs-caldavtester`, Thunderbird, DAVx⁵, iPhone emprunté) | à venir |

4a a fait de `vcard_raw` la donnée souveraine et des colonnes une projection recalculée à chaque
écriture ; il a posé `contacts.card_hash`, le SHA-256 de la carte, en annonçant qu'il serait la base
de l'ETag. 4b a donné à l'utilisateur de quoi remplir ces champs. 4c ouvre le carnet au protocole.

## Ce que fait la tranche

Le carnet devient accessible à un client CardDAV : découverte du principal et du carnet, listing,
lecture, écriture, suppression, et synchronisation incrémentale. Comme aucun client tiers ne peut
traverser l'écran de login du webmail, la tranche livre d'abord de quoi s'authentifier — des mots
de passe d'application, engendrés et révoqués depuis les paramètres.

Ce que 4c **ne** fait **pas** : prouver que Thunderbird, DAVx⁵ ou iOS sont contents. Ici on écrit
au RFC ; 4d écrit aux clients.

## Découpage

Un seul spec, deux plans d'implémentation, dans cet ordre strict :

- **4c-i — les mots de passe d'application.** Table, engendrement, hachage, schéma
  d'authentification, API, écran de paramètres. Livrable et testable seul.
- **4c-ii — le serveur DAV.** Découverte, `PROPFIND`, les trois `REPORT`, les verbes, les ETags,
  la séquence et les pierres tombales.

Couper là n'est pas cosmétique : sans 4c-i, 4c-ii n'a aucune authentification, et un plan couvrant
les deux ferait trente tâches dont la moitié attend l'autre.

## Décisions

### 1. Le secret est engendré par nous, donc haché vite

Une table `app_passwords` porte des secrets de 20 caractères base32 — environ 100 bits — engendrés
par `RandomNumberGenerator`, affichés une seule fois, jamais restitués. Ils sont hachés en
**SHA-256 salé, et non par un KDF à itérations**.

C'est l'inverse de la règle habituelle, et la raison doit être écrite ici pour que personne ne
« corrige » le hachage plus tard : un KDF lent existe pour rendre coûteuse l'attaque par
dictionnaire d'un secret que l'humain a choisi et qui porte donc une vingtaine de bits d'entropie.
Ici l'entropie vient de nous. Une recherche exhaustive sur 100 bits reste hors de portée quelle que
soit la vitesse du hachage, tandis qu'un client DAV se ré-authentifie à **chaque requête** — un
PBKDF2 à 100 000 itérations y serait un déni de service que nous nous infligeons nous-mêmes, et
qu'un attaquant déclencherait à volonté avec des requêtes non authentifiées.

Le sel reste par secret : il empêche qu'une même chaîne engendrée deux fois se reconnaisse dans la
table, et il ne coûte rien.

La comparaison du condensat se fait en temps constant (`CryptographicOperations.FixedTimeEquals`).

`last_used_at` est mis à jour au plus une fois par heure et par secret : à chaque requête ce serait
une écriture par `PROPFIND`, pour une colonne dont la précision utile se mesure en jours.

### 2. La portée d'un secret est `/dav`, et rien d'autre

Un mot de passe d'application n'ouvre pas l'API du webmail. Il est porté par un schéma
d'authentification distinct — `CardDav`, Basic sur TLS — que seules les routes `/dav` acceptent.
Le JWT reste le schéma par défaut de `/api` et vaut **aussi** sur `/dav`, ce qui rend la surface
testable depuis une session webmail ordinaire, sans engendrer de secret.

Aucune colonne de portée dans la table : il n'y a qu'une portée, et une colonne qui ne prend qu'une
valeur ment sur son extensibilité.

Le schéma répond `401` avec `WWW-Authenticate: Basic realm="weesky CardDAV"` — sans cet en-tête, un
client n'a aucune raison de renvoyer des identifiants et boucle sur l'échec.

### 3. Un seul carnet, nommé `default`

Le modèle n'a pas de table de carnets et rien ne la réclame. L'ajouter par anticipation coûterait
une jointure sur chaque requête et une colonne sur `contacts`, pour une fonction que personne n'a
demandée. Le jour où plusieurs carnets seront voulus, l'URL les accueille déjà : c'est le segment
`default` qui deviendra variable.

### 4. Le principal est un GUID, et le chemin est vérifié

`/dav/principals/{userId}/` porte l'identifiant de `users`, pas l'adresse e-mail : une adresse dans
une URL doit être échappée, se retrouve dans les journaux du proxy, et n'apporte rien qu'un client
regarde.

Toute route `/dav` dont le segment `{userId}` diffère de l'utilisateur authentifié répond `404`, pas
`403` : un `403` confirmerait l'existence du principal visé.

### 5. Le nom de ressource est stocké, jamais dérivé

Un client choisit lui-même l'URL de son `PUT`, et rien dans le RFC ne l'oblige à la faire coïncider
avec l'`UID` de la carte. Une colonne `contacts.dav_name`, unique par utilisateur, porte donc ce que
le client a choisi ; une fiche née dans le webmail reçoit `{id}.vcf`.

Dériver le nom de l'`UID` casserait sur les UID contenant `/` ou `:` — que l'import accepte déjà,
puisque 4a garde l'UID de la carte source verbatim, préfixe `urn:uuid:` compris.

`dav_name` est validé à l'écriture : au plus 255 caractères, aucun `/`, aucun segment `.` ou `..`.

### 6. La séquence avance exactement quand `card_hash` change

C'est l'invariant qui tient toute la synchronisation, et il tient en une phrase parce qu'il répond
seul au cas piégeux : basculer l'étoile ne modifie pas la carte, donc ne réveille aucun client.
`is_favorite` n'est projeté de rien (décision 1 de 4a) — il ne doit pas non plus être visible du
protocole. Même chose pour `source` et pour un `last_used_at` de secret.

Réciproquement, toute écriture qui change la carte avance la séquence, quelle qu'en soit la porte :
édition webmail, import, fusion, rattrapage, `PUT` DAV.

Le compteur vit dans `contact_sync_state`, une ligne par utilisateur, incrémenté sous le verrou de
sa propre ligne (`UPDATE … SET sequence = sequence + 1` puis relecture dans la même transaction).
Un `MAX(sync_sequence) + 1` court après deux écritures simultanées et rendrait deux fiches à la
même séquence — un client qui synchronise entre les deux en perdrait une définitivement.

### 7. Le jeton et le ctag sortent du même compteur

`getctag` vaut la séquence courante. `sync-token` vaut `urn:snoopy:contacts:{séquence}`.

Un `sync-collection` portant le jeton *n* rend :

- toute fiche de `sync_sequence > n`, en réponse `200` avec `href` et `getetag` ;
- toute pierre tombale de `sync_sequence > n`, en réponse `404` ;
- le nouveau jeton, égal à la séquence courante.

Jeton absent : synchro initiale — tout le carnet, aucune tombe.

Le compteur étant par utilisateur, deux utilisateurs portent le même jeton pour des carnets
différents. C'est sans conséquence et volontaire : un jeton n'est jamais évalué que contre la
collection de l'utilisateur authentifié, et y mêler l'identifiant du principal ferait fuir celui-ci
dans les journaux de tous les clients sans rien rendre plus sûr.

### 8. Les pierres tombales, et le filigrane qui les rend sûres

`contact_tombstones` retient `(user_id, dav_name, sync_sequence, deleted_at)`. Un balayage les
élague à 180 jours — un troisième `PeriodicSweeper`, la mécanique existe.

L'élagage remonte `contact_sync_state.pruned_below` d'autant, et un jeton `≤ pruned_below` répond
`403 valid-sync-token`, sur quoi le client repart d'une synchro complète. Sans ce filigrane, un
jeton périmé serait accepté et la réponse omettrait une suppression **sans que rien ne le signale** :
le client garderait la fiche pour toujours. C'est le seul mode de défaillance silencieux de tout le
protocole, et c'est pour lui que la colonne existe.

### 9. L'ETag est le `card_hash`, et un `PUT` qui transforme n'en renvoie pas

L'ETag vaut `"{card_hash}"`, fort, et il est honnête : les octets servis sont exactement
`vcard_raw`, dont `card_hash` est le SHA-256.

Un point se trompe facilement. 4a insère un `UID` dans une carte qui n'en déclare pas — l'invariant
vaut pour toute carte stockée. Quand cela se produit sur un `PUT`, ce qui est stocké diffère de ce
qui a été envoyé, et le RFC exige alors de **ne pas** renvoyer d'ETag dans la réponse, pour que le
client relise. Renvoyer l'ETag des octets stockés serait pire que de n'en renvoyer aucun : le client
croirait détenir la carte qu'il a envoyée, et ne la relirait jamais.

`If-Match` en désaccord répond `412`. `If-None-Match: *` sur une ressource existante répond `412`
également.

### 10. Le `PUT` est la troisième porte, pas une quatrième

Le diagramme de 4a nomme déjà « carte importée / PUT CardDAV (4c) » comme la porte qui pose la carte
verbatim. 4c s'y branche : carte reçue → `VCardProjector` → `ReplaceProjectionAsync`. Aucun nouveau
chemin d'écriture, aucune règle métier dupliquée.

Ce qui survit à une mise à jour par `PUT` : `id`, `user_id`, `is_favorite`, `source`. Ce qui est
recalculé : tout le reste, puisque tout le reste est une projection.

Un `UID` déjà porté par une **autre** ressource du carnet répond `403 no-uid-conflict` : l'index
unique `(user_id, uid)` posé par 4a est exactement ce garde-fou, il suffit de traduire sa violation
plutôt que de la laisser remonter en 500.

Un `PUT` sur un nom que porte une pierre tombale la lève.

### 11. `addressbook-query` : le filtre est évalué, ou refusé — jamais ignoré

Le rapport est évalué sur les colonnes projetées, pour les propriétés que le modèle porte. Un filtre
que le serveur ne sait pas évaluer répond `403 supported-filter`.

C'est la décision qui compte dans ce rapport : répondre « tout le carnet » à un filtre incompris a
l'apparence du succès et donne au client un jeu de résultats faux, qu'il inscrira dans son cache.
Un refus explicite le fait basculer sur un listing complet, qu'il sait faire.

### 12. XML écrit et lu à la main, sans DTD

`XmlWriter` pour les réponses, `XDocument` pour les requêtes, lecteur configuré
`DtdProcessing = Prohibit` et `XmlResolver = null`. Un corps de `REPORT` est une entrée non fiable,
et l'expansion d'entités y est la faille classique — un fichier local lu et renvoyé dans une réponse
`multistatus`.

Aucune bibliothèque WebDAV .NET libre n'est maintenue ; la seule sérieuse est commerciale. Le volume
à écrire reste modeste parce que la surface est fixe : cinq documents de réponse, trois de requête.

## La surface HTTP

```
GET      /.well-known/carddav                    301 → /dav/
OPTIONS  /dav/…                                  DAV: 1, 3, addressbook · Allow
PROPFIND /dav/                                   current-user-principal
PROPFIND /dav/principals/{userId}/               addressbook-home-set, principal-URL
PROPFIND /dav/addressbooks/{userId}/             depth 1 → la collection « default »
PROPFIND /dav/addressbooks/{userId}/default/     depth 0 → getctag, sync-token, supported-report-set
                                                 depth 1 → une ressource par fiche, avec getetag
REPORT   …/default/                              addressbook-multiget · addressbook-query · sync-collection
GET      …/default/{nom}.vcf                     la carte verbatim, ETag, Content-Type text/vcard
PUT      …/default/{nom}.vcf                     If-Match / If-None-Match
DELETE   …/default/{nom}.vcf                     pose une pierre tombale
```

L'utilisateur saisit `https://api.mail.weesky.net` dans son client ; le service sert lui-même
`/.well-known/carddav`, sans rien à configurer sur le serveur web. L'adresse n'étant pas devinable,
l'écran des mots de passe d'application l'affiche, prête à copier.

ASP.NET Core route les méthodes non standard par `[AcceptVerbs("PROPFIND")]` ; Kestrel les accepte
sans configuration.

## Le schéma

Quatre changements, en SQL manuel — le projet n'a pas de migrations EF (`PreferencesDbContext`) — et
consignés dans `docs/superpowers/webmail-carddav-tables.md`, sur le modèle de
`webmail-contacts-tables.md`.

```
app_passwords       (id, user_id, label, secret_hash, salt, created_at, last_used_at)
                    FK user_id → users(id) ON DELETE CASCADE
contact_sync_state  (user_id, sequence, pruned_below)
                    FK user_id → users(id) ON DELETE CASCADE
contact_tombstones  (user_id, dav_name, sync_sequence, deleted_at)
                    PK (user_id, dav_name) · INDEX (user_id, sync_sequence)
                    FK user_id → users(id) ON DELETE CASCADE
contacts          + dav_name VARCHAR(255)   UNIQUE (user_id, dav_name)
                  + sync_sequence BIGINT    INDEX  (user_id, sync_sequence)
```

Les trois nouvelles entités déclarent leur arête vers `WebmailUser` dans `OnModelCreating`, sans
propriété de navigation, comme les cinq tables existantes : sans arête déclarée, EF ordonne les
`INSERT` par nom de table et casse la clé étrangère.

Un rattrapage remplit `dav_name` et `sync_sequence` sur les fiches existantes, et crée une ligne
`contact_sync_state` par utilisateur. Il est livré en SQL à passer à la main, comme
`contacts-display-name-backfill.sql`.

## Les erreurs

| Situation | Réponse |
|---|---|
| Pas d'identifiants, ou secret inconnu | `401` + `WWW-Authenticate: Basic realm="weesky CardDAV"` |
| `{userId}` n'est pas l'utilisateur authentifié | `404` |
| `Depth: infinity` sur `PROPFIND` | `403 propfind-finite-depth` |
| `UID` déjà porté par une autre ressource | `403 no-uid-conflict` |
| Carte au-delà de 1 Mo | `403 max-resource-size` |
| Filtre `addressbook-query` non évaluable | `403 supported-filter` |
| Jeton de synchronisation périmé ou inconnu | `403 valid-sync-token` |
| `If-Match` en désaccord, `If-None-Match: *` sur ressource existante | `412` |
| Corps vCard illisible | `400` |
| Ressource inconnue | `404` |

Chaque `403` porte le corps `DAV:error` nommant sa condition — c'est ce que le client lit pour
choisir son repli, un `403` nu ne lui laissant que l'abandon.

## Ce que 4c ferme des résidus de 4a

`docs/superpowers/contacts-4a-residuals.md` § « À traiter en 4c » énumère cinq points. Quatre entrent
dans cette tranche :

- **`UID:urn:uuid:X` ressort en `UID;VALUE=TEXT:…` en 4.0** — cosmétique jusqu'ici, servi à de vrais
  clients désormais.
- **`VCardProjector.RawCard` ne s'arrête pas au premier `END:VCARD`** — le résidu annonçait
  exactement ce moment : le `PUT` devient un second producteur de `vcard_raw`, et l'entrée cesse
  d'être garantie par le découpeur.
- **`If-None-Match: *`, ETags faibles et valeurs multiples non honorés** sur `GetPhoto` — 4c porte la
  vraie sémantique d'ETag, la route d'avatar s'aligne dessus.
- **Le test du corpus compose avec l'`Uid` du projecteur** plutôt que celui de la production.

Le cinquième — le repli du nom d'affichage à l'export ignore l'ordre `PREF` — reste au backlog : il
concerne l'export CSV, que le protocole ne traverse pas.

## Fichiers

**4c-i, backend**

- `Data/Preferences/AppPassword.cs`, et son arête dans `PreferencesDbContext`
- `Repositories/IAppPasswordStore.cs`, `AppPasswordStore.cs`
- `Services/AppPasswordSecret.cs` — engendrement base32, sel, condensat, comparaison en temps constant
- `Authentication/CardDav/CardDavAuthenticationHandler.cs`, ses options et sa constante de schéma
- `Controllers/AppPasswordsController.cs`

**4c-i, frontend**

- `modules/settings/appPasswords/` — liste, création (le secret montré une fois), révocation, et
  l'adresse du serveur prête à copier
- la route, l'entrée de menu et les libellés (l'UI reste en anglais)

**4c-ii**

- `Data/Preferences/ContactTombstone.cs`, `ContactSyncState.cs`, et leurs arêtes
- `Repositories/IContactSyncStore.cs` et son implémentation ; `ContactStore` avance la séquence
- `Services/CardDav/DavPaths.cs` — construction et analyse des chemins, validation de `dav_name`
- `Services/CardDav/DavXml.cs` — noms d'éléments et espaces de noms
- `Services/CardDav/MultiStatusWriter.cs`
- `Services/CardDav/PropfindRequest.cs`, `ReportRequest.cs` — analyse, sans DTD
- `Services/CardDav/AddressBookFilter.cs` — évaluation du filtre, ou refus
- `Controllers/CardDavController.cs`, `WellKnownController.cs`
- `Services/ContactTombstoneSweeper.cs`

## Tests

- **Le secret** : engendrement distinct à chaque appel, condensat non réversible, comparaison en
  temps constant, `last_used_at` amorti.
- **Le schéma d'authentification** : `401` avec l'en-tête de défi, secret révoqué refusé, secret
  d'un autre utilisateur refusé, JWT accepté, secret refusé sur `/api`.
- **Les documents XML** : assertions sur les corps de réponse, adossées aux exemples **littéraux**
  des RFC 6352 et 6578 plutôt qu'à des corps inventés — un corps inventé prouve que le code fait ce
  que le code fait.
- **L'invariant de séquence** : une édition qui change la carte l'avance ; un basculement d'étoile
  ne l'avance pas.
- **La synchro** : initiale sans jeton ; incrémentale rendant créations, modifications et tombes ;
  jeton périmé répondant `403 valid-sync-token`.
- **Le `PUT`** : carte posée verbatim, `is_favorite` et `source` préservés, conflit d'`UID`,
  `If-Match`, absence d'ETag quand la carte a été transformée, tombe levée.
- **Le filtre** : évalué sur ce qu'on modélise, refusé sur le reste — jamais silencieusement ignoré.
- **XXE** : un corps de `REPORT` déclarant une entité externe est refusé.

## Prérequis d'infrastructure

Deux notes, sur le modèle de `reverse-proxy-prerequisite.md` :

1. **Le DDL**, à passer à la main sur dev puis prod, rattrapage compris.
2. **Le proxy inverse** : vérifier qu'il laisse passer `PROPFIND` et `REPORT`, et qu'il ne retire ni
   `Depth` ni `If-Match`. Un `limit_except` ou un pare-feu applicatif les refuse silencieusement, et
   le symptôme côté client est un carnet vide, sans erreur — c'est-à-dire le symptôme qui coûte le
   plus cher à diagnostiquer.

## Ce que la tranche ne fait pas

- **Aucune conformité client prouvée.** C'est 4d, et l'ordre est délibéré : un défaut trouvé par
  `ccs-caldavtester` sur un serveur qui suit le RFC est un défaut du serveur ; trouvé sur un serveur
  écrit contre un client, il est indiscernable d'une divergence de ce client.
- **Pas de CalDAV.** Le calendrier n'existe pas dans le produit.
- **Pas de plusieurs carnets, pas de partage, pas de `MKCOL`.** Le carnet est créé avec
  l'utilisateur.
- **Pas de `PROPPATCH`.** Rien de mutable ne se présente : le nom du carnet est fixe.
- **Pas de découverte par SRV DNS ni depuis `mail.weesky.net`.** L'adresse à saisir est celle de
  l'API ; les deux autres chemins demandent de la configuration hors dépôt et pourront s'ajouter en
  4d si un client les réclame.
