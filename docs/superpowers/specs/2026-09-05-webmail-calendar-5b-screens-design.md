# Agenda 5b — les écrans

**Tranche 5b du projet Agenda 5.** Le cadrage (`2026-09-04-webmail-calendar-5-overview-design.md`)
a fixé les données, le moteur et l'interface ; 5a a livré les fondations sans écran ; cette tranche
livre l'agenda que l'utilisateur voit. Les **maquettes validées**
(<https://claude.ai/code/artifact/e0f2b333-7491-4ac1-9229-8ac31a45f2c7>) et la § L'interface du
cadrage font foi pour tout ce qui se dessine ; ce document ne les répète pas, il tranche ce qu'elles
laissaient ouvert et fixe l'architecture qui les réalise.

## Où en est le projet

5a a posé six tables, le moteur Ical.Net et deux contrôleurs : `GET /api/Calendars` (avec `tz`),
`POST`/`PUT`/`DELETE` d'un agenda, `PUT …/Visible`, `GET …/Export`, `POST …/Import` ; et pour les
événements `GET /api/Calendar/Events?from=&to=&tz=` (la fenêtre, une occurrence par instance),
`GET …/Search?q=` (un résultat par événement, à sa prochaine occurrence), `GET`/`POST`/`PUT`/`DELETE`
d'un événement, `PUT` et `DELETE` prenant une **portée** (`This`, `ThisAndFollowing`, `All`), un
`instanceId` pour les deux premières, et `ifHash` sur le `PUT`. Le contrat frontend est écrit
(`src/frontend/src/modules/calendar/calendarTypes.ts`, bloc agenda d'`api.js`). L'entrée « Agenda »
du rail mène encore à `ComingSoon`.

`calendar-5a-residuals.md` liste sept points que l'écran doit savoir ; chacun est repris ici à
l'endroit où il compte.

## Ce que fait la tranche

Les dix planches : semaine, jour, mois et liste « à venir » ; barre latérale avec mini-mois et
agendas ; bulle d'aperçu ; éditeur avec la question de portée ; glisser-déposer et
redimensionnement ; recherche ; import et export ; les cinq écrans de téléphone. Trois petits
ajouts backend que l'API de 5a ne couvrait pas (§ Backend). À la fin, l'agenda est utilisable au
quotidien dans le webmail, et seulement là.

## Décisions

### 1. Deux colonnes dans l'outlet, la sélection dans l'URL

`CalendarLayout` construit ses colonnes dans l'outlet du shell comme le courrier et les contacts :
la barre latérale (240 px, mini-mois puis agendas) et la grille avec sa barre d'outils. Sous
1024 px la barre latérale va dans le `ContextDrawer` que les deux autres modules utilisent, ouvert
par le ☰ de la barre d'outils.

**La vue et la date sont des paramètres de recherche** : `/calendar?view=week&date=2026-09-14`,
comme `?folder=&uid=` pour le courrier. `date` est le jour d'ancrage — la semaine, le mois ou les
30 jours qui le contiennent — et vaut aujourd'hui quand il manque. `view` prend `day`, `week`,
`month`, `list` ; quand il manque, c'est la dernière vue choisie sur cet appareil
(`localStorage` `calendar.view`, comme les tailles de volets), `week` la première fois sur grand
écran et `month` sur téléphone. Sur téléphone `week` n'existe pas et se lit `day`. Le bouton
Retour du navigateur remonte ainsi de vue en vue, et une semaine a une adresse qu'on peut coller
dans un mail.

**Ni la bulle ni la sélection d'un événement ne sont dans l'URL** : consulter est un geste, pas un
lieu. L'éditeur, lui, est une route (décision 6).

### 2. La grille est maison, pas une bibliothèque

Pas de FullCalendar ni d'équivalent. Les maquettes fixent la grille au pixel sur les tokens du
webmail (56 px par heure, gouttière de 56 px, bandeau sur `--surface-sunken`, quatre rendus
d'événement) ; une bibliothèque impose ses propres classes, ses propres tailles et son propre
modèle d'événement, et chaque écart avec la maquette se paierait en surcharges CSS fragiles. Ce
qu'une bibliothèque apporterait — le placement des chevauchements, le glisser-déposer — tient dans
deux modules purs testés unitairement :

- `overlapLayout.ts` : dans une colonne, les événements dont les intervalles se touchent forment
  une grappe ; dans la grappe, chaque événement prend la première sous-colonne libre en partant du
  plus tôt, et toutes les sous-colonnes ont la même largeur (partage égal, tranché sur la maquette).
  Un événement de moins de 20 px est dessiné à 20 px, sinon un quart d'heure de 14 px ne se voit
  pas ; le titre apparaît à 40 px, l'heure et le lieu à 58 px.
- `gridGeometry.ts` : minutes ↔ pixels, l'accrochage au quart d'heure (14 px), la colonne sous un
  pointeur. Le glisser-déposer et le redimensionnement (décision 9) ne font que l'appeler.

Le trait de l'heure courante se recalcule chaque minute ; la grille s'ouvre défilée sur 07:00.

### 3. Un événement daté sur plusieurs jours

Le cadrage laissait deux rendus candidats. Règle retenue, celle de Google :

- **Vingt-quatre heures ou plus** : l'événement monte dans le bandeau « journée entière » et
  s'étire sur tous les jours qu'il touche, avec son heure de début dans le libellé
  (« 09:00 Salon »). Un salon de trois jours avec des heures se lit comme un salon de trois jours.
- **Moins de vingt-quatre heures en franchissant minuit** : deux morceaux, un par colonne
  (22:00–24:00 puis 00:00–02:00), qui portent le même identifiant d'occurrence, s'allument ensemble
  au survol et ouvrent la même bulle. Une soirée n'est pas une journée entière.

En vue mois, tout événement de plus d'un jour est une puce pleine étirée sur ses jours, comme une
journée entière. Pas de troisième cas, pas de seuil réglable.

### 4. La fenêtre demandée, et l'erreur qu'elle peut rendre

L'écran demande **une fenêtre par vue**, élargie d'un jour de chaque côté : la journée entière est
jugée sur ses minuits UTC côté serveur (résidu 5a), et le jour de marge est ce qui garantit que le
30 septembre n'échappe pas à une semaine qui finit le 30. Semaine et jour : du premier jour visible
moins un au dernier plus un ; mois : six rangées toujours demandées, même quand cinq suffisent, pour
que passer d'un mois à l'autre ne change pas la forme de la requête ; liste : de ce matin à trente
jours. Les instants `from`/`to` sont les minuits locaux du navigateur convertis en UTC ; `tz` est
le fuseau du navigateur. Ce que le serveur rend au-delà de l'écran est filtré côté client.

`windowOf.ts` calcule tout ça et n'a pas d'autre rôle. La clé de cache est la fenêtre elle-même,
donc deux semaines consécutives sont deux entrées et revenir en arrière ne recharge pas.

Un `400` « The window holds too many occurrences; narrow it » **s'affiche comme état d'erreur de
la grille**, avec le message et un bouton Réessayer, jamais en rétrécissant la fenêtre en douce :
une grille à qui il manque la moitié de ses instances sans le dire est le défaut que le plafond
existe pour éviter, et six semaines à vingt mille occurrences ne se rencontrent pas dans un agenda
personnel. Les autres refus de chargement suivent la même bande d'erreur.

### 5. Les fuseaux à l'écran et dans l'éditeur

**L'écran est dans le fuseau du navigateur** (décision 6 du cadrage), lu une fois par
`Intl.DateTimeFormat().resolvedOptions().timeZone` : une occurrence datée se place par son
`startUtc`, une journée entière par ses dates, une flottante par son `localStart` tel quel.

**L'éditeur montre les heures dans le fuseau de l'événement**, pas dans celui du navigateur.
`fields.start` et `fields.end` sont des heures murales dans `fields.timeZone`, et le contrat de
« celle-ci et les suivantes » exige de renvoyer l'instance **telle que le maître la produit**
(résidu 5a) — convertir pour afficher, puis reconvertir pour envoyer, est exactement la façon de se
tromper d'une heure à la bascule d'été. Quand ce fuseau n'est pas celui du navigateur, une ligne en
muet sous les heures le dit (« Times in America/New_York ») ; l'événement le garde à
l'enregistrement. Pas de sélecteur de fuseau : aucun agenda de référence n'en propose un sur le
formulaire simple, et le cadrage n'ajoute aucune préférence tant qu'un cas réel n'en réclame pas.

**Un événement flottant reçoit le fuseau du navigateur quand on le rouvre**, et cesse d'être
flottant à l'enregistrement. C'est une décision, pas un détail : un fichier importé sans `TZID`
disait « 9 h, où que vous soyez », et après un passage par l'éditeur il dira « 9 h à Bruxelles ».
L'alternative — exiger un fuseau de l'utilisateur sur un champ qu'il ne voit nulle part ailleurs —
ferait échouer en 400 le simple fait de corriger une faute dans un titre. À l'écran rien ne bouge,
puisque le flottant était déjà dessiné à son heure murale dans le fuseau du navigateur.

### 6. L'éditeur est une route : modale sur grand écran, plein écran sur téléphone

`/calendar/new` et `/calendar/:id/edit`, deux routes de plus pointées sur le même `CalendarLayout`
paresseux, exactement le mécanisme de `/mail/compose` et de `/contacts/:id/edit`. À partir de
640 px la grille reste montée et l'éditeur est la modale maison par-dessus (`.modal`, 560 px de
mesure via `--field-w` sur ses rangées, jamais une largeur en dur — le contrat de `modal.css`) ; sous
640 px il prend l'écran, Save dans l'en-tête et ✕ à droite comme la maquette, et `AppShell` retire
`BottomNav` tant qu'il est ouvert, la condition qu'il applique déjà au composeur.

Une route plutôt qu'un état local, pour trois raisons : le bouton Retour d'Android ferme l'éditeur
au lieu de quitter l'agenda ; un événement a une adresse ; et le composeur et la fiche contact ont
déjà montré que c'est le mécanisme qui tient. `?instance=<instanceId>` désigne l'occurrence
éditée d'un récurrent ; `/calendar/new` reçoit son pré-remplissage (début, fin, journée entière)
par des paramètres de recherche (`start`, `end`, `allDay`) et non par l'état de navigation
(`location.state`), qui ne survit pas à un rechargement : un brouillon ouvert doit se rouvrir
tel quel après un F5, et c'est ce qui l'emporte sur une URL qu'on ne partage pas. Sans
paramètre, un nouvel événement commence à la prochaine heure
ronde et dure une heure, dans l'agenda utilisé en dernier sur cet appareil (`calendar.lastUsed`,
`default` la première fois).

L'éditeur **charge `GET /api/Calendar/Events/{id}` et l'attend** avant de monter, comme la fiche
contact : il sème son état une fois. Pour une occurrence, le début et la fin viennent de
l'occurrence — cherchée dans la fenêtre déjà chargée, sinon par une fenêtre d'un jour autour de
`instance` — jamais d'une valeur recalculée à partir du maître (résidu 5a). Un identifiant que
l'API ne résout plus toaste et redirige vers `/calendar` avec `replace`, comme un contact disparu.

**Pas de bouton Annuler**, la ✕ ferme (règle maison). Un formulaire modifié qu'on ferme demande
d'abord « Discard changes? » par le `DeleteConfirmModal` partagé ; un formulaire vierge se ferme
sans rien dire.

### 7. Ce que l'éditeur ne sait pas exprimer est verrouillé, pas raboté

Le cadrage dit qu'une règle de répétition ou un rappel plus riches que l'éditeur « s'affichent en
texte et se conservent tels quels ». 5a conserve bien les rappels étrangers, mais **rabote la
règle** : le lecteur projette la `RRULE` dans le sous-ensemble (`fields.repeat`), et le composeur
réécrit cette projection au premier enregistrement. Un « deuxième et dernier lundi de mars et
septembre » devient un mensuel après un passage par l'éditeur pour corriger un titre. Deux ajouts
backend (§ Backend) ferment ça :

- `EventDetail.repeatIsExact` dit si `fields.repeat` rend la règle **sans perte**. Quand c'est
  faux, le sélecteur « Repeat » est verrouillé sur le texte de la règle (« Custom rule from another
  app — kept as is »), et l'éditeur envoie `keepRepeat: true` : le composeur ne touche pas à la
  `RRULE`. Choisir quand même une autre répétition est possible d'un clic sur « Replace », qui
  déverrouille et envoie une règle du sous-ensemble.
- `EventDetail.foreignAlarms` liste en texte les rappels que l'éditeur ne porte pas (par mail,
  déclencheur absolu ou relatif à la fin, déjà acquitté) ; ils s'affichent en muet sous les
  rappels, « Kept from another app », et restent dans le fichier.

