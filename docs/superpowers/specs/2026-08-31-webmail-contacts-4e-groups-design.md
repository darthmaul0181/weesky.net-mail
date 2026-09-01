# Contacts 4e — les groupes

Cinquième tranche du projet CardDAV, à la suite de
[4d](2026-08-31-webmail-contacts-4d-conformance-design.md). Elle ouvre le seul objet du carnet que
le protocole connaît et que le produit ignore, et elle est nommée comme reportée par deux specs
antérieures : 4a la met hors périmètre (« Les groupes de contacts (`KIND:group`, `CATEGORIES`) […]
méritent leur tranche »), 4c la décrit et laisse la décision « devant un client réel ».

## Où en est le projet

| | Tranche | État |
|---|---|---|
| 4a | Modèle de données complet + moteur d'aller-retour vCard | livrée |
| 4b | Éditeur et fiche webmail étendus | livrée |
| 4c | Serveur CardDAV | livrée |
| 4d | Conformité clients | livrée |
| **4e** | Groupes de contacts *(ce document)* | à faire |

## Ce que fait la tranche

Un groupe se crée, se renomme, se supprime. Des contacts s'y glissent depuis la liste, s'en
retirent depuis la bande de sélection ou depuis les puces de leur fiche. Un groupe se synchronise
comme une carte, parce qu'il en est une. Et il sert de liste de diffusion : son nom se complète dans
le composeur et développe ses membres, et la bande offre « Écrire au groupe ».

## Ce que la tranche répare

**Un utilisateur qui a des groupes sur son téléphone les voit aujourd'hui dans le webmail comme des
fiches quasi vides portant le nom du groupe.** C'est écrit dans 4c, section « Pas de traitement des
groupes de contacts », pour que 4d ne prenne pas ces fiches-là pour un défaut. Le stockage verbatim
les traverse déjà sans perte : rien n'est à réparer côté protocole, tout est à faire côté modèle et
côté écran.

Une requête jouée sur `snoopy_webmail_dev` le 2026-08-31 ne trouve aucune carte portant
`X-ADDRESSBOOKSERVER-KIND`, `KIND:group` ni `CATEGORIES:` : aucun groupe n'a encore traversé le fil,
dans aucun des deux encodages. Le choix d'encodage n'est donc contraint par aucun stock existant.

## Décisions

### 1. Un groupe est une ligne de `contacts`, pas une table à part

Colonne `kind` sur `contacts`, `ENUM('individual','group')`, défaut `individual`.

Dans CardDAV un groupe **est une carte de la collection** : il a un nom de ressource, un ETag, un
rang de synchronisation, une tombe quand il disparaît, une révision quand il est remplacé. Toute
cette plomberie existe en 4c-ii et ne connaît qu'une table. En faire une ligne de `contacts`, c'est
en hériter sans écrire une ligne.

L'alternative — une table `contact_groups` portant ses propres `dav_name`, `sync_sequence`,
`card_hash` — laisse toutes les requêtes existantes intactes, mais fait de la collection DAV une
union de deux tables : `PROPFIND`, `addressbook-multiget`, `sync-collection`, les filtres du REPORT,
les tombes, l'unicité de `dav_name` **et** d'UID à travers les deux. C'est rouvrir toute la couche
que 107 tests CalDAVTester et deux clients réels valident aujourd'hui, pour éviter un filtre.

**Le `MODIFY COLUMN source` de 4c-ii a laissé une leçon qui s'applique mot pour mot** : un `ENUM`
que MariaDB refuse en mode strict passe le fournisseur InMemory des tests sans broncher, et rend
`503` au premier appel réel. Le DDL est à jouer sur les deux bases **avant** tout déploiement, et un
test épingle la valeur `group`.

### 2. Les membres dans une table, indexés sur l'UID du membre

`contact_group_members` est à `X-ADDRESSBOOKSERVER-MEMBER` ce que `contact_emails` est à `EMAIL` :
la projection interrogeable d'une propriété, la carte restant l'archive. Le principe posé en 3a et
élargi en 4a s'applique sans exception nouvelle.

Elle se remplit par le même chemin que ses sœurs : `ReplaceProjectionAsync`, la re-projection
totale qu'un `PUT` DAV comme une édition webmail traversent. Les membres y sont remplacés comme les
`EMAIL` et les `TEL` le sont, et `ContactProjection` gagne les deux champs — `Kind` et les membres —
que le projecteur lit et que ce chemin recopie. C'est là qu'est le gros du travail dans
`ContactStore`, pas dans le filtre de la décision 4.

**Le cycle a deux moitiés, et la seconde énumère ses tables à la main.** `LoadProjectionAsync`,
`ProjectionCache.Clear` et `ClearProjectionAsync` nomment les quatre tables filles une par une.
Sans la cinquième, le second `PUT` de la même carte de groupe frappe `UNIQUE (group_id,
member_uid)` et rend `500` — pas une fois, à chaque synchronisation suivante du téléphone. Les
commentaires qui comptent les tables — le « four queries » de `LoadProjectionAsync`, le « four
families » de `ClearProjectionAsync` — se corrigent avec le code.

**Un `MEMBER` plus long que la colonne est écarté de la projection ; la carte le garde.** Le
garde-fou d'aujourd'hui ne mesure que l'UID propre d'une carte : sans cette règle, une valeur de
trois cents caractères atteint MariaDB en mode strict et revient en `500`. Le régime général du
projecteur — la troncature — ne convient pas ici : un UID tronqué désignerait le mauvais contact.
C'est le traitement de l'exception nommée, celle de l'adresse e-mail, « dropped whole, never
truncated », et pour la même raison.

**La colonne porte `member_uid`, pas `contact_id`, et n'a pas de clé étrangère vers `contacts`.**
Un client a le droit de `PUT` la carte d'un groupe avant celles de ses membres — c'est même l'ordre
naturel d'une première synchronisation qui pousse ce qu'elle a sous la main. Une FK ferait échouer
ce `PUT` : un `500` au premier contact avec un vrai téléphone. La référence pendante est un état
légal du protocole.

**Elle se résout par `(user_id, uid)`, jamais par `uid` seul.** `uid` n'est unique que sous
`uq_contacts_user_uid`, et la table des membres ne porte pas de `user_id` à elle : la frontière
entre deux carnets vit entièrement dans la requête de jointure. Un `MEMBER` portant l'UID d'un
contact d'autrui résoudrait dessus et le ferait sortir par `memberIds`, puis par l'expansion du
composeur. La jointure porte le `user_id` du carnet, sans exception.

