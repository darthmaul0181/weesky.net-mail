# Agenda 5 — cadrage du projet : données, fonctionnalités, interface

Document de tête du projet Agenda, à la suite du projet Contacts
([4a](2026-08-14-webmail-contacts-4a-vcard-model-design.md),
[4b](2026-08-22-webmail-contacts-4b-editor-design.md),
[4c](2026-08-23-webmail-contacts-4c-carddav-design.md),
[4d](2026-08-31-webmail-contacts-4d-conformance-design.md)). Il fixe ce que le module stocke, ce
qu'il fait et à quoi il ressemble ; chaque tranche recevra ensuite sa propre spec, qui renvoie ici
pour les décisions communes et ne les rediscute pas.

## Ce que le projet vise

Un agenda **personnel** dans le webmail, synchronisé avec les téléphones et les clients de bureau
par CalDAV, sur le modèle exact de ce que 4a–4d ont fait du carnet d'adresses. Le webmail est le
serveur ; il n'y a pas de serveur tiers. La référence fonctionnelle est le socle commun des agendas
web open source auto-hébergés — Nextcloud Calendar et SOGo pour l'écran, Roundcube/Kolab pour ce
qu'un webmail y intègre ; côté serveur, l'étalon reste celui de 4c, sabre/dav (donc Baïkal et
Nextcloud) et Radicale. AgenDAV, souvent cité, est à l'arrêt depuis des années et ne compare plus
rien d'actuel. Ce socle, c'est trois vues plus une liste, plusieurs agendas colorés, un éditeur
complet, glisser-déposer, recherche, import/export. Le projet ne vise pas le travail en équipe :
pas de partage, pas de disponibilité d'autrui, pas de réservation de créneaux.

## D'où viennent les numéros

Ce document, comme ceux qu'il cite, désigne le travail passé par des codes courts : « 4c »,
« 2c3a », « 5b ». La règle de lecture est fixe. **Le chiffre est le sous-projet**, dans l'ordre
où le webmail a été construit ; **la lettre est la tranche** de ce sous-projet, chacune avec sa
spec, son plan et sa livraison ; **ce qui suit la lettre est une sous-tranche**, quand une tranche
s'est révélée trop large pour un seul document (`2c2a`, `2c2b`) ; et un chiffre romain (`4c-i`,
`4c-ii`) n'est pas une spec mais un **plan d'implémentation** découpé sous une même spec. La
numérotation du shell (§ 11 de sa spec) donnait le calendrier en 3 et les contacts en 4 ; l'ordre
réel a été l'inverse, et le carnet a occupé deux sous-projets, d'où l'agenda en 5.

| Code | Ce que c'est | Spec | État |
|---|---|---|---|
| **1** | **Le shell** : la coquille de l'application — connexion, bandeau, rail des modules, thèmes, routes, contrat de tokens CSS | [shell](2026-07-18-webmail-shell-design.md) | livrée |
| **2** | **Le courrier**, en quatre tranches | | |
| 2a | Lire : connexion IMAP, dossiers, liste paginée, volet de lecture, pièces jointes, session | [2a](2026-07-18-webmail-mail-2a-design.md) | livrée |
| 2a.5 | Les dossiers systèmes (corbeille, archive, indésirables, brouillons, envoyés) reconnus par rôle | [2a.5](2026-07-19-webmail-mail-2a5-system-folders-design.md) | livrée |
| 2b | Organiser, en quatre sous-tranches : 2b1 drapeaux lu/non-lu et étoile · 2b2 corbeille, archive, indésirable, déplacer/copier · 2b3 sélection multiple et vider un dossier · 2b4 recherche IMAP | [2b1](2026-07-22-webmail-flags-2b1-design.md) · [2b2](2026-07-22-webmail-actions-2b2-design.md) · [2b3](2026-07-23-webmail-multiselect-2b3-design.md) · [2b4](2026-07-23-webmail-search-2b4-design.md) | livrées |
| 2c | Écrire : 2c1 rédaction et envoi SMTP · 2c2a identités d'envoi · 2c2b répondre, répondre à tous, transférer · 2c3a brouillons (2c3b signatures reste à faire) | [2c1](2026-07-23-webmail-compose-2c1-design.md) · [2c2a](2026-07-24-webmail-identities-2c2a-design.md) · [2c2b](2026-07-25-webmail-reply-forward-2c2b-design.md) · [2c3a](2026-07-25-webmail-drafts-2c3a-design.md) | livrées sauf 2c3b |
| 2d | Le multi-comptes : connecter d'autres boîtes à sa session, avec leurs identifiants chiffrés | [2d](2026-07-29-webmail-multi-accounts-2d-design.md) | livrée |
| **3** | **Le carnet d'adresses**, version simple (nom, adresses, favori) | | |
| 3a · 3b | Tables, API, module à trois colonnes et éditeur (3a) ; autocomplétion des destinataires depuis le carnet (3b) | [3a/3b](2026-07-27-webmail-contacts-3a3b-design.md) | livrées |
| 3c | Capture automatique : un envoi vers une adresse inconnue crée la fiche | [3c](2026-07-27-webmail-contacts-3c-design.md) | livrée |
| 3d | Import et export CSV | [3d](2026-07-27-webmail-contacts-3d-design.md) | livrée |
| **4** | **Le carnet complet et sa synchronisation CardDAV** — le webmail devient serveur pour le téléphone | | |
| 4a | Le modèle complet et le moteur vCard : la fiche est un fichier `.vcf` souverain, les colonnes n'en sont qu'un index | [4a](2026-08-14-webmail-contacts-4a-vcard-model-design.md) | livrée |
| 4b | L'éditeur étendu (téléphones, adresses, dates, organisation…) | [4b](2026-08-22-webmail-contacts-4b-editor-design.md) | livrée |
| 4c | Le serveur CardDAV : 4c-i le secret de synchronisation et l'onglet Sync · 4c-ii le protocole (découverte, listage, lecture, écriture, synchronisation incrémentale, tombes, historique) | [4c](2026-08-23-webmail-contacts-4c-carddav-design.md) | livrée |
| 4d | La conformité : suite de tests `ccs-caldavtester`, Thunderbird, DAVx⁵ | [4d](2026-08-31-webmail-contacts-4d-conformance-design.md) | livrée |
| 4e | Les groupes de contacts | [4e](2026-08-31-webmail-contacts-4e-groups-design.md) | livrée |
| 4f | La photo en écriture | [4f](2026-09-03-webmail-contacts-4f-photo-design.md) | livrée |
| **5** | **L'agenda et sa synchronisation CalDAV** — ce document | | |
| 5a | Modèle de données, moteur iCalendar, occurrences, API | à écrire | à venir |
| 5b | Les écrans | à écrire | à venir |
| 5c | Le serveur CalDAV | à écrire | à venir |
| 5d | La conformité clients, iOS compris | à écrire | à venir |
| 5e | Les invitations | envisagée | à venir |

Les specs sans numéro sont des améliorations transversales, livrées au fil de l'eau entre deux
tranches : palettes, notifications, rafraîchissement périodique, en-tête et actions du lecteur,
score de spam, images distantes et expéditeurs de confiance, images en ligne, priorité, texte
brut, format de rédaction par défaut, modales à la taille du contenu, code source d'un message,
PWA, localisation, mobile et tablette, conversations groupées, animation de sortie d'une ligne,
actions groupées sur les contacts, découplage du webmail, OAuth des comptes connectés, table
`users` à clé GUID, pool de connexions IMAP.

## Découpage

| | Tranche | Analogue contacts | Dépend de |
|---|---|---|---|
| 5a | Modèle de données, moteur iCalendar (Ical.Net), expansion des occurrences, API | 4a | — |
| 5b | Écrans : vues, barre latérale, bulle d'aperçu, éditeur, téléphone | 3x + 4b | 5a |
| 5c | Serveur CalDAV sous le principal `/dav` existant | 4c | 5a |
| 5d | Conformité clients (`ccs-caldavtester`, Thunderbird, DAVx⁵, iOS) | 4d | 5c |

L'ordre est **5a → 5b → 5c → 5d**, pour la raison qui a fixé celui des contacts : le module devient
utile et éprouvé par une édition réelle avant qu'un client externe n'en dépende.

**Ce que chaque tranche livre, concrètement.**

- **5a — les fondations, sans écran.** Deux tables (`calendars`, `calendar_events`) et la table
  fille des participants ; le moteur qui lit et écrit un fichier iCalendar (Ical.Net) et en
  extrait les colonnes ; le calcul des occurrences d'un événement récurrent sur une fenêtre ; et
  l'API REST que l'écran appellera (`GET /api/calendar/events?from=&to=&tz=`, créer, modifier,
  supprimer, import et export `.ics`). À la fin de 5a rien n'est visible, mais tout se teste :
  les cinq fichiers réels de la décision 4 passent en aller-retour, et un test crée un
  hebdomadaire, en déplace une occurrence et vérifie la liste plate que l'API rend. C'est
  l'équivalent de 4a, qui avait fait de la fiche un fichier vCard sans changer un écran.