Le `STATUS:CANCELLED` d'un client suit la règle du cadrage : barré et compté Libre à l'écran, et
l'enregistrer écrit la disponibilité choisie.

### 8. La portée d'un récurrent : ce que l'écran envoie

Un événement est récurrent à l'écran quand son occurrence porte un `instanceId` non vide. Sur un
tel événement, Enregistrer, Supprimer et un dépôt après glisser ouvrent d'abord `ScopeModal` —
« Save a recurring event », trois boutons empilés, « This occurrence only » en primaire — et
l'appel part avec la portée choisie et l'`instanceId` de l'occurrence cliquée. `eventForm.ts` est
le seul endroit qui assemble le corps du `PUT` (`EventWrite` + `scope` + `instanceId` + `ifHash`)
et du `DELETE`.

**Changer d'agenda exige la portée entière** (résidu 5a) : si le sélecteur d'agenda a changé, la
modale n'offre que « All occurrences », les deux autres grisés avec la raison — plutôt que laisser
l'API répondre 400 après coup. Un `409` (l'événement a bougé depuis l'ouverture) toaste « This
event changed elsewhere; reopen it » et recharge le détail ; le formulaire n'est pas perdu tant que
l'utilisateur ne ferme pas.

Supprimer un événement **non** récurrent passe par `DeleteConfirmModal` : contrairement à un
message, qui va à la corbeille, un événement supprimé n'a pas d'annulation. Pour un récurrent, la
question de portée est la confirmation.

