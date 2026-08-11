# Mobile and tablet — the desktop floor comes out

The webmail is desktop-only by declaration, not by accident: `.app-shell` carries
`min-width: 1024px` with the comment *"desktop-first floor; below this the page scrolls"*, and the
whole stylesheet holds exactly one media query — `prefers-reduced-motion` on the refresh spinner.
A phone gets the desktop layout and a horizontal scrollbar. This makes the interface work on a
phone and on a tablet, and keeps it looking like itself while it does.

The whole webmail is covered in one go — login, shell, mail, composer, contacts, every settings
page and the administration tabs. A half-responsive interface reads as a broken one: a user who
reaches a settings page that still needs sideways scrolling concludes the site is broken, not that
one section is.

## What this is not

It is not a separate mobile application, and it is not a second component tree. One set of
components serves every width. A `MobileMailLayout` beside `MailLayout` would keep the desktop
guaranteed untouched, at the price of two lists, two readers and two composers to maintain, and
every future mail feature built twice.

It is not a swipe-gesture project. Swiping a row to archive or delete is its own design — travel
thresholds, undo, and a conflict with both vertical scrolling and the existing drag-to-folder. The
layout is the actual need. Pull-to-refresh is in, because the refresh button loses its home.

It is not a PWA or offline story. The manifest and notification work already shipped and is
untouched here.

It does not add `viewport-fit=cover`. Without it the browser already keeps content inside the
iPhone's safe area; with it, every bottom bar, top band and dialog would have to carry
`env(safe-area-inset-*)`. It buys a colour under the notch and costs insets everywhere.

## Breakpoints, and which way the cascade runs

Three tiers:

| Tier | Width | Shape |
|---|---|---|
| desktop | ≥ 1024px | rail, folder tree, list, reader — today's layout |
| tablet | 640–1023px | rail kept; the context pane becomes a drawer; the reading-pane preference still decides the rest |
| phone | < 640px | one pane at a time; bottom tab bar; context pane in a drawer |

**The desktop declarations stay the base rule, with no media query around them.** The two new
blocks are `@media (max-width: 1023px)` and `@media (max-width: 639px)`, the second overriding the
first through the ordinary cascade. Written the other way — mobile-first, with desktop behind a
`min-width` — every existing rule in four stylesheets would have to move inside a query, and the
diff would stop being reviewable. Here, no desktop line changes except the deletion of
`min-width: 1024px`.

**Media queries live in the file they override** — `shell.css`, `mail.css`, `index.css`,
`modal.css` — never in a central `responsive.css`. A rule and its narrow-screen behaviour have to
be readable together: gathered elsewhere, a change to `.mail-folders` silently breaks an override
two thousand lines away in another file.

**Deleting `min-width: 1024px` is a visible change for desktop too.** A window narrowed below
1024px now gets the tablet layout instead of a horizontally scrolling desktop one. That is the
intent, but it is not invisible to someone working in a half-screen window.

## `useViewport`

`src/hooks/useViewport.ts` — two `matchMedia` queries and a listener, the pattern
`ThemeContext.tsx` already uses for `prefers-color-scheme`. Returns `'phone' | 'tablet' |
'desktop'`.

It decides **what mounts, never how wide anything is**. A width computed in JavaScript would be a
second source of truth beside the stylesheet, and the two would drift. Three things need it, and
they are all mounting decisions CSS cannot express:

- which pane React renders — a reader hidden with `display: none` still fetches the message body
  and mounts its iframe
- whether `PaneSplitter` is rendered at all, rather than left in the DOM with live pointer handlers
- the drawer, which needs a focus trap, `Escape` and `aria-modal`

Without `matchMedia` it returns `'desktop'`: the layout that exists today, never a blank screen.

**Every existing test keeps its current layout for free.** `test-setup.js` stubs `matchMedia` with
`matches: false` for every query, so `useViewport` resolves to `'desktop'` throughout the suite.

## Four foundations

| Defect | Fix |
|---|---|
| `.app-shell { height: 100vh }` — the mobile URL bar eats the bottom, taking the tab bar off screen | `height: 100dvh`, with the `100vh` line kept above it as the fallback |
| `body { font-size: 14px }` — iOS auto-zooms on focusing any field below 16px | `font-size: 16px` on `input`, `select` and `textarea` under 640px |
| Touch targets at 32–40px (`.rail-item` 40, `.dropdown-item` ≈32, `.pane-item`) | a `--touch: 44px` token declared in the phone block, applied as `min-height` |
| `.app-shell { min-width: 1024px }` | deleted — the line that forbids everything else |

`100dvh` also replaces `100vh` on `.page-center` (login) and on the two other full-height roots.

`overscroll-behavior-y: contain` goes on `.app-shell`, so Chrome for Android's own
pull-to-refresh does not fire over the application's.

## The shell below 1024px

**The topbar is hidden below 640px.** It carries nothing functional any more — brand only, since
the account block moved to the foot of the folder column. Its 44px is 7% of a 640px-tall viewport,
returned to the message list. The brand still appears at the head of the drawer.

