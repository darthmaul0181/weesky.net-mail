# Content-Sized Modals Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every webmail dialog size to its own content within a reading measure, so a translated string 35% longer than the English does not re-break each of the nineteen dialogs individually.

**Architecture:** Three layers, applied in order. The overlay takes the scroll and centres with `margin: auto`, so a dialog taller than the viewport anchors to the top instead of overflowing both edges. `.modal` becomes `width: max-content` between a floor (`--modal-w`) and a measure ceiling, with the prose's own `max-width: 56ch` doing the actual bounding — a box's intrinsic contribution is clamped by its child's max-width, so the container never needs a pixel width. Form controls declare zero intrinsic width today (`flex-basis: 0`, `width: 100%`), so a third layer gives them a character measure (`--field-w`) per form idiom.

**Tech Stack:** React 18, Vite, plain CSS (no preprocessor, no CSS-in-JS), Vitest + jsdom + @testing-library/react.

**Spec:** `docs/superpowers/specs/2026-07-31-webmail-content-sized-modals-design.md` — read it before Task 1. Every "why" is there; this plan is the "how".

## Global Constraints

- **A token names a role, never a value.** Add a role token rather than hard-coding. No literal colour anywhere in this change (there are none — this is sizing only).
- **`--modal-w: 24rem`** — the dialog floor. **`--field-w: 34ch`** — the field measure. Both are starting values calibrated in Task 3; both are declared as `var(--x, <fallback>)` at the point of use, so no `:root` entry is required and a dialog or a row can override either locally.
- **The prose measure is `56ch`.**
- **Every new selector is scoped under `.modal`.** The settings pages use the same `.field-h` class with `.is-setting`, where a `<select>` must keep sizing to its widest option. An unscoped selector silently moves `GeneralPage`, `AppearancePage` and the contact editor.
- **No test in this repo can verify geometry** — jsdom computes no layout. Task 3's browser measurement is the acceptance gate for Tasks 1 and 2, not a formality after them. Do not report Task 1 or 2 as "working"; report them as "landed, unverified".
- **Commit messages: two lines max, and never begin or end with `@`.** Use a heredoc (`git commit -F -`), never a PowerShell here-string.
- **Do not run `npm run deploy`** — it does not exist any more. Pushing is what deploys; do not push.

## File Structure

| File | Responsibility |
|---|---|
| `src/frontend/src/styles/modal.css` | **New.** The whole modal contract in one place: overlay, box, prose measure, the three field-idiom amendments. Follows the `tooltip.css` precedent — a component-scoped stylesheet beside `shell.css` / `mail.css`, rather than more weight in the ~2200-line `index.css`. |
| `src/frontend/src/main.tsx` | Import `modal.css` **after** `index.css` and **before** `mail.css`. |
| `src/frontend/src/index.css` | Loses the six base modal rules to `modal.css`; loses the two dead `.modal-admin` / `.modal-rules` blocks; loses `.modal.identity-modal`'s width. |
| `src/frontend/src/styles/mail.css` | Loses `.modal-folders`' width and `.modal.folder-pick-modal`'s whole rule. |
| `src/frontend/src/styles/modals.test.ts` | **New.** Source sweep asserting no modal root carries an inline width. Sits beside `palettes.test.ts`, the repo's precedent for a cross-cutting invariant test. |
| 11 dialog components | Lose their `style={{ maxWidth }}`. |
| `src/frontend/CLAUDE.md` | Gains the modal contract note. |

---

### Task 1: The modal contract

Extract the base modal rules into `modal.css` and give them content sizing, the overlay scroll and the three field-idiom measures. Delete the dead and superseded rules. The eleven inline `maxWidth` attributes stay for now — they are still valid `max-width` caps, so the app remains coherent and this task is reviewable on its own.

**Files:**
- Create: `src/frontend/src/styles/modal.css`
- Modify: `src/frontend/src/main.tsx:12-15`
- Modify: `src/frontend/src/index.css` — remove `:936-990` (six base rules), `:1087-1105` (`.modal-admin` block), `:2064-2081` (`.modal-rules` block), and the `max-width` in `:553`
- Modify: `src/frontend/src/styles/mail.css` — remove `max-width: 520px` from `.modal-folders` `:230`, remove `.modal.folder-pick-modal` `:1142-1146` with its comment

