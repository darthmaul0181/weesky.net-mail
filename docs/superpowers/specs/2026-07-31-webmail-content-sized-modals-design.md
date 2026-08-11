# Content-sized modals

**Date** — 2026-07-31
**Scope** — `src/frontend`, dialog sizing and vertical overflow, in preparation for localization.

## Problem

`.modal` (`src/frontend/src/index.css:947`) is `width: 100%; max-width: 380px` and nothing else. Every
dialog is therefore a fixed box whose width was chosen by hand against one English string. The empty-trash
confirm is the visible symptom: at 380px, "This action will permanently delete all emails from the Trash
folder." wraps onto a second line while the sentence below it fits on one, so the paragraph reads as ragged
rather than as two sentences.

Above that base sit **19 modal roots carrying 12 distinct widths**, eleven of them declared as inline
`style={{ maxWidth }}`: 380 ×3, 420 ×3, 460 ×2, 480, 520, 560 ×2, 600 ×2, 640, 680, 712, 760 and
`min(900px, 92vw)`. None is derived from its content. A translation 30% longer — German, Finnish —
re-breaks each of them individually, with no global lever.

Vertically the situation is worse and already documented: the overlay centres its child with
`align-items: center` and no scroll, and 15 of the 19 modals declare no `max-height`, so a dialog taller
than the viewport overflows past **both** edges with neither reachable. `src/frontend/src/styles/mail.css:1142`
records the local workaround (`max-height: 100%` plus `overflow-y: auto` on `.modal.folder-pick-modal`) and
the price it pays: a scroll container clips absolutely positioned descendants, so the folder filter's
dropdown is cut off by the very rule that keeps the dialog on screen.

## Design

### The box follows the text, not a pixel count

The load-bearing idea is that **the ceiling belongs to the prose, not to the container**. A box's intrinsic
contribution is clamped by its own `max-width`, so a reading measure on the paragraph is enough to stop
`.modal` growing — the container never needs to know a pixel width. A form has no prose: it widens to its
broadest field row and stops there on its own.

This is already the house idiom (`.folders-page-hint { max-width: 62ch }`, `.mail-full-pane p { max-width: 46ch }`);
the change extends it from the text to the box around it.

```css
.modal-overlay {
  display: flex;              /* no align-items / justify-content */
  overflow-y: auto;           /* the scroll lives here, never on .modal */
  padding: 24px;
}

.modal {
  margin: auto;               /* centred when it fits, top-anchored when it does not */
  flex: none;                 /* without it flex-shrink squashes an over-tall dialog */
  width: max-content;
  min-width: var(--modal-w, 24rem);
  max-width: min(56rem, calc(100vw - 48px));
  overflow-wrap: break-word;
}

.modal-header { gap: 24px; }  /* space-between collapses under max-content */
.modal p, .modal .modal-hint { max-width: 56ch; text-wrap: balance; }
```

That is the whole mechanism for a dialog made of prose. A dialog made of fields needs one rule more, for
the reason set out in the next section.

`margin: auto` on a flex item, not `place-items: safe center`: identical result — centred while the dialog
fits, anchored to the top and scrollable once it does not — with universal support and no prefixing.

`flex: none` is not decoration. The overlay's main axis is horizontal, but a flex item defaults to
`flex-shrink: 1`, and an item wider than the line would be shrunk below the `max-width` clamp that is meant
to be the only thing bounding it.

`.modal-header` is `display: flex; justify-content: space-between`. Under `max-content` the free space
`space-between` distributes is zero, so a dialog whose header is its widest element would draw the title
flush against the ✕. The gap is what reserves that distance intrinsically.

`text-wrap: balance` is what answers the reported symptom directly: it equalises line lengths instead of
leaving "Trash folder." alone on a second line. Where it is unsupported the text wraps normally — the
degradation is the current behaviour, so there is nothing to guard.

`overflow-wrap: break-word` covers the one input that can defeat a measure: `DeleteConfirmModal` prints
`entityLabel` verbatim, and a folder path carries no spaces to break at.

### A form has no prose, so it needs a field measure

`width: max-content` alone would deliver nothing for a form, and it is worth being precise about why: a
size scale of `sm/md/lg/xl` classes was designed first and rejected on this finding.

`index.css:1350` declares `.field-h input, .field-h select { flex: 1 }`, i.e. `flex-basis: 0`, and **a zero
basis contributes nothing to intrinsic sizing**. A `.field-h` row's max-content contribution is therefore
exactly `110px` (the label column) `+ 16px` (the gap) `+ 0`. The same holds for `.field-h .quota-field
{ flex: 1 }`, for `.identity-combo input { width: 100% }` and for `.rule-wizard-input { width: 100% }`
(`index.css:2186`) — a percentage is equally invisible to intrinsic sizing. Under a content-sized `.modal`
every form dialog would collapse onto its floor, the floor would *be* the width, and a size scale would be
the twelve magic numbers in new clothes. Only the prose dialogs would size to anything.