- **5b — l'agenda que l'utilisateur voit.** Les dix planches des maquettes (§ L'interface) :
  vues semaine, mois, jour et liste, barre latérale avec mini-mois et agendas colorés, bulle
  d'aperçu, éditeur avec la question « cette occurrence / suivantes / toutes », glisser-déposer
  et redimensionnement, recherche, import/export, et les cinq écrans de téléphone. À la fin de
  5b, l'agenda est utilisable au quotidien dans le webmail — mais **seulement** dans le webmail.
  C'est l'équivalent de 3a et 4b réunis.
- **5c — le webmail devient serveur CalDAV.** Sous le même `/dav` que le carnet : le téléphone
  découvre les agendas, les lit, y écrit, en crée, et se synchronise par jeton comme pour les
  contacts. Concrètement : le second interrupteur de l'onglet Sync, la généralisation du routage
  `/dav` à plusieurs collections, les verbes `MKCALENDAR`/`MKCOL`, les cinq rapports (décision 8),
  les filtres, les tombes et l'historique par agenda. À la fin de 5c, on écrit « au RFC » : le
  protocole est complet, mais aucun client réel n'a encore été branché. C'est l'équivalent de 4c.
- **5d — la preuve avec de vrais clients.** On rejoue la suite de tests d'Apple
  (`ccs-caldavtester`, partie CalDAV) contre le serveur de dev, puis Thunderbird, DAVx⁵ et un
  iPhone sur des scénarios fixés d'avance : appairage par la seule adresse, création dans les deux
  sens, « cette occurrence seulement », rappel, couleur, suppression. Chaque écart est corrigé ou
  nommé comme divergence assumée, dans un rapport. C'est l'équivalent de 4d, avec iOS en plus.
- **5e — les invitations, seulement envisagée.** Envoyer une invitation par mail quand on ajoute
  un participant, comprendre le `.ics` d'une invitation reçue, répondre accepter/refuser, et
  afficher l'état des participants. 5a en pose déjà la donnée (la table des participants) et 5c
  en réserve la place (`calendar-user-address-set`), mais rien ici ne la conçoit : elle aura sa
  propre spec, si elle se fait.

**5d vise iOS, et 4d ne l'avait pas visé.** L'iPhone de 4d était emprunté et n'était plus
disponible ; la tranche l'avait sorti de son périmètre et laissé les points d'Apple en « points de
guet », nommés dans son rapport. Ce n'est pas une raison de recommencer, parce que le rapport de
force a changé : l'agenda est le module où les clients d'Apple ont le plus de particularités —
`calendar-user-address-set` à la découverte, le `PROPPATCH` de la couleur et de l'ordre, le
`time-range` sur un `comp-filter VALARM`, `CALDAV:expand` sur `calendar-data` — et la décision 8 les
sert toutes. Les servir sans jamais les exercer, c'est écrire du code sur une supposition. Trois
moyens, dans l'ordre de préférence :

1. **Un iPhone**, le seul qui vérifie vraiment le client autant que le serveur : appairage par la
   seule adresse de l'onglet Sync, création dans les deux sens, « cette occurrence seulement » sur
   un récurrent, rappel, couleur d'agenda, suppression.
2. **Le simulateur iOS, ou Calendar.app, sur un Mac.** Réglages → Calendrier → Ajouter un compte →
   Autre → CalDAV parle le même protocole avec le même code client ; ce que le simulateur ne couvre
   pas est ce qui dépend du matériel, et il n'y a rien de tel ici.
3. **Le rejeu de traces**, en dernier recours. Les jeux de propriétés qu'iOS demande à la
   découverte sont publics — sabre les documente, et `ccs-caldavtester` vient du serveur d'Apple —,
   et un test qui les rejoue vérifie nos réponses sans rien vérifier du client.

Ce que 5d ne fera pas, c'est déclarer iOS couvert sans l'avoir branché. Si aucun des trois n'est
disponible le jour venu, le rapport le dit point par point comme 4d l'a fait, plutôt que de laisser
croire.

Tranches envisagées **après** 5d, non conçues ici : 5e invitations (envoi, réponses par mail,
statut des participants) ; agenda « Anniversaires » projeté depuis les contacts ; abonnement à un
agenda externe (webcal) ; partage entre utilisateurs ; notifications de rappel dans le webmail.

## Les décisions

### 1. Le fichier est la référence, les colonnes sont un index

Une ligne de `calendar_events` est une **ressource CalDAV**, pas un `VEVENT` : un `VCALENDAR`
complet (RFC 5545) qui porte tout ce qui partage un même `UID` — le `VEVENT` maître, ses exceptions
à `RECURRENCE-ID`, les `VTIMEZONE` qu'ils citent —, stocké tel quel dans une colonne `ics_raw`.
C'est la règle de RFC 4791 § 4.1 (une ressource, un `UID`), et c'est ce qu'un client dépose d'un
seul `PUT` quand il modifie « celle-ci seulement ». Les colonnes se lisent sur le maître.
Quelques colonnes en sont extraites à chaque écriture, pour ce qu'une base ne sait pas demander à
un fichier : placer sur une grille, chercher un mot, filtrer. C'est la règle de 4a (`vcard_raw`
souverain, colonnes projetées), et pour la même raison : un téléphone écrit des lignes qu'on ne
comprend pas (`X-APPLE-…`, blocs `VTIMEZONE`, rappels avec leurs propres propriétés) et qu'il doit
retrouver intactes.

Ce que les colonnes portent, et pourquoi :

| Colonne | Sert à |
|---|---|
| titre, lieu, description | recherche texte |
| début, fin, journée entière, fuseau | placer sur la grille |
| première et dernière occurrence | ne lire que les événements qui touchent la fenêtre affichée, récurrences comprises (le `firstoccurence`/`lastoccurence` de sabre/dav) ; calculées **exceptions et `RDATE` compris**, car les unes comme les autres déplacent une occurrence hors de la plage de la règle. Stockées en instants UTC ; une heure flottante ou une date sans heure y est posée dans le fuseau de l'agenda (décision 6), et la requête de fenêtre **élargit ses bornes d'un jour de chaque côté** avant que l'expansion ne tranche — sans cette marge, un navigateur à l'ouest du fuseau de l'agenda perd l'anniversaire du bord de la fenêtre |
| récurrent (oui/non) | affichage, et savoir qu'il faut expanser |
| agenda (`calendar_id`) | filtre par agenda |
| disponibilité, visibilité | le badge de la bulle d'aperçu, et l'évaluation d'un `prop-filter` CalDAV sur `STATUS`, `TRANSP` ou `CLASS` sans relire le fichier |
| `UID`, empreinte, nom DAV, séquence de synchro | CalDAV, comme en 4c — mais l'`UID` est unique **par agenda**, voir plus bas |

**L'`UID` est unique par agenda, pas par utilisateur.** C'est la lettre du RFC 4791 § 4.1 —
l'unicité vaut « in the scope of the calendar collection » — et c'est la précondition
`no-uid-conflict` du § 5.3.2.1, obligatoire sur le `PUT`. L'index est donc `(calendar_id, uid)` là
où 4c portait `(user_id, uid)`, et ce n'est pas une transposition mécanique : le même `UID` dans
deux agendas est **légitime**, c'est ce qu'écrit un client qui copie un événement de l'un vers
l'autre, et un index par utilisateur le refuserait par un code que le client ne peut pas corriger.
Le corollaire tombe sur le changement d'agenda de la décision 2 : la suppression et la création se
font dans **une seule transaction**, sans quoi l'`UID` existe un instant dans les deux collections —
ou dans aucune.

**Une récurrence sans fin n'a pas de dernière occurrence, et la colonne doit quand même en porter
une.** `FREQ=WEEKLY` sans `UNTIL` ni `COUNT` est le cas courant, pas le cas limite. Laisser la
colonne à `NULL` obligerait chaque requête de fenêtre à porter un `OR derniere IS NULL`,
c'est-à-dire à lire de toute façon tous les événements infinis — ce que la colonne existe
précisément pour éviter. La dernière occurrence d'une règle sans fin vaut donc une **date-butoir**
fixée une fois pour toutes, `2100-01-01` : au-delà de cet horizon la grille cesse de montrer les
récurrences infinies, ce qu'aucun utilisateur vivant n'observera et qu'un recalcul corrigerait.
sabre fait le même geste avec `2038-01-01`, une borne qu'il tient du temps Unix sur 32 bits et que
rien ne nous impose ; la nôtre est plus loin pour la même raison qu'elle est arbitraire. La première
occurrence, elle, existe toujours.

**Une exception sans maître se stocke quand même.** Un fichier peut ne porter que des `VEVENT` à
`RECURRENCE-ID`, sans le `VEVENT` maître qui porte la règle : c'est ce qu'un export Google ou une
invitation iTIP produit quand on n'a été invité qu'à une occurrence, et RFC 4791 ne l'interdit pas.
Les colonnes se lisent alors sur la **première** exception, l'événement est marqué non récurrent,
et chaque exception est une occurrence à part entière. Le refuser rejetterait des fichiers réels ;
inventer un maître écrirait ce que le client n'a pas dit.

L'organisateur et les participants vont dans une table fille (adresse, nom affiché, rôle, réponse,
et le `RECURRENCE-ID` du composant d'où la ligne vient, `NULL` pour le maître), projetée dès 5a et
affichée en lecture seule en 5b : rien n'est perdu quand un événement invité arrive d'un téléphone,
et 5e trouvera la donnée en place. Elle est **projetée** comme les colonnes, et pas davantage : une
réécriture la purge et la réinsère, rien ne s'y ajoute que le fichier ne porte. Le dire ici évite
qu'on la traite un jour comme une donnée souveraine — 5e hériterait d'une table qu'une simple
relecture effacerait.

Pour un événement simple, les colonnes dupliquent presque tout le fichier ; c'est assumé. Dès qu'il
est récurrent ou vient d'un client, elles n'en sont plus qu'un extrait. Si on les perdait, on les
recalculerait depuis les fichiers.

Restent dans le fichier seulement : la règle de récurrence et ses exceptions, les rappels
(`VALARM`), les catégories, la priorité, l'adresse web, les pièces jointes, les lignes `X-`.

### 2. Plusieurs agendas par utilisateur, créés du webmail comme du téléphone

Une table `calendars` : propriétaire, nom, description, couleur, ordre, fuseau (décision 6), nom
DAV, et **affiché ou masqué** — la case à cocher de la barre latérale, tenue côté serveur pour
suivre l'utilisateur d'un navigateur à l'autre comme chez Nextcloud, et qui est à l'agenda ce
qu'`is_favorite` est à la fiche (4c décision 6) : invisible du protocole, jamais projetée, et un
changement n'avance ni ctag ni jeton. Un agenda `default` est créé à la première requête du
webmail qui en a besoin (décision 6) ;
les autres depuis la barre latérale du webmail **ou depuis un client**. Chaque événement porte son
`calendar_id`. La description n'a pas d'écran dans le webmail ; elle existe parce que DAVx⁵ la
propose à la création comme à la modification d'un agenda, et qu'une propriété qu'on annonce sans
la stocker répondrait toujours vide à qui vient de l'écrire.

**Un client CalDAV peut créer un agenda, et il faut deux verbes pour couvrir les deux téléphones.**
C'est l'écart avec 4c, où le carnet était unique et où toute création répondait `405`. Le
raisonnement a changé avec le produit : l'appareil sur lequel on tient son agenda est le téléphone,
et le bouton « créer un agenda » y existe dans DAVx⁵ comme chez Apple. Le refuser, c'est laisser à
l'écran un bouton qui échoue — le manuel de DAVx⁵ prévient d'ailleurs que le geste ne marche que
« si le serveur le prend en charge, ce qui n'est pas obligatoire ». Refuser était aussi devenu
bancal depuis que le `DELETE` d'un client supprime un agenda (plus bas) : on pouvait effacer depuis
le téléphone sans pouvoir recréer.

Les deux verbes ne sont pas un caprice de conformité :

- **`MKCALENDAR`** (RFC 4791 § 5.3.1), le verbe du RFC CalDAV, est celui des deux téléphones : les
  clients d'Apple l'émettent, et DAVx⁵ aussi — son corps réel, visible dans la discussion #2209 de
  `davx5-ose`, porte `resourcetype`, `displayname`, une `calendar-description` vide,
  `apple:calendar-color` et `supported-calendar-component-set`. C'est la porte qui compte.
- **`MKCOL` étendu** (RFC 5689), c'est-à-dire le `MKCOL` de WebDAV avec un corps `DAV:mkcol` qui
  déclare « cette collection est un agenda », est la porte des bibliothèques génériques et de la
  suite `mkcol` de `ccs-caldavtester`, que 4d avait dû éteindre dans ses `<features>`. sabre et
  Radicale la servent ; elle coûte un décodage de corps, pas un second chemin.

Un seul chemin de création derrière, deux portes devant. Le RFC dispense de `MKCALENDAR` les
serveurs « qui n'ont qu'un agenda par personne, créé d'avance » (§ 5.3.1) : c'est la dispense d'un
serveur à agenda unique, et nous en avons vingt. sabre — donc Nextcloud et Baïkal — et Radicale
servent tous les deux la création ; seul Google ne la sert pas, et sa raison est que son interface
web *est* l'endroit où l'on gère ses agendas. La nôtre est le téléphone.

