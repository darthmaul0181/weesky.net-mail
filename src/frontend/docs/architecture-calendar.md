### The calendar module

**Calendar module** — `CalendarLayout` (`src/modules/calendar/CalendarLayout.tsx`) builds two
columns inside the shell's single outlet, the way `MailLayout`, `ContactsLayout` and
`SettingsLayout` build theirs: `.calendar-sidebar` (a mini-month and the calendar list) and
`.calendar-main` (a toolbar over a stage). `/calendar/new` and `/calendar/:id/edit` are two further
routes pointed at the **same** lazy layout, not layouts of their own — the `/mail/compose` and
`/contacts/:id/edit` mechanism — so the editor is a surface over the grid rather than a trip away
from it, and the sidebar never unmounts under it.

**The URL is the state.** `?view` and `?date` are normalised by an effect with `replace`, so Back
leaves the module instead of bouncing off the normalisation, and a value neither reads falls back
rather than blanking the screen. The default view comes from `localStorage` `calendar.view`,
written by `setView` alone — the splitter sizes' precedent, a device memory and not an account
preference. Every navigation inside the module carries `view` and `date` along (`searchWith`), and
`openEditor`/`createAt` pass their arguments as **search params rather than router state**: a
reload has to reopen the same draft, and `state` does not survive one.

Files under `src/modules/calendar/`:

- `calendarTypes.ts` — the wire shapes (`Occurrence`, `EventDetail`, `EventWrite`,
  `EventUpdateBody`, `Calendar`, `CalendarImportReport`, `EditScope`)
- `queries.ts` — the TanStack hooks and `calendarKeys`, account-scoped like the mail and contacts
  keys. Every mutation invalidates **`onSettled`**, so a refused write leaves the screen on server
  state rather than on an optimistic lie — the one exception is `useMoveOccurrence`, below
- `plainDate.ts` — `PlainDate` (`'2026-09-14'`, no hour, no zone) and the zone arithmetic
- `calendarLocale.ts` — `weekRulesOf`, `hourCycleOf`, `dateLocaleOf`, `startOfWeek`, `monthGrid`,
  `weekNumberOf`, `formatRangeTitle`, `formatTime`, `weekdayNameOf`, `dayNames`. The last takes
  the **resolved** locale rather than a language and a region: `MiniMonth` and `PhoneMonth` have
  already grafted one, and doing it a second time inside the helper is how two answers drift — it
  is why both of them used to spell the seven-name expression out instead of calling it
- `windowOf.ts` — what one screenful asks the API for, and what it draws
- `multiDay.ts` — `wallClockOf`, `placeOccurrence`, `placeAll`, `itemsByDay`: where an occurrence
  goes, for a whole screen, in one pass
- `overlapLayout.ts` / `gridGeometry.ts` / `occurrenceStyle.ts` — the columns two events share, the
  pixel arithmetic (`HOUR_PX` 56, `SNAP_MINUTES` 15, `FIRST_VISIBLE_HOUR` 7) and the
  rendering/colour/key of one occurrence
- `eventForm.ts` — the pure form layer: `formOf`, `newEventForm`, `writeOf`, `updateBodyOf`,
  `movedBody`, `movedOccurrence`, `allowedScopes`, `isRecurring`, `validate`
- `recurrenceSummary.ts`, `reminderPresets.ts`, `icsHeader.ts`, `calendarColors.ts` — a rule in
  words, the reminder menu, `X-WR-CALNAME`/`X-APPLE-CALENDAR-COLOR` off an uploaded file, the
  twelve palette swatches
- `CalendarSidebar.tsx` + `MiniMonth.tsx` + `ColorSwatches.tsx` — the column, its month picker and
  the swatch grid two dialogs draw
- `CalendarToolbar.tsx` — Today, the two chevrons, the heading, `CalendarSearch` and the `.seg`
- `WeekView.tsx` + `DayColumn.tsx` + `AllDayBand.tsx` + `NowLine.tsx` — the hour grid; a day is a
  week one column wide, so there is one component and not two
