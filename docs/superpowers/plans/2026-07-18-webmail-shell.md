# Webmail Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the webmail application shell — routing, vertical app rail, auth/theme contexts, a role-based CSS token contract with two palettes (night/classic × light/dark), and port the existing Aliases/Rules/Admin pages into it.

**Architecture:** React Router 6 (already installed, unused) replaces the boolean-driven navigation. An `AppShell` layout (thin top bar + vertical rail + content outlet) hosts modules under `src/modules/`. `AuthContext`/`ThemeContext` replace module-level mutable state. All colors go through role-named CSS custom properties selected by `data-palette` × `data-theme` attributes.

**Tech Stack:** React 18.3, react-router-dom 6.26, Vite 5, TypeScript (progressive, `allowJs`), Vitest 4 + Testing Library, plain CSS with custom properties.

**Spec:** `docs/superpowers/specs/2026-07-18-webmail-shell-design.md`

## Global Constraints

- **All commands run from `src/frontend`** unless stated otherwise.
- **New code is TypeScript** (`.ts`/`.tsx`, strict). **Moved code stays `.jsx`** — moving a file is not "touching" it; only rewritten code migrates.
- **UI copy is English** (the spec is French; the product is not).
- **Token rule: a token names a role, never a color.** Never introduce a hard-coded color in a component; add a role token instead.
- Palettes: `night` (default) and `classic`. `data-palette="night|classic"` × `data-theme="light|dark"` on `<html>`. localStorage keys: `appearance_theme` (existing), `appearance_palette` (new).
- **No new runtime dependencies.** Dev dependencies added: `typescript`, `@types/react`, `@types/react-dom`, `typescript-eslint`.
- **No test lost without a replacement**: a test is deleted only when its subject is deleted, and the surviving behavior must be covered by a new test in the same task.
- Backend code untouched. Backend doc fix (Task 14) is documentation only.
- Desktop-first: shell has `min-width: 1024px`; below that the page scrolls horizontally but nothing overlaps or becomes unreachable.
- Commit messages: imperative mood, ending with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Mid-branch state warning: between Task 6 and Task 13 the routed app shows ComingSoon placeholders for pages not yet ported, while the old page components remain fully tested standalone. This is expected on the feature branch; the app is complete again at Task 13.

---

### Task 1: TypeScript toolchain

**Files:**
- Create: `src/frontend/tsconfig.json`
- Modify: `src/frontend/package.json` (devDeps + scripts)
- Modify: `src/frontend/eslint.config.js`
- Rename: `src/frontend/src/main.jsx` → `src/frontend/src/main.tsx`
- Modify: `src/frontend/index.html` (script tag)

**Interfaces:**
- Produces: `npm run typecheck` script; `npm run build` now runs `tsc --noEmit` first; `.ts`/`.tsx` files compile, lint, and run under Vitest.

- [ ] **Step 1: Install dev dependencies**

```bash
npm install -D typescript @types/react @types/react-dom typescript-eslint
```

- [ ] **Step 2: Create `tsconfig.json`**

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "moduleResolution": "bundler",
    "jsx": "react-jsx",
    "allowJs": true,
    "checkJs": false,
    "strict": true,
    "noEmit": true,
    "skipLibCheck": true,
    "isolatedModules": true,
    "resolveJsonModule": true,
    "allowImportingTsExtensions": false,
    "types": ["vite/client", "vitest/globals"]
  },
  "include": ["src"]
}
```

- [ ] **Step 3: Update `package.json` scripts**

```json
"build": "tsc --noEmit && vite build",
"typecheck": "tsc --noEmit",
```
(keep every other script unchanged)

- [ ] **Step 4: Extend ESLint to TS files**

In `eslint.config.js`: add `import tseslint from 'typescript-eslint'` at the top; change the existing block's `files` to `['**/*.{js,jsx,ts,tsx}']`; append after the existing block:

```js
  ...tseslint.configs.recommended.map(cfg => ({
    ...cfg,
    files: ['**/*.{ts,tsx}'],
  })),
```

- [ ] **Step 5: Rename `src/main.jsx` to `src/main.tsx`**

Use `git mv src/main.jsx src/main.tsx`. Content unchanged except: `document.getElementById('root')` needs a non-null assertion — `createRoot(document.getElementById('root')!)`.

In `index.html`, change `<script type="module" src="/src/main.jsx"></script>` to `src="/src/main.tsx"`.

- [ ] **Step 6: Verify everything still works**

Run: `npm run typecheck` → 0 errors. `npm run lint` → 0 errors. `npm run test` → 309 tests pass. `npm run build` → succeeds.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Add progressive TypeScript toolchain to the frontend"
```

---

### Task 2: Design tokens and the two palettes

**Files:**
- Create: `src/frontend/src/styles/tokens.css`
- Create: `src/frontend/src/styles/theme-night.css`
- Create: `src/frontend/src/styles/theme-classic.css`
- Modify: `src/frontend/src/index.css` (delete `:root` block lines 7-20, delete token overrides from the `[data-theme="dark"]` block at ~line 1347, rename token usages)
- Modify: `src/frontend/src/main.tsx` (import order)
- Modify: `src/frontend/index.html` (blocking script sets `data-palette`)

**Interfaces:**
- Produces: the full token contract below, consumed by every later task. Palette selection via `data-palette` attribute (`night` | `classic`), guaranteed set before first paint by the blocking script.

**Token contract** (all colors live in palette files, never in `tokens.css` or components):

```
--font, --radius-sm, --radius-md
--bg, --surface, --surface-raised, --surface-sunken, --border
--text, --text-muted
--topbar-bg, --topbar-fg
--rail-bg, --rail-fg, --rail-item, --rail-item-active, --rail-item-active-fg
--pane-item-hover, --pane-item-active-bg, --pane-item-active-fg
--accent-unread
--action-primary, --action-primary-hover, --action-primary-fg
--danger, --danger-hover, --success
```

- [ ] **Step 1: Create `src/styles/tokens.css`**

```css
/* Design token contract. A token names a ROLE, never a color.
   Values live in theme-night.css / theme-classic.css, selected by
   [data-palette] and [data-theme] on <html> (set pre-paint by index.html).
   Never hard-code a color in a component — add a role token instead. */
:root {
  --font: system-ui, -apple-system, 'Segoe UI', sans-serif;
  --radius-sm: 4px;
  --radius-md: 8px;
}
```

- [ ] **Step 2: Create `src/styles/theme-night.css`**

```css
/* Palette "night" — Nuit & corail (default). */
[data-palette='night'] {
  --bg: #faf8f6;
  --surface: #ffffff;
  --surface-raised: #ffffff;
  --surface-sunken: #f4f1ee;
  --border: #e5e0da;
  --text: #1c1a18;
  --text-muted: #6f6a64;
  --topbar-bg: #182238;
  --topbar-fg: #ffffff;
  --rail-bg: #182238;
  --rail-fg: #c3ccdd;
  --rail-item: #26334f;
  --rail-item-active: #e2674a;
  --rail-item-active-fg: #ffffff;
  --pane-item-hover: #f0ece8;
  --pane-item-active-bg: #ffffff;
  --pane-item-active-fg: #182238;
  --accent-unread: #e2674a;
  --action-primary: #182238;
  --action-primary-hover: #26334f;
  --action-primary-fg: #ffffff;
  --danger: #dc2626;
  --danger-hover: #b91c1c;
  --success: #16a34a;
}

/* Deliberate: --action-primary shifts hue between modes (navy in light,
   coral in dark) — navy would dissolve into a dark background. The ROLE
   is stable, the hue is not. Do not "fix" this. */
[data-palette='night'][data-theme='dark'] {
  --bg: #17191d;
  --surface: #212429;
  --surface-raised: #262a31;
  --surface-sunken: #1c1f24;
  --border: #2c3038;
  --text: #e9e5e1;
  --text-muted: #9b968f;
  --topbar-bg: #0f1626;
  --topbar-fg: #e9e5e1;
  --rail-bg: #0f1626;
  --rail-fg: #aab6cc;
  --rail-item: #1e2942;
  --rail-item-active: #f0785c;
  --rail-item-active-fg: #1a0f0b;
  --pane-item-hover: #262a31;
  --pane-item-active-bg: #262a31;
  --pane-item-active-fg: #f3efeb;
  --accent-unread: #f0785c;
  --action-primary: #f0785c;
  --action-primary-hover: #f28a72;
  --action-primary-fg: #1a0f0b;
  --danger: #f87171;
  --danger-hover: #ef4444;
  --success: #4ade80;
}
```

- [ ] **Step 3: Create `src/styles/theme-classic.css`**

Light values are the current `:root` values of `index.css` plus the new roles. **Dark values: copy the exact variable overrides from the existing `[data-theme="dark"]` block in `index.css` (~line 1347) — those are authoritative for `--bg`, `--surface`, `--border`, `--text`, `--text-muted`, `--danger*`, `--success` if present.** Values shown for tokens that do not exist today:

```css
/* Palette "classic" — Continuité (the current weesky admin look). */
[data-palette='classic'] {
  --bg: #f0f2f5;
  --surface: #ffffff;
  --surface-raised: #ffffff;
  --surface-sunken: #f7f8fa;
  --border: #dde1e7;
  --text: #1a1d23;
  --text-muted: #6b7280;
  --topbar-bg: #3450a3;
  --topbar-fg: #ffffff;
  --rail-bg: #e4e9f2;
  --rail-fg: #4b5563;
  --rail-item: #d3dae7;
  --rail-item-active: #3450a3;
  --rail-item-active-fg: #ffffff;
  --pane-item-hover: #e7eaf0;
  --pane-item-active-bg: #dbe3f5;
  --pane-item-active-fg: #25397a;
  --accent-unread: #3450a3;
  --action-primary: #3450a3;
  --action-primary-hover: #2a4090;
  --action-primary-fg: #ffffff;
  --danger: #dc2626;
  --danger-hover: #b91c1c;
  --success: #16a34a;
}

[data-palette='classic'][data-theme='dark'] {
  /* --bg, --surface, --border, --text, --text-muted, --danger*, --success:
     copy the exact values from index.css [data-theme="dark"] block. */
  --surface-raised: #2c3340;
  --surface-sunken: #212630;
  --topbar-bg: #232833;
  --topbar-fg: #e6e9ee;
  --rail-bg: #232833;
  --rail-fg: #9aa4b5;
  --rail-item: #333b49;
  --rail-item-active: #84aad8;
  --rail-item-active-fg: #12161c;
  --pane-item-hover: #2f3849;
  --pane-item-active-bg: #33405c;
  --pane-item-active-fg: #bcd2ec;
  --accent-unread: #84aad8;
  --action-primary: #84aad8;
  --action-primary-hover: #9dbde4;
  --action-primary-fg: #12161c;
}
```

- [ ] **Step 4: Surgery on `index.css`**

1. Delete the `:root { ... }` block (lines 7-20) — replaced by the styles files.
2. In the `[data-theme="dark"]` block (~line 1347): **move** the custom-property declarations (`--bg: ...` etc.) into `theme-classic.css`'s dark section (step 3). **Keep** the ~15 component-specific rule overrides (selectors with properties other than custom properties) in `index.css`, still under `[data-theme="dark"]` — they apply in dark mode for both palettes, which is acceptable for now.
3. Rename token usages, in this order (hover first — `--primary` is a substring):
   - `var(--primary-hover)` → `var(--action-primary-hover)`
   - `var(--primary)` → `var(--action-primary)`
   - `var(--radius)` → `var(--radius-sm)`

Verify no occurrence remains: `grep -n "var(--primary" src/index.css` and `grep -n "var(--radius)" src/index.css` → both empty.

- [ ] **Step 5: Import order in `main.tsx`**

```ts
import './styles/tokens.css'
import './styles/theme-night.css'
import './styles/theme-classic.css'
import './index.css'
```
(replaces the single `import './index.css'`)

- [ ] **Step 6: Blocking script sets `data-palette`**

In `index.html`, extend the existing IIFE (do not add a second script):

```js
(function(){
  var t=localStorage.getItem('appearance_theme')||'system';
  var d=t==='dark'||(t==='system'&&window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.setAttribute('data-theme',d?'dark':'light');
  var p=localStorage.getItem('appearance_palette');
  if(p!=='night'&&p!=='classic')p='night';
  document.documentElement.setAttribute('data-palette',p);
})();
```

- [ ] **Step 7: Verify**

