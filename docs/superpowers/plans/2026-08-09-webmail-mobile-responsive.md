# Webmail Mobile & Tablet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the whole webmail — login, shell, mail, composer, contacts, settings — usable and good-looking on phones and tablets, with one component tree serving every width.

**Architecture:** The desktop declarations stay the base rules with no media query around them; two `max-width` blocks (`1023px`, then `639px`) override them through the ordinary cascade, co-located in the stylesheet they override. A single `useViewport()` hook decides *what mounts* — which pane, whether the splitter exists, whether the drawer traps focus — and never decides a width. The mail module's existing `readingPane: 'none'` machinery is what a phone reuses for list ↔ reader.

**Tech Stack:** React 19 + Vite, TypeScript, react-router-dom, TanStack Query, i18next (en/fr), Vitest + Testing Library (jsdom), plain CSS with role tokens.

**Spec:** `docs/superpowers/specs/2026-08-09-webmail-mobile-responsive-design.md`

## Global Constraints

- Working directory for every command in this plan is `src/frontend`.
- Three tiers: **desktop ≥ 1024px**, **tablet 640–1023px**, **phone < 640px**. The only two media widths allowed anywhere are `@media (max-width: 1023px)` and `@media (max-width: 639px)`.
- **Never wrap an existing desktop rule in a media query.** Desktop stays the unqualified base. `@media (min-width: …)` is forbidden project-wide and `styles/responsive.test.ts` fails the build on one.
- Media queries live in the file whose rules they override — `shell.css`, `mail.css`, `index.css`, `modal.css`. There is no central `responsive.css`.
- No colour literal anywhere. Every colour goes through a role token from `styles/tokens.css` / the theme files.
- `useViewport()` decides what mounts, never how wide anything is. No component may compute a pixel width in JavaScript.
- Do not add `viewport-fit=cover` to `index.html`, and do not introduce `env(safe-area-inset-*)`.
- Every user-visible string goes in `src/locales/en/*.json` **and** `src/locales/fr/*.json`. English is the byte-identity baseline: a red assertion on visible English text means the catalogue is wrong, never the test.
- Touch targets are floored at 44px through the `--touch` token, declared only inside the phone block.
- Tests: `npm test` (vitest, jsdom). jsdom computes no layout — never assert a pixel there. Geometry is verified only in `probes/mobile-layout.html` under Chrome device emulation.
- `dotnet` is not involved. Do not touch `src/snoopy.microservice`.
- Commit messages: two lines maximum, and never begin or end with `@`.

---

### Task 1: `useViewport` and the test helpers

**Files:**
- Create: `src/hooks/useViewport.ts`
- Create: `src/hooks/useViewport.test.ts`
- Modify: `src/test-utils.ts`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `type Viewport = 'phone' | 'tablet' | 'desktop'` and `useViewport(): Viewport` from `src/hooks/useViewport.ts`
  - `mockViewport(tier: Viewport): void`, `changeViewport(tier: Viewport): Promise<void>` and `resetViewport(): void` from `src/test-utils.ts`

- [ ] **Step 1: Add the test helpers to `src/test-utils.ts`**

Append to the existing file (keep `settle` as it is):

```ts
import { act } from '@testing-library/react'

export type Viewport = 'phone' | 'tablet' | 'desktop'

const VIEWPORT_WIDTH: Record<Viewport, number> = { phone: 360, tablet: 768, desktop: 1280 }

// jsdom answers no media query on its own and test-setup.js stubs every one to matches:false,
// which is what keeps the whole existing suite on the desktop layout. These helpers replace that
// stub for one file at a time; resetViewport puts the original back.
const original = window.matchMedia
const listeners = new Set<() => void>()
let width = VIEWPORT_WIDTH.desktop
let installed = false

/** Puts the environment in one tier. Call before rendering. */
export function mockViewport(tier: Viewport) {
  width = VIEWPORT_WIDTH[tier]
  if (installed) return
  installed = true
  window.matchMedia = ((query: string) => {
    const limit = Number(/max-width:\s*(\d+)px/.exec(query)?.[1] ?? NaN)
    return {
      // A getter, not a value: the same MediaQueryList object is read again after a tier change.
      get matches() { return Number.isNaN(limit) ? false : width <= limit },
      media: query,
      addEventListener: (_event: string, fn: () => void) => { listeners.add(fn) },
      removeEventListener: (_event: string, fn: () => void) => { listeners.delete(fn) },
    } as unknown as MediaQueryList
  }) as typeof window.matchMedia
}

/** Changes tier after a render — a rotation — and lets the subscribers react. */
export async function changeViewport(tier: Viewport) {
  mockViewport(tier)
  await act(async () => { listeners.forEach(fn => fn()) })
}

/** Restores the suite-wide stub. Call in afterEach of any file using the two above. */
export function resetViewport() {
  window.matchMedia = original
  listeners.clear()
  installed = false
  width = VIEWPORT_WIDTH.desktop
}
```

- [ ] **Step 2: Write the failing test**

Create `src/hooks/useViewport.test.ts`:

```ts
import { afterEach, describe, expect, it } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useViewport } from './useViewport'
import { changeViewport, mockViewport, resetViewport } from '../test-utils'

afterEach(resetViewport)

describe('useViewport', () => {
  it('reads phone below 640px', () => {
    mockViewport('phone')
    expect(renderHook(() => useViewport()).result.current).toBe('phone')
  })

  it('reads tablet between 640 and 1023px', () => {
    mockViewport('tablet')
    expect(renderHook(() => useViewport()).result.current).toBe('tablet')
  })

  it('reads desktop at 1024px and above', () => {
    mockViewport('desktop')
    expect(renderHook(() => useViewport()).result.current).toBe('desktop')
  })

  it('follows a tier change', async () => {
    mockViewport('desktop')
    const { result } = renderHook(() => useViewport())
    await changeViewport('phone')
    expect(result.current).toBe('phone')
  })

  it('falls back to desktop without matchMedia', () => {
    const saved = window.matchMedia
    // @ts-expect-error deliberately removing the API an old browser may not have
    delete window.matchMedia
    expect(renderHook(() => useViewport()).result.current).toBe('desktop')
    window.matchMedia = saved
  })

  it('unsubscribes on unmount', () => {
    mockViewport('phone')
    const { unmount } = renderHook(() => useViewport())
    unmount()
    // A listener left behind would setState on an unmounted hook at the next change.
    expect(() => changeViewport('desktop')).not.toThrow()
  })
})
```

- [ ] **Step 3: Run it and watch it fail**

Run: `npm test -- src/hooks/useViewport.test.ts`
Expected: FAIL — `Failed to resolve import "./useViewport"`.

- [ ] **Step 4: Write `src/hooks/useViewport.ts`**

```ts
import { useEffect, useState } from 'react'

export type Viewport = 'phone' | 'tablet' | 'desktop'

// The one place these two widths exist in JavaScript. They mirror the stylesheets' only two
// media widths; changing one without the other splits the layout from the mounting decision.
const PHONE = '(max-width: 639px)'
const TABLET = '(max-width: 1023px)'

function read(): Viewport {
  if (typeof window.matchMedia !== 'function') return 'desktop'
  if (window.matchMedia(PHONE).matches) return 'phone'
  if (window.matchMedia(TABLET).matches) return 'tablet'
  return 'desktop'
}

/**
 * Which tier the viewport is in. It decides what MOUNTS — which pane, whether the splitter
 * exists, whether the drawer traps focus — and never how wide anything is: a width computed
 * here would be a second source of truth beside the stylesheet, and the two would drift.
 *
 * Without matchMedia it answers 'desktop', which is the layout that exists today rather than
 * a blank screen.
 */
export function useViewport(): Viewport {
  const [viewport, setViewport] = useState(read)

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return
    const queries = [window.matchMedia(PHONE), window.matchMedia(TABLET)]
    const apply = () => setViewport(read())
    queries.forEach(query => query.addEventListener('change', apply))
    // The first read happened during useState; this catches a change between render and effect.
    apply()
    return () => queries.forEach(query => query.removeEventListener('change', apply))
  }, [])

  return viewport
}
```

- [ ] **Step 5: Run the tests**

Run: `npm test -- src/hooks/useViewport.test.ts`
Expected: PASS, 6 tests.

- [ ] **Step 6: Prove the existing suite is untouched**

Run: `npm test`
Expected: PASS — every existing test still resolves to the desktop layout, because `test-setup.js` answers `matches: false` to every query.

- [ ] **Step 7: Commit**

```bash
git add src/hooks/useViewport.ts src/hooks/useViewport.test.ts src/test-utils.ts
git commit -m "feat: add useViewport tier hook and its test helpers"
```

---

### Task 2: CSS foundations and the probe harness

**Files:**
- Create: `src/styles/responsive.test.ts`
- Create: `probes/mobile-layout.html`
- Modify: `src/styles/shell.css` (delete `min-width: 1024px`, `100dvh`, `--touch`, `overscroll-behavior-y`)
- Modify: `src/index.css` (`100dvh` on the two full-height roots, 16px form controls under 640px)

**Interfaces:**
- Consumes: nothing.
- Produces: the `--touch` custom property, declared in the phone block of `shell.css` and readable by every later task; `probes/mobile-layout.html`, which later tasks add sections to.

- [ ] **Step 1: Write the failing stylesheet contract test**

Create `src/styles/responsive.test.ts`:

```ts
import { describe, it, expect } from 'vitest'

// ?raw on the stylesheets, the mechanism modals.test.ts uses on the components.
const sheets = import.meta.glob('./*.css', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>
const root = import.meta.glob('../index.css', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>
const all = { ...sheets, ...root }

function widthsUsedBy(query: RegExp): string[] {
  return Object.entries(all).flatMap(([path, css]) =>
    [...css.matchAll(query)].map(match => `${path}: ${match[0]}`))
}

describe('responsive contract', () => {
  it('reads the stylesheets, not an empty glob', () => {
    expect(Object.keys(all).length).toBeGreaterThan(5)
  })

  it('holds no desktop floor', () => {
    expect(all['./shell.css']).not.toMatch(/min-width:\s*1024px/)
  })

  // Desktop stays the unqualified base rule. A min-width query means somebody inverted the
  // cascade, and every desktop rule now has to be read through a filter.
  it('uses no min-width media query', () => {
    expect(widthsUsedBy(/@media[^{]*min-width[^{]*/g)).toEqual([])
  })

  // Exactly two breakpoints, spelled one way each. Scoped to @media on purpose: @container
  // queries carry their own widths and answer to the column they measure, not to the window.
  it('uses only the two agreed breakpoint widths', () => {
    const widths = widthsUsedBy(/@media[^{]*max-width:\s*\d+px/g)
      .map(entry => entry.replace(/.*max-width:\s*/, ''))
    expect([...new Set(widths)].sort()).toEqual(['1023px', '639px'])
  })

  it('sizes the full-height roots in dvh', () => {
    expect(all['./shell.css']).toMatch(/height:\s*100dvh/)
    expect(all['../index.css']).toMatch(/min-height:\s*100dvh/)
  })

  it('declares the touch floor once, in the phone block', () => {
    expect([...all['./shell.css'].matchAll(/--touch:/g)]).toHaveLength(1)
  })
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test -- src/styles/responsive.test.ts`
Expected: FAIL on "holds no desktop floor", "uses only the two agreed breakpoint widths", "sizes the full-height roots in dvh" and "declares the touch floor once".