**Et elle essaie l'UID sous ses deux formes.** RFC 6350 recommande `UID:urn:uuid:…`, et
`contacts.uid` est verbatim : un membre dont la carte porte cet UID-là, référencé
`MEMBER:urn:uuid:X`, donne un `member_uid` strippé `X` qu'une égalité simple ne joindrait jamais.
La résolution matche donc `uid` contre `member_uid` **et** contre sa forme préfixée
`urn:uuid:` + `member_uid` — deux formes, pas plus : le strip de la décision 5 garantit qu'un
`member_uid` ne porte plus le préfixe.

### 3. Un membre en double ne crée pas deux lignes

`UNIQUE (group_id, member_uid)`. C'est la seule entorse apparente à la règle « rendre la carte
fidèlement » qui vaut pour deux `TEL` identiques — et elle n'en est pas une. L'appartenance est un
ensemble par nature : afficher deux fois la même personne dans un groupe est un défaut visible, pas
une fidélité. Et rien ne se perd : `vcard_raw` garde les deux `MEMBER` intacts, donc l'aller-retour
CardDAV rend la carte d'origine telle quelle. La projection est un ensemble, l'archive reste fidèle.

Conséquence assumée : les `position` gardent des trous. C'est déjà le cas des autres tables filles,
où `position` est le rang dans la carte et non un compteur dense.

### 4. Le filtre `kind` est une clause partagée ; le DAV ne filtre rien

`ContactStore` touche `context.Contacts` vingt-cinq fois. Un `Where(c => c.Kind == …)` recopié
vingt-cinq fois est un `Where` qu'on oubliera au vingt-sixième appel, et l'oublier **c'est
reproduire exactement le défaut que la tranche répare** : un groupe affiché comme un contact.

Le pattern existe déjà à côté : `ContactVisibility.Visible()`, une extension `IQueryable` écrite une
fois pour la clause du protocole. La sienne s'appelle `Individuals()` / `GroupCards()` et vit au
même endroit, avec le même commentaire d'intention. `GroupCards()` et non `Groups()` : « groupe »
désigne déjà le groupe de propriété dans ce code — `group_name`, `ProjectedLine.GroupName`,
`item1.EMAIL` —, et un `Groups()` posé sur `IQueryable<Contact>` se lirait de travers.

**Le côté DAV ne filtre rien** — `DavContactReader`, `DavContactWriter`, les rapports : la
collection sert les deux espèces, et c'est ce qui la rend conforme. Un filtre qui remonterait
jusque-là ferait disparaître les groupes de la synchronisation.

Surfaces à auditer, sans exception : la liste, l'export CSV, la suppression en lot, le favori en
lot, l'autocomplétion du composeur, les compteurs de la bande, le plafond du carnet (décision 18),
et le dédoublonnage à l'import. Celui-ci mérite d'être nommé : son index par nom ne contient que
les contacts **sans adresse**, et un groupe est sans adresse et porte un nom — une ligne CSV nommée
« Amis » fusionnerait dans la carte du groupe si l'index ne l'en écartait pas.

Les routes à id de `ContactsController` sont des surfaces au même titre : `PUT /api/Contacts/{id}`
appelé avec l'id d'un groupe — client bogué, onglet périmé — réécrirait sa carte en fiche, `N` et
placeholder posés, `KIND` perdu. Les cinq (`GET`, `PUT`, `DELETE`, `Favorite`, `Photo`) rendent
`404` sur un groupe : la clause `Individuals()` posée sur leur lecture le fait sans mécanisme de
plus, et un groupe se lit et s'écrit par ses propres routes.

### 5. Lecture des deux dialectes, création dans un seul

`VCardProjector` reconnaît `KIND:group` (vCard 4.0, RFC 6350 § 6.1.4) **et**
`X-ADDRESSBOOKSERVER-KIND:group` (3.0). Les membres de même : `MEMBER` et
`X-ADDRESSBOOKSERVER-MEMBER`.

**La valeur se lit large et s'écrit étroit.** RFC 6350 § 6.6.5 admet n'importe quel URI —
`mailto:`, `tel:`, une adresse http — et des clients écrivent l'UID nu. À la lecture, un préfixe
`urn:uuid:` insensible à la casse est retiré et le reste devient `member_uid` ; ce qui ne désigne
personne pend (décision 9) plutôt que d'être refusé. À l'écriture, `urn:uuid:` sans condition — la
convention d'Apple —, y compris quand l'UID n'est pas un UUID, ce qui est le cas de toute carte
venue d'ailleurs.

Une carte de groupe **née ici** ne porte que le second, puisqu'une carte née ici est en 3.0 —
`SourceCard.Fresh()` le fixe, et la décision 4 de 4a interdit de réécrire une carte dans une autre
version. C'est le dialecte qu'Apple sait lire, et celui que DAVx⁵ lit dès que son compte est réglé
sur *separate vCards*. Cela ne vaut que pour la création : une carte **reçue** en 4.0 se modifie
en 4.0, ce que la décision 6 énonce ligne par ligne.

**Le convertisseur de version demande une exception assumée, pas une vérification.**
`VCardVersionConverter` sert la 4.0 à un client qui la demande, et sa règle affichée est que les
transpositions sont celles de la bibliothèque, jamais une réécriture textuelle de nous. Or
la bibliothèque ne traduit jamais une propriété `X-` : elle la recopie. Traduire
`X-ADDRESSBOOKSERVER-KIND` → `KIND` et `X-ADDRESSBOOKSERVER-MEMBER` → `MEMBER` est donc une
réécriture de nous, la seconde de cette classe après `RestoreUid` et pour la même raison : sans
elle, un client strictement 4.0 reçoit un groupe qu'il lit comme une fiche vide — le défaut
d'aujourd'hui, déplacé d'un cran. Le commentaire de tête de la classe est à corriger avec le code,
sans quoi il énonce une règle que deux méthodes enfreignent.

**Et la traduction va dans les deux sens, mais ce ne sont pas deux fois le même geste.** De la 3.0
vers la 4.0, la bibliothèque recopie les lignes `X-` : elles sont là, sous les yeux, et les
renommer suffit. De la 4.0 vers la 3.0, elle a **déjà supprimé** `KIND` et `MEMBER` avant qu'on
regarde — le commentaire de `DropEmbeddedCards` le rappelle pour `KIND`. Il n'y a alors rien à
renommer : les deux lignes se rebâtissent depuis la carte **stockée**, comme `RestoreUid` va y
relire son UID. Sans quoi un groupe poussé en 4.0 arrive sur un iPhone comme une fiche : le même
défaut, en miroir. Chaque sens porte son test, et celui du retour rougit si on l'a écrit comme un
renommage.

