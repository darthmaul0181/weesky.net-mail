# Agenda 5b — ce que la tranche laisse derrière elle

Le tri de fin de tranche, sur le modèle de `calendar-5a-residuals.md` : ce que les sept tâches ont
délibérément différé, ce que les sondes ont montré et qui n'est pas corrigé, et ce dont 5c hérite.
Il existe pour que la tranche suivante n'ait pas à redécouvrir à ses frais ce qui a déjà coûté une
mesure ou un arbitrage.

Rappel de périmètre : 5b est **l'écran**. Le contrat d'API, les gardes iCalendar et les refus
nommés viennent de 5a et ne sont pas rediscutés ici ; ce qui est ouvert côté serveur reste dans
`calendar-5a-residuals.md`.

## Ce que l'utilisateur peut voir, et qui n'est pas corrigé

| Point | Où | Ce que ça donne à l'écran, et pourquoi c'est resté |
|---|---|---|
| **Les rappels que l'éditeur ne sait pas modifier sont invisibles dans la bulle** | `EventPreview.tsx`, `EventEditor.tsx` (`editor.foreignAlarms`) | La bulle lit `hasAlarm` et dit « Reminder set », un point. L'éditeur, lui, distingue les rappels qu'il gère de ceux qu'un autre client a écrits et qu'il conserve sans y toucher. Un utilisateur qui n'ouvre que la bulle croit donc pouvoir régler un rappel qui, en réalité, ne lui appartient pas. La sortie est un `foreignAlarms` sur `Occurrence` — donc une colonne de plus dans la réponse de fenêtre — et pas un changement d'écran : la bulle ne requête rien, par construction. |
| **La recherche n'est pas filtrée par les agendas cochés** | `CalendarLayout.tsx` (`visible` n'entre pas dans `SearchResults`) | Décocher un agenda le retire de la grille et **pas** des résultats de recherche : le même événement disparaît d'un écran et reste sur l'autre. Deux lectures se défendent (« la recherche cherche partout » contre « la case est un filtre global ») et l'arbitrage n'a pas été posé. Le correctif est d'une ligne si c'est la seconde qui gagne — `searchQuery.data` passé par le même prédicat que la fenêtre. |
| **Ni 12/24 h ni premier jour de la semaine ne sont préférençables** | `calendarLocale.ts` (`hourCycleOf`, `weekRulesOf`), lus sur `navigator.language` | Un francophone en Belgique a du 24 h et une semaine qui ouvre le lundi parce que sa **langue de navigateur** le dit, pas parce qu'il l'a choisi. Un compte qui veut l'inverse n'a aucune porte. C'est une paire de clés dans `usePreferences` plus deux replis — le module lit déjà les deux valeurs en un seul endroit, donc le branchement est court ; c'est le registre backend qui manque. |
| **Le redimensionnement au doigt ne se termine presque jamais** | `useResizeEvent.ts`, `.event-resize-handle` (`calendar.css`) | Le plancher de 44 px sur la poignée existe sous `@media (hover: none)`, mais sur un écran tactile le défilement reprend le geste (`pointercancel`) avant qu'il n'aboutisse. Le rendre praticable demande `touch-action: none` sur la poignée, ce qui coûte le défilement vertical sur le dernier tiers de chaque puce : un arbitrage produit, pas une correction. **Inerte par décision**, donc, et non par oubli. |
| **Aucun chemin clavier pour déplacer ou redimensionner** | `useDragEvent.ts`, `useResizeEvent.ts` | La poignée est `aria-hidden` dans un `<button>`, et les trois gestes sont des gestes de pointeur. Le chemin clavier reste l'éditeur, qui porte les mêmes heures — donc rien n'est inatteignable, mais rien n'est direct non plus. |
| **`import.errorLine` interpole la prose anglaise du serveur** | `CalendarImportReportModal.tsx`, catalogue `import.errorLine` | Un écran français affiche « Entrée 12 — The resource carries no VEVENT. ». C'est le point ouvert n° 2 de la tâche 3, et il est resté : la route est celle de `ImportReportModal.reasonText` côté contacts (une table de refus stables vers des clés de catalogue), et elle demande treize entrées dans deux langues plus un test. **Les refus sont maintenant énumérés** (relevés dans `IcsGuards`/`IcsDocument`/`CalendarEventStore`, à faire correspondre à l'identique pour les huit premiers, par préfixe pour les trois interpolés) : `The body is not iCalendar text.` · `The resource carries no VEVENT.` · `The resource puts a VTODO, VJOURNAL or VFREEBUSY beside its VEVENT.` · `The components do not share one UID.` · `The resource carries more than one component without a RECURRENCE-ID.` · `A component carries no UID.` · `A UID or attendee address is too long` · `The recurrence cannot be expanded` · `The collection holds VEVENT only, not VTODO, VJOURNAL or VFREEBUSY.` · `VERSION is '…', not 2.0.` (interpolé) · `The resource is … bytes, over the … allowed.` (interpolé) · `Over … instances in the year following DTSTART.` (interpolé) · plus `CalendarEventStore.CapReached`. |
| **Une rangée de mois sous ~93 px ravale son « +N more »** | `.month-cell` (`calendar.css`), sonde `calendar-grid.html` | Le budget de la cellule (3 px de marge, 1 px d'écart, rond de 18, bandeau de 18, puces à 16, compte à 15 = 93) est mesuré, mais il est serré : sur une fenêtre plus basse que ~560 px de plateau, le compte repart hors de la cellule, qui est `overflow: hidden`. La sortie est de descendre à deux puces sous un seuil, ce qui demande une mesure côté JS que la règle maison proscrit. Le palier téléphone ne l'aggrave pas : il ne dessine plus `MonthView` du tout. |
| **L'éditeur avec `Custom…` ouvert fait 1140 px de haut** | `RecurrenceEditor.tsx`, sonde de la tâche 5 | Sur un écran de 768 px de haut, la modale dépasse la fenêtre. Rien ne fuit — le défilement est celui de `.modal-overlay`, comme le contrat maison l'exige — mais la récurrence personnalisée demande de faire défiler un dialogue, ce qui est la forme la moins agréable de ce panneau. Le palier téléphone y échappe : là, l'éditeur est déjà l'écran entier et son formulaire est la bande qui défile. |
| **Le sélecteur de mois du téléphone laisse ~132 px à la liste du jour sur un écran de 640** | `PhoneMonth`, mesuré (sonde `calendar phone month-360`) | Barre d'outils 121 + sélecteur 330 + barre d'onglets 57 = 508 d'un écran de 640 : la liste du jour sélectionné ouvre sur deux ou trois lignes avant de devoir défiler. C'est la conséquence des **six rangées toujours dessinées** (`monthGrid`, pour que la grille ne change pas de hauteur entre septembre et octobre) et du plancher de 48 px par cellule. Un écran de 844 (390×844, l'iPhone courant) n'a pas le problème. Descendre à cinq rangées quand le mois y tient ferait bouger la grille sous le pouce : laissé tel quel. |
| **Le ruban des jours ne change pas de jour quand on le fait glisser** | `DayStrip.tsx` | Un balayage montre la semaine voisine ; la grille sous lui ne bouge qu'au **tapotement** d'un jour. C'est voulu — regarder n'est pas choisir, la règle que `MiniMonth` applique déjà — mais deux surfaces montrent alors deux semaines différentes tant que rien n'est tapé. La sortie serait un observateur de défilement qui recentre, c'est-à-dire exactement le code de geste que ce ruban existe pour ne pas écrire. |

## Ce que les sondes ont montré

Les nombres et le mode d'emploi sont dans le rapport de la tâche 7 ; ce qui suit est ce qui reste
ouvert après la passe.

- **Deux planchers tactiles ne sont visibles qu'en émulation tactile.** `.calendar-step` (36 px) et
  `.editor-more` (20 px de haut) n'atteignent 44 px que par `@media (hover: none)`. Une souris —
  et donc `probes/mobile-layout.html` piloté sans `Emulation.setTouchEmulationEnabled` — les lit à
  36 et 20 et les compte `undersized`. **Ce n'est pas de la rouille** : c'est la même distinction
  que `src/frontend/CLAUDE.md` documente déjà pour les grappes révélées au survol. Le rapport de
  la tâche 7 porte les deux lectures, souris et doigt ; toute relecture de ces cas doit préciser
  laquelle.
- **Le `clipped` du ruban vaut quatre largeurs de fenêtre**, à chaque taille (1280 à 320, 1440 à
  360, 1560 à 390). C'est son propre défilement (`scroll-snap-type: x mandatory`, une semaine par
  arrêt, **cinq** semaines rendues) et non une fuite. Le cas est isolé
  (`calendar day strip-360`) précisément pour que ce nombre ne masque pas un vrai débordement
  ailleurs dans la grille. Un nombre qui n'est **pas** quatre fois la largeur pilotée est la
  régression — et il l'est aussi si `BEFORE`/`AFTER` changent dans `DayStrip.tsx` sans que le fixe
  de la sonde suive.
- **`calendar preview-768` ne se lit qu'à 768.** Sous 640 px il n'y a pas de bulle du tout — un
  tapotement ouvre l'éditeur — donc le `left` du fixe, calculé pour une fenêtre de tablette, sort
  de l'écran par construction et `escape.right` y vaut 232.
- **Aucune sonde de contraste sur les deux écrans neufs, et la vraie raison est un calcul, pas une
  réutilisation.** Le rond plein (`--action-primary-fg` sur `--action-primary`) et les jours hors
  mois (`--text-muted` sur `--surface`) sont bien des paires déjà mesurées. **La paire neuve est
  `color: var(--action-primary)` en TEXTE sur `--surface`** (`calendar.css`, le numéro et
  l'initiale du jour *courant* dans les deux écrans) : le module ne la peignait nulle part ailleurs
  — `.mini-day.is-today` n'est qu'un anneau et garde `--text`. Elle a été calculée sur les **huit
  palettes**, en clair et en sombre : pire cas **4,84** (slate, clair) et **5,41** (ink, sombre),
  donc au-dessus du plancher de 4,5 partout, et aucun cas de sonde n'est requis. C'est une marge
  courte : une palette future qui éclaircirait `--action-primary` doit refaire ce calcul.
- **Les contrôles de `.field-h` sont à 14 px sur téléphone, pas à 16.** Le bloc téléphone
  d'`index.css` pose `input, select, textarea { font-size: 16px }` — la parade au zoom
  automatique d'iOS — mais sa spécificité (0,0,1) perd contre
  `:is(.field-h, .field-v) input:not(…) { font-size: 14px }` (0,2,1) du même fichier. Mesuré :
  chaque champ de l'éditeur d'événement calcule **14 px** pour **44 px** de haut. Le plancher
  tactile tient donc, la parade au zoom non — et c'est vrai de **toutes** les rangées `.field-h`
  de l'application, pas seulement de l'agenda. Le corriger est une remontée de spécificité d'une
  règle, mais elle grossit le texte de tous les formulaires de tous les modules sous 640 px : ça
  demande une passe de sonde complète (courrier, contacts, réglages), donc sa propre tranche. Le
  corriger dans `calendar.css` seul ferait de l'agenda le seul module dont les champs diffèrent.

## Dette de style et de nommage

- **`0 8px 24px rgba(0, 0, 0, 0.18)` est écrit trois fois** — `shell.css` deux fois
  (`:201`, `:287`) et `calendar.css` une (`.event-preview`). C'est l'ombre des surfaces qui
  « sortent » de la page (menus, bulles). Elle veut un token, `--shadow-pop`, et un token de
  couleur se déclare dans les fichiers de palette : la règle de thème du dépôt fait de ce
  changement une modification des six palettes plus leur test de parité, pas une ligne dans
  `calendar.css`. Hors périmètre d'une tranche d'écrans, listé ici pour que la quatrième copie ne
  soit jamais écrite.
- **La bulle garde un pointeur sur son ancre pour le clic-dehors seul.** Sa position, elle, vient
  désormais du rectangle lu au clic (`Preview.rect`) et non du nœud au montage, ce que la
  décision 11 a forcé : un résultat de recherche vide la liste en ouvrant sa bulle, donc la puce
  a quitté l'écran quand la mesure aurait lieu. Reste qu'un nœud d'ancre remplacé sous la bulle
  fait rendre `false` à `anchor.contains` : un clic sur la puce d'origine fermerait la bulle au
  lieu de la laisser. Non observé — React réutilise le nœud — mais pas prouvé.
- **`ContactImportError` sert aussi les agendas**, `Line` y étant le rang de la ressource dans le
  fichier et non un numéro de ligne. Le type est partagé avec la tranche contacts et ses valeurs
  traversent l'API ; le renommer appartient à la tranche qui touchera vraiment au type.

## Deux mineurs backend différés

Relevés par la revue de la tâche 1, triés « résidus » par la revue finale : ni l'un ni l'autre ne
se voit à l'écran, et les deux coûtent plus à corriger qu'ils ne rendent.

- **Une branche 404 morte dans `CalendarsController.ImportAsNew`**
  (`CalendarsController.cs`, après `store.ListAsync`). L'agenda vient d'être créé pour cet
  utilisateur, donc `calendars.FirstOrDefault(c => c.Id == created.Value)` le trouve toujours et le
  `NotFoundEnveloppe` est inatteignable. Le retirer demande d'assumer un `First()` qui lèverait
  (500) au lieu de répondre (404) sur un cas qui ne peut pas arriver : la branche morte est le
  moins mauvais des deux, et elle est documentée ici plutôt que supprimée.
- **`IcsDocument.MasterOf` est recalculé quatre fois par `CalendarEventStore.GetAsync`** — une
  fois pour la `RRULE` du détail, puis une fois chacun dans `IcsReader.Read`, `RepeatIsExact` et
  `ForeignAlarms`. C'est un `First(e => e.RecurrenceIdentifier is null)` sur les composants d'une
  ressource, pour un GET unitaire : il n'y a pas de fenêtre derrière. Le fermer veut dire passer
  le `CalendarEvent` maître en paramètre aux trois lecteurs, donc changer trois signatures
  internes et leurs tests **sans mesure préalable**. À mesurer d'abord.

## Ce dont 5c hérite

5c ouvre les routes CalDAV. Ce que 5b lui laisse, en plus de tout ce que `calendar-5a-residuals.md`
lui adresse déjà (`MKCALENDAR`/`PROPPATCH`/`DELETE`, `caldav_enabled`, les codes `IcsPrecondition`
en XML, `RDATE;VALUE=PERIOD`, `RANGE=THISANDFUTURE`, l'`instanceId` orphelin) :

- **Le webmail écrit désormais des ressources qu'un client tiers va relire.** Tout ce que
  l'éditeur compose — la coupure de série de `ThisAndFollowing`, les `EXDATE` d'une suppression
  d'occurrence, le bloc `VTIMEZONE` — est ce que DAVx⁵ et Thunderbird liront en 5d. Le
  « cinquième cas » que 5a renvoie à 5d se vérifie contre **ces** écritures-là.
- **Le verrou `keepRepeat` est le contrat entre les deux mondes.** Une règle que l'éditeur ne sait
  pas dessiner revient au serveur inchangée, et c'est ce qui garantit qu'un aller-retour par le
  webmail ne dégrade pas une récurrence écrite par un téléphone. Une route DAV qui réécrirait la
  ressource entière au lieu de la fusionner casserait cette garantie sans qu'aucun test d'écran ne
  rougisse.
- **Le `ifHash` figé au semis est la brique d'optimistic concurrency côté client.** 5c doit
  l'exposer en ETag DAV sur la même valeur, sinon deux clients auront deux notions de « la version
  que j'ai lue » pour une même ressource.
- **La fenêtre demandée est plus large que la fenêtre dessinée** (un jour de rab de chaque côté,
  `SLACK_DAYS`). Un `calendar-query` DAV qui prendrait les bornes de l'écran au pied de la lettre
  perdrait les instances qu'un fuseau lointain fait basculer d'un jour — c'est le même piège que
  la note 5a sur les minuits UTC, vu depuis l'autre bout.
- **Le module ne connaît aucun participant en écriture.** Les participants sont affichés en lecture
  seule (`editor.attendeesReadOnly`) et aucun `ATTENDEE`/`ORGANIZER` n'est composé par cet écran :
  la planification (invitations, `SCHEDULE-STATUS`, la boîte aux lettres iTIP) n'a pas de client
  et n'est donc pas encore contrainte par un écran existant.
