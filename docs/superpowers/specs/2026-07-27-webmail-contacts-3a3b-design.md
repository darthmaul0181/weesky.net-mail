# Webmail — module Contacts (tranches 3a + 3b)

**Date :** 2026-07-27
**Statut :** conception validée, prête pour le plan d'implémentation

## Objet

Le module Contacts est aujourd'hui une page `ComingSoon`. Cette tranche lui donne son socle — deux
tables dans `snoopy_webmail`, un CRUD, et un module frontend à trois colonnes — puis branche les
contacts sur les champs To/Cc/Bcc du composeur.

Le carnet sert deux usages, et le second est la raison d'être du premier : consulter et tenir ses
contacts, et **ne plus retaper une adresse déjà connue** au moment d'écrire.

## Découpage du module

Le module se livre en quatre tranches indépendantes. Chacune dépend de 3a et de rien d'autre ;
l'ordre est le seul contraint.

| Tranche | Contenu | Statut |
|---|---|---|
| **3a** | Tables, CRUD API, module `/contacts` (bande + tuiles + fiche), éditeur pleine largeur | **cette spec** |
| **3b** | Autocomplétion des champs To/Cc/Bcc alimentée par les contacts | **cette spec** |
| **3c** | Capture automatique : à l'envoi vers une adresse inconnue, et à la réponse (récupération du nom complet de l'émetteur) | spec ultérieure |
| **3d** | Import CSV et vCard | spec ultérieure |

## Décisions

| Sujet | Décision |
|---|---|
| Champs d'un contact | prénom, nom, pseudo, plusieurs adresses, favori. **Pas de téléphone** (voir ci-dessous) |
| Clé de `contacts` | GUID `CHAR(36)` généré côté application, comme `users.id` |
| Rattachement | FK `user_id` vers `users(id)` en cascade — le GUID de la revendication `webmail_uid` |
| UID vCard | colonne `uid` dédiée, distincte de `id` ; renseignée depuis la source à l'import, repliée sur `id` pour une fiche née chez nous ; `UNIQUE (user_id, uid)` |
| vCard source | conservé tel quel en `MEDIUMTEXT` (`vcard_raw`), jamais lu ni servi par l'UI |
| Propriétés vCard non modélisées | **non modélisées, mais préservées** dans `vcard_raw` |
| Adresses multiples | table fille ordonnée par `position` ; **position 0 = principale**. Pas d'étiquette (`TYPE`) |
| Minimum pour exister | au moins un nom / prénom / pseudo, **ou** au moins une adresse |
| Adresse partagée | **autorisée** : une même adresse peut appartenir à plusieurs contacts du même utilisateur |
| Favoris | booléen local, étoile sur la tuile et portée dans la bande. Non synchronisable en CardDAV |
| Recherche et tri | **côté client**, sur la liste complète mise en cache. Aucune route de recherche serveur |
| Plafond | 5 000 contacts par utilisateur, 50 adresses par contact |
| Structure de page | trois colonnes — bande de portées, tuiles, fiche — la grammaire du module Mail |
| Surface d'édition | **échange plein cadre** sur une seconde route, le mécanisme de `/mail/compose` |
| Création de schéma | manuelle, sans migration EF, comme les quatre tables existantes |

### Décisions assumées et leurs conséquences

Trois décisions vont contre la recommandation initiale. Elles sont prises en connaissance de cause ;
leurs conséquences sont consignées ici pour que personne ne les redécouvre par surprise.

**1. Les propriétés vCard non modélisées ne sont pas modélisées.** Adresse postale, anniversaire,
société, fonction, photo, notes restent hors du modèle relationnel. L'UI ne les affiche pas et ne
permet pas de les éditer. Elles ne sont pour autant **pas perdues** : `vcard_raw` les conserve
intégralement. Conséquence : un contact importé puis réexporté ressort complet, mais un contact créé
ou modifié chez nous ne portera jamais ces propriétés.