- [ ] **Step 3: Edit `src/styles/shell.css` — the shell root**

Replace the `.app-shell` rule at the top of the file:

```css
.app-shell {
  display: flex;
  flex-direction: column;
  height: 100vh;
  /* dvh follows the mobile URL bar; the vh line above is the fallback for a browser without it.
     With 100vh alone the bottom band — the tab bar — sits under the URL bar and off screen. */
  height: 100dvh;
  /* Chrome for Android's own pull-to-refresh fires on an overscroll at the top of the page.
     The page never scrolls here, but the gesture still reaches it, and it would run over the
     list's own pull-to-refresh. */
  overscroll-behavior-y: contain;
}
```

Note that `min-width: 1024px` and its comment are gone — that is the line that forbade everything else.

- [ ] **Step 4: Add the two breakpoint blocks at the foot of `src/styles/shell.css`**

```css
/* ── Narrow tiers ─────────────────────────────────────────────────────────────
   Desktop is the unqualified base above. These two blocks only override it, the
   second refining the first through the ordinary cascade. Never invert this:
   wrapping the desktop rules in a min-width query makes the whole file conditional. */

@media (max-width: 1023px) {
  /* The context pane is a drawer from here down, so no module shows one inline. */
  .app-content { margin: 0; border-radius: 0; }
}

@media (max-width: 639px) {
  :root { --touch: 44px; }

  /* Brand only since the account block moved to the foot of the folder column: 44px of pure
     decoration out of a 640px-tall viewport, returned to the message list. The brand stays
     visible at the head of the drawer. */
  .app-topbar { display: none; }

  .rail-item { min-height: var(--touch); }
  .dropdown-item { min-height: var(--touch); }
  .pane-item { min-height: var(--touch); }
}
```

- [ ] **Step 5: Edit `src/index.css` — dvh and the iOS zoom floor**

Change `body`'s `min-height: 100vh` to keep the fallback pair:

```css
body {
  font-family: var(--font);
  background: var(--bg);
  color: var(--text);
  font-size: 14px;
  line-height: 1.5;
  min-height: 100vh;
  min-height: 100dvh;
}
```

Do the same for `.page-center`'s `min-height`. Then append at the foot of `src/index.css`:

```css
@media (max-width: 639px) {
  /* iOS zooms the page in when a focused field's text is under 16px, and never zooms back out.
     16px on the controls, not on body: the interface keeps its 14px scale. */
  input, select, textarea { font-size: 16px; }
}
```

- [ ] **Step 6: Run the contract test**

Run: `npm test -- src/styles/responsive.test.ts`
Expected: PASS, 6 tests.

- [ ] **Step 7: Create the probe harness `probes/mobile-layout.html`**

```html
<!doctype html>
<!--
  Probe: does anything overflow, and is anything too small to hit, below 1024px?

  Serving the real app was not an option: the frontend talks to https://api.mail.weesky.net and
  the dev API carries no `localhost` origin, so a locally served build cannot get past the login
  screen. This page links the REAL stylesheets and restates only the markup, so a CSS edit changes
  what is measured. It needs no session because it needs no data.

  Served by the Vite dev server (npm run dev) at /probes/mobile-layout.html. Drive it in Chrome
  under device emulation at 360x640, 390x844, 768x1024 and 1024x768. #out holds the JSON and
  document.title becomes 'probe-done' once every case has been measured.

  Reading the numbers:
  - `overflow` is documentElement.scrollWidth - clientWidth. Anything above 0 is a horizontal
    scrollbar on the page, which is the defect this whole plan exists to remove.
  - `smallest` is the shortest measured touch target in the section, in CSS pixels. Floor is 44.
-->
<html data-palette="night" data-theme="light">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <!-- main.tsx's order, exactly: the cascade decides several of these boxes. -->
  <link rel="stylesheet" href="/src/styles/tokens.css" />
  <link rel="stylesheet" href="/src/styles/theme-night.css" />
  <link rel="stylesheet" href="/src/index.css" />
  <link rel="stylesheet" href="/src/styles/modal.css" />
  <link rel="stylesheet" href="/src/styles/shell.css" />
  <link rel="stylesheet" href="/src/styles/tooltip.css" />
  <link rel="stylesheet" href="/src/styles/mail.css" />
  <style>
    #out { padding: 12px; white-space: pre; font-family: monospace; font-size: 11px;
           background: #fff; color: #111; }
    .case { margin-bottom: 8px; }
    .case > h2 { font: 700 11px monospace; padding: 2px 6px; background: #333; color: #fff; }
  </style>
</head>
<body>
  <pre id="out">measuring…</pre>
  <div id="stage"></div>
  <script type="module">
    const TOUCH_FLOOR = 44
    // Later tasks push their own {name, html} entries onto this array. One section per screen.
    const CASES = []

    function measure() {
      const stage = document.getElementById('stage')
      const report = CASES.map(({ name, html, touch }) => {
        stage.innerHTML = `<div class="case"><h2>${name}</h2>${html}</div>`
        const doc = document.documentElement
        const targets = touch ? [...stage.querySelectorAll(touch)] : []
        const heights = targets.map(el => Math.round(el.getBoundingClientRect().height))
        return {
          name,
          viewport: `${window.innerWidth}x${window.innerHeight}`,
          overflow: doc.scrollWidth - doc.clientWidth,
          smallest: heights.length ? Math.min(...heights) : null,
          undersized: heights.filter(h => h < TOUCH_FLOOR).length,
        }
      })
      stage.innerHTML = ''
      document.getElementById('out').textContent = JSON.stringify(report, null, 1)
      document.title = 'probe-done'
    }

    window.addEventListener('resize', measure)
    measure()
  </script>
</body>
</html>
```

- [ ] **Step 8: Run the whole suite**

Run: `npm test`
Expected: PASS. Nothing but stylesheet text changed, and no test asserts on layout.

- [ ] **Step 9: Type-check**

Run: `npm run typecheck`
Expected: no errors.

- [ ] **Step 10: Commit**

```bash
git add src/styles/shell.css src/styles/responsive.test.ts src/index.css probes/mobile-layout.html
git commit -m "feat: drop the 1024px desktop floor and add the responsive foundations"
```

---

### Task 3: `ContextDrawer`

**Files:**
- Create: `src/layouts/ContextDrawer.tsx`
- Create: `src/layouts/ContextDrawer.test.tsx`
- Create: `src/icons/MenuIcon.tsx`
- Modify: `src/styles/shell.css` (drawer styles, inside the 1023px block)
- Modify: `src/locales/en/common.json`, `src/locales/fr/common.json`

**Interfaces:**
- Consumes: `useViewport` from Task 1.
- Produces, all from `src/layouts/ContextDrawer.tsx`:
  - `default function ContextDrawer({ open, onClose, children }: { open: boolean; onClose: () => void; children: ReactNode })`
  - `export function DrawerToggle({ onClick }: { onClick: () => void })`
  - `export function useContextDrawer(): { inDrawer: boolean; open: boolean; toggle: () => void; close: () => void }`

- [ ] **Step 1: Add the strings**

In `src/locales/en/common.json`, add a top-level `drawer` block beside `rail`:

```json
"drawer": {
  "label": "Navigation",
  "open": "Open navigation",
  "close": "Close navigation"
}
```

In `src/locales/fr/common.json`, the same block:

```json
"drawer": {
  "label": "Navigation",
  "open": "Ouvrir la navigation",
  "close": "Fermer la navigation"
}
```

- [ ] **Step 2: Write the failing test**

Create `src/layouts/ContextDrawer.test.tsx`:

```tsx
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, render, renderHook, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import ContextDrawer, { useContextDrawer } from './ContextDrawer'
import { changeViewport, mockViewport, resetViewport } from '../test-utils'

afterEach(resetViewport)

function drawer(open: boolean, onClose = vi.fn()) {
  return render(
    <MemoryRouter>
      <ContextDrawer open={open} onClose={onClose}>
        <button type="button">Inbox</button>
      </ContextDrawer>
    </MemoryRouter>,
  )
}

describe('ContextDrawer', () => {
  it('keeps its children mounted while closed', () => {
    drawer(false)
    // Mounted, not merely present: the folder tree's expand state and its query live in here.
    expect(screen.getByRole('button', { name: 'Inbox' })).toBeTruthy()
  })

  it('marks the open panel as a modal dialog', () => {
    drawer(true)
    const panel = screen.getByRole('dialog')
    expect(panel.getAttribute('aria-modal')).toBe('true')
  })

  it('closes on Escape', async () => {
    const onClose = vi.fn()
    drawer(true, onClose)
    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalled()
  })

  it('closes on a scrim click', async () => {
    const onClose = vi.fn()
    const { container } = drawer(true, onClose)
    await userEvent.click(container.querySelector('.context-drawer-scrim')!)
    expect(onClose).toHaveBeenCalled()
  })

  it('does not listen for Escape while closed', async () => {
    const onClose = vi.fn()
    drawer(false, onClose)
    await userEvent.keyboard('{Escape}')
    expect(onClose).not.toHaveBeenCalled()
  })
})

describe('useContextDrawer', () => {
  it('puts the pane in a drawer below 1024px', () => {
    mockViewport('tablet')
    expect(renderHook(() => useContextDrawer()).result.current.inDrawer).toBe(true)
  })

  it('leaves the pane inline on desktop', () => {
    mockViewport('desktop')
    expect(renderHook(() => useContextDrawer()).result.current.inDrawer).toBe(false)
  })

  it('closes when the viewport grows to desktop', async () => {
    mockViewport('phone')
    const { result } = renderHook(() => useContextDrawer())
    await act(async () => result.current.toggle())
    expect(result.current.open).toBe(true)
    await changeViewport('desktop')
    // A focus trap left armed on a panel nobody can see is worse than a drawer left open.
    expect(result.current.open).toBe(false)
  })
})
```

- [ ] **Step 3: Run it and watch it fail**

Run: `npm test -- src/layouts/ContextDrawer.test.tsx`
Expected: FAIL — `Failed to resolve import "./ContextDrawer"`.

- [ ] **Step 4: Create `src/icons/MenuIcon.tsx`**

```tsx
/** Three rules. The drawer's trigger below 1024px. */
export default function MenuIcon({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 6h16M4 12h16M4 18h16" />
    </svg>
  )
}
```

- [ ] **Step 5: Create `src/layouts/ContextDrawer.tsx`**

