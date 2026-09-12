# Agenda 5e — les invitations

Cinquième tranche du projet Agenda, celle que le cadrage avait laissée « envisagée », à la suite
de [5a](2026-09-04-webmail-calendar-5-overview-design.md#découpage),
[5b](2026-09-05-webmail-calendar-5b-screens-design.md),
[5c](2026-09-06-webmail-calendar-5c-caldav-design.md) et
[5d](2026-09-07-webmail-calendar-5d-conformance-design.md).

## Où en est le projet

| | Tranche | État |
|---|---|---|
| 5a | Fondations : modèle, moteur iCalendar, stores, API | livrée |
| 5b | Les écrans de l'agenda dans le webmail | livrée |
| 5c | Serveur CalDAV : découverte, agendas multiples, cinq rapports, filtres, tombes, historique | livrée |
| 5d | Conformité : `ccs-caldavtester`, correctifs, clients réels | livrée, close |
| **5e** | **Les invitations, en deux phases : recevoir (5e1), puis inviter (5e2)** | 5e1 livrée ; 5e2 à planifier |

5d est close. 5e2 ajoute au serveur une réaction aux écritures CalDAV, la première fois qu'il
fait autre chose que stocker un `PUT` ; elle a son propre scénario client, dans cette tranche
(voir Tests).

## Ce que fait la tranche

Une invitation d'agenda, c'est un mail qui porte, en plus de son texte, un fichier calendrier
(`text/calendar`) décrivant un rendez-vous, avec une **méthode** qui dit ce que l'expéditeur
attend : `REQUEST` (« viens-tu ? »), `REPLY` (« oui / peut-être / non »), `CANCEL` (« c'est
annulé »). C'est le format qu'échangent Google, Outlook, Apple, Thunderbird et tout le reste
(iTIP, RFC 5546, transporté par mail, iMIP, RFC 6047). Aujourd'hui le webmail voit ce fichier
comme une pièce jointe muette.

**Phase 1, 5e1 — recevoir.** Le côté invité, qui est ce dont un particulier a besoin : les
invitations viennent d'un employeur, d'un médecin, d'une salle de sport, d'un ami sur Gmail.
Le lecteur reconnaît le fichier, affiche un encart lisible (quand, où, qui organise, qui est
invité) et trois boutons, Accepter / Provisoire / Refuser. Un clic enregistre le rendez-vous dans
l'agenda **tel que l'organisateur l'a écrit** et envoie la réponse par mail. Une mise à jour ou
une annulation reçue met l'agenda à jour, toujours derrière un bouton. Rien n'entre dans l'agenda
sans clic.

**Phase 2, 5e2 — inviter.** Le côté organisateur, au minimum utile : un champ « Invités » dans
l'éditeur, un mail d'invitation à l'enregistrement, une mise à jour quand ce qui compte change,
une annulation à la suppression. La règle est portée par le serveur, pas par l'écran : un
rendez-vous que le webmail a invité est tenu à jour **d'où que vienne la modification**, y
compris d'un iPhone synchronisé en CalDAV, parce que le serveur envoie alors le mail lui-même
par un compte de service SMTP. Les réponses des invités sont lues quand on ouvre leur mail.

**Ce que 5e n'est pas.** Le relais d'invitations au sens de la RFC 6638 (`calendar-auto-schedule`),
où le serveur annonce aux téléphones qu'il gère les invitations, envoie et lit les mails en
arrière-plan et propose un champ Invités dans l'application Calendrier de l'iPhone. C'est le
modèle de Google, d'iCloud, de Nextcloud et de SOGo ; c'est aussi la pièce la plus lourde de
tout le sous-projet, et elle exigerait une campagne de conformité à elle seule. 5e2 pose ce
qu'il faut pour y arriver un jour (l'envoi hors session par le compte de service, la règle
d'empreinte) sans le construire. Voir « Ce que la tranche ne fait pas ».

## Ce qui a été écarté, et pourquoi

Quatre choix ont été faits en brainstorming, contre l'usage des grands agendas, et il faut
pouvoir les relire.

- **L'invitation n'entre pas dans l'agenda avant qu'on ait cliqué.** Google et Outlook posent
  l'invitation dans l'agenda dès réception, grisée, avant toute réponse. Ici, rien ne bouge sans
  clic : pas de balayage de la boîte de réception, pas de rendez-vous fantôme. Un mail non lu
  est un rendez-vous invisible, et c'est assumé.
- **L'état des invités d'un rendez-vous reçu n'est pas affiché.** Le fichier reçu porte bien un
  état par invité (`PARTSTAT`), mais c'est l'état **au moment où l'organisateur a envoyé** : sur
  une invitation nouvelle, personne n'a encore répondu, et rien ne nous informe des réponses
  ultérieures des autres, qui vont à l'organisateur. Afficher cette colonne, c'est afficher
  presque toujours du vide ou du faux. Les invités sont des noms ; seule la réponse de
  l'utilisateur est montrée. La donnée reste projetée en base comme depuis 5a.
- **Le mail répondu ne quitte la boîte que sur un refus.** Outlook retire par défaut toute demande
  de réunion une fois répondue. Ici une acceptation laisse le mail en place, parce que le
  rendez-vous dans l'agenda suffit à se souvenir de la réponse et que l'encart peut alors la
  rappeler et la changer d'un clic ; un refus part, parce que rien d'autre ne le mémoriserait.
  Boîte un peu moins nette, règle un peu moins uniforme, sept états de maquette réellement
  atteignables (décision 4, arbitré le 12 septembre 2026).
- **Pas de niveau intermédiaire « le webmail envoie, le téléphone se tait ».** La première
  version du côté organisateur laissait un rendez-vous déplacé depuis l'iPhone sans mail tant
  qu'on ne revenait pas dans le webmail cliquer « prévenir les invités ». Un oubli, ou ne pas
  connaître ce fonctionnement, et les invités ne sauraient jamais. Le compte de service SMTP
  (testé le 12 septembre 2026 sur le 587 de `mail.weesky.be`, avec `sender_login_maps` étendu)
  ferme ce trou : c'est ce qui fait de 5e2 un « 2 bis » et non un « 2 ».

## Les maquettes

Le canevas [Invitation d'agenda dans le lecteur](https://claude.ai/code/artifact/4be5d67a-c420-47eb-b64e-b76fc1a85b73)
porte, page « Retenue », l'encart choisi (proposition 2b : la carte de l'aperçu d'événement,
sur le fond enfoncé des bandeaux du lecteur, avec les lignes libellées Quand / Où / Organisateur
/ Invités) dans ses sept états, et page « Explorations » les cinq propositions de départ et les
variantes comparées. Fichiers de travail sous `.superpowers/design/invitation-encart/`.

**L'état 4 est à redessiner** (arbitré le 12 septembre 2026, décision 3) : il ne porte plus un
bouton « Mettre à jour mon agenda » mais les trois réponses, la précédente surlignée, comme
Google le fait sur une invitation modifiée.

## Décisions — 5e1, recevoir

### 1. Le serveur lit l'invitation, le lecteur l'affiche

Le détail d'un message (`GET /api/Mail/Messages/Detail`) gagne un bloc optionnel `invitation`.
Il est rempli quand le message porte une partie `text/calendar` (ou une pièce `.ics`) dont le
`METHOD` est `REQUEST` ou `CANCEL`, et vide sinon. `REPLY` est ignoré en 5e1 (on n'organise
rien) et pris en charge en 5e2 (décision 12) ; les autres méthodes (`ADD`, `REFRESH`,
`COUNTER`, `DECLINECOUNTER`, `PUBLISH`) sont ignorées et le fichier reste une pièce jointe.
S'il y a plusieurs parties calendrier, la première qui porte une méthode traitée gagne.

**Où est la partie.** Google, Outlook et Apple ne joignent pas l'invitation comme un fichier :
ils la glissent dans le `multipart/alternative` du corps, à côté du texte et du HTML, sans nom de
fichier ni `Content-Disposition`, et joignent *en plus* un doublon `invite.ics`. Le lecteur ne
liste aujourd'hui que les pièces jointes (`MailMessageMapper.IsListedPart`) et ne télécharge que
les parties texte : il ne verrait ni l'une ni l'autre. Le détail parcourt donc le `BODYSTRUCTURE`
entier et retient toute partie `text/calendar`, `application/ics`, ou nommée `*.ics`, listée ou
non ; celle qui gagne est téléchargée à part, seulement si sa taille annoncée passe
`IcsGuards.MaxIcsBytes`. Une seule partie est lue par message, jamais toutes.

Le serveur lit la partie avec Ical.Net et **les mêmes garde-fous que pour un fichier déposé par
un téléphone** (`IcsGuards.CheckAll` : taille, fuseaux, échappements, surcharges, densité de
répétition). Un fichier qui ne passe pas ne casse pas le mail : le bloc porte seulement
`unreadable: true` et un motif, l'encart dit « Invitation illisible » et la pièce jointe reste
téléchargeable.

Le bloc :

| champ | ce qu'il dit |
|---|---|
| `method` | `Request` ou `Cancel` |
| `uid`, `sequence` | l'identité et la version du rendez-vous, telles que le fichier les porte |
| `summary`, `start`, `end`, `isAllDay`, `location`, `repeats` | ce que l'encart affiche ; `start`/`end` en UTC comme les occurrences de l'API (des dates seules pour une journée entière) ; `repeats` est vrai si le maître porte une `RRULE` ou des `RDATE` |
| `organizer` | `{ email, name? }`, depuis `ORGANIZER` ; absent si le fichier n'en a pas |
| `attendees` | `[{ email, name? }]`, sans état (voir « Ce qui a été écarté ») |
| `addressedTo` | l'adresse de l'utilisateur trouvée parmi les `ATTENDEE`, ou absent (décision 2) |
| `filePartStat` | la réponse que porte **le fichier reçu**, si `addressedTo` est présent : `NEEDS-ACTION` sur une invitation nouvelle, la réponse que l'organisateur a enregistrée sur un renvoi |
| `savedPartStat` | la réponse que porte **le fichier en base**, donc celle que l'utilisateur a donnée ; absente quand le rendez-vous n'est pas dans l'agenda (décision 3) |
| `inCalendar` | l'état dans l'agenda (décision 3) |
| `occurrenceOnly` | vrai quand le fichier ne vise qu'une date d'une série (décision 1 bis) |
| `part` | le spécificateur MIME de la partie, la clé que la réponse renvoie (décision 4) |

Deux champs pour la réponse et non un : sur le cas courant ils diffèrent, le fichier reçu disant
`NEEDS-ACTION` là où la base dit `ACCEPTED`. C'est `savedPartStat` que l'encart rappelle
(« Vous avez accepté »), `filePartStat` ne servant qu'à reconnaître une invitation que
l'organisateur renvoie avec la réponse déjà dedans : quand il vaut autre chose que
`NEEDS-ACTION` sur un rendez-vous `absent`, la phrase de contexte de l'encart dit
« L'organisateur vous a noté comme ayant accepté », et les boutons restent, parce que la base
ne porte rien de cette réponse-là (elle a pu être donnée depuis un autre agenda, ou l'ancien).

Le même bloc est le retour du point d'entrée de réponse, pour que l'encart se redessine sans
recharger le mail.

### 1 bis. Une invitation qui ne vise qu'une date

Un organisateur peut n'annuler ou ne déplacer **qu'une occurrence** d'une série : « la réunion
du 12 octobre est annulée », « celle du 19 passe à 15 h ». Le fichier porte alors un
`RECURRENCE-ID` et ne décrit pas la série, seulement la date visée.

**5e1 l'affiche et n'y touche pas.** Le bloc porte `occurrenceOnly: true`, l'encart dessine
normalement le titre, la pastille, Quand / Où / Organisateur / Invités, et son pied est celui de
l'état 6 : une phrase, aucun bouton — « Cette invitation ne porte que sur une date de la série.
Corrigez le rendez-vous dans votre agenda. » Le point d'entrée de réponse refuse un tel fichier
par un `400`.

Pourquoi ne pas le traiter : appliquer cette date supposerait de **fusionner** le fichier reçu
avec celui déjà en base, alors que tout le reste de 5e1 se contente de stocker le fichier de
l'organisateur tel quel, octet pour octet. C'est le seul endroit de la tranche qui demanderait
une fusion, et la faire à moitié détruit la série : déposer le fichier reçu sous le nom de la
série remplacerait les dates par la seule surcharge, et supprimer la ressource sur un
`CANCEL` d'occurrence effacerait toute la série au lieu d'une date. Afficher sans agir ne perd
rien et ne trompe personne. Écrit dans « Ce que la tranche ne fait pas ».

### 2. Qui est l'utilisateur dans le fichier

L'adresse de l'utilisateur est cherchée parmi les `ATTENDEE` du composant maître — ou du seul
composant du fichier quand il ne vise qu'une date, décision 1 bis —, dans la liste
des adresses **du compte de messagerie où le mail a été lu** (celui que tout point d'entrée mail
reçoit par `X-Account-Id`) :

- le compte principal : la liste que 5c publie déjà comme `calendar-user-address-set`
  (décision 5 de 5c), l'adresse principale, les adresses sur les autres domaines du compte, et
  les identités d'envoi. Elle vit aujourd'hui dans une méthode privée de
  `DavPrincipalController` ; elle est extraite dans un service `UserAddresses` que le principal
  DAV et le lecteur d'invitations appellent tous deux, sans changer ce que le principal publie ;
- un compte connecté (Gmail, Outlook.com…) : son adresse de connexion et les identités
  enregistrées pour lui, exactement la règle que `OutgoingMessageFactory` applique déjà pour
  autoriser un `From` sur ce compte.

La comparaison ignore la casse et le préfixe `mailto:`. La première qui correspond devient
`addressedTo` : c'est l'adresse qui portera la réponse (décision 5), parce qu'une réponse
envoyée depuis une autre adresse que celle invitée est rejetée ou ignorée par l'agenda de
l'organisateur. Le rendez-vous, lui, entre toujours dans les agendas de l'utilisateur, quel que
soit le compte où l'invitation est arrivée : il n'y a qu'un agenda par utilisateur, pas un par
boîte.

Sans correspondance, l'invitation est **transférée** : soit quelqu'un a fait suivre le mail,
soit elle visait une adresse que le serveur ne connaît pas comme celle de l'utilisateur. Dans ce
cas, répondre n'a pas de sens ; l'encart propose seulement « Ajouter à l'agenda » (état 3 de la
maquette), et le fichier est enregistré sans toucher à sa liste d'invités.

### 3. L'état dans l'agenda : `UID` et `SEQUENCE`

Le serveur cherche le `UID` du fichier dans les événements de l'utilisateur, tous agendas
confondus, par `(user_id, uid)`. L'index unique de 5a est `(calendar_id, uid)` : il ne sert pas
cette recherche, et sans index elle balaierait tous les événements de l'utilisateur à chaque
ouverture d'invitation. 5e1 ajoute donc `ix_calendar_events_user_uid (user_id, uid)` (voir Le
schéma). Le `UID` étant unique par agenda et non par utilisateur, la recherche peut rendre
plusieurs lignes ; celle de l'agenda par défaut gagne (celui que 5a reconnaît à son nom DAV et
que l'API publie comme `isDefault`), sinon la première dans l'ordre de la barre latérale, et
`calendarId` dit laquelle. La ligne retenue livre aussi son `dav_name`, qui est **le nom sous
lequel la réponse écrira** (décision 4). La `SEQUENCE` n'est pas une colonne : elle est lue dans
`ics_raw` de la ligne trouvée, ce qui coûte une analyse Ical.Net par ouverture d'invitation
déjà enregistrée, et rien pour les autres. Le résultat, `inCalendar` :

