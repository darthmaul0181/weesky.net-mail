# Agenda 5b — les écrans : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** L'agenda que l'utilisateur voit dans le webmail — vues semaine, jour, mois et liste, barre latérale, bulle d'aperçu, éditeur avec la question de portée, glisser-déposer, recherche, import/export, cinq écrans de téléphone — sur l'API de 5a, plus trois petits ajouts backend.

**Architecture:** Un module React `src/frontend/src/modules/calendar/` construit dans l'outlet du shell comme le courrier et les contacts : deux colonnes, `?view=&date=` en paramètres de recherche, l'éditeur sur deux routes rendues en modale (≥ 640 px) ou en plein écran (téléphone). La grille est maison : le placement des chevauchements, la géométrie et les fenêtres demandées sont des modules purs testés unitairement ; TanStack Query porte les données sous `['calendar', accountId, …]`. Côté backend, `POST /api/Calendars/Import` crée un agenda depuis un fichier, `EventWrite.keepRepeat` protège une règle riche, `EventDetail` gagne `repeatIsExact` et `foreignAlarms`.

**Tech Stack:** React 18 + TypeScript, react-router v6, TanStack Query, react-i18next, Vitest + Testing Library ; ASP.NET Core (.NET 10), Ical.Net 5.2.3, xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-09-05-webmail-calendar-5b-screens-design.md` (les décisions 1–15) ; le cadrage `docs/superpowers/specs/2026-09-04-webmail-calendar-5-overview-design.md` § « Les fonctionnalités du socle (5b) » et § « L'interface » ; les maquettes validées <https://claude.ai/code/artifact/e0f2b333-7491-4ac1-9229-8ac31a45f2c7> (en cas d'écart, la maquette gagne) ; les résidus `docs/superpowers/calendar-5a-residuals.md` § « À traiter en 5b ».

## Global Constraints

- **Frontend** : `src/frontend/CLAUDE.md` et `website-design.md` sont la loi. Tokens seulement, jamais une couleur en dur (sauf la couleur d'un agenda, qui est une donnée). Deux largeurs de media query, `1023px` et `639px`, jamais `min-width` (`styles/responsive.test.ts` le vérifie). Une modale ne porte jamais de largeur en dur (`styles/modals.test.ts`). Le ✕ est la seule sortie d'un dialogue ; pas de bouton Annuler. Chaque colonne est une pile de bandes (`flex: 1; min-height: 0; overflow-y: auto` sur une seule bande).
- **Localisation** : chaque texte visible passe par `t()` avec une clé littérale (jamais une variable), namespace `calendar` (nouveau) ; `en` et `fr` en parité (`locales/parity.test.ts`), typographie française avec U+00A0 avant `; : ? !` et dans « », apostrophe U+2019. Les dates via `Intl`, jamais formatées à la main.
- **Tests** : à côté du fichier (`Foo.tsx` → `Foo.test.tsx`) ; le texte visible s'affirme en anglais ; ce qui dérive d'une requête s'attend avec `findBy`/`waitFor`. Aucune assertion dépendante de l'hôte (fin de ligne, fuseau du CI : les tests de fuseau passent un fuseau explicite, jamais celui de la machine). `dotnet test` (jamais `--no-build`) après tout nouveau fichier de test ; **révertir `ApiDocumentation.xml`** avant de committer (artefact que `dotnet test` régénère).
- **Backend** : « un seul moteur » — toute lecture d'iCalendar passe par Ical.Net et `Services/Calendar/*` ; aucun `try/catch` large ; les refus sont des `Result.Failure` mappés en 400/404/409 par le contrôleur. Lignes de code sans commentaire quand le code se lit seul ; 3 lignes max quand il en faut un.
- **API de 5a inchangée** hors des trois ajouts de la tâche 1 ; le webmail ne connaît le texte iCalendar que pour lire `NAME`/`COLOR` d'un fichier à importer (décision 12).
- **Sécurité / performance** : la fenêtre demandée est bornée à ce qu'une vue montre plus un jour de marge (décision 4) ; jamais un `invalidateQueries` sur toutes les fenêtres depuis un geste — les invalidations se font `onSettled` sur la clé `['calendar', accountId]`, et le glisser-déposer est optimiste sur sa fenêtre seule.
- **Git** : messages concis, deux lignes, jamais un `@` en tête ou en fin ; un commit par étape marquée « Commit ».
- **Rappel du cadrage** : pas de répétition infra-journalière ni de rappel « après » dans l'éditeur ; le sélecteur d'agenda est grisé hors portée `All` ; `ThisAndFollowing` renvoie toujours l'instance cliquée telle que l'API l'a rendue.

---

## Vue d'ensemble des tâches

| # | Tâche | Livre |
|---|---|---|
| 1 | Backend : les trois ajouts, et le contrat frontend | `POST /api/Calendars/Import`, `keepRepeat`, `repeatIsExact`, `foreignAlarms` ; `calendarTypes.ts`, `api.js`, cinq icônes |
| 2 | Les modules purs, les requêtes et le catalogue | `calendarLocale`, `windowOf`, `overlapLayout`, `gridGeometry`, `multiDay`, `eventForm`, `recurrenceSummary`, `reminderPresets`, `icsHeader`, `queries.ts`, `calendar.json` |
| 3 | Le squelette : layout, routes, barre latérale, barre d'outils, dialogues des agendas | `CalendarLayout`, `CalendarSidebar`, `MiniMonth`, `CalendarToolbar`, `CalendarDialog`, `ImportDialog`, `CalendarImportReportModal`, `calendar.css` |
| 4 | Les vues : semaine et jour, mois, liste, résultats | `WeekView`, `DayColumn`, `AllDayBand`, `NowLine`, `EventChip`, `MonthView`, `UpcomingList`, `SearchResults` |
| 5 | La bulle, l'éditeur, la portée, la suppression | `EventPreview`, `EventEditor`, `RecurrenceEditor`, `ReminderList`, `ScopeModal` |
| 6 | Les gestes : glisser, redimensionner, créer | `useDragEvent`, `useResizeEvent`, `useCreateByDrag`, mise à jour optimiste |
| 7 | Le téléphone, les sondes, la documentation, les résidus | `phone/PhoneMonth`, `phone/DayStrip`, éditeur plein écran, `probes/mobile-layout.html`, `docs/architecture-calendar.md`, `calendar-5b-residuals.md` |

Chaque tâche se termine suite verte : `npm run lint && npm run typecheck && npm test` dans `src/frontend`, `dotnet test` dans `src/snoopy.microservice` quand le backend a bougé.

---

### Task 1: Backend — les trois ajouts, et le contrat frontend

**Files:**
- Modify: `src/snoopy.microservice/Models/Calendar/EventWrite.cs`, `EventRequest.cs`, `EventDetail.cs`, `EventRequestValidator.cs`
- Modify: `src/snoopy.microservice/Services/Calendar/IcsComposer.cs` (`Apply`, `PlaceRule`), `IcsReader.cs` (`RepeatIsExact`, `ForeignAlarms`)
- Modify: `src/snoopy.microservice/Repositories/CalendarEventStore.cs` (`GetAsync`)
- Modify: `src/snoopy.microservice/Controllers/CalendarsController.cs` (`ImportAsNew`)
- Test: `snoopy.microservice.Tests/Services/IcsReaderTests.cs` (créer si absent, sinon compléter), `Services/IcsComposerTests.cs`, `Controllers/CalendarsControllerTests.cs`, `Repositories/CalendarEventStoreTests.cs`
- Modify: `src/frontend/src/modules/calendar/calendarTypes.ts`, `src/frontend/src/api.js`
- Create: `src/frontend/src/icons/BellIcon.tsx`, `RepeatIcon.tsx`, `ClockIcon.tsx`, `PlusIcon.tsx`, `CloseIcon.tsx` ; compléter `src/icons/icons.test.tsx`

**Interfaces:**
- Produces (backend) :
  - `POST /api/Calendars/Import?tz=<iana>` multipart `file`, `displayName`, `color?` → `201 { calendar: CalendarView, report: CalendarImportReport }` ; `400` sans fichier, mauvais type, nom vide, fuseau inconnu, ou plafond des vingt agendas — ce dernier **avant** de lire le fichier.
  - `EventRequest.KeepRepeat` (`bool`, défaut `false`) → `EventWrite.KeepRepeat` ; quand vrai, `IcsComposer.Apply` n'appelle pas `PlaceRule` et le validateur ignore `Repeat`.
  - `EventDetail` gagne `bool RepeatIsExact` et `IReadOnlyList<string> ForeignAlarms` (après `Status`).
- Produces (frontend) : dans `calendarTypes.ts`
  ```ts
  export interface EventWrite { …; keepRepeat?: boolean }
  export interface EventDetail { …; repeatIsExact: boolean; foreignAlarms: string[] }
  /** Body of PUT /api/Calendar/Events/{id}. */
  export interface EventUpdateBody extends EventWrite { scope: EditScope; instanceId?: string; ifHash: string }
  export interface CalendarImportOutcome { calendar: Calendar; report: CalendarImportReport }
  ```
  et dans `api.js` : `importCalendarAsNew: (file, displayName, color, tz)` (FormData `file`, `displayName`, `color` si non vide ; `POST /api/Calendars/Import?tz=`).
  Icônes : `export default function BellIcon({ size = 16 }: { size?: number })`, même signature pour les cinq, viewBox 24, `stroke="currentColor"`, `strokeWidth="1.9"`, `aria-hidden="true"` — les tracés du générateur de maquettes : cloche `M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15z M10 20a2 2 0 0 0 4 0` ; répétition `M17 2l4 4-4 4 M3 11V9a4 4 0 0 1 4-4h14 M7 22l-4-4 4-4 M21 13v2a4 4 0 0 1-4 4H3` ; horloge `circle cx=12 cy=12 r=9` + `M12 7v5l3 2` ; plus `M12 5v14M5 12h14` ; fermer `M6 6l12 12M18 6L6 18`.

- [ ] **Step 1 : les tests backend qui échouent**

`IcsReaderTests` (le fichier existe-t-il ? `ls snoopy.microservice.Tests/Services/`) :

```csharp
[Fact]
public void RepeatIsExact_IsTrueForTheEditorsWeekly()
{
    var parsed = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY;BYDAY=MO"))!;
    Assert.True(IcsReader.RepeatIsExact(parsed));
}