**2. Pas de champ téléphone.** Il figurait dans la demande initiale ; il en a été retiré parce qu'un
contact réel porte plusieurs numéros (mobile, fixe, professionnel) et qu'une colonne scalaire aurait
imposé plus tard la seule migration réellement coûteuse du modèle — passage à une table fille,
reprise de données, tous les chemins de lecture à revoir. Conséquence : un `TEL` importé survit dans
`vcard_raw` sans être affiché, et les téléphones seront modélisés en table fille depuis une page
blanche le jour où ils arriveront.

**3. Une adresse peut appartenir à plusieurs contacts.** Cas réels visés : une boîte partagée
(`info@…`), une adresse de couple. Deux conséquences :

- **L'autocomplétion indexe ses lignes par adresse, pas par contact.** Une adresse portée par
  plusieurs contacts produit **une seule ligne**, libellée de tous les noms qui la portent :
  `alice@x.be — Alice Dupont, Compta Weesky`. Deux lignes produisant le même destinataire seraient du
  bruit, et choisir un seul nom serait un arbitrage arbitraire.
- **La capture automatique de 3c devra arbitrer.** Une adresse entrante déjà connue peut désigner
  plusieurs contacts ; laquelle enrichir n'est pas décidable ici. Question ouverte de 3c, pas trou de
  celle-ci.

Corollaire de schéma : `contact_emails` garde la clé `(contact_id, address)` et ne porte **pas** de
`user_id` ni d'index unique global. Seule l'unicité intra-contact est assurée, et c'est le seul
niveau qui compte.

## Schéma

À rejouer sur `snoopy_webmail` **et** `snoopy_webmail_dev`. Un document de prérequis accompagne la
tranche et porte le script, comme pour `trusted_senders`.