| valeur | ce que ça veut dire | ce que l'encart montre |
|---|---|---|
| `absent` | jamais enregistré | état 1 (ou 3 si transférée) ; pour un `CANCEL`, état 6 « Ce rendez-vous n'est pas dans votre agenda », sans bouton |
| `current` | présent, même `SEQUENCE` | état 2 « Vous avez accepté · dans Personnel », les deux autres réponses en boutons discrets |
| `outdated` | présent, `SEQUENCE` reçue plus grande | état 4 « L'organisateur a modifié ce rendez-vous », et **la question est reposée** : les trois réponses, la précédente (`savedPartStat`) surlignée ; « Ajouter à l'agenda » seul si transférée. Arbitré le 12 septembre 2026 : une heure qui change peut changer la réponse, et c'est ce que fait Google |
| `newer` | présent, `SEQUENCE` reçue plus petite, `REQUEST` **ou `CANCEL`** | état 6, phrase seule, aucun bouton ; un `CANCEL` périmé n'a pas de bouton, sinon il mènerait au `409` de la décision 4 |
| `cancelled` | `CANCEL` reçu, l'événement présent, `SEQUENCE` reçue au moins égale | état 5, bouton « Retirer de mon agenda » |

Avec `current`, `outdated` et `cancelled`, le bloc porte aussi `calendarId` et `savedPartStat`,
la réponse que porte le fichier en base, qui est celle rappelée par l'encart. La `SEQUENCE` d'un
fichier qui n'en a pas vaut 0 (RFC 5545 § 3.8.7.4). Un fichier `occurrenceOnly` (décision 1 bis)
ne déclenche pas cette recherche : son pied est l'état 6 quoi que porte l'agenda.