[Fact]
public void RepeatIsExact_IsTrueWithoutARule()
{
    var parsed = IcsDocument.TryLoad(Ics.Single("20260914T090000", "20260914T100000", zone: "Europe/Brussels"))!;
    Assert.True(IcsReader.RepeatIsExact(parsed));
}

[Theory]
[InlineData("FREQ=MONTHLY;BYDAY=2MO")]              // ordinal in BYDAY: the editor writes BYSETPOS
[InlineData("FREQ=YEARLY;BYMONTH=3,9;BYDAY=-1MO")]   // two months
[InlineData("FREQ=WEEKLY;BYDAY=MO,WE;WKST=SU")]      // WKST is not carried
public void RepeatIsExact_IsFalseWhenTheSubsetLosesSomething(string rrule)
{
    var parsed = IcsDocument.TryLoad(Ics.Rule(rrule))!;
    Assert.False(IcsReader.RepeatIsExact(parsed));
}

[Fact]
public void ForeignAlarms_ListsWhatTheEditorCannotShow()
{
    var ics = Ics.Single("20260914T090000", "20260914T100000", zone: "Europe/Brussels", extra:
        "BEGIN:VALARM\r\nACTION:EMAIL\r\nTRIGGER:-P1D\r\nSUMMARY:x\r\nDESCRIPTION:x\r\nATTENDEE:mailto:a@b.c\r\nEND:VALARM\r\n" +
        "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;VALUE=DATE-TIME:20260914T070000Z\r\nDESCRIPTION:x\r\nEND:VALARM\r\n" +
        "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nDESCRIPTION:x\r\nEND:VALARM\r\n");
    var parsed = IcsDocument.TryLoad(ics)!;
    Assert.Equal(["EMAIL, 1 day before", "DISPLAY, 2026-09-14 07:00 UTC"], IcsReader.ForeignAlarms(parsed));
}
```

Vérifier que `Ics.Single` accepte `extra` à cet endroit (lire `Fixtures/Ics.cs:105`) ; sinon composer le VCALENDAR à la main dans le test, sur le modèle de `Ics.Rule`.

`IcsComposerTests` :

```csharp
[Fact]
public void KeepRepeat_LeavesARichRuleUntouched()
{
    var parsed = IcsDocument.TryLoad(Ics.Rule("FREQ=YEARLY;BYMONTH=3,9;BYDAY=-1MO"))!;
    var master = IcsDocument.MasterOf(parsed)!;
    var write = IcsReader.Read(parsed, Guid.NewGuid()) with { KeepRepeat = true, Summary = "Renamed" };
    IcsComposer.Apply(master, write, withRule: true);
    Assert.Equal("FREQ=YEARLY;BYMONTH=3,9;BYDAY=-1MO", master.RecurrenceRule!.ToString());
    Assert.Equal("Renamed", master.Summary);
}
```

(Si `RecurrencePattern.ToString()` réordonne les parties, comparer les propriétés `Frequency`, `ByMonth`, `ByDay` plutôt que le texte.)

`CalendarsControllerTests` : `ImportAsNew_CreatesThenImports` (le store `CreateAsync` rend un id, `ImportAsync` reçoit cet id, réponse 201 avec `calendar` et `report`) ; `ImportAsNew_RefusesAtTheCapWithoutReadingTheFile` (`CreateAsync` rend `Result.Failure("You have reached the maximum of 20 calendars")` → 400, `ImportAsync` jamais appelé) ; `ImportAsNew_NeedsADisplayName` (400). Le `CalendarView` créé se relit par `_store.Setup(s => s.ListAsync(...))` comme le fait `Create` — lire le test existant `Create_…` pour le modèle.

`CalendarEventStoreTests` : `Get_ReportsRepeatIsExactAndForeignAlarms` — un événement stocké avec `outlook-2003.ics` (`Fixtures/ICalendar/`) rend `RepeatIsExact == false` ; `google-alarm.ics` rend un `ForeignAlarms` non vide (l'alarme acquittée `ACKNOWLEDGED` reste un `DISPLAY` relatif : vérifier sur le fichier quel alarme y est étrangère et adapter l'attendu ; si aucune, utiliser `thunderbird.ics`).

- [ ] **Step 2 : lancer, voir rouge**

`dotnet test --filter "FullyQualifiedName~IcsReaderTests|FullyQualifiedName~IcsComposerTests|FullyQualifiedName~CalendarsControllerTests|FullyQualifiedName~CalendarEventStoreTests"` — échecs de compilation attendus (membres absents).

- [ ] **Step 3 : implémenter**

`EventWrite` : ajouter `bool KeepRepeat = false` en dernier paramètre du record (les appelants positionnels existants restent valides). `EventRequest.KeepRepeat { get; set; }`. `EventRequestValidator.Validate` : quand `request.KeepRepeat`, ne pas valider `Repeat` et passer `null` avec `KeepRepeat = true`. `IcsComposer.Apply` : `if (withRule && !w.KeepRepeat) PlaceRule(evt, w.Repeat);`.

`IcsReader.RepeatIsExact(IcsCalendar parsed)` : `master.RecurrenceRule` nul → vrai ; sinon recomposer `IcsComposer.RuleOf(Repeat(master, start), start)` (rendre `RuleOf` `internal`) et comparer à la règle du maître sur `Frequency`, `Interval`, `Count`, `Until`, `ByDay` (jour + offset), `ByMonthDay`, `ByMonth`, `BySetPosition`, `ByHour`/`ByMinute`/`BySecond`/`ByYearDay`/`ByWeekNo` (tous vides dans la recomposition, donc non vides ⇒ faux), `FirstDayOfWeek` (différent du défaut ⇒ faux). Écrire une méthode `SameRule(RecurrencePattern a, RecurrencePattern b)` privée, listes comparées ordonnées.

`IcsReader.ForeignAlarms(IcsCalendar parsed)` : `master.Alarms.Where(a => !IcsComposer.IsStartReminder(a))` → `$"{ACTION}, {trigger}"` où le déclencheur relatif se décrit en `N day(s)/hour(s)/minute(s) before|after` (durée négative = before ; `RELATED=END` ajoute ` the end`) et l'absolu en `yyyy-MM-dd HH:mm UTC`. Action absente → `ALARM`.

`CalendarEventStore.GetAsync` : passer `IcsReader.RepeatIsExact(parsed)` et `IcsReader.ForeignAlarms(parsed)` au `new EventDetail(...)`.

`CalendarsController.ImportAsNew` : `[HttpPost("Import")]`, `[RequestSizeLimit(CalendarEventStore.MaxImportBytes)]`, paramètres `string tz, [FromForm] string? displayName, [FromForm] string? color, IFormFile? file` ; ordre des vérifications : fuseau, nom, fichier et type (mêmes messages que `Import`), `store.CreateAsync(uid, new CalendarWrite(displayName.Trim(), null, color, null), tz)` → 400 sur échec, puis lecture du fichier, `events.ImportAsync`, relecture de la vue via `store.ListAsync` (comme `Create` le fait pour rendre la vue) et `StatusCode(201, new CalendarImportOutcome(view, report))` — nouveau record `Models/Calendar/CalendarImportOutcome(CalendarView Calendar, CalendarImportReport Report)`. Factoriser la lecture et le contrôle du fichier avec `Import` dans un helper privé `ReadCalendarFileAsync` (un `Result<string>`), pas de duplication.

- [ ] **Step 4 : vert, puis toute la suite**

`dotnet test` ; `git checkout -- ApiDocumentation.xml` si le fichier a dérivé.

- [ ] **Step 5 : le contrat frontend et les icônes**

Éditer `calendarTypes.ts` et `api.js` comme dans Interfaces (commentaires de doc dans le style du fichier : ce que le champ signifie, pas ce qu'il est). Créer les cinq icônes ; ajouter chacune à `icons.test.tsx` selon le motif du fichier (il vérifie `aria-hidden` et la taille). `npm run typecheck && npm test -- icons`.

- [ ] **Step 6 : Commit**

```
git add src/snoopy.microservice src/frontend/src/modules/calendar/calendarTypes.ts src/frontend/src/api.js src/frontend/src/icons
git commit -m "feat(calendar): import dans un nouvel agenda, keepRepeat, repeatIsExact, foreignAlarms" -m "Trois ajouts backend de 5b, le contrat frontend qui les porte et cinq icones."
```

---

### Task 2: Les modules purs, les requêtes et le catalogue

**Files:**
- Create: `src/frontend/src/modules/calendar/calendarLocale.ts` (+ `.test.ts`), `windowOf.ts`, `overlapLayout.ts`, `gridGeometry.ts`, `multiDay.ts`, `eventForm.ts`, `recurrenceSummary.ts`, `reminderPresets.ts`, `icsHeader.ts` (chacun avec son `.test.ts`)
- Create: `src/frontend/src/modules/calendar/queries.ts` (+ `queries.test.tsx`)
- Create: `src/frontend/src/locales/en/calendar.json`, `src/frontend/src/locales/fr/calendar.json` ; modify `locales/en/index.ts`, `locales/fr/index.ts`

**Interfaces (Produces — les signatures exactes que les tâches 3 à 7 consomment) :**

```ts
// calendarLocale.ts
export interface WeekRules { firstDay: 1 | 2 | 3 | 4 | 5 | 6 | 7 /* 1 = Monday, ISO */; minimalDays: 1 | 4 | 7 }
export function weekRulesOf(region: string): WeekRules          // navigator.language → getWeekInfo, else table, else {1,4}
export function hourCycleOf(region: string): 'h12' | 'h23'
export function startOfWeek(day: PlainDate, rules: WeekRules): PlainDate
export function weekNumberOf(day: PlainDate, rules: WeekRules): number   // ISO for {1,4}; generic otherwise
export function monthGrid(year: number, month: number, rules: WeekRules): PlainDate[][] // always 6 rows × 7
export function formatRangeTitle(from: PlainDate, to: PlainDate, view: View, lang: string): string
export function formatTime(instant: Date, lang: string, cycle: 'h12' | 'h23', tz: string): string
export function dayNames(lang: string, rules: WeekRules, style: 'short' | 'narrow'): string[]  // in week order

