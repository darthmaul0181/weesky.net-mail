# Website design & UX

The look, feel and interaction conventions every screen of the webmail frontend must follow.
This file is the single reference for those rules; keep it in English.

These conventions exist so the whole application reads as one product: a user who has learned one
screen already knows how the next one behaves. When you build or change a screen, reuse the classes
and patterns described here rather than inventing a local variation — a second dialect of dialog,
list or search makes the app look like two applications.

**Reference implementations:** the **Administration** tabs (`modules/settings/admin/`) and the
**Identities** page (`modules/settings/identities/`) are the canonical examples of everything below.
When a rule is ambiguous, copy what those screens do.

## Foundations (tokens)

- **A token names a role, never a colour** — components never hard-code a colour, spacing or radius;
  everything goes through the CSS custom properties. Common roles: `--action-primary` (the accent:
  primary buttons, active tabs, hover tints, focus borders), `--surface`, `--bg`, `--border`,
  `--text`, `--text-muted`, `--danger`, `--success`, `--radius-sm`, `--font`.
- The interactive accent is always `--action-primary`; destructive intent is always `--danger`.
- The deeper theming contract (palette files, dark-mode hue shifts) lives in
  `.claude/rules/frontend-theming.md`.

## Page layout

- A settings/admin page is a `.settings-page`. Its heading is a `.settings-page-header` holding a
  `.settings-page-title` that **pairs a leading icon with the title text** (e.g. shield +
  "Administration"). The title is the page's `<h1>`, and the icon is a 17px sibling of the text
  inside it — the gap is the title's own `flex` `gap`, never the whitespace between two JSX
  children, which JSX drops entirely across a line break. `SettingsLayout.test.tsx` walks every
  settings route and asserts the pairing; a new page that forgets its icon fails there.
- **An icon's meaning is exclusive within a module.** Two settings pages must not wear glyphs of the
  same construction: sliders read as "adjust these things" on General, so Rules cannot also be
  stacked bars with tick marks — at 17px the difference was 2px, and the two pages sit three rows
  apart in the nav. Check a new glyph against its neighbours at final size, not enlarged.
- A busy/loading region shows a centred `.spinner`, never a bare "Loading…" where a spinner fits.
- Feedback is a toast: `useToasts()` + `<Toasts>`, green for success, red for error, short-lived.
  Success and failure of an action both speak; silence reads as a hang.

## Lists & tiles

- **Any list of elements on a page is rendered as tiles**, one per element (`.admin-list` of
  `.admin-list-item`) — never a bare table or an unstyled row list. A tile is a surface card with a
  border and `--radius-sm`.
- **A tile highlights on hover by colouring its border.** The border switches to the accent
  (`--action-primary`) and a soft ring of the same hue appears around it. Because the highlight is
  driven by the `--action-primary` token — not a fixed colour — it automatically follows whatever
  theme (light/dark) and palette the user has chosen; never hard-code the hover colour. This is the
  standard affordance for every interactive tile (admin rows, identities, aliases).
- **Unless the tile can be *picked* — then it is a list row, and it takes the message row whole.**
  A tile lives on a page, where a row is acted on and never selected; a list column sits beside a
  detail pane, and the two surfaces want different things. The border-and-ring hover collides there
  with the selected language below, which spends the same `--action-primary` on its border: the
  contacts list shipped with the picked tile and the one under the cursor both ringed in the accent,
  indistinguishable at a glance. A list carrying `is-selected` therefore copies `.message-row`
  rather than `.admin-list-item`, in three respects — **states**: `--list-row-hover` on hover, the
  selected fill plus the inset `--accent-unread` bar when picked, and no accent border in either;
  **geometry**: rows run edge to edge under a `--list-separator` hairline instead of standing apart
  as cards, so no radius, no gap between rows, and no padding on the scroll container — a card's
  border, radius and gap frame a list among other blocks on a page and only spend width and vertical
  rhythm in a column; **row actions**: the cluster leaves the first line for the message row's own
  idiom — out of the flow on the bottom-right corner, revealed on hover/focus, with the bottom line
  reserving its width on reveal so the text ends in an ellipsis instead of running under the buttons.
  Out of the flow is the point: a slot reserved beside the name spends that width permanently for a
  control that is only ever there under the cursor. **One control size across the row**, `.row-btn`'s
  26px box on an 18px glyph, star included: the contacts row shipped with three sizes — an 18px star
  on a 14px glyph beside 28px `.admin-icon-btn`s on 16px ones — and the smallest of them was the one
  control drawn at rest. A row that mixes sizes reads as two lists spliced together.