**Ce qu'un client peut régler à la création, et ce qui reste au webmail.** Le corps de la requête
porte des propriétés ; on honore les cinq que le `PROPPATCH` accepte — `displayname`,
`CALDAV:calendar-description`, `apple:calendar-color`, `apple:calendar-order`,
`CALDAV:calendar-timezone` — et **toute autre propriété du corps est ignorée, jamais refusée**.
C'est la règle de sabre, et elle n'est pas une facilité : le RFC impose de tout défaire dès qu'une
propriété échoue, et DAVx⁵ tient un `207` portant un `403` pour une création manquée — l'agenda
apparaît puis disparaît à la synchro suivante, c'est exactement ce que Fastmail lui a fait en
refusant `calendar-color` (#2209). Or les corps réels portent plus que nos cinq : le `resourcetype`
de DAVx⁵, et chez Apple les propriétés d'ordonnancement (`calendar-free-busy-set`,
`schedule-calendar-transp`) qu'iCal écrit parce que d'autres serveurs les annoncent — nous ne les
annonçons pas (décision 8) et les ignorons ici comme partout. Refuser l'inconnu, c'est refuser les
deux téléphones. Le
`resourcetype`, s'il est présent, doit dire `collection` + `calendar` ; autre chose est un refus
(table ci-dessous). Ce qui manque prend un défaut : le nom vaut le segment d'URL choisi par le
client, la couleur est la suivante de la palette, le fuseau est celui de `default`, l'ordre place
l'agenda en dernier. Un agenda né d'un téléphone apparaît donc dans la barre latérale à la synchro
suivante avec ce que le téléphone a su dire, et **tout le reste se règle dans le webmail** — c'est
la granularité admise : le téléphone crée, le webmail affine.

**Un agenda a deux noms, et le webmail choisit le second sans le faire parler.** Le nom affiché
(`displayname`) est celui de la barre latérale et du téléphone ; il se change des deux côtés, à tout
moment. L'adresse — le dernier segment de `/dav/calendars/{userId}/{nom}/` — est le nom technique
par lequel les appareils désignent la collection, et il est fixé à la création. Un client choisit le
sien ; **un agenda né dans le webmail prend son `id`**, comme une fiche née dans le webmail prend
`{id}.vcf` en 4c (décision 5). Dériver l'adresse du nom saisi serait plus joli une journée : elle
mentirait dès le premier renommage, et deux agendas homonymes demanderaient un suffixe qu'il
faudrait inventer. Un identifiant ne veut rien dire, donc il ne peut pas mentir.

C'est aussi ce qui rend le renommage inoffensif. Changer le nom affiché ne touche pas l'adresse,
donc aucun appareil ne voit disparaître une collection : il lit un libellé différent, rien de plus.
Changer l'adresse, à l'inverse, ferait répondre `404` à l'ancienne et apparaître une neuve — chaque
téléphone re-téléchargerait l'agenda entier et perdrait ce qu'il gardait en propre dessus (la case
affiché/masqué, les rappels déjà programmés dans le système), en affichant les deux le temps de
faire le ménage. C'est pourquoi l'adresse ne se renomme pas, ni ici ni chez sabre, qui sépare les
deux notions pour cette raison.

**L'état de synchro est par agenda, et c'est un écart avec 4c, pas un héritage.** En 4c la
séquence, l'epoch et le filigrane vivent par utilisateur (`contact_sync_state` a `user_id` pour
seule clé), et les tombes aussi, parce qu'il n'y avait qu'un carnet. Avec vingt agendas, un
compteur commun ferait avancer le jeton de tous à chaque écriture dans un seul, et chaque téléphone
relirait dix-neuf collections pour rien ; les tombes, elles, doivent savoir de quelle collection
elles viennent. Chaque agenda porte donc sa propre séquence, sa propre epoch et son propre
filigrane, ses tombes sont clés par `(calendar_id, dav_name)`, et **un agenda naît avec sa ligne
d'état, dans la même transaction que lui**. 4c tolère un carnet sans ligne d'état — son `0` nu
n'a rien à protéger, et le premier vrai ctag en différera — ; ici la ligne existe d'emblée parce
que l'agenda peut naître d'un client qui va la lire dans la seconde, et qu'un `null` à ce moment-là
serait un chemin de plus à écrire. La règle vaut pour les deux portes, webmail et client.

**Les refus, chacun avec la précondition que le client sait lire.** Ce sont les seuls endroits où
la création peut mal tourner, et les nommer ici évite qu'ils sortent en `500` :

| Cas | Réponse |
|---|---|
| succès | `201 Created`, avec le `Cache-Control: no-cache` que l'exemple du RFC montre — pas un MUST, mais ce que sabre rend et ce que les clients ont vu |
| un agenda porte déjà ce nom d'URL | `405`, ce que le RFC 4918 § 9.3.1 impose au `MKCOL` et dont `MKCALENDAR` hérite |
| l'adresse visée n'est pas directement sous le home d'agendas — dans un agenda, ailleurs dans `/dav` | `403 CALDAV:calendar-collection-location-ok` |
| le corps demande `VTODO` ou `VJOURNAL` dans `supported-calendar-component-set` | `207` dont le `propstat` de cette propriété porte `403`, et **rien n'est créé** — le RFC impose de tout défaire ; c'est la décision 3 appliquée à la porte de création, et créer un agenda d'événements à qui demandait une liste de tâches serait pire que refuser. C'est le **seul** `propstat` à `403` que la création émet : toute autre propriété inconnue est ignorée (ci-dessus) |
| le corps d'un `MKCOL` ne déclare pas un agenda — pas de corps du tout, ou un autre `resourcetype` — ou le `resourcetype` d'un `MKCALENDAR` dit autre chose que `collection` + `calendar` | `403 DAV:valid-resourcetype` (RFC 5689). L'absence de corps vaut « collection ordinaire », et une collection ordinaire dans un home d'agendas n'est pas un type que nous servons : c'est notre lecture, et elle est écrite ici plutôt que devinée en 5d. Le second cas est l'abus qu'iCal fait de `MKCALENDAR` depuis OS X 10.9.2 pour créer un **abonnement** (`calendarserver:subscribed`) — sabre le contourne en honorant ce `resourcetype` ; nous n'avons pas d'abonnements, donc nous le refusons |
| le `calendar-timezone` fourni n'est pas un objet iCalendar à un seul `VTIMEZONE` | `403 CALDAV:valid-calendar-data` |
| le nom d'URL est invalide | la validation de la décision 5 de 4c, mot pour mot : non vide, 255 caractères, pas de `/`, pas de `\`, pas de caractère de contrôle, ni espace de bord, ni `.` ni `..` — appliquée au segment **décodé une seule fois** depuis le chemin, et ré-encodé segment par segment dans tout `href` écrit, comme 4c le fait pour `dav_name` ; la colonne est en `utf8mb4_bin` pour la même raison |
| vingt agendas déjà présents | `507 Insufficient Storage`, le code que le RFC 4918 § 9.3.1 prévoit pour un `MKCOL` qui manque de place |

**Ce que ça change pour 5d, et c'est un gain net.** 4d lançait `mkcol.xml` en sachant qu'elle
échouerait — « un fichier qu'on ne lance pas ne mesure rien » — et éteignait `Extended MKCOL` dans
ses `<features>`, ce qui comptait les tests concernés comme ignorés. Ici la création est servie, la
fonctionnalité s'allume, et ces suites mesurent quelque chose ; le geste « créer un agenda » de
DAVx⁵ et d'iOS, tous deux par `MKCALENDAR`, entre dans les scénarios vérifiés au lieu d'être un
refus à observer.

En revanche le `PROPPATCH` d'un client est **accepté** sur cinq propriétés.
`displayname`, `CALDAV:calendar-description`, `apple:calendar-color` et `apple:calendar-order` sont
ce que « modifier l'agenda » offre dans DAVx⁵ comme dans iOS, la table les porte, et les refuser
donnerait une couleur par appareil ; la couleur s'accepte avec le canal alpha qu'Apple écrit
(`#RRGGBBFF`) et se range sur six chiffres. `CALDAV:calendar-timezone` n'a pas d'écran, elle : les
clients d'Apple l'écrivent sans rien demander, et c'est le réglage dont la décision 6 fait dépendre
l'interprétation des heures flottantes — la refuser laisserait le serveur en décider seul. Toute
autre propriété reste refusée, y compris les rappels par défaut qu'iOS pose sur un agenda
(`default-alarm-vevent-datetime`, `default-alarm-vevent-date`) : sabre les refuse aussi, et 5d
observe ce qu'iOS en fait plutôt que de le supposer.

**Le gestionnaire de `PROPPATCH` devient donc à statut mixte, et c'est le premier endroit où 5c
s'écarte de 4c.** La décision 16 de 4c rend `207` avec `403` sur **chaque** propriété, partout ; ici
la même réponse porte des `propstat` à `200` pour les cinq propriétés ci-dessus et à `403` pour
les autres. Le carnet d'adresses ne change pas — son `PROPPATCH` refuse toujours tout, `me-card`
compris. Et ce qu'un agenda accepte n'avance ni le ctag ni le jeton de synchronisation. RFC 6578 ne
tranche pas la question — il parle des membres et se tait sur les propriétés de la collection — ;
c'est donc notre choix, et sa raison est qu'un client qui vient de renommer sa collection n'a aucun
événement à relire : les autres appareils voient le nouveau nom au prochain `PROPFIND` du home,
que DAVx⁵ comme iOS refont d'eux-mêmes.

**Vingt agendas par utilisateur.** Le plafond de 5000 ressources de la fonctionnalité 6 est par
agenda ; sans borne sur le nombre d'agendas, ce n'est plus un plafond mais un facteur — et la
création étant maintenant ouverte au protocole, la borne n'est plus seulement une politique
d'écran. Vingt est au-delà de ce qu'une barre latérale montre sans défiler ; le vingt-et-unième
répond `507` au client et un message à l'écran.

**Un événement peut changer d'agenda** depuis l'éditeur ; pour CalDAV c'est une suppression dans
une collection et une création dans l'autre. La ligne garde son `id`, son `UID` et son `dav_name`
(unique par agenda, renommé seulement en cas de collision) ; l'ancien agenda reçoit une tombe et
avance sa séquence, le nouveau avance la sienne et la ligne prend cette valeur. **Supprimer un
agenda** archive chacun de ses événements en révision (cause `delete`), puis supprime événements,
tombes et état de synchro : la collection répond `404`, ce qu'un client range en retirant l'agenda.
L'archivage se fait **par lots de cent**, la séquence avançant à chaque lot : un agenda plein, c'est
5000 ressources qui peuvent peser 1 Mo chacune, et les archiver sous un seul rang mettrait plusieurs
gigaoctets dans une transaction. C'est la mécanique du `DELETE` de carnet de 4d, décision 3, à une
différence près : 4d gardait la ligne d'état (le carnet réapparaît vide, epoch conservée), ici elle
disparaît avec l'agenda. **Les révisions s'ancrent sur l'utilisateur, pas sur l'agenda** : comme
`contact_revisions` en 4c (clé étrangère vers `users`, `contact_id` nullable pour survivre à la
fiche), leur `calendar_id` et leur `event_id` sont nullables et sans cascade — sinon la suppression
d'agenda effacerait au rang suivant l'archive qu'elle vient d'écrire. L'agenda `default` ne se
supprime pas.

**Le `DELETE` d'un client sur une collection supprime l'agenda ; sur `default`, il le vide.** 4d a
dû ouvrir ce verbe sur le carnet, et pas par complaisance : `ccs-caldavtester` supprime la
collection dans le bloc `<start>` de plusieurs suites, et un `<start>` en échec **saute le fichier
entier** — c'est ainsi que 4d a perdu `put.xml` et `sync-report.xml` avant de le corriger. Le cas
revient tel quel en 5d, à une différence près qui compte : avec plusieurs agendas, « supprimer » ne
peut plus vouloir dire « vider ». Le geste « Delete collection » de DAVx⁵ sur un agenda secondaire
le supprime donc pour de bon, par le même chemin que la barre latérale — révisions, tombes, `404`
ensuite. Sur `default`, le verbe vide l'agenda et répond `204` sans le faire disparaître, comme le
carnet de 4d : le seul agenda d'un compte ne peut pas s'absenter. `If-Match` est ignoré, une
collection n'ayant pas d'ETag.