### 9. Glisser, redimensionner, créer

Sur les vues semaine et jour, à partir de 640 px : un événement se déplace au pointeur (seuil de
4 px avant que le geste compte, accrochage au quart d'heure et à la colonne), se redimensionne par
une poignée de 6 px à son pied (jamais sous quinze minutes), et un glisser sur une case vide crée un
événement de la plage tracée. Un clic sur une case vide crée une heure à partir de la demi-heure
cliquée. Une puce du bandeau se déplace de jour en jour ; elle ne se redimensionne pas. Échap
annule un geste en cours. **Rien de tout ça sur téléphone**, où le doigt qui glisse fait défiler ;
le bouton flottant « + » y crée.

Le déplacement et le redimensionnement sont **optimistes** : l'occurrence est dessinée à sa nouvelle
place pendant l'appel, par `setQueryData` sur la fenêtre, et revient avec un toast si l'API refuse.
Un geste qui attendrait le serveur pour bouger ne se sentirait pas comme un geste. Tout le reste
— l'éditeur, les agendas — attend la réponse et invalide `onSettled`, comme les contacts. Un
glisser ne change ni l'agenda ni la journée entière ; il ne peut donc jamais tomber sur un 400 de
portée.

Un événement déplacé garde son fuseau : le corps envoyé est l'heure murale du nouvel emplacement
dans `fields.timeZone`, obtenue en ajoutant le décalage du geste à l'instance d'origine — pas une
heure du navigateur reconvertie. Pour ça, le dépôt charge le détail de l'événement avant le `PUT`
(il y faut `ifHash` de toute façon).

### 10. La bulle d'aperçu, et pas sur téléphone

Au clic sur un événement, à partir de 640 px : la bulle de la maquette (300 px, style du menu
déroulant), ancrée à droite de l'événement et retournée à gauche quand la place manque, close par
Échap, un clic dehors ou un clic sur un autre événement. Titre — « (No title) » quand il manque, ce
qu'un override sans `SUMMARY` rend légitimement (résidu 5a) —, date et heure, puis lieu, rappel,
récurrence et agenda avec leurs icônes, Modifier en primaire, Supprimer en fantôme. Double-clic sur
l'événement ouvre l'éditeur directement.

**Sur téléphone, taper un événement ouvre l'éditeur**, sans bulle : les listes du mois et du jour
montrent déjà heure, lieu et couleur, et une bulle de 300 px sur 360 est une modale qui ne dit pas
son nom.

### 11. La recherche

Le champ de la barre d'outils lance `GET …/Search?q=` à la validation (Entrée ou 300 ms sans
frappe, comme le carnet). **Les résultats remplacent la grille**, comme les résultats de recherche
remplacent la liste de courrier : une bande « N results · Clear » puis une ligne par résultat —
date de la prochaine occurrence, heure, pastille et nom de l'agenda, titre, lieu. Un clic va à
cette date dans la vue courante et ouvre la bulle sur l'occurrence. Le serveur en rend 200 au plus ;
au-delà la bande le dit. La recherche ne filtre pas par agenda visible : on cherche ce qu'on a,
pas ce qu'on regarde.