// plainDate.ts (tiny helper shared by the module; put it in calendarLocale.ts if it stays under 40 lines)
export type PlainDate = string   // 'YYYY-MM-DD'
export function addDays(d: PlainDate, n: number): PlainDate
export function todayIn(tz: string): PlainDate
export function plainDateOf(instant: Date, tz: string): PlainDate
export function utcOfLocalMidnight(d: PlainDate, tz: string): Date

// windowOf.ts
export type View = 'day' | 'week' | 'month' | 'list'
export interface Window { from: string /* ISO instant */; to: string; firstVisible: PlainDate; lastVisible: PlainDate }
export function windowOf(view: View, anchor: PlainDate, tz: string, rules: WeekRules): Window

// overlapLayout.ts
export interface Placed<T> { item: T; column: number; columns: number; top: number; height: number }
export function layoutColumn<T>(items: T[], startMinuteOf: (t: T) => number, endMinuteOf: (t: T) => number,
  pxPerHour: number, minHeight: number): Placed<T>[]

// gridGeometry.ts
export const HOUR_PX = 56, SNAP_MINUTES = 15, GUTTER_PX = 56, FIRST_VISIBLE_HOUR = 7
export function minutesToPx(m: number): number
export function pxToMinutes(px: number): number            // snapped to SNAP_MINUTES, clamped 0..1440
export function columnAt(x: number, left: number, width: number, columns: number): number

// multiDay.ts
export type Slice = { day: PlainDate; startMinute: number; endMinute: number }
export function placeOccurrence(o: Occurrence, tz: string, visible: PlainDate[]):
  { kind: 'band'; from: PlainDate; to: PlainDate; label?: string } | { kind: 'slices'; slices: Slice[] }

// eventForm.ts
export interface EventFormState { calendarId, title, isAllDay, startDate: PlainDate, startTime: 'HH:mm', endDate, endTime,
  timeZone: string, repeat: RepeatChoice, reminders: number[], location, description, availability, visibility, url,
  keepRepeat: boolean, foreignAlarms: string[] }
export type RepeatChoice = { kind: 'never' } | { kind: 'daily' | 'weekly' | 'monthly' | 'yearly' } | { kind: 'custom'; rule: RecurrenceWrite }
export function newEventForm(start: Date, end: Date, allDay: boolean, calendarId: string, tz: string): EventFormState
export function formOf(detail: EventDetail, occurrence: Occurrence | null, browserTz: string): EventFormState
export function writeOf(form: EventFormState): EventWrite
export function updateBodyOf(form: EventFormState, detail: EventDetail, occurrence: Occurrence | null, scope: EditScope): EventUpdateBody
export function allowedScopes(form: EventFormState, detail: EventDetail): EditScope[]   // ['All'] when the calendar changed
export function movedBody(detail: EventDetail, occurrence: Occurrence, deltaMinutes: number, newDurationMinutes: number | null, scope: EditScope): EventUpdateBody
export function validate(form: EventFormState): string | null   // key of the error, e.g. 'editor.endBeforeStart'

// recurrenceSummary.ts
export function recurrenceSummary(rule: RecurrenceWrite, t: TFunction<'calendar'>, lang: string): string

// reminderPresets.ts
export const DATED_PRESETS = [0, 5, 10, 15, 30, 60, 120, 1440, 2880, 10080] as const
export const ALL_DAY_PRESETS = [360, 900, 2340, 9540] as const
export const MAX_REMINDERS = 5
export function reminderLabel(minutes: number, allDay: boolean, t: TFunction<'calendar'>): string
export function convertReminder(minutes: number, toAllDay: boolean): number