### 6. Le composeur gagne trois capacités, chirurgicales toutes les trois

La décision 4 de 4a énonce que `VCardComposer` **remplace des valeurs dans la carte stockée et ne
réécrit jamais une propriété**. Ajouter ou retirer un membre n'est ni l'un ni l'autre : c'est une
insertion et une suppression de ligne. Elle reste chirurgicale — elle touche les lignes de membres
et rien d'autre de la carte, groupes de propriétés, paramètres et propriétés non modélisées
compris.

**La ligne insérée porte le dialecte de la carte, pas le nôtre.** Le stockage est verbatim : une
carte poussée en 4.0 est là en 4.0, `KIND` et `MEMBER` compris. Y glisser un
`X-ADDRESSBOOKSERVER-MEMBER` à côté de son `KIND:group` rend une carte mixte qu'un lecteur 4.0
strict lit comme un groupe **sans membre** — le défaut de la tranche, reconstitué par le geste
censé le réparer. On écrit `MEMBER` dans une carte qui dit `KIND`, et `X-ADDRESSBOOKSERVER-MEMBER`
dans une carte qui dit `X-ADDRESSBOOKSERVER-KIND`.

**Et l'ajout comme le retrait ne traversent jamais le sérialiseur : ils éditent les lignes de
`vcard_raw`.** C'est déjà ce que la décision 20 en dit, et ce n'est pas un raccourci. Passer par le
modèle ne marche dans aucune version, pour deux raisons qui ne sont pas la même. Sur une carte 3.0,
`VCard.Members` est une propriété 4.0-only de la bibliothèque : l'écrivain 3.0 ne l'émet jamais —
le membre est perdu avant même le splice —, et un `X-ADDRESSBOOKSERVER-MEMBER` posé en NonStandard
serait, lui, réverté par `SpliceUnmodelledFamilies` dès que la carte en portait déjà, puisqu'il
réinjecte verbatim, depuis la carte d'entrée, toute famille absente d'`OwnedNames` — son
commentaire nomme les familles `X-` explicitement. La route rendrait `204` sans avoir rien fait.
Sur une carte 4.0, le modèle émettrait bien le `MEMBER` — au prix d'une re-sérialisation de la
carte entière, précisément ce que la décision 4 de 4a interdit. Inscrire les deux noms dans
`OwnedNames` ne lèverait que le piège du splice, et retirerait au composeur la fidélité verbatim
que cette liste protège pour tout le reste ; l'édition de lignes, elle, ne touche que ce qu'elle
vise.

La deuxième capacité est de **composer une carte de groupe neuve**. `ComposeNew(uid, write)` pose
aujourd'hui le `N` et le `FN` d'une personne, avec le `?` que `StripNamePlaceholders` retire
ensuite ; une carte de groupe porte `X-ADDRESSBOOKSERVER-KIND:group`, un `FN` qui est le nom du
groupe, et un `N` vide (décision 17). C'est un chemin d'écriture à part et non un paramètre de plus
sur celui des personnes : `ContactWrite` décrit une fiche, et lui faire porter un groupe est
précisément le mélange que la colonne `kind` existe pour empêcher.

La troisième est le **renommage**, et c'est le même argument une seconde fois. Remplacer un `FN`
est bien ce que le composeur sait faire, mais la seule porte qui le fasse est
`Compose(existingCard, uid, ContactWrite)` — laquelle appelle aussi `SetName`,
`ReplaceFirstNickname`, `PoseOptional` et `Paired(card.EMails, …)`. Avec un `ContactWrite` vide,
elle poserait un `N` et **effacerait tous les `EMAIL` de la carte**. Le renommage prend donc son
propre chemin, qui remplace la valeur du `FN` et rien d'autre — une édition de ligne, comme les
deux autres.

**La carte neuve est la seule des trois qui passe par le sérialiseur**, et elle le peut : une carte
qui n'existe pas encore n'a rien à préserver. Les deux autres écrivent dans une carte qu'un
téléphone a composée, et c'est cette carte-là que la décision 4 de 4a protège.

