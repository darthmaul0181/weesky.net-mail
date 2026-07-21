# Four more palettes, with a preview each — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship four new palettes (`forest`, `slate`, `plum`, `ink`) and give the Appearance page a thumbnail per palette, so the choice is made by looking rather than by trying each one.

**Architecture:** A palette is a stylesheet of 33 role tokens in two blocks plus four wiring edits (import, type, module validation, pre-paint script validation). The thumbnails need no new tokens: the palette selectors are `[data-palette='x']`, unanchored to `html`, so stamping the two attributes on a `<span>` re-declares every token inside it.

**Tech Stack:** React 18 + TypeScript, plain CSS custom properties, Vitest + `@testing-library/react` (jsdom, `globals: true`), Node `fs` for the two file-parity tests.

## Global Constraints

- **The colour values live in the spec, `docs/superpowers/specs/2026-07-21-webmail-palettes-design.md`, and nowhere else.** Copy each CSS block from it verbatim — every hex, in the order written. Do not adjust, round, reformat or "harmonise" a value; they were approved on rendered mockups.
- Palette ids: `forest`, `slate`, `plum`, `ink`. Labels: `Forest & amber`, `Slate & teal`, `Plum & gold`, `Ink`.
- Picker order, first to last: `night`, `classic`, `forest`, `slate`, `plum`, `ink`.
- No new role token, and no colour literal in any component — the thumbnail included.
- The palette selectors must stay unanchored (`[data-palette='x']`, never `html[data-palette='x']`): anchoring them blanks every thumbnail while leaving the app itself correct.
- Frontend commands run from `src/frontend`. House rules (repo CLAUDE.md): no comment where the code is obvious, three lines max otherwise; avoid duplication; think about performance.

---

### Task 1: The four stylesheets, guarded by a parity test

**Files:**
- Create: `src/frontend/src/styles/theme-forest.css`, `theme-slate.css`, `theme-plum.css`, `theme-ink.css`
- Create: `src/frontend/src/styles/palettes.test.ts`
- Modify: `src/frontend/src/main.tsx`

**Interfaces:**
- Produces: four `[data-palette='<id>']` / `[data-palette='<id>'][data-theme='dark']` rule pairs, loaded by `main.tsx`. Later tasks rely on the ids `forest`, `slate`, `plum`, `ink` existing as stylesheets.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/styles/palettes.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

const STYLES = join(process.cwd(), 'src/styles')

const files = readdirSync(STYLES).filter(f => /^theme-.+\.css$/.test(f))
const idOf = (file: string) => file.slice('theme-'.length, -'.css'.length)

/** The ` {` is what keeps the light selector from also matching the dark one, which starts with it. */
function tokensIn(css: string, selector: string): string[] {
  const at = css.indexOf(`${selector} {`)
  if (at < 0) return []
  const body = css.slice(at, css.indexOf('}', at))

  return [...body.matchAll(/(--[\w-]+)\s*:/g)].map(m => m[1]).sort()
}

function blocks(file: string) {
  const css = readFileSync(join(STYLES, file), 'utf8')
  const id = idOf(file)

  return {
    light: tokensIn(css, `[data-palette='${id}']`),
    dark: tokensIn(css, `[data-palette='${id}'][data-theme='dark']`),
  }
}

const reference = blocks('theme-night.css')