```tsx
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation } from 'react-router-dom'
import MenuIcon from '../icons/MenuIcon'
import { useViewport } from '../hooks/useViewport'

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]),'
  + ' select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

interface Props {
  open: boolean
  onClose: () => void
  children: ReactNode
}

/**
 * The context pane below 1024px: mail's folder tree, contacts' scopes, settings' navigation.
 * One component for all three — they differ in what they hold, never in how they open.
 *
 * Closed, it is display:none rather than unmounted, so the tree keeps its expand state and its
 * query while leaving the tab order and the accessibility tree alike.
 */
export default function ContextDrawer({ open, onClose, children }: Props) {
  const { t } = useTranslation()
  const panel = useRef<HTMLDivElement>(null)
  // pathname AND search: mail names its folder in a search param, so a folder pick — the very
  // thing the drawer exists to do — moves search and leaves pathname alone.
  const { pathname, search } = useLocation()

  useEffect(() => { onClose() }, [pathname, search, onClose])

  useEffect(() => {
    if (!open) return
    function onKey(event: KeyboardEvent) {
      if (event.key === 'Escape') { onClose(); return }
      if (event.key !== 'Tab') return
      const items = panel.current?.querySelectorAll<HTMLElement>(FOCUSABLE)
      if (!items?.length) return
      const first = items[0]
      const last = items[items.length - 1]
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onClose])

  return (
    <div className={`context-drawer${open ? ' is-open' : ''}`}>
      <div className="context-drawer-scrim" onClick={onClose} />
      <div className="context-drawer-panel" ref={panel} role="dialog" aria-modal="true"
        aria-label={t('drawer.label')}>
        {children}
      </div>
    </div>
  )
}

/** The hamburger. It lives in whichever header band the module already owns. */
export function DrawerToggle({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation()
  return (
    <button type="button" className="drawer-toggle" aria-label={t('drawer.open')}
      title={t('drawer.open')} onClick={onClick}>
      <MenuIcon size={20} />
    </button>
  )
}

/** The state the three layouts share, so none of them re-derives the tier rule. */
export function useContextDrawer() {
  const inDrawer = useViewport() !== 'desktop'
  const [open, setOpen] = useState(false)
  const close = useCallback(() => setOpen(false), [])

  // Growing back to desktop must disarm it: the panel goes inline and an open flag would
  // otherwise reopen the drawer the moment the window narrows again.
  useEffect(() => { if (!inDrawer) setOpen(false) }, [inDrawer])

  return { inDrawer, open, toggle: () => setOpen(value => !value), close }
}
```

- [ ] **Step 6: Run the tests**

Run: `npm test -- src/layouts/ContextDrawer.test.tsx`
Expected: PASS, 8 tests.

- [ ] **Step 7: Add the drawer styles to `src/styles/shell.css`**

Inside the existing `@media (max-width: 1023px)` block, replacing it wholesale:

```css
@media (max-width: 1023px) {
  /* The context pane is a drawer from here down, so no module shows one inline. */
  .app-content { margin: 0; border-radius: 0; }

  /* Closed is display:none, which takes the panel out of the tab order and the accessibility
     tree in one move while React keeps its children mounted. */
  .context-drawer { display: none; }
  .context-drawer.is-open { display: block; }

  .context-drawer-scrim {
    position: fixed;
    inset: 0;
    z-index: 100;
    background: rgba(0, 0, 0, 0.35);
  }

  .context-drawer-panel {
    position: fixed;
    top: 0;
    left: 0;
    bottom: 0;
    z-index: 101;
    display: flex;
    flex-direction: column;
    width: min(320px, 86vw);
    background: var(--folders-bg);
    border-right: 1px solid var(--border);
    box-shadow: 0 0 32px rgba(0, 0, 0, 0.25);
    animation: drawer-in 0.18s ease-out;
  }

  @keyframes drawer-in { from { transform: translateX(-100%); } to { transform: none; } }
  @media (prefers-reduced-motion: reduce) {
    .context-drawer-panel { animation: none; }
  }

  /* The pane inside the drawer fills it: its own column width belongs to the desktop layout. */
  .context-drawer-panel > .mail-folders,
  .context-drawer-panel > .contacts-scopes-column,
  .context-drawer-panel > .context-pane {
    width: auto;
    flex: 1;
    min-height: 0;
    border-right: none;
  }

  .drawer-toggle {
    flex: none;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 40px;
    height: 40px;
    padding: 0;
    border: none;
    border-radius: var(--radius-sm);
    background: none;
    color: var(--text);
    cursor: pointer;
  }
  .drawer-toggle:hover { background: color-mix(in oklab, var(--text) 10%, var(--surface)); }
}

/* Desktop never draws the hamburger: the pane is right there. */
.drawer-toggle { display: none; }
```

Put that last, unqualified `.drawer-toggle { display: none }` **above** the media block, with the other base rules — a base rule after a media block still wins the cascade at every width and would hide the button everywhere.

- [ ] **Step 8: Run the suite and type-check**

Run: `npm test && npm run typecheck`
Expected: PASS, no type errors.

- [ ] **Step 9: Commit**

```bash
git add src/layouts/ContextDrawer.tsx src/layouts/ContextDrawer.test.tsx src/icons/MenuIcon.tsx src/styles/shell.css src/locales/en/common.json src/locales/fr/common.json
git commit -m "feat: add the shared context drawer for narrow viewports"
```

---

### Task 4: `modules.ts`, `BottomNav`, and the rail's phone behaviour

**Files:**
- Create: `src/layouts/modules.ts`
- Create: `src/layouts/BottomNav.tsx`
- Create: `src/layouts/BottomNav.test.tsx`
- Modify: `src/layouts/AppRail.tsx` (read `modules.ts`)
- Modify: `src/layouts/AppShell.tsx` (render `BottomNav`)
- Modify: `src/styles/shell.css`

**Interfaces:**
- Consumes: `useViewport` (Task 1).
- Produces: `MODULES: readonly { to: string; labelKey: string; Icon: (props: { size?: number }) => JSX.Element }[]` and `SETTINGS_MODULE` of the same shape, from `src/layouts/modules.ts`.

- [ ] **Step 1: Write the failing test**

Create `src/layouts/BottomNav.test.tsx`:

```tsx
import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import AppRail from './AppRail'
import BottomNav from './BottomNav'
import { MODULES, SETTINGS_MODULE } from './modules'

function names(container: HTMLElement) {
  return [...container.querySelectorAll('a')].map(a => a.getAttribute('href'))
}

describe('BottomNav', () => {
  it('offers the same destinations as the rail', () => {
    const rail = render(<MemoryRouter><AppRail /></MemoryRouter>)
    const bottom = render(<MemoryRouter><BottomNav /></MemoryRouter>)
    // Both read modules.ts. A module added to one and not the other is the bug this catches.
    expect(names(bottom.container)).toEqual(names(rail.container))
  })

  it('covers every module plus settings', () => {
    render(<MemoryRouter><BottomNav /></MemoryRouter>)
    expect(names(document.body)).toHaveLength(MODULES.length + 1)
    expect(SETTINGS_MODULE.to).toBe('/settings')
  })

  it('labels each destination in text, not only in aria', () => {
    render(<MemoryRouter><BottomNav /></MemoryRouter>)
    // A bar of bare glyphs is unreadable at 56px; the label is what makes it a tab bar.
    expect(screen.getByText('Mail')).toBeTruthy()
    expect(screen.getByText('Settings')).toBeTruthy()
  })
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test -- src/layouts/BottomNav.test.tsx`
Expected: FAIL — `Failed to resolve import "./BottomNav"`.

- [ ] **Step 3: Create `src/layouts/modules.ts`**

```ts
import CalendarIcon from '../icons/CalendarIcon'
import ContactsIcon from '../icons/ContactsIcon'
import GearIcon from '../icons/GearIcon'
import MailIcon from '../icons/MailIcon'

export interface ModuleEntry {
  to: string
  labelKey: string
  Icon: (props: { size?: number }) => React.JSX.Element
}

/** The one definition of the module set. AppRail and BottomNav both read it, or a module added
    later shows up on a desktop and vanishes on a phone. */
export const MODULES: readonly ModuleEntry[] = [
  { to: '/mail', labelKey: 'rail.mail', Icon: MailIcon },
  { to: '/calendar', labelKey: 'rail.calendar', Icon: CalendarIcon },
  { to: '/contacts', labelKey: 'rail.contacts', Icon: ContactsIcon },
]

/** Apart from the list because the rail pushes it to the far end with a spacer. */
export const SETTINGS_MODULE: ModuleEntry = {
  to: '/settings', labelKey: 'rail.settings', Icon: GearIcon,
}
```

- [ ] **Step 4: Rewrite `src/layouts/AppRail.tsx` to read it**

```tsx
import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { MODULES, SETTINGS_MODULE } from './modules'

function railClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'rail-item is-active' : 'rail-item'
}

export default function AppRail() {
  const { t } = useTranslation()
  const item = ({ to, labelKey, Icon }: typeof SETTINGS_MODULE) => {
    const label = t(labelKey)
    return (
      <NavLink key={to} to={to} className={railClass} aria-label={label} title={label}>
        <Icon />
      </NavLink>
    )
  }
  return (
    <nav className="app-rail" aria-label={t('rail.label')}>
      {MODULES.map(item)}
      <div className="rail-spacer" />
      {item(SETTINGS_MODULE)}
    </nav>
  )
}
```

- [ ] **Step 5: Create `src/layouts/BottomNav.tsx`**

```tsx
import { NavLink } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { MODULES, SETTINGS_MODULE } from './modules'

function tabClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'bottom-nav-item is-active' : 'bottom-nav-item'
}

/**
 * The rail, moved under the thumb. Phone only — CSS hides it from 640px up, where the rail
 * itself is back. Rendered unconditionally so nothing here depends on the viewport hook.
 */
export default function BottomNav() {
  const { t } = useTranslation()
  const tab = ({ to, labelKey, Icon }: typeof SETTINGS_MODULE) => (
    <NavLink key={to} to={to} className={tabClass}>
      <Icon size={22} />
      <span className="bottom-nav-label">{t(labelKey)}</span>
    </NavLink>
  )
  return (
    <nav className="app-bottom-nav" aria-label={t('rail.label')}>
      {MODULES.map(tab)}
      {tab(SETTINGS_MODULE)}
    </nav>
  )
}
```

- [ ] **Step 6: Render it from `src/layouts/AppShell.tsx`**

```tsx
import { Outlet, useMatch } from 'react-router-dom'
import { useMailNotifications } from '../modules/mail/notify/useMailNotifications'
import { useTabTitle } from '../hooks/useTabTitle'
import { useFaviconBadge } from '../hooks/useFaviconBadge'
import AppRail from './AppRail'
import BottomNav from './BottomNav'
import TopBar from './TopBar'

export default function AppShell() {
  useMailNotifications()
  useTabTitle()
  useFaviconBadge()
  // Composing is a full-screen task with its own send bar, and a tab bar under a software
  // keyboard serves nobody.
  const composing = useMatch('/mail/compose') != null

  return (
    <div className="app-shell">
      <TopBar />
      <div className="app-shell-body">
        <AppRail />
        <main className="app-content">
          <Outlet />
        </main>
      </div>
      {!composing && <BottomNav />}
    </div>
  )
}
```

- [ ] **Step 7: Run the tests**

Run: `npm test -- src/layouts/`
Expected: PASS, including the existing `IdentityMenu` and `RequirePrimary` tests.

- [ ] **Step 8: Style the bar in `src/styles/shell.css`**

Base rule, beside `.app-rail`:

```css
/* Phone only. The media block below is what shows it; here it is simply absent. */
.app-bottom-nav { display: none; }
```

Inside the existing `@media (max-width: 639px)` block:

```css
  .app-rail { display: none; }

  .app-bottom-nav {
    display: flex;
    flex: none;
    background: var(--rail-bg);
    border-top: 1px solid var(--border);
  }

  .bottom-nav-item {
    flex: 1;
    min-width: 0;
    min-height: 56px;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 2px;
    padding: 6px 2px;
    color: var(--rail-fg);
    text-decoration: none;
    font-size: 11px;
  }

  .bottom-nav-item.is-active { color: var(--rail-item-active-fg); }

  .bottom-nav-label {
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
```

- [ ] **Step 9: Run the whole suite and type-check**

Run: `npm test && npm run typecheck`
Expected: PASS, no type errors.

- [ ] **Step 10: Commit**

```bash
git add src/layouts/modules.ts src/layouts/BottomNav.tsx src/layouts/BottomNav.test.tsx src/layouts/AppRail.tsx src/layouts/AppShell.tsx src/styles/shell.css
git commit -m "feat: add the phone tab bar and share one module list with the rail"
```

---

### Task 5: `effectivePane` and the mail layout

**Files:**
- Create: `src/modules/mail/effectivePane.ts`
- Create: `src/modules/mail/effectivePane.test.ts`
- Create: `src/components/FloatingAction.tsx`
- Modify: `src/modules/mail/MailLayout.tsx`
- Modify: `src/modules/mail/MailLayout.test.tsx`
- Modify: `src/styles/mail.css`

**Interfaces:**
- Consumes: `useViewport` and `Viewport` (Task 1); `ContextDrawer`, `DrawerToggle`, `useContextDrawer` (Task 3).
- Produces:
  - `effectivePane(preference: ReadingPane, viewport: Viewport): ReadingPane` from `src/modules/mail/effectivePane.ts`
  - `default function FloatingAction({ label, onClick, children }: { label: string; onClick: () => void; children: ReactNode })` from `src/components/FloatingAction.tsx`
  - `MessageList` gains an optional `leading?: ReactNode` prop, forwarded to `SelectionToolbar` in Task 6. Pass it now; Task 6 renders it.

- [ ] **Step 1: Write the failing test for `effectivePane`**

Create `src/modules/mail/effectivePane.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { effectivePane } from './effectivePane'

describe('effectivePane', () => {
  it('forces one pane at a time on a phone', () => {
    expect(effectivePane('right', 'phone')).toBe('none')
    expect(effectivePane('bottom', 'phone')).toBe('none')
    expect(effectivePane('none', 'phone')).toBe('none')
  })

  // 240 + 320 minimums is 560px, against the 584px a 640px tablet leaves beside the 56px rail.
  // Overriding an explicit choice on a 900px tablet would be arbitrary.
  it('keeps the stored preference on a tablet', () => {
    expect(effectivePane('right', 'tablet')).toBe('right')
    expect(effectivePane('bottom', 'tablet')).toBe('bottom')
  })

  it('keeps the stored preference on a desktop', () => {
    expect(effectivePane('bottom', 'desktop')).toBe('bottom')
  })
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test -- src/modules/mail/effectivePane.test.ts`
Expected: FAIL — `Failed to resolve import "./effectivePane"`.

- [ ] **Step 3: Create `src/modules/mail/effectivePane.ts`**

```ts
import type { ReadingPane } from '../../hooks/usePreferences'
import type { Viewport } from '../../hooks/useViewport'

/**
 * Which arrangement is actually on screen. Only a phone overrides the account's choice: below
 * 640px there is no width for two panes at once, and `none` is the arrangement the module
 * already implements — the list stays mounted under `is-hidden` while the reader is open.
 */
export function effectivePane(preference: ReadingPane, viewport: Viewport): ReadingPane {
  return viewport === 'phone' ? 'none' : preference
}
```

- [ ] **Step 4: Run it and watch it pass**

Run: `npm test -- src/modules/mail/effectivePane.test.ts`
Expected: PASS, 3 tests.

- [ ] **Step 5: Create `src/components/FloatingAction.tsx`**

```tsx
import type { ReactNode } from 'react'

/**
 * The primary action of a module, below 1024px, where its home is behind a drawer. Rendered
 * unconditionally: CSS hides it on desktop, which takes it out of the tab order too, so no
 * component has to reason about the tier for this.
 */
export default function FloatingAction(
  { label, onClick, children }: { label: string; onClick: () => void; children: ReactNode },
) {
  return (
    <button type="button" className="floating-action" aria-label={label} title={label}
      onClick={onClick}>
      {children}
    </button>
  )
}
```

- [ ] **Step 6: Write the failing layout test**

Append to `src/modules/mail/MailLayout.test.tsx` (keep every existing test as it is):

```tsx
import { afterEach } from 'vitest'
import { mockViewport, resetViewport } from '../../test-utils'

afterEach(resetViewport)

describe('MailLayout on a phone', () => {
  it('renders no splitter', async () => {
    mockViewport('phone')
    const { container } = renderMailLayout()   // the file's existing helper
    await settle()
    expect(container.querySelector('.pane-splitter')).toBeNull()
  })

  it('puts the folder column in a drawer', async () => {
    mockViewport('phone')
    const { container } = renderMailLayout()
    await settle()
    expect(container.querySelector('.context-drawer .mail-folders')).toBeTruthy()
  })

  it('leaves the folder column inline on a desktop', async () => {
    mockViewport('desktop')
    const { container } = renderMailLayout()
    await settle()
    expect(container.querySelector('.context-drawer')).toBeNull()
    expect(container.querySelector('.mail-folders')).toBeTruthy()
  })
})
```

If the file has no `renderMailLayout` helper, reuse whatever render wrapper its existing tests use and keep the assertions identical. Check the splitter's real class name first with `grep -n 'className' src/modules/mail/split/PaneSplitter.tsx` and use that exact string.

- [ ] **Step 7: Run it and watch it fail**

Run: `npm test -- src/modules/mail/MailLayout.test.tsx`
Expected: FAIL on all three new cases — the splitter renders and there is no drawer.

- [ ] **Step 8: Declare the two new `MessageList` props, then wire `src/modules/mail/MailLayout.tsx`**

First, in `src/modules/mail/list/MessageList.tsx`, add to `Props` — Task 6 is what renders them:

```tsx
  /** Rendered at the head of the toolbar: the drawer's hamburger below 1024px. */
  leading?: ReactNode
  /** Refresh, which loses its home once the folder column is a drawer. */
  onRefresh?: () => void
```

and destructure both in the component signature so `typecheck` and `lint` are clean.

Then add the imports to `MailLayout.tsx`:

```tsx
import ContextDrawer, { DrawerToggle, useContextDrawer } from '../../layouts/ContextDrawer'
import FloatingAction from '../../components/FloatingAction'
import { useViewport } from '../../hooks/useViewport'
import { effectivePane } from './effectivePane'
```

Replace the pane resolution:

```tsx
  const viewport = useViewport()
  const { data: preferences, isLoading: preferencesLoading } = usePreferences()
  // Until the preferences answer, today's layout — the list already waits on the same query,
  // so nothing meaningful can flash in the wrong arrangement.
  const pane = effectivePane(preferences ? readingPaneOf(preferences) : 'right', viewport)
  const drawer = useContextDrawer()
```

Change the `list` helper so the row layout follows the tier, not the pane alone:

```tsx
  // `wide` is the one-line row layout, whose .message-row-from is pinned at 180px — half of a
  // 360px screen for the sender alone. A phone always takes the stacked one.
  const wideRows = viewport !== 'phone' && pane !== 'right'

  const list = (selected: number | null) => (
    <MessageList
      folderPath={folder}
      folderName={folderName}
      folderRole={folderNode?.specialUse ?? null}
      selectedUid={selected}
      onSelect={selectMessage}
      wide={wideRows}
      leading={drawer.inDrawer ? <DrawerToggle onClick={drawer.toggle} /> : null}
      onRefresh={refresh}
      onNotify={addToast}
      onRows={keepRows}
      onDeparted={departed}
      search={search}
      onSearchChange={changeSearch}
      onOpenResult={openResult}
    />
  )
```

Update the three call sites from `list(uid, false)` / `list(uid, true)` / `list(null, true)` to `list(uid)` / `list(uid)` / `list(null)`.

Extract the folder column into a variable and wrap it for narrow tiers:

```tsx
  const folderColumn = (
    <div className="mail-folders">
      {/* … the existing contents, unchanged … */}
    </div>
  )
```

and in the returned JSX replace the inline `<div className="mail-folders">…</div>` with:

```tsx
      {drawer.inDrawer
        ? <ContextDrawer open={drawer.open} onClose={drawer.close}>{folderColumn}</ContextDrawer>
        : folderColumn}
```

Gate the splitter on the tier — `pane === 'right'` branch:

```tsx
              {preferences && viewport !== 'phone' && (
                <PaneSplitter
                  orientation="vertical" size={listWidth} defaultSize={380} min={240} reserve={320}
                  onResize={setListWidth}
                />
              )}
```

and the `pane === 'bottom'` branch:

```tsx
              {viewport !== 'phone' && (
                <PaneSplitter
                  orientation="horizontal" size={listHeight} defaultSize={280} min={120} reserve={160}
                  onResize={setListHeight}
                />
              )}
```

Finally add the floating compose button, just before `<Toasts …>`:

```tsx
      {!composing && (
        <FloatingAction label={t('layout.newMessage')} onClick={openCompose}>
          <RocketIcon size={22} />
        </FloatingAction>
      )}
```

- [ ] **Step 9: Run the tests**

Run: `npm test -- src/modules/mail/MailLayout.test.tsx`
Expected: PASS, including every pre-existing case. If a pre-existing case broke, the cause is the `list()` signature change — fix the call site, never the assertion.

- [ ] **Step 10: Style the button and the columns in `src/styles/mail.css`**

Base rules, near `.mail-layout`:

```css
/* Desktop's compose button is in the folder column; this one would be a second copy of it. */
.floating-action { display: none; }
```

Then at the foot of the file:

```css
@media (max-width: 1023px) {
  /* Compose and Refresh both leave the drawer: opening a drawer to write a message, or to
     check for new mail, is one gesture too many. Compose becomes the floating button below;
     Refresh becomes the first entry of the list toolbar's kebab. */
  .context-drawer-panel .mail-folders-compose { display: none; }

  .floating-action {
    position: fixed;
    right: 16px;
    bottom: 16px;
    z-index: 60;
    display: flex;
    align-items: center;
    justify-content: center;
    width: 56px;
    height: 56px;
    padding: 0;
    border: none;
    border-radius: 50%;
    background: var(--action-primary);
    color: var(--action-primary-fg);
    box-shadow: 0 6px 18px rgba(0, 0, 0, 0.28);
    cursor: pointer;
  }
  .floating-action:hover { background: var(--action-primary-hover); }
}

@media (max-width: 639px) {
  /* Clear of the tab bar, which is 56px plus its border. */
  .floating-action { bottom: 73px; }
}
```

- [ ] **Step 11: Run the whole suite and type-check**