**Interfaces:**
- Consumes: nothing.
- Produces: the CSS custom properties `--modal-w` (length, default `24rem`) and `--field-w` (length, default `34ch`), both readable as `var(--modal-w, 24rem)` / `var(--field-w, 34ch)`. Task 2 relies on `.modal` needing no inline width; Task 3 calibrates both values and may set `--field-w` on individual rows.

- [ ] **Step 1: Read the spec**

Read `docs/superpowers/specs/2026-07-31-webmail-content-sized-modals-design.md` end to end. The cascade reasoning in "A form has no prose" is the part this task implements literally, and getting the specificity wrong is silent — nothing errors, the dialog is just the wrong width.

- [ ] **Step 2: Confirm the two blocks being deleted are really dead**

Run:
```bash
cd src/frontend && grep -rn "modal-admin\|modal-rules" src --include="*.tsx" --include="*.jsx"
```
Expected: exactly two hits, `AdminPage.jsx` and `RulesPage.jsx`, both on the **inner** `admin-modal-body` / `rules-modal-body` class. Zero hits on the root `modal-admin` / `modal-rules`.

If a root hit appears, **stop and report** — the premise of the deletion is wrong and the spec needs amending.

- [ ] **Step 3: Create `src/frontend/src/styles/modal.css`**

```css
/* The whole dialog contract. A modal sizes to its own content between a floor and a reading
   measure; the overlay owns the scroll. See
   docs/superpowers/specs/2026-07-31-webmail-content-sized-modals-design.md */

/* margin:auto on the child, not align-items:center on the overlay: centred while the dialog
   fits, anchored to the top and scrollable once it does not. align-items:center plus a scroll
   makes the overflowing top unreachable. */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  z-index: 200;
  display: flex;
  overflow-y: auto;
  padding: 24px;
}

/* The ceiling that matters is the prose's, below: an intrinsic contribution is clamped by the
   child's own max-width, so max-width here is only the viewport guard. flex:none because a flex
   item defaults to shrinking, which would take the dialog under that guard. */
.modal {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  margin: auto;
  flex: none;
  width: max-content;
  min-width: var(--modal-w, 24rem);
  max-width: min(56rem, calc(100vw - 48px));
  overflow-wrap: break-word;
  padding: 24px;
}

/* gap, because space-between distributes zero free space under max-content and would draw the
   title flush against the ✕ whenever the header is the widest element. */
.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  margin-bottom: 20px;
}

.modal-title {
  font-size: 16px;
  font-weight: 600;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.modal-close {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 16px;
  color: var(--text-muted);
  padding: 2px 6px;
  border-radius: var(--radius-sm);
  transition: color 0.15s;
}

.modal-close:hover { color: var(--text); }

.modal-success {
  color: var(--success);
  font-size: 14px;
  padding: 12px 0;
}

/* This is what actually stops the box growing. balance equalises the lines instead of leaving a
   two-word orphan on the last one. */
.modal p,
.modal .modal-hint {
  max-width: 56ch;
  text-wrap: balance;
}

/* A form control declares no intrinsic width — flex-basis:0 in .field-h, width:100% in the other
   two idioms — so a content-sized dialog would collapse onto its floor. One amendment per idiom,
   each adding .modal to the selector it amends: a single blanket selector cannot both beat
   .field-h's 0-1-1 and lose to .folder-pick-filter's 0-1-0. */
.modal .field-h > :is(input[type="text"], input[type="password"], input[type="email"],
                      input[type="number"], select, .quota-field, .identity-combo) {
  flex-basis: var(--field-w, 34ch);
  min-width: 0;
  max-width: 100%;
}

.modal .field > :is(input, select, textarea) {
  width: var(--field-w, 34ch);
  max-width: 100%;
}

.modal .rule-wizard-input {
  width: var(--field-w, 34ch);
  max-width: 100%;
}
```

- [ ] **Step 4: Import it in the right place**

In `src/frontend/src/main.tsx`, add the import between `index.css` and `shell.css`:

```tsx
import './index.css'
import './styles/modal.css'
import './styles/shell.css'
import './styles/tooltip.css'
import './styles/mail.css'
```

Position is load-bearing: `mail.css` must stay **after** it, so `.folder-pick-filter`'s declared basis is the later declaration in the one place where the two tie.

- [ ] **Step 5: Remove what moved and what died from `index.css`**

Delete strictly **bottom-up**, so each line number is still valid when you reach it:

1. `.modal-rules` and `.modal.modal-rules > .modal-header` (~`:2064-2081`). `.rules-modal-body` and everything below it is live — keep it, and retitle the `/* ── Rules modal ── */` banner to `/* ── Rules page body ── */`.
2. `.modal-admin` and `.modal.modal-admin > .modal-header` (~`:1087-1105`). Same treatment: `.admin-modal-body` and below are live; retitle to `/* ── Admin page body ── */`.
3. `.modal-overlay`, `.modal`, `.modal-header`, `.modal-title`, `.modal-close`, `.modal-close:hover`, `.modal-success` (~`:934-990`) with the `/* ── Change password modal ── */` banner, which named one dialog for rules governing all nineteen.
4. `max-width: 460px;` from `.modal.identity-modal` (~`:553`). The rule becomes empty — delete the selector too, and keep the comment above it only insofar as it still describes `.identity-combo` on the next line.

Leave `.field-h`'s own rules alone — the amendment lives in `modal.css` and beats them by adding `.modal`.

**`.attachment-viewer`, `.palette-zoom-modal` and `.rule-help-modal` are not touched in this task.** The first two are workspaces that size to the window on purpose and keep their rules for good; the third loses its width in Task 2 but keeps its band stack.

- [ ] **Step 6: Remove the two superseded rules from `mail.css`**

Delete `max-width: 520px;` from `.modal-folders` (`:231`), keeping the rest of the rule (`max-height: 78vh`, the flex column) — its band stack is still wanted.

Delete `.modal.folder-pick-modal { max-height: 100%; overflow-y: auto; }` (`:1146`) **together with the three-line comment above it** (`:1142-1145`), which explains a workaround that no longer exists.

- [ ] **Step 7: Verify nothing broke that a machine can see**

```bash
cd src/frontend && npm run lint && npm run typecheck && npm test
```
Expected: all three pass. None of them can see a width — this only proves no selector typo took a JS module down with it.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/styles/modal.css src/frontend/src/main.tsx src/frontend/src/index.css src/frontend/src/styles/mail.css
git commit -F - <<'EOF'
Extract the modal contract into modal.css

Dialogs size to their content; the overlay owns the scroll. Drops the
dead .modal-admin/.modal-rules roots and folder-pick's clip workaround.
EOF
```

---

### Task 2: Drop the eleven inline widths

With the contract in place, the per-dialog pixel caps are what stops content sizing from happening. Remove them, and add the sweep that stops them coming back.

**Files:**
- Create: `src/frontend/src/styles/modals.test.ts`
- Modify (delete `style={{ maxWidth: … }}` from the `.modal` root only): `src/frontend/src/modules/mail/compose/ComposeView.tsx:505,538`, `src/frontend/src/modules/mail/folders/CreateFolderModal.tsx:25`, `src/frontend/src/modules/mail/folders/FolderManager.tsx:113`, `src/frontend/src/modules/mail/list/AdvancedSearchModal.tsx:49`, `src/frontend/src/modules/settings/accounts/ConnectedAccountsPage.tsx:51`, `src/frontend/src/modules/settings/admin/AddEditDomainModal.jsx:36`, `src/frontend/src/modules/settings/admin/AddEditUserModal.jsx:64`, `src/frontend/src/modules/settings/admin/ExternalDomainDialog.tsx:105`, `src/frontend/src/modules/settings/rules/RulesPage.jsx:606,764`
- Modify: `src/frontend/src/index.css` — `.rule-help-modal` loses `max-width: 600px`. Find it by selector, not by line: Task 1 deleted ~60 lines above it, so any number quoted here is stale.

**Interfaces:**
- Consumes: `--modal-w` / `--field-w` from Task 1; `.modal` must already be `width: max-content`, or every one of these dialogs jumps to the 56rem ceiling.
- Produces: nineteen dialogs whose width is emergent. Task 3 measures them.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/styles/modals.test.ts`:

```ts
import { describe, it, expect } from 'vitest'

// ?raw on a .tsx source is the same mechanism palettes.test.ts uses on main.tsx.
const sources = import.meta.glob('../**/*.{jsx,tsx}', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>

/** A dialog's width is the contract's business, never a component's. */
function inlineWidths(): string[] {
  return Object.entries(sources)
    .filter(([path]) => !path.includes('.test.'))
    .flatMap(([path, src]) => src.split('\n')
      .map((line, i) => ({ path, at: i + 1, line }))
      .filter(({ line }) => /className="modal[\s"]/.test(line) && /[Ww]idth:/.test(line))
      .map(({ path, at }) => `${path}:${at}`))
}

describe('modal roots', () => {
  // Without this the glob could return nothing and every check below would pass vacuously.
  it('reads the components, not an empty glob', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('carry no inline width', () => {
    expect(inlineWidths()).toEqual([])
  })
})
```