Run: `npm run test` → 309 pass (tests don't assert colors). `npm run build` → succeeds. `npm run dev` → app renders in **night** colors (navy header, warm background); switch localStorage `appearance_palette` to `classic` in devtools → current look returns; toggle `appearance_theme` dark on both.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Introduce role-based design tokens with night and classic palettes"
```

---

### Task 3: Extract duplicated shared components, hook, and icons

**Files:**
- Create: `src/frontend/src/hooks/useToasts.js` (moved code stays JS)
- Create: `src/frontend/src/components/Toasts.jsx`
- Create: `src/frontend/src/components/DeleteConfirmModal.jsx`
- Create: `src/frontend/src/components/HelpTooltip.jsx`
- Create: `src/frontend/src/icons/TrashIcon.jsx`, `src/frontend/src/icons/PencilIcon.jsx`
- Modify: `src/frontend/src/pages/AliasesPage.jsx`, `src/frontend/src/pages/RulesPage.jsx` (remove local copies, import shared)
- Modify: `src/frontend/src/pages/AliasesPage.main.test.jsx`, `AliasesPage.admin.test.jsx`, `RulesPage.test.jsx` (import paths for the moved components)

**Interfaces:**
- Produces: `useToasts()` → `{ toasts, addToast, removeToast }` (verify against the existing hook's exact return shape before writing the interface — `Toasts` needs an `onRemove`, so a removal function exists; keep its real name); `<Toasts toasts onRemove />`; `<DeleteConfirmModal entityLabel onConfirm onClose loading />`; `<HelpTooltip text />`; `<TrashIcon />`, `<PencilIcon />`.

The duplicated definitions and their current locations:

| Item | AliasesPage.jsx | RulesPage.jsx |
|---|---|---|
| `useToasts` | line ~7 | line ~6 |
| `Toasts` | line 25 | line 21 |
| `TrashIcon` | line 51 | line 49 |
| `PencilIcon` | line 64 | line 62 |
| `HelpTooltip` | line 125 | line 141 |
| `DeleteConfirmModal` | line 410 (exported) | line 831 (not exported) |

- [ ] **Step 1: Diff each pair before moving**

For each item, compare the two copies (they are believed identical — verify). If a pair differs, reconcile into one version that satisfies both call sites and note the difference in the commit message.

- [ ] **Step 2: Create the shared files**

Move each definition verbatim into its new file with a default export **and** a named export (e.g. `export function Toasts(...)` + `export default Toasts`). `useToasts` goes to `hooks/useToasts.js` as a named export.

- [ ] **Step 3: Replace local copies with imports**

In both page files: delete the local definitions, add imports:

```js
import { useToasts } from '../hooks/useToasts.js'
import Toasts from '../components/Toasts.jsx'
import DeleteConfirmModal from '../components/DeleteConfirmModal.jsx'
import HelpTooltip from '../components/HelpTooltip.jsx'
import TrashIcon from '../icons/TrashIcon.jsx'
import PencilIcon from '../icons/PencilIcon.jsx'
```

Keep re-exports in the page files ONLY if a test imports the symbol from the page module and you are not updating that test in this task — prefer updating the test import.

- [ ] **Step 4: Update test imports**

In the three test files, change imports of `Toasts` / `DeleteConfirmModal` from the page modules to the new component paths. Do not otherwise touch the tests.

- [ ] **Step 5: Verify**

Run: `npm run test` → 309 pass. `npm run lint` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Extract shared toast, modal, tooltip and icon components"
```

---

### Task 4: ThemeContext

**Files:**
- Create: `src/frontend/src/contexts/ThemeContext.tsx`
- Test: `src/frontend/src/contexts/ThemeContext.test.tsx`

**Interfaces:**
- Produces: `ThemeProvider`, `useTheme(): { theme: 'light'|'dark'|'system', palette: 'night'|'classic', setTheme, setPalette }`. Persists to localStorage keys `appearance_theme` / `appearance_palette`, applies `data-theme` / `data-palette` on `document.documentElement`, subscribes to `matchMedia('(prefers-color-scheme: dark)')` when theme is `system`.

- [ ] **Step 1: Write the failing test**

```tsx
// src/contexts/ThemeContext.test.tsx
import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ThemeProvider, useTheme } from './ThemeContext'

function Probe() {
  const { theme, palette, setTheme, setPalette } = useTheme()
  return (
    <div>
      <span data-testid="theme">{theme}</span>
      <span data-testid="palette">{palette}</span>
      <button onClick={() => setTheme('dark')}>dark</button>
      <button onClick={() => setPalette('classic')}>classic</button>
    </div>
  )
}

describe('ThemeContext', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.removeAttribute('data-palette')
  })

  it('defaults to system theme and night palette', () => {
    render(<ThemeProvider><Probe /></ThemeProvider>)
    expect(screen.getByTestId('theme')).toHaveTextContent('system')
    expect(screen.getByTestId('palette')).toHaveTextContent('night')
    expect(document.documentElement.getAttribute('data-palette')).toBe('night')
    // matchMedia stub matches:false → system resolves to light
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
  })

  it('setTheme("dark") applies attribute and persists', () => {
    render(<ThemeProvider><Probe /></ThemeProvider>)
    fireEvent.click(screen.getByText('dark'))
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem('appearance_theme')).toBe('dark')
  })

  it('setPalette("classic") applies attribute and persists', () => {
    render(<ThemeProvider><Probe /></ThemeProvider>)
    fireEvent.click(screen.getByText('classic'))
    expect(document.documentElement.getAttribute('data-palette')).toBe('classic')
    expect(localStorage.getItem('appearance_palette')).toBe('classic')
  })

  it('reads persisted preferences on mount', () => {
    localStorage.setItem('appearance_theme', 'dark')
    localStorage.setItem('appearance_palette', 'classic')
    render(<ThemeProvider><Probe /></ThemeProvider>)
    expect(screen.getByTestId('theme')).toHaveTextContent('dark')
    expect(screen.getByTestId('palette')).toHaveTextContent('classic')
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/contexts/ThemeContext.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```tsx
// src/contexts/ThemeContext.tsx
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

export type ThemePreference = 'light' | 'dark' | 'system'
export type Palette = 'night' | 'classic'

interface ThemeContextValue {
  theme: ThemePreference
  palette: Palette
  setTheme: (t: ThemePreference) => void
  setPalette: (p: Palette) => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

const THEME_KEY = 'appearance_theme'
const PALETTE_KEY = 'appearance_palette'

function readTheme(): ThemePreference {
  const v = localStorage.getItem(THEME_KEY)
  return v === 'light' || v === 'dark' ? v : 'system'
}

function readPalette(): Palette {
  return localStorage.getItem(PALETTE_KEY) === 'classic' ? 'classic' : 'night'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemePreference>(readTheme)
  const [palette, setPaletteState] = useState<Palette>(readPalette)

  useEffect(() => {
    function apply() {
      const dark = theme === 'dark' ||
        (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)
      document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light')
    }
    apply()
    if (theme !== 'system') return
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    mq.addEventListener('change', apply)
    return () => mq.removeEventListener('change', apply)
  }, [theme])

  useEffect(() => {
    document.documentElement.setAttribute('data-palette', palette)
  }, [palette])

  function setTheme(t: ThemePreference) {
    localStorage.setItem(THEME_KEY, t)
    setThemeState(t)
  }

  function setPalette(p: Palette) {
    localStorage.setItem(PALETTE_KEY, p)
    setPaletteState(p)
  }

  return (
    <ThemeContext.Provider value={{ theme, palette, setTheme, setPalette }}>
      {children}
    </ThemeContext.Provider>
  )
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext)
  if (!ctx) throw new Error('useTheme must be used within ThemeProvider')
  return ctx
}
```

(The matchMedia application logic mirrors `AliasesPage.jsx:1050-1061`, which is removed in Task 13.)

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/contexts/ThemeContext.test.tsx` → PASS. Then `npm run typecheck` → 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/contexts/
git commit -m "Add ThemeContext managing theme and palette preferences"
```

---

### Task 5: Account identity helper + AuthContext

**Files:**
- Create: `src/frontend/src/lib/accountIdentity.ts`
- Create: `src/frontend/src/contexts/AuthContext.tsx`
- Test: `src/frontend/src/lib/accountIdentity.test.ts`, `src/frontend/src/contexts/AuthContext.test.tsx`

**Interfaces:**
- Consumes: `api`, `hasSession`, `clearSession`, `setUnauthorizedHandler`, `setIsAdmin` from `src/api.js`.
- Produces:
  - `deriveIdentity(account: Account): AccountIdentity` with `Account = { userName?, mailbox?, fullName?, isAdmin?, domains?: {id,name}[] }` and `AccountIdentity = { email, displayName, initials, subDomains }`.
  - `AuthProvider`, `useAuth(): { isLoggedIn, isAdmin, account, accountLoaded, identity, activeAccount, accounts, syncFromSession(), logout(), refreshAccount() }`. `activeAccount = { id:'primary', email, displayName, isPrimary:true } | null`; `accounts` = `[activeAccount]` when present (structure ready for sub-project 2's linked accounts).

- [ ] **Step 1: Write the failing identity test**

```ts
// src/lib/accountIdentity.test.ts
import { describe, it, expect } from 'vitest'
import { deriveIdentity } from './accountIdentity'

describe('deriveIdentity', () => {
  const account = {
    userName: 'mick',
    mailbox: 'WSY',
    fullName: 'Mick D.',
    domains: [{ id: 'WSY', name: 'weesky.be' }, { id: 'EXT', name: 'example.org' }],
  }

  it('builds email from userName and primary domain', () => {
    expect(deriveIdentity(account).email).toBe('mick@weesky.be')
  })

  it('displayName prefers fullName, falls back to email', () => {
    expect(deriveIdentity(account).displayName).toBe('Mick D.')
    expect(deriveIdentity({ ...account, fullName: '' }).displayName).toBe('mick@weesky.be')
  })

  it('initials are first letters of user and domain, uppercased', () => {
    expect(deriveIdentity(account).initials).toBe('MW')
  })

  it('subDomains excludes the primary domain', () => {
    expect(deriveIdentity(account).subDomains).toEqual([{ id: 'EXT', name: 'example.org' }])
  })

  it('when mailbox matches no domain, all domains are subDomains', () => {
    const a = { ...account, mailbox: 'ZZZ' }
    expect(deriveIdentity(a).subDomains).toHaveLength(2)
    expect(deriveIdentity(a).email).toBe('mick@weesky.be')
  })
})
```

- [ ] **Step 2: Run to verify failure, then implement**

Run: `npx vitest run src/lib/accountIdentity.test.ts` → FAIL.

```ts
// src/lib/accountIdentity.ts
// Mirrors the identity derivation previously inlined in AliasesPage (getAccount effect).
export interface AccountDomain { id: string; name: string }

export interface Account {
  userName?: string
  mailbox?: string
  fullName?: string
  isAdmin?: boolean
  domains?: AccountDomain[]
}

export interface AccountIdentity {
  email: string
  displayName: string
  initials: string
  subDomains: AccountDomain[]
}

export function deriveIdentity(account: Account): AccountIdentity {
  const list = account.domains ?? []
  const primaryDomain = list.find(d => d.id === account.mailbox)
  const defaultDomain = primaryDomain ?? list[0]
  const domainName = defaultDomain?.name ?? ''
  const email = domainName ? `${account.userName}@${domainName}` : (account.userName ?? '')
  const initials =
    (account.userName?.[0] ?? '').toUpperCase() +
    (domainName?.[0] ?? account.mailbox?.[0] ?? '').toUpperCase()
  return {
    email,
    displayName: account.fullName || email,
    initials,
    subDomains: primaryDomain ? list.filter(d => d.id !== account.mailbox) : list,
  }
}
```

Run: `npx vitest run src/lib/accountIdentity.test.ts` → PASS.

- [ ] **Step 3: Write the failing AuthContext test**

```tsx
// src/contexts/AuthContext.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { AuthProvider, useAuth } from './AuthContext'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../api.js', () => ({
  api: { getAccount: mocks.getAccount, logout: mocks.logout },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function Probe() {
  const { isLoggedIn, isAdmin, identity, accountLoaded, logout } = useAuth()
  return (
    <div>
      <span data-testid="logged">{String(isLoggedIn)}</span>
      <span data-testid="admin">{String(isAdmin)}</span>
      <span data-testid="loaded">{String(accountLoaded)}</span>
      <span data-testid="email">{identity?.email ?? ''}</span>
      <button onClick={() => logout()}>out</button>
    </div>
  )
}

const account = {
  userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: true,
  domains: [{ id: 'WSY', name: 'weesky.be' }],
}

describe('AuthContext', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAccount.mockResolvedValue(account)
    mocks.logout.mockResolvedValue(null)
  })

  it('loads the account when a session exists', async () => {
    mocks.hasSession.mockReturnValue(true)
    render(<AuthProvider><Probe /></AuthProvider>)
    expect(screen.getByTestId('logged')).toHaveTextContent('true')
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    expect(screen.getByTestId('admin')).toHaveTextContent('true')
    expect(screen.getByTestId('email')).toHaveTextContent('mick@weesky.be')
    expect(mocks.setIsAdmin).toHaveBeenCalledWith(true)
  })

  it('does not load the account without a session', () => {
    mocks.hasSession.mockReturnValue(false)
    render(<AuthProvider><Probe /></AuthProvider>)
    expect(screen.getByTestId('logged')).toHaveTextContent('false')
    expect(mocks.getAccount).not.toHaveBeenCalled()
  })

  it('registers an unauthorized handler that logs out the UI', async () => {
    mocks.hasSession.mockReturnValue(true)
    render(<AuthProvider><Probe /></AuthProvider>)
    const handler = mocks.setUnauthorizedHandler.mock.calls[0][0]
    expect(typeof handler).toBe('function')
  })

  it('logout calls the API, clears the session, resets state', async () => {
    mocks.hasSession.mockReturnValue(true)
    render(<AuthProvider><Probe /></AuthProvider>)
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    fireEvent.click(screen.getByText('out'))
    await waitFor(() => expect(screen.getByTestId('logged')).toHaveTextContent('false'))
    expect(mocks.logout).toHaveBeenCalled()
    expect(mocks.clearSession).toHaveBeenCalled()
  })
})
```

- [ ] **Step 4: Run to verify failure, then implement**

Run: `npx vitest run src/contexts/AuthContext.test.tsx` → FAIL.

```tsx
// src/contexts/AuthContext.tsx
import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from 'react'
import { api, hasSession, clearSession, setUnauthorizedHandler, setIsAdmin } from '../api.js'
import { deriveIdentity, type Account, type AccountIdentity } from '../lib/accountIdentity'

export interface ActiveAccount {
  id: 'primary'
  email: string
  displayName: string
  isPrimary: true
}