**The measure belongs to the control, not to the row.** The dialogs use three form idioms, not one —
`.field-h` (label beside control, eight dialogs), `.field` (label above, `AddEditDomainModal`) and the
bespoke `.rule-wizard` (the two rules editors) — so a rule keyed on any row class would cover a third of
the problem. Keyed on the control it covers all three at once:

```css
/* .field-h — amends index.css:1350, which is where flex-basis: 0 is declared */
.modal .field-h > :is(input[type="text"], input[type="password"], input[type="email"],
                      input[type="number"], .quota-field, .identity-combo) {
  flex-basis: auto;
  width: var(--field-w, 34ch);
  min-width: 0;
  max-width: 100%;
}

/* A <select> is exempt — its widest option already is the right measure */
.modal .field-h > select { flex-basis: auto; min-width: 0; max-width: 100%; }

/* .field — a column flex row, so the measure is a width */
.modal .field > :is(input, select, textarea) { width: var(--field-w, 34ch); max-width: 100%; }

/* .rule-wizard — amends index.css:2186's width: 100% */
.modal .rule-wizard-input { width: var(--field-w, 34ch); max-width: 100%; }
```

**One rule per idiom, not one rule for all three, and the reason is the cascade.** A single control-level
selector cannot satisfy both constraints at once: written with `:is()` it weighs 0-2-1 and overrides
`.folder-pick-filter { flex: 0 1 200px }` (0-1-0) and `.quota-field input[type="number"] { width: 80px }`
(0-2-1, decided by source order), the two rules that must keep winning; written with `:where()` it weighs
0-1-0 and loses to `.field-h input { flex: 1 }` (0-1-1), leaving every form pinned to the floor. Adding
`.modal` to each idiom's own selector beats exactly the declaration it is amending and nothing else.

**The measure is a `width`, never a `flex-basis`, and that was established by measurement rather than by
reading the spec.** The first implementation set `flex-basis: var(--field-w)`; in Chrome, `--field-w` at
34ch, 44ch and 60ch all produced the identical box, because a flex basis does not feed the flex container's
intrinsic contribution — only the item's own `width` does. `flex-grow` is left alone, so a control in a row
wider than its measure still fills it.

The type lists are explicit because `input[type="range"]` (the quota slider) and `input[type="checkbox"]`
(the toggle switch) must keep their own sizing. `.quota-field` and `.identity-combo` are named because they
are wrappers standing where a control would be, and `index.css:1377` gives the first of them the same
`flex: 1`.

**A `<select>` is exempt.** Its widest option already is exactly the measure content sizing is looking for —
`SystemFoldersModal` lands at 516px on its folder names with no help at all — and imposing `--field-w` on it
made that dialog *narrower* than the names it lists.

Two consequences follow for the quota row, both measured:

- **A range input's intrinsic width is a fixed ~129px** whatever it controls, so no content-derived measure
  can express what the slider needs. That row declares its own: `.modal .field-h:has(> .quota-field)
  { --field-w: 58ch }`, which restores `AddEditUserModal` to 614px and the slider to 319px.
- **`.field-h input[type="number"] { flex: 1 }` is a descendant selector**, so the quota number box has
  always carried a `flex-grow` nobody intended. Invisible while the row was 250px; at 438px it drew a 208px
  number field. `.modal .quota-field input[type="number"] { flex: none }` pins it back to its declared 80px.

The scale then collapses to **two tokens, both in characters**:

| token | value | what it decides |
|---|---|---|
| `--modal-w` | `24rem` / 384px | how narrow a dialog may get. One floor, not four. |
| `--field-w` | `34ch` | what a field must be able to show. This is what actually sets a form's width. |

A form dialog is then `48px` (padding) `+ 110` (label) `+ 16` (gap) `+ 34ch` ≈ **420px** — precisely the
step six dialogs were hand-set to, which is corroboration that the measure is right rather than a number
retro-fitted to the answer. `34ch` is what the product's longest field content needs: an address
(`darth_claude@weesky.be`, 22 characters) with room to spare. The exact value is calibrated at the browser,
not argued.

`AddEditUserModal` exceeds 420px on its own, because its domain `<select>` sizes to its widest option and
its quota row carries a fixed 80px number field beside the slider. That is the wanted behaviour, not an
exception needing a declaration.

**There are no size classes.** A dialog that needs to be wide no longer says so on its box but on the row
that justifies it — `style={{ '--field-w': '48ch' }}` on the rules editor's *value* field, for instance.
Width stops being a chosen number and becomes a consequence. Twelve pixel counts become two character
counts, and characters survive a translation and a font change where pixels do not.

Eleven inline `style={{ maxWidth }}` attributes are deleted, as are the width declarations in
`.modal.identity-modal` (460px), `.modal-folders` (520px) and `.modal.folder-pick-modal` (460px); the rest
of each of those rules — `.modal-folders`' band stack, `.identity-combo`, the folder filter — is untouched.