### 12. Les agendas

- **Afficher / masquer** : la case colorée de la maquette appelle `PUT …/Visible` ; l'état est
  celui du serveur (5a l'a mis en colonne pour que le téléphone et le webmail voient la même chose),
  invalidé `onSettled`.
- **Créer** : le « + » de l'en-tête « Calendars » ouvre `CalendarDialog` — nom, couleur. La
  couleur se choisit parmi **douze pastilles** fixes (celles que proposent Apple et Nextcloud, dans
  les tokens de la maquette) ou un champ hexadécimal pour qui veut la sienne ; un agenda venu du
  téléphone avec une couleur hors liste la garde, et le champ la montre.
- **Renommer, couleur** : deux entrées du kebab qui ouvrent le même `CalendarDialog`, le champ
  concerné focalisé.
- **Supprimer** : `DeleteConfirmModal`, « … and every event in it » ; grisé avec sa raison sur
  l'agenda par défaut (`isDefault`), que l'API refuse de toute façon.
- **Exporter** : `GET …/Export` par `downloadBlob`, nommé par le serveur.
- **Importer** : depuis le kebab d'un agenda ; le fichier `.ics` choisi, `ImportDialog` demande
  « Into *Personal* » ou « Into a new calendar », le second pré-rempli avec le nom et la couleur
  que le fichier porte — `NAME`/`X-WR-CALNAME`, `COLOR`/`X-APPLE-CALENDAR-COLOR`, lus sur le texte
  déplié côté navigateur, quatre lignes que la ressource n'a pas besoin du moteur pour trouver. Le
  premier appelle `POST /api/Calendars/{id}/Import`, le second la nouvelle route
  `POST /api/Calendars/Import` (§ Backend). Le rapport s'affiche dans `CalendarImportReportModal`
  : créés, remplacés, tâches et journaux ignorés, en erreur, puis chaque ligne refusée avec son rang
  — le modèle du carnet, avec ses deux compteurs de plus. L'`<input type="file">` est vidé avant
  l'envoi, pour la raison que le carnet documente.

Le kebab, dans l'ordre : Rename…, Colour…, Import…, Export, Delete….

### 13. Les rappels

« Reminder » est un sélecteur par rappel, « + Add » en ajoute un, cinq au plus (c'est la limite de
Google ; un téléphone qui en a mis plus les garde, l'éditeur les montre tous et n'en ajoute pas).
Présélections, en minutes avant le début, seule forme que l'API porte (résidu 5a) :

| Événement daté | Journée entière |
|---|---|
| At time of event (0), 5, 10, 15, 30 minutes, 1 hour, 2 hours, 1 day (1440), 2 days (2880), 1 week (10080) | The day before at 18:00 (360), The day before at 09:00 (900), 2 days before at 09:00 (2340), 1 week before at 09:00 (9540) |

Une valeur venue d'un client qui n'est dans aucune liste s'affiche comme sa propre option
(« 45 minutes before ») et se conserve. Basculer « All day » convertit le rappel le plus proche de
sens (15 min → la veille à 18 h, et retour) plutôt que de le perdre. Le défaut d'un nouvel
événement daté est 15 minutes ; une journée entière naît sans rappel et Libre, comme le cadrage
le fixe.

### 14. Localisation

`calendarLocale.ts`, pur, rend tout ce que l'écran demande à partir de deux sources (cadrage,
fonctionnalité 8) :