```sql
CREATE TABLE `contacts` (
  `id`          CHAR(36)     NOT NULL COMMENT 'GUID généré côté application',
  `user_id`     CHAR(36)     NOT NULL,
  `uid`         VARCHAR(255) NOT NULL COMMENT 'UID vCard d''origine ; = id quand la source n''en portait pas',
  `first_name`  VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `last_name`   VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `nickname`    VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `is_favorite` TINYINT(1)   NOT NULL DEFAULT 0,
  `vcard_raw`   MEDIUMTEXT   DEFAULT NULL COMMENT 'vCard source tel quel ; jamais servi à l''UI',
  `updated_at`  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_contacts_user_uid` (`user_id`, `uid`),
  KEY `ix_contacts_user` (`user_id`),
  CONSTRAINT `fk_contacts_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_emails` (
  `contact_id` CHAR(36)          NOT NULL,
  `address`    VARCHAR(320)      NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `position`   SMALLINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = adresse principale',
  PRIMARY KEY (`contact_id`, `address`),
  CONSTRAINT `fk_contact_emails_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

**La collation mixte est intentionnelle.** La table est en `utf8mb4_bin` comme ses quatre sœurs :
`uid` est une chaîne opaque sensible à la casse, et `address` est stockée sous forme canonique — une
collation insensible y fusionnerait deux valeurs que le code traite comme distinctes. Les trois
colonnes de nom portent au contraire `utf8mb4_unicode_ci` : c'est du texte humain, et un `LIKE`
binaire y serait inutilisable si une recherche serveur apparaissait un jour. Aujourd'hui tri et
filtre sont côté client, donc cette collation ne sert encore à rien — elle évite d'avoir tort plus
tard. `utf8mb4_unicode_ci` et non `utf8mb4_0900_ai_ci` : la base est MariaDB.

**`updated_at` est géré par le schéma**, contrairement aux dates de `users` qui sont posées
explicitement par le code. La raison est inverse dans les deux cas : `users.creation_date` ne doit
**jamais** bouger, donc le schéma ne doit pas y toucher ; `contacts.updated_at` doit suivre **toute**
écriture, puisqu'il est la base d'un futur ETag CardDAV — d'où `DEFAULT CURRENT_TIMESTAMP ON UPDATE
CURRENT_TIMESTAMP`.

**Adresses canonicalisées** via `IdentityResolver.Canonical` (`Trim().ToLowerInvariant()`), réutilisé
et non réimplémenté — c'est déjà la règle de `sending_identities` et `trusted_senders`.

## Backend

`ContactsController`, `[Authorize]`, **sans cookie de credentials** : c'est de la donnée webmail, pas
de la donnée serveur mail. Aucune session IMAP n'est ouverte, exactement comme `IdentitiesController`.

| Route | Verbe | Corps | Réponses |
|---|---|---|---|
| `/api/Contacts` | GET | — | 200 la liste entière, adresses incluses |
| `/api/Contacts` | POST | le contact | 200 le contact créé, 400 |
| `/api/Contacts/{id}` | PUT | le contact complet | 204, 400, 404 |
| `/api/Contacts/{id}` | DELETE | — | 204, 404 |
| `/api/Contacts/{id}/Favorite` | PUT | `{ isFavorite }` | 204, 404 |

**`PUT` remplace le contact entier** — noms, favori, adresses et leur ordre. Dernière écriture
gagnante, assumé : un carnet personnel n'a pas deux rédacteurs simultanés.

**`Favorite` a sa propre route** parce que l'étoile se bascule depuis la tuile sans ouvrir le
formulaire. Un `PUT` complet déclenché depuis une tuile écraserait le contact avec la copie que la
liste détient, potentiellement périmée ; une écriture ciblée ne peut pas. Même raisonnement que
`PUT /api/Mail/Messages/Flags` plutôt qu'une réécriture de message.

**`vcard_raw` n'est jamais servi au client.** Il est écrit par le chemin d'import (3d) et destiné à un
éventuel serveur CardDAV. L'exposer alourdirait la charge utile sans qu'aucun écran ne le lise.

**Portée.** Toute opération est filtrée sur le `user_id` lu dans la revendication `webmail_uid` via
l'accesseur existant. Un `id` appartenant à un autre utilisateur répond **404, jamais 403** :
l'espace de noms est scellé par compte, et un 403 confirmerait l'existence de la fiche — la règle
déjà appliquée aux pièces jointes staged.

**Validation.**

- au moins un de `firstName` / `lastName` / `nickname`, **ou** au moins une adresse — sinon 400 ;
- chaque adresse doit parser ; le 400 nomme l'adresse fautive ;
- une adresse répétée **au sein d'un même contact** est dédoublonnée en silence, pas refusée :
  l'intention de l'utilisateur est sans ambiguïté et la clé primaire l'interdirait de toute façon ;
- `position` est réattribuée en séquence à l'écriture, jamais reprise telle quelle du client — un
  trou ou un doublon de position venu du client ne doit pas pouvoir désigner deux principales.

**Plafond, et il est porteur.** Sur le modèle de `TrustedSenderStore.MaxPerAccount` : constante
interne, message d'erreur interpolé pour que le chiffre ne soit écrit qu'une fois, compté seulement
sur la branche qui ajoute. 5 000 contacts par utilisateur, 50 adresses par contact. Ce n'est pas une
limite produit — personne ne l'atteindra — c'est ce qui **borne la charge utile envoyée au
navigateur**, puisque la liste entière y part : quelques centaines de contacts font ~50 Ko, le
plafond borne le cas extrême à environ 1 Mo. Il protège d'un import qui s'emballe en 3d ou d'une
boucle fautive, pas d'un utilisateur.

**Store.** `IContactStore` / `ContactStore` sur `PreferencesDbContext`, sur le modèle de
`TrustedSenderStore` : `Result<T>`, `CancellationToken` partout, aucune exception pour signaler un
échec métier. `PreferencesDbContext` gagne les deux `DbSet` et la configuration de clés ; les
entités `Contact` et `ContactEmail` rejoignent `Data/Preferences/`.

**Pas de journal d'audit.** Une version antérieure de cette spec en exigeait un sur chaque mutation,
« comme les autres dépôts » — la prémisse était fausse. Seuls les dépôts adossés à `dovecot`
journalisent (`Audit: login`, `change_password`, `add_alias`…), parce qu'ils touchent à
l'authentification et aux droits ; **aucun des quatre stores de `snoopy_webmail`** —
`FolderRoleStore`, `UserPreferenceStore`, `SendingIdentityStore`, `TrustedSenderStore` — n'émet quoi
que ce soit. Un utilisateur qui édite son propre carnet n'est pas de cette classe, et `ContactStore`
suit ses frères plutôt que la convention d'un autre étage.

## Frontend

Module `src/modules/contacts/`. Le routeur remplace le `ComingSoon` de `/contacts` et ajoute deux
routes pointant sur le **même** layout, `lazy()`-importé.

```
/contacts              bande + tuiles + fiche
/contacts/new          bande + éditeur pleine largeur
/contacts/:id/edit     bande + éditeur pleine largeur
```

- **`ContactsLayout.tsx`** — trois colonnes en pile de bandes (`display:flex; flex-direction:column;
  overflow:hidden`, une seule bande en `flex:1; min-height:0; overflow-y:auto`). `useMatch` sur les
  deux routes d'édition échange tuiles et fiche contre l'éditeur pendant que le rail et la bande
  restent en place — le mécanisme exact de `MailLayout` avec `/mail/compose`, pas un layout de plus.
- **`ContactScopes.tsx`** — la bande, sur `--folders-bg` : Tous / Favoris avec compteurs. Langage
  « navigation » — remplissage et graisse, **pas de barre d'accent**. Pas d'entrée « Importer… » dans
  cette tranche : elle relève de 3d.
- **`ContactList.tsx`** — les tuiles sur **une seule peau, à deux lignes** (étoile + nom + actions,
  puis l'adresse dessous). La liste de messages en porte deux parce que trois arrangements de volet
  existent dans le mail ; ici la liste est toujours à côté de la fiche, donc une peau large serait du
  code inatteignable. Les deux lignes sont de toute façon la réponse à la largeur de la colonne —
  380 px par défaut, 240 px au plancher du séparateur, sous un plancher de page de 1024 px — où nom,
  adresse et actions ne tiennent pas sur une ligne. L'anatomie de tuile de `website-design.md` est
  respectée : étoile à l'extrême gauche, actions à l'extrême droite. Les contrôles révélés au survol
  occupent un espace réservé en permanence. `.search-input` dans la bande d'en-tête, compteur
  « correspondants / total ». La tuile sélectionnée porte le langage « liste de contenu » —
  remplissage **plus** barre d'accent en bord gauche. Une liste vide affiche une ligne centrée
  discrète, pas une zone blanche.
- **`ContactCard.tsx`** — la fiche en lecture : nom en tête, pseudo, les adresses dans l'ordre avec
  la principale signalée. Le groupe d'actions (modifier, supprimer, basculer favori) est en bas à
  droite, `align-self:flex-end`, comme le lecteur de mail.
- **`ContactEditView.tsx`** — le formulaire, modes création et édition, un seul composant. Lignes
  `.field-h` (la pleine largeur les rend lisibles, ce qui était l'argument décisif du choix de
  surface), prénom et nom sur une ligne, liste d'adresses répétable avec badge « principale » sur la
  position 0, retrait et réordonnancement. Un `htmlFor`/`id` explicite par champ : `.field-h` place
  le label **à côté** du contrôle, donc sans lui le champ n'a pas de nom accessible et
  `getByLabelText` ne l'atteint pas. Un `<form>` pour que Entrée valide, un seul `.btn-primary`.
- **`contactName.ts`** — `displayNameOf()` pur : « Prénom Nom », sinon le pseudo, sinon l'adresse
  principale. **Un seul endroit**, lu par la tuile, la fiche, l'éditeur et l'autocomplétion — sinon
  quatre écrans nomment le même contact de quatre façons.
- **`contactSearch.ts`** — `matches()` sur les quatre champs (prénom, nom, pseudo, adresses),
  insensible à la casse et aux accents, plus le classement favoris d'abord. **Partagé par le filtre
  de la page et par la liste déroulante du composeur** : c'est ce partage qui justifie le filtrage
  client, une règle de correspondance dupliquée finirait par diverger.
- **`queries.ts`** — `useContacts()` sur la clé `['contacts', accountId]` (scopée par compte dès le
  départ comme `['mail', …]`), `staleTime` de 5 min puisque la liste ne change que depuis ce module ;
  plus `useCreateContact` / `useUpdateContact` / `useDeleteContact` / `useSetContactFavorite`, invalidation
  **`onSettled` et non `onSuccess`**, pour qu'une écriture refusée resynchronise l'écran sur l'état
  serveur au lieu de laisser une liste optimiste mensongère.

**Tri.** `localeCompare` avec `sensitivity: 'base'`, comme `folderNodes.sortFolders` : un tri par
points de code exile tout nom accentué après « Z », et un tri sensible à la casse exile
« e-commerce » après tout nom capitalisé.

**Suppression.** `DeleteConfirmModal` partagé — **le seul modal du module**.

### Composeur (3b)

`ComposeView` appelle `useContacts()` **une fois** et passe les suggestions en prop aux trois
`RecipientsField`. Le champ reste présentationnel : il ne connaît pas la couche de données, ses
tests actuels gardent leur sens, et la règle « aucun test perdu sans remplaçant » est satisfaite
sans réécriture — `RecipientsField.test.tsx` existe déjà.

La liste déroulante suit la mécanique maison de la combobox :

- ancrée directement **sous** le cadre, plafonnée à une dizaine de lignes ;
- commit sur `onMouseDown` avec `preventDefault`, pour que le blur de l'input ne batte pas le clic ;
- Escape ferme, un clic extérieur ferme ;
- navigation clavier ↑ / ↓ / Entrée ;
- une valeur déjà retenue disparaît des options.

**Les jetons restent dans le cadre**, en ligne, comme aujourd'hui — et non en chips au-dessus.
`website-design.md` prescrit les chips au-dessus pour le sélecteur de propriétaires ; ici le champ
existe, il est testé, et les jetons en ligne sont la convention de tous les clients mail. Le contrat
de fond est tenu : valeur choisie visible, retirable, et retirée des options.

**Une ligne par adresse, pas par contact** — on choisit une adresse, pas une personne. Chaque ligne
se lit `Bruno Mertens — bruno@exemple.be`. L'adresse principale passe en tête pour un contact qui en
porte plusieurs, sans quoi une fiche à cinq adresses noierait le plafond de dix lignes. Une adresse
portée par plusieurs contacts donne **une ligne unique** libellée de tous leurs noms.

**La saisie libre reste intacte.** Taper une adresse complète et valider par Entrée, virgule ou
point-virgule commit comme aujourd'hui. La liste est un accélérateur, jamais un péage : le champ doit
rester pleinement utilisable avec zéro contact.

## Gestion d'erreur

- **401** — chemin existant : `api.js` efface la session et le prochain rendu redirige vers `/login`.
- **Validation** — `.alert.alert-error` en tête de formulaire, portant le message du backend.
- **Plafond atteint** — 400 portant le chiffre ; remonte en toast.
- **`id` inconnu ou appartenant à autrui** — 404 ; l'écran revient à `/contacts` avec un toast.
- **Succès et échec parlent tous les deux**, par toast : le silence se lit comme un plantage.
- **Écriture concurrente** — dernière écriture gagnante, assumé (voir `PUT`).

## Tests

**Backend** — store : CRUD complet ; isolation entre deux `user_id` distincts ; canonicalisation de
l'adresse à l'écriture ; `position` réattribuée en séquence ; plafond contacts et plafond adresses ;
cascade à la suppression du contact **et** à celle de l'utilisateur ; `uid` replié sur `id` à la
création manuelle. Contrôleur : chaque branche de validation, 204 / 400 / 404, et l'`id` d'un autre
utilisateur qui répond bien 404 et non 403.

**Frontend** — `contactSearch` : accents, casse, les quatre champs, classement favoris d'abord.
`contactName` : les trois repliements. `ContactList` : l'ordre de l'anatomie de tuile, la ligne
d'adresse toujours rendue, le compteur du filtre, la liste vide. `ContactCard` : les champs absents ne rendent rien. `ContactEditView` : création,
édition, ajout / retrait / réordonnancement d'adresse, la porte de validation (ni nom ni adresse →
soumission refusée). `RecipientsField` : filtrage live, commit `mouseDown`, Escape, saisie libre
préservée, valeur retenue retirée des options, et **une adresse portée par deux contacts rendue en
une seule ligne libellée des deux noms**.

## Prérequis avant tout serveur CardDAV

Ce qui suit n'est pas dans le périmètre, mais doit être levé **avant** qu'un vrai client CardDAV
écrive dans le carnet. Un client qui pousse une fiche et la retrouve appauvrie ne subit pas un
service réduit : il perd des données sur tous ses appareils.

| Prérequis | Pourquoi |
|---|---|
| Étiquette `TYPE` sur les adresses | `EMAIL;TYPE=work` figure sur presque toute ligne émise par un vrai client ; une colonne `label` nullable suffit |
| Précision des ETags | `updated_at` est à la seconde : deux modifications dans la même seconde sont indiscernables et un client synchronisé peut manquer un changement. `DATETIME(3)` ou un compteur de révision |
| Téléphones en table fille | modélisation depuis une page blanche ; les `TEL` importés attendent dans `vcard_raw` |
| Pierres tombales | tracer les suppressions pour un client hors ligne, sinon il repousse une fiche supprimée. Se rétrofite proprement : ne pas pouvoir signaler des suppressions antérieures au serveur est sans conséquence |
| Carnets multiples | aujourd'hui un carnet unique implicite par utilisateur ; un `address_book_id` s'ajoute plus tard |

`uid` et `vcard_raw` étaient les deux seuls points **irréversibles** — un UID d'origine jeté fait
tout dupliquer à la première synchronisation, et une propriété jamais stockée ne se retrouve nulle
part. Ils sont dans cette tranche pour cette raison, alors que rien ne les lit encore.

## Questions ouvertes pour les tranches suivantes

- **3c — arbitrage sur adresse partagée.** Une adresse entrante déjà connue peut désigner plusieurs
  contacts ; lequel enrichir du nom complet de l'émetteur ? Conséquence directe de la décision
  d'autoriser les doublons.
- **3c — capture silencieuse ou proposée ?** Créer un contact sans rien demander, ou suggérer.
- **3d — déduplication à l'import.** Sur `uid` en priorité ; que faire d'une fiche sans `UID`, et
  d'une adresse déjà présente sur un autre contact.

## Hors périmètre

- **Groupes / listes de diffusion** — aucune table, aucune UI.
- **Export** — l'import est en 3d, l'export n'est demandé nulle part.
- **Recherche serveur** — filtrage client par décision ; une route n'apparaîtra que si un carnet
  réel dépasse ce que le cache encaisse.
- **Photo de contact** — préservée dans `vcard_raw` à l'import, jamais affichée.
- **Serveur CardDAV** — hors périmètre entier ; seuls ses deux prérequis irréversibles sont posés.
