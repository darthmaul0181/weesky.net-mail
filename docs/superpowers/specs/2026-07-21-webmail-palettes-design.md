# Four more palettes, and a preview for each

Adds four palettes to the two that ship, and gives the Appearance page a thumbnail per palette so
the choice can be made by looking rather than by trying.

Mockups the four were approved from, rendered on the real three-column layout in both modes:
<https://claude.ai/code/artifact/dc578fd8-a101-424c-a12c-596b76ebe004>

## The four palettes

Each is a complete set of the 33 role tokens in light and dark, in its own
`src/styles/theme-<id>.css`, following `theme-night.css` exactly: a `[data-palette='<id>']` block
and a `[data-palette='<id>'][data-theme='dark']` block. No new token is introduced and no
component changes — a palette that needed a new role would be a palette this layout cannot wear.

**Forest & amber** (`forest`) — evergreen chrome, amber for unread and counts. The warmest of the
four and the only one whose ground is tinted green rather than grey.

```css
[data-palette='forest'] {
  --bg:#f6f7f3; --surface:#ffffff; --surface-raised:#ffffff; --surface-sunken:#eef1ea;
  --border:#dde2d6; --text:#1a1f1a; --text-muted:#66705f;
  --topbar-bg:#1e3a2b; --topbar-fg:#ffffff; --rail-bg:#1e3a2b; --rail-fg:#b8ccbe;
  --rail-item:#2b4d3a; --rail-item-active:#d99a2b; --rail-item-active-fg:#1a1205;
  --pane-item-hover:#eaeee5; --pane-item-active-bg:#fbf2df; --pane-item-active-fg:#1e3a2b;
  --accent-unread:#b8791a;
  --list-row-hover:#eef1ea; --list-row-selected-bg:#fbf2df; --list-row-selected-fg:#1e3a2b;
  --list-separator:#e6ebe0; --badge-count-bg:#b8791a; --badge-count-fg:#ffffff;
  --reader-header-border:#e6ebe0; --quote-text:#7c8676; --attachment-chip-bg:#eef1ea;
  --action-primary:#1e3a2b; --action-primary-hover:#2b4d3a; --action-primary-fg:#ffffff;
  --danger:#c02626; --danger-hover:#9c1c1c; --success:#15803d;
}
[data-palette='forest'][data-theme='dark'] {
  --bg:#141a16; --surface:#1c231e; --surface-raised:#212a23; --surface-sunken:#171d19;
  --border:#2b352d; --text:#e4e9e3; --text-muted:#929c8f;
  --topbar-bg:#0f1712; --topbar-fg:#e4e9e3; --rail-bg:#0f1712; --rail-fg:#a3b8a8;
  --rail-item:#1c2a20; --rail-item-active:#e0a63f; --rail-item-active-fg:#191204;
  --pane-item-hover:#212a23; --pane-item-active-bg:#2a2418; --pane-item-active-fg:#f2ecdc;
  --accent-unread:#e0a63f;
  --list-row-hover:#212a23; --list-row-selected-bg:#2a2418; --list-row-selected-fg:#f2ecdc;
  --list-separator:#2b352d; --badge-count-bg:#e0a63f; --badge-count-fg:#191204;
  --reader-header-border:#2b352d; --quote-text:#869080; --attachment-chip-bg:#212a23;
  --action-primary:#e0a63f; --action-primary-hover:#ebb757; --action-primary-fg:#191204;
  --danger:#f87171; --danger-hover:#ef4444; --success:#4ade80;
}
```

**Slate & teal** (`slate`) — graphite chrome with a deep teal. The most sober of the four: the
accent stays legible at 11px and the ground stays neutral behind message HTML that arrives with
colours of its own.