### 4. Répondre : un point d'entrée, le fichier relu, déposé comme un `PUT`

`POST /api/Calendar/Invitations/Respond` reçoit `{ folder, uid, part, answer, calendarId }` où
`answer` ∈ `Accepted | Tentative | Declined | AddOnly | Remove`, et le compte de messagerie par
`X-Account-Id`, comme tout point d'entrée mail (le contrôleur résout la connexion par
`TryResolveAsync` de `MailControllerBase`, ce que les contrôleurs d'agenda ne font pas
aujourd'hui). Le serveur **relit la partie depuis IMAP** par `folder`/`uid`/`part` ; le
navigateur ne lui envoie jamais le fichier, qui pourrait être n'importe quoi. Puis, selon la
méthode et la réponse :

| reçu | réponse | ce qui est écrit dans l'agenda | mail |
|---|---|---|---|
| `REQUEST` | `Accepted`, `Tentative` | **le fichier de base** (voir ci-dessous), `METHOD` retiré, le `PARTSTAT` de l'utilisateur écrit dans **son** `ATTENDEE` (`ACCEPTED` / `TENTATIVE`, `RSVP` retiré) ; rien d'autre ne change, octet pour octet | `REPLY` |
| `REQUEST` | `Declined` | si présent : supprimé ; sinon rien | `REPLY` |
| `REQUEST` | `AddOnly` (transférée) | le fichier de base tel quel, `METHOD` retiré | aucun |
| `CANCEL` | `Remove` | supprimé | aucun : l'organisateur n'attend pas de réponse à une annulation |

**Quel fichier est réécrit.** Le fichier de base est celui de l'organisateur, relu depuis le mail,
quand le rendez-vous est `absent` ou `outdated` : c'est sa version qui doit entrer dans l'agenda.
Quand il est `current`, c'est **le fichier déjà en base** (`ics_raw` de la ligne trouvée à la
décision 3) qui est réécrit, et non celui du mail : les deux portent la même version de
l'organisateur, mais celui de la base porte aussi ce que l'utilisateur y a ajouté depuis un
téléphone, un rappel (`VALARM`), des notes. Changer d'avis depuis le mail ne doit pas les
effacer. Arbitré le 12 septembre 2026. Sur `outdated`, en revanche, la nouvelle version de
l'organisateur remplace tout, rappels compris : c'est le prix d'une mise à jour, écrit dans
« Ce que la tranche ne fait pas ». La réécriture du `PARTSTAT` est **textuelle**, sur la ligne
`ATTENDEE` de l'utilisateur seulement (repliée ou non), jamais une resérialisation Ical.Net, qui
normaliserait tout le fichier et changerait ses octets.

L'écriture passe par **le chemin du `PUT` CalDAV** (`IDavCalendarWriter.PutAsync` / `DeleteAsync`),
pas par le composeur du webmail : c'est lui qui stocke un fichier venu d'ailleurs sans le
réécrire, projette les colonnes et les participants, incrémente la synchro et archive la
version remplacée. Il archive aujourd'hui sous la seule cause `Put` ; il gagne un paramètre de
cause, `Put` par défaut, que le répondeur (et l'application des réponses, décision 12) passe à
`webmail`, comme toute écriture que l'utilisateur fait depuis le webmail. Ce chemin ne lit pas
`SEQUENCE` (un `PUT` CalDAV n'est jamais refusé pour
ça, et ne le sera pas davantage) : c'est le répondeur qui, avant d'écrire, compare la
`SEQUENCE` reçue à celle du fichier en base et répond `409` si elle est plus petite. Le
rendez-vous accepté est un événement CalDAV comme un autre ; un téléphone synchronisé le voit à
la prochaine synchro. Répondre à nouveau (état 2) réécrit le fichier en base avec le nouveau
`PARTSTAT` ; `Declined` sur un rendez-vous présent le supprime.

**Sous quel nom DAV, et dans quel agenda.** Une création prend un nom dérivé du `UID`, comme
toute création webmail (5a). Une **réécriture prend le nom de la ligne trouvée** à la décision 3,
et son agenda : `calendarId` du corps est ignoré dès que le rendez-vous existe. Ce n'est pas un
détail de confort. Un rendez-vous déjà enregistré par un téléphone porte un nom que ce téléphone
a tiré au hasard, pas un nom dérivé du `UID` ; déposer sous un nom dérivé un fichier portant un
`UID` que **un autre nom du même agenda** détient déjà est exactement ce que
`IDavCalendarWriter.PutAsync` refuse, `no-uid-conflict`. Écrire sous le nom existant est la
seule façon de mettre à jour un rendez-vous venu d'ailleurs. Et garder son agenda évite le
doublon que l'index `(calendar_id, uid)`, unique par agenda et non par utilisateur, laisserait
passer entre deux agendas.

`calendarId` n'est donc l'agenda cible que sur une création (`absent`) ; absent, l'agenda par
défaut. Avec un seul agenda, l'encart n'affiche pas de sélecteur (la règle de l'éditeur depuis le
10 septembre).

Un fichier `occurrenceOnly` (décision 1 bis) est refusé par un `400` : aucune réponse ne s'y
applique.

**Le mail part à la corbeille sur un refus, et sur un refus seulement.** Chaque réponse est
mémorisée là où elle laisse une trace naturelle, et la tranche n'ajoute aucun stockage pour ça :

- **accepté, provisoire** : le rendez-vous est dans l'agenda, et c'est lui la mémoire. Rouvrir le
  mail rend `inCalendar: current` et `savedPartStat`, donc l'état 2, « Vous avez accepté · dans
  Personnel », avec les deux autres réponses en boutons discrets. Changer d'avis reste à un clic,
  et le corps du mail — qui porte souvent le lien de visioconférence, absent du fichier —
  reste consultable ;
- **refusé** : rien n'est écrit en base, et il n'y a rien à écrire. Le départ du mail **est** la
  mémoire du refus : sans lui, l'encart reposerait la question comme si l'utilisateur n'avait
  jamais répondu. Le serveur déplace le message vers la corbeille du compte (le dossier que
  `SpecialUseCatalog` reconnaît comme `trash`, puis `MoveOrCopyAsync`, le même déplacement que
  l'action Supprimer du lecteur déclenche, sauf que c'est le serveur qui choisit le dossier et
  non l'écran), et le lecteur passe au message suivant comme après une suppression ;
- **`AddOnly`, `Remove`** : le mail reste. Le premier ajoute un rendez-vous transféré, le second
  en retire un annulé, et dans les deux cas l'agenda dit ce qui s'est passé.

Le déplacement n'a lieu que si le mail de refus est parti ; sinon le mail reste, pour que
« Renvoyer » (décision 5) reste à portée. Le champ `trashed` de la réponse dit ce qui a été fait.

Ce n'est pas ce que fait Outlook, qui retire par défaut toute demande de réunion de la boîte une
fois répondue. Le prix de cette uniformité serait de rendre l'état 2 presque invisible et de ne
pouvoir revenir sur une acceptation qu'en allant rouvrir le mail dans la corbeille, pour une
boîte plus nette. La règle retenue est moins uniforme et se paie d'une phrase d'explication ;
elle garde les sept états de la maquette réellement atteignables.

### 5. Le mail de réponse

Il part par le **pipeline d'envoi existant** (`MailSender` / `OutgoingMessageFactory`), donc par
la session SMTP **du compte où l'invitation a été lue** (celui de `X-Account-Id` : le serveur
de weesky pour le compte principal, celui de Gmail pour un compte Gmail connecté), depuis
`addressedTo` (résolu et validé comme tout `From`, il appartient forcément à ce compte puisqu'il
vient de sa liste d'adresses), et se range dans les Envoyés de ce compte. Destinataire :
l'adresse `ORGANIZER`, pas l'expéditeur du mail. Contenu :

- sujet « Accepté : Dîner chez Marc » / « Provisoire : … » / « Refusé : … » (clés i18n, en
  anglais « Accepted: … », « Tentative: … », « Declined: … ») ;
- un corps texte d'une ligne (« Marie-Rose Molhan a accepté l'invitation « Dîner chez Marc »
  du samedi 10 octobre 2026, 19:30. ») ;
- une partie `text/calendar; method=REPLY; charset=utf-8` contenant un `VCALENDAR` réduit :
  `METHOD:REPLY`, un `VEVENT` avec le `UID`, la `SEQUENCE` reçue, `DTSTAMP`, `ORGANIZER`, le
  seul `ATTENDEE` de l'utilisateur avec son `PARTSTAT`, et le `DTSTART` (que certains agendas
  exigent pour apparier). C'est la forme que produisent Thunderbird et Google, et celle que
  Google, Outlook et Apple apparient sans erreur.

