# Content-sized modals — browser measurements

Measured on `snoopy-dev.mail.weesky.net` (Chrome, real session) after `89ff5f8`.
"Before" is the shipped width from the spec's inventory; every "after" is `getBoundingClientRect()`,
never an eyeball estimate.

## Widths

| Dialog | before | after | driven by |
|---|---:|---:|---|
| `DeleteConfirmModal` (empty trash) | 380 | **461** | its prose, 56ch measure |
| `AddEditUserModal` | 600 | **614** | the quota row's declared 58ch |
| `RuleEditorModal` | 680 | **565** | the wizard's own rows |
| `SystemFoldersModal` | 520 | **516** | its `<select>`s' widest option |
| `MoveMessagesModal` | 460 | **391** | the folder list |
| `AdvancedSearchModal` | 640 | **433** | `--field-w` 34ch |
| `CreateFolderModal` | 560 | **433** | `--field-w` 34ch |
| `IdentityDialog` | 460 | **433** | `--field-w` 34ch |
| `ExternalDomainDialog` | 480 | **433** | `--field-w` 34ch |
| `AddEditDomainModal` (`.field`) | 380 | **384** | the floor |
| `ComposeView` leave guard | 420 | **384** | the floor |
| `rule-help-modal` | 600 | **896** | its `<dl>` grid, at the 56rem ceiling |
| `palette-zoom-modal` | 760 cap | **544** | unchanged, out of scope |

Every plain form dialog converging on 433px is the intended outcome, not a coincidence: each is
`48 (padding) + 110 (label) + 16 (gap) + 34ch`. Content only diverges where it genuinely differs.

`rule-help-modal` is the one large move. Its content is a two-column `<dl>`, not `<p>`, so the 56ch prose
measure does not apply and it grows to the ceiling. It reads better at 896 than at 600; recorded as a
change rather than a defect.

## Named failure modes

| Check | Expected | Measured |
|---|---|---|
| `.folder-pick-filter` keeps its basis | 200px | **200px** |
| `.quota-field input[type=number]` stays narrow | 80px | **80px** (needed `flex: none`, see below) |
| `.identity-combo input` gets a measure, not 0 | ≈`--field-w` | **257px** |
| `SystemFoldersModal`'s `<select>` does not overflow | inside the box | **1px inside** |
| `.modal` no longer owns a scroll | `overflow-y: visible` | **visible** |
| 120-char unbroken token in `entityLabel` | wraps | **wraps, 6px inside the padding box** |

## Vertical behaviour

`ExternalDomainDialog` at a 682px viewport is 678px tall — the case the change exists for.
Anchored at `top: 24`, the overlay carries 44px of scroll; scrolled to the end, "Create domain" sits at
633px, inside the viewport, and scrolling back restores the header. **Both ends reachable**, which the old
`align-items: center` with no scroll could not do.

## Localization

`DeleteConfirmModal` (empty trash), German string ~35% longer than the English:

- English: 461px, 2 lines
- German: **473px, 4 lines** (+12px), split 2+2 and balanced — no orphan

This is the defect that opened the thread, exercised in the language it was raised for.

## Viewports

- **1024×682** — full pass, the `shell.css:6` floor. No dialog overflows, no control spills.
- **1594×862** — full pass. Dialogs centred exactly (561px each side, 312px top and bottom on the confirm).
- **2560** — **not measured.** The display caps the window at 1594px wide. The ceiling
  `min(56rem, 100vw - 48px)` is already saturated at 896px from ~944px of viewport upward, so no new
  behaviour can appear above that; a wider viewport only adds empty space around a centred dialog. Stated
  as an argument, not as a measurement.

## Not measured

Six of the nineteen were not reached, each for a reason:

- `ImportReportModal` — needs a contacts file upload.
- `ConnectedAccountsPage`'s dialog — the route redirects to `/mail`; no connected account on this mailbox.
- `FolderManager` rename and delete — same `DeleteConfirmModal` and `.field-h` shapes already covered.
- `ConvertConfirmModal` — reachable only by switching Extended rules off, which rewrites the account's rules.
- `AttachmentViewerModal` — out of scope, its rules are untouched.
- `DeleteConfirmModal` row-delete variant — same component as the empty-trash one, different props.

## Fixes this campaign produced

1. **`flex-basis` does not feed intrinsic sizing.** The first implementation set
   `flex-basis: var(--field-w)`; 34ch, 44ch and 60ch all produced the identical box. Changed to `width`.
2. **`<select>` exempted** — its widest option already is the measure.
3. **The quota row declares `--field-w: 58ch`** — a range input's intrinsic width is a fixed ~129px.
4. **`.modal .quota-field input[type="number"] { flex: none }`** — `.field-h input[type="number"] { flex: 1 }`
   is a *descendant* selector, so the number box had always carried a grow factor. Invisible at a 250px row,
   a 208px number field at 438px.