```css
[data-palette='slate'] {
  --bg:#f4f6f7; --surface:#ffffff; --surface-raised:#ffffff; --surface-sunken:#eceff1;
  --border:#d9dee1; --text:#171b1e; --text-muted:#626d73;
  --topbar-bg:#2b3a41; --topbar-fg:#ffffff; --rail-bg:#2b3a41; --rail-fg:#b3c3c9;
  --rail-item:#3a4d55; --rail-item-active:#0f7d78; --rail-item-active-fg:#ffffff;
  --pane-item-hover:#e7ebed; --pane-item-active-bg:#dcefee; --pane-item-active-fg:#0d5551;
  --accent-unread:#0f7d78;
  --list-row-hover:#eceff1; --list-row-selected-bg:#dcefee; --list-row-selected-fg:#0d5551;
  --list-separator:#e3e7e9; --badge-count-bg:#0f7d78; --badge-count-fg:#ffffff;
  --reader-header-border:#e3e7e9; --quote-text:#78838a; --attachment-chip-bg:#eceff1;
  --action-primary:#0f7d78; --action-primary-hover:#0c6763; --action-primary-fg:#ffffff;
  --danger:#dc2626; --danger-hover:#b91c1c; --success:#16a34a;
}
[data-palette='slate'][data-theme='dark'] {
  --bg:#14181a; --surface:#1d2225; --surface-raised:#232a2d; --surface-sunken:#181d1f;
  --border:#2c3438; --text:#e2e8ea; --text-muted:#8d989d;
  --topbar-bg:#121a1d; --topbar-fg:#e2e8ea; --rail-bg:#121a1d; --rail-fg:#9fb3ba;
  --rail-item:#1f2b30; --rail-item-active:#3fbdb4; --rail-item-active-fg:#06201e;
  --pane-item-hover:#232a2d; --pane-item-active-bg:#16332f; --pane-item-active-fg:#b7e6e0;
  --accent-unread:#3fbdb4;
  --list-row-hover:#232a2d; --list-row-selected-bg:#16332f; --list-row-selected-fg:#b7e6e0;
  --list-separator:#2c3438; --badge-count-bg:#3fbdb4; --badge-count-fg:#06201e;
  --reader-header-border:#2c3438; --quote-text:#7f8a8f; --attachment-chip-bg:#232a2d;
  --action-primary:#3fbdb4; --action-primary-hover:#58ccc3; --action-primary-fg:#06201e;
  --danger:#f87171; --danger-hover:#ef4444; --success:#4ade80;
}
```

**Plum & gold** (`plum`) — aubergine chrome, old gold as the accent. The one with real
personality, worth having precisely because neither shipping palette takes a risk.

```css
[data-palette='plum'] {
  --bg:#f8f5f8; --surface:#ffffff; --surface-raised:#ffffff; --surface-sunken:#f1ebf1;
  --border:#e3d9e3; --text:#1f1a1f; --text-muted:#6d626d;
  --topbar-bg:#3a2140; --topbar-fg:#ffffff; --rail-bg:#3a2140; --rail-fg:#c9b6cd;
  --rail-item:#4d2e54; --rail-item-active:#c9962b; --rail-item-active-fg:#1c1405;
  --pane-item-hover:#efe8ef; --pane-item-active-bg:#f9f0dd; --pane-item-active-fg:#3a2140;
  --accent-unread:#a87a12;
  --list-row-hover:#f1ebf1; --list-row-selected-bg:#f9f0dd; --list-row-selected-fg:#3a2140;
  --list-separator:#ebe2eb; --badge-count-bg:#a87a12; --badge-count-fg:#ffffff;
  --reader-header-border:#ebe2eb; --quote-text:#837783; --attachment-chip-bg:#f1ebf1;
  --action-primary:#3a2140; --action-primary-hover:#4d2e54; --action-primary-fg:#ffffff;
  --danger:#dc2626; --danger-hover:#b91c1c; --success:#16a34a;
}
[data-palette='plum'][data-theme='dark'] {
  --bg:#17141a; --surface:#201c24; --surface-raised:#26212b; --surface-sunken:#1a171e;
  --border:#322b38; --text:#e8e3ea; --text-muted:#988f9c;
  --topbar-bg:#150e18; --topbar-fg:#e8e3ea; --rail-bg:#150e18; --rail-fg:#b9a6bf;
  --rail-item:#291d2e; --rail-item-active:#d9b04a; --rail-item-active-fg:#1c1405;
  --pane-item-hover:#26212b; --pane-item-active-bg:#2d2617; --pane-item-active-fg:#f0e6cf;
  --accent-unread:#d9b04a;
  --list-row-hover:#26212b; --list-row-selected-bg:#2d2617; --list-row-selected-fg:#f0e6cf;
  --list-separator:#322b38; --badge-count-bg:#d9b04a; --badge-count-fg:#1c1405;
  --reader-header-border:#322b38; --quote-text:#8b8290; --attachment-chip-bg:#26212b;
  --action-primary:#d9b04a; --action-primary-hover:#e3bf63; --action-primary-fg:#1c1405;
  --danger:#f87171; --danger-hover:#ef4444; --success:#4ade80;
}
```