L'usage réel d'un agenda est presque toujours pluriel (personnel / travail / famille), tous les
clients CalDAV affichent une liste d'agendas colorés, et l'ajouter après coup imposerait un
rattrapage sur chaque événement et sur les jetons de synchro.

### 3. Des événements seulement, pas de tâches

Le module ne gère que `VEVENT`. La collection CalDAV annonce
`supported-calendar-component-set = VEVENT` : un client n'y dépose jamais de `VTODO`, rien ne
traverse « verbatim », rien n'est perdu — c'est refusé à la porte. Les tâches sont un module à part
entière (état, échéance, priorité, second éditeur, secondes vues) et feront un projet à part si un
jour il en faut un.

L'import ne répond pas la même chose, et ce n'est pas une contradiction : un `PUT` porte **une**
ressource, qu'on accepte ou qu'on refuse entière, tandis qu'un fichier importé en porte des
centaines dont on ne va pas rejeter le tout pour une tâche égarée — il ignore les `VTODO` et les
`VJOURNAL` et les compte (fonctionnalité 6). Deux portes, deux contrats, et la même donnée absente
du stock dans les deux cas.

### 4. Ical.Net en lecture et en écriture

Le parsing, la projection des colonnes, l'expansion des occurrences et la réécriture d'un événement
modifié depuis le webmail passent par `Ical.Net` (MIT, 35 M de téléchargements, v5.2.3 de juin
2026, trois versions en 2026). Un seul outil, un seul modèle, aucune double
interprétation du même fichier.

**Et une seule base de fuseaux, celle qu'il embarque.** Ical.Net 5.2.3 dépend de NodaTime (≥ 3.2.2)
et résout ses `TZID` sur la TZDB de NodaTime ; résoudre les mêmes identifiants par `TimeZoneInfo` à
côté rouvrirait exactement la double interprétation que le paragraphe précédent ferme — deux bases
qui peuvent différer d'une transition, et dont le désaccord ne se voit que le jour du changement
d'heure, sur les événements de ce jour-là. C'est donc la TZDB qui répond partout, y compris là où le
webmail calcule sans passer par un fichier. En prime, `TimeZoneInfo` sur un identifiant IANA dépend
d'ICU sous Windows et tombe en mode globalisation invariante : une dépendance d'hôte qu'un service
n'a pas à porter.

L'alternative envisagée — un moteur de récurrences maison, ou Ical.Net en lecture seule avec un
remplacement de lignes maison pour l'écriture — a été écartée. La difficulté d'un moteur de
récurrences n'est pas l'algorithme du RFC mais ce que les clients réels écrivent, et une
bibliothèque qui a vu passer des millions de fichiers y est meilleure que ce qu'on écrirait ;
l'hybride, lui, crée deux lectures du même fichier qui peuvent ne pas être d'accord (un `VALARM` a
son propre `SUMMARY`).

Ce que ça implique : une modification depuis le webmail relit, modifie et **réécrit le fichier en
entier**. Les lignes inconnues sont conservées ; leur ordre et leur mise en forme peuvent changer.
L'empreinte (ETag) change, ce qui est normal pour une modification. Les révisions archivées
(décision 17 de 4c) gardent la version d'avant. **Si rien n'a changé, rien n'est écrit** : ouvrir
puis enregistrer sans toucher ne doit pas réveiller tous les clients. Mais « rien n'a changé » ne
peut **pas** se mesurer sur l'empreinte du fichier stocké : un fichier écrit par DAVx⁵ ou iOS ressort
d'Ical.Net avec un autre pliage et un autre ordre de propriétés, donc une autre empreinte, à
contenu égal — l'empreinte ne serait fidèle que pour les fichiers que le webmail a lui-même écrits.
La mesure est donc la **forme canonique** : l'ancien fichier relu et resérialisé par le même moteur,
comparé au nouveau sérialisé, tous deux **avant** les estampilles du paragraphe suivant. Égaux,
rien n'est écrit — ni le fichier, ni `DTSTAMP`. C'est toujours le fichier qui juge, jamais une
comparaison de champs, qui manquerait ce que les colonnes ne portent pas.

**Ce qu'une réécriture qui change quelque chose estampille.** `DTSTAMP` et `LAST-MODIFIED` prennent
l'instant de l'écriture — le premier est un MUST de RFC 5545 § 3.8.7.2, le second est ce que les
clients montrent dans « modifié le » — et `CREATED` se pose à la création. `SEQUENCE` s'incrémente
quand le début, la fin, la règle, ses `RDATE`/`EXDATE` ou le `STATUS` changent, c'est-à-dire sur
ce que RFC 5546 tient pour une modification significative : sans participants c'est inoffensif, et
avec 5e c'est ce qui dira aux invités qu'il faut relire. Un fichier venu d'un client n'est jamais
estampillé : il est stocké verbatim (décision 8).

Lacunes connues à traiter au-dessus de la bibliothèque. `RECURRENCE-ID;RANGE=THISANDFUTURE` :
la version 5 sérialise et relit le paramètre, mais l'**expansion** ne l'applique pas (issue #455,
à revérifier sur la 5.2.3 le jour de 5a, la lacune ayant pu se fermer) — un fichier qui le porte
est conservé tel quel et journalisé, et l'occurrence concernée s'affiche sans la modification tant
que l'issue tient ; la coupure de la décision 5 ne dépend pas de cette issue, elle tient par ses
propres raisons. Et pas de validation des `RRULE` (#903) : l'exception part à l'**expansion**, pas
au parsing, donc un `PUT` malformé passerait et casserait ensuite toute la fenêtre. Deux gardes par
nos soins : un `PUT` et un import expansent d'essai avant d'écrire et répondent
`403 valid-calendar-data` (décision 8) ou une ligne en erreur ; l'expansion d'une fenêtre isole
l'erreur par événement, le journalise et rend les autres.

**Le webmail écrit les `VTIMEZONE` qu'il cite.** RFC 4791 § 4.1 en fait un MUST : la ressource porte
un `VTIMEZONE` par `TZID` employé. Ça ne se fait pas tout seul — un `VEVENT` construit avec un
`TZID` se sérialise volontiers sans le bloc qui le définit — et le fichier qui en manque est
justement celui qu'iOS et Thunderbird lisent mal ou refusent. Le bloc se sérialise depuis la TZDB
(décision 6).

**Un `TZID` qui n'est pas un identifiant IANA est un cas réel, pas un cas limite.** Les fichiers
d'Outlook et d'Exchange écrivent `TZID=Romance Standard Time` ou `TZID=(UTC+01:00) Bruxelles…`,
avec leur propre `VTIMEZONE` à côté, et la TZDB de NodaTime ne les connaît pas. Trois paliers,
dans l'ordre : la table Windows → IANA que NodaTime embarque (`TzdbDateTimeZoneSource.WindowsMapping`)
résout les noms Windows ; à défaut, le `VTIMEZONE` porté par le fichier fait foi et ses règles
sont appliquées telles quelles ; à défaut encore, l'heure est traitée comme flottante et
journalisée. Un `PUT` n'est jamais refusé pour un fuseau inconnu, parce que le client qui l'écrit
ne peut pas le corriger.

**L'expansion est bornée, et les deux bornes sont ici parce que c'est une question de sécurité.**
Un `RRULE` est un programme que l'auteur d'un `PUT` choisit : `FREQ=SECONDLY;COUNT=1000000` tient en
une ligne et occupe un cœur. Mais une règle sans fin n'a pas de nombre d'occurrences, et c'est le
cas courant : un plafond « par ressource » refuserait tout hebdomadaire, ou bien devrait fixer un
horizon — et jusqu'à 2100, un quotidien sans fin dépasse déjà 10 000. La borne est donc une
**densité** : une ressource ne peut pas produire plus de **10 000 occurrences dans l'année qui suit
son `DTSTART`**. C'est ce qu'un `PUT` et un import expansent d'essai avant d'écrire — au-delà, `403`
sur la précondition `max-instances` que la collection annonce (décision 8), ou une ligne en
erreur. Un quotidien en fait 365, un horaire 8 760 ; `MINUTELY` et `SECONDLY` sont refusés, et rien
d'autre ne l'est. L'annonce `max-instances = 10000` est donc évaluée sur un an, ce que le RFC ne
prévoit pas : divergence nommée, pour que 5d la lise comme un choix. Et la fenêtre de l'API est
bornée à **cinq ans**, une demande plus large répondant `400` : c'est notre propre écran qui
l'appelle, et aucune vue n'affiche cinq ans. Un `time-range` CalDAV plus large reste servi, lui, et
**jamais tronqué** : la densité garantit qu'une ressource acceptée rend au plus 10 000 occurrences
par année de fenêtre, donc une quantité finie que l'énumération paresseuse d'Ical.Net 5 déroule sans
rien compter d'avance — et un client qui demande dix ans a le droit de les obtenir. Un serveur qui
rendrait une fenêtre à laquelle il manque des occurrences sans le dire ferait pire qu'un refus.

**Vérification en 5a, au premier plan** : quatre événements réels (iPhone, Thunderbird, Google
Agenda, et une invitation Outlook avec son `TZID` Windows) passés par lecture → écriture,
différences comparées ; une perte d'information se corrige localement ou en amont. Et un
**cinquième cas, dans l'autre sens** : un événement récurrent créé
dans le webmail, avec un `TZID`, un rappel, une occurrence modifiée (`RECURRENCE-ID;TZID=…`) et
une occurrence supprimée, relu par Thunderbird et par DAVx⁵ — et son jumeau **journée entière**,
dont l'`EXDATE;VALUE=DATE` doit encore retirer l'occurrence après relecture : c'est là que les
bibliothèques perdent une exception sans bruit, quand la forme de l'`EXDATE` ne suit pas celle du
`DTSTART`. C'est ce cas-là qui casse en premier — relire un fichier bien formé est ce qu'une
bibliothèque fait le mieux, en écrire un complet est ce que l'appelant oublie.

### 5. Les occurrences se calculent côté serveur

Un événement récurrent est stocké une fois, avec sa règle. Une **occurrence** est une instance
concrète à une date donnée ; **expanser**, c'est dérouler la règle sur une fenêtre. Le serveur seul
le fait : le `calendar-query` avec `time-range` de 5c l'exige (RFC 4791 §9.9), et l'API de 5b —
`GET /api/calendar/events?from=…&to=…` — rend une **liste plate d'occurrences** (chacune avec son
`UID`, sa date d'instance et ses champs affichables). Le client pose sur la grille, il ne calcule
rien. Un seul moteur, deux consommateurs.

La forme d'une occurrence suit celle de son heure, parce que le serveur expanse mais ne connaît pas
le fuseau du navigateur (décision 6) :

- **datée** : début et fin en instants UTC, plus le `TZID` d'origine ;
- **journée entière** : des dates sans heure, fin **exclusive** comme dans RFC 5545 — l'éditeur
  affiche la fin incluse ;
- **flottante** (ni `TZID` ni `Z`) : l'heure locale telle quelle, marquée flottante ; c'est le
  client qui la pose dans son fuseau.

La fenêtre `from`/`to` est en instants, que le client calcule dans son fuseau, **et la requête
porte ce fuseau** (`tz`, un identifiant IANA, obligatoire) : un événement journée entière ou
flottant touche la fenêtre si sa date touche un jour de la fenêtre, et « un jour » ne se découpe
dans des instants qu'avec un fuseau — celui du navigateur, que le serveur ne peut pas deviner. La
marge d'un jour de la décision 1 présélectionne en SQL ; c'est `tz` qui tranche à l'expansion. Ce
n'est pas la préférence de fuseau que la décision 6 refuse : rien n'est stocké, le navigateur dit
d'où il regarde à chaque requête. Le fuseau de l'agenda, lui, reste celui du protocole. Sans ces
règles, un anniversaire glisse d'un jour dès qu'on regarde depuis l'ouest de Greenwich.

**Ce que l'éditeur écrit, dans l'autre sens.** Un événement daté part avec le `TZID` du navigateur
(`Europe/Brussels`), jamais en UTC : un hebdomadaire de 9 h écrit en `Z` passerait à 10 h au
changement d'heure, et c'est le premier bogue qu'un utilisateur d'agenda remarque. Une journée
entière s'écrit `VALUE=DATE`, avec un `DTEND` exclusif. En lecture, `DURATION` vaut `DTEND`, et un
`DTEND` absent vaut un jour pour une date et zéro pour une heure (RFC 5545 § 3.6.1).

`EXDATE` retire une occurrence ; `RDATE` en ajoute ; un second `VEVENT` de même `UID` avec
`RECURRENCE-ID` remplace une occurrence par une version modifiée — c'est ainsi que les clients
écrivent « modifier celle-ci seulement », et c'est ainsi que le webmail l'écrira aussi, y compris
quand le geste est un glisser-déposer. **Supprimer celle-ci seulement** est un `EXDATE` sur le
maître, plus le retrait de l'exception qui portait ce `RECURRENCE-ID` s'il y en avait une ; sans ce
retrait, l'exception survivrait à l'occurrence qu'elle remplaçait.

**Un `RECURRENCE-ID` écrit par le webmail garde la forme du `DTSTART` du maître** : `VALUE=DATE`
pour une journée entière, le même `TZID` pour une datée — jamais l'instant UTC que l'API rend. C'est
l'erreur classique du sujet, et elle est silencieuse : un `RECURRENCE-ID` en UTC posé sur un maître
en heure locale ne se rattache à aucune occurrence, donc le client affiche l'ancienne et la nouvelle
côte à côte au lieu de l'une à la place de l'autre. L'API peut rendre l'instant, mais l'identifiant
d'occurrence qu'elle rend et que l'éditeur lui renvoie porte **la valeur littérale** du
`RECURRENCE-ID` à écrire.

« Celle-ci et les suivantes » ne s'écrit **pas** avec `RANGE=THISANDFUTURE` mais par coupure :
`UNTIL` sur la règle de l'original et un nouvel `UID` pour la suite — ce que Google et Nextcloud
font aussi, et ce que tout client relit sans rien savoir. La raison n'est pas d'abord Ical.Net
(décision 4) : `RANGE` est lu par Thunderbird, ignoré par Android — donc par ce que DAVx⁵ y dépose
— et refusé par Google à l'import, tandis que deux ressources ordinaires passent partout. Cinq
points s'y jouent, et chacun casse un fichier s'il est laissé au hasard :

- **Le `DTSTART` de la suite est la valeur que l'utilisateur vient de saisir**, pas le début de
  l'occurrence choisie : le geste courant est « à partir de maintenant, 10 h au lieu de 9 h », ou
  un glisser-déposer sur une autre journée. Si le jour de la semaine change sur une règle
  hebdomadaire, `BYDAY` suit — sinon la suite continuerait le lundi qu'on vient de quitter. Quand
  rien n'a bougé, c'est le début **d'origine** de l'occurrence, pas son début déplacé si elle avait
  été modifiée à part.

- **`UNTIL` se pose à l'instant qui précède l'occurrence choisie**, et non « la veille » : deux
  occurrences le même jour — `FREQ=HOURLY`, ou une règle qui produit deux séances le mardi —
  verraient sinon la première emportée avec la seconde. Pour une journée entière, où `UNTIL` est
  une `DATE` inclusive, c'est bien la veille.
- **`UNTIL` s'écrit en UTC** dès que le `DTSTART` porte un `TZID` (RFC 5545 § 3.3.10, un MUST). Une
  règle dont l'`UNTIL` est en heure locale donne un fichier invalide, que des clients rejettent en
  bloc plutôt qu'en partie.
- **`UNTIL` et `COUNT` s'excluent** (même paragraphe, un MUST NOT). Une règle à `COUNT=10` coupée
  après la quatrième occurrence perd son `COUNT` pour l'`UNTIL`, et la suite repart avec
  `COUNT=6` — le reliquat, compté sur les occurrences que la **règle** a produites avant la coupe,
  qu'elles aient été retirées par un `EXDATE` ou non (RFC 5545 § 3.8.5.3 : `COUNT` borne la règle,
  `EXDATE` retranche ensuite) ; les `RDATE` n'y comptent pas.