- **Tile anatomy is fixed, left to right:**
  1. the **favorite star** on the far left (favorite / set-as-default), when the list has one;
  2. the **primary identifier** (`.admin-list-item-email`, bold — the email, domain, display name…);
  3. the **secondary text** (`.admin-list-item-name`, muted, takes up the slack);
  4. optional **metadata** (e.g. a quota mini-gauge);
  5. the **action icons** (`.admin-list-item-actions`), pinned to the far right.
- The star always leads on the far left; the action icons are always the rightmost thing on the row.
  **On a list column (see the picked-tile rule below) the star moves to the right end of the first
  line instead**, where the message row keeps its own — the row there has no second identifier to
  balance it, and a star at the head of the line pushes every name 25px in.
- **A tile may run to two lines when its column is too narrow for one.** The contacts list
  (`.contact-tile`) is the first: the top line carries the star far left and the name taking the
  slack, and the second line the primary address, muted and indented under the name, with a `· +N`
  suffix when the contact holds further addresses. Two lines are a response to width, never a
  licence to reorder. **The second line is rendered even when it is empty**, so a contact with no
  address is not a shorter tile than its neighbours — the same reason the message list always
  renders its preview element. Its action icons are **not** on the first line: that is the
  list-column rule below, not an exception to the anatomy.
- **A selectable list column wears `SelectionBand` as its heading, never a band of its own**
  (`src/components/SelectionBand.tsx`, `src/styles/selection.css`). The skeleton owns the master
  checkbox, the reserved gutter the row checkboxes are revealed in, and one rule: **the centre
  belongs to the caller at rest and gives way to the count while a selection stands.** What sits in
  that centre is the module's — the mail puts its folder name there, the contacts their search field
  and total — and so are the actions on the right. A control that filters rather than acts goes in
  `trailing`, which survives the swap, because a filter is still true while rows are checked: the
  mail's starred toggle is the one case. **A module whose centre holds something reachable at zero
  clicks owes it a door back**: the contacts field is swallowed by the count, so the band grows a
  loupe while selecting, and using it clears the selection and returns the field. Two bands to keep
  in step are two bands that drift, which is why the styles left `mail.css`.
- **A drag target lights up with `drop-ready`, and its label travels in `--drop-label`.** The state
  is deliberately louder than `is-active` — an accent ring plus tint — so the place you are already
  in reads as excluded rather than as the target, and a target that cannot receive the drop never
  lights at all (the mail's source folder, the contacts' "All contacts", which is the complete view
  rather than a group). The label is a custom property and not a `content` literal because it is
  translated: the folder tree said `Drop here` in French for as long as it was written in the
  stylesheet.
- A tiled list carries **one** skin unless several layouts actually exist for it. The message list
  has a wide single-line row and a narrow two-line row because three reading-pane arrangements exist
  there; the contacts list always sits beside the contact card, so a second skin would be code
  nothing could reach.
- An empty list shows a centred muted line ("No virtual alias domains"), not a blank area.
- **This tile pattern is for management / settings lists.** A dense content list — the mail message
  list — is *not* tiled: it uses edge-to-edge rows divided by hairline separators (see *The mail
  module*). Reach for tiles when the user manages a handful of entities, for content rows when they
  scan a long stream.

## Buttons & row actions

- **Row actions are icon-only** `.admin-icon-btn` buttons (ghost, muted; hover tints to the accent),
  each carrying a `title` tooltip. The destructive one is `.admin-icon-btn.is-danger` (hover turns
  red). Conventional order: **edit, then delete**.
- The page's primary action is a filled `.btn.btn-primary`. An **"Add" button pairs an icon with the
  word "Add"** (the icon says what is being added — person, globe…).
- **`.btn-primary` is full-width by default — add `.btn-auto` everywhere that is not a form's
  submit.** The `width: 100%` was written for the login form and the dialogs, where the button is
  the form's only control and filling the width is right. Everywhere else — a settings page, a
  toolbar, a band, a card footer — it stretches the button across the whole panel, which reads as a
  broken layout. This has now been the same bug six times, each one patched with its own
  `<parent> .btn-primary { width: auto }` rule; `.btn-auto` is the one modifier for it. A primary
  button that is not submitting a form it fills should carry it from the first render — jsdom sees
  no layout, so no test in this repo will ever catch its absence.
- **Deleting opens a `DeleteConfirmModal`**, never deletes in place on the first click.
- A disabled button dims and switches its cursor; a button that fires async work shows a `.spinner`
  while it is pending.

## Modals & dialogs

- **Shell:** `.modal-overlay` (a click on the backdrop closes the dialog) wraps a `.modal` (clicks
  inside are contained via `stopPropagation`). Width is set per dialog — roughly `380px` for a
  compact form, up to `600px` for a rich one.