**Ink** (`ink`) — near-monochrome: black chrome, paper ground, and a single electric blue that
only ever marks state. Everything colourful on screen belongs to the message, not to the app.

```css
[data-palette='ink'] {
  --bg:#f7f7f8; --surface:#ffffff; --surface-raised:#ffffff; --surface-sunken:#efeff1;
  --border:#dcdcdf; --text:#131315; --text-muted:#67676e;
  --topbar-bg:#131315; --topbar-fg:#ffffff; --rail-bg:#131315; --rail-fg:#a9a9b0;
  --rail-item:#26262a; --rail-item-active:#2563eb; --rail-item-active-fg:#ffffff;
  --pane-item-hover:#eaeaec; --pane-item-active-bg:#e4ecfd; --pane-item-active-fg:#1a44a8;
  --accent-unread:#2563eb;
  --list-row-hover:#efeff1; --list-row-selected-bg:#e4ecfd; --list-row-selected-fg:#1a44a8;
  --list-separator:#e6e6e9; --badge-count-bg:#2563eb; --badge-count-fg:#ffffff;
  --reader-header-border:#e6e6e9; --quote-text:#7d7d85; --attachment-chip-bg:#efeff1;
  --action-primary:#131315; --action-primary-hover:#2b2b30; --action-primary-fg:#ffffff;
  --danger:#dc2626; --danger-hover:#b91c1c; --success:#16a34a;
}
[data-palette='ink'][data-theme='dark'] {
  --bg:#121214; --surface:#1a1a1d; --surface-raised:#202024; --surface-sunken:#161618;
  --border:#2a2a2f; --text:#e8e8ea; --text-muted:#92929a;
  --topbar-bg:#0c0c0e; --topbar-fg:#e8e8ea; --rail-bg:#0c0c0e; --rail-fg:#a0a0a8;
  --rail-item:#1d1d21; --rail-item-active:#5b8cf7; --rail-item-active-fg:#06122e;
  --pane-item-hover:#202024; --pane-item-active-bg:#18233d; --pane-item-active-fg:#cbdaf9;
  --accent-unread:#5b8cf7;
  --list-row-hover:#202024; --list-row-selected-bg:#18233d; --list-row-selected-fg:#cbdaf9;
  --list-separator:#2a2a2f; --badge-count-bg:#5b8cf7; --badge-count-fg:#06122e;
  --reader-header-border:#2a2a2f; --quote-text:#85858d; --attachment-chip-bg:#202024;
  --action-primary:#5b8cf7; --action-primary-hover:#799ff9; --action-primary-fg:#06122e;
  --danger:#f87171; --danger-hover:#ef4444; --success:#4ade80;
}
```

