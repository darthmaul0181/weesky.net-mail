### The calendar module

**Calendar module** — `CalendarLayout` (`src/modules/calendar/CalendarLayout.tsx`) builds two
columns inside the shell's single outlet, the way `MailLayout`, `ContactsLayout` and
`SettingsLayout` build theirs: `.calendar-sidebar` (a mini-month and the calendar list) and
`.calendar-main` (a toolbar over a stage). `/calendar/new` and `/calendar/:id/edit` are two further
routes pointed at the **same** lazy layout, not layouts of their own — the `/mail/compose` and
`/contacts/:id/edit` mechanism — so the editor is a surface over the grid rather than a trip away
from it, and the sidebar never unmounts under it.

**The URL is the state** (`useCalendarUrlState`). `?view` and `?date` are normalised by an effect
with `replace`, so Back leaves the module instead of bouncing off the normalisation, and a value
neither reads falls back rather than blanking the screen. The default view comes from `localStorage`
`calendar.view`, written by `setView` alone — the splitter sizes' precedent, a device memory and not
an account preference. Every navigation inside the module carries `view` and `date` along
(`searchWith`), and `openEditor`/`createAt` pass their arguments as **search params rather than
router state**: a reload has to reopen the same draft, and `state` does not survive one.

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
- `useCalendarUrlState.ts`, `useCalendarSearch.ts`, `useEditorSeed.ts`, `useGestureWrites.ts`,
  `useEventWrites.ts`, `useCalendarWrites.ts`, `CalendarDialogs.tsx` — `CalendarLayout`'s parts:
  the URL, the debounced search, the editor's seed, the queued gesture writes, the event saves and
  deletions, the calendars' own writes, and the seven dialogs over the grid
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

**The editor is seeded once per `editorKey`, and the key is the lever** (`useEditorSeed`).
`editorKey` is `` `${id ?? 'new'}#${instance}#${reloads}` ``; a seed is planted when it changes and
never again, so an invalidation behind an open form — one of this module's own mutations, a focus
refetch — cannot reseed what is being typed. `editorReady` is **latched on the seed** for the same
reason: it is recomputed every render, and a window arriving without the edited instance flipped it
false, unmounted the keyed editor and threw the draft away. A form already sown never waits for
anything again. Nor is a form ever sown from a detail being read again after it went stale — a
save's own invalidation is one, and the hash it holds is the version that save replaced, the
invitation hook writing once more — or from one whose last read failed, since a cached copy of an
event deleted elsewhere would sow a form for nothing. An occurrence is found in the window first,
then in the search results the editor may have been opened from, then in one day fetched around the
instance: without the last two, a search result seeded the *master's* hours and a narrow save,
finding no instance, moved the occurrence instead of editing it. **And the seed dies with the editor it was sown for**: kept past the close, the next
creation found it under the same `new##0` key and reused it — a click on the grid put its slot in
the URL and opened the draft of the last *New event*, the next hour of the clock. The URL was right
all along and the test only read the URL, which is how it shipped; the test now reads the form.

**A save carries the hash the form was seeded with, and a 409 offers Reload rather than a retry.**
`updateBodyOf` is handed `{ ...detail, icsHash: seed.hash }`, so a second Save after a stale
refusal is refused again instead of quietly overwriting what the other client wrote — the
invalidate-then-retry shape would have passed, which is what `ContactsLayout` refuses too. The
band under the form carries `errors.conflict` and a Reload button; Reload refetches and, only on
success, bumps `reloads`, which changes `editorKey` and reseeds the form from the new version.
The form stands untouched behind the band until the user pulls that lever: bouncing back to a
grid that kept nothing is how somebody loses an hour's work without being told why.

**Repetition is a switch, and it is always "custom".** The Repeat row is a `.toggle-switch`, the
All-day row's twin, and turning it on opens the recurrence block on Outlook's default — every week,
on the start's own weekday, for ever (`defaultRule`). There is no preset picker any more: the
`daily`/`weekly`/… kinds of `RepeatChoice` survive only so a stored plain rule still reads back,
and the block draws whatever `ruleOf` answers. A rule the switch was turned off on is remembered
in a ref, so turning it back on finds it rather than the default.