- **la langue d'interface** (`useLocale`), pour les noms de mois et de jours, le titre de la barre
  d'outils (`Intl.DateTimeFormat.formatRange`, « 14 – 20 September 2026 »), « Today · » et
  « Tomorrow · » ;
- **la région du navigateur** (`navigator.language`), pour le premier jour de la semaine et le
  nombre de jours minimal de la première semaine (`Intl.Locale.prototype.getWeekInfo` quand il
  existe, sinon une table par région : dimanche pour US, CA, JP, BR, IL et quelques autres, lundi
  en repli), et pour le format horaire (`hourCycle` résolu, 12 h ou 24 h).

Le numéro de semaine se calcule avec ces deux paramètres, ce qui rend l'ISO pour un Belge
(lundi, quatre jours) et la numérotation américaine pour un Américain, sans cas particulier. Les
catalogues `en` et `fr` gagnent un espace `calendar`, sous la parité et la typographie française
que `parity.test.ts` impose.

### 15. Téléphone

Sous 640 px : `.seg` compact Month / Day / List à côté du ☰ ; **Mois** compact (jour sélectionné
plein, jusqu'à trois points de couleur) avec la liste du jour tapé dessous ; **Jour** avec sa bande
de 7 jours qu'on glisse de semaine en semaine (un `scroll-snap` horizontal, pas de gestion de geste
maison) au-dessus de la grille horaire — le même `DayColumn` que la semaine, en une colonne, sans
glisser-déposer ; **Liste** des trente prochains jours groupée par jour. Le tiroir de 320 px reprend
la barre latérale ; le bouton flottant « + » crée ; `BottomNav` reste tant qu'aucun éditeur n'est
ouvert. Champs de l'éditeur à 16 px et 44 px de haut, pour que le clavier d'iOS ne zoome pas et que
le doigt vise. Entre 640 et 1023 px, l'agenda est celui du grand écran avec la barre latérale en
tiroir, glisser-déposer compris.