- **Les exceptions se répartissent de part et d'autre de la coupure.** Les `VEVENT` à
  `RECURRENCE-ID`, les `RDATE` et les `EXDATE` postérieurs au point de coupe passent à la nouvelle
  ressource et disparaissent de l'ancienne ; ceux qui le précèdent restent. Sans ce partage, une
  occurrence déjà déplacée réapparaît à sa place d'origine, ou en double. Mais un `RECURRENCE-ID`
  désigne une occurrence par son début d'origine, et **si la coupure a changé ce début, il ne
  désigne plus rien** : la suite afficherait l'ancienne et la nouvelle côte à côte. Quand seule
  l'heure a changé, les `RECURRENCE-ID`, `RDATE` et `EXDATE` transférés sont **rebasés** — même
  date, nouvelle heure ; quand le jour ou la règle a changé, ils sont **abandonnés**, et l'éditeur
  le dit avant d'enregistrer (« les modifications faites à des occurrences ultérieures seront
  perdues »), comme Calendar.app. Rebaser une date est deviner, et une exception devinée est pire
  qu'une exception perdue qu'on a annoncée.

### 6. Fuseau horaire : celui du navigateur pour l'écran, celui de l'agenda pour le protocole

Pas de préférence de fuseau dans les paramètres, jusqu'à ce qu'un cas réel en réclame une : l'écran
suit le navigateur, et c'est lui qui calcule la fenêtre qu'il demande. Les identifiants IANA
(`Europe/Brussels`) se résolvent sur la TZDB de NodaTime qu'Ical.Net embarque (décision 4), sur
Windows comme sur Linux ; les blocs `VTIMEZONE` traversent avec le fichier.

**Mais le serveur, lui, a besoin d'un fuseau à lui, et le RFC le nomme.** Une heure flottante et une
date sans heure n'ont pas d'instant tant qu'un fuseau ne leur en donne pas un ; un `time-range`
CalDAV doit pourtant décider si elles touchent la fenêtre. RFC 4791 § 7.3 tranche : le serveur
s'appuie sur `CALDAV:calendar-timezone` s'il est défini, sinon sur « le fuseau de son choix ». Un
choix implicite voudrait dire ici que l'API — qui reçoit la fenêtre du navigateur — et le
`calendar-query` — qui n'a que lui-même — ne répondent pas la même chose sur le même anniversaire,
c'est-à-dire le défaut que la décision 5 chasse, revenu par la porte du protocole. La table
`calendars` porte donc **une colonne de fuseau**, celle du navigateur au moment de la création,
servie et acceptée en écriture comme `CALDAV:calendar-timezone` (décision 2).

**D'où vient celui de `default`.** L'agenda `default` n'est pas créé « avec l'utilisateur » — la
ligne `users` naît sans navigateur à qui demander un fuseau — mais par **la première requête du
webmail qui en a besoin** : l'ouverture du module, ou l'allumage de l'interrupteur « CalDAV » de
l'onglet Sync, qui portent tous deux le fuseau du navigateur. **Un client CalDAV ne peut pas arriver
avant, et ce n'est pas le secret qui le garantit** — les utilisateurs de 4c en ont déjà un, et
DAVx⁵ découvre CardDAV et CalDAV sur le même compte, sans qu'on lui demande rien : dès que
`/dav/calendars/` répond, un téléphone déjà appairé pour les contacts y envoie son premier
`PROPFIND` sans qu'aucun navigateur n'ait jamais fourni de fuseau. Ce qui le garantit, c'est que
**la synchronisation CalDAV naît éteinte** (`caldav_enabled = 0`, § Paramètres) et ne s'allume que
depuis l'onglet, dans la même transaction que la création de `default`. Un agenda créé par un client
sans `calendar-timezone` hérite du fuseau de `default` (décision 2), qui existe donc toujours quand
la question se pose. Si la ligne manquait malgré tout — une base restaurée à la main —, le home
répond une liste vide plutôt que d'inventer un agenda dans un fuseau deviné.

La propriété ne porte pas un identifiant mais **un objet iCalendar contenant un `VTIMEZONE`**
(RFC 4791 § 5.2.2) : la colonne stocke l'identifiant IANA, la lecture sérialise le bloc depuis la
TZDB, un `PROPPATCH` lit le `TZID` du bloc reçu. C'est le seul réglage d'agenda qu'un client écrit
et que le webmail n'expose pas — et le laisser vide reviendrait à laisser le serveur choisir dans
le dos de l'utilisateur.

### 7. Invitations : modélisées, pas traitées

`ORGANIZER` et `ATTENDEE` sont projetés (décision 1) et affichés en lecture seule. Aucun envoi,
aucune réponse, aucun traitement d'un `.ics` reçu par mail avant 5e.

### 8. Ce que 5c annonce, et ce qu'il sert

4c a fermé la liste de ses propriétés plutôt que de la découvrir rapport de bogue après rapport de
bogue (décision 13), parce qu'un client traite une absence comme une collection cassée. 5c refera
l'exercice ; ce document en fixe la cible, pour que la tranche n'ait pas à la deviner et pour que
5d sache ce qu'il vérifie.

