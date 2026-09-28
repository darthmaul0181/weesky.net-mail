# Agenda « Anniversaires » — conception

Date : 2026-09-28 · Branche : `birthdays-calendar` · Planche validée :
https://claude.ai/artifact/JgjmGBma857e5YZ4hj3pq3

La spec d'ensemble de l'agenda (2026-09-04, § « Hors périmètre ») avait écarté « l'agenda dérivé
(anniversaires) » des tranches 5a–5d. Cette tranche le livre.

## Le but

Voir l'anniversaire de ses contacts dans l'agenda **du webmail et du téléphone**, avec un rappel,
sans rien saisir deux fois. Le téléphone compte autant que le webmail : pour certains
utilisateurs, c'est leur seul outil. Et un téléphone Android dont les contacts sont synchronisés
par CardDAV n'a en général aucun agenda d'anniversaires à lui.

Critères de réussite :

- une date de naissance saisie sur une fiche, que ce soit dans le webmail ou sur le téléphone,
  apparaît dans l'agenda « Anniversaires » du webmail et, à la synchronisation suivante, sur
  chaque client CalDAV ;
- modifier ou retirer la date, ou supprimer le contact, fait de même ;
- aucun client ne peut écrire dans cet agenda ; toute correction passe par la fiche du contact ;
- le rappel est réglable, et le changer met à jour tous les appareils.

## Les décisions

### 1. Un vrai agenda stocké, tenu à jour par les contacts

Deux voies s'offraient :

- calculer les événements à chaque requête sans rien stocker ;
- les stocker dans `calendar_events` et les tenir à jour à chaque écriture de contact.

La seconde est retenue. Tout ce que CalDAV sait déjà faire (`calendar-query`, expansion,
`free-busy`, `sync-collection`, empreintes) travaille sur des lignes stockées. Un agenda virtuel
obligerait à refaire chaque rapport pour des événements imaginaires, dans la partie la plus
pointilleuse du projet.

Le risque de la voie retenue est qu'un chemin d'écriture de contact oublie l'anniversaire. La
décision 4 y répond.

### 2. L'agenda : une ligne de `calendars` d'un genre nouveau

Trois colonnes s'ajoutent à `calendars` :

| Colonne | Type | Rôle |
|---|---|---|
| `kind` | `ENUM('regular','birthdays') NOT NULL DEFAULT 'regular'` | le genre de l'agenda |
| `birthday_reminder` | `ENUM('none','same_day','day_before','week_before') NULL` | le rappel ; `NULL` pour un agenda ordinaire |
| `birthday_language` | `CHAR(2) NULL` | `fr` ou `en`, la langue de la description ; `NULL` pour un agenda ordinaire |

**Au plus un agenda d'anniversaires par utilisateur.** C'est la colonne générée
`birthdays_owner` (`IF(kind='birthdays', user_id, NULL)`, `PERSISTENT`) qui le garantit, par un
index unique. MariaDB accepte plusieurs `NULL` dans un index unique, donc les agendas ordinaires ne
sont pas concernés.

