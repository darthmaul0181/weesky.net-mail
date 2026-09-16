# Reading pane position — design

**Date:** 2026-07-22
**Status:** approved

## Goal

A settings-backed choice of where the message reader sits relative to the message list:

- `right` — beside the list (today's layout), now with a draggable splitter
- `bottom` — below the list, full width, draggable splitter
- `none` — no split: the list fills the space; opening a message replaces it with a
  full-width reader and a back button

The overall look stays the same: same columns, same tokens, same reader. Only the
arrangement changes.

Mockups (approved): https://claude.ai/code/artifact/c5d48838-f7eb-476f-9cde-72f14430e00a

## Decisions taken

| Question | Decision |
|---|---|
| Where the setting lives | Settings → General, backend preference `mail.readingPane` |
| Control shape | Radio cards with layout glyphs (Right / Bottom / Hidden), like Appearance's palette thumbnails |
| Back navigation in `none` | ← button in the reader header (removes `uid`), Esc does the same, browser Back already works |
| Splitter | Draggable in **both** split modes (vertical in `right`, horizontal in `bottom`) |
| Splitter persistence | `localStorage`, per device (`mail.split.right`, `mail.split.bottom`) — a 4K screen and a laptop have different ideal ratios |
| Wide-list rows | Single-line layout (sender · subject — preview · date) in `bottom` and `none`; the `right` mode keeps today's stacked rows |

## Backend

One entry in the `UserPreferences.All` registry (`Models/UserPreferences.cs`):

- `MailReadingPane = "mail.readingPane"`, default `"right"`, allowed `["right", "bottom", "none"]`

Validation, default fill-in and invalid-value fallback are already the registry's job.
No controller, repository or route changes.

## Frontend

### Preference plumbing — `src/hooks/usePreferences.ts`

- Add `readingPane: 'mail.readingPane'` to `PREFERENCE_KEYS`.
- Add `readingPaneOf(preferences): 'right' | 'bottom' | 'none'` — falls back to `'right'`
  on any unexpected value, like the other defensive accessors. Nothing outside this
  accessor reads the raw stored string.
- Saving already invalidates the `['mail']` cache; no new wiring.

### `MailLayout` — three arrangements, one source of state

Selection stays in search params (`folder`, `uid`) — no route changes. The layout reads
`readingPaneOf` and renders:

- **`right`** — `.mail-folders` | `.mail-list` | vertical `PaneSplitter` | `.mail-reader`.
  Today's layout; the splitter replaces the fixed 380px border.
- **`bottom`** — `.mail-folders` full height | a column stack: `.mail-list` /
  horizontal `PaneSplitter` / `.mail-reader`. Default ratio ~40% list / 60% reader.
- **`none`** — `.mail-folders` | either the list or the reader, driven by `uid` presence.
  **The list stays mounted and CSS-hidden while reading**: unmounting would lose the
  scroll position and, in streaming ("All") mode, the loaded blocks. No selected-row
  styling in this mode — there is no message "open beside".

While preferences are loading, render the `right` arrangement without a splitter —
`MessageList` already shows its own loading state until preferences arrive.

### `PaneSplitter` — one shared component

- Props: orientation (`vertical | horizontal`), storage key, default size, min/max bounds.
- Pointer events (mouse + touch in one path). Double-click resets to the default
  (380px list width / 40% list height).
- Min bounds keep both panes usable (list ≥ 240px wide or ≥ 120px tall; reader ≥ 320px
  wide or ≥ 160px tall). Clamp on apply, so a size stored on a large screen cannot
  crush a pane on a small one.
- Persists to `localStorage` (`mail.split.right`, `mail.split.bottom`) — outside the
  backend preference system, per device.
- Keyboard accessible: `role="separator"`, `aria-orientation`, arrow keys nudge,
  matching the draggable behaviour.

### Wide-list rows — `MessageList`

- New `wide` boolean prop, set by `MailLayout` (true in `bottom` and `none`).
- Wide row: sender (fixed width) · subject — inline preview · date, one line.
- The existing Preview setting stays respected: hidden → the line ends at the subject.
- Unread/selected reuse exactly the same tokens (bold, dot, selection background,
  inset bar) — two skins of one row, not two components.

### Back button — `MessageReader`

- Optional `onBack` prop; only the `none` mode passes it (removes `uid` from params).
- Renders a ← button at the head of the subject line. Esc triggers the same handler.
- The reader is otherwise unchanged in every mode.

### Settings UI — `GeneralPage`

- A "Reading pane" row under "Messages per page": three radio cards
  (Right / Bottom / Hidden), each a real `<input type="radio">` dressed with a small
  layout glyph, like Appearance's palette thumbnails. Accessible radiogroup.
- Saves through the existing `save()` helper with a toast, disabled while pending.

### CSS

- New rules in `mail.css` (layout arrangements, splitter, wide rows) and the settings
  card styles alongside the existing settings CSS. Role tokens only — no literal
  colors, no new tokens needed.

## Error handling

- Invalid/missing stored preference → `readingPaneOf` falls back to `'right'`.
- Invalid/garbage `localStorage` splitter value → default size; values clamped to
  bounds on apply.
- Backend save failure → existing error toast path in `GeneralPage`.

## Testing

- `usePreferences`: `readingPaneOf` valid values + fallback.
- `MailLayout`: renders the right arrangement per mode; in `none`, toggles
  list ↔ reader on `uid` without unmounting the list.
- `PaneSplitter`: drag updates size, bounds clamp, double-click resets, persistence
  round-trip, keyboard nudge.
- `MessageList`: wide rows render single-line, preview setting respected,
  unread/selected styling intact.
- `GeneralPage`: the radiogroup saves the preference and toasts.
- Backend: registry accepts the three values, rejects others, serves the default.
- No existing test is removed without an equivalent replacement.

## Out of scope

- Splitter position as a backend preference (deliberately per device).
- Any reader content/header change beyond the ← button.
- Responsive/mobile-specific layouts.