- [ ] **Step 2: Run it and watch it fail**

```bash
cd src/frontend && npx vitest run src/styles/modals.test.ts
```
Expected: `reads the components` PASSES, `carry no inline width` FAILS listing eleven `path:line` entries. If the first test fails, the glob path is wrong — fix that before touching any component.

Record the eleven paths from the failure output; they are the exact work list for Step 3.

- [ ] **Step 3: Delete the eleven attributes**

In each of the eleven lines, remove only the `style` attribute on the `.modal` root. For example, in `AddEditUserModal.jsx:64`:

```jsx
- <div className="modal" style={{ maxWidth: '600px' }} onClick={e => e.stopPropagation()}>
+ <div className="modal" onClick={e => e.stopPropagation()}>
```

Two are not plain `className="modal"` and need the same treatment — `CreateFolderModal.tsx:25` and `FolderManager.tsx:113` use `onClick={event => …}` rather than `e`; keep each file's own parameter name.

Do **not** touch `style` attributes anywhere else in these files. `RulesPage.jsx:615` carries `style={{ marginBottom: '16px' }}` on an `.alert` one line into the same dialog — it is not a width and not on the root.

- [ ] **Step 4: Remove the last declared width**

In `src/frontend/src/index.css`, find `.rule-help-modal` by selector and delete its `max-width: 600px;`. Keep `padding: 0`, the flex column and `max-height: 80vh` — that dialog keeps its band stack and its internal scroll on `.rule-help-body`, because the overflow there is on the body rather than on `.modal`.

One existing assertion mentions `max-width` and is **not** related: `SquireEditor.mount.test.tsx:54` checks the compose editor's inline-image rule. Leave it alone; it must stay green.

- [ ] **Step 5: Run the test and the suite**

```bash
cd src/frontend && npx vitest run src/styles/modals.test.ts && npm test && npm run lint
```
Expected: the sweep passes, the full suite passes, lint clean. `npm test`, not `--no-build`-style shortcuts — a new test file was added.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/styles/modals.test.ts src/frontend/src/modules src/frontend/src/index.css
git commit -F - <<'EOF'
Drop the per-dialog pixel widths

Eleven inline maxWidth attributes and rule-help's 600px, replaced by the
contract's floor and measure. A source sweep guards the regression.
EOF
```

---

### Task 3: Calibrate at the browser and document

The acceptance gate. Everything above is unverified until this runs: jsdom computes no layout, so nothing landed in Tasks 1–2 has actually been seen working. `--field-w: 34ch` is a starting point, not a result.

**Files:**
- Modify: `src/frontend/src/styles/modal.css` — the calibrated `--field-w`, plus any per-row override
- Modify: `src/frontend/CLAUDE.md` — the contract note
- Create: `docs/superpowers/plans/2026-07-31-content-sized-modals-measurements.md` — the before/after table

**Interfaces:**
- Consumes: the full contract from Tasks 1–2.
- Produces: a calibrated `--field-w` and a recorded measurement table.

- [ ] **Step 1: Start the app**

```bash
cd src/frontend && npm run dev
```

Log in against the dev API. **Do not point a local frontend at the deployed API** — `localhost` is not in its CORS origin list, so every request fails and no dialog gets the data it needs to render at its real width. Run all-local or all-deployed, never mixed.

- [ ] **Step 2: Measure the nineteen dialogs**

For each dialog, open it and read the real geometry rather than judging by eye:

```js
const m = document.querySelector('.modal')
console.log(m.className, Math.round(m.getBoundingClientRect().width))
```

Cover all nineteen: `DeleteConfirmModal` (row delete **and** the empty-trash variant, which is the reported symptom), `ImportReportModal`, `ComposeView`'s two leave guards, `CreateFolderModal`, `FolderManager`'s rename, `AdvancedSearchModal`, `MoveMessagesModal`, `AttachmentViewerModal`, `ConnectedAccountsPage`, `AddEditDomainModal`, `AddEditUserModal`, `ExternalDomainDialog`, `AppearancePage`'s palette zoom, `IdentityDialog`, `SystemFoldersModal`, `RulesPage`'s help panel and its two editors.

At three viewport widths: **1024** (the `min-width` floor `shell.css:6` declares), **1440**, **2560**.

Record every number in the measurements file against the pre-change width from the spec's inventory. A dialog that came out **narrower** than before is the one regression this change can cause; flag it rather than adjusting it away.

- [ ] **Step 3: Check the three known failure modes**

Read the computed style, not the rendered look:

```js
getComputedStyle(document.querySelector('.folder-pick-filter')).flexBasis   // expect 200px
getComputedStyle(document.querySelector('.quota-field input[type=number]')).width  // expect 80px
getComputedStyle(document.querySelector('.identity-combo input')).width     // expect ≈ --field-w, not 0
```

Then, in `SystemFoldersModal`, confirm a long folder path drives the `<select>` to the ceiling and `max-width: 100%` lets it shrink back rather than overflowing the box. And in `DeleteConfirmModal`, confirm an `entityLabel` with no spaces breaks instead of overflowing.

- [ ] **Step 4: Test the localization case**

In the console, replace a confirm's text with a string ~35% longer, standing in for German:

```js
document.querySelector('.modal p').textContent =
  'Diese Aktion wird alle E-Mail-Nachrichten unwiderruflich aus dem Papierkorb-Ordner entfernen. Dieser Vorgang kann nicht unterbrochen oder rückgängig gemacht werden.'