| Ressource | Ce que CalDAV ajoute à 4c |
|---|---|
| découverte | `/.well-known/caldav`, sur **toute méthode** et sans authentification, `301` vers `/dav/` avec le `Cache-Control: max-age=86400` de 4c, comme son jumeau CardDAV — DAVx⁵ et Thunderbird y envoient un `PROPFIND`, pas un `GET` |
| en-tête `DAV:` | `calendar-access` et `extended-mkcol`, en plus du `1, 3, addressbook` que le code sert aujourd'hui — le second est un MUST de RFC 5689 § 3.1 dès qu'on sert le `MKCOL` étendu, et `ccs-caldavtester` s'en sert pour décider si la fonctionnalité existe ; `access-control` a été retiré par la seconde revue de 4c, et `current-user-privilege-set` reste servi sans être annoncé, ce que 5c hérite tel quel. **Ni `calendar-auto-schedule` ni `calendar-schedule`** : voir l'ordonnancement, plus bas |
| principal | `calendar-home-set` — absent (`propstat 404`) quand CalDAV est éteint (§ Paramètres) ; `calendar-user-address-set` (RFC 6638 § 2.4.1) porte en `mailto:` **toutes** les adresses de l'utilisateur, la principale en tête : celles des comptes (2d) **et** celles de `sending_identities`, ses alias d'envoi — c'est par là qu'un client reconnaît l'organisateur ou le participant qu'il est, et une invitation reçue sur un alias absent ferait de l'utilisateur un étranger dans ses propres invitations, ce que 5e paierait |
| home | il rend **plusieurs** collections en `Depth: 1` : le premier écart structurel avec le carnet unique de 4c ; et il accepte `MKCALENDAR` et `MKCOL` étendu (décision 2) |
| agenda | `supported-calendar-component-set` (`VEVENT` seul), `supported-calendar-data`, `supported-collation-set` (`i;ascii-casemap` et `i;octet`, que RFC 4791 § 7.5.1 impose dès qu'on sert `text-match`), `calendar-timezone`, `calendar-description`, `max-resource-size`, `max-instances`, `apple:calendar-color`, `apple:calendar-order` — plus les propriétés de collection de 4c (`getctag`, `sync-token`, `supported-report-set`, `current-user-privilege-set`, `owner`). **Pas de `min-date-time` ni de `max-date-time`** : les annoncer engage à refuser un `PUT` hors bornes, et nous n'avons aucune borne à défendre — la date-butoir de la décision 1 est un horizon d'affichage, pas un refus ; ils sortent en `propstat 404` |
| événement | `getetag` (l'empreinte, comme le `card_hash` de 4c décision 9), `getcontenttype` = `text/calendar; charset=utf-8; component=VEVENT` — le paramètre `component` est celui que sabre écrit et que DAVx⁵ lit —, `getcontentlength` en octets, `getlastmodified` depuis `updated_at` en HTTP-date, `resourcetype` (vide), `current-user-privilege-set`, `supported-report-set` (`calendar-multiget` + `calendar-query`, que `REPORT` sert aussi sur une ressource, comme 4c le fait sur une carte) |
| rapports | `calendar-query`, `calendar-multiget`, `free-busy-query`, `sync-collection`, `expand-property` |
| filtres | `comp-filter` imbriqué `VCALENDAR/VEVENT`, `time-range`, `prop-filter`, `param-filter`, `text-match`, `is-not-defined` — et le `time-range` sur un `comp-filter VALARM`, qu'Apple exerce réellement |

**Les en-têtes `Allow` sont énumérés ici**, pour la raison que 4c donnait déjà : « conforme à
`OPTIONS` » ne se vérifie pas. Les trois listes de 4c restent ce qu'elles sont ; deux formes
s'ajoutent, et une seule gagne des verbes que le carnet n'a pas.

```
racine, principal, home de contacts   OPTIONS, PROPFIND, PROPPATCH, REPORT
carnet                                OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT          (4d)
fiche                                 OPTIONS, HEAD, GET, PUT, DELETE, PROPFIND, PROPPATCH, REPORT
home d'agendas                        OPTIONS, PROPFIND, PROPPATCH, REPORT, MKCALENDAR, MKCOL
agenda                                OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT
événement                             OPTIONS, HEAD, GET, PUT, DELETE, PROPFIND, PROPPATCH, REPORT
```

`MKCALENDAR` et `MKCOL` ne figurent que sur le home d'agendas, parce que c'est le seul endroit où
un agenda peut naître ; `DELETE` figure sur l'agenda parce qu'il y répond (décision 2), comme sur le
carnet depuis 4d. Tout ce qui n'est pas dans ces listes répond `405` avec l'en-tête, comme en 4c.

**Ce que 5c hérite de 4c sans le redire, et ce qu'il doit généraliser.** Les bornes d'entrée de la
décision 15 de 4c valent telles quelles pour `calendar-multiget` et `calendar-query` : 1 Mo par
corps de rapport (`413`), 5000 `href` au plus par `multiget` (`507
number-of-matches-within-limits`) — un multiget d'agenda est le même vecteur qu'un multiget de
carnet. `no-uid-conflict` s'applique tel que le code le fait **aujourd'hui**, pas tel que 4c
l'écrivait : un `UID` qui change sous son propre nom DAV est accepté depuis la seconde revue de 4c,
parce que DAVx⁵ ne renonce jamais sur un `403`. Et le routage `/dav` n'est pas prêt pour un home à
segment variable : `DavPaths.Parse` et les gabarits du contrôleur sont câblés sur les trois
segments `principals`/`addressbooks`/`default` en littéral. Généraliser ce parseur est un vrai
travail de 5c, à compter comme tel.

**Ce qu'un `PUT` vérifie, et le nom de chaque refus** — 4c avait sa décision 10, et un client ne
peut corriger que ce qu'on lui nomme (RFC 4791 § 5.3.2.1). Le fichier accepté est stocké
**verbatim**, sans normalisation d'aucune sorte : un `VTIMEZONE` manquant est accepté — sabre et
Radicale l'acceptent, Google l'omet dans ses exports — et c'est la première réécriture depuis le
webmail qui l'ajoute (décision 4).

| Cas | Réponse |
|---|---|
| le corps n'est pas `text/calendar`, ou `VERSION` n'est pas `2.0` | `403 CALDAV:supported-calendar-data` |
| le corps ne se parse pas, ou son `RRULE` ne s'expanse pas (décision 4) | `403 CALDAV:valid-calendar-data` |
| plusieurs `UID`, plusieurs `VEVENT` maîtres, un `VEVENT` sans `UID`, ou un composant qui n'est ni `VEVENT` ni `VTIMEZONE` à côté d'eux | `403 CALDAV:valid-calendar-object-resource` — un client écrit toujours l'`UID` ; seul l'import en insère un qui manque, comme 4a le fait pour une carte |
| un `VTODO`, un `VJOURNAL` ou un `VFREEBUSY` seul | `403 CALDAV:supported-calendar-component` (décision 3) |
| l'`UID` existe déjà sous un autre nom DAV du **même** agenda | `403 CALDAV:no-uid-conflict`, avec l'`href` de la ressource qui le porte — tel que le code de 4c le fait aujourd'hui (plus bas) |
| plus d'un mégaoctet | `403 CALDAV:max-resource-size` |
| plus de 10 000 occurrences dans l'année qui suit le `DTSTART` | `403 CALDAV:max-instances` (décision 4) |
| la cible n'est pas directement dans un agenda | `403 CALDAV:calendar-collection-location-ok` |
| `If-Match` / `If-None-Match` en désaccord | `412`, comme en 4c, et la révision `rejected` avec |

**L'ordonnancement n'est pas servi, et il faut le dire à la découverte plutôt que le laisser
deviner.** RFC 6638 fait du serveur le relais des invitations ; nous ne le sommes pas avant 5e.
L'en-tête `DAV:` ne porte donc ni `calendar-auto-schedule` ni `calendar-schedule`, et
`schedule-inbox-URL`, `schedule-outbox-URL` et `schedule-default-calendar-URL` sortent du principal
en `propstat 404`. La conséquence est celle qu'on veut : les clients d'Apple, qui ne proposent des
invités que si le serveur s'en charge, n'en proposent pas ; Thunderbird, qui envoie lui-même ses
courriels iTIP quand le serveur ne le fait pas, dépose des `ATTENDEE` que la décision 7 projette
et affiche. `calendar-user-address-set` reste servi sans le reste — il ne promet rien, il dit qui
est l'utilisateur, et un client qui le lit sans `schedule-inbox-URL` sait à quoi s'en tenir.

**Un `time-range` porte sur les occurrences, pas sur le maître** (RFC 4791 § 9.9) : un événement
récurrent correspond dès qu'**une** occurrence chevauche la fenêtre. C'est le moteur de la
décision 5, et c'est ce que « un seul moteur, deux consommateurs » veut dire concrètement — les
colonnes de première et dernière occurrence présélectionnent, l'expansion tranche.

**`free-busy-query` est servi, parce que le RFC 4791 § 7 l'exige.** Il aurait été la seule
divergence à un MUST de tout le projet, et elle ne valait pas d'être défendue : le rapport prend une
fenêtre et rend un `VFREEBUSY` des plages occupées de la collection, c'est-à-dire exactement ce que
le moteur d'expansion de la décision 5 produit déjà, trié sur la disponibilité que les colonnes
portent déjà (décision 1) — et trié comme la table du § 7.10 le dicte : `TRANSP:TRANSPARENT` et
`STATUS:CANCELLED` sortent, `STATUS:TENTATIVE` entre en `FBTYPE=BUSY-TENTATIVE`, le reste en
`BUSY` ; une journée entière suit son `TRANSP` comme les autres, et le webmail écrit le sien
(fonctionnalité 3). Il hérite des mêmes bornes que le reste (décision 4).

Il n'ouvre rien : le rapport ne répond qu'à l'utilisateur lui-même, sur ses propres agendas, comme
tout `/dav` depuis la décision 4 de 4c. Ce qui reste hors du projet, c'est le free/busy **entre
personnes** — interroger l'agenda d'un collègue pour y poser une réunion —, qui suppose un partage
que le modèle n'a pas et un écran que 5b ne dessine pas. `ccs-caldavtester` a une suite dédiée que
5d joue au lieu de l'écarter, et c'est le seul endroit du projet où couvrir le RFC coûte moins cher
que documenter pourquoi on ne le couvre pas.

**`CALDAV:expand` est servi ; `limit-recurrence-set` ne l'est pas, et `limit-freebusy-set` n'a rien
à limiter.** Les trois sont facultatifs au sens du RFC (§ 9.6). Le premier ne coûte rien ici — le
moteur d'expansion est déjà écrit et déjà appelé par le `time-range` — et il est ce qui permet à un
client sans moteur de récurrences d'afficher un agenda juste ; sa sortie a la forme que le § 9.6.5
fixe, un `VEVENT` par instance avec son `RECURRENCE-ID`, sans `RRULE` ni `VTIMEZONE`, toutes les
heures en UTC, et le plafond des 10 000 occurrences s'y applique comme partout. Le deuxième est une optimisation de
transfert pour des clients qui, eux, savent expanser : ne pas le servir leur rend simplement le
fichier entier. Le troisième ne borne que les composants `VFREEBUSY` **stockés**, dont la collection
n'en porte aucun — le `VFREEBUSY` que nous rendons est calculé par `free-busy-query`, pas une
ressource. Un corps qui nomme les deux derniers est donc ignoré et non refusé : ils changent la
taille d'une réponse, pas son sens.

## Les fonctionnalités du socle (5b)

1. **Vues** mois, semaine, jour, et liste « à venir » ; navigation précédent / suivant /
   aujourd'hui ; mini-mois de navigation dans la barre latérale. Les vues semaine et jour portent
   **un bandeau « journée entière » au-dessus de la grille** et **un trait de l'heure courante** :
   le premier parce qu'un événement sans heure n'a pas de place honnête dans une colonne d'heures —
   et la journée entière est un citoyen de première classe de la décision 5 —, le second parce que
   c'est ce qui fait lire « où on en est » sans chercher. Tous les agendas de référence ont les
   deux. Les événements qui se chevauchent se partagent la largeur de leur colonne, du plus tôt au
   plus tard — tranché sur les maquettes (§ L'interface). Reste à trancher sur capture
   **l'événement daté qui franchit minuit ou s'étale sur plusieurs jours** — une
   soirée de 22 h à 2 h, un salon de trois jours avec des heures : FullCalendar, donc Nextcloud,
   le monte dans le bandeau journée entière au-delà d'un seuil, SOGo le découpe colonne par
   colonne ; les deux rendus sont candidats, et 5b choisit sur capture. Le **numéro de semaine**
   s'affiche dans le mini-mois et en tête de chaque semaine de la vue mois — tous les agendas de
   référence l'ont, et un Belge compte en semaines ISO ; sa numérotation suit la région du
   navigateur comme le premier jour (fonctionnalité 8), ISO en repli.
2. **Agendas** colorés, affichables ou masquables d'une case à cocher ; création, renommage,
   couleur, suppression depuis la barre latérale. Un agenda créé depuis un téléphone y apparaît
   comme les autres, avec ce que le client a su régler ; c'est ici qu'on affine le reste
   (décision 2).
3. **Éditeur** : titre, agenda, journée entière, début et fin, répéter, rappel(s), lieu,
   description. Sous « Plus d'options » : disponibilité (Occupé / Provisoire / Libre), visibilité
   (Par défaut / Privé), adresse web, participants en lecture seule. Le pli existe pour qu'un
   événement simple se crée sans faire défiler. L'agenda présélectionné est le dernier utilisé
   dans ce navigateur, `default` la première fois.
   - « Rappel » écrit un `VALARM` à `ACTION:DISPLAY` et `TRIGGER` relatif au début (`-PT15M`) ;
     tout autre rappel venu d'un client (`ACTION:EMAIL`, déclencheur absolu ou relatif à la fin,
     `ACKNOWLEDGED`, `X-WR-ALARMUID`) s'affiche en texte et se conserve tel quel.
   - Une journée entière naît **Libre** (`TRANSP:TRANSPARENT`), comme les clients d'Apple
     l'écrivent : un congé ne bloque pas un free/busy, et l'utilisateur peut la passer Occupée.
   - « Répéter » propose Jamais / Tous les jours / Toutes les semaines / Tous les mois / Tous les
     ans / Personnalisé… ; le réglage personnalisé donne l'intervalle, les jours de la semaine, le
     mois par quantième ou par n-ième jour, et la fin (jamais / après N fois / à une date). C'est le
     sous-ensemble curé d'Apple et de Google ; une règle plus riche venue d'un client s'affiche en
     texte et se conserve.
   - « Disponibilité » fusionne `STATUS` et `TRANSP` en un seul champ : Provisoire s'écrit
     `STATUS:TENTATIVE`, Libre `TRANSP:TRANSPARENT`, Occupé l'absence des deux. Pas de statut
     « Annulé » : on supprime.
   - En lecture, le badge suit une priorité : `STATUS:TENTATIVE` → Provisoire ; sinon
     `TRANSP:TRANSPARENT` → Libre ; sinon Occupé, `STATUS:CONFIRMED` (le défaut d'Apple et de
     Google) compris. Un `STATUS:CANCELLED` venu d'un client s'affiche barré et compte Libre ;
     l'enregistrer depuis le webmail écrit la valeur choisie et retire le statut.
   - Sur un événement récurrent, Enregistrer, Supprimer et le glisser-déposer demandent d'abord
     « Cette occurrence seulement / Celle-ci et les suivantes / Toutes les occurrences »
     (décision 5).
4. **Glisser-déposer** pour déplacer, **redimensionnement** pour changer la durée, sur les vues
   semaine et jour ; glisser sur une case vide crée un événement.
5. **Recherche** par texte sur titre, lieu, description. Un résultat est un événement, pas une
   occurrence : un récurrent apparaît une fois, à sa prochaine occurrence, ou à la dernière s'il
   est fini.
6. **Import / export** `.ics`, par agenda. À l'import, **c'est le regroupement par `UID` qui
   fabrique la ressource** : le `VEVENT` maître, ses composants à `RECURRENCE-ID` et les
   `VTIMEZONE` qu'ils citent forment une ligne, quel que soit leur ordre dans le fichier — un
   fichier d'agenda est un seul `VCALENDAR` de centaines de composants, pas une suite de
   ressources. Le réimport est idempotent comme en 4a, mais **par remplacement, pas par fusion** :
   un `UID` déjà présent dans l'agenda désigne le même événement et la ressource importée prend la
   place de l'ancienne, entière — 4a fusionne champ par champ et n'écrase jamais, ce qui a un sens
   pour une fiche à cent champs indépendants et n'en a aucun pour un `VCALENDAR` dont la règle,
   les exceptions et les rappels forment un tout. Un `VTODO` ou un `VJOURNAL` est **ignoré et
   compté**, troisième issue que 4a n'avait pas ; une ressource au-delà du plafond, au-delà des
   10 000 occurrences (décision 4) ou qui n'expanse pas est une ligne en erreur. Les plafonds sont
   le 1 Mo par ressource de 4a, 5000 ressources **par agenda** (4a bornait par utilisateur ; avec
   vingt agendas, la borne par utilisateur vaut donc 100 000, et c'est assumé), et **20 Mo pour le
   fichier importé**, le même que la route d'import des contacts — sans lui, un plafond par
   ressource ne borne rien. L'export est la symétrie exacte : un seul `VCALENDAR`, tous les
   événements de l'agenda, les `VTIMEZONE` dédupliqués en tête, et le nom et la couleur de
   l'agenda en `NAME` / `COLOR` (RFC 7986) doublés de `X-WR-CALNAME` / `X-APPLE-CALENDAR-COLOR`,
   que Google, Apple et Nextcloud lisent. L'import lit les mêmes lignes dans l'autre sens : le
   dialogue propose « dans un agenda existant » ou « dans un nouvel agenda », et pré-remplit le
   second avec ce que le fichier porte — c'est ce que fait Nextcloud, et ça ne coûte que la lecture
   de quatre propriétés déjà parsées.
7. **Rappels** stockés dans le fichier et synchronisés — c'est le téléphone qui sonne. Aucune
   notification dans le webmail pour l'instant.
8. **Localisation** : deux sources, parce que le produit n'a pas de locale de compte. Ce qu'il a,
   c'est une **langue d'interface** (`ui.language` : `auto`, `en`, `fr`), et les dates du courrier
   passent par `Intl` avec cette langue nue. Les noms de mois et de jours la suivent — un mois en
   anglais sous une interface en français serait le défaut le plus visible de l'écran. Mais une
   langue nue ne dit ni le premier jour de la semaine ni le format horaire : `fr` rend lundi et
   24 h, `en` nu rend **dimanche et 12 h**, et un Belge en interface anglaise aurait sa semaine au
   dimanche. Le premier jour et le format horaire viennent donc de la **région du navigateur**
   (`navigator.language`, `en-BE` ou `en-GB`), par `Intl.Locale.getWeekInfo` là où il existe et
   par une table par région sinon, lundi en repli. Le croquis ci-dessous montre lundi parce que
   c'est ce que rend un navigateur belge, pas parce que c'est figé. Aucune préférence n'est
   ajoutée tant qu'un cas réel n'en réclame pas une, comme pour le fuseau (décision 6).

## L'interface

Le bandeau et le rail sont ceux du webmail ; l'entrée « Agenda » du rail existe déjà
(`ComingSoon`). Le module construit ses colonnes dans son outlet, comme le courrier et les contacts.

**Les maquettes de référence sont validées et font foi pour 5b** :
<https://claude.ai/code/artifact/e0f2b333-7491-4ac1-9229-8ac31a45f2c7> — dix planches (semaine et
mois sur grand écran, bulle d'aperçu, éditeur, question de portée d'un récurrent, cinq écrans de
téléphone, onglet Sync), dessinées sur les valeurs réelles du webmail. Les croquis ASCII ci-dessous
ne sont qu'un rappel ; en cas d'écart, la maquette gagne. Ce qu'elles fixent, et que 5b ne
rediscute pas :

- **Grille horaire** : 56 px par heure, demi-heure en trait plus clair, 07:00–18:00 visibles à
  l'ouverture (le reste défile), gouttière de 56 px avec le numéro de semaine en tête, bandeau
  journée entière sur `--surface-sunken`, colonne du jour teintée à 4 % de l'accent et trait de
  l'heure courante en `--accent-unread` avec un point sur la colonne du jour, ombre du trait sur
  les autres colonnes.
- **Quatre rendus d'événement**, tous à barre gauche de 3 px dans la couleur de l'agenda : occupé
  (fond teinté à 18 % de la couleur), libre (contour, fond `--surface`), provisoire (hachures à 45°),
  annulé (contour neutre, texte barré et atténué). Titre en 600, heure puis lieu en `--text-muted`
  dès que la hauteur le permet (40 px, puis 58 px).
- **Chevauchement** : partage égal de la largeur de la colonne, du plus tôt au plus tard. Tranché
  sur la maquette, plus sur capture.
- **Agendas de la barre latérale** : une case à cocher **colorée** — carré de 16 px de la couleur
  de l'agenda, coche blanche quand il est affiché, contour seul quand il est masqué —, et non la
  case native à `accent-color` du carnet. C'est un écart volontaire avec les contacts : la couleur
  est l'information, la case ne fait que la porter. Un kebab à droite ouvre renommer / couleur /
  supprimer.
- **Barre d'outils** : « Today » en bouton fantôme, deux chevrons de 36 px, titre en 15 px 650
  suivi de « Week 38 » en 12 px muet, champ de recherche de la largeur du carnet, sélecteur de
  vue en `.seg` (Day / Week / Month / List).
- **Bulle d'aperçu** : le style du menu déroulant (`--surface`, bordure, ombre `0 8px 24px`,
  rayon 4), 300 px, ancrée à droite de l'événement, pastille de couleur, titre 15 px 650, date et
  heure, puis lieu, rappel, récurrence, agenda avec leur icône, et deux boutons — Modifier en
  primaire, Supprimer en fantôme.
- **Vue mois** : 5 ou 6 rangées de hauteur égale, numéro de semaine dans une gouttière de 36 px,
  jours hors mois sur `--surface-sunken`, journée entière en puce pleine, événement daté en point
  de couleur + heure muette + titre, « +N more » au-delà de trois.
- **Éditeur** : la modale maison (`.modal`, 560 px, rayon 4, `.field-h` à libellés de 110 px en
  capitales), champs dans cet ordre : Title, Calendar (sélecteur à pastille), All day (interrupteur),
  Start et End (date + heure), Repeat (sélecteur, résumé en muet à droite), Reminder (sélecteur +
  « + Add »), Location, Description ; un filet, puis « More options » à chevron : Availability et
  Visibility en `.seg`, URL, Attendees en lecture seule avec la note « Read only until invitations
  are supported ». Pied : Delete en `.btn-danger` à gauche, Save en primaire à droite, **pas de
  bouton Annuler** — la ✕ ferme, règle maison.
- **Portée d'un récurrent** : une petite modale « Save a recurring event », une phrase, trois
  boutons empilés — This occurrence only (primaire), This and following occurrences, All
  occurrences.
- **Téléphone** : sélecteur Month / Day / List en `.seg` compact dans la barre d'outils, à côté du
  ☰ ; mois compact à jour sélectionné plein et jusqu'à trois points de couleur, liste du jour tapé
  en dessous (heure de début et de fin sur deux lignes, barre de couleur, titre, lieu) ; jour à
  bande de 7 jours ; liste « à venir » groupée par jour avec « Today · » et « Tomorrow · » ; tiroir
  de 320 px reprenant la barre latérale ; éditeur plein écran avec Save dans l'en-tête et ✕ à
  droite, champs en 16 px et 44 px de haut.
- **Onglet Sync** : second interrupteur « Calendar (CalDAV) » sous « Contacts (CardDAV) », mêmes
  trois valeurs, et une phrase de pied sur iOS (deux comptes) et DAVx⁵ (un seul).
- **Icônes manquantes au jeu maison**, à dessiner sur la même grille (24, trait 1.9) : cloche,
  répétition, horloge, plus, fermer.

### Grand écran : deux colonnes

```
┌──────┬──────────────┬─────────────────────────────────────────────────────┐
│ rail │ mini-mois    │ Aujourd'hui ◀ ▶  14 – 20 septembre 2026   [J S M L] [+]│
│      │              ├──────┬──────┬──────┬──────┬──────┬──────┬──────┬──────┤
│      │ ☑ Personnel  │      │lun 14│mar 15│mer 16│jeu 17│ven 18│sam 19│dim 20│
│      │ ☑ Travail    │j.ent.│      │      │▒▒▒▒▒▒ Congé ▒▒▒▒▒▒▒│      │      │
│      │ ☐ Famille    ├──────┼──────┼──────┼──────┼──────┼──────┼──────┼──────┤
│      │              │  9h  │▒Dent.│      │▒Point│      │      │      │      │
│      │              │ 10h  │      │      │      │▒Atel.│      │      │      │
│      │              │ 11h  │╌╌╌╌╌╌┼╌╌╌╌╌╌┼╌╌╌╌╌╌┼╌╌╌╌╌╌┼╌╌╌╌╌╌┼╌╌╌╌╌╌┼╌╌╌╌╌╌│
│      │              │ 12h  │      │▒Banq.│      │      │      │      │      │
└──────┴──────────────┴──────┴──────┴──────┴──────┴──────┴──────┴──────┴──────┘
```

La bande `j.ent.` porte les événements sans heure, le trait pointillé est l'heure courante.

Barre latérale à gauche (mini-mois, liste des agendas), grille à droite avec sa barre d'outils.
**Pas de troisième colonne** : contrairement au courrier et aux contacts, garder la grille lisible
pendant qu'on édite n'a pas d'intérêt fonctionnel, et un panneau de détail y serait à l'étroit.

**Au clic sur un événement, une bulle d'aperçu** ancrée à l'événement : titre, horaire, lieu,
rappel, agenda, et deux boutons (Modifier, Supprimer). L'éditeur complet ne s'ouvre qu'au second
geste — « Modifier » ou double-clic — dans une fenêtre par-dessus la grille. Consulter est le geste
le plus fréquent, et une bulle ne masque pas la semaine.

### Téléphone (sous 640 px)

Trois vues, toutes accessibles par le sélecteur de la barre d'outils :

- **Mois compact + liste du jour choisi**, la vue par défaut : le mois avec un point de couleur par
  événement, et sous lui la liste des événements du jour tapé. D'un coup d'œil le mois et le détail
  d'un jour.
- **Jour avec bande de 7 jours** en haut : grille horaire du jour, la bande se glisse pour changer
  de semaine.
- **Liste « à venir »** : les événements des prochains jours groupés par jour, sans grille.

Le bouton ☰ ouvre un **tiroir** à gauche avec ce que la barre latérale montrait (mini-mois, agendas
à cocher), comme dans le courrier et les contacts. L'éditeur prend **tout l'écran**, comme la fiche
contact ; le bouton flottant « + » crée un événement.

### Paramètres

L'onglet « Sync » de 4c gagne un second interrupteur, « CalDAV », à côté de « CardDAV » : même
secret, même adresse, même identifiant. `dav_credentials` est une ligne par utilisateur au secret
partagé, avec une colonne `carddav_enabled` ; la décision 19 de 4c avait prévu qu'un second
protocole serait « une seconde ligne dans le même panneau et une seconde colonne, pas une
migration ». La colonne `caldav_enabled` **n'existe pas encore** : c'est un DDL de 5c, et
`webmail-carddav-tables.md` s'amende avec. L'interrupteur hérite du gate de l'onglet — compte
principal, `capabilities.dav`, et l'onglet absent sans `Dav__PublicUrl`. L'adresse affichée ne
change pas : c'est la même racine `/dav`, et `/.well-known/caldav` fait le reste (décision 8).

**`caldav_enabled` naît à `0`, à l'inverse de `carddav_enabled`, et les deux défauts disent la même
chose.** Le défaut de 4c décrit la ligne qu'un utilisateur vient de créer en allumant CardDAV ; le
DDL de 5c, lui, s'applique à des lignes **existantes**, dont les propriétaires n'ont rien demandé et
dont le téléphone est déjà appairé — un défaut à `1` allumerait CalDAV chez tous sans qu'aucun
navigateur n'ait fourni le fuseau de `default` (décision 6). Corollaire : **le code pose toujours
les deux colonnes explicitement** à la création d'une ligne, quel que soit l'interrupteur qui la
crée — sinon allumer CalDAV en premier allumerait CardDAV par le défaut du DDL. L'allumage de
CalDAV porte le fuseau du navigateur et crée `default` dans la même transaction, s'il n'existe pas.

**Deux interrupteurs, un seul schéma d'authentification, et c'est la ressource qui juge.** En 4c
le `403` « éteint » est rendu par le schéma lui-même, après comparaison du condensat ; il ne connaît
pas le chemin. Désormais le schéma ne refuse que si **les deux** colonnes sont à `0` — toujours
après le condensat, pour la raison de 4c décision 2 — et porte les deux drapeaux dans l'identité
résolue ; le contrôleur applique le bon selon la ressource : `carddav_enabled` sous
`/dav/addressbooks/`, `caldav_enabled` sous `/dav/calendars/`, et `403` sans lecture dans le cas
contraire. Les ressources communes — `/`, `/dav/`, le principal — répondent dès qu'un des deux est
allumé, et le principal ne rend `calendar-home-set` que si CalDAV l'est (`addressbook-home-set` de
même pour CardDAV) : un `propstat 404` sur le home-set, et DAVx⁵ ne cherche pas d'agendas là où
l'utilisateur les a éteints, sans que ses contacts cessent de se synchroniser.