// icsHeader.ts
export function calendarHeaderOf(text: string): { name?: string; color?: string }
```

- [ ] **Step 1 : les tests**

Écrire chaque `.test.ts` avant son module. Les cas qui comptent :

- `calendarLocale` : `weekRulesOf('fr-BE')` → `{1,4}` ; `weekRulesOf('en-US')` → `{7,1}` ; un `Intl.Locale` sans `getWeekInfo` (le supprimer par `vi.spyOn` sur le prototype) tombe sur la table ; `weekNumberOf('2026-09-16', {1,4})` = 38 ; `weekNumberOf('2027-01-01', {1,4})` = 53 ; `weekNumberOf('2026-01-03', {7,1})` = 1 ; `monthGrid(2026, 9, {1,4})[0][0]` = `'2026-08-31'` et six rangées ; `formatRangeTitle('2026-09-14','2026-09-20','week','en')` contient `14` et `20 September 2026` (via `formatRange`, ne pas figer le tiret) ; `formatTime` en `Europe/Brussels` rend `09:00` pour `2026-09-14T07:00:00Z` en `h23`.
- `windowOf` : semaine du 14/09/2026 en `Europe/Brussels` → `from = 2026-09-12T22:00:00.000Z` (le 13 à minuit local, un jour avant le 14), `to = 2026-09-21T22:00:00.000Z` (le 22 à minuit) ; mois → 6 rangées, du 30/08 au 6/10 ; liste → de ce matin à +31 jours ; à la bascule du 25/10/2026 le `from` du jour vaut `2026-10-23T22:00:00.000Z` et le `to` `2026-10-25T23:00:00.000Z` (pas de dérive d'une heure).
- `overlapLayout` : deux événements disjoints → chacun `columns = 1` ; A 9–10 et B 9–10 → colonnes 0 et 1, `columns = 2` ; A 9–12, B 9–10, C 10–11 → A col 0, B col 1, C col 1, `columns = 2` (C reprend la colonne libérée) ; un 9:00–9:10 a `height = 20` ; les `top` sont `minutesToPx(start)`.
- `gridGeometry` : `pxToMinutes(HOUR_PX * 9 + 20)` = 555 (9:15) ; borné à 1440 ; `columnAt` sur 7 colonnes.
- `multiDay` : daté 22:00–02:00 → deux tranches (22:00–24:00 le J, 00:00–02:00 le J+1) ; daté 09:00 J → 11:00 J+2 (50 h) → `band` du J au J+2 avec `label = '09:00'` ; journée entière 3 jours → `band` ; daté flottant → tranche par `localStart` ; ce qui sort de `visible` est coupé, pas rendu.
- `eventForm` : `formOf` d'un flottant (`timeZone` absent, `start` présent) donne `timeZone = browserTz` ; d'un daté `America/New_York` garde ce fuseau ; avec une occurrence, `startDate/startTime` viennent de `startUtc` **converti dans `fields.timeZone`** (jamais dans `browserTz`) ; `updateBodyOf(…, occurrence, 'This')` porte `instanceId = occurrence.instanceId` et `ifHash = detail.icsHash` ; `allowedScopes` rend `['All']` dès que `calendarId` diffère ; `writeOf` d'une journée entière n'a ni `start` ni `timeZone` et `endDateInclusive = endDate` ; `movedBody` avec `deltaMinutes = 90` sur une instance `08:00` Brussels rend `start = '2026-09-14T09:30:00'` ; `validate` refuse fin < début et fin de journée entière < début.
- `recurrenceSummary` : `{frequency:'WEEKLY', interval:1, byDay:['MO','WE'], end:'Never'}` → `Every week on Monday and Wednesday` ; interval 2 mensuel by `bySetPos:-1, bySetPosDay:'FR'` → `Every 2 months on the last Friday` ; `end: 'Count', count: 10` → `…, 10 times` ; `Until` → `…, until 20 December 2026`.
- `reminderPresets` : `reminderLabel(15, false)` = `15 minutes before` ; `reminderLabel(900, true)` = `The day before at 09:00` ; `reminderLabel(45, false)` = `45 minutes before` (hors liste) ; `convertReminder(15, true)` = 360 ; `convertReminder(900, false)` = 15.
- `icsHeader` : sur le texte d'un export réel (recopier l'en-tête de `Fixtures/ICalendar/apple-icloud.ics` : `X-WR-CALNAME`, `X-APPLE-CALENDAR-COLOR`) rend nom et couleur ; une ligne pliée (`NAME:Long\r\n  name`) est dépliée ; `#RRGGBBAA` d'Apple est ramené à `#RRGGBB` ; sans les deux → `{}` ; l'analyse s'arrête au premier `BEGIN:VEVENT` (ne jamais parcourir 20 Mo).
- `queries.test.tsx` : `calendarKeys.window(accountId, from, to, tz)` sous `calendarKeys.all` ; `useMoveOccurrence` remplace l'occurrence dans le cache de sa fenêtre avant la réponse et restaure l'ancienne sur échec (mock `api.updateEvent` rejetant) ; toute mutation invalide `onSettled`.

- [ ] **Step 2 : rouge** — `npm test -- modules/calendar`.

- [ ] **Step 3 : implémenter**

Points d'implémentation qui ne se devinent pas :

- Tout calcul de date en `Intl` avec `timeZone` explicite ; **jamais `new Date(y, m, d)`** (heure locale de la machine). `utcOfLocalMidnight` se calcule par itération : partir de `Date.UTC(y, m, d)`, lire les composantes dans `tz` par `Intl.DateTimeFormat(..., {timeZone: tz, hourCycle: 'h23', …}).formatToParts`, corriger du décalage, une seconde passe pour la bascule.
- `weekRulesOf` : `new Intl.Locale(region)`, `getWeekInfo?.()` ou la propriété `weekInfo` (Safari) ; table de repli des régions au dimanche : `US CA JP BR IL MX PH ZA KR TW HK AU` ; `minimalDays` 4 en repli.
- `weekNumberOf` : algorithme général — la semaine 1 est celle qui contient le `minimalDays`-ième jour de janvier ; calculer le début de semaine de ce jour et compter.
- `queries.ts` : clés `all`, `calendars`, `window(from,to,tz)`, `event(id)`, `search(q)` ; `useCalendars(tz)` (`staleTime` 5 min), `useWindow(window, tz)` (`staleTime` 60 s, `placeholderData: keepPreviousData` pour que la grille ne clignote pas en changeant de semaine), `useEvent(id)`, `useSearch(q)` ; mutations `useCreateEvent`, `useUpdateEvent`, `useDeleteEvent`, `useCreateCalendar`, `useUpdateCalendar`, `useSetCalendarVisible`, `useDeleteCalendar`, `useImportCalendar`, `useImportCalendarAsNew` ; `useMoveOccurrence(window)` optimiste (`onMutate` → `cancelQueries` + `setQueryData` ; `onError` → restaure ; `onSettled` → invalide `all`). Un `409` sur `updateEvent` se reconnaît par `error instanceof ApiError && error.status === 409`.
- Catalogue `calendar.json` : clés par écran (`toolbar.*`, `sidebar.*`, `views.*`, `preview.*`, `editor.*`, `scope.*`, `reminders.*`, `repeat.*`, `dialogs.*`, `import.*`, `phone.*`, `errors.*`) ; pluriels `_one`/`_other` ; le français avec ses insécables. Enregistrer `calendar` dans les deux `index.ts`.

- [ ] **Step 4 : vert** — `npm run lint && npm run typecheck && npm test` (la parité et `keys.test.ts` doivent passer).

- [ ] **Step 5 : Commit** — `feat(calendar): modules purs, requetes et catalogue du module agenda`.

---

### Task 3: Le squelette — layout, routes, barre latérale, barre d'outils, dialogues des agendas