```

Expected: the dialog grows to the 56ch measure and the lines come out balanced — no two-word orphan on the last line, which is the defect the whole change answers. Screenshot it beside the English.

- [ ] **Step 5: Calibrate `--field-w`**

If the form dialogs came out cramped or bloated, adjust the `34ch` fallback in all three amendments in `modal.css` together — they must stay one number. If a single row genuinely needs more (the rules editor's *value* field is the expected candidate), give that row `style={{ '--field-w': '48ch' }}` rather than moving the global.

Re-measure after any change. State the final value and why in the measurements file.

- [ ] **Step 6: Document the contract**

Add to `src/frontend/CLAUDE.md`, in the Design & UX section:

> **A dialog sizes to its content, never to a pixel count** (`src/styles/modal.css`). `.modal` is `width: max-content` between `--modal-w` (24rem, the floor) and a viewport guard; what actually bounds it is the prose's own `max-width: 56ch`, since a box's intrinsic contribution is clamped by its child's max-width. Form controls declare no intrinsic width of their own — `flex-basis: 0` in `.field-h`, `width: 100%` in `.field` and `.rule-wizard-input` — so each idiom is amended under `.modal` to take a `--field-w` measure (34ch). **One amendment per idiom rather than one blanket selector, and that is a cascade constraint, not a style choice**: written with `:is()` a single selector overrides `.folder-pick-filter` and `.quota-field`'s number box, and written with `:where()` it loses to `.field-h input { flex: 1 }` and leaves every form on the floor. **The scroll is on `.modal-overlay`, never on `.modal`** — a scroll container clips absolutely positioned descendants, which is what used to cut off the folder picker's dropdown — and the overlay centres with `margin: auto` on the child rather than `align-items: center`, so a dialog taller than the viewport anchors to the top and stays reachable. `modal.css` must be imported after `index.css` and before `mail.css`. Do not reintroduce a per-dialog width: a new dialog declares `--field-w` on the row that justifies it, or nothing at all.

- [ ] **Step 7: Full verification**

```bash
cd src/frontend && npm run lint && npm run typecheck && npm test && npm run build
```
Expected: all four pass.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/styles/modal.css src/frontend/CLAUDE.md docs/superpowers/plans/2026-07-31-content-sized-modals-measurements.md
git commit -F - <<'EOF'
Calibrate the field measure and document the contract

Nineteen dialogs measured at three widths against a 35%-longer string.
EOF
```

---

## Known Minors

Report any of these that survive, per the project's rule that a minor visible on screen is fixed rather than excused:

- A dialog measurably **narrower** after the change than before.
- A control that overflows its dialog's right edge at 1024px.
- A `<select>` whose widest option drives its dialog to the 56rem ceiling — content sizing working as specified, but possibly not as wanted.

Twenty minutes per attempt, three attempts, then write it up rather than continuing.