Les cas de géométrie vont dans `probes/mobile-layout.html` — grille du jour à 360 et 320, mois
compact, éditeur plein écran, barre d'outils — parce que jsdom ne voit aucune mise en page.

## Backend : trois ajouts

Tout le reste de l'API est celle de 5a, inchangée.

1. **`POST /api/Calendars/Import?tz=`**, multipart : `file`, `displayName` (obligatoire, mêmes
   règles que `CalendarWrite`), `color` (facultatif). Crée l'agenda avec le fuseau donné — le
   plafond des vingt agendas s'applique et refuse en 400 **avant** de lire le fichier — puis verse
   les ressources exactement comme la route par agenda. Réponse `201` `{ calendar, report }` ; un
   import qui échoue ligne à ligne laisse l'agenda créé avec son rapport, comme il le laisserait
   sur un agenda existant.
2. **`EventWrite.keepRepeat`** (`bool`, `false` par défaut) : à `true`, le composeur laisse la
   `RRULE` du maître telle quelle et ignore `repeat`. Sans effet sur la portée `This`, qui n'écrit
   jamais de règle ; sur `ThisAndFollowing`, la série coupée garde la règle d'origine, que le
   découpeur copie déjà.
3. **`EventDetail.repeatIsExact`** (`bool`) et **`EventDetail.foreignAlarms`** (`string[]`).
   Le premier vaut vrai quand recomposer `fields.repeat` rend une `RRULE` équivalente à celle du
   maître (comparaison des motifs après normalisation, pas du texte) ; un événement sans règle le
   rend vrai. Le second décrit chaque `VALARM` qui n'est pas un `DISPLAY` à déclencheur relatif au
   début, en texte court (« EMAIL, 1 day before », « DISPLAY, 2026-09-14 09:00 UTC »).

## Fichiers

`src/frontend/src/modules/calendar/` :