**Files:**
- Create: `modules/calendar/CalendarLayout.tsx` (+ test), `CalendarSidebar.tsx` (+ test), `MiniMonth.tsx` (+ test), `CalendarToolbar.tsx` (+ test), `CalendarDialog.tsx` (+ test), `ImportDialog.tsx` (+ test), `CalendarImportReportModal.tsx` (+ test), `calendarColors.ts`
- Create: `src/frontend/src/styles/calendar.css` ; l'importer là où `mail.css` l'est (`grep -rn "mail.css" src/main.tsx src/App.tsx`), après lui
- Modify: `src/routes.tsx` (`calendar`, `calendar/new`, `calendar/:id/edit` → `lazy(() => import('./modules/calendar/CalendarLayout'))` ; retirer `ComingSoon` pour l'agenda), `src/layouts/AppShell.tsx` (`writing` couvre aussi `/calendar/new` et `/calendar/:id/edit`)

**Interfaces:**
- Consumes : tâche 2 (`windowOf`, `calendarLocale`, `queries`), `ContextDrawer`/`DrawerToggle`/`useContextDrawer` (`layouts/ContextDrawer.tsx`), `useViewport`, `DropdownMenu`, `DeleteConfirmModal` (`components/DeleteConfirmModal.jsx`), `useToasts`, `downloadBlob`, `apiErrorMessage`.
- Produces :
  ```ts
  // CalendarLayout owns: view, anchor date, tz, weekRules, visible calendars, the drawer, the editor route.
  // It renders <CalendarToolbar>, the sidebar, and a <div className="calendar-stage"> whose content the
  // later tasks fill: it exports the context below so the views and the editor read one source.
  export interface CalendarContextValue { tz: string; rules: WeekRules; lang: string; cycle: 'h12' | 'h23';
    view: View; anchor: PlainDate; setView(v: View): void; setAnchor(d: PlainDate): void;
    calendars: Calendar[]; calendarById: Map<string, Calendar>; window: Window; occurrences: Occurrence[] | undefined;
    windowError: string | null; retryWindow(): void; openEditor(id: string, instanceId?: string): void;
    createAt(start: Date, end: Date, allDay: boolean): void }
  export const CalendarContext = createContext<CalendarContextValue | null>(null)
  export function useCalendar(): CalendarContextValue
  // calendarColors.ts
  export const CALENDAR_COLORS: readonly string[]   // 12 hex, from the mockup: '#3b82c4','#7c5cbf','#2e9e6b','#e2674a','#d9a400','#c2410c','#0e9aa7','#be185d','#4b5563','#7c3aed','#15803d','#b45309'
  export function isHexColor(s: string): boolean
  ```
  Markup de la barre latérale (la maquette, tokens du webmail) : `.calendar-sidebar` 240 px sur `--folders-bg`, `.column-actions` avec un seul bouton primaire « New event » (`navigate('/calendar/new')`), une bande défilante contenant `<MiniMonth>` puis l'en-tête `.calendar-sidebar-heading` (« Calendars », 11 px 700 capitales `--text-muted`, un bouton « + » `aria-label="New calendar"`), puis une `.calendar-row` par agenda : `<label>` avec un `<input type="checkbox">` visuellement masqué et le carré coloré `.calendar-swatch` (16 px, rayon 3, fond couleur + coche blanche si visible, contour 2 px couleur seul sinon), le nom ellipsé, et un `DropdownMenu` kebab (`ariaLabel` = nom de l'agenda) aux entrées Rename…, Colour…, Import…, Export, Delete… (Delete `disabled` + `title` sur `isDefault`).
  `CalendarToolbar` : `.calendar-toolbar` — `DrawerToggle` quand `inDrawer`, « Today » (`.btn` fantôme), deux boutons chevron de 36 px (`aria-label` Previous/Next period), le titre 15 px 650 puis le sous-titre « Week 38 » 12 px muet (semaine et jour seulement), le champ de recherche (`<input type="search" placeholder="Search events">`, largeur `calc(30ch + 26px)`, masqué sous 640 px — le téléphone cherche depuis la liste, tâche 7), le `.seg` Day/Week/Month/List (Month/Day/List sous 640 px). Chevrons : `day` ±1, `week` ±7, `month` ±1 mois (même quantième, borné), `list` ±30.

- [ ] **Step 1 : tests** (Testing Library, `MemoryRouter` + `QueryClientProvider`, `vi.mock('../../api.js')` sur le modèle de `ContactsLayout.test.tsx`, `mockViewport`/`resetViewport` de `src/test-utils.ts`) :
  - `CalendarLayout` : sans `?view` ni `?date`, l'URL devient `?view=week&date=<today>` (`replace`) ; `?view=week` sur `mockViewport('phone')` devient `day` ; le chevron suivant en semaine ajoute 7 jours à `date` ; « Today » ramène à aujourd'hui ; `getCalendars` est appelé avec le fuseau du navigateur ; un `getOccurrences` rejeté avec `ApiError('The window holds too many occurrences; narrow it', 400)` affiche la bande d'erreur avec ce message et un bouton Retry qui relance ; la vue choisie est écrite dans `localStorage` `calendar.view` ; `/calendar/new` monte le conteneur `data-testid="calendar-editor"` (vide pour l'instant, la tâche 5 le remplit) ; `AppShell.test.tsx` gagne le cas « `/calendar/new` ne rend pas `BottomNav` ».
  - `CalendarSidebar` : coche → `setCalendarVisible(id, false)` ; kebab → Delete grisé sur l'agenda par défaut ; « + » ouvre `CalendarDialog` en création ; Rename ouvre le dialogue avec le nom focalisé ; Export appelle `exportCalendar` puis `downloadBlob`.
  - `MiniMonth` : rend 6 rangées avec numéro de semaine dans la première colonne ; clic sur un jour → `onPick(date)` ; le jour d'ancrage est plein, aujourd'hui cerclé.
  - `CalendarDialog` : Save inerte tant que le nom est vide ; une pastille cliquée met `color` ; le champ hexadécimal refuse `xyz` (Save inerte) et accepte `#abcdef` ; la ✕ ferme.
  - `ImportDialog` : deux radios ; « new calendar » pré-rempli depuis `calendarHeaderOf(await file.text())` ; Import appelle `importCalendar(id, file)` ou `importCalendarAsNew(file, name, color, tz)` ; le rapport ouvre `CalendarImportReportModal` avec les six compteurs et les lignes en erreur.
- [ ] **Step 2 : rouge.**
- [ ] **Step 3 : implémenter.** `CalendarLayout` lit `useSearchParams`, `useViewport`, `useContextDrawer`, `useLocale().locale` pour `lang`, `navigator.language` pour `rules`/`cycle` (calculés une fois en `useMemo`), `useCalendars(tz)`, `useWindow(windowOf(view, anchor, tz, rules), tz)`. Les routes de l'éditeur : `useMatch('/calendar/new')`, `useMatch('/calendar/:id/edit')` → `inEditor` ; le layout rend le conteneur de l'éditeur (`.modal-overlay` ≥ 640 px, `.calendar-editor-screen` sous 640 px) que la tâche 5 remplit. `calendar.css` : la colonne, les bandes, la barre d'outils, la barre latérale, la ligne d'agenda et sa case, les dialogues (`--field-w` sur les rangées, jamais une largeur), les douze pastilles (`.color-swatches` grille 6 × 2, 24 px, coche sur la choisie), la bande d'erreur `.calendar-error` (comme `.contacts-empty` + un `.btn`). Le tiroir sous 1024 px reprend la barre latérale entière.
- [ ] **Step 4 : vert** — lint, typecheck, tests, et `styles/responsive.test.ts` / `modals.test.ts` intacts.
- [ ] **Step 5 : Commit** — `feat(calendar): layout, routes, barre laterale et dialogues des agendas`.

---

### Task 4: Les vues — semaine et jour, mois, liste, résultats de recherche

**Files:**
- Create: `WeekView.tsx` (+ test), `DayColumn.tsx`, `AllDayBand.tsx`, `NowLine.tsx`, `EventChip.tsx` (+ test), `MonthView.tsx` (+ test), `UpcomingList.tsx` (+ test), `SearchResults.tsx` (+ test), `occurrenceStyle.ts` (+ test)
- Modify: `CalendarLayout.tsx` (monte la vue selon `view`, ou `SearchResults` quand une recherche est active), `CalendarToolbar.tsx` (la recherche), `calendar.css`