**Two of them shift hue between modes, deliberately, the way `night` already does.** Forest's
evergreen and Plum's aubergine both dissolve into a dark ground, so dark mode promotes the amber
and the gold to `--action-primary`. The role is stable; the hue is not. This is the same note
already written above `theme-night.css`'s dark block, and it must travel to these two files —
without it, the next reader "fixes" the inconsistency.

Palette order in the picker: `night`, `classic`, `forest`, `slate`, `plum`, `ink` — the two that
ship first, so the default stays where it has always been, then the four in the order they were
proposed.

## The preview

**One thumbnail per palette, not one that follows the selection.** Choosing a palette is a
comparison: a single adaptive preview makes the user click all six and hold them in memory, which
is the work the preview exists to remove. Six side by side answer the question at a glance. The
selected palette needs no preview at all — it is already on every surface around the picker.

**The thumbnail costs no new CSS variables, because the palette selectors are not anchored to
`html`.** They are written `[data-palette='night']`, which matches *any* element, so a
`<span data-palette="plum" data-theme="dark">` redefines all 33 tokens on its own subtree. The
thumbnail is therefore ordinary markup consuming the ordinary role tokens, and a palette added
later gets its thumbnail with no change to the thumbnail's CSS.

**This is load-bearing and must be recorded in `src/frontend/CLAUDE.md`.** Narrowing those
selectors to `html[data-palette='…']` — an inviting tidy-up — would silently blank every
thumbnail while leaving the app itself correct.

The thumbnail carries the *resolved* theme, `useTheme().isDark`, not the stored preference:
`system` names no mode by itself, and the preview has to show what the user will actually get.
Both attributes go on the same element, since the dark block needs them together.

Small enough for six across a settings column, faithful enough to decide on: the topbar band, the
rail, and a surface holding three list rows, the first of them unread with its accent bar. It is
`aria-hidden`, since the label beside it already names the palette and a screen reader has no use
for a picture of colours.

The six rows become a grid of cards rather than a stack of `.radio-row`s. The radio input stays
inside the `<label>`, so keyboard selection and `getByLabelText` keep working exactly as today.

## The wiring, and the drift it invites

A palette is four edits, none of which the others imply:

1. `src/styles/theme-<id>.css` — the two blocks.
2. `src/main.tsx` — the import, beside the existing two.
3. `src/contexts/ThemeContext.tsx` — the `Palette` union, and `readPalette`'s validation, which
   today is a two-way comparison (`=== 'classic' ? 'classic' : 'night'`) and becomes a
   membership test over an exported list.
4. `index.html` — the pre-paint script's validation, which today is `if(p!=='night'&&p!=='classic')`
   and becomes the same membership test written out again.

Point 4 cannot import from point 3: the script runs before any module loads, which is the whole
reason it exists. The duplication is deliberate and already documented for two names. At six it
needs a guard, because forgetting it produces a bug that only appears in a real browser on a
reload — the pre-paint script rejects the stored palette, paints the default, and React corrects
it a frame later. A flash, on first load only, for one palette.

**A test asserts the two lists agree**: it reads `index.html` from disk, extracts the palette
names the inline script accepts, and compares that set against the exported list. It fails on a
palette added to one side only, in either direction.

## Tests

- `ThemeContext.test.tsx` — each of the four new ids round-trips through `setPalette` and lands on
  `data-palette`; an unknown stored value falls back to `night`; and `classic`, which used to be
  the only alternative, still survives the rewritten validation.
- A new `src/styles/palettes.test.ts` — every palette file defines the same set of token names as
  `theme-night.css`, in both blocks. A palette missing `--quote-text` renders a browser default
  no one would notice in review, and this is the only cheap way to catch it across six files.
- The same file carries the `index.html` drift test described above.
- `AppearancePage.test.tsx` — the six options are offered in order with their labels; selecting one
  calls `setPalette`; each thumbnail carries the `data-palette` of the option it belongs to and the
  resolved `data-theme`, which is what proves a thumbnail cannot render the wrong palette; and the
  thumbnails are `aria-hidden`, so the accessible name of each option is its label alone.
