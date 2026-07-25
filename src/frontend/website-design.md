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
  border and `--radius-sm`; on hover its border lifts to `--action-primary` with a soft ring.
- **Tile anatomy is fixed, left to right:**
  1. the **favorite star** on the far left (favorite / set-as-default), when the list has one;
  2. the **primary identifier** (`.admin-list-item-email`, bold — the email, domain, display name…);
  3. the **secondary text** (`.admin-list-item-name`, muted, takes up the slack);
  4. optional **metadata** (e.g. a quota mini-gauge);
  5. the **action icons** (`.admin-list-item-actions`), pinned to the far right.
- The star always leads on the far left; the action icons are always the rightmost thing on the row.
- An empty list shows a centred muted line ("No virtual alias domains"), not a blank area.

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