**The end of an event and the end of a series are two questions, and the form only ever asks
one.** While the event repeats, the End row keeps its date box — same row, same width — but the
box is **disabled and follows the start date** (`alignEnd`, applied on every write through `set`).
That is a shipped defect rather than a preference: a 3-month event repeated weekly is ten
overlapping occurrences every day for four months, and the form accepted it because a free end
date beside a rule reads as the end of the *series* to everyone who did not write the code. The
one thing that can still move that date is a time that crosses midnight — 23:00 to 01:00 ends the
morning after, and pinning it to the start day would refuse the save. **Start and End are two
rows of one shape**, and a whole day does not change it: the date box is as wide as a date
(`flex: none`, no measure), the time box four characters, and the All-day switch sits on the
Start row after the time box, where it acts — one row fewer than a row of its own. All day on,
the two time boxes stay, **disabled and reading 00:00**; the clocks they hide are kept in the form,
so the switch turned back off finds them. (A first version drew a whole day as one sentence,
*From … to …* / *On …*; the owner replaced it with the sleeping boxes on 2026-09-10, the same
"asleep, not gone" rule the End date follows.)

**The dialog's width does not move under the switch, and the contract is what keeps it still.**
The recurrence block is the widest thing the editor ever draws, and a content-sized dialog would
grow for it; `.calendar-editor-form { --field-w: 66ch }` therefore hands the Title box — drawn in
every state — a measure wide enough for the block's widest line in French *and* in English, on the
contract's own variable rather than a pixel width. Measured in `probes/recurrence-editor.html`:
**675px in all eight states** (hours / whole day × once / repeating × FR / EN), block 499, escape 0.
A `UNE seule largeur` of NON in that probe is the regression to look for — and the probe links
`shell.css`, because the end row's segmented control lives there and without it the three choices
measure as bare radios, 42px narrower than what ships.

**The calendar picker is a dropdown drawn as the select it replaces** (`CalendarSelect`, on
`DropdownMenu`). A native `<select>` cannot paint an option, so the colour swatch used to sit
*beside* the box, and the box then started 8px to the right of every other control in the column.
The trigger wears the `.field-h` control's own box with the swatch inside it, and every row of the
menu carries its calendar's swatch. **Description lives under *More options*, availability does not**: on a plain event
the form is title, calendar, when, whether it repeats, reminders, location and availability; a
description that holds text opens the chevron on its own, the rule every other field under it
already follows (2026-09-10: availability moved up under the location, so it is no longer one).

**The block says one sentence and nothing more.** *Repeat every [n] [day|week|month|year]*, the
seven weekday boxes, and *Ends: never / after [n] times / on [date]*. Three things about it. The
weekday boxes are **drawn for every unit and never withheld** — under *day* they are all lit and
disabled, since choosing days makes no sense when it is every day, and a row that comes and goes
makes the block jump under the pointer; leaving *day* lands on the start's own weekday rather than
on no day at all, which a rule would repeat on nothing. They are the calendar row's own trick: a
hidden checkbox keeping the tab order, the space bar and the long name, a one-letter
`.recurrence-day` box that is what is seen. The *ends* row is **one line**: the label, the three
choices as the `.seg` segmented control the toolbar and *More options* already draw for a choice
among a few (never bare radios — the form has one control language), then the one value the
choice needs — a counter after *after*, a date after *on*, nothing after *never*. Both `.seg` and
the date box are `flex: none` there: modal.css's `flex: 1` would hand the date the whole column
and push the dialog wide. **The day-of-month and weekday-position controls
are gone from the block**: `byMonthDay`/`bySetPos` are still read, written and summarised, but a
stored rule carrying either is opened *locked* (`beyondTheBlock` arms `keepRepeat` beside
`repeatIsExact`), so a screen that cannot show "the last Friday" cannot rewrite it; *Replace*
starts from `defaultRule`, never from the rule the block cannot draw.