interface AuthContextValue {
  isLoggedIn: boolean
  isAdmin: boolean
  account: Account | null
  accountLoaded: boolean
  identity: AccountIdentity | null
  /** The account whose mail context is active. Primary only until sub-project 2. */
  activeAccount: ActiveAccount | null
  /** All linked accounts. Length 1 until sub-project 2. */
  accounts: ActiveAccount[]
  /** Re-read the session flag after LoginPage completed api.login(). */
  syncFromSession: () => void
  logout: () => Promise<void>
  refreshAccount: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoggedIn, setIsLoggedIn] = useState<boolean>(hasSession())
  const [account, setAccount] = useState<Account | null>(null)
  const [accountLoaded, setAccountLoaded] = useState(false)

  const refreshAccount = useCallback(async () => {
    try {
      const data: Account = await api.getAccount()
      setAccount(data)
      setIsAdmin(data?.isAdmin === true)
    } catch {
      setAccount(null)
    } finally {
      setAccountLoaded(true)
    }
  }, [])

  useEffect(() => {
    setUnauthorizedHandler(() => {
      setIsLoggedIn(false)
      setAccount(null)
      setAccountLoaded(false)
    })
    return () => setUnauthorizedHandler(null)
  }, [])

  useEffect(() => {
    if (isLoggedIn) {
      refreshAccount()
    } else {
      setAccount(null)
      setAccountLoaded(false)
    }
  }, [isLoggedIn, refreshAccount])

  function syncFromSession() {
    setIsLoggedIn(hasSession())
  }

  async function logout() {
    try {
      await api.logout()
    } catch {
      // best effort — the cookie may already be gone
    } finally {
      clearSession()
      setIsLoggedIn(false)
    }
  }

  const identity = account ? deriveIdentity(account) : null
  const activeAccount: ActiveAccount | null = identity
    ? { id: 'primary', email: identity.email, displayName: identity.displayName, isPrimary: true }
    : null

  return (
    <AuthContext.Provider value={{
      isLoggedIn,
      isAdmin: account?.isAdmin === true,
      account,
      accountLoaded,
      identity,
      activeAccount,
      accounts: activeAccount ? [activeAccount] : [],
      syncFromSession,
      logout,
      refreshAccount,
    }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
```

Run: `npx vitest run src/contexts/AuthContext.test.tsx` → PASS. `npm run typecheck` → 0 errors. `npm run test` → full suite passes.

- [ ] **Step 5: Commit**

```bash
git add src/lib/ src/contexts/AuthContext.tsx src/contexts/AuthContext.test.tsx
git commit -m "Add AuthContext and account identity derivation"
```

---

### Task 6: Router skeleton — App, routes, guards, AppShell, rail, ComingSoon

**Files:**
- Create: `src/frontend/src/routes.tsx`, `src/frontend/src/layouts/AppShell.tsx`, `src/frontend/src/layouts/AppRail.tsx`, `src/frontend/src/layouts/RequireAuth.tsx`, `src/frontend/src/layouts/RequireAdmin.tsx`, `src/frontend/src/components/ComingSoon.tsx`, `src/frontend/src/pages/LoginRoute.tsx`
- Create: `src/frontend/src/icons/MailIcon.tsx`, `CalendarIcon.tsx`, `ContactsIcon.tsx`, `GearIcon.tsx` (simple inline SVGs, `currentColor`, 20×20 viewBox, follow the style of the existing inline icons)
- Create: `src/frontend/src/styles/shell.css`
- Rewrite: `src/frontend/src/App.jsx` → `src/frontend/src/App.tsx`
- Delete + Replace: `src/frontend/src/App.test.jsx` → `src/frontend/src/App.test.tsx` (the 6 old tests test the boolean switch, whose subject disappears; replaced by routing tests below)
- Modify: `src/frontend/src/main.tsx` (import `./styles/shell.css`, import App from `./App`)

**Interfaces:**
- Consumes: `useAuth` (Task 5), `ThemeProvider` (Task 4), `LoginPage` (existing, untouched).
- Produces: exported `routes` array (for memory-router tests) and `router`; `<ComingSoon module title? />`; shell CSS classes `.app-shell`, `.app-topbar` (placeholder div this task, filled in Task 7), `.app-rail`, `.rail-item`, `.app-content`.

Route tree (from the spec): `/login` public; everything else behind `RequireAuth` + `AppShell`; `/` → `/mail`; `/mail|/calendar|/contacts` → ComingSoon; `/settings/*` added in Task 8; `*` → `/mail`.

- [ ] **Step 1: Write the failing routing tests**

```tsx
// src/App.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { AuthProvider } from './contexts/AuthContext'
import { ThemeProvider } from './contexts/ThemeContext'
import { routes } from './routes'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
  login: vi.fn(),
  markLoggedIn: vi.fn(),
}))

vi.mock('./api.js', () => ({
  api: { getAccount: mocks.getAccount, logout: mocks.logout, login: mocks.login },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  markLoggedIn: mocks.markLoggedIn,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(
    <ThemeProvider>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </ThemeProvider>
  )
  return router
}

const account = {
  userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: false,
  domains: [{ id: 'WSY', name: 'weesky.be' }],
}

describe('routing', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAccount.mockResolvedValue(account)
  })

  it('redirects unauthenticated users to /login', async () => {
    mocks.hasSession.mockReturnValue(false)
    const router = renderAt('/mail')
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })

  it('redirects / to /mail when authenticated', async () => {
    mocks.hasSession.mockReturnValue(true)
    const router = renderAt('/')
    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(await screen.findByText(/coming soon/i)).toBeInTheDocument()
  })

  it('renders the rail with the four module links', async () => {
    mocks.hasSession.mockReturnValue(true)
    renderAt('/mail')
    expect(await screen.findByLabelText('Mail')).toBeInTheDocument()
    expect(screen.getByLabelText('Calendar')).toBeInTheDocument()
    expect(screen.getByLabelText('Contacts')).toBeInTheDocument()
    expect(screen.getByLabelText('Settings')).toBeInTheDocument()
  })

  it('unknown paths fall back to /mail', async () => {
    mocks.hasSession.mockReturnValue(true)
    const router = renderAt('/nope')
    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
  })

  it('redirects authenticated users away from /login', async () => {
    mocks.hasSession.mockReturnValue(true)
    const router = renderAt('/login')
    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/App.test.tsx` → FAIL (routes not found). Delete `src/App.test.jsx` in the same change (its subject — the boolean switch — is deleted this task; these 5 tests are the replacement).

- [ ] **Step 3: Implement the components**

```tsx
// src/layouts/RequireAuth.tsx
import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

export default function RequireAuth() {
  const { isLoggedIn } = useAuth()
  if (!isLoggedIn) return <Navigate to="/login" replace />
  return <Outlet />
}
```

```tsx
// src/layouts/RequireAdmin.tsx
import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

export default function RequireAdmin() {
  const { isAdmin, accountLoaded } = useAuth()
  if (!accountLoaded) return null // account still loading — decide once known
  if (!isAdmin) return <Navigate to="/settings/account" replace />
  return <Outlet />
}
```

```tsx
// src/components/ComingSoon.tsx
import { Link } from 'react-router-dom'

export default function ComingSoon({ module }: { module: string }) {
  return (
    <div className="coming-soon">
      <h1>{module}</h1>
      <p>This module is coming soon.</p>
      {module === 'Mail' && (
        <p className="coming-soon-links">
          In the meantime: <Link to="/settings/aliases">Aliases</Link>
          {' · '}
          <Link to="/settings/rules">Rules</Link>
        </p>
      )}
    </div>
  )
}
```

```tsx
// src/layouts/AppRail.tsx
import { NavLink } from 'react-router-dom'
import MailIcon from '../icons/MailIcon'
import CalendarIcon from '../icons/CalendarIcon'
import ContactsIcon from '../icons/ContactsIcon'
import GearIcon from '../icons/GearIcon'

const modules = [
  { to: '/mail', label: 'Mail', Icon: MailIcon },
  { to: '/calendar', label: 'Calendar', Icon: CalendarIcon },
  { to: '/contacts', label: 'Contacts', Icon: ContactsIcon },
]

function railClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'rail-item is-active' : 'rail-item'
}

export default function AppRail() {
  return (
    <nav className="app-rail" aria-label="Modules">
      {modules.map(({ to, label, Icon }) => (
        <NavLink key={to} to={to} className={railClass} aria-label={label} title={label}>
          <Icon />
        </NavLink>
      ))}
      <div className="rail-spacer" />
      <NavLink to="/settings" className={railClass} aria-label="Settings" title="Settings">
        <GearIcon />
      </NavLink>
    </nav>
  )
}
```

```tsx
// src/layouts/AppShell.tsx
import { Outlet } from 'react-router-dom'
import AppRail from './AppRail'

export default function AppShell() {
  return (
    <div className="app-shell">
      <header className="app-topbar">{/* TopBar content lands in Task 7 */}</header>
      <div className="app-shell-body">
        <AppRail />
        <main className="app-content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
```

```tsx
// src/pages/LoginRoute.tsx
import { Navigate, useNavigate } from 'react-router-dom'
import LoginPage from './LoginPage.jsx'
import { useAuth } from '../contexts/AuthContext'

export default function LoginRoute() {
  const { isLoggedIn, syncFromSession } = useAuth()
  const navigate = useNavigate()
  if (isLoggedIn) return <Navigate to="/" replace />
  return (
    <LoginPage
      onLogin={() => {
        syncFromSession()
        navigate('/', { replace: true })
      }}
    />
  )
}
```

```tsx
// src/routes.tsx
import { createBrowserRouter, Navigate, type RouteObject } from 'react-router-dom'
import RequireAuth from './layouts/RequireAuth'
import AppShell from './layouts/AppShell'
import LoginRoute from './pages/LoginRoute'
import ComingSoon from './components/ComingSoon'

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginRoute /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <Navigate to="/mail" replace /> },
          { path: 'mail', element: <ComingSoon module="Mail" /> },
          { path: 'calendar', element: <ComingSoon module="Calendar" /> },
          { path: 'contacts', element: <ComingSoon module="Contacts" /> },
          // /settings subtree lands in Task 8
          { path: '*', element: <Navigate to="/mail" replace /> },
        ],
      },
    ],
  },
]

export const router = createBrowserRouter(routes)
```

```tsx
// src/App.tsx  (delete src/App.jsx)
import { RouterProvider } from 'react-router-dom'
import { router } from './routes'
import { AuthProvider } from './contexts/AuthContext'
import { ThemeProvider } from './contexts/ThemeContext'

export default function App() {
  return (
    <ThemeProvider>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </ThemeProvider>
  )
}
```

Icons: each file exports a default function component returning an inline `<svg width="20" height="20" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">…</svg>` — envelope (Mail), calendar grid (Calendar), person silhouette (Contacts), gear (Settings). Copy the SVG authoring style of the existing inline icons in `AliasesPage.jsx` (lines 41-123).

