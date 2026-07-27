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
  "Administration").
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
- **Tile anatomy is fixed, left to right:**
  1. the **favorite star** on the far left (favorite / set-as-default), when the list has one;
  2. the **primary identifier** (`.admin-list-item-email`, bold — the email, domain, display name…);
  3. the **secondary text** (`.admin-list-item-name`, muted, takes up the slack);
  4. optional **metadata** (e.g. a quota mini-gauge);
  5. the **action icons** (`.admin-list-item-actions`), pinned to the far right.
- The star always leads on the far left; the action icons are always the rightmost thing on the row.
- **A tile may run to two lines when its column is too narrow for one.** The contacts list
  (`.contact-tile`) is the first: the top line keeps the anatomy above unchanged — star far left,
  name taking the slack, action icons pinned far right — and the second line carries the primary
  address, muted and indented under the name, with a `· +N` suffix when the contact holds further
  addresses. Two lines are a response to width, never a licence to reorder: nothing moves out of the
  first line but the secondary text. **The second line is rendered even when it is empty**, so a
  contact with no address is not a shorter tile than its neighbours — the same reason the message
  list always renders its preview element.
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
- A boolean is a `.toggle-switch`, not a bare checkbox.

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

## Icon buttons: two hover languages

- **Settings / admin icon buttons** (`.admin-icon-btn`, `.folder-action`) tint their **background** on
  hover and colour the glyph to the accent.
- **Mail icon buttons** (`.row-btn`, `.action-btn`, `.selection-btn`) recolour **only the glyph** — no
  background — to `--icon-hover-accent` (the palette's vivid accent, identical in both themes), and a
  destructive one to `--icon-hover-danger`. An "on" star sits at `--badge-count-bg`.

Match the surrounding surface: glyph-only recolour inside the mail columns, background tint in the
settings/admin lists.

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