**Le prix d'écrire une ligne à la main est de l'échapper à la main.** `FN` est une valeur `text` :
le `\`, le `;`, la virgule et le saut de ligne s'y échappent, et la ligne se plie à 75 octets comme
toutes les autres. C'est ce que le sérialiseur faisait pour nous, et un groupe nommé « Amis,
Famille » sort faux sans cela. `Fold` et `Unfold` sont déjà dans le composeur, et la moitié
inverse existe aussi : `AddressBookFilter.Unescaped` déséchappe déjà ces quatre-là, et la lecture
passe par la bibliothèque, qui décode. L'échappement **sortant** est le seul morceau à ajouter.

### 7. Supprimer un contact le retire de chaque groupe qui le porte

Dans la transaction qui le supprime. L'alternative — laisser la référence pendre, ce que fait Apple — ne se
verrait même pas à l'écran, puisque `memberIds` ne rend que les membres résolus : elle laisse un
`MEMBER` fantôme dans la carte, qui ressusciterait l'appartenance si l'UID était un jour réutilisé,
et qu'un autre client peut afficher comme un membre de plus. La carte doit dire ce que le carnet
sait.

**La règle vaut pour les trois chemins de suppression** : `ContactStore.DeleteAsync`,
`DeleteManyAsync`, et `DavContactWriter.DeleteAsync` — qui retire la ligne lui-même, sans passer
par le store : un contact supprimé depuis le téléphone sort des groupes comme un contact supprimé
au webmail. Et `DavContactWriter.DeleteAllAsync`, qui vide le carnet par `DeleteManyAsync`,
emporte les groupes avec : le retrait ignore les groupes qui meurent avec lui, sous peine de rangs
et de révisions sur des cartes déjà condamnées.

**Le retrait matche toutes les formes que la lecture accepte** (décision 5) : `urn:uuid:` quelle
que soit sa casse, l'UID nu, et les deux noms de propriété. La ligne à retirer se retrouve par son
`member_uid` résolu, jamais par une comparaison textuelle — un `MEMBER` que DAVx⁵ a écrit nu sort
de la carte comme celui qu'Apple a préfixé.

**« Avec lui » désigne la liste entière, pas la tranche.** `DeleteManyAsync` découpe à `BatchSize`,
cent, et chaque tranche est sa propre transaction sous son propre rang. Un vidage de carnet en fait
cinquante ; une exclusion calculée sur la tranche laisserait un groupe tombant en cinquantième
position se faire réécrire quarante-neuf fois — quarante-neuf rangs, quarante-neuf révisions
archivées — avant de mourir. L'exclusion se calcule sur les `ids` remis à la méthode, jamais sur
la tranche. Et un groupe que deux tranches touchent prend deux rangs : la transaction dont parle
l'ouverture de cette décision est celle de la tranche, pas celle du lot.

Le prix est que chaque carte de groupe touchée prend un rang de synchronisation et une révision. Il
reste petit : les groupes se comptent par dizaines, pas par milliers, et une suppression en lot
plafonnée à 200 contacts ne touche jamais plus de cartes qu'il n'existe de groupes.

Symétriquement, supprimer un **groupe** ne touche aucun contact : cascade sur la table des membres,
tombe DAV, révision, et les fiches restent.

### 8. Le mode `CATEGORIES` est hors périmètre, et nommé

DAVx⁵ offre deux encodages au choix du compte. L'autre, `CATEGORIES`, n'a aucune entité groupe :
chaque contact porte la liste de ses groupes en texte sur sa propre carte (`CATEGORIES:Amis,
Collègues`), et le groupe n'est que la même chaîne répétée sur N cartes.

Il est écarté pour trois raisons qui tiennent ensemble. Renommer un groupe de quarante personnes y
réécrit quarante cartes, soit quarante rangs de synchronisation au lieu d'un. Un groupe vide ne peut
pas exister, faute de carte pour le porter — donc « créer un groupe puis y glisser un premier
contact » est impossible, et c'est le geste central de la tranche. Et la propriété est standard sans
être convenue : un autre client peut y écrire de vraies étiquettes qui ne sont pas des groupes.

**Le supporter en lecture seule serait pire que de l'ignorer** : la bande afficherait des groupes
qu'on ne peut ni renommer ni vider, moitié de lignes refusant les gestes de l'autre moitié.

Ce qui est déjà vrai le reste : `CATEGORIES` traverse `vcard_raw` sans dommage, aucune donnée n'est
perdue, rien ne s'affiche. Un utilisateur concerné change un réglage dans son client.

**Ce qu'il faut mesurer avant de figer cette décision, c'est de quel côté tombe le défaut du
réglage.** Si DAVx⁵ arrive en *separate vCards*, la tranche marche d'origine sur Android et le
repli ne concerne que ceux qui ont changé le réglage eux-mêmes. S'il arrive en *categories*, il
faut que **chaque** utilisateur Android aille toucher un réglage pour voir la fonction — et la note
de version doit le dire. L'écart entre les deux mondes est trop grand pour être supposé :
l'appareil de la campagne 4d répond à la question en une minute, et elle se pose avant
l'implémentation.

### 9. Groupes imbriqués : stockés, non résolus

Un `MEMBER` pointant sur un groupe est légal. La table l'accepte — `member_uid` est un UID
quelconque — et la carte le conserve. La résolution le laisse de côté : la jointure ne rend que les
membres qui sont des contacts. Aucune hiérarchie n'apparaît à l'écran, rien ne se perd sur le fil.

### 10. L'API parle en ids de contact, la carte en UID

Les payloads de membres transportent des `contact_id` : c'est ce que les écrans tiennent, et
`contactTypes.ts` énonce déjà la règle — « The API sends neither the vCard UID nor the raw card ».
Le store fait la conversion au moment d'écrire la ligne `MEMBER`. Aucun UID vCard ne franchit la
frontière HTTP.

### 11. Les routes de membres sont en lot dès le départ

Le drag transporte toute la sélection cochée (`dragIds`), et « Retirer du groupe » agit sur une
sélection. Cinquante contacts feraient sinon cinquante requêtes, sans rien à dire d'un échec au
trentième — l'argument exact qui a donné `DELETE /api/Contacts` avec ses ids dans le corps. Même
plafond de 200, même traitement d'un id inconnu : un no-op silencieux, parce qu'un lot ne peut pas
échouer à moitié.

**Le plafond est celui d'un appel, pas celui d'un groupe.** Rien ne borne le nombre de membres, et
rien n'a besoin de le faire : le carnet en compte 5 000 au plus (décision 18), soit une carte de
quelque deux cent cinquante kilo-octets — loin du mébioctet que `MaxCardBytes` accorde.

### 12. Le scope porte le groupe dans l'URL

`ContactScope` devient l'union `'all'`, `'favorites'`, `group:<guid>`. Le scope transite déjà en clair
dans la barre d'adresse, et un GUID y voyage sans souci — l'argument rendu en 3a pour `?id=`.

`canDropIntoScope` garde sa forme exacte : elle refuse `all` et accepte tout le reste. **Les groupes
deviennent donc des cibles de drop sans une ligne de plus**, ce que son commentaire annonçait
(« Groups, when they land, are targets by construction »).

Le drop sur un groupe **ajoute et ne retire jamais**, la règle du drop sur Favoris et pour la même
raison : un geste qui ajouterait ou retirerait selon l'état de chaque ligne rendrait un résultat
différent par contact.

Un scope qui ne résout plus — groupe supprimé depuis la bande ou depuis un autre appareil, GUID
étranger collé dans l'URL — se replie sur `all` : le repli qu'un `?id=` périmé reçoit déjà — la
suppression en lot le laisse pendre et la fiche se ferme sans navigation corrective.

### 13. Deux dialogues, pas trois

Le produit a déjà le sien, `DeleteConfirmModal` (partagé dans `components/`), qui prend la suppression de groupe telle quelle avec
un message qui dit que les contacts, eux, restent. Création et renommage partagent un
`GroupNameModal` unique : même champ, même validation, deux titres.

Le « + » vit sur l'en-tête de la section *Groupes* de la bande, **pas** dans `.column-actions` : ce
rang mesuré ne laisse que 141px pour un libellé français qui en prend 131,9, et un troisième carré
de 40px y forcerait une re-mesure dans `probes/localisation-widths.html`.

Renommer, supprimer et « Écrire au groupe » sortent d'un `DropdownMenu` sur la ligne, plutôt que
d'une édition en place : la ligne est déjà une cible de drop, et un champ de saisie qui reçoit un
`dragover` est un conflit inutile.

### 14. Le retrait a deux chemins, chacun nommé

Dans le scope d'un groupe, la bande de sélection gagne **« Retirer du groupe »** à côté de
**« Supprimer »**, qui reste la suppression du carnet et garde son dialogue. Sans les deux libellés,
« Supprimer » dans un scope de groupe est ambigu, et l'ambiguïté porte sur une perte de données.

La fiche du contact liste ses groupes en **puces avec un ×**, pour que l'appartenance soit visible
sans parcourir les scopes.

### 15. Le composeur gagne une espèce de ligne, pas une seconde liste

`suggestionsFor` clé ses lignes sur l'adresse repliée ; une ligne de groupe n'a pas d'adresse et
insère N destinataires. Elle porte son propre discriminant, se range **avant** les adresses — ils
sont peu nombreux et plus spécifiques que le nom d'une personne —, affiche son nombre de membres,
et insère l'adresse principale de chaque membre résolu en dédoublonnant contre les jetons déjà
posés.

**Le plafond de dix lignes ne se partage pas.** `suggestionsFor` tronque à `DEFAULT_LIMIT` après
avoir trié ; des groupes rangés en tête d'une liste commune videraient le menu de ses adresses chez
qui en a beaucoup. Les groupes ont leur propre budget — trois lignes —, appliqué avant la fusion :
les dix places des adresses restent les dix places des adresses.

Le retour de `suggestionsFor` devient donc une union, et le travail se partage : `contactSearch.ts`
range et discrimine, `RecipientsField` rend la ligne, la parcourt au clavier — l'index `active` et
la touche Entrée ne connaissent aujourd'hui que des adresses — et développe la sélection.

**Un groupe qui n'apporte aucune adresse est refusé par un toast**, jamais inséré en silence —
qu'il soit sans membre résolu ou qu'aucun de ses membres résolus ne porte d'adresse, le cas est le
même : n'ajouter aucun destinataire sans rien dire est indistinguable d'un bug. Le champ n'a
aucun moyen de le dire lui-même : le message remonte à l'`onNotify` de `ComposeView`, la voie que
toutes les autres annonces du composeur empruntent déjà.

**Le groupe déjà entièrement posé n'est pas ce cas-là.** `suggestionsFor` reçoit les jetons en
place dans son `exclude` ; un groupe dont ils couvrent tous les membres n'a plus rien à offrir, et
l'annoncer en échec désignerait comme une anomalie ce que l'utilisateur a sous les yeux. Il ne
paraît pas dans le menu, exactement comme une adresse déjà posée n'y paraît pas.

### 16. « Écrire au groupe » réutilise le chemin existant

`newMessageSeed` prend déjà un tableau d'adresses. `ContactsLayout.writeTo`, lui, en prend **une**
et la lui emballe : écrire au groupe l'élargit à `string | string[]`, avec les adresses principales
des membres résolus. Son `backTo` pointe aujourd'hui sur `?id=${selectedId}` en dur et doit rendre
le scope du groupe. Deux signatures, aucun mécanisme nouveau.

Le cas vide de la décision 15 se traite ici en amont : l'entrée est désactivée quand le groupe
n'offre aucune adresse, plutôt que d'ouvrir un composeur sans destinataire.

### 17. Le nom du groupe est son `FN`, et la colonne est `display_name`

Un groupe n'a pas de prénom. Son nom vit là où vit celui d'une fiche qui n'en a pas non plus :
`display_name`, la colonne qui porte le `FN` que le produit affiche.

**La carte porte le nom en `FN` et un `N` vide, `N:;;;;`.** Un `N` rempli serait un nom de famille
inventé ; l'omettre coûterait deux choses de plus. RFC 2426 rend le `N` obligatoire en 3.0, et
Apple en écrit un sur ses propres cartes de groupe. Et le writer 3.0 de la bibliothèque en pose un
de toute façon : `StripNamePlaceholders` ne fait que *vider* le `?` qu'il y met, il ne retire
jamais la ligne — omettre le `N` demanderait un mécanisme neuf pour aller à l'encontre du format.
La forme vide de RFC 2426 n'invente rien et ne coûte rien.

Deux mécanismes du projecteur croisent cette décision et la respectent déjà : `Chosen(…)`, qui
rend `null` quand la carte n'a pas de `FN` — c'est un filtre qui *retire* un `FN` qu'un writer
aurait dérivé, jamais un générateur ; la dérivation vit côté écriture, dans `FallbackDisplayName`
— donc un groupe sans `FN` est un groupe sans nom, affiché comme tel plutôt que nommé d'après un
membre ; et `WithoutPlaceholder`, qui retire le `?` : un groupe nommé `?` n'existe pas.

**Deux groupes peuvent porter le même nom.** L'unicité serait une règle du produit imposée à un
protocole qui l'ignore : un client a le droit de `PUT` deux cartes de groupe homonymes, et un
refus ferait échouer la synchronisation pour une préférence d'affichage.

### 18. Un groupe compte dans le plafond du carnet

`MaxPerUser` vaut 5 000 et se lit sur les lignes de `contacts` en trois endroits — la création
webmail, le lot de l'import, et le garde-fou du `PUT` DAV. Un groupe est une ligne de `contacts`
(décision 1), donc il compte.

La création d'un groupe devient le quatrième endroit qui le **contrôle** : compter dans le plafond
sans jamais s'y heurter le laisserait franchir par la seule porte qui ne regarde pas.

L'alternative — ne compter que les `Individuals()` — ferait diverger le compte du produit de celui
du protocole, à moins de corriger les trois. Et c'est le protocole qui a raison : ce que le plafond
protège est le nombre de cartes que la collection sert, pas le nombre de personnes que l'utilisateur
connaît. Les groupes se comptent par dizaines, la marge le supporte.

### 19. Une carte de groupe importée par `.vcf` est projetée comme telle

`POST /api/Contacts/Import` accepte un `.vcf` autant qu'un CSV, et `VCardImportMapper` fait passer
la carte par le projecteur. Sans cette décision, un carnet exporté d'un téléphone et réimporté ici
rendrait ses groupes en fiches quasi vides : **le défaut que la tranche répare, survivant dans le
seul chemin qu'elle n'aurait pas regardé.**

Le projecteur lisant déjà `KIND` et les membres, il n'y a rien à écrire de plus que de laisser
passer : `kind` et les lignes de membres sortent de la projection comme les `EMAIL`. Les `MEMBER`
qui ne désignent encore personne restent pendants, ce que la décision 2 autorise, et se résolvent
quand les cartes des membres arrivent — l'ordre du fichier n'a donc pas d'importance.

**Et une ligne de groupe entrante ne se résout que par UID, jamais par nom.** L'index par nom
écarte les groupes comme cibles ; le sens entrant doit l'être aussi, sans quoi une carte de groupe
à l'UID inconnu fusionnerait dans la fiche sans adresse d'un homonyme — ou dans un groupe homonyme,
que la décision 17 autorise précisément. Un groupe que l'UID ne connaît pas se crée, toujours.

**L'index par nom tenu pendant la lecture du fichier écarte les groupes lui aussi.** Celui que la
décision 4 fait charger sans eux n'est que la moitié du mécanisme : `ImportAsync` continue de
l'alimenter à mesure qu'il crée des lignes sans adresse, et un groupe est une ligne sans adresse.
Sans cette seconde moitié, une carte sans adresse nommée « Amis », placée dans le même `.vcf` après
la carte du groupe « Amis », fusionnerait dans le groupe qui vient de naître — le défaut de la
décision 4, déplacé d'un tour de boucle. (Le cas CSV de la décision 4 est l'autre moitié : là, le
groupe existait déjà et vient de l'index chargé.)

**Et un UID qui traverse la frontière des espèces n'est pas une fusion.** L'index par UID est
consulté le premier et ignore le `kind` : une carte de groupe dont l'UID appartient déjà à une
fiche — ou l'inverse — remplirait la mauvaise espèce, et la créer à côté violerait
`uq_contacts_user_uid`. C'est le seul cas où l'UID ne tranche pas ; la ligne est refusée et
comptée, comme l'est aujourd'hui un nom ambigu.

Ce qui reste hors périmètre est l'appartenance dans le **CSV**, faute de colonne convenue.

### 20. Les écritures de groupe n'ont pas de revendication de version

`PUT /api/Contacts/{id}` porte un `cardHash` et rend `409` : l'éditeur montre une carte entière, et
sauver par-dessus le travail d'un téléphone y perdrait des lignes qu'on avait sous les yeux. Les
trois routes qui modifient une carte de groupe — renommage, ajout, retrait — font aussi un
lire-modifier-écrire de `vcard_raw`, et n'en portent pas ; la création compose une carte neuve, la
suppression ne relit rien, la question ne s'y pose pas.

**Sans revendication ne veut pas dire sans le chemin complet d'une écriture de carte.** Les trois
routes prennent celui d'`UpdateAsync` : `NextSequenceAsync` dans la transaction, relecture de
`vcard_raw` sous son verrou, ligne éditée, `card_hash` recalculé, révision archivée,
re-projection. Le rang et la révision ne sont pas une politesse — sans eux l'ETag et le jeton de
synchronisation ne bougent pas, et un renommage fait au webmail n'atteint jamais le téléphone. Et
c'est ce chemin qui rend vrai le paragraphe suivant : le `INSERT … ON DUPLICATE KEY` de
`NextSequenceAsync` pose le verrou InnoDB qui sérialise tous les écrivains d'un utilisateur —
c'est lui, pas le `cardHash`, qui fait du lire-modifier-écrire une section critique.

Ce qu'un conflit y coûte est sans commune mesure : un renommage perdu perd un nom, et l'écriture
étant chirurgicale (décision 6), la course ne touche que la ligne en jeu — un `MEMBER` ajouté
pendant un renommage survit au renommage. Le dernier qui écrit gagne, sur cette ligne-là seulement.

Le prix est nommé : deux appareils qui renomment le même groupe dans la même seconde en gardent un
seul nom, sans que personne soit prévenu. C'est accepté ; le jour où un écran montrerait la carte
d'un groupe entière, il faudra la revendication avec.

## Schéma

À rejouer sur `snoopy_webmail` **et** `snoopy_webmail_dev`, avant tout déploiement du backend. À
verser dans `docs/superpowers/webmail-contacts-tables.md` comme les tranches précédentes.

```sql
ALTER TABLE `contacts`
  ADD COLUMN `kind` ENUM('individual','group') NOT NULL DEFAULT 'individual'
    COMMENT 'Espèce de la carte ; group = KIND:group / X-ADDRESSBOOKSERVER-KIND:group'
    AFTER `source`;