**Labels were renamed for what they name.** `repeat.ends` was *Fin* in French — the End row's own
word — and `repeat.endUntil` was *Le*, which names nothing on its own; they are *Se termine* and
*au* / *on*. `repeat.interval` reads *Répéter chaque* / *Repeat every*. The rule's date and the
counter each took a name of their own (`repeat.untilDate` *Repeat until*, `repeat.countValue`
*Number of times*) instead of sharing *End date* and *After* with the controls beside them.

**`keepRepeat` is a lock, not a value.** An event whose `RRULE` this editor cannot draw comes back
with `repeatIsExact: false`; the Repeat row is then frozen on `editor.keptRepeat` with a
**Replace** button beside it, and the rule travels back to the server unchanged. Offering the
five-way `<select>` there would silently flatten a rule the screen never understood — the user has
to say "replace it" out loud before anything is thrown away.

**Scope All takes the change from the form, never the day.** The editor is sown with the
occurrence that was opened, so a form saved for the whole series from the 2 November instance of
a series begun 14 September holds 2 November as its start — and `RewriteAll` writes whatever
start it is handed as the master's DTSTART. Written as it stood, the series began on 2 November
and every occurrence before it was gone; the shipped defect was All-day toggled on a later
occurrence, but any edit saved for all from any occurrence but the first did the same.
`updateBodyOf` therefore re-poses the form on the series' own first day under scope All
(`rebasedOnMaster`): the clocks and the length are the form's, the day is the master's, and a
start date the user moved travels as a shift in days applied to that first day — moving the
Wednesday instance to Thursday for all moves the whole series by one day, which is what Google
and Outlook both do. A narrow scope and a save from the first occurrence are untouched, since the
day sown is then the day meant.