- `MonthView.tsx`, `UpcomingList.tsx`, `SearchResults.tsx`, `EventChip.tsx` — the other three
  stages and the one chip they all draw, in four variants
- `EventPreview.tsx` + `usePopoverPosition.ts` — the bubble a click opens, and where it hangs
- `EventEditor.tsx` + `RecurrenceEditor.tsx` + `ReminderList.tsx` + `ScopeModal.tsx` — the form,
  the custom-recurrence panel, the reminder rows and the three-way scope question
- `CalendarDialog.tsx`, `ImportDialog.tsx`, `CalendarImportReportModal.tsx` — the calendar
  create/rename/recolour dialogue, the import chooser and its report
- `gridGestures.ts`, `pointerGesture.ts`, `useDragEvent.ts`, `useResizeEvent.ts`,
  `useCreateByDrag.ts` — the three pointer gestures over the grid
- `phone/PhoneMonth.tsx`, `phone/DayStrip.tsx` — the two screens that exist only below 640px
- `calendarContext.ts` — the module's context, in a file of its own; `calendarTestHarness.tsx` —
  `renderInCalendar`, which mounts any view with neither a router nor a `QueryClient`

**The context is the module's plumbing, and it is a file of its own for a cycle.** Every view, the
chip and the editor read `useCalendar()` for `tz`, `rules`, `lang`, `region`, `cycle`, `today`,
`visible` and `calendarById`, and `CalendarLayout` imports every one of them back. That cycle holds
only because `useCalendar` is a hoisted function declaration nobody calls at module evaluation —
which is exactly why the context does not live in `CalendarLayout.tsx`. Five values across four
views and two levels is the duplication a prop chain would have been; `EventChip` set the
precedent and the editor follows it.

**Nothing here ever builds a `Date` from local components.** `new Date(2026, 8, 14)` reads the
machine's own zone, so a Brussels laptop answers what a UTC runner does not, and the suite would
be green on one and red on the other. `plainDate.ts` is the whole zone layer: a day is a string,
day arithmetic is milliseconds in UTC, and a wall clock becomes an instant through
**`utcOfLocalTime`, which iterates**. An offset is only knowable *at* an instant, so the first pass
guesses and the second corrects it on the days a transition moves it — and the minutes go into
that function rather than being added to midnight afterwards, because the day the clocks go back
is twenty-five hours long and midnight plus three hours is 02:00, not 03:00. The hour labels in the
gutter are read off a **reference day with no transition on it** (1 January, in UTC) for the same
family of reasons: the gutter names hours, not instants, and reading them in the user's zone writes
01:00 twice on one night of the year.

**The window a screen asks for is wider than the screen it draws.** `windowOf` answers four fields:
`from`/`to` are the instants the request carries, `firstVisible`/`lastVisible` the days the grid
holds. Day, week and month ask for **one day of slack on each side** (`SLACK_DAYS`), because an
occurrence is selected by its instant and a zone fourteen hours away puts a Tuesday morning in
Sydney on Monday evening here. Each edge reads the offset of *its own* date, so a window spanning
the clock change does not drift by an hour at one end. The list is its own window — thirty-one days
from the anchor, no slack — since it shows exactly what it asks for. A refused window (the server's
`The window holds too many occurrences; narrow it`) is recognised and **re-said from the
catalogue** as `errors.windowTooLarge`, `ImportReportModal`'s road: English prose on a French
screen is worse than a sentence of our own, and the English wording is word-identical to the
server's so the two sides say the same thing.

**A calendar the list has not answered for yet is drawn, not withheld.** `visible` filters the
window's occurrences by `isVisible !== false` rather than `=== true`: a box nobody has unticked
hiding its own events reads as a load that lost them.

**The editor is seeded once per `editorKey`, and the key is the lever.** `editorKey` is
`` `${id ?? 'new'}#${instance}#${reloads}` ``; a seed is planted when it changes and never again,
so an invalidation behind an open form — one of this module's own mutations, a focus refetch —
cannot reseed what is being typed. `editorReady` is **latched on the seed** for the same reason:
it is recomputed every render, and a window arriving without the edited instance flipped it false,
unmounted the keyed editor and threw the draft away. A form already sown never waits for anything
again.