That decision removes the need for a shell-level "title and actions" context: the hamburger goes
into the header band each module already owns, and each module stays the owner of its own header.

**`BottomNav`** (`src/layouts/BottomNav.tsx`, phone only) — the same four entries as the rail,
icon over a short label, 56px tall. The `modules` array moves out of `AppRail.tsx` into
`src/layouts/modules.ts`, read by both: one definition, or a module added later appears on one
side only.

It is hidden on `/mail/compose`. Composing is a full-screen task with its own send/save bar, and a
tab bar under a software keyboard serves nobody.

**`ContextDrawer`** (`src/layouts/ContextDrawer.tsx`, below 1024px) — one component for all three
modules. It takes the context pane as `children` and presents it as a modal left drawer: clickable
scrim, `Escape`, focus trap, `aria-modal`, and no slide under `prefers-reduced-motion`. It closes
on picking a folder, on a route change, and when the viewport becomes `desktop` — a focus trap left
active on an invisible panel is worse than a drawer that stays open.

No body scroll lock is needed: `.app-shell` is `100dvh` and the page itself never scrolls.

**`.mail-folders` enters the drawer as it is** — tree, account foot and all. One band is hidden
inside it, `.mail-folders-compose`, because both its buttons find a better home. Both changes
apply below 1024px, since the drawer starts there:

- **Compose** becomes a 56px floating button, bottom right — above the tab bar on a phone, 16px
  from the edge on a tablet. Composing must never cost the opening of a drawer.
- **Refresh** moves into the list toolbar's kebab, with pull-to-refresh as the primary gesture.

## The message list

**The list toolbar takes two states when its column is narrow**, because six 44px controls do not
fit across 360px:

```
idle        [☰]  Inbox ★                          [🔍] [⋮]
selection   [☑]  3 selected      [archive][junk][delete]  [⋮]
```

This removes nothing usable. Archive, junk and delete are **already disabled** when nothing is
selected — idle, they only rendered dead state. Selection is entered by long-press on a row, and
the master checkbox appears with it.

