# Agenda 5c — ce que la tranche laisse derrière elle

Le tri de fin de tranche, sur le modèle de `calendar-5b-residuals.md` : ce que les huit tâches ont
délibérément différé ou assumé, ce que les sept revues de code ont trouvé et classé « ACCEPTABLE »
plutôt que corrigé, ce que les tests n'ont pas pu couvrir, et ce dont 5d hérite. Il existe pour que
la tranche suivante n'ait pas à redécouvrir à ses frais ce qui a déjà coûté une mesure ou un
arbitrage.

Rappel de périmètre : 5c ouvre `/dav/calendars/` aux clients CalDAV sur le socle WebDAV partagé
avec le carnet de 4c. Les résidus 5a et 5b qu'elle referme ou assume sont marqués « 5c » dans
`calendar-5a-residuals.md` et `calendar-5b-residuals.md` directement, à côté du point qu'ils
adressaient ; ils ne sont pas répétés ici sauf quand une revue en a précisé la portée.

## Ce qu'un client peut rencontrer, et qui n'est pas corrigé

| Point | Où | Ce que ça donne, et pourquoi c'est resté |
|---|---|---|
| **`REPORT` sur une ressource inexistante répond `404` là où 4c rendait un `207`/`403`** | `DavControllerBase.ReportAsync` | La base résout la ressource avant `ServeReportAsync` (T1) : un `addressbook-multiget` adressé à une carte disparue mais dont le corps liste des cartes existantes ne rend plus rien du tout, là où 4c servait les cartes trouvées. Plus juste au sens de RFC 3253 § 3.6 (REPORT porte sur *la* ressource de la Request-URI) et de RFC 9110 § 15.5.5 (le 404 sur une ressource absente) — référence corrigée en 5d —, mais c'est un changement de comportement du carnet que la suite ne couvrait pas et qu'aucun client réel (multiget/sync-collection adressés à la collection) ne rencontre. |
| **La lecture d'état de synchro que `ContextOrNotFoundAsync` fait uniformément, alors que seul `expand-property` s'en sert** | `CardDavController`/`CalDavController.ContextOrNotFoundAsync` | Un `SELECT` par clé primaire de plus sur `PROPPATCH`/`REPORT`, deux sur `sync-collection` (qui relit l'état dans sa propre transaction). Construire le contexte paresseusement remettrait « quel verbe sers-je » dans le crochet qu'on vient d'en sortir (T1), pour un aller-retour supplémentaire sur une table à une ligne par utilisateur/agenda, toujours en buffer pool. Non corrigé, des deux côtés du protocole. |
| **Trois actions de forme d'URL échappent à `SwitchedOn()`** | `CardDavController.GetCollection` et les fourre-tout `405`, leurs équivalents agenda | Un appelant dont CalDAV (ou CardDAV) est éteint lit encore `405 + Allow + DAV:` sur ces formes précises. Aucune lecture de store, la réponse ne dépend que de la forme de l'URL et de l'uid du demandeur : pas de fuite, juste une incohérence de principe avec le reste de la surface. |
| **Les deux branches mortes de `CardDavOutcomeTranslator`** (`UnsupportedComponent`, `TooManyInstances`) | `Services/CardDav/CardDavOutcomeTranslator.cs` | `DavContactWriter` ne produit jamais ces deux statuts ; les deux `case` existent pour que le `switch` reste exhaustif (`EveryEnumValue_IsPinnedToItsOwnCodeByName`, `NoTwoStatuses_ShareTheirWholeAnswer`) et rendent un élément **CalDAV** depuis un traducteur **CardDAV**. Elles laissent une arête de dépendance `Services/CardDav → Services/CalDav` que la règle « le carnet ne connaît pas l'agenda » aurait préférée absente. Sortie propre le jour où c'est possible : porter l'élément par l'`outcome` des deux côtés, comme `Precondition` le fait déjà côté agenda, ou déplacer les deux `XName` vers `Services/Dav`. |
| **Le rollback du rang sur un refus décidé *dans* la porte transactionnelle** | `CalendarEventStore.InTransactionAsync(commit)` | La garantie « un échec commité par erreur revient sur `NextSequenceAsync`, jamais un rang perdu pour rien » est réelle (`UidConflict`, `CollectionFull`, `NotFound`, un second `If-Match`) mais seule une base réelle l'exerce : le provider InMemory n'a ni transaction ni rollback. Le test qui l'accompagne (`TheGate_ConsultsItsCommitPredicate_WithWhatTheBodyAnswered`) n'atteste que la **consultation** du prédicat, pas l'effet du rollback lui-même. |
| **Une couleur ou un rang hors forme sont *ignorés*, pas refusés, à la création d'un agenda** | `MkCalendarRequest.Parse` | La table du § 11 de la spec ne donne à un `MKCALENDAR`/`MKCOL` aucun refus par propriété hors `supported-calendar-component-set` : une couleur illisible replie sur `null` (la couleur suivante de la palette), un rang illisible de même. Un `PROPPATCH`, lui, refuse la propriété seule. Sans conséquence pour un client réel — DAVx⁵, iOS et Thunderbird sortent tous d'un sélecteur, aucun n'envoie de texte libre — et le mécanisme (`WriteCreationRefusalAsync(refused: […])`) est déjà en place si la conformité stricte RFC 4791 § 5.3.1.1 est un jour voulue. |
| **`catch (DbUpdateException) → NameTaken` de `CreateRowAsync`, et `NameTaken` de `EnableAsync`** | `CalendarStore.cs`, `DavCredentialStore.cs` | Le provider InMemory n'applique aucun index unique : la course entre deux `MKCALENDAR`/deux premières activations sur le même segment/protocole n'est vérifiable qu'en base réelle. Le filet InMemory est la lecture faite dans la transaction, qui couvre le cas non concurrent seul. |
| **La branche `403 valid-calendar-data` d'un `MKCALENDAR`/`MKCOL`** | `CalDavController` | Tout `Result` d'échec du store autre que `CapReached`/`NameTaken` y tombe — un filet, jamais atteint aujourd'hui puisque `Parse` replie toute couleur illisible sur `null` avant que `BadColour` ne puisse remonter du store. Gardée pour qu'un futur refus n'y devienne pas un `500`. |
| **Le `403 max-instances` d'un membre expansé est écrit *dans* le multistatus, pas en refus global** | `MultigetReport.WriteMemberAsync`, réutilisé par `CalendarQueryReport` | Le `207` est déjà ouvert quand le résolveur d'un membre précis dépasse le plafond d'expansion : juger le refus plus tôt supposerait de pré-expanser les cinq mille membres avant d'écrire le premier octet. La forme retenue (`<D:response><D:href/><D:status>403</D:status><D:error>…</D:error></D:response>`) est admise par RFC 4918 § 14.24 et déjà ce que `sync-collection` sert pour une collection non synchronisable — un client CalDAV la rencontre donc déjà ailleurs. Écart à la spec § 8, qui annonçait un `403` global. |
| **`calendar-data` est servie telle que stockée dans un `sync-collection`**, là où 4c décision 8 rendait un `404` propstat pour `address-data` | `EventMemberSource.Prepare` | La table de l'événement (T3) tient déjà `calendar-data`, hors `allprop` ; un `sync-collection` qui la nomme passe par la même table qu'un `PROPFIND`, et la cacher aurait demandé un retrait puis une remise artificielle dans `Missing`. RFC 6578 § 3.2 ne restreint aucune propriété (vérifié dans le texte), et sabre la sert de même. Le pire cas — 5000 événements × 1 Mio — est identique à celui que 4c refusait pour le carnet, mais aucun client visé (DAVx⁵, iOS) ne nomme `calendar-data` dans un `sync-collection` : les deux enchaînent un `multiget` à la place. Cas théorique, gardé en connaissance de cause. |
| **La marge d'un jour du marcheur consomme le plafond d'expansion** | `OccurrenceExpander.Expansion.Periods` (marge avant), `ExpandedCalendarData.Expand` | Le marcheur part de `from − 1 jour` et prend `Cap` occurrences sur cette fenêtre élargie ; si la troncature tombe dans `[from, to[`, le compte rendu est `Cap − N_marge_avant`, strictement sous le plafond — un document amputé sans le dire, silencieusement. Mesuré (revue T6) : pour une fenêtre d'un an à la densité maximale que le `PUT` admet (10 000/an), la perte plafonne à **un jour, ~27 instances, ~0,3 %**, et n'apparaît que dans une bande de densité étroite juste sous la porte du `PUT` — en dessous de ~9 975/an la marge arrière absorbe la coupe et la perte est nulle. Refermer demanderait que l'expander signale sa propre troncature (API de 5a), non fait. |
| **La marge d'un jour borne aussi le `time-range` d'un `VALARM` à un déclencheur d'au plus 24 h** | `OccurrenceExpander.AlarmFires` | Un `TRIGGER:-P1W` avant une instance elle-même hors fenêtre est manqué — une réponse incomplète plutôt que fausse, et le bornage est assumé : une semaine de marge referait relire tout l'agenda à chaque requête, exactement le défaut que la revue T7 a trouvé bloquant sur la forme sans présélection. |
| **`match-type` est honoré alors que RFC 4791 § 9.7.5 ne le définit pas** (c'est RFC 6352/CardDAV) | `CalendarQueryFilter.ParseTextMatch` | sabre l'ignore silencieusement ; le servir sert exactement ce qu'un client qui l'écrit demande (une égalité plutôt qu'un `contains`), et l'ignorer aurait rendu plus que demandé sans le dire. Choix retenu et distinct de `test="anyof"` (refusé, lui) : l'un est évaluable exactement, l'autre ne l'est pas par ce moteur. **Corollaire mesuré** : la présélection par colonnes `STATUS`/`TRANSP`/`CLASS` du § 8 (`CalendarQueryFilter.Columns`) n'accepte une colonne que sur un `text-match` en égalité *stricte*, laquelle n'existe qu'avec l'attribut `match-type` — qu'aucun client RFC-pur n'écrit. La présélection par colonnes ne se déclenche donc pour **aucun** client conforme visé ; ce qui filtre réellement pour eux est la fenêtre `FirstOccurrence`/`LastOccurrence`. |
| **Un `param-filter` négatif correspond sur un paramètre absent** | `CalendarQueryFilter.MatchesParam` | `<param-filter><text-match negate-condition="yes">…</text-match></param-filter>` sur un paramètre que l'instance ne porte pas rend `true` — sabre dit non (`validateParamFilters` refuse avant même de lire le `text-match`), et le `prop-filter` du même fichier lit l'absence différemment (`instances.Count == 0 → false`, sans regarder la négation). RFC 4791 § 9.7.3 est muet sur ce croisement précis. La réponse actuelle est un surensemble défendable (`PARTSTAT` omis vaut `NEEDS-ACTION`, donc « les participants qui n'ont pas décliné » est plutôt ce qu'un client veut) ; alignement sur sabre en une ligne (`if (named.Count == 0) return false;`) si l'équipe le préfère. |
| **Une ressource non récurrente à plusieurs composants « cette occurrence seulement » reste présélectionnée sur le premier composant** | `DavCalendarReader.CandidatesAsync` (garde `IsRecurring`) | Plusieurs surcharges sans maître, même UID (une forme que le `PUT` n'interdit pas explicitement mais qu'aucun client visé n'écrit) : la garde `IsRecurring` protège une série avec maître, pas ce cas limite. Un `STATUS equals` que seule la seconde surcharge porte ferait tomber la ressource avant que le fichier ne soit lu. Refermer demanderait de ne plus jamais présélectionner sur les colonnes, ce que le carnet a fini par faire mais que l'agenda ne fait pas encore. |
| **La récupération partielle de `calendar-data` (`comp`/`prop` dans le corps), `RANGE=THISANDFUTURE`, les bornes ouvertes fermées à cinq ans, `limit-*`** | `CalendarDataRequest`, `OccurrenceExpander` | Assumés et documentés par la spec § 14 dès le cadrage : § 9.6.1 n'est servi par aucun client visé (comp/prop sont lus puis ignorés, la ressource entière sort — spec § 7) ; `RANGE=THISANDFUTURE` est lu par Ical.Net mais jamais appliqué à l'expansion (résidu 5a, commentaire sur `OccurrenceExpander`) ; une borne de **`time-range`** absente se ferme à `OccurrenceExpander.MaxSpan` (cinq ans) plutôt que de refuser — **rouvert en 5d, décision 11** : DAVx⁵ pose cette question à chaque synchro avec son réglage par défaut, la borne sera servie ouverte pour le `calendar-query` ; le `time-range` d'un `VALARM` reste fermé et `free-busy-query` continue de refuser une borne absente, résidu 5d — ; l'`expand`, lui, **refuse** en `valid-filter` une borne manquante, ce que § 9.6.5 demande (`CalendarDataRequest.Parse`), et n'est donc pas une divergence : ligne corrigée en 5d, qui l'avait d'abord recopiée ; `limit-freebusy-set`/`limit-recurrence-set` (RFC 4791 § 9.6.6/9.6.7) ne sont lus nulle part. |
| **`calendar-data` est servie aussi dans un `PROPFIND`**, alors que RFC 4791 § 9.6 la réserve aux `REPORT` | `Services/CalDav/CalDavProperties.CalendarTable` (l'événement) | Divergence héritée de 4c, qui sert `address-data` de même. Un client qui ne la nomme pas ne la reçoit pas (hors `allprop`, via `Excluding`) ; celui qui la nomme reçoit ce qu'il a demandé — plus généreux que le RFC, jamais moins. |
| **`calendar-user-address-set` est annoncée alors qu'elle est définie par RFC 6638**, dont la tranche ne sert rien d'autre | `DavPrincipalProperties` | C'est par elle qu'un client se reconnaît participant d'une invitation ; l'absence de `calendar-auto-schedule` dans l'en-tête `DAV:` dit qu'on n'ordonnance rien. Nuance relevée en 5d : RFC 6638 § 2.4.1 dit de la propriété seule « If not present, then the associated calendar user is not enabled for scheduling on the server » — sa présence, lue à la lettre, dit l'inverse du jeton absent ; un client qui lit les deux suit le jeton. Servie quand même parce que DAVx⁵ la lit (`queryEmailAddress`) pour construire ses propres invitations sortantes même sans planification serveur. |
| **Les adresses du principal sont aussi construites sur un `PROPFIND Depth: 0` de `/dav/principals/`**, où aucune table ne les sert | `DavPrincipalController.AddressesOrNullAsync` | La porte s'ouvre sur `kind is Principal or PrincipalCollection` pour garantir « une construction par requête » (l'enfant du `Depth: 1` est un `with` du parent) ; sur la collection en `allprop`, aucune table ne sert `calendar-user-address-set`, donc l'appel à `IAccountInfoProvider` et le `SELECT` sur `sending_identities` sont faits pour rien. Invisible avec `ClaimsAccountInfoProvider` (aucune sortie de processus) ; à mesurer si un fournisseur d'un futur déploiement en coûte une vraie. |
| **Aucun cache d'`AccountInfo` sur `calendar-user-address-set`** | `DavPrincipalController` | Un appel plateforme par cycle de synchro et par appareil (le principal est relu à chaque poll DAVx⁵/Thunderbird). Sans conséquence mesurable avec le fournisseur actuel ; à mesurer en 5d si un fournisseur futur en fait un vrai aller-retour réseau. |
| ~~**`SyncState` (le record `Epoch`/`Seq`/`PrunedBelow`) vit encore sous `Models/Contacts`** alors que trois fichiers de `Services/Dav` (le socle générique, partagé par les deux protocoles) l'importent, et que `Services/Dav/SyncStateConsistencyCheck` (5c) compare la même forme pour un agenda~~ | `Models/Dav/SyncState.cs` | Relevé par la revue de la tâche 1 dès le déplacement du socle ; ni le plan ni la spec ne demandaient ce déplacement précis, donc pas un manquement de 5c — mais le nom du fichier mentait sur ce qu'il sert. **Refermé par la tâche zéro de 5d** : le record est passé sous `Models/Dav/`, à côté de `DavWriteStatus`/`DavWriteOutcome`. |

## Dette de forme mineure

- ~~**Deux constantes `Margin` d'un jour**, désormais solidaires sans être unifiées :
  `OccurrenceExpander.Margin` (privée) et `CalendarEventStore.Margin` (`internal`) valent toutes
  deux `TimeSpan.FromDays(1)`. `AlarmFires` déroule sur la première, `CandidatesAsync`
  présélectionne sur la seconde, et la justesse d'une réponse dépend de leur égalité — comme
  `Shift`/`CapFor`/`Span`, qui ont chacun été unifiés en un seul point durant la tranche, ces deux-là
  ne l'ont pas été. Deux lignes suffiraient à n'en garder qu'une. Relevé en 5d, la même paire
  existe pour la borne de cinq ans : `OccurrenceExpander.MaxSpan` (366 × 5 jours) et le
  `365 × MaxYears` de `CalendarEventStore` — même dette, même remède.~~ **Refermé par la tâche zéro
  de 5d** : une seule marge, un seul empan, tous deux portés par
  `OccurrenceExpander`. Une troisième écriture,
  relevée en 5d, n'en fait **pas** partie : `CalendarEventsController.MaxWindow`
  (365,2425 × `MaxYears`) borne la fenêtre de l'API du webmail et non une réponse DAV ; l'aligner
  élargirait cette borne de quatre jours et retournerait
  `CalendarEventsControllerTests.Window_RefusesMoreThanFiveYears`.
- **`CalDavController.ReadSyncWindowAsync` et son jumeau `CardDavController.ReadSyncWindowAsync`**
  se ressemblent à dix lignes près (ouverture de transaction, lecture de l'état, `ReadWindowAsync`,
  commit) mais divergent sur le point qui compte — `ReadOrCreateStateAsync` contre `ReadStateAsync`
  + `403` journalisé sur une ligne absente — et sur le type de retour. Factoriser coûterait plus
  qu'il ne rapporte pour dix lignes qui ne peuvent pas converger entièrement.
- **N+1 sur `ReadStateAsync` dans le `Depth: 1` du home** (`CalDavController`) : un aller-retour
  par agenda dans la boucle, à l'intérieur de la transaction-instantané. Plafonné par
  `CalendarStore.MaxPerUser` (20), donc au pire vingt allers-retours ; `ICalendarSyncStore` n'expose
  pas de lecture groupée et en ajouter une pour vingt lignes n'est pas rentable.
- **Le `VTIMEZONE` de `calendar-timezone` est régénéré par agenda et par réponse**
  (`CalDavProperties`), bien que la table qui le sert soit mémoïsée par année. Un cache
  `ConcurrentDictionary<(string, int), string>` sur le document sérialisé serait une ligne ; avec
  vingt agendas au plus et le fournisseur actuel, ça ne se mesure probablement pas.

## Dette de forme relevée par les revues, et non refermée

- **`Prepare` renifle une seconde fois le genre du rapport.** `CardMemberSource.Prepare` lit
  `ReportRequest.KindOf(body)` pour ne servir `address-data` que sur un multiget ; T6 a repris la
  même forme dans `EventMemberSource`. La décision « quel rapport suis-je en train de servir » vit
  donc à deux endroits — le contrôleur, qui l'a déjà prise, et la source, qui la reprend. Sans
  conséquence visible, mais c'est un doublon que la règle interdit ailleurs.
- **`DavContactReader.TombstonesAsync` n'a pas d'`AsNoTracking()`** là où ses voisines en ont un.
  Fidèle à son jumeau, donc hors du périmètre de 5c ; une lecture suivie qui n'a rien à suivre.
- ~~**L'ordre des `propstat` d'un `PROPPATCH` mixte (200 avant 403) n'est asserté que par leur
  compte**, pas par leur rang : inverser les deux blocs laisserait la suite verte.~~ **Constat
  faux, relevé en 5d** : `CalDavProppatchTests.TheFiveWritableOnes_Answer200AndTheRest403_InThatOrder`
  compare la séquence `["HTTP/1.1 200 OK", "HTTP/1.1 403 Forbidden"]`, qu'une inversion rougit.
  Rien à faire.
- **`Request.Method == "MKCOL"` est comparé sensiblement à la casse** là où le routeur ASP.NET Core
  ne l'est pas. Aucun client n'écrit `Mkcol`, mais la comparaison ne dit pas la même chose que la
  route qui l'a amenée là.
- **`trace.Responses = 1` sur un corps d'échec de création** qui ne porte aucun `DAV:response` : la
  ligne de journal compte une réponse qui n'existe pas dans le document.
- **Un `supported-calendar-component-set` vide crée l'agenda** au lieu de le refuser : la boucle ne
  refuse que les `comp` nommant autre chose que `VEVENT`, et zéro `comp` ne nomme rien.
- **Cinq formes de la surface HTTP n'ont pas de test** : `PROPFIND Depth: 0` sur `/dav/calendars/`
  et sur le home, le `405 + Allow` de la forme événement, le `308` éprouvé sur `PROPFIND` seul, et
  le `supported-report-set` de l'événement, qu'aucune assertion ne ferme.
- ~~**`SyncState` habite toujours `Models/Contacts`** alors que le socle partagé l'importe — et,
  depuis que 5c a rendu `ICalendarSyncStore` publique, il traverse une **API publique** sous un nom
  qui ment. À déplacer en tête de 5d, dans un commit mécanique isolé.~~ **Fait** : commit
  la tâche zéro de 5d, la même dette que la ligne barrée du tableau ci-dessus.

## Ce que les tests n'ont pas couvert

- **L'atomicité de la bascule CalDAV et de la création de l'agenda `default`.** Le provider
  InMemory n'a ni transaction ni isolation : `DavCredentialStore.EnableAsync` (READ COMMITTED,
  depuis la revue de T2) et l'`alongside` qui crée `default` dans la même transaction ne se
  prouvent qu'à la main, sur `snoopy_webmail_dev` — allumer CalDAV pour un compte sans agenda et
  voir naître ensemble la ligne `dav_credentials` et l'agenda `default` avec sa ligne
  `calendar_sync_state`. Procédure écrite dans `docs/superpowers/webmail-carddav-tables.md` § « Tranche 5c ».
- **Le rollback du rang sur un refus décidé dans la porte transactionnelle**, déjà noté ci-dessus :
  le test qui l'accompagne (`TheGate_ConsultsItsCommitPredicate_WithWhatTheBodyAnswered`) atteste
  la consultation du prédicat de commit, jamais l'effet réel d'un rollback — que seule une base
  réelle peut montrer.
- **Le `catch (DbUpdateException)` sur l'index unique** (`(user_id, dav_name)`,
  `(calendar_id, dav_name)`, `(calendar_id, uid)`) : trois courses entre deux clients concurrents
  que l'InMemory ne peut pas fabriquer, faute d'appliquer les index uniques.
- **L'ordre « jeton invalide avant `Prepare` »** dans `sync-collection` (`SyncCollectionReport.WriteAsync`)
  n'est plus épinglé par aucun test depuis que ni `CardMemberSource.Prepare` ni
  `EventMemberSource.Prepare` ne lisent `address-data`/`calendar-data` hors multiget — plus aucun
  chemin ne peut faire lever `Prepare` sur un `sync-collection`, donc intervertir les deux lignes
  laisserait la suite verte. Ce n'est pas une régression de 5c (le code et le commentaire sont ceux
  de T1), mais le garde-fou est aujourd'hui nominal plutôt que mordant.

## Ce dont 5d hérite

- **La procédure du « cinquième cas »**, renvoyée par 5a puis par 5b : un événement récurrent écrit
  par le webmail, relu par Thunderbird et par DAVx⁵ sur un téléphone — mêmes heures, mêmes
  exceptions. **L'attente « même bloc de fuseau » est fausse, et retirée en 5d** : Thunderbird
  comme DAVx⁵ régénèrent le `VTIMEZONE` — le premier ignore explicitement ceux qu'il reçoit et
  réémet sa propre définition, le second passe par ical4j. Ce qui se compare est l'instant et
  l'identifiant de fuseau, jamais le bloc. 5c ouvre les routes qui la rendent possible pour la première
  fois (`/dav/calendars/`, un `PUT` qui stocke verbatim, un `GET`/`REPORT` qui sert le fichier
  stocké) mais ne la joue pas elle-même : elle appartient à 5d, avec le reste des vérifications
  faites contre de vrais clients.
- **Les `<features>` de `ccs-caldavtester` à allumer** : `Extended MKCOL` (RFC 5689, servi par
  `MKCOL` avec corps `DAV:mkcol` déclarant `resourcetype` `collection` + `calendar`),
  `free-busy-query` (§ 9 de la spec, cette tâche), `calendar-query` avec `expand` (§ 7/8, tâches 6
  et 7). Les autres rapports RFC 4791 sont servis (`calendar-multiget`, `sync-collection`,
  `expand-property`) mais n'ont pas de `<feature>` dédiée dans la suite de conformité.
- **Un `MKCOL` étendu dont le refus de `resourcetype`/`calendar-timezone` sort en `403` +
  `<D:error>`** (`RefuseAsync` de la base), là où l'exemple de RFC 5689 § 3 montre un
  `DAV:mkcol-response` nommant la propriété refusée. Le statut est le bon dans les deux cas (corrigé
  en revue de T5 pour le refus de `supported-calendar-component-set`, § « `AsksUnsupportedComponent` »
  ci-dessus) ; c'est la **forme du corps** de ces deux refus-là que la spec § 11 a tranchée
  autrement, sciemment. **Requalifié en 5d** : `DavHeaders.ComplianceClasses` annonce
  `extended-mkcol`, et RFC 5689 § 3 fait du `mkcol-response` à `propstat` un MUST — non-conformité
  sous jeton annoncé, refermée par la tâche zéro de 5d (point 7), pas laissée à l'outil, qui ne
  l'aurait jamais mesurée (aucun de ses `MKCOL` ne porte de corps).
- **L'inventaire de résidus 5a/5b que 5c referme ou assume** est marqué directement dans
  `calendar-5a-residuals.md` et `calendar-5b-residuals.md`, section par section, plutôt que
  recopié ici — voir ces deux fichiers pour le détail marqué « 5c ».