```css
/* src/styles/shell.css — application shell layout */
.app-shell {
  display: flex;
  flex-direction: column;
  height: 100vh;
  min-width: 1024px; /* desktop-first floor; below this the page scrolls */
}

.app-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 44px;
  padding: 0 12px 0 16px;
  background: var(--topbar-bg);
  color: var(--topbar-fg);
  flex: none;
}

.app-shell-body {
  display: flex;
  flex: 1;
  min-height: 0;
}

.app-rail {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  width: 56px;
  padding: 10px 0;
  background: var(--rail-bg);
  flex: none;
}

.rail-item {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: var(--radius-md);
  color: var(--rail-fg);
}

.rail-item:hover { background: var(--rail-item); }

.rail-item.is-active {
  background: var(--rail-item-active);
  color: var(--rail-item-active-fg);
}

.rail-spacer { flex: 1; }

.app-content {
  flex: 1;
  min-width: 0;
  overflow: auto;
  background: var(--bg);
  color: var(--text);
}

.coming-soon {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 60vh;
  gap: 8px;
  color: var(--text-muted);
}

.coming-soon h1 { color: var(--text); font-size: 22px; }
.coming-soon-links a { color: var(--action-primary); }
```

Add `import './styles/shell.css'` in `main.tsx` after `index.css`, and update the App import to `./App` (extension-less).

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/App.test.tsx` → 5 PASS. `npm run test` → full suite green (308 = 309 − 6 old App tests + 5 new). `npm run typecheck`, `npm run lint` → clean.

- [ ] **Step 5: Manual smoke**

`npm run dev`: login → lands on `/mail` ComingSoon; rail navigates; unknown URL → `/mail`; direct `/mail` while logged out → `/login`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Replace boolean navigation with router, guards and app shell"
```

---

### Task 7: TopBar and AvatarMenu

**Files:**
- Create: `src/frontend/src/layouts/TopBar.tsx`, `src/frontend/src/layouts/AvatarMenu.tsx`
- Modify: `src/frontend/src/layouts/AppShell.tsx` (use TopBar), `src/frontend/src/styles/shell.css` (append styles)
- Test: `src/frontend/src/layouts/AvatarMenu.test.tsx`

**Interfaces:**
- Consumes: `useAuth()` — `identity` (`initials`, `displayName`, `email`), `accounts`, `activeAccount`, `logout`.
- Produces: `<TopBar />` (brand left — reuse `src/assets/logo_circle.jpg` + `weesky_net.png` as in the current `.site-header` of AliasesPage — avatar button right); `<AvatarMenu />` with sections: identity, accounts list (`role="menuitem"` per account, active one marked), Settings link, Sign out button.

- [ ] **Step 1: Write the failing test**

```tsx
// src/layouts/AvatarMenu.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { AuthProvider } from '../contexts/AuthContext'
import AvatarMenu from './AvatarMenu'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(() => true),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../api.js', () => ({
  api: { getAccount: mocks.getAccount, logout: mocks.logout },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderMenu() {
  return render(
    <MemoryRouter>
      <AuthProvider><AvatarMenu /></AuthProvider>
    </MemoryRouter>
  )
}

describe('AvatarMenu', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.hasSession.mockReturnValue(true)
    mocks.logout.mockResolvedValue(null)
    mocks.getAccount.mockResolvedValue({
      userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: false,
      domains: [{ id: 'WSY', name: 'weesky.be' }],
    })
  })

  it('shows the user initials on the trigger', async () => {
    renderMenu()
    expect(await screen.findByRole('button', { name: /account menu/i })).toHaveTextContent('MW')
  })

  it('opens on click, showing identity, accounts and actions', async () => {
    renderMenu()
    fireEvent.click(await screen.findByRole('button', { name: /account menu/i }))
    expect(screen.getByText('mick@weesky.be')).toBeInTheDocument()
    expect(screen.getAllByRole('menuitem').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('Settings')).toBeInTheDocument()
    expect(screen.getByText('Sign out')).toBeInTheDocument()
  })

  it('signs out via the auth context', async () => {
    renderMenu()
    fireEvent.click(await screen.findByRole('button', { name: /account menu/i }))
    fireEvent.click(screen.getByText('Sign out'))
    await waitFor(() => expect(mocks.logout).toHaveBeenCalled())
    expect(mocks.clearSession).toHaveBeenCalled()
  })

  it('closes on outside mousedown', async () => {
    renderMenu()
    fireEvent.click(await screen.findByRole('button', { name: /account menu/i }))
    expect(screen.getByText('Sign out')).toBeInTheDocument()
    fireEvent.mouseDown(document.body)
    expect(screen.queryByText('Sign out')).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run to verify failure, then implement**

Run: `npx vitest run src/layouts/AvatarMenu.test.tsx` → FAIL.

```tsx
// src/layouts/AvatarMenu.tsx
import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

export default function AvatarMenu() {
  const { identity, accounts, activeAccount, logout } = useAuth()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()

  useEffect(() => {
    if (!open) return
    function onMouseDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onMouseDown)
    return () => document.removeEventListener('mousedown', onMouseDown)
  }, [open])

  if (!identity) return null

  async function handleSignOut() {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="avatar-menu-root" ref={rootRef}>
      <button
        className="topbar-avatar"
        aria-label="Account menu"
        aria-expanded={open}
        onClick={() => setOpen(o => !o)}
      >
        {identity.initials}
      </button>
      {open && (
        <div className="avatar-menu" role="menu">
          <div className="avatar-menu-identity">
            <div className="avatar-menu-name">{identity.displayName}</div>
            <div className="avatar-menu-email">{identity.email}</div>
          </div>
          <div className="avatar-menu-accounts">
            {accounts.map(acc => (
              <div
                key={acc.id}
                role="menuitem"
                className={acc.id === activeAccount?.id ? 'avatar-menu-account is-active' : 'avatar-menu-account'}
              >
                {acc.email}
              </div>
            ))}
          </div>
          <div className="avatar-menu-actions">
            <Link to="/settings" role="menuitem" onClick={() => setOpen(false)}>Settings</Link>
            <button role="menuitem" onClick={handleSignOut}>Sign out</button>
          </div>
        </div>
      )}
    </div>
  )
}
```

```tsx
// src/layouts/TopBar.tsx
import logoCircle from '../assets/logo_circle.jpg'
import wordmark from '../assets/weesky_net.png'
import AvatarMenu from './AvatarMenu'

export default function TopBar() {
  return (
    <header className="app-topbar">
      <div className="topbar-brand">
        <img src={logoCircle} alt="" className="topbar-logo" />
        <img src={wordmark} alt="weesky.net" className="topbar-wordmark" />
      </div>
      <AvatarMenu />
    </header>
  )
}
```

Update `AppShell.tsx`: replace the placeholder `<header className="app-topbar">…</header>` with `<TopBar />` (and import it).

Append to `shell.css`:

```css
.topbar-brand { display: flex; align-items: center; gap: 10px; }
.topbar-logo { height: 26px; width: 26px; border-radius: 50%; }
.topbar-wordmark { height: 16px; }

.avatar-menu-root { position: relative; }

.topbar-avatar {
  width: 30px;
  height: 30px;
  border-radius: 50%;
  border: none;
  cursor: pointer;
  background: rgba(255, 255, 255, 0.18);
  color: var(--topbar-fg);
  font-weight: 600;
  font-size: 12px;
}

.avatar-menu {
  position: absolute;
  top: 38px;
  right: 0;
  min-width: 240px;
  background: var(--surface);
  color: var(--text);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.18);
  z-index: 50;
}

.avatar-menu-identity { padding: 12px 14px; border-bottom: 1px solid var(--border); }
.avatar-menu-name { font-weight: 600; }
.avatar-menu-email { color: var(--text-muted); font-size: 12px; }

.avatar-menu-accounts { padding: 6px 0; border-bottom: 1px solid var(--border); }
.avatar-menu-account { padding: 7px 14px; font-size: 13px; }
.avatar-menu-account.is-active { font-weight: 600; }

.avatar-menu-actions { display: flex; flex-direction: column; padding: 6px 0; }
.avatar-menu-actions a,
.avatar-menu-actions button {
  text-align: left;
  padding: 8px 14px;
  background: none;
  border: none;
  color: var(--text);
  font: inherit;
  cursor: pointer;
  text-decoration: none;
}
.avatar-menu-actions a:hover,
.avatar-menu-actions button:hover { background: var(--pane-item-hover); }
```

- [ ] **Step 3: Run tests**

Run: `npx vitest run src/layouts/AvatarMenu.test.tsx` → PASS. `npm run test`, `npm run typecheck`, `npm run lint` → clean.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Add top bar with sectioned avatar menu"
```

---

### Task 8: Settings section — layout, nav, and routes

**Files:**
- Create: `src/frontend/src/modules/settings/SettingsLayout.tsx`
- Modify: `src/frontend/src/routes.tsx` (add the `/settings` subtree), `src/frontend/src/styles/shell.css` (append)
- Test: `src/frontend/src/modules/settings/SettingsLayout.test.tsx`

**Interfaces:**
- Consumes: `useAuth()` (`isAdmin`), `RequireAdmin` (Task 6), `ComingSoon` (Task 6).
- Produces: `/settings` subtree with the context-pane nav. Routes `account`, `accounts`, `appearance`, `aliases`, `rules` render `ComingSoon` placeholders until Tasks 9-13 replace them; `admin` is wrapped in `RequireAdmin`.

- [ ] **Step 1: Write the failing test**