Si l'envoi échoue **après** un enregistrement réussi, l'enregistrement n'est pas défait : la
réponse HTTP est un `200` qui porte `replySent: false` et un motif (jamais un `502`, réservé à
un IMAP injoignable *avant* l'enregistrement), et l'encart montre un bouton « Renvoyer », qui
rappelle le même point d'entrée avec la même réponse. La phrase dépend de la réponse :
« Ajouté à l'agenda. La réponse n'a pas pu être envoyée » sur une acceptation ou un provisoire,
« La réponse n'a pas pu être envoyée » sur un refus, où rien n'a été ajouté (et où un
rendez-vous présent a déjà été retiré). « Renvoyer » est idempotent : sur une acceptation, le
rendez-vous est désormais `current` et le fichier en base est réécrit avec le même `PARTSTAT`,
donc à l'identique, sans nouvelle version ni réveil des téléphones ; sur un refus, il n'y a plus
rien à retirer ; dans les deux cas le mail repart. Un refus ne va à la corbeille qu'une fois la
réponse partie (décision 4).

### 6. L'encart, et ce qu'il fait des pièces jointes

L'encart est celui de la maquette, entre l'en-tête du message et le corps, dessiné avec les
tokens du lecteur : titre avec l'icône d'agenda et une pastille de nature (« Invitation »,
« Mise à jour », « Annulé »), une phrase de contexte quand il y a lieu, les lignes Quand / Où /
Organisateur / Invités (une ligne absente n'est pas dessinée : un fichier minimal donne Quand
et Organisateur), et un pied qui dépend de l'état (décision 3, ou l'état 6 quand le fichier ne vise qu'une date,
décision 1 bis). La date est écrite dans le
fuseau du navigateur, celui dans lequel les écrans de l'agenda placent déjà toutes les heures
(`CalendarLayout`), en toutes lettres, « Journée entière » quand c'est le cas, et « Se répète »
en mot court quand `repeats` est vrai. Le fuseau d'origine du fichier n'est pas montré : une
invitation new-yorkaise s'affiche à l'heure de Bruxelles, comme elle s'affichera dans la grille.

Pendant l'appel, les boutons sont désactivés ; au retour, l'encart se redessine avec le bloc
renvoyé. **Toute partie calendrier** (`text/calendar`, `application/ics`, `*.ics`, donc aussi le
doublon `invite.ics` que Google et Outlook joignent) disparaît de la liste des pièces jointes
quand l'encart est affiché, comme les images affichées dans le corps ; elles y restent quand le
fichier est illisible.

### 7. Les participants dans l'agenda

Pour un événement reçu (l'organisateur n'est pas l'utilisateur), l'aperçu montre une ligne
« Organisé par … » et la liste des invités par leurs noms, en lecture seule, comme l'éditeur le
fait déjà sous « Plus d'options » depuis 5b ; l'indicateur d'état (`PARTSTAT`) qu'il y affichait
est retiré, pour la raison dite plus haut. Le libellé « Read only until invitations are
supported » disparaît en 5e2 avec le champ Invités (décision 8).

## Décisions — 5e2, inviter

### 8. Le champ « Invités » et l'organisateur

L'éditeur gagne un champ « Invités » sous « Lieu », visible sur un nouvel événement et sur tout
événement dont l'organisateur est l'utilisateur ou qui n'a pas d'organisateur. Saisie d'adresses
en puces, avec l'autocomplétion des contacts que le composeur utilise déjà
(`RecipientsField`, `suggestionsFor`). Sur un événement reçu, le champ cède la place à la liste
en lecture seule de la décision 7.

À l'enregistrement d'un événement avec invités, le composeur écrit `ORGANIZER` = l'identité
d'envoi par défaut du compte principal (`SendingIdentity.IsDefault` ; s'il n'y en a pas,
l'adresse principale de l'utilisateur, `CN` = son nom affiché), et un `ATTENDEE` par invité avec
`CN` depuis les contacts s'il existe, `ROLE=REQ-PARTICIPANT`, `PARTSTAT=NEEDS-ACTION`,
`RSVP=TRUE`. Les invités déjà présents gardent leur `PARTSTAT`. Pas de choix d'identité dans
l'éditeur : l'organisateur est celui que les réponses trouveront, et c'est l'adresse par défaut.
Une seule règle simple, à étendre si le besoin vient.

**Une réserve sur cette adresse.** Elle doit être sur un domaine que ce serveur héberge, ceux de
la table `domains`. C'est la seule condition sous laquelle le compte de service peut expédier en
son nom quand la modification vient d'un téléphone (décision 10) : Postfix ne l'y autorise que
là. Une identité d'envoi par défaut posée sur une adresse extérieure, un Gmail par exemple,
ferait donc partir les invitations depuis le webmail et échouer celles du compte de service, ce
qui est le pire des deux mondes. Le composeur écarte cette identité et retombe sur l'adresse
principale de l'utilisateur, toujours sur un domaine hébergé.

### 9. La règle d'empreinte : une seule, quel que soit l'appareil

Deux colonnes sur `calendar_events` :

- `scheduling_owner` : `'webmail'` quand le webmail a envoyé la première invitation, `NULL`
  sinon. Un événement avec invités écrit par Thunderbird reste `NULL` : Thunderbird envoie
  lui-même ses mails quand le serveur ne relaie pas, et on ne double pas les siens. Le webmail
  prend la main (pose `'webmail'`) la première fois qu'il enregistre lui-même un tel événement
  avec l'utilisateur pour organisateur.
- `scheduling_hash` : l'empreinte des champs qui comptent pour un invité, telle qu'elle était
  **au dernier envoi** : la forme que le composeur compare déjà pour incrémenter `SEQUENCE`
  (`IcsComposer.Shape` : début, fin, statut, règle, dates ajoutées et exclues), plus le titre,
  le lieu, et la liste triée des adresses des invités, **en minuscules et sans `mailto:`** : un
  `REPLY` d'Outlook réécrit volontiers l'adresse dans une autre casse, et sans cette
  normalisation appliquer la réponse (décision 12) changerait l'empreinte et renverrait une
  invitation à tout le monde.

**L'empreinte couvre toutes les occurrences, pas seulement la série.** `IcsComposer.Shape` ne lit
que le composant maître, celui qui porte la règle de répétition. Or déplacer une seule date d'une
série n'y touche pas : le webmail comme l'iPhone écrivent alors un **composant de surcharge** à
côté, avec son propre début et sa propre fin, et laissent la règle et les dates exclues du maître
intactes. Une empreinte prise sur le seul maître serait donc identique avant et après, et
personne ne serait prévenu que la réunion du 19 passe à 15 h. `SchedulingShape` applique donc la
forme à **chaque composant du fichier**, maître et surcharges, chacune préfixée de son
`RECURRENCE-ID`, et concatène le tout dans l'ordre de ces identifiants. Ajouter, retirer ou
déplacer une surcharge change alors l'empreinte, ce qui est le comportement attendu.