**Interfaces:**
- Consumes : `useCalendar()`, `layoutColumn`, `placeOccurrence`, `gridGeometry`, `calendarLocale`, `useSearch`.
- Produces :
  ```ts
  // occurrenceStyle.ts — the four renderings (cadrage, fonctionnalité 3)
  export type Rendering = 'busy' | 'free' | 'tentative' | 'cancelled'
  export function renderingOf(o: Pick<Occurrence, 'status' | 'transparency'>): Rendering
  // EventChip: one occurrence drawn — in a column (absolute, top/height/left/width from Placed), in the band, or in a month cell
  export interface EventChipProps { occurrence: Occurrence; color: string; variant: 'column' | 'band' | 'month' | 'row';
    style?: CSSProperties; onOpen(o: Occurrence, anchor: HTMLElement): void; onOpenEditor(o: Occurrence): void; selected?: boolean }
  // WeekView: days.length === 1 is the day view. Same component on the phone (task 7) with gestures off.
  export interface WeekViewProps { days: PlainDate[]; onOpen; onOpenEditor; gestures: boolean; selectedKey?: string }
  export function occurrenceKey(o: Occurrence): string   // `${eventId}#${instanceId}`
  ```
  Rendu (la maquette, décision 2) : en-tête `.week-head` (`grid-template-columns: 56px repeat(N, minmax(0,1fr))`, la gouttière porte `W38`, chaque jour son nom court en capitales et son numéro dans un rond de 26 px, plein `--action-primary` pour aujourd'hui) ; `.allday-band` sur `--surface-sunken`, libellé « all-day » à droite de la gouttière, puces absolues étirées sur `calc(n * 100% / N)` ; `.week-body` défilante (`flex: 1; min-height: 0; overflow-y: auto`) avec la colonne des heures (`HH:00` à −8 px, 11 px muet) et une `.day-column` par jour (`position: relative; height: 24 * 56px`, `border-left`, traits d'heure `border-top` et de demi-heure par `linear-gradient` comme le générateur, colonne du jour teintée `color-mix(in oklab, var(--accent-unread) 4%, transparent)`) ; `NowLine` (2 px `--accent-unread`, point de 10 px sur la colonne du jour, ombre à 35 % ailleurs, `setInterval` 60 s, nettoyé) ; scroll initial à `minutesToPx(7 * 60)` via `ref` au montage. `EventChip` en colonne : `padding: 3px 6px 3px 8px`, rayon 4, 12 px, barre gauche 3 px couleur ; `busy` = `color-mix(in oklab, <c> 18%, var(--surface))` ; `free` = fond `--surface` + contour 1 px couleur ; `tentative` = `repeating-linear-gradient(135deg, <mix 18%> 0 6px, var(--surface) 6px 10px)` ; `cancelled` = contour `--border`, texte `--text-muted` barré. Titre 600 ellipsé ; heure puis lieu en muet dès 40 px puis 58 px de haut (la hauteur est connue : `Placed.height`). Un titre absent rend `t('preview.noTitle')`. Chevauchement : `left: calc(col * 100% / columns)`, `width: calc(100% / columns)`.
  `MonthView` : `grid-template-columns: 36px repeat(7, minmax(0,1fr))`, `grid-template-rows: repeat(6, minmax(0,1fr))` — **toujours les six rangées que `monthGrid` rend**, une rangée entièrement hors mois restant sur `--surface-sunken`, pour que le mois ne change pas de hauteur d'un mois à l'autre, numéro de semaine dans la gouttière, jours hors mois sur `--surface-sunken`, journée entière et > 24 h en puce pleine, événement daté en point de 7 px + heure muette + titre, au-delà de trois « +N more » (`button`) qui passe en vue jour sur ce jour.
  `UpcomingList` : groupes par jour (`.upcoming-day` en-tête 11 px 700 capitales : « Today · Wednesday 16 September », « Tomorrow · … », sinon la date), lignes `.event-row` (heure de début et de fin sur deux lignes dans 44 px, barre de 4 px couleur — contour seul pour `free` —, titre 14 px 600, sous-ligne lieu ou nom d'agenda) ; un jour sans événement n'apparaît pas ; vide → `t('views.nothingUpcoming')`.
  `SearchResults` : `.calendar-results-band` (« N results » + Clear) puis des `.event-row` avec la date en tête ; clic → `setAnchor(date)` + `onOpen` ; la bande dit `t('search.capped', { max: 200 })` quand 200 résultats reviennent.
- [ ] **Step 1 : tests** — `occurrenceStyle` (TENTATIVE → tentative ; TRANSPARENT → free ; CANCELLED → cancelled ; CONFIRMED → busy) ; `EventChip` (les quatre classes, le titre absent, l'heure absente sous 40 px) ; `WeekView` avec trois occurrences fixes en `Europe/Brussels` (le test force `tz` par le contexte) : le 09:00–10:00 a `top: 504px; height: 56px` (`toHaveStyle`), deux occurrences 09:00 se partagent la colonne (`width: calc(100% / 2)`), la journée entière est dans `.allday-band`, le 22:00–02:00 rend deux chips avec le même `data-key`, le 50 h est dans la bande avec `09:00` ; `NowLine` présent seulement quand aujourd'hui est visible ; `MonthView` (six rangées, `+1 more` quand quatre occurrences, clic → `setView('day')` + `setAnchor`) ; `UpcomingList` (groupes Today/Tomorrow, ordre chronologique, `free` sans fond) ; `SearchResults` (bande, clic → `setAnchor`, Clear vide la recherche).
- [ ] **Step 2 : rouge.**
- [ ] **Step 3 : implémenter.** Les occurrences visibles se filtrent une fois dans `CalendarLayout` (agendas cochés, `placeOccurrence` sur `visible`) et arrivent aux vues déjà placées. Le titre de la barre d'outils suit la vue (`formatRangeTitle`). La recherche : `q` en état local de `CalendarLayout` (pas dans l'URL), `useSearch(q)` avec `q.length >= 2`, `SearchResults` remplace la vue tant que `q` est non vide.
- [ ] **Step 4 : vert.**
- [ ] **Step 5 : Commit** — `feat(calendar): vues semaine, jour, mois, liste et resultats de recherche`.

---

### Task 5: La bulle, l'éditeur, la portée, la suppression

**Files:**
- Create: `EventPreview.tsx` (+ test), `EventEditor.tsx` (+ test), `RecurrenceEditor.tsx` (+ test), `ReminderList.tsx` (+ test), `ScopeModal.tsx` (+ test), `usePopoverPosition.ts`
- Modify: `CalendarLayout.tsx` (l'état `preview`, la route de l'éditeur, la suppression, le 409), `calendar.css`

**Interfaces:**
- Consumes : `eventForm`, `recurrenceSummary`, `reminderPresets`, `useEvent`, `useCreateEvent`, `useUpdateEvent`, `useDeleteEvent`, `DeleteConfirmModal`, `useToasts`.
- Produces :
  ```ts
  export interface EventPreviewProps { occurrence: Occurrence; calendar: Calendar; anchor: HTMLElement; onClose(): void; onEdit(): void; onDelete(): void }
  export interface EventEditorProps { detail: EventDetail | null /* null = new */; occurrence: Occurrence | null; initial: EventFormState;
    calendars: Calendar[]; saving: boolean; error: string | null; fullScreen: boolean;
    onSave(form: EventFormState, scope: EditScope | null): void; onDelete(scope: EditScope | null): void; onClose(): void }
  export interface ScopeModalProps { title: string; sentence: string; allowed: EditScope[]; onPick(scope: EditScope): void; onClose(): void }
  ```
  Bulle (maquette) : `.event-preview` 300 px, `position: fixed`, `--surface`, bordure, `box-shadow: 0 8px 24px rgba(0,0,0,.18)` (la valeur du menu déroulant — la reprendre du token/classe existant si `shell.css` en déclare un), rayon 4 ; `usePopoverPosition(anchor)` la place à `anchor.right + 8`, retournée à `anchor.left - 308` quand elle sortirait de `window.innerWidth`, et ramenée dans la hauteur de la fenêtre ; `role="dialog"`, `aria-label` = titre ; ferme sur Échap, clic dehors (`mousedown` sur `document`, ignoré quand la cible est dans la bulle ou dans l'ancre), défilement de `.week-body`. Contenu : pastille 12 px couleur, titre 15 px 650 (« (No title) » si absent), date et heure (« Monday 14 September · 09:00 – 10:00 », ou « Monday 14 – Wednesday 16 September » pour une journée entière de plusieurs jours), puis lieu (`MapPinIcon`), rappel (`BellIcon`, `hasAlarm` → « Reminder set » — l'occurrence ne porte pas les minutes ; le détail les apporte à l'éditeur), récurrence (`RepeatIcon`, `recurrenceText`), agenda (`CalendarIcon`), Edit primaire (`PencilIcon`), Delete fantôme (`TrashIcon`).
  Éditeur (maquette, décision 6) : formulaire `.field-h` à libellés de 110 px en capitales, dans l'ordre Title, Calendar (`<select>` précédé de la pastille de couleur de l'agenda choisi ; **il reste actif sur un récurrent — la portée est demandée après, et c'est `ScopeModal` qui ne propose que `All` quand l'agenda a changé**, par `allowedScopes`), All day (`.toggle-switch`), Start et End (`<input type="date">` + `<input type="time">`, l'heure masquée en journée entière ; changer Start décale End de la durée courante), Repeat (`<select>` Never/Daily/Weekly/Monthly/Yearly/Custom… + résumé en muet ; verrouillé sur `t('repeat.keptFromOtherApp')` avec un bouton « Replace » quand `!detail.repeatIsExact && form.keepRepeat`), Reminder (`ReminderList` : un `<select>` par rappel, ✕ pour retirer, « + Add » tant que `< MAX_REMINDERS` ; `foreignAlarms` en muet dessous), Location, Description (`<textarea>` 80 px) ; un filet ; « More options » (bouton chevron, ouvert d'office quand disponibilité ≠ Busy, visibilité ≠ Default, URL ou participants non vides) : Availability et Visibility en `.seg`, URL, Attendees (liste lecture seule : point vert pour l'organisateur, nom ou adresse, `partStat` en muet, note « Read only until invitations are supported »). Pied : Delete `.btn-danger` à gauche (édition seulement), Save primaire à droite ; `<form onSubmit>` pour qu'Entrée enregistre. Sous « Times in <zone> » (`.field-hint`) quand `form.timeZone !== browserTz`. Mode `fullScreen` : en-tête « New event / Edit event », Save dans l'en-tête, ✕ à droite (la tâche 7 en fixe le CSS ; ici le markup existe déjà).
  `RecurrenceEditor` (« Custom… ») : dans la même modale, sous le sélecteur : Every [n] [day(s)/week(s)/month(s)/year(s)] ; weekly → sept cases à cocher dans l'ordre de `rules` ; monthly → radio « on day N » / « on the [first/second/third/fourth/last] [weekday] » (`bySetPos` 1–4 ou −1 + `bySetPosDay`) ; End : Never / After [n] times / On [date]. Toujours une `RecurrenceWrite` complète en sortie.
  `ScopeModal` : `.modal` 420 px de mesure, titre « Save a recurring event » / « Delete a recurring event », phrase « “Dentist” repeats every 6 months. Which occurrences should take the change? », trois boutons empilés (`.btn-primary` pour This occurrence only, `.btn` pour les deux autres), grisés avec `title` quand absents de `allowed`.
  Flux dans `CalendarLayout` : `onSave` → `validate` → si récurrent et `scope == null` → `ScopeModal` puis rappel avec la portée ; création → `createEvent(writeOf(form))` ; édition → `updateEvent(id, updateBodyOf(...))` ; succès → toast + `navigate('/calendar?…', { replace: true })` en gardant `view`/`date` ; `409` → toast `t('errors.changedElsewhere')` + `invalidateQueries(event(id))`, formulaire conservé ; autre `ApiError` → `error` sous le formulaire (`apiErrorMessage`). `onDelete` depuis la bulle ou l'éditeur : non récurrent → `DeleteConfirmModal` ; récurrent → `ScopeModal` (titre Delete) → `deleteEvent(id, scope, instanceId)`. Fermer un formulaire modifié (`dirty` = différent de `initial`) → `DeleteConfirmModal` « Discard changes? » ; vierge → fermeture directe. Le dernier agenda choisi s'écrit dans `localStorage` `calendar.lastUsed` à chaque enregistrement réussi.
- [ ] **Step 1 : tests** — `EventPreview` (contenu, retournement quand `anchor.right + 308 > innerWidth`, Échap ferme, Edit/Delete rappellent) ; `EventEditor` (sème depuis `initial` ; All day masque les heures et convertit le rappel ; Start décale End ; Repeat verrouillé quand `repeatIsExact` faux, « Replace » déverrouille ; « + Add » disparaît à cinq ; More options ouvert quand `availability: 'Free'` ; Save appelle `onSave(form, null)` ; validation fin < début affiche l'erreur et n'appelle pas `onSave`) ; `RecurrenceEditor` (monthly by set pos → `bySetPos: -1, bySetPosDay: 'FR'`) ; `ScopeModal` (trois boutons, grisés hors `allowed`) ; `CalendarLayout` (ouvrir `/calendar/:id/edit?instance=…` appelle `getEvent`, sème depuis l'occurrence de la fenêtre ; Save sur un récurrent ouvre `ScopeModal` puis `updateEvent` avec `scope: 'This'`, `instanceId`, `ifHash` ; agenda changé → seul All actif ; `updateEvent` rejeté 409 → toast et formulaire toujours monté ; supprimer un non-récurrent passe par la confirmation ; un id inconnu (`getEvent` rejette 404) toaste et redirige vers `/calendar`).
- [ ] **Step 2 : rouge.**
- [ ] **Step 3 : implémenter.** L'éditeur ne fait aucune requête lui-même (le layout lui donne `detail`, `occurrence`, `initial`) : ses tests le montent sans fournisseur. `formOf` est appelé une fois par `key` (`${id ?? 'new'}#${instance ?? ''}#${reloads}`).
- [ ] **Step 4 : vert.**
- [ ] **Step 5 : Commit** — `feat(calendar): bulle d'apercu, editeur, portee d'un recurrent et suppression`.