```tsx
// src/modules/settings/SettingsLayout.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { AuthProvider } from '../../contexts/AuthContext'
import { ThemeProvider } from '../../contexts/ThemeContext'
import { routes } from '../../routes'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(() => true),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../../api.js', () => ({
  api: { getAccount: mocks.getAccount, logout: mocks.logout },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(
    <ThemeProvider>
      <AuthProvider><RouterProvider router={router} /></AuthProvider>
    </ThemeProvider>
  )
  return router
}

const baseAccount = {
  userName: 'mick', mailbox: 'WSY', fullName: 'Mick',
  domains: [{ id: 'WSY', name: 'weesky.be' }],
}

describe('settings section', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.hasSession.mockReturnValue(true)
  })

  it('/settings redirects to /settings/account', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    const router = renderAt('/settings')
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/account'))
  })

  it('shows the nav without Administration for non-admins', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    renderAt('/settings/account')
    expect(await screen.findByText('Account')).toBeInTheDocument()
    expect(screen.getByText('Linked accounts')).toBeInTheDocument()
    expect(screen.getByText('Appearance')).toBeInTheDocument()
    expect(screen.getByText('Aliases')).toBeInTheDocument()
    expect(screen.getByText('Rules')).toBeInTheDocument()
    await waitFor(() => expect(mocks.getAccount).toHaveBeenCalled())
    expect(screen.queryByText('Administration')).not.toBeInTheDocument()
  })

  it('shows Administration for admins', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
    renderAt('/settings/account')
    expect(await screen.findByText('Administration')).toBeInTheDocument()
  })

  it('blocks /settings/admin for non-admins', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    const router = renderAt('/settings/admin')
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/account'))
  })
})
```

- [ ] **Step 2: Run to verify failure, then implement**

```tsx
// src/modules/settings/SettingsLayout.tsx
import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'

function paneClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'pane-item is-active' : 'pane-item'
}

export default function SettingsLayout() {
  const { isAdmin } = useAuth()
  return (
    <div className="settings-layout">
      <nav className="context-pane" aria-label="Settings">
        <NavLink to="/settings/account" end className={paneClass}>Account</NavLink>
        <NavLink to="/settings/accounts" className={paneClass}>Linked accounts</NavLink>
        <NavLink to="/settings/appearance" className={paneClass}>Appearance</NavLink>
        <NavLink to="/settings/aliases" className={paneClass}>Aliases</NavLink>
        <NavLink to="/settings/rules" className={paneClass}>Rules</NavLink>
        {isAdmin && <NavLink to="/settings/admin" className={paneClass}>Administration</NavLink>}
      </nav>
      <div className="settings-content">
        <Outlet />
      </div>
    </div>
  )
}
```

In `routes.tsx`, insert before the `'*'` entry (import `SettingsLayout` and `RequireAdmin`):

```tsx
{
  path: 'settings',
  element: <SettingsLayout />,
  children: [
    { index: true, element: <Navigate to="/settings/account" replace /> },
    { path: 'account', element: <ComingSoon module="Account" /> },        // Task 11
    { path: 'accounts', element: <ComingSoon module="Linked accounts" /> }, // sub-project 2
    { path: 'appearance', element: <ComingSoon module="Appearance" /> },  // Task 12
    { path: 'aliases', element: <ComingSoon module="Aliases" /> },        // Task 13
    { path: 'rules', element: <ComingSoon module="Rules" /> },            // Task 9
    {
      element: <RequireAdmin />,
      children: [{ path: 'admin', element: <ComingSoon module="Administration" /> }], // Task 10
    },
  ],
},
```

Append to `shell.css`:

```css
.settings-layout { display: flex; height: 100%; }

.context-pane {
  width: 200px;
  flex: none;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 14px 10px;
  border-right: 1px solid var(--border);
}

.pane-item {
  padding: 7px 10px;
  border-radius: var(--radius-sm);
  color: var(--text);
  text-decoration: none;
  font-size: 14px;
}

.pane-item:hover { background: var(--pane-item-hover); }

.pane-item.is-active {
  background: var(--pane-item-active-bg);
  color: var(--pane-item-active-fg);
  font-weight: 600;
}

.settings-content {
  flex: 1;
  min-width: 0;
  overflow: auto;
  padding: 24px 28px;
}
```

- [ ] **Step 3: Run tests**

Run: `npx vitest run src/modules/settings/SettingsLayout.test.tsx` → PASS. Full suite, typecheck, lint → clean.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Add settings section with context-pane navigation and admin guard"
```

---

### Task 9: Port RulesPage to /settings/rules

**Files:**
- Move: `src/frontend/src/pages/RulesPage.jsx` → `src/frontend/src/modules/settings/rules/RulesPage.jsx`
- Move: `src/frontend/src/pages/RulesPage.test.jsx` → `src/frontend/src/modules/settings/rules/RulesPage.test.jsx`
- Modify: `src/frontend/src/routes.tsx` (wire the real page), `src/frontend/src/pages/AliasesPage.jsx` (stop rendering RulesPage as a modal)

**Interfaces:**
- Consumes: shared components from Task 3 (import paths become `../../../components/...` etc.).
- Produces: `RulesPage` default export rendered as a routed page (no `onClose` prop).

- [ ] **Step 1: Move the files with `git mv`, fix import paths**

Relative imports to `api.js`, shared components/hooks/icons change from `../` to `../../../`. Run the moved test file to confirm nothing else broke: `npx vitest run src/modules/settings/rules/RulesPage.test.jsx` → tests pass (they still test the component standalone).

- [ ] **Step 2: Remove the modal contract**

Read the `RulesPage` default export's signature and root JSX. It is currently mounted as an overlay from AliasesPage (`rulesOpen && <RulesPage onClose={...} />`). Changes:
1. Remove the `onClose` prop and any close-button / overlay-dismiss UI that calls it (the settings nav is now the way out). If the root element uses overlay/modal classes, replace with a plain page container (`<div className="settings-page">`).
2. In `AliasesPage.jsx`: delete the `rulesOpen` state, the `onRules` prop pass-through to `AccountPanel`, and the conditional `<RulesPage … />` render, plus the now-unused import. (AccountPanel keeps its "Mail rules" button until Task 13 — point it at nothing? No: remove the button now since its target is gone; its replacement is the settings nav, already live.)
3. Update the moved tests: remove/adapt only the tests that exercised `onClose`/overlay behavior — their subject is deleted. Every other test stays untouched.

- [ ] **Step 3: Wire the route**

In `routes.tsx`: replace `{ path: 'rules', element: <ComingSoon module="Rules" /> }` with the real component. RulesPage is a heavy page — lazy-load it:

```tsx
import { lazy, Suspense } from 'react'
const RulesPage = lazy(() => import('./modules/settings/rules/RulesPage.jsx'))
// route:
{ path: 'rules', element: <Suspense fallback={null}><RulesPage /></Suspense> },
```

- [ ] **Step 4: Verify**

Run: `npm run test` → suite green. `npm run dev` → `/settings/rules` shows the rules manager inside the shell; create/edit/reorder still work against the dev API.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Port rules manager from modal to routed settings page"
```

---

### Task 10: Port the admin module to /settings/admin