Run: `npm test && npm run typecheck`
Expected: PASS, no type errors. The two `MessageList` props were declared at the head of Step 8; they stay unused until Task 6 renders them.

- [ ] **Step 12: Commit**

```bash
git add src/modules/mail/effectivePane.ts src/modules/mail/effectivePane.test.ts src/modules/mail/MailLayout.tsx src/modules/mail/MailLayout.test.tsx src/components/FloatingAction.tsx src/modules/mail/list/MessageList.tsx src/styles/mail.css
git commit -m "feat: one pane and a drawer for the mail module on narrow screens"
```

---

### Task 6: The list toolbar's two states

**Files:**
- Modify: `src/modules/mail/list/SelectionToolbar.tsx`
- Modify: `src/modules/mail/list/SelectionToolbar.test.tsx`
- Modify: `src/modules/mail/list/MessageList.tsx`
- Modify: `src/styles/mail.css`
- Modify: `probes/mobile-layout.html`

**Interfaces:**
- Consumes: `leading` and `onRefresh` on `MessageList` (declared in Task 5).
- Produces: `SelectionToolbarProps` gains `leading?: ReactNode` and `refresh?: ToolbarAction`; `.selection-toolbar` carries `is-selecting` exactly when `count > 0`.

- [ ] **Step 1: Write the failing test**

Append to `src/modules/mail/list/SelectionToolbar.test.tsx`, reusing the file's existing props factory (call it `props()` below; use whatever the file already defines):

```tsx
describe('SelectionToolbar narrow states', () => {
  it('marks itself as selecting exactly when rows are selected', () => {
    const { container, rerender } = render(<SelectionToolbar {...props({ count: 0 })} />)
    expect(container.querySelector('.selection-toolbar')!.className).not.toContain('is-selecting')
    rerender(<SelectionToolbar {...props({ count: 3 })} />)
    expect(container.querySelector('.selection-toolbar')!.className).toContain('is-selecting')
  })

  it('renders whatever the leading slot is handed', () => {
    render(<SelectionToolbar {...props({ count: 0 })} leading={<button type="button">Menu</button>} />)
    expect(screen.getByRole('button', { name: 'Menu' })).toBeTruthy()
  })

  it('offers Refresh in the kebab when the layout supplies one', async () => {
    const onRun = vi.fn()
    render(<SelectionToolbar {...props({ count: 0 })} refresh={{ onRun }} />)
    await userEvent.click(screen.getByRole('button', { name: 'More actions' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Refresh' }))
    expect(onRun).toHaveBeenCalled()
  })
})
```

Check `DropdownMenu`'s rendered role first — `grep -n 'role=' src/components/DropdownMenu.tsx` — and use the exact role its entries carry instead of `menuitem` if it differs.

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test -- src/modules/mail/list/SelectionToolbar.test.tsx`
Expected: FAIL — no `is-selecting`, no leading slot, no Refresh entry.

- [ ] **Step 3: Extend `src/modules/mail/list/SelectionToolbar.tsx`**

Add to the props interface:

```tsx
  /** The drawer's hamburger below 1024px, nothing on desktop. */
  leading?: ReactNode
  /** Refresh, which loses its home in the folder column once that column is a drawer. */
  refresh?: ToolbarAction
```

Add the entry at the head of the kebab, above `markRead`:

```tsx
  const kebab: MenuEntry[] = [
    ...(props.refresh
      ? [{ label: t('folders.refresh'), icon: <LoaderIcon size={18} />, onSelect: props.refresh.onRun },
        'separator' as const]
      : []),
    kebabItem(t('toolbar.markRead'), <MailOpenIcon size={18} />, props.markRead),
```

with `import LoaderIcon from '../../../icons/LoaderIcon'` beside the other icon imports.

Change the root element and put the slot before the checkbox:

```tsx
    <div className={`selection-toolbar${count > 0 ? ' is-selecting' : ''}`}>
      {props.leading}
      <input
```

- [ ] **Step 4: Run the tests**

Run: `npm test -- src/modules/mail/list/SelectionToolbar.test.tsx`
Expected: PASS.

- [ ] **Step 5: Forward the two props from `src/modules/mail/list/MessageList.tsx`**

The two optional fields declared in Task 5 stop being unused — hand them on:

```tsx
      <SelectionToolbar
        leading={leading}
        refresh={onRefresh ? { onRun: onRefresh } : undefined}
        title={folderName || folderPath}
```

- [ ] **Step 6: Run the list's tests**

Run: `npm test -- src/modules/mail/list/`
Expected: PASS.

- [ ] **Step 7: Add the container query to `src/styles/mail.css`**

Beside the `.mail-list` rule:

```css
/* A container, so the toolbar sizes itself against the column it sits in and not the window.
   Those are different numbers: the splitter can leave this column at its 240px minimum on a
   tablet — narrower than any phone — while the same tablet under readingPane:'none' gives it
   960px. Safe: this column's width comes from the splitter's inline style, never from its
   contents, which is exactly what inline-size containment assumes. */
.mail-list {
  background: var(--surface);
  container-type: inline-size;
}
```

Then, at the foot of the file:

```css
/* Six 44px controls do not fit across 360px. Nothing usable is lost: archive, junk and delete
   are already disabled with an empty selection, so at rest they only rendered dead state. */
@container (max-width: 480px) {
  .selection-toolbar:not(.is-selecting) .selection-archive,
  .selection-toolbar:not(.is-selecting) .selection-junk,
  .selection-toolbar:not(.is-selecting) .selection-delete,
  .selection-toolbar:not(.is-selecting) .selection-master { display: none; }

  .selection-toolbar.is-selecting .selection-search,
  .selection-toolbar.is-selecting .selection-star { display: none; }

  .selection-btn { width: 40px; height: 40px; }
}
```

Give the four buttons those class names in `SelectionToolbar.tsx` — `selection-btn selection-archive`, `selection-btn selection-junk`, `selection-btn is-danger selection-delete`, `selection-btn selection-search` — so the query targets them by name rather than by position.

- [ ] **Step 8: Add the toolbar case to `probes/mobile-layout.html`**

Push onto `CASES`, above the `measure()` call:

```js
    const toolbar = (selecting) => `
      <div class="mail-list" style="width:${selecting ? 360 : 360}px">
        <div class="selection-toolbar${selecting ? ' is-selecting' : ''}">
          <button class="drawer-toggle" style="display:inline-flex">☰</button>
          <input type="checkbox" class="selection-master" />
          <span class="selection-heading">
            <span class="selection-title">${selecting ? '3 selected' : 'Inbox'}</span>
            <button class="selection-btn selection-star">★</button>
          </span>
          <div class="selection-actions">
            <button class="selection-btn selection-archive">A</button>
            <button class="selection-btn selection-junk">J</button>
            <button class="selection-btn is-danger selection-delete">D</button>
            <button class="selection-btn selection-search">S</button>
            <button class="selection-btn">⋮</button>
          </div>
        </div>
      </div>`
    CASES.push({ name: 'toolbar-idle-360', html: toolbar(false), touch: '.selection-btn, .drawer-toggle' })
    CASES.push({ name: 'toolbar-selecting-360', html: toolbar(true), touch: '.selection-btn' })
    CASES.push({
      name: 'toolbar-idle-240',
      html: toolbar(false).replace('width:360px', 'width:240px'),
      touch: '.selection-btn, .drawer-toggle',
    })
```

- [ ] **Step 9: Measure in a browser**

Run `npm run dev`, open `http://localhost:5173/probes/mobile-layout.html` in Chrome with device emulation at 360×640, and read `#out`.
Expected: `overflow: 0` on all three toolbar cases and `undersized: 0`. If `overflow` is above 0 at 240px, hide `.selection-star` as well in the `:not(.is-selecting)` branch and measure again.

- [ ] **Step 10: Run the suite and type-check, then commit**

Run: `npm test && npm run typecheck`

```bash
git add src/modules/mail/list/SelectionToolbar.tsx src/modules/mail/list/SelectionToolbar.test.tsx src/modules/mail/list/MessageList.tsx src/styles/mail.css probes/mobile-layout.html
git commit -m "feat: give the list toolbar an idle and a selecting state in narrow columns"
```

---

### Task 7: Rows under a finger

**Files:**
- Create: `src/hooks/useLongPress.ts`
- Create: `src/hooks/useLongPress.test.ts`
- Modify: `src/modules/mail/list/MessageList.tsx`
- Modify: `src/styles/mail.css`

**Interfaces:**
- Consumes: nothing new.
- Produces: `useLongPress(onLongPress: () => void, ms?: number): { onPointerDown, onPointerUp, onPointerMove, onPointerCancel }` from `src/hooks/useLongPress.ts` — a props bag spread onto an element.

- [ ] **Step 1: Write the failing test**

Create `src/hooks/useLongPress.test.ts`:

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useLongPress } from './useLongPress'

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

const at = (x: number, y: number) => ({ clientX: x, clientY: y }) as React.PointerEvent

describe('useLongPress', () => {
  it('fires once the press outlasts the delay', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).toHaveBeenCalledTimes(1)
  })

  it('does not fire on a tap', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { result.current.onPointerUp() })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).not.toHaveBeenCalled()
  })

  it('does not fire when the finger travels — that is a scroll', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { result.current.onPointerMove(at(0, 30)) })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).not.toHaveBeenCalled()
  })

  it('tolerates the jitter of a still finger', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { result.current.onPointerMove(at(3, 4)) })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).toHaveBeenCalledTimes(1)
  })
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test -- src/hooks/useLongPress.test.ts`
Expected: FAIL — `Failed to resolve import "./useLongPress"`.

- [ ] **Step 3: Create `src/hooks/useLongPress.ts`**

```ts
import { useCallback, useEffect, useRef } from 'react'
import type { PointerEvent } from 'react'

const TRAVEL = 10

/**
 * A press held still for `ms`. The travel guard is what separates it from a scroll: a finger
 * that moves more than 10px was dragging the list, not choosing a row.
 */
export function useLongPress(onLongPress: () => void, ms = 500) {
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const origin = useRef({ x: 0, y: 0 })

  const cancel = useCallback(() => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = null
  }, [])

  useEffect(() => cancel, [cancel])

  return {
    onPointerDown(event: PointerEvent) {
      origin.current = { x: event.clientX, y: event.clientY }
      cancel()
      timer.current = setTimeout(() => { timer.current = null; onLongPress() }, ms)
    },
    onPointerMove(event: PointerEvent) {
      const { x, y } = origin.current
      if (Math.hypot(event.clientX - x, event.clientY - y) > TRAVEL) cancel()
    },
    onPointerUp: cancel,
    onPointerCancel: cancel,
  }
}
```

- [ ] **Step 4: Run the tests**

Run: `npm test -- src/hooks/useLongPress.test.ts`
Expected: PASS, 4 tests.

- [ ] **Step 5: Wire it into the row in `src/modules/mail/list/MessageList.tsx`**

Import it, and inside the row map — after `check` is built — add:

```tsx
            // Entering selection with no visible checkbox to aim at: the row itself is the target.
            const press = useLongPress(() => {
              if (!crossFolder) selection.toggle(message.uid, index)
            })