## Ce que le projet ne fait pas

- Pas de tâches (`VTODO`), pas de journal (`VJOURNAL`).
- Pas de free/busy **entre personnes** : le rapport `free-busy-query` est servi sur ses propres
  agendas parce que le RFC l'exige (décision 8), mais rien n'interroge l'agenda d'un autre — il n'y
  a pas de partage, et aucun écran ne pose de réunion.
- Pas d'invitations envoyées ni traitées avant 5e, et pas d'ordonnancement RFC 6638 : ni
  `calendar-auto-schedule` dans l'en-tête, ni boîte d'entrée ou de sortie sur le principal
  (décision 8).
- Pas de partage d'agenda entre utilisateurs, pas d'ACL.
- Pas de `MOVE` ni de `COPY` (Nextcloud les sert ; iOS, DAVx⁵ et Thunderbird déplacent par
  `DELETE` + `PUT`), et pas de pièces jointes gérées (RFC 8607, un `POST` sur la ressource, qu'iOS
  n'exerce que si le serveur l'annonce) : `405` avec l'`Allow` de la décision 8, comme tout verbe
  absent des listes.
- Pas de collection WebDAV ordinaire dans le home d'agendas : `MKCOL` n'y crée qu'un agenda, et
  seulement s'il le déclare (décision 2).