The floor is 24rem rather than the 22rem a bare minimum would suggest, so the dialogs sitting at 380px
today do not visibly narrow.

**Amended 2026-08-09 (the mobile slice): below 640px the box takes the screen.** `min-width` always beats
`max-width`, so on a 360px phone the 24rem floor put a 384px dialog in the 312px the overlay's padding
leaves — and since the overlay carries `overflow-y: auto`, CSS Overflow 3 forces the other axis to `auto`
too, so the symptom was never a scrolling page but a dialog wider than the screen inside an overlay
scrolling sideways to reach it. `modal.css`'s `@media (max-width: 639px)` block therefore drops
`--modal-w` to 0 and gives `.modal` `width: 100%` with 18px of padding and a 12px overlay inset. It is the
one width in this design that is a pixel count rather than a consequence of the content, and it is bounded:
at every other width the contract above stands unchanged, and the scroll stays the overlay's.

**The control rule is scoped under `.modal`, never applied globally.** The settings pages use
`.field-h.is-setting`, where the project's own note requires a `<select>` to size to its widest option; an
unscoped selector would overwrite that with a fixed measure, and `AppearancePage`, `GeneralPage` and the
contact editor would all move with it.

### Out of scope, deliberately

`.attachment-viewer` (`min(900px, 92vw)`) and `.palette-zoom-modal` (760px) keep their rules. These are
workspaces rather than dialogues — they size to the window, not to their text, and content-sizing an image
viewer is meaningless since the image is what it is. They inherit the new vertical behaviour and nothing
else.

`.rule-help-modal` is the one dialog that is both: it loses its 600px width and sizes to its prose like any
other, but **keeps** its `max-height: 80vh` and the internal scroll on `.rule-help-body`. The overflow there
is on the body, not on `.modal`, so the clipping objection does not apply, and a documentation panel wants
its header fixed.

### What this fixes on the way

`src/frontend/src/styles/mail.css:1146` — `.modal.folder-pick-modal { max-height: 100%; overflow-y: auto }` —
is deleted along with its comment. Once the scroll is on the overlay, the dialog cannot be clipped and the
filter's dropdown is no longer cut off by its own scroll container.

The 15 modals declaring no `max-height` stop being unreachable at both ends when they outgrow the viewport.

**`.modal-admin` and `.modal-rules` are dead and go with them.** Both declare `width: 75vw; height: 75vh`
on a root class that no JSX applies: Administration and Rules are `settings-page` screens now, and only
their inner `admin-modal-body` / `rules-modal-body` survived the move (`AdminPage.jsx:29`,
`RulesPage.jsx:992`). Four rules are removed — `.modal-admin`, `.modal.modal-admin > .modal-header`
(`index.css:1089`, `:1100`), `.modal-rules`, `.modal.modal-rules > .modal-header` (`index.css:2066`,
`:2076`) — and nothing else in either block is touched, since the body classes are live. This is in scope
rather than adjacent cleanup: they are modal *width* declarations, and leaving two orphan viewport-sized
roots in the sheet makes the new scale read as having exceptions it does not have.

## Verification

**No test in this repo can catch any of this** — jsdom computes no geometry, so `width: max-content`,
`--field-w`, `text-wrap: balance` and the overlay scroll are all invisible to it.

Every dialog's width becomes **emergent** rather than declared. The browser pass is therefore not a check
at the end of the work, it **is** the deliverable: the 19 dialogs × three viewport widths (1024, 1440,
2560) × two string sets, the current English and a pseudo-locale inflated ~35% standing in for German, with
the before/after width of each recorded. Without that table there is no way to state that no dialog
narrowed, which is the one regression this change can cause.

`--field-w`'s value is calibrated in that pass. `34ch` is the starting point, not a result.

Three failure modes are checked first, each observed in the source rather than hypothesised:

- **`SystemFoldersModal`'s `<select>`s** — they size to their widest option, and a long folder path drags
  the box to the ceiling. `max-width: 100%` is what must then let the control shrink back.
- **`.folder-pick-filter` and `.quota-field input[type="number"]`** — the two rules the per-idiom scoping
  is designed not to touch. Measured, not assumed: both are read off the computed style.
- **`.identity-combo input { width: 100% }`** — the input is nested inside the combo rather than being the
  `.field-h` child itself, which is why the combo is named in the amendment; the check is that the
  percentage inside it then resolves against that basis rather than collapsing to zero.
- **`DeleteConfirmModal`'s `entityLabel`** — a long unbroken folder path, exercising `overflow-wrap`.

Unit tests: nothing to add, since nothing here is assertable. The only check is that no existing test
asserts a deleted inline `style` — a sweep over `*.test.tsx`/`*.test.jsx` finds one `max-width` assertion,
in `SquireEditor.mount.test.tsx:54`, which concerns the compose editor's inline-image rule and is unrelated.

## Non-goals

Right-to-left layout, a translation framework, and the extraction of the strings themselves. This slice
makes the dialogs able to survive longer strings; it does not introduce them.
