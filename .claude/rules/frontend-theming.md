---
paths:
  - "src/frontend/src/styles/**"
  - "src/frontend/src/contexts/ThemeContext.tsx"
  - "src/frontend/src/modules/settings/appearance/**"
  - "src/frontend/index.html"
---

# Theming

Token contract lives in `src/styles/`:
- `tokens.css` — role tokens only (`--font`, `--radius-sm`, `--radius-md`, ...). **A token names a role, never a color.** Components must never hard-code a color; add a role token instead if one doesn't exist.
- `theme-<id>.css` — one file per **palette** (`night`, `classic`, `forest`, `slate`, `plum`, `ink`), each defining the 33 role tokens for `[data-palette='<id>']`, further overridden by `[data-palette='<id>'][data-theme='dark']`. Twelve combinations. **The selectors are deliberately not anchored to `html`**: an attribute selector matches any element, which is what lets the Appearance page's thumbnails re-declare a whole palette on one `<span>` and preview a palette that is not the active one. Anchoring them to `html[data-palette='…']` would blank every thumbnail while leaving the app itself correct. `src/styles/palettes.test.ts` asserts every palette file declares the same role set as `theme-night.css`, in both blocks — a missing `--quote-text` is a browser default nobody notices in review, and a missing `--list-row-selected-bg` silently falls back to the light value in dark mode. `classic`'s dark block is the one recorded exception: it inherits `--danger`, `--danger-hover` and `--success` from its own light block.
- `shell.css` — application shell layout (topbar, rail, settings pane, etc.), consumes the role tokens.
- `mail.css` — the mail module's three columns, folder tree, list rows and reader. New CSS goes here rather than into `index.css`, which is already ~2200 lines. Contains no literal color.

Mail added role tokens across all four palette-mode blocks, for states a settings pane does not have: `--list-row-hover`, `--list-row-selected-bg`/`-fg`, `--list-separator`, `--badge-count-bg`/`-fg`, `--reader-header-border`, `--quote-text`. `--accent-unread` was provisioned in the shell slice and has consumers only since the mail module. `--attachment-chip-bg` went the way of `--list-row-unread-bg` when the reader's chips took the accent fill.

**`--action-primary-fg` is `#ffffff` in all twelve blocks, and that is a decision rather than an oversight.** It was per-mode — near-black over dark mode's light accents, which reads better on a contrast meter — and was rejected on look: the owner wants white text over the accent in both modes. Every consumer sits on `--action-primary` (`.btn-primary`, the blocked-images button and its seam, the attachment chips, the identity pill), so the token stays the one place to change it, and no component hard-codes a white. Do not "restore" the per-mode values, and do not collapse the token into a literal. **Unread rows deliberately wear the selected tokens** (background, fg, inset bar) plus bold and the dot — same tokens rather than copied values, so the two looks cannot drift apart; the old `--list-row-unread-bg` was removed with its last consumer. What still singles out unread among selected-looking rows is the bold and the dot.

Palette and theme are selected via `data-palette` / `data-theme` attributes on `<html>`, persisted in `localStorage` (`appearance_palette`, `appearance_theme`; theme is `'light' | 'dark' | 'system'`). A blocking inline `<script>` in `index.html` reads both keys and sets the attributes **before first paint** (avoids a flash of the wrong theme) — it duplicates the resolution logic that `ThemeContext` also runs, deliberately, since the context only mounts after React hydrates. **It therefore also repeats the palette names**, which it cannot import — `PALETTE_IDS` (`ThemeContext.tsx`) is the module-side list, and `palettes.test.ts` asserts the two agree. Forgetting the script half produces a bug no other test sees and no reviewer notices: on reload the script rejects the stored palette, paints `night`, and React corrects it a frame later — a flash, on first load, for one palette.

`ThemeContext` (`src/contexts/ThemeContext.tsx`) owns `theme`/`palette` state, re-applies the `data-theme`/`data-palette` attributes on change, and (when `theme === 'system'`) subscribes to `matchMedia('(prefers-color-scheme: dark)')` for live OS-theme changes. `AppearancePage` is the only UI that calls `setTheme`/`setPalette`.

**Night `--action-primary` hue-shift** — deliberate: in the night palette, `--action-primary` is navy in light mode but shifts to coral in dark mode (`theme-night.css`, see the comment above the dark-mode block) because navy would dissolve into the dark background. The *role* (`--action-primary`) is stable across modes; the *hue* backing it is not. Do not "fix" this into a single fixed color. `forest` and `plum` do the same for the same reason — evergreen and aubergine both dissolve into a dark ground, so dark mode promotes their amber and gold to `--action-primary`.