CREATE TABLE `contact_group_members` (
  `group_id`   CHAR(36)          NOT NULL,
  `member_uid` VARCHAR(255)      NOT NULL
    COMMENT 'UID du membre sans son préfixe urn:uuid: ; pas son id, un client peut PUT le groupe avant ses membres',
  `position`   SMALLINT UNSIGNED NOT NULL COMMENT 'Rang du MEMBER dans la carte ; simple attribut',
  PRIMARY KEY (`group_id`, `member_uid`),
  INDEX `ix_group_members_uid` (`member_uid`),
  CONSTRAINT `fk_group_members_group`
    FOREIGN KEY (`group_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Pas de `COLLATE` de colonne sur `member_uid` : la table l'est déjà en `utf8mb4_bin`, et
`contacts.uid` — la colonne qu'elle vient rejoindre — n'en porte pas non plus. Et la clé primaire
tient : `(group_id, member_uid)` pèse 1 164 octets, exactement comme `uq_contacts_user_uid` qui
tourne déjà sur ce schéma — le format de ligne DYNAMIC y est donc prouvé, pas supposé.

**La clé est l'identité, pas le rang.** `(group_id, member_uid)` est la clé primaire et `position`
un simple attribut : retirer un membre renumérote les survivants, et sous une clé
`(group_id, position)` un survivant renuméroté changeait de clé primaire — EF le suivait alors
comme une paire `Deleted`+`Added`, émettait l'`INSERT` avant le `DELETE`, et l'unique
`uq_group_member` sautait. Sous cette clé-ci la paire fusionne en un seul `UPDATE` de `position`,
le mécanisme que `contact_emails` exerce déjà en production.

Aucun rattrapage de données : la requête de sondage ne trouve aucune carte de groupe en base, et le
défaut `individual` classe correctement tout le stock. Une carte de groupe arrivant par un `PUT`
ultérieur est projetée comme telle au moment où elle arrive.

**La relation EF doit être déclarée** dans `PreferencesDbContext`. Sans arête déclarée, EF ordonne
les `INSERT` par nom de table, et les tests InMemory ne peuvent pas l'attraper : `contact_group_members`
s'insérerait avant `contacts`. La forme est celle des trois sœurs à clé composite, ligne pour
ligne — `contact_photos`, la quatrième, a une clé simple — : `HasKey(new { GroupId, MemberUid })`,
puis `HasOne<Contact>().WithMany().HasForeignKey(…).OnDelete(DeleteBehavior.Cascade)`.

## API

| Route | Corps | Réponse |
|---|---|---|
| `GET /api/ContactGroups` | | `{ groups: [{ id, name, memberIds }] }` |
| `POST /api/ContactGroups` | `{ name }` | `200` + `{ id, name, memberIds: [] }` |
| `PUT /api/ContactGroups/{id}` | `{ name }` | `204` |
| `DELETE /api/ContactGroups/{id}` | | `204` |
| `POST /api/ContactGroups/{id}/Members` | `{ contactIds }` | `204` |
| `DELETE /api/ContactGroups/{id}/Members` | `{ contactIds }` | `204` |

**`200` et non `201` à la création**, et le groupe entier plutôt que son seul id : c'est ce que
`POST /api/Contacts` rend, et une seconde convention dans le même module se paierait en lecture
sans rien acheter.

**Une enveloppe à la lecture, pas un tableau nu**, pour la raison que `ContactListResponse` porte
écrite : un compte ou un jeton de synchronisation s'y ajoute plus tard sans changer la forme de la
réponse. C'est le même argument que le `200`, appliqué à l'autre bout de la route.

**Les refus, ceux du carnet.** `404` pour un id que le carnet de l'appelant ne porte pas — un
groupe d'autrui est un groupe qui n'existe pas — ; `400` pour un nom vide, pour un nom plus long
que la colonne, et pour le plafond atteint, qui est déjà l'enveloppe `400` que `POST /api/Contacts`
rend au-delà de 5 000 (décision 18) ; `409` jamais (décision 20). Un `contactId` inconnu dans un lot
de membres est un no-op silencieux, exactement comme un id inconnu dans un `DELETE /api/Contacts` —
et un id qui désigne un groupe, le sien compris, l'est aussi : l'imbrication est un état du fil
(décision 9), pas un geste qu'une route à nous compose.

**Un contrôleur à part.** `ContactsController` porte onze routes sur 454 lignes ; six de plus en
feraient le fichier qui fait trop de choses.

**`memberIds` ne contient que les membres résolus.** Une seule requête sert alors quatre besoins —
compteurs de la bande, filtrage de la liste, puces de la fiche, expansion dans le composeur — et le
compteur annonce exactement ce que la liste montrera, par construction plutôt que par vigilance. Le
filtrage reste côté client, comme tout le reste du module. `GET /api/Contacts/{id}` ne change pas.

**Clé de cache `['contactGroups', accountId]`**, à côté de `['contacts', accountId]`. Les mutations
de membres invalident les deux — le nombre de groupes d'un contact change ses puces. Et la
symétrie vaut : les mutations existantes qui touchent l'appartenance sans le savoir —
`deleteContact`, `deleteContacts`, `importContacts` — invalident `['contactGroups']` aussi, sans
quoi compteurs et puces survivent au membre supprimé. `onSettled`, jamais `onSuccess`.

## Fichiers

| Fichier | Rôle |
|---|---|
| `Data/Preferences/Contact.cs` | colonne `kind` |
| `Data/Preferences/ContactGroupMember.cs` | l'entité fille |
| `Data/Preferences/PreferencesDbContext.cs` | la relation déclarée |
| `Models/Contacts/ContactProjection.cs` | `Kind` et les membres dans la projection |
| `Repositories/ContactKind.cs` | `Individuals()` / `GroupCards()`, la clause partagée |
| `Repositories/ContactGroupStore.cs` (+ interface) | CRUD et membres |
| `Repositories/ContactStore.cs` | la re-projection **et ses trois chemins de purge**, le plafond, les suppressions, l'audit des vingt-cinq accès |
| `Models/Contacts/ContactGroup*.cs` | les payloads des six routes |
| `Repositories/DavContactWriter.cs` | le retrait des groupes sur le `DELETE` DAV (décision 7) |
| `Controllers/ContactGroupsController.cs` | les six routes |
| `Services/ContactValidator.cs` | la borne du nom de groupe, à côté des autres largeurs de colonne |
| `Services/Contacts/VCardProjector.cs` | `KIND` et `MEMBER`, deux dialectes |
| `Services/Contacts/VCardComposer.cs` | `MEMBER` inséré / retiré, et la carte de groupe neuve |
| `Services/Contacts/VCardImportMapper.cs` | le groupe importé par `.vcf` |
| `Services/CardDav/VCardVersionConverter.cs` | les deux propriétés, dans les deux sens |
| `api.js` | les six appels, à côté des onze de `Contacts` |
| `modules/contacts/contactGroupTypes.ts`, `queries.ts` | le modèle client et ses hooks |
| `modules/contacts/ContactScopes.tsx` | la section *Groupes*, son « + », son menu |
| `modules/contacts/GroupNameModal.tsx` | création et renommage |
| `modules/contacts/ContactsLayout.tsx` | scope `group:` (sept `'favorites'` en dur), drop, « Écrire au groupe » |
| `modules/contacts/ContactList.tsx` | « Retirer du groupe » dans la bande |
| `modules/contacts/ContactCard.tsx` | les puces |
| `modules/contacts/contactSearch.ts` | la ligne de groupe dans les suggestions |
| `modules/mail/compose/RecipientsField.tsx` | la ligne rendue, parcourue au clavier, et développée |
| `modules/mail/compose/ComposeView.tsx` | le toast du groupe vide, par son `onNotify` |
| `locales/{en,fr}/contacts.json` | les libellés du module |
| `locales/{en,fr}/compose.json` | ceux de la ligne de groupe et du toast |

## Tests

- **Le projecteur, sur les deux dialectes** — `KIND:group` et `X-ADDRESSBOOKSERVER-KIND:group`
  reconnus, membres extraits, doublon de `MEMBER` réduit à une ligne, `MEMBER` pendant conservé.
  Et sur les formes de valeur : `urn:uuid:` retiré quelle que soit sa casse, UID nu accepté, URI
  d'un autre schéma stocké tel quel et laissé pendant, `MEMBER` de plus de 255 caractères écarté
  de la projection sans que la carte le perde.
- **Le second `PUT` de la même carte de groupe** rend le même `2xx` que le premier et laisse une
  seule ligne par membre : le test de la moitié « clear » du cycle, celui qui rougit si
  `ProjectionCache` est resté à quatre tables.
- **Le composeur** — un `MEMBER` ajouté et un retiré laissent le reste de la carte identique
  octet pour octet. C'est le test qui doit rougir en premier si la décision 4 de 4a est enfreinte.
  Et une carte de groupe neuve porte `X-ADDRESSBOOKSERVER-KIND:group`, un `FN` qui est le nom, et
  un `N:;;;;` — pas le `?` de la bibliothèque.
- **Le membre ajouté suit le dialecte de la carte** — sur une carte 4.0 stockée, la ligne écrite
  est `MEMBER`, jamais `X-ADDRESSBOOKSERVER-MEMBER`. Et sur une carte 3.0, le membre ajouté est
  bien dans la carte relue : le test qui rougit si l'écriture est repassée par le modèle —
  l'écrivain 3.0 n'émettant jamais `VCard.Members`, et `SpliceUnmodelledFamilies` révertant une
  famille `X-` que la carte portait déjà.
- **Le retrait efface la ligne sous toutes ses formes** — un `MEMBER` écrit nu sort de la carte
  comme un `MEMBER` préfixé `urn:uuid:`, quelle que soit la casse du préfixe, dans les deux
  dialectes.
- **Le renommage** ne touche que le `FN` : les `EMAIL` de la carte sont là après, et il n'apparaît
  pas de `N` rempli. Un groupe nommé « Amis, Famille » se relit sous ce nom-là — la virgule
  échappée à l'écriture, déséchappée à la lecture.
- **Chaque écriture de groupe prend un rang et une révision** — renommage, ajout et retrait
  avancent le jeton de synchronisation et changent l'ETag de la carte, sans quoi le téléphone ne
  voit jamais le geste du webmail.
- **Le convertisseur de version, dans les deux sens** — un groupe 3.0 servi en 4.0 porte `KIND` et
  `MEMBER` ; un groupe 4.0 servi en 3.0 porte `X-ADDRESSBOOKSERVER-KIND` et
  `X-ADDRESSBOOKSERVER-MEMBER`. Ni l'un ni l'autre ne sort de la bibliothèque seule, et le second
  rougit si on l'a écrit comme un renommage de lignes que le writer a déjà supprimées.
- **L'import d'un `.vcf`** — une carte de groupe importée par fichier arrive en groupe, membres
  compris, et un `MEMBER` dont la carte suit dans le même fichier se résout quel que soit l'ordre.
  Deux fusions refusées dans le même `.vcf` : une carte sans adresse nommée « Amis » placée
  **après** la carte du groupe « Amis » crée une fiche au lieu d'entrer dans le groupe, et une
  carte dont l'UID appartient déjà à l'autre espèce est comptée refusée plutôt que fusionnée.
- **La clause `kind`** — un groupe n'apparaît ni dans la liste, ni dans l'export CSV, ni dans
  l'autocomplétion, ni dans les compteurs, ni comme cible de fusion de l'index par nom à l'import ;
  il apparaît dans toutes les lectures DAV, et il compte dans le plafond du carnet.
- **La suppression d'un contact** retire son UID de chaque groupe, et chaque carte touchée prend un
  rang de synchronisation — par les trois chemins, `DELETE` DAV compris ; le vidage du carnet ne
  touche pas les cartes des groupes qu'il emporte, **au-delà de cent contacts** : le test tient
  plus d'une tranche de `DeleteManyAsync`, sans quoi il ne prouve rien.
- **La résolution des membres porte le `user_id`** — un `MEMBER` désignant l'UID d'un contact d'un
  autre carnet ne résout pas, ne sort ni par `memberIds` ni par l'expansion du composeur. Et elle
  essaie les deux formes : un membre dont l'UID stocké est lui-même `urn:uuid:…` résout.
- **L'`ENUM`** — un test épingle la valeur `group`, la leçon du `MODIFY COLUMN source` de 4c-ii.
- **Frontend** — `canDropIntoScope` sur un scope `group:`, le drop qui ajoute sans retirer, la bande
  qui distingue « Retirer » de « Supprimer », le scope `group:` qui ne résout plus et se replie
  sur `all`, et dans `RecipientsField` : la ligne de groupe rangée
  avant les adresses, atteinte à la flèche, développée en N jetons dédoublonnés contre ceux déjà
  posés, et le groupe sans adresse à offrir qui n'en pose aucun mais fait remonter le toast. Plus
  les deux bornes du menu : le budget de trois lignes de groupe ne mange pas les dix places des
  adresses, et un groupe dont tous les membres sont déjà posés ne paraît pas dans le menu — donc
  ne déclenche pas le toast.

**Un scénario client à ajouter à `carddav-4d-conformance.md` section 5**, et il ne peut pas être une
observation : aucun groupe n'existe en base, il faut en **créer** un de chaque côté. Groupe créé au
webmail vu sur le téléphone ; groupe créé sur le téléphone vu au webmail ; membre ajouté dans chaque
sens ; groupe supprimé dans chaque sens.

**Le tableau nomme son client par ligne, parce que les deux de la campagne 4d ne se valent pas
ici.** Thunderbird ne mappe pas les groupes vCard sur ses listes de diffusion : la carte y sort en
fiche, et « créé côté client » n'y est pas jouable — c'est une limite du client, pas un défaut à
relever. DAVx⁵ l'est. Et l'app Contacts d'iOS crée des groupes depuis iOS 16 — des « listes », qui
se synchronisent en cartes de groupe —, donc « créé sur le téléphone » se joue sur DAVx⁵ comme sur
un iPhone.

**Rien d'Apple n'a encore été observé sur ce serveur.** La section 7 de 4d parque les points de
guet Apple au « jour où un appareil Apple se présente », et ce jour n'est pas venu. Ce que cette
spec dit d'Apple — le dialecte qu'il comprend, le `N` qu'il écrit, la référence pendante qu'il
laisse — relève de la connaissance générale, pas d'un passage. La tranche ne se clôt pas dessus :
faute d'appareil, la ligne reste ouverte, comme elle l'est en 4d, et le dire vaut mieux que la
cocher.

## Ce que la tranche ne fait pas

- **Le mode `CATEGORIES`** — décision 8.
- **Les groupes imbriqués** — décision 9 : stockés, non résolus.
- **Les carnets multiples** — toujours hors périmètre depuis 4c ; un groupe n'est pas un carnet.
- **L'appartenance dans le CSV** — le CSV du produit décrit une fiche, pas un carnet, et
  l'appartenance y demanderait une colonne dont le format n'est convenu nulle part. C'est aussi
  tout l'export : `GET /api/Contacts/Export` ne sait écrire que du CSV, donc un groupe ne sort pas
  d'ici en fichier. Le `.vcf` les porte, mais **à l'entrée seulement** (décision 19).
- **Un groupe comme destinataire persistant** — le composeur développe les membres au moment de
  l'insertion et n'en garde pas trace. Un message envoyé « au groupe » est un message à N personnes.