**Où vit la règle.** Deux portes seulement écrivent dans l'agenda : l'API du webmail
(`CalendarEventsController`) et le dépôt CalDAV d'un appareil (`CalDavController`, `PUT` et
`DELETE`). Chacune appelle `InvitationScheduler` **après** son écriture réussie, en lui passant
le fichier d'avant et celui d'après. Aucune des deux n'a besoin de plus : elle sait déjà d'où
vient la demande, donc quelle session SMTP employer (décision 10), et le fichier remplacé est
celui que l'écriture vient d'archiver — `DavWriteOutcome` gagne un champ `Replaced`, nul sur une
création, lu dans la transaction qui l'archive déjà, donc sans relecture. Le crochet **ne vit pas
dans la couche base** : y faire descendre l'origine de la requête et les identifiants du serveur
de mail, deux choses qu'elle ignore entièrement, coûterait plus de plomberie que la règle
elle-même. Le prix de ce choix est qu'une troisième porte d'écriture, si elle naissait, pourrait
oublier l'appel ; il n'y en a que deux, et les tests de la tranche les exercent toutes les deux.

À chaque écriture d'un événement dont `scheduling_owner` = `'webmail'`, le planificateur
recalcule l'empreinte et :

| situation | mails | sujet | après |
|---|---|---|---|
| première écriture avec invités depuis le webmail | `REQUEST` à tous | « Invitation » ; « Mise à jour » si le fichier en base portait déjà des invités (un événement que Thunderbird avait invité, que le webmail reprend) | `owner` posé, empreinte écrite |
| empreinte inchangée | aucun | — | — |
| empreinte changée, mêmes invités | `REQUEST` à tous | « Mise à jour » | empreinte écrite |
| invités ajoutés | `REQUEST` aux ajoutés ; `REQUEST` aux autres seulement si autre chose a changé | « Invitation » aux ajoutés, « Mise à jour » aux autres | empreinte écrite |
| invités retirés | `CANCEL` aux retirés (seuls) ; `REQUEST` aux restants seulement si autre chose a changé | « Annulation » / « Mise à jour » | empreinte écrite |
| plus aucun invité, ou suppression | `CANCEL` à tous | « Annulation » | `owner` remis à `NULL` |

La `SEQUENCE` du fichier est celle que l'écriture a produite : le composeur du webmail
l'incrémente selon sa règle, un client CalDAV l'incrémente lui-même (iOS, Thunderbird, DAVx⁵ le
font) ; si un client ne l'a pas fait alors que l'empreinte a changé, le serveur l'incrémente
dans le fichier stocké avant l'envoi, sinon l'agenda des invités ignorerait la mise à jour
(RFC 5546 § 2.1.4). **Comment** : le crochet tourne après l'écriture, quand le client a déjà reçu
l'ETag de ses octets ; l'incrément est donc **une seconde écriture** par le même chemin `PUT`,
sous le même nom, `SEQUENCE` du composant concerné plus un et rien d'autre (réécriture textuelle,
décision 4). Elle produit une version de plus et un rang de synchro de plus : le téléphone qui
vient d'écrire voit à sa prochaine synchro que la ressource a changé et la relit, exactement
comme si un autre appareil l'avait modifiée. Cette seconde écriture n'a lieu que dans ce cas,
rare (iOS, Thunderbird et DAVx⁵ incrémentent), et jamais deux fois pour une même écriture,
puisque le crochet ne se rappelle pas lui-même. Le fichier en base reste la vérité : c'est
contre lui que les réponses des invités sont comparées (décision 12). Une série s'invite entière ; une occurrence modifiée (« cette occurrence
seulement ») change l'empreinte par sa surcharge, et le `REQUEST` porte le fichier complet, série
et surcharges, ce que les agendas des invités savent lire. Quand seule une surcharge a changé,
c'est **sa** `SEQUENCE` que le composeur incrémente, et non celle du maître ; c'est déjà ce que
fait `IcsComposer.RewriteOne`, et c'est ce que la RFC demande, chaque composant portant sa propre
version.

**Les autres écritures**, que le tableau ne nomme pas, et ce qu'elles font :

- **« Cette occurrence et les suivantes »** coupe la série en deux : l'ancienne s'arrête avant
  la coupure, et la suite naît sous un **nouveau `UID`** (`IcsSplitter`). Pour les invités, ce
  sont deux rendez-vous : le crochet voit une empreinte changée sur l'ancien (« Mise à jour ») et
  une première écriture avec invités sur le nouveau (« Invitation »). Deux mails, c'est ce que
  Google et iOS envoient aussi dans ce cas ; rien de particulier à coder, la règle tombe juste.
- **Déplacer vers un autre agenda** ne change pas l'empreinte : aucun mail.
- **L'import d'un `.ics`** ne pose jamais `'webmail'`, même si l'utilisateur y est organisateur :
  un fichier importé vient d'un agenda qui a déjà invité ses gens.
- **Supprimer un agenda entier** (barre latérale) ou **le vider depuis un téléphone**
  (`DeleteAllAsync`) efface ses rendez-vous **sans annulation** : c'est rare et volontaire, et
  la cascade SQL n'exécute aucun code. Écrit dans « Ce que la tranche ne fait pas ».
- **Répondre par le webmail à une invitation reçue** (5e1) écrit un fichier dont l'organisateur
  n'est pas l'utilisateur : `owner` reste `NULL`, le crochet ne fait rien.

### 10. Deux sessions SMTP pour un même mail

Le mail est composé une seule fois (`InvitationMailer`), et remis par l'une des deux sessions :

- **écriture depuis le webmail** : la session SMTP du compte principal de l'utilisateur,
  ouverte avec les identifiants de sa requête, comme tout envoi ; le mail se range dans ses
  Envoyés. Les contrôleurs d'agenda ne résolvent aucune connexion de messagerie aujourd'hui :
  `CalendarEventsController` gagne la résolution du compte principal (le même
  `IAccountConnectionResolver` que le courrier, sans `X-Account-Id` puisque l'organisateur est
  toujours l'identité du compte principal, décision 8). C'est aussi ce qui fait de ce contrôleur
  le bon endroit pour appeler le planificateur : il est le seul des deux à disposer des
  identifiants de l'utilisateur ;
- **écriture depuis un appareil CalDAV** : le **compte de service** `noreply-agenda@weesky.net`,
  configuré dans le microservice (`Scheduling:Smtp:{Host, Port, Login, Password}` dans la
  configuration locale, jamais dans le dépôt ; le mot de passe ayant transité par la conversation
  de conception est à changer avant la mise en service). `From` et l'adresse d'enveloppe sont
  celles de l'utilisateur ; le compte de service n'apparaît dans aucun en-tête visible. Postfix
  l'y autorise par la troisième branche ajoutée à `smtpd_sender_login_maps` (pour tout domaine
  de la table `domains`, et eux seuls). Pas de copie dans les Envoyés : elle exigerait la même
  confiance côté IMAP, qu'on ne demande pas ; le mail est tracé dans le journal du microservice
  (`UID`, `SEQUENCE`, destinataires, sans corps).

L'envoi hors session est **asynchrone et sans blocage du `PUT`** : l'écriture CalDAV répond
d'abord, l'envoi part ensuite (file en mémoire, un essai, puis un second après une minute, puis
abandon journalisé). Un `PUT` ne peut pas échouer parce que le SMTP est en panne. La file est
en mémoire, **donc perdue si le microservice redémarre** pendant l'attente : le mail n'est
jamais renvoyé, seul le journal en garde la trace. Choix assumé le 12 septembre 2026 contre une
table d'attente en base : un redémarrage pendant la minute d'attente est rare, et une table de
plus avec son balayage au démarrage ne se justifie pas avant qu'un cas réel l'exige.

### 11. Le mail d'invitation