| Fichier | Rôle |
|---|---|
| `CalendarLayout.tsx` | les colonnes, le tiroir, `?view=&date=`, les routes de l'éditeur, le bouton flottant |
| `CalendarSidebar.tsx`, `MiniMonth.tsx`, `CalendarDialog.tsx`, `ImportDialog.tsx`, `CalendarImportReportModal.tsx` | la barre latérale et ses dialogues |
| `CalendarToolbar.tsx` | Today, chevrons, titre, recherche, `.seg` des vues |
| `WeekView.tsx`, `DayColumn.tsx`, `AllDayBand.tsx`, `NowLine.tsx` | semaine et jour (grand écran et téléphone) |
| `MonthView.tsx`, `UpcomingList.tsx`, `SearchResults.tsx` | les autres vues |
| `EventChip.tsx` | un événement dessiné, dans ses quatre rendus |
| `EventPreview.tsx` | la bulle |
| `EventEditor.tsx`, `RecurrenceEditor.tsx`, `ReminderList.tsx`, `ScopeModal.tsx` | l'éditeur |
| `phone/PhoneMonth.tsx`, `phone/DayStrip.tsx` | ce qui n'existe que sous 640 px |
| `useDragEvent.ts`, `useResizeEvent.ts`, `useCreateByDrag.ts` | les gestes, sur `gridGeometry` |
| `calendarLocale.ts`, `windowOf.ts`, `overlapLayout.ts`, `gridGeometry.ts`, `eventForm.ts`, `recurrenceSummary.ts`, `reminderPresets.ts`, `multiDay.ts`, `icsHeader.ts` | modules purs, un test chacun |
| `queries.ts` | TanStack Query, clés `['calendar', accountId, …]` |
| `calendarTypes.ts` | + `EventUpdateBody`, `repeatIsExact`, `foreignAlarms`, `keepRepeat`, `CalendarImportOutcome` |

Ailleurs : `routes.tsx` (trois routes sur le layout paresseux, `ComingSoon` retiré pour l'agenda),
`AppShell.tsx` (retenir `BottomNav` sur l'éditeur), `api.js` (`importCalendarAsNew`),
`src/icons/` (`BellIcon`, `RepeatIcon`, `ClockIcon`, `PlusIcon`, `CloseIcon`), `src/styles/calendar.css`,
`src/locales/{en,fr}/calendar.json`, `probes/mobile-layout.html`, `docs/architecture-calendar.md`
importé par le `CLAUDE.md` du frontend. Backend : `CalendarsController`, `CalendarEventsController`,
`EventWrite`, `EventDetail`, `IcsReader`, `IcsComposer`, `EventRequestValidator`, leurs tests.

## Tests

- **Purs** : `overlapLayout` (grappes, sous-colonnes, largeur égale, hauteur minimale) ;
  `windowOf` (les trois formes, la marge d'un jour, la conversion UTC à la bascule d'été) ;
  `calendarLocale` (lundi/dimanche, 12/24 h, numéro de semaine ISO et américain, repli sans
  `getWeekInfo`) ; `eventForm` (daté ↔ journée entière, flottant qui reçoit le fuseau, portée et
  `instanceId`, agenda changé ⇒ `All` seul, `keepRepeat`) ; `recurrenceSummary` (les cinq
  présélections et le personnalisé) ; `multiDay` (23 h 59 en colonnes, 24 h en bandeau) ;
  `icsHeader` (nom et couleur d'un vrai export, lignes pliées, fichier sans les deux).
- **Composants** : la grille place une occurrence à la bonne hauteur et dans la bonne colonne ;
  le bandeau reçoit une journée entière et un événement de 24 h ; la bulle s'ouvre, se ferme, ouvre
  l'éditeur ; l'éditeur sème depuis le détail et l'occurrence, verrouille « Repeat » quand
  `repeatIsExact` est faux, ouvre `ScopeModal` sur un récurrent et grise ce qu'il faut ; la barre
  latérale coche, renomme, importe ; la recherche remplace la grille ; le téléphone bascule
  `week` en `day` et ouvre l'éditeur au tap ; un `409` toaste et recharge.
- **Backend** (xUnit) : la route d'import crée puis verse, refuse au plafond sans lire le fichier ;
  `keepRepeat` laisse une règle riche intacte ; `repeatIsExact` vrai sur un hebdomadaire, faux sur
  `outlook-2003.ics` et sur un `BYDAY=2MO` ; `foreignAlarms` sur `google-alarm.ics` et
  `thunderbird.ics` (corpus réel).
- **Géométrie** : `probes/mobile-layout.html` à 360, 320, 768 et 1024.

## Ce que la tranche ne fait pas

- L'onglet Sync et la colonne `caldav_enabled` : DDL et interrupteur de 5c.
- Aucune notification de rappel dans le webmail ; les invitations (5e) ; un agenda
  « Anniversaires » ; l'abonnement webcal ; le partage.
- Pas de rappel « après » ni de répétition infra-journalière dans l'éditeur (résidu 5a).
- Pas de sélecteur de fuseau, pas de préférence de premier jour ni de format horaire.
- Pas de glisser-déposer sur téléphone, pas de redimensionnement dans le bandeau.