---

### Task 6: Les gestes — glisser, redimensionner, créer

**Files:**
- Create: `useDragEvent.ts` (+ test), `useResizeEvent.ts` (+ test), `useCreateByDrag.ts` (+ test)
- Modify: `WeekView.tsx`, `DayColumn.tsx`, `AllDayBand.tsx`, `EventChip.tsx`, `CalendarLayout.tsx`, `queries.ts` (`useMoveOccurrence` déjà posé en tâche 2 ; brancher), `calendar.css`

**Interfaces:**
- Consumes : `gridGeometry`, `movedBody`, `useMoveOccurrence`, `useEvent` (pour `ifHash` et `fields.timeZone` au dépôt), `ScopeModal`.
- Produces :
  ```ts
  export interface DragState { key: string; deltaMinutes: number; deltaDays: number }
  export function useDragEvent(opts: { enabled: boolean; days: PlainDate[]; onDrop(o: Occurrence, deltaMinutes: number, deltaDays: number): void }):
    { onPointerDown(o: Occurrence, e: React.PointerEvent): void; drag: DragState | null }
  export function useResizeEvent(opts: { enabled: boolean; onResize(o: Occurrence, newDurationMinutes: number): void }):
    { onPointerDown(o: Occurrence, e: React.PointerEvent): void; resize: { key: string; durationMinutes: number } | null }
  export function useCreateByDrag(opts: { enabled: boolean; days: PlainDate[]; tz: string; onCreate(start: Date, end: Date): void }):
    { onPointerDown(day: PlainDate, e: React.PointerEvent): void; ghost: { day: PlainDate; startMinute: number; endMinute: number } | null }
  ```
  Règles (décision 9) : seuil de 4 px avant qu'un `pointerdown` devienne un glisser (un clic reste un clic → la bulle) ; `setPointerCapture` ; accrochage `SNAP_MINUTES` ; `deltaDays` par `columnAt` ; Échap annule (`keydown` sur `document` pendant le geste) ; `pointercancel` = annulation ; la chip glissée reçoit `.is-dragging` (opacité .7, `z-index` 10) et se dessine à sa position provisoire ; le redimensionnement se prend sur `.event-resize-handle` (6 px au pied, `cursor: ns-resize`, apparaît au survol) et ne descend jamais sous 15 min ; la création dessine un `.event-ghost` (contour pointillé `--action-primary`) puis appelle `onCreate` avec les instants (`utcOfLocalMidnight(day, tz) + minutes`) ; un clic simple sur une case vide crée `[demi-heure cliquée, +60 min]`. Dans la bande, le glisser ne change que `deltaDays`. Les gestes sont inertes (`enabled: false`) sous 640 px.
  Dépôt : `CalendarLayout.onDrop` → `getEvent(id)` (cache ou requête) → si récurrent, `ScopeModal` (portée `This` par défaut, les trois permises) → `useMoveOccurrence.mutate({ window, occurrence, body: movedBody(detail, occurrence, deltaMinutes + deltaDays * 1440, null, scope) })` ; le redimensionnement passe `newDurationMinutes`. L'optimisme (tâche 2) déplace l'occurrence dans la fenêtre courante avant la réponse ; l'échec restaure et toaste `apiErrorMessage`. Un dépôt sans déplacement (`delta = 0`) n'envoie rien.