- Pas de renommage de l'adresse d'un agenda après coup : le segment d'URL est fixé à la création,
  comme le nom DAV d'un événement (4c, décision 5) — le changer ferait re-télécharger l'agenda à
  tous les appareils et leur ferait perdre ce qu'ils gardent en propre dessus. Le nom **affiché**,
  lui, se change des deux côtés et n'y touche pas. Qui veut vraiment une autre adresse supprime et
  recrée.
- Pas de notification de rappel dans le webmail ; le téléphone sonne.
- Pas de corbeille avec restauration, là où Nextcloud en a une : les révisions de cause `delete`
  en gardent la matière, sans écran, et une tranche ultérieure pourra l'ouvrir.
- Pas d'agenda dérivé (anniversaires) ni d'abonnement externe (webcal) dans les tranches 5a–5d.
- Pas de préférence de fuseau horaire dans les paramètres, et pas de fuseau par événement dans
  l'éditeur — Nextcloud, SOGo et Thunderbird l'offrent, mais un agenda personnel se tient presque
  toujours dans un seul fuseau et le champ coûterait une ligne de plus dans un éditeur dont le pli
  existe pour qu'on n'y défile pas. Un `TZID` venu d'un client est conservé et affiché. À ne pas
  confondre avec le fuseau **de l'agenda**, qui existe et que la décision 6 impose.
- Pas d'affichage des catégories (`CATEGORIES`), de la priorité ni des pièces jointes (`ATTACH`) :
  conservées dans le fichier, jamais montrées, là où Nextcloud et SOGo les affichent.
- Pas de moteur de récurrences maison.

## Ce que 5a doit trancher en premier

Dans l'ordre, parce que chacun conditionne le suivant :

1. La vérification d'Ical.Net : lecture → écriture sur quatre fichiers réels, Outlook et son
   `TZID` Windows compris, **et** l'écriture d'un cinquième depuis le webmail, `VTIMEZONE` compris
   (décision 4).
   **Tranché** — `.superpowers/sdd/2026-09-04-webmail-calendar-5a-foundations/task-1-report.md`
   (sondes, § 4) et `task-2-report.md` (corpus, § 3 et 5) ; le verdict et ce qu'Ical.Net 5.2.3 fait
   réellement sont repris dans `docs/superpowers/calendar-5a-residuals.md`, § « Ce que les sondes
   ont appris d'Ical.Net 5.2.3 ».
2. Le schéma : `calendars` — propriétaire, nom, description, couleur, ordre, **fuseau**
   (décision 6), nom DAV, affiché/masqué (décision 2) —, `calendar_events` et sa table fille des participants, avec les colonnes
   de la décision 1, l'unicité **`(calendar_id, uid)`**, la date-butoir des récurrences sans fin,
   les exceptions sans maître, et les colonnes DAV de 4c (`dav_name`, `sync_sequence`,
   empreinte) ; l'état de synchro et les tombes **par agenda**, les révisions **par utilisateur**
   (décision 2). Et, comme 4a l'avait fait, la liste des colonnes qui **échappent** à la
   projection et le sort de l'`UID` (figé à la première lecture, ou relu).
   **Tranché** — `docs/superpowers/webmail-calendar-tables.md` (le DDL et les écarts assumés) et
   `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs` (le mapping EF).
3. Le contrat de l'API des occurrences : la fenêtre, son fuseau `tz` et sa borne de cinq ans,
   la forme d'une occurrence, et l'identification d'une occurrence pour « celle-ci seulement » —
   qui porte la valeur littérale du `RECURRENCE-ID` à écrire, pas l'instant UTC (décision 5).
   **Tranché** — `src/snoopy.microservice/Controllers/CalendarEventsController.cs` (la route
   `GET /api/Calendar/Events`, la borne des cinq ans via `OccurrenceExpander.MaxYears`) et
   `src/snoopy.microservice/Models/Calendar/EventOccurrence.cs` (la forme, `InstanceId` littéral).
4. La forme canonique qui décide qu'une réécriture n'a rien changé, et les estampilles
   (`DTSTAMP`, `LAST-MODIFIED`, `SEQUENCE`) d'une réécriture qui a changé quelque chose
   (décision 4) — avant l'éditeur de 5b, parce que c'est lui qui en dépend.
   **Tranché** — `src/snoopy.microservice/Services/Calendar/IcsComposer.cs` (`Canonical`,
   `SameContent`, `Stamp`), voir `task-4-report.md`.