**A save carries the hash the form was seeded with, and a 409 offers Reload rather than a retry.**
`updateBodyOf` is handed `{ ...detail, icsHash: seed.hash }`, so a second Save after a stale
refusal is refused again instead of quietly overwriting what the other client wrote — the
invalidate-then-retry shape would have passed, which is what `ContactsLayout` refuses too. The
band under the form carries `errors.conflict` and a Reload button; Reload refetches and, only on
success, bumps `reloads`, which changes `editorKey` and reseeds the form from the new version.
The form stands untouched behind the band until the user pulls that lever: bouncing back to a
grid that kept nothing is how somebody loses an hour's work without being told why.

**`keepRepeat` is a lock, not a value.** An event whose `RRULE` this editor cannot draw comes back
with `repeatIsExact: false`; the Repeat row is then frozen on `editor.keptRepeat` with a
**Replace** button beside it, and the rule travels back to the server unchanged. Offering the
five-way `<select>` there would silently flatten a rule the screen never understood — the user has
to say "replace it" out loud before anything is thrown away.

**Gestures are optimistic, and each event has a lane.** A drop or a resize patches the window's
cached occurrence through `useMoveOccurrence`'s `onMutate` and rolls it back on failure, so the
block stays where the pointer left it instead of snapping home and back. The write itself needs the
event's `detail` — the version it read and the zone it is written in — which comes from the cache
the editor fills, or one fetch. **Two gestures on one event are queued behind each other**
(`pending`, a `Map<eventId, Promise>` claimed *before* the first await): "move it, then a quarter
of an hour more" is one gesture in the user's head and two drops in ours, and sent together they
carry the same `ifHash` and the second comes back 409 — a refusal for having done exactly what the
grid invites. The second waits and reads the version the first wrote. A drop on a series asks the
scope question first, through `askScope`, which is a **promise**: the layout owns the one dialog
and three callers await its answer, `null` being the ✕ and nothing written.

**The bubble is a highlight, a swallowed click and a rectangle.** `selectedKey` — the previewed
occurrence's `eventId#instanceId` — goes to every stage, so *every* chip of that occurrence lights
under an open bubble, both halves of an evening running to 02:00 included; the same pairing is what
`hoverKey` does on the hour grid, since `:hover` only ever knows the chip under the pointer. While
a bubble stands, a plain click on an empty column is spent closing it and creates nothing, the way
Google swallows that first click — a drag on the same empty column still draws an event, being a
gesture rather than a dismissal. And the bubble is placed from the chip's **rectangle, read at the
click**, never from the chip at mount: a click on a search result clears the results as it opens
the bubble (décision 11: the click goes to that day *in the current view*), so the chip has left
the screen by the time a layout effect could measure it.

**Three tiers, and only the phone gets screens of its own.** `useViewport` decides what mounts.
From 1024px up the module is two columns. Between 640 and 1023 it is the same layout with the
sidebar in `ContextDrawer` and the gestures still on — a touch tablet has the width for them.
Below 640 the sidebar is in the drawer, `week` is read as `day` (seven columns in 360px is six
unreadable ones and a sideways scroll; the *stored* preference is not overwritten by that
coercion), the toolbar drops its search box and its fourth segment, gestures are off
(`gestures={!phone}`), and the month and day stages are replaced:

- **`PhoneMonth`** is a picker, not a grid of events: a 48px cell holding a 30px disc and up to
  three 5px dots, with the selected day's own events listed under it by `UpcomingList` restricted
  to that one day. Three dots is what the cell holds beside its number; a fourth would be drawn
  outside it. The list is handed `empty={t('views.emptyDay')}` — one day holding nothing is not
  the same news as an empty month.