**Files:**
- Create: `src/frontend/src/modules/settings/admin/AdminPage.jsx` (from `AdminModal`), `AccountsTab.jsx`, `DomainsTab.jsx`, `VirtualDomainsTab.jsx`, `AddEditUserModal.jsx`, `AddEditDomainModal.jsx` (all moved out of `AliasesPage.jsx`)
- Modify: `src/frontend/src/pages/AliasesPage.jsx` (remove moved components + `adminOpen` state + AccountPanel's `onAdmin` wiring)
- Move/split: the admin-component tests out of `src/frontend/src/pages/AliasesPage.admin.test.jsx` into `src/frontend/src/modules/settings/admin/AdminPage.test.jsx`
- Modify: `src/frontend/src/routes.tsx` (wire the real page, lazy-loaded like Task 9)

**Interfaces:**
- Consumes: `useToasts`/`Toasts` (Task 3), `DeleteConfirmModal`, `HelpTooltip`, `api`.
- Produces: `AdminPage` default export — a routed page with the existing tab bar (Accounts / Domains / Virtual domains). No `onClose`; it owns its toasts (`useToasts` + `<Toasts />` at page level instead of receiving `addToast` from AliasesPage).

Source locations in `AliasesPage.jsx` (line anchors): `AddEditUserModal` (444), `DOMAIN_RE` + `AddEditDomainModal` (567-569), `AccountsTab` (624), `DomainsTab` (732), `VirtualDomainsTab` (811), `ADMIN_HELP` (981), `AdminModal` (986). Icons used only by these components (`ShieldIcon` 93, `PersonPlusIcon` 102, `GlobeIcon` 114) move to `src/icons/` as separate files; icons used by remaining AliasesPage code stay put.

- [ ] **Step 1: Move components file-by-file**

One file per component, verbatim code, default + named export each. `AdminModal` becomes `AdminPage`:
- rename the function, drop the `onClose` prop and the modal wrapper markup (overlay div, close button) in favor of a page container: `<div className="settings-page admin-page">`,
- drop the `addToast` prop: call `useToasts()` inside and render `<Toasts />`,
- keep the internal tab-bar exactly as is.

- [ ] **Step 2: Split the test file**

`AliasesPage.admin.test.jsx` currently covers both AccountPanel admin visibility AND the admin components. Move every `describe` block about `AddEditUserModal`, `AddEditDomainModal`, `AccountsTab`, `DomainsTab`, `VirtualDomainsTab`, `AdminModal` into `AdminPage.test.jsx` with updated imports; adapt only what the modal→page change requires (no `onClose` prop; `AdminPage` instead of `AdminModal`). The `DeleteConfirmModal` and `AccountPanel` describes stay in the old file untouched (their turn comes in Tasks 3-done/13).

- [ ] **Step 3: Clean AliasesPage**

Delete the moved code, the `adminOpen` state, the `AdminModal` render, and the `onAdmin` prop passed to `AccountPanel` (remove the Administration button from the panel — its replacement, the guarded nav entry, is live).

- [ ] **Step 4: Wire the route (lazy) and verify**

Replace the admin ComingSoon in `routes.tsx` with lazy-loaded `AdminPage`. Run: `npm run test` → green; `npm run dev` → `/settings/admin` works for an admin account, redirects for a non-admin.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Port admin module from modal to routed settings page"
```

---

### Task 11: Account page (identity, domains, quota, password)

**Files:**
- Create: `src/frontend/src/modules/settings/account/AccountPage.tsx`, `ChangePasswordSection.tsx`
- Move: `QuotaBlock` + `QuotaMini` (AliasesPage.jsx lines 202-256) → `src/frontend/src/components/QuotaBlock.jsx` (both exports; AliasesPage imports whichever it still uses)
- Test: `src/frontend/src/modules/settings/account/AccountPage.test.tsx`
- Modify: `src/frontend/src/routes.tsx` (wire the real page)
- Modify: `src/frontend/src/pages/AliasesPage.main.test.jsx` (retarget `QuotaBlock` import; delete the `ChangePasswordModal` describe — replaced here)

**Interfaces:**
- Consumes: `useAuth()` (`identity`, `account`, `refreshAccount`), `api.getQuota`, `api.changeFullName`, `api.changePassword(oldPassword, newPassword)`, `QuotaBlock`, `useToasts`/`Toasts`.
- Produces: `/settings/account` page: full-name inline edit, primary email (read-only), other domains list, quota bar, change-password section. Replaces `ChangePasswordModal` and the informational parts of `AccountPanel`.

Password rules (from the existing modal — verify while reading it): old password required, new password min 10 chars, confirmation must match; api errors surfaced.

- [ ] **Step 1: Write the failing tests**

```tsx
// src/modules/settings/account/AccountPage.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { AuthProvider } from '../../../contexts/AuthContext'
import AccountPage from './AccountPage'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  getQuota: vi.fn(),
  changeFullName: vi.fn(),
  changePassword: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(() => true),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../../../api.js', () => ({
  api: {
    getAccount: mocks.getAccount,
    getQuota: mocks.getQuota,
    changeFullName: mocks.changeFullName,
    changePassword: mocks.changePassword,
    logout: mocks.logout,
  },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderPage() {
  return render(
    <MemoryRouter>
      <AuthProvider><AccountPage /></AuthProvider>
    </MemoryRouter>
  )
}

describe('AccountPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.hasSession.mockReturnValue(true)
    mocks.getAccount.mockResolvedValue({
      userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: false,
      domains: [{ id: 'WSY', name: 'weesky.be' }, { id: 'EXT', name: 'example.org' }],
    })
    mocks.getQuota.mockResolvedValue({ used: 1024, limit: 10240 })
  })

  it('shows identity, other domains and quota', async () => {
    renderPage()
    expect(await screen.findByText('mick@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('example.org')).toBeInTheDocument()
    await waitFor(() => expect(mocks.getQuota).toHaveBeenCalled())
  })

  it('saves an edited full name', async () => {
    mocks.changeFullName.mockResolvedValue(null)
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: /edit name/i }))
    const input = screen.getByDisplayValue('Mick')
    fireEvent.change(input, { target: { value: 'Mick D.' } })
    fireEvent.click(screen.getByRole('button', { name: /save/i }))
    await waitFor(() => expect(mocks.changeFullName).toHaveBeenCalledWith('Mick D.'))
  })

  it('rejects a new password shorter than 10 characters', async () => {
    renderPage()
    fireEvent.change(await screen.findByLabelText(/current password/i), { target: { value: 'old-pass-123' } })
    fireEvent.change(screen.getByLabelText(/^new password/i), { target: { value: 'short' } })
    fireEvent.change(screen.getByLabelText(/confirm/i), { target: { value: 'short' } })
    fireEvent.click(screen.getByRole('button', { name: /change password/i }))
    expect(await screen.findByText(/at least 10 characters/i)).toBeInTheDocument()
    expect(mocks.changePassword).not.toHaveBeenCalled()
  })

  it('rejects mismatched confirmation', async () => {
    renderPage()
    fireEvent.change(await screen.findByLabelText(/current password/i), { target: { value: 'old-pass-123' } })
    fireEvent.change(screen.getByLabelText(/^new password/i), { target: { value: 'long-enough-pw' } })
    fireEvent.change(screen.getByLabelText(/confirm/i), { target: { value: 'different-pw-99' } })
    fireEvent.click(screen.getByRole('button', { name: /change password/i }))
    expect(await screen.findByText(/do not match/i)).toBeInTheDocument()
    expect(mocks.changePassword).not.toHaveBeenCalled()
  })

  it('submits a valid password change', async () => {
    mocks.changePassword.mockResolvedValue(null)
    renderPage()
    fireEvent.change(await screen.findByLabelText(/current password/i), { target: { value: 'old-pass-123' } })
    fireEvent.change(screen.getByLabelText(/^new password/i), { target: { value: 'long-enough-pw' } })
    fireEvent.change(screen.getByLabelText(/confirm/i), { target: { value: 'long-enough-pw' } })
    fireEvent.click(screen.getByRole('button', { name: /change password/i }))
    await waitFor(() =>
      expect(mocks.changePassword).toHaveBeenCalledWith('old-pass-123', 'long-enough-pw'))
  })
})
```

Adjust the quota mock shape to whatever `QuotaBlock` actually consumes (read it first — the existing `QuotaBlock` tests in `AliasesPage.main.test.jsx` show the shape).

- [ ] **Step 2: Run to verify failure, then implement**

`AccountPage.tsx` structure (complete the JSX following existing page styling conventions — `.settings-page`, section headers like the panel's `.panel-quota-label` style):

```tsx
// src/modules/settings/account/AccountPage.tsx
import { useEffect, useState } from 'react'
import { useAuth } from '../../../contexts/AuthContext'
import { api } from '../../../api.js'
import QuotaBlock from '../../../components/QuotaBlock.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import Toasts from '../../../components/Toasts.jsx'
import ChangePasswordSection from './ChangePasswordSection'