- [ ] **Step 1 : tests** — `useDragEvent` (renderHook + événements pointer synthétiques : 3 px ne déclenchent pas, 5 px oui ; `deltaMinutes` accroché à 15 ; Échap annule sans `onDrop` ; le dépôt appelle `onDrop(o, 90, 1)` pour 84 px + une colonne) ; `useResizeEvent` (minimum 15) ; `useCreateByDrag` (un clic → 60 min à partir de la demi-heure ; un glisser 9:00→10:30 → `onCreate` avec les instants attendus en `Europe/Brussels`) ; `CalendarLayout` (un dépôt sur un récurrent ouvre `ScopeModal` puis `updateEvent` avec `scope: 'This'` et `start` décalé dans le fuseau de l'événement ; un dépôt sur un simple part directement ; le cache montre l'occurrence déplacée avant la résolution du mock, et revient après un rejet).
- [ ] **Step 2 : rouge.**
- [ ] **Step 3 : implémenter.** Les hooks n'ont aucune connaissance de React Query ; le layout branche. Pendant un geste, la bulle est fermée et `user-select: none` sur `.week-body`.
- [ ] **Step 4 : vert.**
- [ ] **Step 5 : Commit** — `feat(calendar): glisser-deposer, redimensionnement et creation a la souris`.

---

### Task 7: Le téléphone, les sondes, la documentation, les résidus

**Files:**
- Create: `phone/PhoneMonth.tsx` (+ test), `phone/DayStrip.tsx` (+ test)
- Modify: `CalendarLayout.tsx`, `CalendarToolbar.tsx`, `EventEditor.tsx` (CSS du plein écran), `UpcomingList.tsx` (la recherche sur téléphone : un champ en tête de la liste), `calendar.css` (blocs `@media (max-width: 1023px)` et `(max-width: 639px)`, `@media (hover: none)` pour les planchers de 44 px), `src/layouts/AppShell.tsx` (déjà fait en tâche 3 ; vérifier), `probes/mobile-layout.html`
- Create: `src/frontend/docs/architecture-calendar.md` ; modify `src/frontend/CLAUDE.md` (l'`@import` et la phrase « Calendar is still a placeholder » à remplacer) ; create `docs/superpowers/calendar-5b-residuals.md`

**Interfaces:**
- Consumes : tout ce qui précède, `useViewport`, `mockViewport`.
- Produces :
  ```ts
  export interface PhoneMonthProps { anchor: PlainDate; selected: PlainDate; dotsByDay: Map<PlainDate, string[]> /* up to 3 colours */; onPick(d: PlainDate): void }
  export interface DayStripProps { selected: PlainDate; onPick(d: PlainDate): void }   // 7-day rows, horizontal scroll-snap, the row of `selected` centred
  ```
  Sous 640 px (décision 15, maquettes) : la barre d'outils devient `DrawerToggle` + titre + `.seg` compact Month/Day/List (recherche retirée) ; `month` = `<PhoneMonth>` (cellules de 48 px, jour sélectionné plein 30 px, jusqu'à trois points de 5 px) puis la liste du jour sélectionné (`UpcomingList` restreint à ce jour, en-tête « Wednesday 16 September ») ; `day` = `<DayStrip>` (bande de 7 jours, `overflow-x: auto; scroll-snap-type: x mandatory`, chaque semaine `scroll-snap-align: start`, ronds de 32 px) + `WeekView` à un jour, `gestures: false`, gouttière 52 px ; `list` = `UpcomingList` avec un champ de recherche en tête (`.phone-search`) ; le bouton flottant `.floating-action` (`aria-label="New event"`, retiré tant que l'éditeur est ouvert) ; l'éditeur `.calendar-editor-screen` (plein écran, en-tête 44 px, champs 16 px / 44 px, `More options` replié) ; tap sur un événement → `openEditor` (pas de bulle). Entre 640 et 1023 px : le grand écran avec la barre latérale en tiroir.
  Sondes (`probes/mobile-layout.html`, entrées `{ name, html, … }` poussées dans `CASES`, en reprenant le markup réel des composants avec leurs classes) : `calendar toolbar-360`, `-320` (`clipped` 0, `smallest` ≥ 44 sur `.seg label` et `.drawer-toggle`), `calendar phone month-360` (cellules ≥ 44), `calendar day column-360` (une chip de 30 min lisible, `clipped` 0), `calendar editor screen-360` (`escape` sur les champs Start/End : date + heure sur une ligne sans débordement ; sinon les empiler et le noter), `calendar fab vs. bottom nav` (`overlap`), `calendar preview-768` (la bulle retournée reste dans la fenêtre). Lancer dans Chrome ou Edge aux quatre tailles du fichier et **copier les nombres dans le rapport de tâche** ; corriger ce qui déborde.
- [ ] **Step 1 : tests** — `PhoneMonth` (grille 6 × 7, trois points maximum, clic → `onPick`) ; `DayStrip` (sept jours de la semaine de `selected` dans l'ordre de `rules`) ; `CalendarLayout` sur `mockViewport('phone')` (`month` rend `PhoneMonth` et la liste du jour ; tap sur une ligne → navigation vers `/calendar/:id/edit?instance=` ; le bouton flottant est absent sur `/calendar/new` ; `week` en URL devient `day` ; le tiroir s'ouvre par le ☰) ; `AppShell.test.tsx` (pas de `BottomNav` sur `/calendar/:id/edit`).
- [ ] **Step 2 : rouge.**
- [ ] **Step 3 : implémenter, puis sonder.** Écrire `architecture-calendar.md` sur le modèle d'`architecture-contacts.md` : les fichiers et leurs rôles, les décisions qui ne se relisent pas dans le code (la fenêtre élargie, le fuseau de l'éditeur, le verrou `keepRepeat`, l'optimisme des gestes, pourquoi pas de bulle sur téléphone, les nombres mesurés dans les sondes). Écrire `calendar-5b-residuals.md` : ce que la tranche laisse (au minimum : les rappels étrangers invisibles dans la bulle ; la recherche non filtrée par agenda ; les 12/24 h et le premier jour sans préférence ; ce que les sondes ont montré et qui n'est pas corrigé), et ce que 5c hérite. Mettre à jour `CLAUDE.md` du frontend.
- [ ] **Step 4 : vert** — la suite entière frontend ; `dotnet test` si un fichier backend a bougé ; `ApiDocumentation.xml` réverté.
- [ ] **Step 5 : Commit** — `feat(calendar): ecrans de telephone, sondes de mise en page et documentation du module`.

---

## Auto-revue du plan

**Couverture de la spec.** Décisions 1 (T3), 2 (T2, T4), 3 (T2 `multiDay`, T4), 4 (T2 `windowOf`, T3 bande d'erreur), 5 (T2 `eventForm`, T5 hint), 6 (T3 routes, T5, T7 plein écran), 7 (T1, T5), 8 (T5), 9 (T6), 10 (T5, T7), 11 (T4, T7), 12 (T3), 13 (T2 `reminderPresets`, T5), 14 (T2 `calendarLocale`), 15 (T7) ; § Backend (T1) ; § Fichiers (T2–T7) ; § Tests (répartis) ; § Ce que la tranche ne fait pas (contraintes globales). Les sept résidus 5a : titre `null` (T4/T5 « (No title) »), `Start` de `ThisAndFollowing` (T2 `formOf`/`movedBody`), minuits UTC (T2 `windowOf`), pas de rappel « après » (T2 presets), agenda ⇒ `All` (T2 `allowedScopes`, T5), flottant (T2 `formOf`), import nouvel agenda (T1, T3), plafond de fenêtre (T3).

**Types cohérents.** `Occurrence`, `EventDetail`, `EventWrite`, `EventUpdateBody`, `Calendar`, `CalendarImportOutcome` viennent de `calendarTypes.ts` (T1) ; `PlainDate`, `View`, `Window`, `WeekRules`, `EventFormState`, `Placed`, `Slice` de la T2 et sont consommés par les mêmes noms en T3–T7 ; `occurrenceKey` défini en T4 et utilisé par les gestes en T6.

**Ordre des dépendances.** T1 (contrat) → T2 (purs) → T3 (squelette) → T4 (vues) → T5 (édition) → T6 (gestes, sur T4 et T5) → T7 (téléphone, sur tout). Aucune tâche ne modifie un fichier qu'une tâche ultérieure crée.