describe('the palette stylesheets', () => {
  it('ships every palette the picker offers', () => {
    expect(files.map(idOf).sort()).toEqual(['classic', 'forest', 'ink', 'night', 'plum', 'slate'])
  })

  // A role missing from a palette falls back to whatever the cascade holds — a browser default
  // for --quote-text, the light value for --list-row-selected-bg in dark mode. Neither throws,
  // neither shows up in review, and both look like a rendering fault to the user.
  it.each(files)('%s declares every role in its light block', file => {
    expect(blocks(file).light).toEqual(reference.light)
  })

  // classic's dark block is the one deliberate gap: it omits --danger, --danger-hover and
  // --success, inheriting them from its own light block. Recorded rather than hidden.
  const CLASSIC_INHERITS = ['--danger', '--danger-hover', '--success']

  it.each(files)('%s declares every role in its dark block', file => {
    const expected = idOf(file) === 'classic'
      ? reference.dark.filter(t => !CLASSIC_INHERITS.includes(t))
      : reference.dark

    expect(blocks(file).dark).toEqual(expected)
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run from `src/frontend`: `npm run test -- src/styles/palettes.test.ts`

Expected: FAIL. `'ships every palette the picker offers'` reports only `['classic', 'night']`, and the two `it.each` families run over two files instead of six.

- [ ] **Step 3: Create the four stylesheets**

Create one file per palette, each holding exactly the two CSS blocks the spec gives for that id, **copied verbatim** from `docs/superpowers/specs/2026-07-21-webmail-palettes-design.md`:

- `src/styles/theme-forest.css` ← the spec's **Forest & amber** blocks
- `src/styles/theme-slate.css` ← the spec's **Slate & teal** blocks
- `src/styles/theme-plum.css` ← the spec's **Plum & gold** blocks
- `src/styles/theme-ink.css` ← the spec's **Ink** blocks

Head each file with a one-line comment naming the palette, the way `theme-night.css` does:

```css
/* Palette "forest" — Forest & amber. */
```

`theme-forest.css` and `theme-plum.css` additionally carry, above their dark block, the hue-shift note (this is the same warning `theme-night.css` carries; without it the next reader "fixes" the inconsistency):

```css
/* Deliberate: --action-primary shifts hue between modes (evergreen in light,
   amber in dark) — evergreen would dissolve into a dark background. The ROLE
   is stable, the hue is not. Do not "fix" this. */
```

and for plum:

```css
/* Deliberate: --action-primary shifts hue between modes (aubergine in light,
   gold in dark) — aubergine would dissolve into a dark background. The ROLE
   is stable, the hue is not. Do not "fix" this. */
```

- [ ] **Step 4: Import them**

In `src/main.tsx`, after the two existing theme imports:

```tsx
import './styles/theme-forest.css'
import './styles/theme-slate.css'
import './styles/theme-plum.css'
import './styles/theme-ink.css'
```

- [ ] **Step 5: Run the test to verify it passes**

Run from `src/frontend`: `npm run test -- src/styles/palettes.test.ts`

Expected: PASS — 1 + 6 + 6 = 13 assertions green.

Then prove the parity test bites: delete the `--quote-text` line from `theme-ink.css`'s light block, re-run, confirm `theme-ink.css declares every role in its light block` FAILS, and restore the line.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/styles/theme-forest.css src/frontend/src/styles/theme-slate.css src/frontend/src/styles/theme-plum.css src/frontend/src/styles/theme-ink.css src/frontend/src/styles/palettes.test.ts src/frontend/src/main.tsx
git commit -m "Add the forest, slate, plum and ink palettes"
```

---

### Task 2: The palette list the app validates against

**Files:**
- Modify: `src/frontend/src/contexts/ThemeContext.tsx`
- Test: `src/frontend/src/contexts/ThemeContext.test.tsx`

**Interfaces:**
- Consumes: the stylesheets from Task 1.
- Produces: `export const PALETTE_IDS = ['night', 'classic', 'forest', 'slate', 'plum', 'ink'] as const`, and `Palette` derived from it as `typeof PALETTE_IDS[number]`. Tasks 3 and 4 both import `PALETTE_IDS`.

- [ ] **Step 1: Write the failing tests**

In `ThemeContext.test.tsx`, add:

```tsx
  it.each(['forest', 'slate', 'plum', 'ink'] as const)('stores and applies %s', id => {
    localStorage.setItem('appearance_palette', id)

    render(<ThemeProvider><Probe /></ThemeProvider>)

    expect(document.documentElement.getAttribute('data-palette')).toBe(id)
  })

  // The validation used to be a two-way comparison, so every id but one fell back to night.
  // Classic is the one that already worked: it has to survive the rewrite.
  it('still honours classic', () => {
    localStorage.setItem('appearance_palette', 'classic')

    render(<ThemeProvider><Probe /></ThemeProvider>)

    expect(document.documentElement.getAttribute('data-palette')).toBe('classic')
  })

  it('falls back to night for a value it does not know', () => {
    localStorage.setItem('appearance_palette', 'chartreuse')

    render(<ThemeProvider><Probe /></ThemeProvider>)

    expect(document.documentElement.getAttribute('data-palette')).toBe('night')
  })
```

`Probe` is the component the file already defines and renders inside `ThemeProvider`; it exposes the
palette both as `data-testid="palette"` and on `<html>`. Assert on the attribute, as above — that is
what the pre-paint script and the stylesheets actually key off.

- [ ] **Step 2: Run the tests to verify they fail**

Run from `src/frontend`: `npm run test -- src/contexts/ThemeContext.test.tsx`

Expected: the four `it.each` cases FAIL, each reporting `"night"` instead of the id — the current `readPalette` maps everything but `classic` to `night`. `'still honours classic'` and the fallback test PASS already.

- [ ] **Step 3: Widen the type and the validation**

In `ThemeContext.tsx`, replace the `Palette` type declaration:

```tsx
export const PALETTE_IDS = ['night', 'classic', 'forest', 'slate', 'plum', 'ink'] as const
export type Palette = typeof PALETTE_IDS[number]
```

and replace `readPalette`:

```tsx
function readPalette(): Palette {
  const stored = localStorage.getItem(PALETTE_KEY)
  return PALETTE_IDS.includes(stored as Palette) ? stored as Palette : 'night'
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run from `src/frontend`: `npm run test -- src/contexts/ThemeContext.test.tsx` and `npm run typecheck`

Expected: PASS, whole file green; `tsc --noEmit` silent.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/contexts/ThemeContext.tsx src/frontend/src/contexts/ThemeContext.test.tsx
git commit -m "Validate a palette against the list, not against one name"
```

---

### Task 3: The pre-paint script, and a test that it cannot drift

**Files:**
- Modify: `src/frontend/index.html`
- Test: `src/frontend/src/styles/palettes.test.ts` (append)

**Interfaces:**
- Consumes: `PALETTE_IDS` from Task 2.
- Produces: nothing other tasks depend on.

**Why this task exists:** the inline script runs before any module loads — that is the whole point of it, avoiding a flash of the wrong palette — so it cannot import `PALETTE_IDS` and must repeat the names. Forgetting it produces a bug no test in the suite would see and no reviewer would notice: on a reload, the script rejects the stored palette, paints `night`, and React corrects it one frame later. A flash, on first load, for one palette.

- [ ] **Step 1: Write the failing test**

Append to `src/styles/palettes.test.ts`:

```ts
// The inline script cannot import PALETTE_IDS — it runs before any module loads. The names are
// therefore written twice, and this is what stops the two copies from drifting.
describe('the pre-paint script in index.html', () => {
  it('accepts exactly the palettes the module knows', () => {
    const html = readFileSync(join(process.cwd(), 'index.html'), 'utf8')
    const list = html.match(/\[([^\]]*)\]\.indexOf\(p\)/)

    expect(list, 'no palette list found in the pre-paint script').not.toBeNull()
    const names = [...list![1].matchAll(/'([^']+)'/g)].map(m => m[1])
    expect(names.sort()).toEqual([...PALETTE_IDS].sort())
  })
})
```

and add the import at the top of the file:

```ts
import { PALETTE_IDS } from '../contexts/ThemeContext'
```

- [ ] **Step 2: Run the test to verify it fails**

Run from `src/frontend`: `npm run test -- src/styles/palettes.test.ts`

Expected: FAIL — `no palette list found in the pre-paint script`, since the script still uses `if(p!=='night'&&p!=='classic')`.

- [ ] **Step 3: Rewrite the check**

In `index.html`, replace this line:

```html
        if(p!=='night'&&p!=='classic')p='night';
```

with:

```html
        if(['night','classic','forest','slate','plum','ink'].indexOf(p)<0)p='night';
```

- [ ] **Step 4: Run the test to verify it passes**

Run from `src/frontend`: `npm run test -- src/styles/palettes.test.ts`

Expected: PASS.

Then prove it bites: delete `,'ink'` from the array in `index.html`, re-run, confirm the test FAILS naming the difference, and restore it.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/index.html src/frontend/src/styles/palettes.test.ts
git commit -m "Teach the pre-paint script every palette"
```

---

### Task 4: The picker, with a thumbnail per palette

**Files:**
- Modify: `src/frontend/src/modules/settings/appearance/AppearancePage.tsx`
- Modify: `src/frontend/src/styles/shell.css`
- Test: `src/frontend/src/modules/settings/appearance/AppearancePage.test.tsx`

**Interfaces:**
- Consumes: `PALETTE_IDS` and `Palette` from Task 2; the stylesheets from Task 1.
- Produces: nothing other tasks depend on.

- [ ] **Step 1: Write the failing tests**

In `AppearancePage.test.tsx`, add:

```tsx
  it('offers every palette the app knows, in order', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    const radios = screen.getAllByRole('radio', { name: /Night|Classic|Forest|Slate|Plum|Ink/ })
    expect(radios.map(r => (r as HTMLInputElement).value))
      .toEqual(['night', 'classic', 'forest', 'slate', 'plum', 'ink'])
    expect(new Set(radios.map(r => (r as HTMLInputElement).value)))
      .toEqual(new Set(PALETTE_IDS))
  })

  it('changes to a new palette', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    fireEvent.click(screen.getByLabelText('Plum & gold'))

    expect(document.documentElement.getAttribute('data-palette')).toBe('plum')
    expect(localStorage.getItem('appearance_palette')).toBe('plum')
  })

  // Each thumbnail declares the palette it advertises, which is the only thing standing between
  // six previews and six copies of the active one.
  it('previews each palette in its own colours', () => {
    const { container } = render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    const previews = Array.from(container.querySelectorAll('.palette-preview'))
    expect(previews.map(p => p.getAttribute('data-palette')))
      .toEqual(['night', 'classic', 'forest', 'slate', 'plum', 'ink'])
  })

  // The stored preference may be "system", which names no mode: the preview has to show the
  // mode the user is actually in, so it reads the resolved value.
  it('previews in the resolved theme, not the stored preference', () => {
    localStorage.setItem('appearance_theme', 'dark')

    const { container } = render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    Array.from(container.querySelectorAll('.palette-preview'))
      .forEach(p => expect(p.getAttribute('data-theme')).toBe('dark'))
  })

  // The label already names the palette; a screen reader has no use for a picture of colours.
  it('hides the thumbnails from assistive technology', () => {
    const { container } = render(<ThemeProvider><AppearancePage /></ThemeProvider>)

    Array.from(container.querySelectorAll('.palette-preview'))
      .forEach(p => expect(p).toHaveAttribute('aria-hidden', 'true'))
  })
```

and extend the file's imports:

```tsx
import { ThemeProvider, PALETTE_IDS } from '../../../contexts/ThemeContext'
```

- [ ] **Step 2: Run the tests to verify they fail**

Run from `src/frontend`: `npm run test -- src/modules/settings/appearance/AppearancePage.test.tsx`

Expected: FAIL — the order test reports only `['night', 'classic']`, `Plum & gold` has no label, and `.palette-preview` matches nothing.

- [ ] **Step 3: Add the thumbnail and the grid**

In `AppearancePage.tsx`, replace the `PALETTES` constant:

```tsx
const PALETTES: { value: Palette; label: string }[] = [
  { value: 'night', label: 'Night & coral (default)' },
  { value: 'classic', label: 'Classic' },
  { value: 'forest', label: 'Forest & amber' },
  { value: 'slate', label: 'Slate & teal' },
  { value: 'plum', label: 'Plum & gold' },
  { value: 'ink', label: 'Ink' },
]
```

Add the thumbnail above the component:

```tsx
/** Renders in the palette it advertises rather than the active one: the palette selectors are
    attribute-based and unanchored to <html>, so stamping both attributes here re-declares all
    33 tokens on this subtree. */
function PalettePreview({ value, dark }: { value: Palette; dark: boolean }) {
  return (
    <span
      className="palette-preview"
      data-palette={value}
      data-theme={dark ? 'dark' : 'light'}
      aria-hidden="true"
    >
      <span className="pp-bar" />
      <span className="pp-body">
        <span className="pp-rail" />
        <span className="pp-rows">
          <span className="pp-row is-unread" />
          <span className="pp-row" />
          <span className="pp-row" />
        </span>
      </span>
    </span>
  )
}
```

Pull `isDark` out of the hook — the line currently reads `const { theme, setTheme, palette, setPalette } = useTheme()`:

```tsx
  const { theme, setTheme, palette, setPalette, isDark } = useTheme()
```

and replace the palette `<section>`'s body:

```tsx
        <div className="palette-grid">
          {PALETTES.map(({ value, label }) => (
            <label key={value} className="palette-card">
              <input
                type="radio"
                name="palette"
                value={value}
                checked={palette === value}
                onChange={() => setPalette(value)}
              />
              <PalettePreview value={value} dark={isDark} />
              {label}
            </label>
          ))}
        </div>
```

- [ ] **Step 4: Style them**

Append to `src/styles/shell.css`, after the `.radio-row` rule:

```css
/* The palette blocks are attribute selectors, deliberately not anchored to <html>: stamping
   data-palette and data-theme on the thumbnail re-declares all 33 tokens inside it, so a
   preview costs no colour of its own. Anchoring those selectors blanks every thumbnail. */
.palette-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(190px, 1fr));
  gap: 8px;
  max-width: 560px;
}
.palette-card {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 13px;
}
.palette-card:hover { background: var(--pane-item-hover); }
.palette-card:has(input:checked) { border-color: var(--action-primary); }

.palette-preview {
  width: 62px;
  height: 40px;
  flex: none;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--bg);
}
.palette-preview .pp-bar { height: 9px; background: var(--topbar-bg); }
.palette-preview .pp-body { flex: 1; display: flex; min-height: 0; }
.palette-preview .pp-rail { width: 10px; background: var(--rail-bg); }
.palette-preview .pp-rows {
  flex: 1;
  background: var(--surface);
  padding: 3px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}
.palette-preview .pp-row { height: 5px; border-radius: 1px; background: var(--list-separator); }
.palette-preview .pp-row.is-unread {
  background: var(--list-row-selected-bg);
  border-left: 2px solid var(--accent-unread);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run from `src/frontend`: `npm run test -- src/modules/settings/appearance/AppearancePage.test.tsx`

Expected: PASS, whole file green — including the three tests that were there before.

Then prove the preview test bites: change `data-palette={value}` to `data-palette={palette}` in `PalettePreview`, so every thumbnail renders the *active* palette, re-run, confirm `'previews each palette in its own colours'` FAILS, and restore.

- [ ] **Step 6: Run the whole suite, the typecheck and the lint**

Run from `src/frontend`: `npm run test && npm run typecheck && npm run lint`

Expected: every file green, `tsc --noEmit` silent, no new lint error.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/settings/appearance/AppearancePage.tsx src/frontend/src/modules/settings/appearance/AppearancePage.test.tsx src/frontend/src/styles/shell.css
git commit -m "Show each palette as a thumbnail in Appearance"
```

---

### Task 5: Record what a later reader would otherwise undo

**Files:**
- Modify: `src/frontend/CLAUDE.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Rewrite the Theming section's palette paragraph**

In `src/frontend/CLAUDE.md`, in the **Theming** section, replace this bullet:

> - `theme-night.css` / `theme-classic.css` — the two **palettes**, each defining the actual color values for `[data-palette='night']` / `[data-palette='classic']`, further overridden by `[data-palette='X'][data-theme='dark']` for the dark variant. Four total combinations: night×light, night×dark, classic×light, classic×dark.

with:

> - `theme-<id>.css` — one file per **palette** (`night`, `classic`, `forest`, `slate`, `plum`, `ink`), each defining the 33 role tokens for `[data-palette='<id>']`, further overridden by `[data-palette='<id>'][data-theme='dark']`. Twelve combinations. **The selectors are deliberately not anchored to `html`**: an attribute selector matches any element, which is what lets the Appearance page's thumbnails re-declare a whole palette on one `<span>` and preview a palette that is not the active one. Anchoring them to `html[data-palette='…']` would blank every thumbnail while leaving the app itself correct. `src/styles/palettes.test.ts` asserts every palette file declares the same role set as `theme-night.css`, in both blocks — a missing `--quote-text` is a browser default nobody notices in review, and a missing `--list-row-selected-bg` silently falls back to the light value in dark mode. `classic`'s dark block is the one recorded exception: it inherits `--danger`, `--danger-hover` and `--success` from its own light block.

- [ ] **Step 2: Extend the pre-paint paragraph**

In the same section, replace the closing sentence of the paragraph about the blocking inline script:

> it duplicates the resolution logic that `ThemeContext` also runs, deliberately, since the context only mounts after React hydrates.

with:

> it duplicates the resolution logic that `ThemeContext` also runs, deliberately, since the context only mounts after React hydrates. **It therefore also repeats the palette names**, which it cannot import — `PALETTE_IDS` (`ThemeContext.tsx`) is the module-side list, and `palettes.test.ts` asserts the two agree. Forgetting the script half produces a bug no other test sees and no reviewer notices: on reload the script rejects the stored palette, paints `night`, and React corrects it a frame later — a flash, on first load, for one palette.

- [ ] **Step 3: Note the two hue shifts**

Replace the **Night `--action-primary` hue-shift** paragraph's closing sentence:

> Do not "fix" this into a single fixed color.

with:

> Do not "fix" this into a single fixed color. `forest` and `plum` do the same for the same reason — evergreen and aubergine both dissolve into a dark ground, so dark mode promotes their amber and gold to `--action-primary`.

- [ ] **Step 4: Commit**

```bash
git add src/frontend/CLAUDE.md
git commit -m "Document the six palettes and the unanchored selectors"
```
