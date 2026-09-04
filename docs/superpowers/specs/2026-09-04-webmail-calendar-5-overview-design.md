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
web open source auto-hébergés (Nextcloud Calendar, SOGo, Roundcube/Kolab, AgenDAV) : trois vues plus
une liste, plusieurs agendas colorés, un éditeur complet, glisser-déposer, recherche, import/export.
Le projet ne vise pas le travail en équipe : pas de partage, pas de free/busy, pas de réservation de
créneaux.

## Découpage

| | Tranche | Analogue contacts | Dépend de |
|---|---|---|---|
| 5a | Modèle de données, moteur iCalendar (Ical.Net), expansion des occurrences, API | 4a | — |
| 5b | Écrans : vues, barre latérale, bulle d'aperçu, éditeur, téléphone | 3x + 4b | 5a |
| 5c | Serveur CalDAV sous le principal `/dav` existant | 4c | 5a |
| 5d | Conformité clients (`ccs-caldavtester`, Thunderbird, DAVx⁵, iOS) | 4d | 5c |

L'ordre est **5a → 5b → 5c → 5d**, pour la raison qui a fixé celui des contacts : le module devient
utile et éprouvé par une édition réelle avant qu'un client externe n'en dépende.

Tranches envisagées **après** 5d, non conçues ici : 5e invitations (envoi, réponses par mail,
statut des participants) ; agenda « Anniversaires » projeté depuis les contacts ; abonnement à un
agenda externe (webcal) ; partage entre utilisateurs ; notifications de rappel dans le webmail.

## Les décisions

### 1. Le fichier est la référence, les colonnes sont un index

Un événement est un fichier texte `.ics` (RFC 5545), stocké tel quel dans une colonne `ics_raw`.
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
| première et dernière occurrence | ne lire que les événements qui touchent la fenêtre affichée, récurrences comprises (le `firstoccurence`/`lastoccurence` de sabre/dav) |
| récurrent (oui/non) | affichage, et savoir qu'il faut expanser |
| agenda (`calendar_id`) | filtre par agenda |
| disponibilité, visibilité | badge et filtre |
| `UID`, empreinte, nom DAV, séquence de synchro | CalDAV, à l'identique de 4c |

L'organisateur et les participants vont dans une table fille (adresse, nom affiché, rôle, réponse),
projetée dès 5a et affichée en lecture seule en 5b : rien n'est perdu quand un événement invité
arrive d'un téléphone, et 5e trouvera la donnée en place.

Pour un événement simple, les colonnes dupliquent presque tout le fichier ; c'est assumé. Dès qu'il
est récurrent ou vient d'un client, elles n'en sont plus qu'un extrait. Si on les perdait, on les
recalculerait depuis les fichiers.

Restent dans le fichier seulement : la règle de récurrence et ses exceptions, les rappels
(`VALARM`), les catégories, la priorité, l'adresse web, les pièces jointes, les lignes `X-`.

### 2. Plusieurs agendas par utilisateur, créés depuis le webmail

Une table `calendars` : propriétaire, nom, couleur, ordre, nom DAV. Un agenda `default` est créé
avec l'utilisateur ; les autres depuis la barre latérale du webmail. Chaque événement porte son
`calendar_id`. Le `MKCALENDAR` d'un client CalDAV est refusé en 5c : la création reste un geste du
webmail, ce qui garde le protocole aussi simple que celui du carnet unique.

L'usage réel d'un agenda est presque toujours pluriel (personnel / travail / famille), tous les
clients CalDAV affichent une liste d'agendas colorés, et l'ajouter après coup imposerait un
rattrapage sur chaque événement et sur les jetons de synchro.

### 3. Des événements seulement, pas de tâches

Le module ne gère que `VEVENT`. La collection CalDAV annonce
`supported-calendar-component-set = VEVENT` : un client n'y dépose jamais de `VTODO`, rien ne
traverse « verbatim », rien n'est perdu — c'est refusé à la porte. Les tâches sont un module à part
entière (état, échéance, priorité, second éditeur, secondes vues) et feront un projet à part si un
jour il en faut un.