- **`DayStrip`** is a `scroll-snap` band of whole weeks over a one-day `WeekView`, five weeks wide
  and rebuilt around whatever day is picked. **There is no gesture code**: the browser's own
  snapping is the fling, the rubber band and the accessibility of it, and a tap in a swiped-to week
  re-centres on the week already under the finger, so nothing jumps.
- **The search field is the list's**, `.phone-search`, a band between the toolbar and the stage.
  It is not *inside* `UpcomingList`, and that is load-bearing: typing swaps the list for
  `SearchResults`, so a field inside the box being replaced would unmount under the caret.
  `CalendarSearch` is exported from `CalendarToolbar` and drawn by both, so the placeholder, the
  label and the Enter behaviour cannot drift into two dialects.
- **There is no bubble.** `EventPreview` is 300px wide and hangs off a chip; a 360px screen has
  nowhere to put it, so a tap opens the editor directly. That is why `openPreview` branches on
  `phone` rather than the bubble learning about tiers.
- **The editor takes the screen**, `.calendar-editor-screen` rather than a `.modal-overlay`, with
  a 44px head carrying the title, Save and the ✕ — Save sits outside the `<form>` and reaches it
  through `form="calendar-event-form"`, which is what lets it be up there at all.
- **The floating `+`** is the module's primary action below 1024px, which is why
  `.context-drawer-panel .column-actions` is hidden there like every other module's. It is
  withheld while the editor is open, for the collision `MailLayout` and `ContactsLayout` both
  withhold theirs for: it is anchored 73px up from the edge the tab bar owns.

**The gutter is a CSS token, not a number in TypeScript.** `.week-view` declares
`--cal-gutter: 56px` and the phone block narrows it to 52; `WeekView`'s inline grid template, the
all-day band and the now-line all read it. It used to be `gridGeometry.GUTTER_PX`, and a width
written in JavaScript is a width a media query cannot narrow — the same rule that keeps
`useViewport` out of the business of how wide anything is.

**The numbers below were measured in `probes/mobile-layout.html` and `probes/calendar-grid.html`,
not reasoned.** The grid's own geometry is in `calendar-grid.html` (the 56px hour cell, the 1344px
day column, the 93px month row that the cell's every length is budgeted against, and the thirteen
contrast readings that turned `--text-muted` on a tinted chip into a `color-mix`). The phone tier is
in `mobile-layout.html`, read under touch emulation at 320, 360 and 390: the toolbar is **121px**
of a 640px screen and folds to two rows (the title beside the ☰ and the stepper, the segments
below), the month picker is **330px**, the editor head is **44px**, the floating `+` clears the tab
bar, and the strip's `clipped` is four whole viewports because it *is* a scroller carrying five
weeks. Three of those numbers were defects a probe found. The toolbar was 170px with the title on
a line of its own; at 390px — where the segments just fitted beside the buttons — they were
crushed to 35px and clipped by 20; both are fixed by flex bases of `0` and `100%` respectively.

**And the editor's Start and End rows were stacked when they were meant to be on one line.**
`index.css`'s phone block turns *every* `.field-h` into a `flex-direction: column`, which is right
for a settings row's sentence-length caption and wrong here — and in a column a `flex-basis: 100%`
measures the **cross** axis, `flex-wrap` has no main size to break, and `flex: 1` on the two boxes
means nothing. Measured before the fix at 360: label 332×20, date 332×44, time 332×44, a **116px**
row where the pair on one line is 68 — two of those rows cost the phone editor 96px of scrolling
for nothing, and `escape` read 0 in both arrangements, so only the height said so. The phone block
now restores `flex-direction: row` on `.calendar-editor-screen .field-h`; the label's 100% basis
then takes the first line whole and the date grows beside the 8em time box the base file already
fixes. Measured after **at 360**: label 332×20 on line one, date 212×44 and time 112×44 on line
two, row **68px**. Those three widths are that screen's and no other's — each is a share of it,
so 320 and 390 read three other numbers for the same layout; what was read at every size is
`escape`, 0 on all four edges at 320, 360, 390, 768 and 1024.