- **A modal never carries a Cancel or Close button.** The `.modal-close` ✕ in the top-right corner is
  the only dismissal control — do not add a second one beside the primary action.
- **Icon continuity:** when the control that opens the dialog carries an icon, the **same icon
  precedes the dialog title** (edit → pencil, add account → person-plus, add domain → globe), so the
  trigger and the dialog it produced read as one continuous action.
- **The body is a `<form>`** so Enter submits. Validation/API errors surface as an `.alert.alert-error`
  at the top of the form.
- **Exactly one primary action**, a `.btn.btn-primary`, whose label names the outcome ("Create
  account", "Save changes") rather than saying "OK". It is disabled while the form is invalid or a
  save is pending, and shows a `.spinner` while pending.
- **Fields default to horizontal `.field-h` rows** — the label sits beside the control in a fixed
  narrow column, uppercase and muted. Because the label is beside the control (not wrapping it), every
  field needs an explicit `htmlFor`/`id` pair, or it has no accessible name. Stacked `.field` rows
  (label above the control) are the alternative for very compact dialogs.
- A boolean is a `.toggle-switch`, not a bare checkbox. **Its off state is drawn by a hairline, not by
  the fill**: a `--border` fill measured 1.19:1 against `--bg` and read as a disabled control, while a
  fill dark enough to reach 3:1 on its own comes out mid-grey and reads as an "on" of another colour.
  The track is `--text-muted` at 30% over `--surface` with an inset 1px ring at 75%, which clears 3:1
  (WCAG 1.4.11) on the harder ground in all sixteen palette/mode combinations — `classic` light is the
  binding case at 3.00:1, so lowering either number breaks it. The checked track drops the ring: the
  accent fill is its own boundary. Verify in a browser, per palette; jsdom sees no colour at all.
- **A locked switch is dimmed through `.toggle-switch.is-locked`, never through `:disabled`.** Every
  settings toggle carries `disabled` while its mutation runs — all seven of `GeneralPage`'s do, and so
  does Administration's app-install switch — so a rule keyed on the attribute greys the whole page at
  each click. The caller says which it is: `FolderManager` on `isSystem`, `ToggleRow` via `locked` on a
  permission that will not be re-prompted. WCAG 1.4.11 exempts an inactive component, which is what
  makes the ~1.5:1 the dimming leaves acceptable *there* and nowhere else.
- **An ancestor's opacity must never cover an actionable control.** A disabled rule's card used to
  fade at `opacity: .5` with its switch inside it, and that switch is the only way back on — measured
  1.76:1. The opacity now sits on the card's children, skipping the header and, inside it, the switch;
  an ancestor's opacity cannot be raised back by a descendant. `.rule-wizard-body--locked` keeps its
  blanket fade because it also carries `pointer-events: none`: nothing in there is actionable.

## Search & pickers

Two shapes, both **live** — filtering happens on every keystroke, never behind a submit button:

1. **List filter** — a `.search-input` (type `search`) placed in the list header filters the visible
   tiles in place as the user types. The header count reflects "matching / total". Matching is
   case-insensitive substring across the item's meaningful fields (e.g. email *and* full name).

2. **Combobox picker** — a `.search-input` text box with a `.ownership-dropdown` anchored directly
   **beneath** it. Every letter typed refines the options (`.ownership-dropdown-option`, capped at
   ~10). This is the shape meant by "a search is a textbox with a combobox under it". Rules:
   - selection is committed on `onMouseDown` with `preventDefault`, so the input's blur cannot beat
     the click;
   - Escape closes it, and so does a click outside / blur;
   - already-chosen values are removed from the options and shown above the box as removable
     `.ownership-tile` chips (each chip carries a small trash/✕ to unlink it).

   This is the picker used for the virtual-domain owners and for the Identities alias field.

## Application shell & column layout

- **The frame is a dark "L".** The topbar and the left rail share one colour (`--topbar-bg` /
  `--rail-bg`) and read as a single dark L; the content sits inside it as a rounded panel (`--bg`,
  `--radius-lg`) with a small inset gap that softens the L's inner corner. The app is desktop-first —
  a `1024px` floor, below which the page scrolls rather than reflowing.
- **The rail** (`.app-rail`, 56px) is a vertical stack of 40px icon buttons (`.rail-item`,
  `--radius-md`): the modules at the top, a `.rail-spacer` (`flex:1`) pushing the Settings gear to the
  bottom. Active = filled (`--rail-item-active` / `-fg`); hover = a soft tint (`--rail-item`).
- **The band beside the rail shares one surface across modules.** The first column of a module — the
  mail folder tree and the settings context pane alike — sits on `--folders-bg` behind a `--border`
  right hairline; the content columns to its right sit on `--surface`. One surface, so switching
  modules never repaints the navigation band a different colour.
- **Columns are band stacks, never scrolling boxes.** A module builds its columns inside the shell's
  single content area. Each column is `display:flex; flex-direction:column; min-height:0;
  overflow:hidden` — fixed bands with **exactly one scrolling middle band** (`flex:1; min-height:0;
  overflow-y:auto`). Headings, toolbars, pagers and footers stay pinned; only the middle scrolls.
  `min-height:0` is the load-bearing part — without it the scroll escapes to the whole column and the
  pinned bands drift away. Use this pattern for any new multi-band column.

## Navigation vs. content: two "selected" languages

The app marks "the current / chosen thing" two different ways, and which you use depends on the surface:

- **A navigation pane** — the rail, the folder tree, the settings context pane, the pager — marks its
  active item with a **fill and heavier weight, no bar** (`--pane-item-active-bg` / `-fg`, or the
  rail's own pair). It says "you are here".
- **A content list** — the message rows, the move/copy folder picker — marks a picked or unread row
  with the **selected fill plus an inset accent bar** down its left edge (`--list-row-selected-bg` /
  `-fg` + a 3px `--accent-unread` inset). It says "this row is picked".

Never give a navigation item the accent bar, or a content row a bare fill — the two languages are how
the user tells a nav pane from a list at a glance. (Unread message rows deliberately wear the *same*
selected tokens, plus bold and a leading dot; the bold and dot are what set unread apart from merely
selected — same tokens, so the two looks can never drift.)

## Icon buttons: one hover language

**An icon button recolours its glyph on hover and nothing else** — no background tint, no transition.
`.row-btn`, `.action-btn`, `.selection-btn` (mail) and `.admin-icon-btn` (settings, admin, identities,
rules, contacts) all take `--icon-hover-accent` (the palette's vivid accent, identical in both themes),
and their `.is-danger` variant takes `--icon-hover-danger`. An "on" star sits at `--badge-count-bg`.

The settings lists used to tint their background instead, on the reasoning that a hover language should
match its surrounding surface. It made the same edit-and-delete pair look like two different controls
depending on which module drew it, which is a worse cost than the surface mismatch it avoided. Reach for
this one language for any new icon button.

**The one exception is `.folder-action`**, the folder manager's row controls: it still tints its
background (`--pane-item-hover`) and reddens to `--danger`. Those rows sit inside a navigation-styled
pane where a bare glyph recolour is easy to miss, and the pane is the only surface where they appear —
so it is a deliberate local exception, not a second general language to copy.

## Hover-revealed controls sit in reserved space

Controls that appear only on row hover/focus — the selection checkbox, a row's action cluster —
occupy **permanently reserved** space (a padding gutter or a fixed flex slot), so revealing them never
shoves the row's text sideways. The line a cluster would cover reserves that width and ends in an
ellipsis, instead of running under the buttons. The rule for any hover-revealed control: reserve
first, reveal into the reserve.

## The mail module

Three band-stack columns: **folders** (240px, `--folders-bg`), **message list** (`--surface`) and
**reader** (`--surface`), with a drag `.pane-splitter` between list and reader (it colours to
`--accent-unread` on hover/focus).

- **Folder tree** — full-width `.folder-row` buttons (hover `--folders-item-hover`); system-role
  folders carry **weight only**, no colour; an unread **count pill** (`--badge-count-bg` / `-fg`) sits
  at the row end; an inset hairline `.folder-separator` divides the role folders from the user's own.
  A valid drag drop-target is louder than the active row on purpose — an accent ring, a tint and a
  "Drop here" tag — so the source folder (wearing the active fill) reads as excluded.
- **Message list** — a pinned heading / `.selection-toolbar` band over the scrolling rows, then a
  pinned footer (pager or count). The selection toolbar is deliberately tall, with finger-sized 36px
  targets. Rows come in two skins: a **narrow** two-line stack (sender · date / subject / preview) for
  the side-by-side layout, and a **wide** single baseline line (fixed-width sender · subject — preview
  · date) for the bottom / full-width layouts. The preview line is always reserved even when empty, so
  every row keeps the same height.
- **Reader** — the header is a vertical `.reader-stack`: subject (18px), then the sender line (name +
  a green/red auth shield + date), then To/Cc, then the spam gauge — each shown only when it exists.
  The action cluster sits `align-self:flex-end`, bottom-right of whatever lines are present, with a
  thin `.actions-rule` between groups. The spam gauge is a pattern worth copying: **one
  `--gauge-ratio` custom property drives both the bar's length and its green-to-red colour**, so the
  two can never disagree. The body renders in a sandboxed iframe; attachments are `.attachment-chip`
  pills in their own band below the body, which scrolls on its own so many attachments can't take over
  the panel.