**Le nom DAV** est `birthdays`. Si un client a déjà créé un agenda de ce nom (l'index
`ux_calendars_user_dav_name` l'interdirait), c'est `birthdays-2`, puis `-3`, etc. C'est `kind`
qui identifie l'agenda, jamais son nom DAV. Un `MKCALENDAR` ultérieur sur `birthdays` reste
permis : il reçoit le refus ordinaire « ressource existante » si le nom est pris.

**Les valeurs par défaut à la création :**

- nom affiché « Anniversaires » ou « Birthdays », dans la langue de la requête ;
- couleur framboise `#be185d` ;
- rappel `same_day` ;
- `time_zone` : celui du navigateur, comme pour `default` (spec 5, décision 6) ;
- `sort_order` : après le dernier agenda existant.

### 3. L'interrupteur et la création

**La préférence.** Une préférence utilisateur `calendar.birthdays`, de valeurs `on` et `off`,
vaut `on` par défaut.

- Tant qu'elle vaut `on`, l'agenda est créé **à la première requête du webmail qui en a
  besoin**, par le même chemin que `EnsureDefaultAsync` : l'ouverture du module Agenda, et
  l'activation de CalDAV dans l'onglet Synchronisation. Il est rempli dans la même transaction
  (décision 5, « reconstruction »).
- Passer à `off` supprime la ligne. Les événements, l'état de synchronisation et les tombstones
  partent avec elle par les `ON DELETE CASCADE` existants. Pour les clients, la collection
  disparaît.
- Repasser à `on` recrée l'agenda et le remplit. Le nom et la couleur personnalisés sont perdus,
  et c'est assumé.

Un client CalDAV ne crée jamais l'agenda : sans passage par le webmail, il n'y a pas de
navigateur pour donner un fuseau (même règle que pour `default`).

**L'agenda ne se supprime pas autrement.**

- Un `DELETE` CalDAV sur la collection reçoit `403` avec `DAV:need-privileges`.
- Le `DELETE` de l'API est refusé (`400`, comme pour `default`). Le webmail n'offre que
  « Désactiver… », qui passe par l'interrupteur.
- Supprimer l'agenda n'écrit aucune révision (`calendar_revisions`) : il ne contient que des
  copies.

### 4. Un seul point de passage, appelé par chaque chemin, et un garde-fou

Les contacts sont écrits par `ContactStore` (création, modification, suppression, suppression
groupée, import, rattrapage) et par `DavContactWriter` (`PUT`, `DELETE`, « tout supprimer »).
`ContactGroupStore` n'écrit que des groupes, qui n'ont pas d'anniversaire.

**Le point de passage.** `IBirthdayProjector.ProjectTrackedAsync` est appelé par chacun de ces
chemins **juste avant son `SaveChanges`**, dans la transaction qu'il a déjà ouverte :

1. il relève, dans le suivi des modifications d'EF, les `Contact` ajoutés, supprimés, ou modifiés
   sur `birthday`, `display_name`, `first_name`, `last_name`, `nickname`, `organization` ou `kind` ;
2. il calcule l'événement attendu de chacun et le compare à celui qui est stocké ;
3. s'il y a une différence, il prend un rang de l'agenda d'anniversaires et pose ses propres
   modifications (événements, tombstones), qui partent avec le `SaveChanges` du contact.

Ce qui échoue d'un côté échoue de l'autre.

**Pourquoi pas un crochet dans `SaveChanges` lui-même**, qui aurait évité d'écrire un appel par
chemin : le rang d'un agenda (`ICalendarSyncStore.NextSequenceAsync`) exige une transaction ouverte
par l'appelant, que certains enregistrements de contacts n'ont pas (l'étoile favori). Et les tests
remplacent ce distributeur de rangs, ce qu'un `DbContext` ne peut pas recevoir.

**Le garde-fou.** Quand un projecteur existe dans la portée (toujours en production),
`PreferencesDbContext.SaveChangesAsync` refuse, par une `InvalidOperationException`,
d'enregistrer un `Contact` pertinent (au sens du point 1) que le projecteur n'a pas vu. Un chemin
oublié, aujourd'hui ou demain, échoue au premier test qui l'emprunte au lieu de laisser un
anniversaire périmé en silence.

**Le rattrapage** (`BackfillAsync`) n'ouvrait pas de transaction ; il en ouvre une par lot, comme
l'import.

**Les opérations en masse** (`ExecuteDelete`, `ExecuteUpdate`) contournent le suivi des
modifications, donc le projecteur et le garde-fou. Aucune ne touche aujourd'hui `contacts`. Un
test d'architecture le garantit (voir Tests).

Si l'utilisateur n'a pas d'agenda d'anniversaires (préférence `off`, ou agenda pas encore créé),
le projecteur ne fait rien.

### 5. L'événement

Un contact produit un événement si et seulement si :

- son `kind` est `individual` (pas un groupe) ;
- son `birthday` se lit comme une date complète (`19860621`, `1986-06-21`, ou une date-heure dont
  on ne garde que la date) ou comme une date sans année (`--0621`, `--06-21`) ;
- cette date existe réellement (pas de 31 février).

Une valeur illisible ne produit rien. La fiche l'affiche telle quelle, comme aujourd'hui.

**L'identité de l'événement :**

- `UID` : `birthday-<id du contact>` ;
- nom DAV : `<id du contact>.ics`.

Les deux sont stables tant que le contact existe.

**Le fichier :**

```
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//Scotty webmail//Birthdays//EN
BEGIN:VEVENT
UID:birthday-<id>
DTSTAMP:<instant de l'écriture>
DTSTART;VALUE=DATE:19860621
DTEND;VALUE=DATE:19860622
RRULE:FREQ=YEARLY
SUMMARY:🎂 Alice Martin
DESCRIPTION:Naissance : 1986
TRANSP:TRANSPARENT
CLASS:PRIVATE
X-SCOTTY-CONTACT-ID:<id>
BEGIN:VALARM
ACTION:DISPLAY
DESCRIPTION:🎂 Alice Martin
TRIGGER:-PT15H
END:VALARM
END:VEVENT
END:VCALENDAR
```

Les règles de construction :

- **Titre** : 🎂, une espace, puis le nom du contact : `display_name`, sinon prénom et nom, sinon
  surnom, sinon organisation.
- **Date sans année** : `DTSTART` en 1604, la convention d'Apple pour ce cas, et pas de
  `DESCRIPTION`.
- **Né un 29 février** : `RRULE:FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-1`, c'est-à-dire le dernier jour
  de février. L'anniversaire tombe le 29 les années bissextiles et le 28 les autres.
- **Description** : `Naissance : 1986` en français, `Born 1986` en anglais, selon
  `birthday_language`.
- **`TRANSP:TRANSPARENT`** : l'anniversaire ne rend pas indisponible dans les réponses
  `free-busy`.
- **`CLASS:PRIVATE`**, par prudence si le partage d'agendas arrive un jour.
- **Rappel** (heure locale du jour de l'événement) : `none`, pas de `VALARM` ; `same_day`,
  `TRIGGER:PT9H` ; `day_before`, `TRIGGER:-PT15H` ; `week_before`, `TRIGGER:-P6DT15H`.
- **`X-SCOTTY-CONTACT-ID`** permet au webmail d'ouvrir la fiche. L'API l'expose sur chaque
  occurrence de cet agenda (`contactId`), avec l'année de naissance (`birthYear`, absente pour une
  date sans année) d'où le webmail calcule l'âge. Ces deux champs ne sont posés que pour un
  événement de l'agenda d'anniversaires, jamais d'après le seul fichier : un client CalDAV qui
  écrirait la même ligne dans un autre agenda n'obtient rien.

**Écrire seulement ce qui change.** Le fichier est reconstruit, puis comparé par empreinte
(`ics_hash`) à celui qui est stocké.

- Identique : aucune écriture, la séquence ne bouge pas et les téléphones ne retéléchargent rien.
  C'est ce qui rend un import de 500 contacts, ou un changement de numéro de téléphone, sans
  effet sur l'agenda.
- `DTSTAMP` ne compte pas dans l'empreinte. Sinon, chaque reconstruction serait une modification.

**Les révisions.** Aucune ligne de `calendar_revisions` n'est écrite pour cet agenda : la source
est le contact, qui a ses propres révisions.

**La reconstruction complète** relit tous les contacts de l'utilisateur et aligne l'agenda : elle
ajoute, réécrit (si l'empreinte diffère) et retire. Elle est déclenchée :

- à la création de l'agenda (décision 3) ;
- au changement de `birthday_reminder` ;
- au changement de `birthday_language`, c'est-à-dire quand l'utilisateur choisit explicitement
  `fr` ou `en` pour `ui.language`. Le choix `auto` ne change rien : l'agenda garde la langue
  qu'il a.

### 6. En lecture seule, partout

**CalDAV.**

- `current-user-privilege-set` de la collection et de ses membres : `read`, `read-current-user-privilege-set`
  et `write-properties` (la collection seulement, pour le nom et la couleur).
  `bind`, `unbind` et `write-content` n'y figurent pas. L'iPhone grise alors « Modifier ».
- `PUT`, `DELETE` d'un membre, `MOVE`/`COPY` vers la collection, et `DELETE` de la collection :
  `403` avec `DAV:need-privileges`, qui nomme le privilège manquant.
- `PROPPATCH` de `displayname`, `calendar-color` ou `calendar-order` : permis. Celui de
  `calendar-timezone` aussi, comme sur les autres agendas.
- Un `PROPPATCH` sur `calendar-description` est permis et sans effet sur les événements.
- `schedule-calendar-transp` vaut `transparent` : un client qui calcule lui-même la
  disponibilité ignore l'agenda.
- L'agenda n'est jamais la boîte de réception ni la cible par défaut du scheduling
  (`schedule-default-calendar-URL` continue de désigner `default`).

**API.**

- Créer, modifier, déplacer ou supprimer un événement de cet agenda, ou y importer : `403`,
  avec un code d'erreur propre (`calendar_read_only`) que le webmail traduit.
- Exporter reste permis.
- `PUT /api/Calendars/{id}` accepte en plus `birthdayReminder`, refusé (`400`) sur un agenda
  ordinaire.
- `PUT /api/Calendars/Birthdays?tz=…&lang=…` avec `{ "enabled": true|false }` est l'interrupteur :
  il écrit la préférence, puis crée et remplit, ou supprime, l'agenda. La préférence
  `calendar.birthdays` figure dans le registre, donc dans `GET /api/Preferences`, mais
  `PUT /api/Preferences` la refuse : elle ne change que par cette route, qui a les effets.
- `GET /api/Calendars` accepte `lang` (`fr`/`en`), la langue de l'agenda s'il est créé à cet
  appel. La langue retenue est `ui.language` si elle vaut `fr` ou `en`, sinon `lang`, sinon `en`.

**La réponse de la liste des agendas** gagne deux champs : `kind` et, pour les anniversaires,
`birthdayReminder`. Pour un agenda ordinaire, `birthdayReminder` est omis (l'API omet les `null`).

### 7. L'écran

Tout est dessiné sur la planche validée (lien en tête), à partir des valeurs réelles du thème.

**Barre latérale.** « Anniversaires » s'affiche parmi les agendas, avec sa case et sa couleur.
Les agendas ordinaires gardent leur menu actuel (Renommer…, Couleur…, Importer…, Exporter,
Supprimer…). Celui des anniversaires propose :

- **Réglages…** : la fenêtre habituelle (nom, couleur), plus une ligne **Rappel**, une
  `MenuSelect` avec les choix Aucun / Le jour même à 9:00 / La veille à 9:00 / Une semaine avant ;
- **Exporter** ;
- **Désactiver…**, qui remplace « Supprimer… ».

« Importer… » n'y figure pas.

**Désactiver.** La confirmation se titre « Désactiver les anniversaires ? ». Son texte : « Les
anniversaires disparaîtront du webmail et de vos appareils. Vos contacts ne sont pas modifiés.
Vous pourrez réactiver cet agenda dans Paramètres › Général. » Le bouton d'action est primaire,
pas « danger » : rien n'est perdu.

**Paramètres › Général.** Une nouvelle section « Agenda », entre « Disposition » et
« Confidentialité », avec l'interrupteur « Agenda des anniversaires » et son aide : « Les dates de
naissance de vos contacts, dans le webmail et sur vos appareils synchronisés. »

**Grille.**

- Un anniversaire est une puce du bandeau « Journée », de style « libre » (liseré sans
  remplissage, puisque `TRANSP:TRANSPARENT`).
- On ne peut ni la glisser ni l'étirer.
- Un clic dans un jour vide ne propose jamais cet agenda : il est absent du sélecteur d'agenda de
  l'éditeur.
- Un double-clic ou Entrée sur la puce (qui ouvrent l'éditeur pour un autre événement) ouvre la
  fiche du contact sur bureau, et l'écran de consultation sur téléphone.

**Bulle d'aperçu (bureau).**

- Titre : « 🎂 Alice Martin ».
- Date : « mercredi 30 septembre 2026 · Journée entière ».
- Une ligne gâteau « 40 ans », dans la couleur du texte : c'est l'information qu'on vient
  chercher. L'âge est calculé pour l'occurrence affichée. La ligne est absente pour une date sans
  année.
- Une ligne cloche qui dit le réglage réel (« Rappel la veille à 9:00 »), plutôt que le générique
  « Rappel activé » ; absente pour `none`.
- Les lignes « Chaque année » et « Anniversaires ».
- Un seul bouton, **Ouvrir la fiche**, qui mène à `/contacts?id=<id>`.
- Ni « Modifier » ni « Supprimer ».

**Téléphone (le webmail en petit écran).** Toucher un événement y ouvre l'éditeur plein écran, pas
une bulle. Pour un anniversaire, c'est un **écran de consultation** en lecture seule qui s'ouvre à
cette place : en-tête « Anniversaire » avec ✕, puis le contenu de la bulle, puis « Ouvrir la
fiche » sur toute la largeur.

**« À venir » et la recherche** montrent les anniversaires comme les autres événements.

**Textes.** Toutes les chaînes existent en `fr` et en `en`, dans `calendar.json` et
`settings.json`.

## Hors périmètre

- Les autres dates d'une fiche (`ANNIVERSARY`, dates personnalisées) : le modèle de contact ne
  les porte pas.
- Un rappel à une heure choisie librement ; plusieurs rappels.
- Masquer certains contacts de l'agenda, ou choisir un groupe.
- Un agenda d'anniversaires partagé entre utilisateurs.

## Base de données

Le script SQL manuel est à documenter comme pour les tranches précédentes, et à reporter dans
`install/install.sql` :

```sql
ALTER TABLE calendars
  ADD COLUMN kind ENUM('regular','birthdays') NOT NULL DEFAULT 'regular' AFTER user_id,
  ADD COLUMN birthday_reminder ENUM('none','same_day','day_before','week_before') NULL,
  ADD COLUMN birthday_language CHAR(2) NULL,
  ADD COLUMN birthdays_owner CHAR(36) AS (IF(kind = 'birthdays', user_id, NULL)) PERSISTENT,
  ADD UNIQUE KEY ux_calendars_birthdays_owner (birthdays_owner);
```

La relation EF entre `Calendar` et `CalendarEvent` doit rester déclarée : sans arête, EF ordonne
les `INSERT` par nom de table, et la création suivie du remplissage dans la même transaction
échouerait sur la clé étrangère. Les tests InMemory ne l'attrapent pas ; un test sur MariaDB (ou
la recette) le vérifie.

## Tests

**Projection (unitaires, sans base).** Pour une fiche donnée, le fichier attendu :

- date complète, date sans année, date-heure de téléphone ;
- 29 février, 31 février (rien), valeur illisible (rien) ;
- groupe ou organisation (rien) ;
- chacun des quatre rappels ;
- les deux langues ;
- une empreinte identique quand seul `DTSTAMP` change.

**Point de passage.** Pour **chaque** chemin d'écriture, avec l'agenda présent, l'événement
suit le contact :

- webmail : création, modification (date ajoutée, changée, retirée ; nom changé ; autre champ
  seulement, donc aucune écriture), suppression, suppression groupée ;
- import ; rattrapage ;
- CardDAV : `PUT` nouveau, `PUT` modifié, `DELETE`, « tout supprimer ».

Plus :

- l'échec du contact n'écrit pas l'anniversaire ;
- sans agenda, rien n'est écrit ;
- un test d'architecture échoue si un `ExecuteDelete` ou un `ExecuteUpdate` vise `Contacts`.

**Synchronisation.** Après une modification de date, `sync-collection` rend le membre modifié.
Après un retrait de date ou une suppression de contact, il le rend supprimé (tombstone). Après un
changement de rappel, il rend tous les membres, et seulement eux.

**Lecture seule.** `PUT`, `DELETE` d'un membre ou de la collection et `MOVE` vers la collection
rendent `403` avec `need-privileges`. `PROPPATCH` du nom et de la couleur passe. Les écritures de
l'API rendent `403 calendar_read_only`. L'import est refusé.

**Interrupteur.**

- `off` supprime l'agenda et ses lignes.
- `on` le recrée et le remplit.
- L'ouverture du module ne le recrée pas tant que la préférence vaut `off`.
- Une collision de nom DAV avec un agenda client `birthdays` donne `birthdays-2`.
- Deux créations concurrentes n'en font qu'un : l'index unique tranche, et la seconde relit.

**Écran (Vitest).**

- La bulle : âge, âge absent sans année, texte du rappel, bouton unique.
- L'écran de consultation sur téléphone.
- Le menu de la barre latérale : Désactiver, pas d'Importer, pas de Supprimer.
- La fenêtre Réglages avec le rappel.
- L'interrupteur de Paramètres › Général.
- L'agenda absent du sélecteur de l'éditeur.
- La puce ni glissable ni étirable.

**Recette (à la main, sur l'environnement dev).**

- iPhone : l'agenda apparaît en lecture seule, le rappel sonne, un anniversaire ajouté dans le
  webmail arrive au téléphone et vice versa.
- Android avec DAVx⁵ : le même parcours, et vérifier qu'aucun agenda natif ne fait doublon.
- Thunderbird : l'agenda s'affiche en lecture seule.