```

React forbids a hook inside a loop, so lift the row into its own component instead. Extract the whole `<li>` body into `src/modules/mail/list/MessageRow.tsx` if the map body is longer than about 60 lines; otherwise pass a single `onLongPress` from the parent through a small wrapper component defined in the same file:

```tsx
function Row({ onLongPress, children, ...rest }:
  { onLongPress: () => void; children: ReactNode } & HTMLAttributes<HTMLDivElement>) {
  const press = useLongPress(onLongPress)
  return <div {...rest} {...press}>{children}</div>
}
```

and render `<Row onLongPress={() => selection.toggle(message.uid, index)} …>` in place of the row's `<div role="button" …>`, keeping every existing attribute and handler.

- [ ] **Step 6: Run the list's tests**

Run: `npm test -- src/modules/mail/list/`
Expected: PASS. If a test drove the row with `fireEvent.click`, it still passes — the long-press handlers are additive.

- [ ] **Step 7: Hide the hover affordances in `src/styles/mail.css`**

In the container-query block from Task 6:

```css
  /* There is no hover on a touch screen, and on several touch browsers :hover sticks after a
     tap — the cluster would appear and stay. These actions remain reachable by opening the
     message and through the selection toolbar. */
  .message-row-cluster { display: none; }

  /* A checkbox per row costs width nobody has. Long-pressing a row starts a selection, and
     .has-selection — which the list already carries — brings every checkbox back. */
  .message-list:not(.has-selection) .message-row-check { display: none; }
```

- [ ] **Step 8: Measure**

Add a `message-row` case to `probes/mobile-layout.html` with both a `.message-list` and a `.message-list.has-selection` variant at a 360px container, then reload the probe in Chrome at 360×640.
Expected: `overflow: 0` in both, and the row's own height at or above 44px.

- [ ] **Step 9: Run the suite, type-check, commit**

Run: `npm test && npm run typecheck`

```bash
git add src/hooks/useLongPress.ts src/hooks/useLongPress.test.ts src/modules/mail/list/MessageList.tsx src/styles/mail.css probes/mobile-layout.html
git commit -m "feat: long-press to select and drop the hover affordances on touch"
```

---

### Task 8: Pull to refresh

**Files:**
- Create: `src/hooks/usePullToRefresh.ts`
- Create: `src/hooks/usePullToRefresh.test.ts`
- Modify: `src/modules/mail/list/MessageList.tsx`
- Modify: `src/styles/mail.css`
- Modify: `src/locales/en/mail.json`, `src/locales/fr/mail.json`

**Interfaces:**
- Consumes: `onRefresh` on `MessageList` (Task 5/6).
- Produces: `usePullToRefresh(ref: RefObject<HTMLElement | null>, onRefresh: () => void): { pull: number; armed: boolean }` from `src/hooks/usePullToRefresh.ts`.

- [ ] **Step 1: Add the strings**

`src/locales/en/mail.json`, inside `list`:

```json
"pull": "Pull to refresh",
"release": "Release to refresh"
```

`src/locales/fr/mail.json`, inside `list`:

```json
"pull": "Tirer pour rafraîchir",
"release": "Relâcher pour rafraîchir"
```

- [ ] **Step 2: Write the failing test**

Create `src/hooks/usePullToRefresh.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { createRef } from 'react'
import { usePullToRefresh } from './usePullToRefresh'

function scroller(scrollTop: number) {
  const element = document.createElement('div')
  Object.defineProperty(element, 'scrollTop', { value: scrollTop, writable: true })
  document.body.appendChild(element)
  return element
}

// jsdom has no TouchEvent constructor; a plain Event carrying a `touches` array is what the
// hook actually reads, and it dispatches through the same listeners.
function fire(element: HTMLElement, type: string, y: number) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'touches', { value: [{ clientY: y }] })
  element.dispatchEvent(event)
}