### 4. Ical.Net en lecture et en écriture

Le parsing, la projection des colonnes, l'expansion des occurrences et la réécriture d'un événement
modifié depuis le webmail passent par `Ical.Net` (MIT, 35 M de téléchargements, v5.2.x publiée
tous les deux mois en 2026, v6 en préparation). Un seul outil, un seul modèle, aucune double
interprétation du même fichier.

L'alternative envisagée — un moteur de récurrences maison, ou Ical.Net en lecture seule avec un
remplacement de lignes maison pour l'écriture — a été écartée. La difficulté d'un moteur de
récurrences n'est pas l'algorithme du RFC mais ce que les clients réels écrivent, et une
bibliothèque qui a vu passer des millions de fichiers y est meilleure que ce qu'on écrirait ;
l'hybride, lui, crée deux lectures du même fichier qui peuvent ne pas être d'accord (un `VALARM` a
son propre `SUMMARY`).

Ce que ça implique : une modification depuis le webmail relit, modifie et **réécrit le fichier en
entier**. Les lignes inconnues sont conservées ; leur ordre et leur mise en forme peuvent changer.
L'empreinte (ETag) change, ce qui est normal pour une modification. Les révisions archivées
(décision 17 de 4c) gardent la version d'avant.

Lacunes connues à traiter au-dessus de la bibliothèque : `RECURRENCE-ID;RANGE=THISANDFUTURE` non
implémenté (issue #455, ce qu'iOS écrit pour « cette occurrence et les suivantes ») — journalisé
en attendant ; pas de validation des `RRULE` (#903) — un `PUT` malformé doit répondre `400` par nos
soins. **Vérification en 5a, au premier plan** : trois événements réels (iPhone, Thunderbird,
Google Agenda) passés par lecture → écriture, différences comparées ; une perte d'information se
corrige localement ou en amont.

### 5. Les occurrences se calculent côté serveur

Un événement récurrent est stocké une fois, avec sa règle. Une **occurrence** est une instance
concrète à une date donnée ; **expanser**, c'est dérouler la règle sur une fenêtre. Le serveur seul
le fait : le `calendar-query` avec `time-range` de 5c l'exige (RFC 4791 §9.9), et l'API de 5b —
`GET /api/calendar/events?from=…&to=…` — rend une **liste plate d'occurrences** (chacune avec son
`UID`, sa date d'instance et ses champs affichables). Le client pose sur la grille, il ne calcule
rien. Un seul moteur, deux consommateurs.

`EXDATE` retire une occurrence ; `RDATE` en ajoute ; un second `VEVENT` de même `UID` avec
`RECURRENCE-ID` remplace une occurrence par une version modifiée — c'est ainsi que les clients
écrivent « modifier celle-ci seulement », et c'est ainsi que le webmail l'écrira aussi.

### 6. Fuseau horaire

Celui du navigateur, sans préférence stockée, jusqu'à ce qu'un cas réel en réclame une. Les
identifiants IANA (`Europe/Brussels`) se résolvent par `TimeZoneInfo` sur Windows comme sur Linux ;
les blocs `VTIMEZONE` traversent avec le fichier.

### 7. Invitations : modélisées, pas traitées

`ORGANIZER` et `ATTENDEE` sont projetés (décision 1) et affichés en lecture seule. Aucun envoi,
aucune réponse, aucun traitement d'un `.ics` reçu par mail avant 5e.

## Les fonctionnalités du socle (5b)

1. **Vues** mois, semaine, jour, et liste « à venir » ; navigation précédent / suivant /
   aujourd'hui ; mini-mois de navigation dans la barre latérale.
2. **Agendas** colorés, affichables ou masquables d'une case à cocher ; création, renommage,
   couleur, suppression depuis la barre latérale.
3. **Éditeur** : titre, agenda, journée entière, début et fin, répéter, rappel(s), lieu,
   description. Sous « Plus d'options » : disponibilité (Occupé / Provisoire / Libre), visibilité
   (Par défaut / Privé), adresse web, participants en lecture seule. Le pli existe pour qu'un
   événement simple se crée sans faire défiler.
   - « Répéter » propose Jamais / Tous les jours / Toutes les semaines / Tous les mois / Tous les
     ans / Personnalisé… ; le réglage personnalisé donne l'intervalle, les jours de la semaine, le
     mois par quantième ou par n-ième jour, et la fin (jamais / après N fois / à une date). C'est le
     sous-ensemble curé d'Apple et de Google ; une règle plus riche venue d'un client s'affiche en
     texte et se conserve.
   - « Disponibilité » fusionne `STATUS` et `TRANSP` en un seul champ : Provisoire s'écrit
     `STATUS:TENTATIVE`, Libre `TRANSP:TRANSPARENT`, Occupé l'absence des deux. Pas de statut
     « Annulé » : on supprime.
   - Sur un événement récurrent, Enregistrer et Supprimer demandent d'abord « Cette occurrence
     seulement / Toutes les occurrences ».
4. **Glisser-déposer** pour déplacer, **redimensionnement** pour changer la durée, sur les vues
   semaine et jour ; glisser sur une case vide crée un événement.
5. **Recherche** par texte sur titre, lieu, description.
6. **Import / export** `.ics`, par agenda.
7. **Rappels** stockés dans le fichier et synchronisés — c'est le téléphone qui sonne. Aucune
   notification dans le webmail pour l'instant.

## L'interface

Le bandeau et le rail sont ceux du webmail ; l'entrée « Agenda » du rail existe déjà
(`ComingSoon`). Le module construit ses colonnes dans son outlet, comme le courrier et les contacts.

### Grand écran : deux colonnes

```
┌──────┬──────────────┬─────────────────────────────────────────────────────┐
│ rail │ mini-mois    │ Aujourd'hui ◀ ▶  14 – 20 septembre 2026   [J S M L] [+]│
│      │              ├──────┬──────┬──────┬──────┬──────┬──────┬──────┬──────┤
│      │ ☑ Personnel  │      │lun 14│mar 15│mer 16│jeu 17│ven 18│sam 19│dim 20│
│      │ ☑ Travail    │  9h  │▒Dent.│      │▒Point│      │      │      │      │
│      │ ☐ Famille    │ 10h  │      │      │      │▒Atel.│      │      │      │
│      │              │ 11h  │      │▒Banq.│      │      │      │      │      │
└──────┴──────────────┴──────┴──────┴──────┴──────┴──────┴──────┴──────┴──────┘
```

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
secret, même adresse, même identifiant. C'est ce pour quoi `dav_credentials` a été découpé (4c,
décision 19).

## Ce que le projet ne fait pas

- Pas de tâches (`VTODO`), pas de journal (`VJOURNAL`), pas de free/busy.
- Pas d'invitations envoyées ni traitées avant 5e.
- Pas de partage d'agenda entre utilisateurs, pas d'ACL.
- Pas de `MKCALENDAR` : les agendas se créent depuis le webmail.
- Pas de notification de rappel dans le webmail ; le téléphone sonne.
- Pas d'agenda dérivé (anniversaires) ni d'abonnement externe (webcal) dans les tranches 5a–5d.
- Pas de préférence de fuseau horaire stockée.
- Pas de moteur de récurrences maison.

## Ce que 5a doit trancher en premier

Dans l'ordre, parce que chacun conditionne le suivant :

1. La vérification lecture → écriture d'Ical.Net sur trois fichiers réels (décision 4).
2. Le schéma : `calendars`, `calendar_events` et sa table fille des participants, avec les
   colonnes de la décision 1 et les colonnes DAV de 4c (`dav_name`, `sync_sequence`, empreinte),
   l'état de synchro, les tombes et les révisions par agenda.
3. Le contrat de l'API des occurrences (fenêtre, forme d'une occurrence, identification d'une
   occurrence pour « celle-ci seulement »).