Pour `REQUEST` : sujet « Invitation : Dîner chez Marc » (« Mise à jour : … » quand l'empreinte
change, « Annulation : … » pour `CANCEL`) ; un corps texte lisible partout : le titre, quand
(dans le fuseau de l'événement), où, « Organisé par », et une ligne « Répondez depuis votre
agenda, ou par retour de mail. » ; une partie `text/calendar; method=REQUEST` avec le fichier
complet de l'événement (`METHOD` ajouté, `VTIMEZONE` inclus, le `SEQUENCE` courant), **et** la
même chose en pièce jointe `invitation.ics` (`application/ics`) : la double forme que Gmail,
Outlook et Apple attendent pour montrer leurs boutons. `CANCEL` porte un fichier réduit
(`UID`, `SEQUENCE`, `ORGANIZER`, les `ATTENDEE` visés, `DTSTART`, `STATUS:CANCELLED`). Sa
`SEQUENCE` est celle du fichier stocké quand le rendez-vous survit (des invités retirés : le
fichier reste, et sa version ne bouge pas pour eux), et celle du fichier stocké **plus un**
quand il est supprimé, pour que l'agenda de l'invité ne l'écarte pas comme déjà vue
(RFC 5546 § 3.2.5 : la `SEQUENCE` d'un `CANCEL` doit être au moins celle du dernier `REQUEST`).

### 12. Les réponses, appliquées à l'affichage du mail

Le bloc `invitation` (décision 1) traite aussi `REPLY` : le détail du message le **lit** (l'invité,
son `PARTSTAT`, la `SEQUENCE`) et dit si le `UID` désigne un événement **que le webmail a invité**
(`scheduling_owner` = `'webmail'`), par un champ `applicable`. Le détail n'écrit rien : lire un
mail ne modifie jamais l'agenda, comme aucune lecture du webmail ne modifie quoi que ce soit.
C'est l'encart qui, une fois affiché avec `applicable: true`, appelle **une fois**
`POST /api/Calendar/Invitations/ApplyReply` `{ folder, uid, part }` (arbitré le 12 septembre
2026 contre une écriture faite par le `GET` du détail : une requête de plus, mais le
rafraîchissement périodique du lecteur et un F5 ne repassent pas par le verrou de l'agenda pour
ne rien changer). Le serveur relit la partie depuis IMAP, comme le répondeur de la décision 4,
et **applique la réponse** — le `PARTSTAT` de l'invité est écrit dans son `ATTENDEE` de
l'événement en base, par le chemin `PUT`, cause `webmail` — puis renvoie le bloc à jour, et
l'encart dit « Marc a refusé » (états « a accepté », « a répondu peut-être », « a refusé »). Pas
de bouton : il n'y a aucune décision à prendre. Une réponse dont la `SEQUENCE` est plus petite
que celle de l'événement est affichée avec « réponse à une version précédente » et n'est pas
appliquée ; un `REPLY` pour un `UID` inconnu, dont l'`ATTENDEE` n'est pas dans la liste des
invités (un délégué, une adresse réécrite), ou qui porte un `RECURRENCE-ID` (Marc refuse une
seule date de la série, ce que Google sait envoyer) est affiché en lecture seule, avec la phrase
qui le dit — pour ce dernier, « Réponse pour une seule date de la série, non reportée dans
l'agenda » : la reporter demanderait d'écrire une surcharge, c'est-à-dire la fusion que 1 bis
écarte. Dans ces cas `applicable` est faux et l'encart n'appelle rien.

**Le critère est bien `scheduling_owner`, et non « l'utilisateur est l'organisateur ».** Les deux
ne se recouvrent pas : un rendez-vous que Thunderbird a invité porte l'utilisateur comme
organisateur et `scheduling_owner` à `NULL`, et un rendez-vous supprimé ou vidé de ses invités
repasse à `NULL` lui aussi (voir le tableau). Dans ces cas la réponse n'est pas appliquée, et la
phrase dit ce qui est vrai — « Ce rendez-vous n'a pas été invité depuis le webmail » ou « Ce
rendez-vous n'existe plus » — jamais que l'utilisateur n'est pas l'organisateur, ce qu'il est.
Ne pas appliquer est le bon comportement : l'agenda de Thunderbird tient sa propre liste de
réponses, et lui écrire par-dessus la ferait diverger.
L'invité est apparié à son `ATTENDEE` sans tenir compte de la casse. Écrire un `PARTSTAT` ne
change ni la `SEQUENCE` ni l'empreinte (décision 9, la liste des adresses est normalisée) :
aucun mail ne repart. Une réponse déjà appliquée réécrit un fichier identique octet pour octet,
que le chemin `PUT` reconnaît et ignore : rouvrir dix fois le mail ne crée ni version ni synchro.

Si l'écriture échoue (verrou de l'agenda pris, base indisponible), le mail est déjà affiché et le
reste ; la réponse d'`ApplyReply` porte `applied: false` avec un motif, et l'encart dit
« Réponse non enregistrée dans l'agenda », avec un bouton « Réessayer » qui rappelle le même
point d'entrée.

Pour un événement dont l'utilisateur est l'organisateur, l'aperçu et l'éditeur montrent l'état
de chaque invité (accepté, provisoire, refusé, sans réponse), par une pastille à côté du nom :
là, l'état est vrai, puisque c'est à l'utilisateur que les réponses arrivent. Pour un événement
reçu, des noms (décision 7).

### 13. Ce que le serveur ne dit pas aux clients

L'en-tête `DAV:` reste `1, 3, addressbook, calendar-access, extended-mkcol` ; ni
`calendar-auto-schedule` ni `calendar-schedule`, et `schedule-inbox-URL` / `schedule-outbox-URL`
restent en `propstat 404`, comme 5c les fixe (décision 8 de 5c). Conséquence voulue : l'iPhone ne
propose pas de champ Invités sur nos agendas ; Thunderbird envoie lui-même pour les événements
qu'il crée, et le serveur pour ceux que le webmail a invités (décision 9). Inviter se fait dans
le webmail ; modifier se fait où l'on veut.

Un cas se recouvre : un rendez-vous invité depuis le webmail, puis **modifié dans Thunderbird**.
Thunderbird ne sait pas qui a invité ; il propose d'envoyer ses propres mails de mise à jour, et
le serveur envoie les siens. Si l'utilisateur accepte la proposition, les invités reçoivent deux
mails pour une même modification, tous deux justes (même `UID`, même `SEQUENCE`), que leur agenda
applique deux fois sans dommage. Le serveur ne peut pas le savoir, donc ne l'évite pas ; écrit
dans « Ce que la tranche ne fait pas ».

## La surface HTTP

| verbe | chemin | phase | rôle |
|---|---|---|---|
| `GET` | `/api/Mail/Messages/Detail` | 5e1 | bloc `invitation` ajouté au DTO existant ; `REPLY` lu en 5e2, jamais appliqué ici |
| `POST` | `/api/Calendar/Invitations/Respond` | 5e1 | `{ folder, uid, part, answer, calendarId? }` + `X-Account-Id` → le bloc `invitation` à jour + `replySent`, `replyError?`, `trashed` ; `trashed` n'est vrai que sur un refus (décision 4) ; `400` réponse incompatible avec la méthode ou fichier ne visant qu'une occurrence, `404` message ou partie absente, `409` `SEQUENCE` plus ancienne, `422` fichier refusé par les garde-fous, `502` IMAP injoignable avant l'enregistrement. Un SMTP en panne après l'enregistrement est un `200` avec `replySent: false` (décision 5) |
| `POST` | `/api/Calendar/Invitations/ApplyReply` | 5e2 | `{ folder, uid, part }` + `X-Account-Id` → le bloc `invitation` à jour + `applied`, `applyError?` (décision 12) ; `400` la partie n'est pas un `REPLY` applicable, `404` message ou partie absente, `502` IMAP injoignable. Un agenda indisponible est un `200` avec `applied: false` |
| `POST` / `PUT` | `/api/Calendar/Events`, `/api/Calendar/Events/{id}` | 5e2 | `attendees: [{ email, name? }]` accepté dans le corps ; la réponse porte `scheduling: { owner, sent: n }` |

Aucun point d'entrée nouveau pour 5e2 côté envoi : la règle d'empreinte est appelée par les deux
contrôleurs qui écrivent, celui de l'API et celui de CalDAV, juste après leur écriture
(décision 9).

## Le schéma

5e1 ajoute un index, `ix_calendar_events_user_uid (user_id, uid)` sur `calendar_events`, pour la
recherche de la décision 3. 5e2 ajoute à `calendar_events` : `scheduling_owner VARCHAR(16) NULL`
et `scheduling_hash CHAR(64) NULL` (SHA-256 hex de l'empreinte). Documenté dans
`docs/superpowers/webmail-calendar-tables.md`. Les révisions (`calendar_revisions`) ne changent
pas ; le journal des envois est celui du microservice, pas une table (décision 10).

## Fichiers

**Backend, 5e1.** `Services/Calendar/Invitations/InvitationReader.cs` (la partie MIME →
le bloc ; pur, testable sans IMAP), `InvitationResponder.cs` (relit depuis IMAP, réécrit le
`PARTSTAT`, dépose par `IDavCalendarWriter`, envoie), `ReplyComposer.cs` (le `VCALENDAR`
`REPLY`), `Models/Mail/MailInvitation.cs` (le bloc, dont `filePartStat`, `savedPartStat` et
`occurrenceOnly`), `Controllers/CalendarInvitationsController.cs`
(hérite de `MailControllerBase` pour résoudre le compte), `Services/UserAddresses.cs` (la liste
d'adresses d'un compte, extraite de `DavPrincipalController.AddressesOrNullAsync` et partagée
avec lui), `Services/Calendar/Invitations/PartStatRewriter.cs` (la réécriture textuelle de la
ligne `ATTENDEE`, décision 4 ; pur). `IDavCalendarWriter.PutAsync` / `DeleteAsync` gagnent un
paramètre `RevisionCause`, `Put` par défaut. `ImapMessageCommands.GetAsync` repère la partie
calendrier dans le `BODYSTRUCTURE` et la télécharge ; `MailMessageMapper` appelle le lecteur ;
`IcsProjector` n'est pas touché.

**Frontend, 5e1.** `modules/mail/reader/InvitationCard.tsx` (l'encart, ses sept états),
`modules/mail/reader/invitationText.ts` (les phrases, la date en toutes lettres),
`modules/calendar/EventPreview.tsx` (organisateur et noms), `EventEditor.tsx` (retrait de
l'indicateur d'état), `styles/mail.css` (`.invitation-card…`), locales `mail.json` et
`calendar.json` FR/EN.

**Backend, 5e2.** `Services/Calendar/Scheduling/SchedulingShape.cs` (l'empreinte, tous composants),
`SchedulingDecider.cs` (avant/après → qui reçoit quoi ; pur), `InvitationScheduler.cs` (le
crochet : décide, compose, remet à la bonne session), `InvitationMailer.cs` (compose
`REQUEST`/`CANCEL`), `ServiceSmtpSender.cs` (la session du compte de service, file asynchrone),
`Controllers/CalendarEventsController.cs` (résolution du compte principal, puis l'appel au
crochet), `Controllers/CalDavController.cs` (l'appel au crochet après `PUT`/`DELETE`),
`Controllers/CalendarInvitationsController.cs` (`ApplyReply`, décision 12),
`Models/Dav/DavWriteOutcome.cs` (le champ `Replaced`) et `Repositories/DavCalendarWriter.cs` qui
le remplit, `IcsComposer.cs` (`ORGANIZER`/`ATTENDEE` composés), migration + `CalendarEvent.cs`.

**Frontend, 5e2.** `modules/calendar/AttendeesField.tsx` (puces + autocomplétion, réutilise
`suggestionsFor`), `EventEditor.tsx`, `EventPreview.tsx` (pastilles d'état sur ses propres
événements), `InvitationCard.tsx` (état `REPLY`).

## Tests

**5e1, serveur** (xUnit) : le lecteur sur un `REQUEST` de Google, d'Outlook, d'Apple et de
Thunderbird (fixtures anonymisées, sans adresse réelle), un `CANCEL`, un `REPLY` (ignoré en
5e1), une partie sans `METHOD`, deux parties calendrier, une partie dans le
`multipart/alternative` sans nom de fichier (la forme de Google), une partie trop grosse pour
être téléchargée, un fichier refusé par les garde-fous ; `addressedTo` sur l'adresse
principale, sur un alias, sur l'adresse d'un compte connecté, absent ; `inCalendar` dans ses
cinq valeurs, et le même `UID` dans deux agendas ; un `REQUEST` et un `CANCEL` portant un
`RECURRENCE-ID` → `occurrenceOnly`, aucune recherche dans l'agenda, `400` au répondeur ;
`filePartStat` et `savedPartStat` distincts sur un rendez-vous déjà accepté. Le répondeur :
`Accepted`/`Tentative`/
`Declined` écrivent le bon `PARTSTAT` et laissent le reste du fichier identique (comparaison
octet pour octet hors la ligne `ATTENDEE` et `METHOD`, ligne `ATTENDEE` repliée comprise) ; le
fichier est relu depuis IMAP (le corps de la requête ne le porte pas) ; **sur `current`, c'est le
fichier en base qui est réécrit : un `VALARM` ajouté par un téléphone survit au changement
d'avis** ; sur `outdated`, le fichier reçu remplace tout ; un `CANCEL` de `SEQUENCE` plus
ancienne rend `newer` et non `cancelled` ; `Declined` sans ligne ne crée rien ; `Remove`
supprime ; la version remplacée est archivée sous la cause `webmail` ;
`SEQUENCE` plus ancienne → `409` ; **une mise à jour d'un rendez-vous déjà enregistré sous un nom
DAV tiré par un téléphone est écrite sous ce nom-là et non sous un nom dérivé du `UID`, et ne rend
donc aucun conflit d'identifiant** ; un `calendarId` désignant un autre agenda que celui de la
ligne trouvée est ignoré, et aucun doublon n'apparaît ; le `REPLY` composé porte méthode, `UID`,
`SEQUENCE`, l'`ATTENDEE` réduit, et part de `addressedTo` vers `ORGANIZER` par la connexion du
compte résolu ; échec d'envoi après enregistrement → `200`, `replySent: false`, l'événement reste,
le mail reste ; refus parti → le mail est déplacé vers la corbeille, `trashed: true` ; acceptation
partie → le mail reste et `trashed: false` ; échec d'envoi sur un refus → `200`,
`replySent: false`, rien d'écrit, « Renvoyer » renvoie le mail sans autre effet. Le harnais
`caldavtester` est rejoué avant et après.

**5e1, frontend** (Vitest) : l'encart dans ses sept états depuis un bloc fixé, et l'état 6 dans
ses trois phrases dont celle d'une invitation ne visant qu'une date ; l'état 4 avec ses trois
réponses et la précédente surlignée ; la phrase de contexte quand `filePartStat` porte déjà une
réponse ; toute partie
calendrier absente des pièces jointes ; le clic appelle l'API et redessine ; le passage au
message suivant quand `trashed` est vrai, et le maintien sur le message quand il est faux ;
l'état 2 rappelant `savedPartStat` après une acceptation ; « Renvoyer » sur échec, avec la phrase
propre au refus ; la date dans le fuseau du
navigateur ; sélecteur d'agenda seulement si plusieurs ; parité FR/EN et typographie française.
Géométrie par sonde navigateur (`probes/invitation-card.html`), jamais par jsdom.

**5e2, serveur** : `SchedulingDecider` sur chaque ligne du tableau de la décision 9 ;
`SchedulingShape` change quand **une seule surcharge** bouge alors que le maître ne bouge pas,
depuis le webmail comme depuis un fichier déposé par un appareil, et ne change pas quand un
`PARTSTAT` est réécrit ; le crochet appelé par les deux contrôleurs, celui de l'API du webmail et
celui de CalDAV sur `PUT` comme sur `DELETE` (session utilisateur vs compte de service, choisi par
l'origine), avec le fichier d'avant lu dans `DavWriteOutcome.Replaced` ; aucun envoi sur un
événement dont `scheduling_owner` est `NULL` ; `SEQUENCE` incrémentée par le serveur quand un
client ne l'a pas fait, par une seconde écriture qui produit une version et un rang de synchro
de plus, et aucune quand il l'a fait ; contenu des
mails (`METHOD`, `UID`, `SEQUENCE`, `ORGANIZER`, `From` de l'utilisateur, double forme) ;
`ORGANIZER` retombant sur l'adresse principale quand l'identité d'envoi par défaut est sur un
domaine que le serveur n'héberge pas (décision 8) ; la
file asynchrone (un `PUT` répond même SMTP en panne, un second essai, puis abandon journalisé) ;
les réponses (le détail lit un `REPLY` sans rien écrire ; `ApplyReply` l'applique au bon invité,
adresse en casse différente appariée sans changer
l'empreinte, version périmée non appliquée, `UID` étranger, invité inconnu et `RECURRENCE-ID`
en lecture seule avec `applicable: false`, écriture en échec → `200` avec `applied: false`,
rappel idempotent, aucun mail ne repart) ; la coupure
« cette occurrence et les suivantes » produit une mise à jour et une invitation ; l'import et le
déplacement d'agenda n'envoient rien.

**5e2, frontend** : le champ Invités (saisie, suggestions, retrait), absent sur un événement
reçu ; les pastilles d'état sur ses propres événements et les noms seuls sur les reçus ;
l'encart `REPLY`, qui appelle `ApplyReply` une seule fois au montage quand `applicable` est vrai,
jamais sur un rafraîchissement, et « Réessayer » sur `applied: false`.

**Avec un vrai client**, scénario de recette de 5e2 : déplacer depuis DAVx⁵ ou Thunderbird
un événement invité par le webmail et vérifier chez Gmail que la mise à jour arrive, signée
`DKIM: PASS`, depuis l'adresse de l'utilisateur, sans trace du compte de service.

## Ce que la tranche ne fait pas

- **Pas de relais RFC 6638** : ni `calendar-auto-schedule`, ni boîtes d'ordonnancement, ni
  champ Invités sur l'iPhone, ni réponse à une invitation depuis l'application Calendrier du
  téléphone, ni réponses lues en arrière-plan. Les réponses sont appliquées quand on ouvre le
  mail dans le webmail ; un « refusé » pas encore lu n'induit personne en erreur ailleurs que
  dans la liste d'états.
- **Pas de balayage de la boîte de réception** : une invitation n'entre dans l'agenda que par un
  clic.
- **Pas d'état des invités sur un rendez-vous reçu**, pas de contre-proposition d'horaire
  (`COUNTER`), pas de consultation des disponibilités des invités, pas de rôle facultatif ni de
  délégation, pas de relance des sans-réponse, pas de choix de l'identité organisatrice.
- **Pas d'import d'un `.ics` hors mail** (fichier téléchargé, glissé) et pas de tâches
  (`VTODO`).
- **Pas de copie dans les Envoyés** d'un mail envoyé par le compte de service.
- **Pas de double envoi pour Thunderbird** : les événements qu'il invite lui restent. L'inverse
  n'est pas tenu : un événement invité depuis le webmail puis modifié dans Thunderbird part deux
  fois si l'utilisateur accepte la proposition d'envoi de Thunderbird (décision 13). Sans
  dommage chez les invités, et le serveur ne peut pas le savoir.
- **Pas de conservation des ajouts du téléphone sur une mise à jour de l'organisateur** : quand
  l'organisateur renvoie une nouvelle version (état 4), son fichier remplace celui en base,
  rappels et notes compris. Un simple changement d'avis (état 2), lui, les garde (décision 4).
- **Pas de report d'une réponse portant sur une seule date** d'une série (`REPLY` avec
  `RECURRENCE-ID`) : affichée, non appliquée (décision 12).
- **Pas d'annulation quand un agenda entier est supprimé ou vidé** (barre latérale, ou
  `DELETE` de la collection depuis un téléphone) : les invités des rendez-vous qu'il contenait ne
  sont pas prévenus. Rare et volontaire ; décidé le 12 septembre 2026.
- **Pas de réponse à une invitation qui ne vise qu'une date** d'une série : elle est affichée, avec
  la phrase qui dit de corriger l'agenda à la main, et aucun bouton (décision 1 bis). Décidé le
  12 septembre 2026 : la traiter obligerait à fusionner le fichier reçu avec celui en base, la
  seule fusion de toute la tranche.
- **Pas d'autre mémoire d'un refus que le départ du mail** : rien n'est écrit dans l'agenda, et le
  mail part à la corbeille avec la réponse (décision 4). Refuser à nouveau depuis la corbeille
  renvoie un second mail de refus, sans autre effet.
- **Pas de `VTIMEZONE` dans le `REPLY`** : le `DTSTART` est recopié avec son `TZID`
  sans le bloc de fuseau (RFC 5545 § 3.6.5 le demande) ; Google, Outlook et Apple
  l'apparient sans, un validateur strict le refuserait.
- **Pas de file d'envoi persistante** : un redémarrage du microservice pendant l'attente perd
  le mail du compte de service (décision 10).

## Risques

- **Un client qui n'incrémente pas `SEQUENCE`.** Couvert par la décision 9, à vérifier avec
  DAVx⁵ dans le scénario de recette de 5e2.
- **Le compte de service et la délivrabilité.** Le mail sort du même serveur, signé DKIM, depuis
  l'adresse de l'utilisateur ; le seul en-tête qui le distingue est le login SASL dans un
  `Received`, invisible. À confirmer sur le premier mail réel chez Gmail et Outlook.
- **Une réponse qui n'arrive pas à `ORGANIZER`.** Un organisateur dont l'adresse du fichier
  n'est pas celle du mail (Doctolib, assistant) reçoit la réponse à l'adresse du fichier, comme
  le RFC l'exige ; s'il ne la lit pas, ce n'est pas notre affaire.
- **Le fichier relu depuis IMAP a changé** (message déplacé entre l'affichage et le clic) :
  `404`, l'encart propose de recharger le mail. L'`uid` IMAP change avec le dossier, donc
  l'encart ne peut pas retrouver le message lui-même ; c'est la liste qui le retrouvera.
- **Le déplacement vers la corbeille après un refus** (décision 4) est une écriture IMAP de plus
  après un envoi réussi : s'il échoue, le mail reste dans la boîte, `trashed: false`, et rien
  d'autre n'est défait ; l'utilisateur le supprime à la main. L'encart, lui, reposera la question
  à la prochaine ouverture, le refus n'ayant laissé aucune trace en base ; refuser une seconde
  fois renvoie un mail identique, sans autre effet.
- **Le compte de service peut expédier au nom de n'importe quelle adresse de n'importe quel
  domaine hébergé**, c'est la condition même de la décision 10. Le microservice devient donc, pour
  cette clé, une autorité d'usurpation à l'intérieur de nos domaines. Le mot de passe ne vit que
  dans la configuration locale, hors du dépôt, et la troisième branche de
  `smtpd_sender_login_maps` reste bornée à la table `domains` : jamais un domaine que nous
  n'hébergeons pas.
- **Une réponse appliquée par l'encart** (décision 12) : l'écriture dépend d'un appel que le
  navigateur fait après l'affichage. Si l'utilisateur ferme le mail avant qu'il aboutisse, la
  réponse n'est pas reportée ; la prochaine ouverture du mail la rejoue, et l'écriture est
  idempotente (fichier identique, décision 12), donc rejouer ne coûte rien.
- **Le crochet CalDAV** de 5e2 est la première fois que le serveur réagit à un `PUT` autrement
  qu'en stockant : il ne doit jamais faire échouer ni ralentir l'écriture (décision 10), et la
  suite `caldavtester` est rejouée après lui. Sa seconde écriture, quand un client n'a pas
  incrémenté `SEQUENCE` (décision 9), est le seul cas où le serveur modifie de lui-même ce qu'un
  appareil vient de déposer ; DAVx⁵ et iOS doivent la relire proprement à la synchro suivante,
  ce que le scénario de recette vérifie.
- **Une troisième porte d'écriture qui oublierait le crochet.** Il est appelé par les deux
  contrôleurs et non par la couche base (décision 9) : une porte future qui écrirait sans
  l'appeler laisserait les invités dans le noir, sans que rien ne le signale. Il n'y en a que
  deux aujourd'hui, les tests les exercent toutes les deux, et le jour où une troisième apparaît
  la question se repose.