describe('usePullToRefresh', () => {
  it('refreshes once the pull passes the threshold', () => {
    const element = scroller(0)
    const ref = createRef<HTMLElement>()
    // @ts-expect-error assigning a ref in a test
    ref.current = element
    const onRefresh = vi.fn()
    renderHook(() => usePullToRefresh(ref, onRefresh))
    act(() => {
      fire(element, 'touchstart', 0)
      fire(element, 'touchmove', 100)
      fire(element, 'touchend', 100)
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })

  it('ignores a short pull', () => {
    const element = scroller(0)
    const ref = createRef<HTMLElement>()
    // @ts-expect-error assigning a ref in a test
    ref.current = element
    const onRefresh = vi.fn()
    renderHook(() => usePullToRefresh(ref, onRefresh))
    act(() => {
      fire(element, 'touchstart', 0)
      fire(element, 'touchmove', 20)
      fire(element, 'touchend', 20)
    })
    expect(onRefresh).not.toHaveBeenCalled()
  })

  it('ignores a pull that starts part-way down the list', () => {
    const element = scroller(400)
    const ref = createRef<HTMLElement>()
    // @ts-expect-error assigning a ref in a test
    ref.current = element
    const onRefresh = vi.fn()
    renderHook(() => usePullToRefresh(ref, onRefresh))
    act(() => {
      fire(element, 'touchstart', 0)
      fire(element, 'touchmove', 100)
      fire(element, 'touchend', 100)
    })
    // Pulling down inside a scrolled list is scrolling, not refreshing.
    expect(onRefresh).not.toHaveBeenCalled()
  })
})
```

- [ ] **Step 3: Run it and watch it fail**

Run: `npm test -- src/hooks/usePullToRefresh.test.ts`
Expected: FAIL — `Failed to resolve import "./usePullToRefresh"`.

- [ ] **Step 4: Create `src/hooks/usePullToRefresh.ts`**

```ts
import { useEffect, useState } from 'react'
import type { RefObject } from 'react'

const THRESHOLD = 64
const MAX = 96

/**
 * The gesture that replaces the refresh button once the folder column is a drawer. It only
 * starts at the very top of the list: a downward drag anywhere else is a scroll.
 *
 * Returns the current pull in pixels and whether releasing now would refresh, so the caller can
 * draw a band. Native listeners rather than React's, because touchmove has to be non-passive to
 * be preventable, and React attaches its own passively.
 */
export function usePullToRefresh(ref: RefObject<HTMLElement | null>, onRefresh: () => void) {
  const [pull, setPull] = useState(0)

  useEffect(() => {
    const element = ref.current
    if (!element) return
    // Locals, not refs: the three handlers are created together inside this effect and close
    // over the same two variables, so `end` reads what `move` last wrote without a ref dance.
    let origin: number | null = null
    let travelled = 0

    function start(event: TouchEvent) {
      // Only from the very top. A downward drag anywhere else is a scroll.
      origin = element!.scrollTop === 0 ? event.touches[0].clientY : null
      travelled = 0
    }
    function move(event: TouchEvent) {
      if (origin === null) return
      const travel = event.touches[0].clientY - origin
      if (travel <= 0) { travelled = 0; setPull(0); return }
      // Only once it is really a pull: preventing default earlier would kill ordinary scrolling.
      if (travel > 8 && event.cancelable) event.preventDefault()
      travelled = Math.min(MAX, travel)
      setPull(travelled)
    }
    function end() {
      if (origin !== null && travelled >= THRESHOLD) onRefresh()
      origin = null
      travelled = 0
      setPull(0)
    }

    element.addEventListener('touchstart', start, { passive: true })
    // Non-passive, or preventDefault is ignored and the browser scrolls under the gesture.
    element.addEventListener('touchmove', move, { passive: false })
    element.addEventListener('touchend', end)
    element.addEventListener('touchcancel', end)
    return () => {
      element.removeEventListener('touchstart', start)
      element.removeEventListener('touchmove', move)
      element.removeEventListener('touchend', end)
      element.removeEventListener('touchcancel', end)
    }
  }, [ref, onRefresh])

  return { pull, armed: pull >= THRESHOLD }
}
```

- [ ] **Step 5: Run the tests**

Run: `npm test -- src/hooks/usePullToRefresh.test.ts`
Expected: PASS, 3 tests.

- [ ] **Step 6: Wire it into `src/modules/mail/list/MessageList.tsx`**

`scrollRef` already exists. Beside it:

```tsx
  const { pull, armed } = usePullToRefresh(scrollRef, () => onRefresh?.())
```

and wrap the scrolling band:

```tsx
      <div className="mail-list-scroll" ref={scrollRef}>
        {pull > 0 && (
          <div className="mail-pull" style={{ height: pull }} aria-live="polite">
            {t(armed ? 'list.release' : 'list.pull')}
          </div>
        )}
        {rows()}
      </div>
```

- [ ] **Step 7: Style it in `src/styles/mail.css`**

```css
.mail-pull {
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  color: var(--text-muted);
  font-size: 12px;
}
```

and inside the existing `@media (max-width: 1023px)` block:

```css
  /* Chrome for Android's own pull-to-refresh fires on the same gesture; contained here as well
     as on the shell, or the browser's wins and ours never runs. */
  .mail-list-scroll { overscroll-behavior-y: contain; }
```

- [ ] **Step 8: Run the suite, type-check, commit**

Run: `npm test && npm run typecheck`

```bash
git add src/hooks/usePullToRefresh.ts src/hooks/usePullToRefresh.test.ts src/modules/mail/list/MessageList.tsx src/styles/mail.css src/locales/en/mail.json src/locales/fr/mail.json
git commit -m "feat: pull the message list down to refresh it"
```

---

### Task 9: The reader on a narrow screen

**Files:**
- Modify: `src/modules/mail/reader/sanitizeBody.ts`
- Modify: `src/modules/mail/reader/sanitizeBody.test.ts`
- Modify: `src/modules/mail/reader/MessageReader.tsx`
- Modify: `src/styles/mail.css`

**Interfaces:**
- Consumes: `useViewport` (Task 1).
- Produces: `renderBodyDocument(fragment: string, options?: { dark?: boolean; narrow?: boolean }): string`.

- [ ] **Step 1: Write the failing test**

Append to `src/modules/mail/reader/sanitizeBody.test.ts`:

```ts
describe('renderBodyDocument narrow', () => {
  it('trims the body padding', () => {
    expect(renderBodyDocument('<p>x</p>', { narrow: true })).toContain('padding: 12px 14px')
    expect(renderBodyDocument('<p>x</p>')).toContain('padding: 18px 22px')
  })

  it('pins the text scale, which iOS otherwise inflates on its own', () => {
    expect(renderBodyDocument('<p>x</p>', { narrow: true })).toContain('text-size-adjust: 100%')
  })

  it('keeps the existing width guards whatever the width', () => {
    const narrow = renderBodyDocument('<p>x</p>', { narrow: true })
    expect(narrow).toContain('img { max-width: 100%')
    expect(narrow).toContain('table { max-width: 100%')
    expect(narrow).toContain('overflow-wrap: break-word')
  })
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test -- src/modules/mail/reader/sanitizeBody.test.ts`
Expected: FAIL on the first two cases.

- [ ] **Step 3: Extend `renderBodyDocument`**

Change the signature and the `body` rule:

```ts
export function renderBodyDocument(
  fragment: string, options: { dark?: boolean; narrow?: boolean } = {},
): string {
```

then, above the template:

```ts
  // 44px of side margin out of a 360px screen is a lot to spend on nothing.
  const padding = options.narrow ? '12px 14px' : '18px 22px'
  // iOS reflows a document's font sizes on its own unless told the scale is deliberate.
  const scale = options.narrow ? '-webkit-text-size-adjust: 100%; text-size-adjust: 100%;' : ''
```

and inside the `body` rule of the template, replace the padding line with `padding: ${padding};` and append `${scale}` after the `font` declaration.

- [ ] **Step 4: Run the tests**

Run: `npm test -- src/modules/mail/reader/sanitizeBody.test.ts`
Expected: PASS, including every pre-existing case — the three barriers are untouched.

- [ ] **Step 5: Pass it from `src/modules/mail/reader/MessageReader.tsx`**

```tsx
import { useViewport } from '../../../hooks/useViewport'
```

then beside the other hooks:

```tsx
  const narrow = useViewport() === 'phone'
```

and at the iframe:

```tsx
          srcDoc={renderBodyDocument(body, { dark: inverted, narrow })}
```

- [ ] **Step 6: Style the header in `src/styles/mail.css`**

Inside the `@media (max-width: 639px)` block:

```css
  /* The actions sit under the address lines instead of beside them: at 360px, To, Cc and the
     spam gauge each need the full measure. */
  .reader-header { flex-direction: column; align-items: stretch; }
  .reader-actions { align-self: flex-start; }
  .reader-subject { font-size: 17px; }
  .reader-details { grid-template-columns: 1fr; }
  .reader-details dt { text-align: left; }
  .reader-back { min-height: var(--touch); }
```

- [ ] **Step 7: Run the suite, type-check, commit**

Run: `npm test && npm run typecheck`

```bash
git add src/modules/mail/reader/sanitizeBody.ts src/modules/mail/reader/sanitizeBody.test.ts src/modules/mail/reader/MessageReader.tsx src/styles/mail.css
git commit -m "feat: fit the reader and the message body to a phone"
```

---

### Task 10: Dialogs that fit

**Files:**
- Modify: `src/styles/modal.css`
- Modify: `src/styles/modals.test.ts`
- Modify: `probes/mobile-layout.html`

**Interfaces:** none produced; CSS only.

- [ ] **Step 1: Write the failing test**

Append to `src/styles/modals.test.ts`:

```ts
const modalCss = (import.meta.glob('./modal.css', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>)['./modal.css']

describe('dialogs on a narrow screen', () => {
  // min-width always beats max-width: a 384px floor inside a 312px slot overflows the page.
  it('drops the 24rem floor below 640px', () => {
    const phone = modalCss.slice(modalCss.indexOf('@media (max-width: 639px)'))
    expect(phone).toMatch(/--modal-w:\s*0/)
  })

  it('keeps the content-sized contract above 640px', () => {
    expect(modalCss).toMatch(/min-width:\s*var\(--modal-w,\s*24rem\)/)
  })
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test -- src/styles/modals.test.ts`
Expected: FAIL on "drops the 24rem floor below 640px".

- [ ] **Step 3: Append the block to `src/styles/modal.css`**

```css
/* A 360px viewport leaves 312px inside the overlay's 24px padding, and min-width always beats
   max-width — the 24rem floor put a 384px box in a 312px slot and scrolled the page sideways.
   Below 640px a dialog simply takes the width it is given; the content-sized contract above is
   untouched. The scroll is already the overlay's. */
@media (max-width: 639px) {
  .modal-overlay { padding: 12px; }

  .modal {
    --modal-w: 0;
    width: 100%;
    max-width: 100%;
    padding: 18px;
  }
}
```

- [ ] **Step 4: Run the tests**

Run: `npm test -- src/styles/modals.test.ts src/styles/responsive.test.ts`
Expected: PASS.

- [ ] **Step 5: Add the dialog case to `probes/mobile-layout.html`**

```js
    CASES.push({
      name: 'modal-360',
      html: `<div class="modal-overlay" style="position:static">
               <div class="modal">
                 <div class="modal-header"><span class="modal-title">Move to…</span>
                   <button class="modal-close">✕</button></div>
                 <div class="field-h"><label>Folder</label><select><option>INBOX.Archive</option></select></div>
               </div>
             </div>`,
      touch: '.modal-close',
    })
```

- [ ] **Step 6: Measure**

Reload the probe in Chrome at 360×640.
Expected: `overflow: 0`, and the `.modal` box no wider than the overlay.

- [ ] **Step 7: Commit**

```bash
git add src/styles/modal.css src/styles/modals.test.ts probes/mobile-layout.html
git commit -m "fix: stop the 24rem dialog floor overflowing a 360px screen"
```

---

### Task 11: The composer on a phone

**Files:**
- Modify: `src/styles/mail.css`
- Modify: `probes/mobile-layout.html`

**Interfaces:** none produced; CSS only. `AppShell` already hides the tab bar on `/mail/compose` (Task 4).

- [ ] **Step 1: Add the composer block to `src/styles/mail.css`**

Inside the existing `@media (max-width: 639px)` block:

```css
  /* Wrapped, never scrolled sideways: a scrolling toolbar hides tools behind an affordance
     nobody can see, while wrapping costs two or three rows and keeps every tool reachable. */
  .compose-toolbar { flex-wrap: wrap; }
  .compose-tool, .compose-tool-select { min-height: var(--touch); }

  /* From, To, Cc and Bcc each take the full measure; a label beside a token field leaves the
     field about 200px, which is under two addresses. */
  .compose-from, .compose-to-row { flex-direction: column; align-items: stretch; gap: 4px; }
  .compose-link-form input { width: 100%; }

  /* Send stays reachable with the body scrolled and the keyboard up. */
  .compose-header {
    position: sticky;
    top: 0;
    z-index: 5;
    background: var(--surface);
  }
```

- [ ] **Step 2: Add the composer case to `probes/mobile-layout.html`**

```js
    CASES.push({
      name: 'compose-360',
      html: `<div class="compose-view">
               <div class="compose-header"><button class="btn btn-primary compose-send">Send</button></div>
               <div class="compose-fields">
                 <div class="compose-to-row"><span>To</span><input class="rule-wizard-input" /></div>
               </div>
               <div class="compose-toolbar">
                 ${Array.from({ length: 14 }, (_, i) => `<button class="compose-tool">${i}</button>`).join('')}
               </div>
             </div>`,
      touch: '.compose-tool, .compose-send',
    })
```

- [ ] **Step 3: Measure**

Reload the probe in Chrome at 360×640.
Expected: `overflow: 0`, `undersized: 0`. If the toolbar overflows, the cause is a `.compose-tool-group` that is itself `flex-wrap: nowrap` — add it to the wrap rule and measure again.

- [ ] **Step 4: Run the suite, type-check, commit**

Run: `npm test && npm run typecheck`

```bash
git add src/styles/mail.css probes/mobile-layout.html
git commit -m "feat: wrap the composer toolbar and stack its fields on a phone"
```

---

### Task 12: Contacts

**Files:**
- Modify: `src/modules/contacts/ContactsLayout.tsx`
- Modify: `src/modules/contacts/ContactsLayout.test.tsx`
- Modify: `src/modules/contacts/ContactList.tsx`
- Modify: `src/index.css`

**Interfaces:**
- Consumes: `ContextDrawer`, `DrawerToggle`, `useContextDrawer` (Task 3); `FloatingAction` (Task 5); `useViewport` (Task 1).
- Produces: `ContactList` gains an optional `leading?: ReactNode` prop rendered at the head of `.contacts-list-heading`.

- [ ] **Step 1: Write the failing test**

Append to `src/modules/contacts/ContactsLayout.test.tsx`, reusing the file's existing render helper:

```tsx
import { afterEach } from 'vitest'
import { mockViewport, resetViewport } from '../../test-utils'

afterEach(resetViewport)

describe('ContactsLayout on a phone', () => {
  it('puts the scope column in a drawer and renders no splitter', async () => {
    mockViewport('phone')
    const { container } = renderContacts()   // the file's existing helper
    await settle()
    expect(container.querySelector('.context-drawer .contacts-scopes-column')).toBeTruthy()
    expect(container.querySelector('.pane-splitter')).toBeNull()
  })

  it('shows the list alone until a contact is picked', async () => {
    mockViewport('phone')
    const { container } = renderContacts()
    await settle()
    expect(container.querySelector('[data-testid="contact-list"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="contact-card"]')).toBeNull()
  })
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test -- src/modules/contacts/ContactsLayout.test.tsx`
Expected: FAIL on both.

- [ ] **Step 3: Wire `src/modules/contacts/ContactsLayout.tsx`**

Imports:

```tsx
import ContextDrawer, { DrawerToggle, useContextDrawer } from '../../layouts/ContextDrawer'
import FloatingAction from '../../components/FloatingAction'
import { useViewport } from '../../hooks/useViewport'
```

Inside the component:

```tsx
  const viewport = useViewport()
  const drawer = useContextDrawer()
  const phone = viewport === 'phone'
```

Lift the scope column into a variable and wrap it:

```tsx
  const scopeColumn = (
    <div className="contacts-scopes-column">
      {/* … the existing contents, unchanged … */}
    </div>
  )
```

```tsx
      {drawer.inDrawer
        ? <ContextDrawer open={drawer.open} onClose={drawer.close}>{scopeColumn}</ContextDrawer>
        : scopeColumn}
```

Hand the hamburger to the list and give the two panes their turns:

```tsx
        <div className="contacts-row">
          {!(phone && selectedId) && (
            <div className="contacts-list" style={phone ? undefined : { width: listWidth }}
              data-testid="contact-list">
              {isLoading && <p className="contacts-empty">{t('layout.loading')}</p>}
              {isError && <p className="contacts-empty">{t('layout.loadFailed')}</p>}
              {contacts && (
                <ContactList contacts={scoped} selectedId={selectedId} onSelect={select}
                  leading={drawer.inDrawer ? <DrawerToggle onClick={drawer.toggle} /> : null}
                  onToggleFavorite={toggleFavorite} onDelete={setPendingDelete}
                  onEdit={id => navigate(`/contacts/${id}/edit`)} />
              )}
            </div>
          )}
          {!phone && (
            <PaneSplitter orientation="vertical" size={listWidth} defaultSize={380} min={240}
              reserve={320} onResize={setListWidth} />
          )}
          {!(phone && !selectedId) && (
            <div className="contacts-card" data-testid="contact-card">
              <ContactCard contact={selected} onToggleFavorite={toggleFavorite}
                onDelete={setPendingDelete} onEdit={id => navigate(`/contacts/${id}/edit`)} />
            </div>
          )}
        </div>
```

Add the floating "Add contact" button before the closing `</div>` of `.contacts-layout`:

```tsx
      {!inEditor && (
        <FloatingAction label={t('layout.add')} onClick={() => navigate('/contacts/new')}>
          <PersonPlusIcon size={22} />
        </FloatingAction>
      )}
```

with `import PersonPlusIcon from '../../icons/PersonPlusIcon.jsx'`.

- [ ] **Step 4: Add the `leading` slot to `src/modules/contacts/ContactList.tsx`**

Add `leading?: ReactNode` to its props, and render it first inside the heading:

```tsx
      <div className="contacts-list-heading">
        {leading}
        <span className="contacts-search">
```

- [ ] **Step 5: Run the tests**

Run: `npm test -- src/modules/contacts/`
Expected: PASS, including every pre-existing case.

- [ ] **Step 6: Style it in `src/index.css`**

Append:

```css
@media (max-width: 1023px) {
  .contacts-scopes-column { border-right: none; }
}

@media (max-width: 639px) {
  /* One pane at a time, so neither carries a stored width any more. */
  .contacts-list, .contacts-card { width: auto; flex: 1; min-width: 0; }
  .contact-tile { min-height: var(--touch); }
  .contact-scope { min-height: var(--touch); }
  .contacts-transfer { flex-wrap: wrap; }
  .contact-editor-body { padding: 14px; }
  .contact-editor-body .field-h { flex-direction: column; align-items: stretch; }
  .contact-editor-body .field-h > label:first-child { width: auto; }
}
```

- [ ] **Step 7: Run the suite, type-check, commit**

Run: `npm test && npm run typecheck`

```bash
git add src/modules/contacts/ src/index.css
git commit -m "feat: give contacts a drawer, one pane at a time and a floating add"
```

---

### Task 13: Settings, forms and lists

**Files:**
- Modify: `src/modules/settings/SettingsLayout.tsx`
- Modify: `src/modules/settings/SettingsLayout.test.tsx`
- Modify: `src/styles/shell.css`
- Modify: `src/index.css`

**Interfaces:**
- Consumes: `ContextDrawer`, `DrawerToggle`, `useContextDrawer` (Task 3).
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the failing test**

Append to `src/modules/settings/SettingsLayout.test.tsx`, reusing the file's render helper:

```tsx
import { afterEach } from 'vitest'
import { mockViewport, resetViewport } from '../../test-utils'

afterEach(resetViewport)

describe('SettingsLayout below 1024px', () => {
  it('puts its navigation in a drawer behind a toggle', async () => {
    mockViewport('tablet')
    const { container } = renderSettings()   // the file's existing helper
    await settle()
    expect(container.querySelector('.context-drawer .context-pane')).toBeTruthy()
    expect(container.querySelector('.drawer-toggle')).toBeTruthy()
  })

  it('leaves the navigation inline on a desktop', async () => {
    mockViewport('desktop')
    const { container } = renderSettings()
    await settle()
    expect(container.querySelector('.context-drawer')).toBeNull()
    expect(container.querySelector('.drawer-toggle')).toBeNull()
  })
})
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test -- src/modules/settings/SettingsLayout.test.tsx`
Expected: FAIL on the first case.

- [ ] **Step 3: Wire `src/modules/settings/SettingsLayout.tsx`**

Imports:

```tsx
import ContextDrawer, { DrawerToggle, useContextDrawer } from '../../layouts/ContextDrawer'
```

Inside the component, lift the `<nav className="context-pane">` into a `nav` variable, then:

```tsx
  const drawer = useContextDrawer()

  return (
    <div className="settings-layout">
      {drawer.inDrawer
        ? <ContextDrawer open={drawer.open} onClose={drawer.close}>{nav}</ContextDrawer>
        : nav}
      <div className="settings-content">
        {/* The only module that needs a band of its own: its nine pages each draw their own
            .settings-page-header, so a hamburger placed there would be written nine times. */}
        {drawer.inDrawer && (
          <div className="settings-mobile-bar">
            <DrawerToggle onClick={drawer.toggle} />
            <span className="settings-mobile-title">{t('nav.label')}</span>
          </div>
        )}
        <Outlet />
      </div>
    </div>
  )
```

- [ ] **Step 4: Run the tests**

Run: `npm test -- src/modules/settings/SettingsLayout.test.tsx`
Expected: PASS.

- [ ] **Step 5: Style the bar in `src/styles/shell.css`**

Base rule:

```css
.settings-mobile-bar { display: none; }
```

Inside the `@media (max-width: 1023px)` block:

```css
  .settings-mobile-bar {
    display: flex;
    align-items: center;
    gap: 8px;
    min-height: 44px;
    margin: -24px -28px 12px;
    padding: 0 12px;
    border-bottom: 1px solid var(--border);
  }
  .settings-mobile-title { font-size: 14px; font-weight: 600; }
  .context-pane { border-right: none; }
```

Inside the `@media (max-width: 639px)` block:

```css
  .settings-content { padding: 14px 16px; }
  .settings-mobile-bar { margin: -14px -16px 12px; }
```

- [ ] **Step 6: Stack the forms in `src/index.css`**

Inside the existing `@media (max-width: 639px)` block:

```css
  /* A 110px label beside a field leaves the field under 220px on a 360px screen. */
  .field-h { flex-direction: column; align-items: stretch; gap: 4px; }
  .field-h > label:first-child { width: auto; }

  /* Title and actions on one line squeezes an address to three letters; the actions drop
     under the label instead. */
  .admin-list-item { flex-wrap: wrap; }
  .admin-list-item-actions { width: 100%; justify-content: flex-end; }
  .admin-list-header { flex-wrap: wrap; gap: 8px; }

  .account-section { max-width: none; }
  .admin-icon-btn { min-height: var(--touch); min-width: var(--touch); }
```

- [ ] **Step 7: Add a settings case to `probes/mobile-layout.html`**

```js
    CASES.push({
      name: 'settings-form-360',
      html: `<div class="settings-content">
               <div class="settings-mobile-bar" style="display:flex">
                 <button class="drawer-toggle" style="display:inline-flex">☰</button>
                 <span class="settings-mobile-title">Settings</span>
               </div>
               <div class="account-section">
                 <div class="field-h"><label>Display name</label><input type="text" /></div>
                 <div class="admin-list"><div class="admin-list-item">
                   <span class="admin-list-item-email">a.very.long.address@example.com</span>
                   <div class="admin-list-item-actions">
                     <button class="admin-icon-btn">E</button><button class="admin-icon-btn">D</button>
                   </div>
                 </div></div>
               </div>
             </div>`,
      touch: '.admin-icon-btn, .drawer-toggle, input',
    })
```

- [ ] **Step 8: Measure, run the suite, type-check, commit**

Reload the probe in Chrome at 360×640: `overflow: 0`, `undersized: 0`.

Run: `npm test && npm run typecheck`

```bash
git add src/modules/settings/SettingsLayout.tsx src/modules/settings/SettingsLayout.test.tsx src/styles/shell.css src/index.css probes/mobile-layout.html
git commit -m "feat: put the settings navigation in a drawer and stack its forms"
```

---

### Task 14: Login, and the full measurement pass

**Files:**
- Modify: `src/index.css`
- Modify: `probes/mobile-layout.html`
- Modify: `src/frontend/CLAUDE.md`

**Interfaces:** none produced.

- [ ] **Step 1: Trim the login padding in `src/index.css`**

Inside the existing `@media (max-width: 639px)` block:

```css
  /* 24 + 32 of padding on each side leaves 248px of form on a 360px screen. */
  .page-center { padding: 16px; }
  .card { padding: 20px; }
```

- [ ] **Step 2: Add the login case to `probes/mobile-layout.html`**

```js
    CASES.push({
      name: 'login-360',
      html: `<div class="page-center" style="position:static;min-height:0">
               <div class="card" style="max-width:400px">
                 <div class="field"><label>Email</label><input type="email" /></div>
                 <div class="field"><label>Password</label><input type="password" /></div>
                 <button class="btn btn-primary">Sign in</button>
               </div>
             </div>`,
      touch: 'input, .btn',
    })
```

- [ ] **Step 3: Run the full measurement pass**

Run `npm run dev`, then in Chrome open `http://localhost:5173/probes/mobile-layout.html` under device emulation and record `#out` at each of:

- 360×640 (small Android)
- 390×844 (iPhone)
- 768×1024 (tablet portrait)
- 1024×768 (tablet landscape / the desktop boundary)

Expected at every size: `overflow: 0` on every case, and `undersized: 0` on every case with a `touch` selector.

- [ ] **Step 4: Fix whatever the measurements caught**

Any non-zero `overflow` is a real defect. Fix it in the stylesheet that owns the offending rule, inside the correct media or container block, and re-measure. Do not raise a breakpoint width to make a number go away — `styles/responsive.test.ts` fails the build on a third width.

If a fix cannot be found within 20 minutes for one case, stop, leave the case failing, and record it in the summary as a remaining minor with its measured number. Do not silently drop the probe case.

- [ ] **Step 5: Record the knowledge in `src/frontend/CLAUDE.md`**

Add one paragraph in the layout section, in that file's voice. It must state:

- the three tiers and their exact widths
- that desktop is the unqualified base and `@media (min-width: …)` is forbidden, enforced by `styles/responsive.test.ts`
- that `useViewport` decides what mounts and never a width
- that the list toolbar's two states are driven by a **container** query on `.mail-list`, because the column's width and the window's width are different numbers
- that `min-width: 1024px` was removed from `.app-shell` and must not come back
- that geometry is verified in `probes/mobile-layout.html`, never in jsdom

- [ ] **Step 6: Run everything**

Run: `npm test && npm run typecheck && npm run lint && npm run build`
Expected: all four clean.

- [ ] **Step 7: Check for the versioned artefact drift**

Run: `git status --short`
Expected: only the files this task touched. If `src/snoopy.microservice/ApiDocumentation.xml` appears, revert it — a test run regenerates it with unrelated churn.

- [ ] **Step 8: Commit**

```bash
git add src/index.css probes/mobile-layout.html src/frontend/CLAUDE.md
git commit -m "feat: fit the login card to a phone and record the responsive contract"
```

---

## Self-Review

**Spec coverage.** Every section of the spec maps to a task: breakpoints and the cascade direction → Task 2 (enforced by `responsive.test.ts`); `useViewport` → Task 1; the four foundations → Task 2; topbar hidden, `BottomNav`, `ContextDrawer`, the compose/refresh relocation → Tasks 3–5; the list toolbar's two states and the container query → Task 6; `wide`, the hover cluster, long-press → Tasks 5 and 7; pull-to-refresh → Task 8; `effectivePane` → Task 5; the message body's `narrow` option → Task 9; dialogs → Task 10; the composer → Task 11; contacts → Task 12; settings, forms and `admin-list` → Task 13; login and the measurement pass → Task 14.

**Naming consistency.** `effectivePane`, `useViewport`, `Viewport`, `useContextDrawer`, `ContextDrawer`, `DrawerToggle`, `FloatingAction`, `useLongPress`, `usePullToRefresh`, `MODULES`, `SETTINGS_MODULE`, `mockViewport`, `changeViewport`, `resetViewport`, `--touch`, `.is-selecting`, `.context-drawer`, `.floating-action`, `.settings-mobile-bar`, `.mail-pull` are each defined once and spelled the same way at every later use.

**Known soft spot.** Tasks 5, 12 and 13 reference each test file's existing render helper by a placeholder name (`renderMailLayout`, `renderContacts`, `renderSettings`). Read the file first and use the helper it actually defines; do not add a second one.