**The trigger is a container query on `.mail-list`, not the viewport.** What decides is the width
of the column the toolbar sits in, and that is not the width of the screen: on a tablet the
splitter can leave the list at its 240px minimum — narrower than any phone — while the same tablet
under `readingPane: 'none'` gives it 960px. `.mail-list` gets `container-type: inline-size` (safe:
its width already comes from the splitter's inline style, never from its contents) and
`@container (max-width: 480px)` drives both states. The two states are then pure CSS: React's only
new job is an `is-selecting` class on `.selection-toolbar` when `count > 0`. `SelectionToolbar`
gains one `leading` slot, for the hamburger, and no viewport prop at all.

**`wide` must stop being derived from the pane alone.** Today `wide = pane !== 'right'`. On a phone
`effectivePane` is `'none'`, so `wide` would be true and rows would take the **single-line** layout,
whose `.message-row-from` is pinned at `width: 180px` — half of a 360px screen for the sender
alone. It becomes `wide = viewport !== 'phone' && pane !== 'right'`, which restores the stacked
layout (sender + date / subject / preview): already written, already about 70px tall, already a
correct touch target.

**`.message-row-cluster` is hidden below 640px.** There is no hover on touch, and on several touch
browsers `:hover` sticks after a tap, so it would appear and stay. Those actions remain reachable
by opening the message and through multi-select. Row drag-to-folder stays a desktop affordance —
the tree is behind a drawer, there is no visible target to aim at.

**Pull-to-refresh** — `usePullToRefresh(ref, onRefresh)` on `.mail-list-scroll`: `touchstart` while
`scrollTop === 0`, a 64px threshold, a progress band, then the existing `refresh()`.
`overscroll-behavior-y: contain` on the scroll container as well as the shell.

## Which panes are on screen

```
effectivePane(pref, viewport)  →  viewport === 'phone' ? 'none' : pref
```

A pure function, unit-tested.

**Only the phone overrides the preference.** At 640–1023px all three arrangements fit — `right`
needs 240 + 320 minimums, or 560px, against the 584px left by the 56px rail — and overriding an
explicit choice on a 900px tablet would be arbitrary. `PaneSplitter` stays rendered there and drags
under a finger; it already runs on pointer events. On a phone it is not rendered at all.

The `readingPane: 'none'` machinery is what the phone reuses, unchanged: the list stays mounted
under `is-hidden` so scroll position and streamed blocks survive, the reader receives `onBack`, and
because the open message is a URL parameter, Android's back button already does the right thing.

## The message body

`renderBodyDocument` is in better shape than expected: it already sets `img { max-width: 100% }`,
`table { max-width: 100% }`, `overflow-wrap: break-word` and `pre { overflow-x: auto }`. Two things
are missing, added through a `narrow` option on the same pure function:

- `padding: 12px 14px` instead of `18px 22px` — 44px of margin is a lot out of 360px
- `-webkit-text-size-adjust: 100%`, or iOS reflows the message's font sizes on its own

**An accepted limit**: a newsletter built on a table with fixed cell widths will scroll sideways
**inside the iframe**, never by breaking the application. Making such a document fit means scaling
the whole document, which is a separate piece of work.

The three sanitising barriers are untouched. Nothing here changes the sandbox, the DOMPurify pass,
or the blocked-image handling.

## Dialogs

`.modal` carries `min-width: var(--modal-w, 24rem)` — 384px — against
`max-width: min(56rem, calc(100vw - 48px))`. On a 360px viewport the overlay's 24px padding leaves
312px, and `min-width` always beats `max-width`: a 384px box in a 312px slot, overflowing the page
horizontally. This is a real defect today, not a new constraint.

Below 640px: `--modal-w: 0`, overlay `padding: 12px`, `.modal { width: 100%; padding: 18px }`. The
scroll is already on the overlay and the content-sized contract above 640px is untouched.

## The composer

Full screen on a phone, with the tab bar hidden. `.compose-toolbar` gains `flex-wrap: wrap` below
640px: a horizontally scrolling toolbar hides tools behind an invisible affordance, while a wrapped
one keeps every tool reachable at the cost of two or three rows. Recipient rows stack, Cc and Bcc
stay behind their existing on-demand reveal, and the send/save bar becomes sticky at the bottom.

## Contacts

The same shape as mail — a 240px scope column, a list, a card, a splitter, and an already-routed
full-width editor at `/contacts/new` and `/contacts/:id/edit`. So: the scope column goes into the
same `ContextDrawer`; the hamburger lands in `.contacts-list-heading`, which already carries the
search field and the count; below 640px the list and the card take turns, and since the selected
contact is already in the URL, the back button needs no new code. The splitter is not rendered
below 640px. "Add" leaves the drawer for the same floating button the mail module uses.

## Settings

The only one of the three that needs a new band. Its nine pages each render their own
`.settings-page-header`, so a hamburger placed there would be written nine times. `SettingsLayout`
renders a 44px bar above the `Outlet`, below 1024px only, carrying the hamburger and the active
section's name. The 200px `context-pane` moves into the drawer with its `IdentityMenu` foot.

Mail gets no such bar: its `SelectionToolbar` is already 64px, and stacking another 44px would put
108px of chrome above the first row on a 640px-tall screen.

## Forms and lists

`.field-h` puts a fixed-width label (110–140px) beside its field; below 640px the row becomes a
column with the label above. `.admin-list-item` rows — connected accounts, identities, external
domains, aliases — are flex rows of a title and an action group: `flex-wrap: wrap`, with the
actions dropping below the label rather than squeezing an address into three letters.

There is no `<table>` anywhere in the project. Every list is flex, so none of the usual
table-on-mobile problem applies.

## Login

`.page-center` has `padding: 24px` and `.card` has `padding: 32px`, leaving 248px of usable width
at 360px. Below 640px: overlay 16px, card 20px, which returns 44px to the form. The cover
background already works in portrait. `min-height: 100vh` becomes `100dvh`.

## What does not change

The Calendar page is a centred `ComingSoon` — responsive by construction. The Appearance page's
palette previews are flex figures with `min-width: 240px` and wrap on their own.

## Tests

jsdom computes no layout, so the suite asserts logic only.

| Target | Asserted |
|---|---|
| `effectivePane` | pure: phone forces `none`, the other tiers return the preference |
| `useViewport` | the three thresholds, the fallback without `matchMedia`, unsubscribe on unmount |
| `renderBodyDocument({ narrow })` | pure: reduced padding, `text-size-adjust`, and the existing barriers intact |
| `ContextDrawer` | `Escape` closes, scrim closes, focus trapped, `aria-modal`, closes on route change and on reaching `desktop` |
| `BottomNav` | renders the same list as `AppRail` — both read `modules.ts` |
| `usePullToRefresh` | threshold reached calls `onRefresh`, short pull does not, ignored while `scrollTop > 0` |
| `MailLayout`, `ContactsLayout` | under `'phone'`: no splitter rendered, one pane mounted |
| `SelectionToolbar` | `is-selecting` present exactly when `count > 0`; the `leading` slot renders — the two states themselves are CSS, so the probe verifies them, not jsdom |

A `mockViewport('phone' | 'tablet' | 'desktop')` helper joins `test-utils.ts` — the pattern
`AppearancePage.test.tsx` already improvises locally.

## Verifying the layout

`probes/mobile-layout.html`, one file, following `probes/localisation-widths.html`. It **links the
real stylesheets** (`../src/styles/*.css`), so only the markup is restated, never the CSS. One
section per screen, plus a script that measures and prints to the console. Opened in Chrome under
device emulation at 360×640, 390×844, 768×1024 and 1024×768.

Four measurements, one per defect named above:

1. `document.documentElement.scrollWidth <= clientWidth` on every screen — this is the modal and
   toolbar overflow test
2. every touch target's measured height ≥ 44px
3. `.modal` width ≤ the overlay's width
4. `.selection-toolbar` on a single line in both of its states, at a 360px container **and** at a
   240px one — the narrowest the tablet splitter allows

No API call is made, so the dev API's CORS origin list never comes into it.

**What this does not cover**: a probe carries hand-written markup. A divergence between the probe
and the real component is possible and will only show on a real phone after deployment.