**Gestures are optimistic, and each event has a lane.** A drop or a resize patches the window's
cached occurrence through `useMoveOccurrence`'s `onMutate` and rolls it back on failure, so the
block stays where the pointer left it instead of snapping home and back. The write itself needs the
event's `detail` — the version it read and the zone it is written in — which comes from the cache
the editor fills, or one fetch. **Two gestures on one event are queued behind each other**
(`useGestureWrites`' `pending`, a `Map<eventId, Promise>` claimed *before* the first await): "move
it, then a quarter of an hour more" is one gesture in the user's head and two drops in ours, and
sent together they carry the same `ifHash` and the second comes back 409 — a refusal for having done
exactly what the grid invites. The second waits and reads the version the first wrote. A drop on a
series asks the scope question first, through `askScope`, which is a **promise**: the layout owns
the one dialog and three callers await its answer, `null` being the ✕ and nothing written.

**A click on the empty part of a month cell creates, and the chips around it name the hour** —
and so does **Enter** on the focused cell, which lands below every chip the cell drew, since a
key cannot supply the pointer Y the rule below is read against. A month cell names a day and no
hour, so the one hint there is is where the click lands among the
timed chips, which are stacked in order: below a chip, the new event starts when that one ends
(rounded up to the quarter); above the first, it ends when that one starts; on a day with no
timed chip, nine to ten. Bands carry no hour and are skipped. A click that started on a chip or
on the *+N more* count is theirs — they are buttons whose clicks bubble to the cell, and
`closest('.event-chip, .month-more')` is what tells the two apart — and a click while a bubble
stands is spent closing it, the hour grid's own rule. The rectangles are read at the click, as
the bubble's are; jsdom has none, so the test pins them.

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
  through `form="calendar-event-form"`, which is what lets it be up there at all. It is no `Modal`,
  but it says `role="dialog"`/`aria-modal` and registers a layer, so Escape and Tab are its own
  while it stands and no reader wanders into the grid behind it.
- **The floating `+`** is the module's primary action below 1024px, which is why
  `.context-drawer-panel .column-actions` is hidden there like every other module's. It is
  withheld while the editor is open, for the collision `MailLayout` and `ContactsLayout` both
  withhold theirs for: it is anchored 73px up from the edge the tab bar owns.

**The Title box carries `autoFocus` *and* a `titleRef`, and that is not the redundancy
CLAUDE.md's "pass the ref, never `autoFocus`" rule forbids.** The editor's header and its form
arrive in **two** commits — `editorReady` waits for the event or the calendar list — so the surface
(the desktop `Modal`, the phone layer) activates over the loading header, where the ref is still
null and focus correctly lands on that header's ✕. `autoFocus` is what moves it onto the field on
the later commit the form lands in, which no layer activation follows; the ref is what wins over
`autoFocus` in the same-commit case, a detail already in the cache. Remove either and one of the
two paths opens on the wrong control.

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

**What CalDAV sees (5c).** `/dav/calendars/` serves exactly the rows this page writes and reads —
the webmail is not one client among others, it is the same table read through a second door.
Three consequences for anyone touching this module:

- **A DAV `PUT` stores the file verbatim**, never through `IcsComposer`: a phone or Thunderbird
  writing a series `RecurrenceEditor` cannot draw (the `keepRepeat` note above) leaves it exactly
  as sent, and the webmail must keep reading and merging it without touching it — the same lock,
  seen from the other end of the wire.
- **`ifHash` is the DAV ETag.** The hash the editor freezes at seed time for its own optimistic
  concurrency (`updateBodyOf`, above) is the very value a CalDAV client reads and sends back as
  `If-Match`: both worlds share one notion of "the version I read", never two.
- **A write through either door can send mail (5e2).** The server, not CalDAV, schedules: the
  `DAV:` header still announces no `calendar-auto-schedule`, so a phone never offers guests of its
  own on these calendars, and what it edits is sent for it. The paragraph below says when.

**Invitations (5e).** Receiving (5e1) lives in the mail reader; inviting (5e2) starts in this
module's editor and ends in the same reader.

- **The Guests field.** `AttendeesField` sits under Location whenever `EventDetail.canInvite` is
  true — no organizer, or one of the primary account's addresses (`IUserAddresses.ForPrimaryAsync`;
  a connected account's identity, a Gmail, organizes like anybody else) — and a received event keeps its read-only
  names instead. The editor writes `attendees: [{ email, name? }]`; the server sets `ORGANIZER` to
  the primary account's default sending identity (`OrganizerIdentity`) and keeps each known
  guest's `PARTSTAT`. Guests on an event somebody else organizes are refused `400 not_organizer`;
  an address that is not one is refused on screen first (`editor.invalidAttendee`). The answers
  under the field follow the chips, each with the dot `attendeeStatus` reads from the stored
  master; a guest list edited from one occurrence is the whole series'.
- **Two doors, one hook.** `CalendarEventsController` and `CalDavController` (`PUT`, `DELETE`) call
  `InvitationScheduler.AfterWriteAsync` after a successful write, with the file before and after.
  Nothing else does: not an import, not an answer (`Respond`), not `ApplyReply`, not deleting a
  whole calendar. The hook catches everything, so it never fails the write it follows; it is awaited
  before the response, and anything SMTP cannot take at once leaves through the queue.
- **Two columns decide.** `scheduling_owner` is `'webmail'` once the webmail owns the event — it
  sent the first invitation, or took over one silently (below); `scheduling_hash` is
  `SchedulingShape` — every component's dates, rule, status, title, location and lower-cased guest
  addresses — as of the last send, or, for a silent takeover, the reference fingerprint recorded
  without a send. `SchedulingDecider` turns the pair and the new file into REQUESTs and CANCELs; a
  changed shape whose SEQUENCE the client did not advance gets it advanced by a second, conditional
  write (`RevisionCause.Scheduling`), on the component that moved. **A webmail write also takes
  ownership silently** of an event that already had guests but no owner (Thunderbird invited it, or
  it predates 5e2): the stored file stands in for the last version sent, so nothing mails unless
  what a guest is told about — the fingerprint — differs from it: a description or a calendar move
  takes ownership with no REQUEST at all.
- **Two sessions, one queue.** A webmail write sends through the user's own SMTP session; a device
  write, or a session that cannot be opened, goes through `ServiceMailQueue`, the service account
  an administrator enters in Administration > Application (`api/SchedulingAccount`), with one retry a
  minute later. `POST`/`PUT` answer `scheduling: { owner,
  sent }`, where `sent` counts the people told, not the messages (one carries every recipient of its
  kind), and the editor's toast shows it (`editor.savedSent`). `From` is the file's `ORGANIZER` when it is
  one of the primary account's addresses, the resolved identity otherwise.
- **A guest's REPLY.** The message detail reads it into `invitation.reply` and writes nothing;
  `InvitationCard` then calls `POST /api/Calendar/Invitations/ApplyReply` once per mounting when
  `status` is `Applicable` and not `applied`. Only yes, maybe and no are applicable. A reply to an
  earlier SEQUENCE is `Stale`; one older than the recorded answer of the same SEQUENCE is
  `Superseded`, ordered by its DTSTAMP, which the stored `ATTENDEE` keeps as `X-WEESKY-REPLY-STAMP`
  (stripped from outgoing mail). The write carries If-Match: a change in between answers `200
  applied:false, applyError:"calendar_conflict"` and the card offers Retry; `400
  reply_not_applicable` and `reply_not_a_reply` offer Reload, which redraws the card from the
  message as it now reads.
- **Error codes this module can see.** `not_organizer` on a write; from the card, the
  `invitation_*`, `reply_*` and `calendar_conflict` / `calendar_busy` / `calendar_refused` codes
  `apiErrorMessage` translates. `Respond` answers `replyError: "invitation_no_organizer"` when the
  file gives no address a reply can be sent to (none, or one no mail can reach), and
  `"invitation_uid_unwritable"` when its UID holds a character no reply may carry: either way the
  answer is recorded and no Resend is offered.

**The month is a grid of weeks, and the day cell is the widget.** `.month-view` carries
`role="grid"` and is `MonthView`'s own root, so it stands at `useGridNav`'s first layout effect
without the small component `MessageGrid` and `ContactGrid` were extracted to be; each week is a
`role="row"` opening on its week number as a `role="rowheader"`, and each day a `role="gridcell"`
that is **itself the activation target** — hence the constant `tabIndex={-1}` on it, and hence
`cellEntry`, which walks the cells and reaches the chips inside one with **F2**, Escape coming
back out (the hook spends Escape there and nowhere else, so the bubble and the editor keep it
everywhere else). **Enter on a cell creates rather than entering it**: creating is a day's primary
action and has to mean the same thing on an empty day as on a full one, F2 already being the
documented way in — and the hour it lands on is the click's own rule read with no pointer to read
it against, which is below every chip the cell drew. **`aria-selected` names the anchor and
`aria-current="date"` names today**, two different questions that a calendar must not answer with
one attribute: the stop opens on the anchor ahead of today, and `CalendarLayout` keys the grid on
the month so a chevron step re-opens it on the new anchor instead of leaving the hook to recover a
lost stop next door — row 0 at the old weekday, which for Monday to Wednesday is an outside day of
the month just left, where Enter then created. A cell is named for its day and the count it holds,
drawn or hidden alike, and **a chip is named with its own day** (`views.chipLabel`): the day is in
the cell around it, which is position, and no reader hears position. **The week head above the hour
grid is a `role="table"` of `role="columnheader"`s**, not a second grid: it holds no focusable of
its own, and the hour slots under it are out of the keyboard **by decision** — a day is ninety-six
quarter-hour targets, the three gestures over them are the pointer's, and a chip is reached from
the month, the list or the search instead.

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