export default function AccountPage() {
  const { identity, refreshAccount } = useAuth()
  const { toasts, addToast, removeToast } = useToasts() // match the hook's real API
  const [quota, setQuota] = useState(null)
  const [editingName, setEditingName] = useState(false)
  const [nameValue, setNameValue] = useState('')

  useEffect(() => {
    api.getQuota().then(setQuota).catch(() => {})
  }, [])

  async function saveName() {
    try {
      await api.changeFullName(nameValue)
      await refreshAccount()
      setEditingName(false)
      addToast('Name updated')
    } catch {
      addToast('Failed to update name')
    }
  }

  if (!identity) return null

  return (
    <div className="settings-page account-page">
      <h1>Account</h1>

      <section className="account-section">
        <h2>Identity</h2>
        {/* full name display + "Edit name" button; when editing: input + Save/Cancel
            (port the inline-edit interaction from AccountPanel, AliasesPage.jsx:257+) */}
        {/* primary email, read-only: {identity.email} */}
      </section>

      {identity.subDomains.length > 0 && (
        <section className="account-section">
          <h2>Other domains</h2>
          <ul>{identity.subDomains.map(d => <li key={d.id}>{d.name}</li>)}</ul>
        </section>
      )}

      <section className="account-section">
        <h2>Storage</h2>
        <QuotaBlock quota={quota} />
      </section>

      <section className="account-section">
        <h2>Password</h2>
        <ChangePasswordSection onDone={() => addToast('Password changed')} />
      </section>

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
```

`ChangePasswordSection.tsx`: port the form logic of `ChangePasswordModal` (AliasesPage.jsx:134-201) — three labeled inputs (`Current password`, `New password`, `Confirm new password`), client-side validation messages exactly as tested above (`at least 10 characters`, `do not match`), submit button `Change password`, `api.changePassword(oldPassword, newPassword)`, clear fields on success, surface API errors.

Move `QuotaBlock`/`QuotaMini` to `src/components/QuotaBlock.jsx` (verbatim, both exported; default export = `QuotaBlock`), update AliasesPage import and `AliasesPage.main.test.jsx` import. Delete the `ChangePasswordModal` describe from `AliasesPage.main.test.jsx` and the component from `AliasesPage.jsx` **in this task** (its replacement tests land here); also remove the `changePasswordOpen` state and the panel's "Change password" button wiring.

Add minimal CSS to `shell.css`:

```css
.settings-page h1 { font-size: 20px; margin-bottom: 18px; }
.account-section { margin-bottom: 26px; max-width: 560px; }
.account-section h2 {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
  margin-bottom: 8px;
}
```

- [ ] **Step 3: Wire the route and verify**

Replace the account ComingSoon in `routes.tsx` with `AccountPage` (direct import — it's light). Run: `npx vitest run src/modules/settings/account/AccountPage.test.tsx` → PASS; full suite green; typecheck/lint clean. `npm run dev` → name edit, quota, password change work.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Add account settings page replacing password modal and panel info"
```

---

### Task 12: Appearance page

**Files:**
- Create: `src/frontend/src/modules/settings/appearance/AppearancePage.tsx`
- Test: `src/frontend/src/modules/settings/appearance/AppearancePage.test.tsx`
- Modify: `src/frontend/src/routes.tsx` (wire the real page)

**Interfaces:**
- Consumes: `useTheme()` (Task 4).
- Produces: `/settings/appearance` — radio group Theme (Light / Dark / System), radio group Palette (Night & coral / Classic).

- [ ] **Step 1: Write the failing test**

```tsx
// src/modules/settings/appearance/AppearancePage.test.tsx
import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ThemeProvider } from '../../../contexts/ThemeContext'
import AppearancePage from './AppearancePage'

describe('AppearancePage', () => {
  beforeEach(() => localStorage.clear())

  it('reflects current preferences', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)
    expect(screen.getByLabelText('System')).toBeChecked()
    expect(screen.getByLabelText(/Night & coral/)).toBeChecked()
  })

  it('changes the theme', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)
    fireEvent.click(screen.getByLabelText('Dark'))
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem('appearance_theme')).toBe('dark')
  })

  it('changes the palette', () => {
    render(<ThemeProvider><AppearancePage /></ThemeProvider>)
    fireEvent.click(screen.getByLabelText('Classic'))
    expect(document.documentElement.getAttribute('data-palette')).toBe('classic')
    expect(localStorage.getItem('appearance_palette')).toBe('classic')
  })
})
```

- [ ] **Step 2: Run to verify failure, then implement**

```tsx
// src/modules/settings/appearance/AppearancePage.tsx
import { useTheme, type ThemePreference, type Palette } from '../../../contexts/ThemeContext'

const THEMES: { value: ThemePreference; label: string }[] = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
]

const PALETTES: { value: Palette; label: string }[] = [
  { value: 'night', label: 'Night & coral (default)' },
  { value: 'classic', label: 'Classic' },
]

export default function AppearancePage() {
  const { theme, setTheme, palette, setPalette } = useTheme()
  return (
    <div className="settings-page">
      <h1>Appearance</h1>

      <section className="account-section">
        <h2>Theme</h2>
        {THEMES.map(({ value, label }) => (
          <label key={value} className="radio-row">
            <input
              type="radio"
              name="theme"
              checked={theme === value}
              onChange={() => setTheme(value)}
            />
            {label}
          </label>
        ))}
      </section>

      <section className="account-section">
        <h2>Palette</h2>
        {PALETTES.map(({ value, label }) => (
          <label key={value} className="radio-row">
            <input
              type="radio"
              name="palette"
              checked={palette === value}
              onChange={() => setPalette(value)}
            />
            {label}
          </label>
        ))}
      </section>
    </div>
  )
}
```

Add to `shell.css`: `.radio-row { display: flex; align-items: center; gap: 8px; padding: 4px 0; cursor: pointer; }`.

- [ ] **Step 3: Wire the route, run tests, commit**

Replace the appearance ComingSoon in `routes.tsx`. Run: page tests, full suite, typecheck, lint → clean.

```bash
git add -A
git commit -m "Add appearance settings page for theme and palette"
```

---

### Task 13: Slim AliasesPage and retire AccountPanel

**Files:**
- Move: `src/frontend/src/pages/AliasesPage.jsx` → `src/frontend/src/modules/settings/aliases/AliasesPage.jsx` (after slimming)
- Move/trim: `src/frontend/src/pages/AliasesPage.main.test.jsx` → `src/frontend/src/modules/settings/aliases/AliasesPage.test.jsx`; delete `src/frontend/src/pages/AliasesPage.admin.test.jsx` (whatever remains of it)
- Modify: `src/frontend/src/routes.tsx` (wire the real page, lazy-loaded)

**Interfaces:**
- Consumes: shared components (Task 3), `QuotaBlock` no longer needed here (Task 11), `api`.
- Produces: a pure aliases-management page: domain selector, search/create input, alias tiles, flat/alphabetical modes, **local** "Alphabetical" toggle control.

What gets **deleted** from AliasesPage (each subject now lives elsewhere or is retired):

| Deleted | Replacement (already live) |
|---|---|
| `.site-header` banner + avatar button | `TopBar` (Task 7) |
| `AccountPanel` (line 257) | `AvatarMenu` + `/settings/account` |
| `ChangePasswordModal` + state | `ChangePasswordSection` (Task 11) |
| theme state + `data-theme` effect (1048-1061, 1068-1071) | `ThemeContext` (Task 4) + `/settings/appearance` |
| identity states: `initials`, `fullName`, `primaryEmail`, `subDomains`, `greeting` | `AuthContext.identity` |
| `LockIcon`, `CheckIcon`, `XIcon` (if only used by deleted code) | gone with their consumers |
| `onLogout` prop | `AvatarMenu` sign out |

What **stays**: alias list/CRUD, domain select (`api.getAccount` still fetched here for the domains list — keep it, it's the page's own data need), search-as-create, alpha mode with letter rail, `alias_alpha_mode` persistence, toasts, delete confirm.

- [ ] **Step 1: Add the local alphabetical toggle**

The toggle was in AccountPanel; the page keeps `alphaMode` state + `handleAlphaModeChange` (1063-1066). Add a small control in the page toolbar (near the domain selector): a labeled switch `Alphabetical`, using the existing `.switch` styles from the panel (check `index.css` for the class names the panel toggle used and reuse them).

- [ ] **Step 2: Delete the retired code, move the file**

Do the deletion in place first, run the remaining tests, then `git mv` both files and fix import paths (`../../../`). Trim the test file: delete the `AccountPanel` describes; keep and adapt the `AliasesPage` default-export describes (they may need `MemoryRouter` wrapping if the page still renders links; remove assertions about the header/panel; add a test that the alphabetical toggle is on the page and persists to `localStorage.alias_alpha_mode`).

Example new test to add:

```jsx
it('persists the alphabetical toggle locally', async () => {
  render(<MemoryRouter><AliasesPage /></MemoryRouter>)
  const toggle = await screen.findByRole('checkbox', { name: /alphabetical/i })
  fireEvent.click(toggle)
  expect(localStorage.getItem('alias_alpha_mode')).toBe('true')
})
```

- [ ] **Step 3: Wire the route, delete leftovers**

Lazy-load AliasesPage at `/settings/aliases` in `routes.tsx` (same pattern as Task 9). Delete `src/pages/AliasesPage.jsx` remnants; `src/pages/` should now contain only `LoginPage.jsx`, `LoginPage.test.jsx`, `LoginRoute.tsx`.

- [ ] **Step 4: Verify hard**

Run: `npm run test` → green; `npm run test:coverage` → review: every new file covered, overall line coverage ≥ 95%. `npm run typecheck` && `npm run lint` && `npm run build` → clean. `npm run dev` → full manual pass: login → /mail → aliases CRUD in both display modes → rules → admin → account (name, password) → appearance → sign out.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Slim aliases page into settings module, retire AccountPanel"
```

---

### Task 14: Documentation and final verification

**Files:**
- Rewrite: `src/frontend/CLAUDE.md`
- Modify: `src/snoopy.microservice/CLAUDE.md` (doc-only)
- Modify: `src/README.md` if it describes the frontend structure (check)

**Interfaces:** none — documentation.

- [ ] **Step 1: Rewrite `src/frontend/CLAUDE.md`**

Replace the stale document. Required content (write it against the final code, not from memory):
- Commands (unchanged) + new `typecheck` script.
- Architecture: router + route table, `AppShell`/`TopBar`/`AvatarMenu`/`AppRail`, `SettingsLayout`, module directories, lazy-loaded heavy pages.
- Auth: cookie-based session (NOT localStorage bearer token — fix the stale "Token persistence" section), `AuthContext` (`syncFromSession`, `logout`, `identity`, `activeAccount` prepared for multi-account), 401 → login redirect.
- Theming: token contract location, palette/theme attributes, blocking script, `ThemeContext`, the night `--action-primary` hue-shift note.
- Testing: file layout, the removal of the "named export for tests" convention, `no test lost without a replacement` rule.
- Correct component names (`VirtualDomainsTab`, not `OwnershipTab`).

- [ ] **Step 2: Fix `src/snoopy.microservice/CLAUDE.md`**

Replace the `/api/Admin/ownerships` route documentation with the real routes (`GET /api/Admin/domains/virtuals`, `PUT /api/Admin/domains/virtuals/{domainId}`, `DELETE /api/Admin/domains/virtuals/{domainId}/{userId}`) and add `POST /api/Account/FullName` to the Account endpoints list. Touch nothing else.

- [ ] **Step 3: Full verification pass (spec § 10)**

1. `npm run lint` → 0 errors.
2. `npm run test` → all green; confirm no orphaned test files under `src/pages/` except LoginPage's.
3. `npm run test:coverage` → line coverage ≥ 95%, every new file covered.
4. `npm run build` → clean.
5. Manual: all 4 theme combinations (night/classic × light/dark) on every route — hunting hard-coded colors. Check via `/settings/appearance` toggles.
6. Navigation: deep-link every route by URL; back button; `/` → `/mail`; `/settings` → `/settings/account`; `/settings/admin` as non-admin → redirected.
7. Session: expire the cookie (or 401 via devtools) on any route → back at `/login`.
8. Resize to 1024px → no overlap, nothing unreachable.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Update frontend and backend docs for the shell architecture"
```

---

## Post-plan notes for the executor

- **Read before you cut:** Tasks 9-13 move code you have not seen in full. Always read the component you're about to move, run its tests before and after, and prefer `git mv` so history follows.
- The `.env.dev` / `--mode dev` quirk and the legacy `npm run deploy` script are known issues, out of scope here.
- CI (`.github/workflows/deploy.yml`) runs lint + test + build on `src/frontend/**` changes — it picks up `tsc` via the build script automatically; no workflow change needed.
- If a moved component's two copies (Task 3) turn out to differ, stop and reconcile consciously — do not pick one blindly.
