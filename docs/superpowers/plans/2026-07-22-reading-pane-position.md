# Reading Pane Position Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A settings-backed choice of where the message reader sits: beside the list (`right`, today's layout), below it (`bottom`), or replacing it (`none`) — with a draggable splitter in both split modes.

**Architecture:** One new backend registry entry (`mail.readingPane`). On the frontend, `MailLayout` reads the preference and renders one of three arrangements from the same search-param state; a shared `PaneSplitter` + `usePaneSize` pair handles resizing and per-device persistence; `MessageList` gains a single-line `wide` row skin; `MessageReader` gains an optional `onBack`; `GeneralPage` gains a radio-card control.

**Tech Stack:** React 18 + TypeScript (frontend, Vitest + Testing Library), ASP.NET Core .NET 10 (backend, xUnit).

**Spec:** `docs/superpowers/specs/2026-07-22-reading-pane-position-design.md`

## Global Constraints

- Backend tests: always `dotnet test` (never `--no-build`) when new test files exist. Run from `src/snoopy.microservice`.
- Frontend commands run from `src/frontend`: `npm test`, `npm run lint`, `npm run typecheck`.
- **No literal colors in components or mail.css** — role tokens only (`var(--...)`). No new tokens are needed.
- UI copy is English; commit messages concise (two lines max).
- Comments only where the code cannot say it; 3 lines max.
- No existing test may be removed without an equivalent replacement.
- The preference default is `right`; allowed values exactly `right`, `bottom`, `none`.
- localStorage keys: `mail.split.right` (list width px, default 380, min 240), `mail.split.bottom` (list height px, default 280, min 120). Reader keeps ≥ 320px width / ≥ 160px height.

---

### Task 1: Backend registry entry

**Files:**
- Modify: `src/snoopy.microservice/Models/UserPreferences.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Produces: `UserPreferences.MailReadingPane == "mail.readingPane"`, default `"right"`, allowed `["right", "bottom", "none"]`. Served by the existing `GET /api/Preferences` with no controller change.

- [ ] **Step 1: Write the failing tests**

In `UserPreferencesTests.cs`, extend three existing members (do not add new test methods elsewhere):

Add to `All_CarriesTheKeysTheClientOffers`:

```csharp
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailReadingPane);
```

Add to the `Default_IsTheValueAnAccountWithNoRowsGets` theory:

```csharp
    [InlineData(UserPreferences.MailReadingPane, "right")]
```

Add to the `IsValid_AcceptsOnlyTheOfferedValues` theory:

```csharp
    [InlineData(UserPreferences.MailReadingPane, "right", true)]
    [InlineData(UserPreferences.MailReadingPane, "bottom", true)]
    [InlineData(UserPreferences.MailReadingPane, "none", true)]
    [InlineData(UserPreferences.MailReadingPane, "left", false)]
    [InlineData(UserPreferences.MailReadingPane, "Right", false)]  // the value is a symbol, not prose
```

- [ ] **Step 2: Run tests to verify they fail**

Run (from `src/snoopy.microservice`): `dotnet test --filter UserPreferencesTests`
Expected: compile error — `MailReadingPane` does not exist. That is the failure.

- [ ] **Step 3: Implement the registry entry**

In `Models/UserPreferences.cs`, add the constant after `MailShowSpamScore`:

```csharp
    public const string MailReadingPane = "mail.readingPane";
```

Add the definition at the end of the `All` collection:

```csharp
        new(MailReadingPane, "right", ["right", "bottom", "none"]),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter UserPreferencesTests`
Expected: PASS (the `Default_IsItselfAValueTheRegistryAccepts` member-data theory picks up the new key automatically).

- [ ] **Step 5: Run the full backend suite**

Run: `dotnet test`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/snoopy.microservice/Models/UserPreferences.cs src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs
git commit -m "Add mail.readingPane preference (right/bottom/none)"
```

---

### Task 2: `readingPaneOf` accessor

**Files:**
- Modify: `src/frontend/src/hooks/usePreferences.ts`
- Test: `src/frontend/src/hooks/usePreferences.test.tsx`

**Interfaces:**
- Produces: `PREFERENCE_KEYS.readingPane === 'mail.readingPane'`; `export type ReadingPane = 'right' | 'bottom' | 'none'`; `readingPaneOf(preferences: Preferences): ReadingPane` — falls back to `'right'` on anything unexpected. Tasks 6 and 8 consume both.

- [ ] **Step 1: Write the failing test**

In `usePreferences.test.tsx`, import `readingPaneOf` alongside the other accessors, and add to the `describe('the accessors')` block:

```tsx
  // The backend registry only serves the three values, but this layer must not trust that:
  // an older API answers nothing for the key, and nothing must mean today's layout.
  it.each([
    ['right', 'right'],
    ['bottom', 'bottom'],
    ['none', 'none'],
    ['sideways', 'right'],
    [undefined, 'right'],
  ])('reads readingPane %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.readingPane]: stored }

    expect(readingPaneOf(preferences)).toBe(expected)
  })
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `src/frontend`): `npm test -- src/hooks/usePreferences.test.tsx`
Expected: FAIL — `readingPaneOf` is not exported.

- [ ] **Step 3: Implement the accessor**

In `usePreferences.ts`, add to `PREFERENCE_KEYS`:

```ts
  readingPane: 'mail.readingPane',
```

Add after `showSpamScoreOf`:

```ts
export type ReadingPane = 'right' | 'bottom' | 'none'

/** Falls back to 'right' — today's layout — for an absent key or a value this build ignores. */
export function readingPaneOf(preferences: Preferences): ReadingPane {
  const stored = preferences[PREFERENCE_KEYS.readingPane]
  return stored === 'bottom' || stored === 'none' ? stored : 'right'
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- src/hooks/usePreferences.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/hooks/usePreferences.ts src/hooks/usePreferences.test.tsx
git commit -m "Read mail.readingPane with a defensive right fallback"
```

---

### Task 3: `usePaneSize` — stored splitter size

**Files:**
- Create: `src/frontend/src/modules/mail/split/usePaneSize.ts`
- Test: `src/frontend/src/modules/mail/split/usePaneSize.test.tsx`

**Interfaces:**
- Produces: `usePaneSize(storageKey: string, defaultSize: number, min: number): [number, (next: number) => void]`. The setter floors at `min`, rounds, and persists to localStorage. Garbage or sub-minimum stored values answer `defaultSize`. Task 6 consumes it.

- [ ] **Step 1: Write the failing test**

Create `usePaneSize.test.tsx`:

```tsx
import { describe, it, expect, beforeEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { usePaneSize } from './usePaneSize'

describe('usePaneSize', () => {
  beforeEach(() => localStorage.clear())

  it('starts at the default when nothing is stored', () => {
    const { result } = renderHook(() => usePaneSize('mail.split.test', 380, 240))

    expect(result.current[0]).toBe(380)
  })

  it('reads a stored size back', () => {
    localStorage.setItem('mail.split.test', '412')

    const { result } = renderHook(() => usePaneSize('mail.split.test', 380, 240))

    expect(result.current[0]).toBe(412)
  })

  // localStorage outlives the code that wrote it: garbage, or a size below the floor,
  // must answer the default rather than a crushed pane.
  it.each(['garbage', '100', '-5', ''])('falls back to the default for stored %s', stored => {
    localStorage.setItem('mail.split.test', stored)

    const { result } = renderHook(() => usePaneSize('mail.split.test', 380, 240))

    expect(result.current[0]).toBe(380)
  })

  it('persists what the setter is given, floored at the minimum', () => {
    const { result } = renderHook(() => usePaneSize('mail.split.test', 380, 240))

    act(() => result.current[1](500.4))
    expect(result.current[0]).toBe(500)
    expect(localStorage.getItem('mail.split.test')).toBe('500')

    act(() => result.current[1](50))
    expect(result.current[0]).toBe(240)
    expect(localStorage.getItem('mail.split.test')).toBe('240')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- src/modules/mail/split/usePaneSize.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement the hook**

Create `usePaneSize.ts`:

```ts
import { useCallback, useState } from 'react'

/**
 * A pane size persisted per device — a 4K screen and a laptop have different ideal splits,
 * which is why this is localStorage and not a backend preference.
 */
export function usePaneSize(
  storageKey: string, defaultSize: number, min: number,
): [number, (next: number) => void] {
  const [size, setSize] = useState(() => {
    const stored = Number(localStorage.getItem(storageKey))
    return Number.isFinite(stored) && stored >= min ? Math.round(stored) : defaultSize
  })

  const update = useCallback((next: number) => {
    const clamped = Math.max(min, Math.round(next))
    setSize(clamped)
    localStorage.setItem(storageKey, String(clamped))
  }, [storageKey, min])

  return [size, update]
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- src/modules/mail/split/usePaneSize.test.tsx`
Expected: PASS. (Note: `Number('')` is `0`, below min → default; `Number(null)` for a missing key is `0` too — same path.)

- [ ] **Step 5: Commit**

```bash
git add src/modules/mail/split/
git commit -m "Store splitter sizes per device"
```

---

### Task 4: `PaneSplitter` component

**Files:**
- Create: `src/frontend/src/modules/mail/split/PaneSplitter.tsx`
- Test: `src/frontend/src/modules/mail/split/PaneSplitter.test.tsx`
- Modify: `src/frontend/src/styles/mail.css` (append splitter rules)

**Interfaces:**
- Consumes: nothing from other tasks (size state is handed in).
- Produces:

```ts
interface PaneSplitterProps {
  orientation: 'vertical' | 'horizontal'
  size: number          // current size of the pane BEFORE the splitter (list width or height)
  defaultSize: number   // double-click / reset target
  min: number           // floor for that pane
  reserve: number       // space the pane AFTER the splitter keeps (drag ceiling = parent span − reserve)
  onResize: (size: number) => void
}
export default function PaneSplitter(props: PaneSplitterProps)
```

  Task 6 consumes it. Rendered element: `role="separator"`, `aria-orientation` = the `orientation` prop, `aria-label="Resize the panes"`, `tabIndex 0`.

- [ ] **Step 1: Write the failing test**

Create `PaneSplitter.test.tsx`:

```tsx
import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import PaneSplitter from './PaneSplitter'

function renderSplitter(overrides: Partial<Parameters<typeof PaneSplitter>[0]> = {}) {
  const onResize = vi.fn()
  render(
    <div>
      <PaneSplitter
        orientation="vertical" size={380} defaultSize={380} min={240} reserve={320}
        onResize={onResize} {...overrides}
      />
    </div>,
  )
  return { onResize, separator: screen.getByRole('separator') }
}

describe('PaneSplitter', () => {
  it('is an accessible separator carrying its orientation', () => {
    const { separator } = renderSplitter({ orientation: 'horizontal' })

    expect(separator).toHaveAttribute('aria-orientation', 'horizontal')
    expect(separator).toHaveAttribute('tabindex', '0')
  })

  it('drags along its axis', () => {
    const { onResize, separator } = renderSplitter()

    fireEvent.pointerDown(separator, { clientX: 400, clientY: 10 })
    fireEvent.pointerMove(window, { clientX: 460, clientY: 10 })
    expect(onResize).toHaveBeenLastCalledWith(440)

    fireEvent.pointerUp(window)
    fireEvent.pointerMove(window, { clientX: 500, clientY: 10 })
    expect(onResize).toHaveBeenCalledTimes(1)  // released — later moves are not drags
  })

  it('never drags below the minimum', () => {
    const { onResize, separator } = renderSplitter()

    fireEvent.pointerDown(separator, { clientX: 400 })
    fireEvent.pointerMove(window, { clientX: 100 })

    expect(onResize).toHaveBeenLastCalledWith(240)
  })

  it('nudges with the arrow keys, clamped at the minimum', () => {
    const { onResize, separator } = renderSplitter({ size: 250 })

    fireEvent.keyDown(separator, { key: 'ArrowRight' })
    expect(onResize).toHaveBeenLastCalledWith(266)

    fireEvent.keyDown(separator, { key: 'ArrowLeft' })
    expect(onResize).toHaveBeenLastCalledWith(240)  // 250 − 16 floors at min
  })

  it('nudges vertically when horizontal', () => {
    const { onResize } = renderSplitter({ orientation: 'horizontal', size: 280, min: 120 })

    fireEvent.keyDown(screen.getByRole('separator'), { key: 'ArrowDown' })
    expect(onResize).toHaveBeenLastCalledWith(296)
  })

  it('resets to the default on double-click', () => {
    const { onResize, separator } = renderSplitter({ size: 500 })

    fireEvent.doubleClick(separator)

    expect(onResize).toHaveBeenCalledWith(380)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- src/modules/mail/split/PaneSplitter.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement the component**

Create `PaneSplitter.tsx`:

```tsx
interface PaneSplitterProps {
  orientation: 'vertical' | 'horizontal'
  size: number
  defaultSize: number
  min: number
  /** What the other pane keeps: the drag ceiling is the parent's span minus this. */
  reserve: number
  onResize: (size: number) => void
}

const NUDGE = 16

/**
 * The draggable bar between two panes. It owns no size — the parent hands one in and hears
 * about changes — so the same bar serves the vertical and horizontal splits.
 */
export default function PaneSplitter(
  { orientation, size, defaultSize, min, reserve, onResize }: PaneSplitterProps,
) {
  const vertical = orientation === 'vertical'

  function startDrag(event: React.PointerEvent<HTMLDivElement>) {
    event.preventDefault()
    const parent = event.currentTarget.parentElement
    const span = vertical ? parent?.clientWidth : parent?.clientHeight
    // jsdom and a not-yet-laid-out parent both answer 0: no ceiling rather than a crushed pane.
    const limit = span ? Math.max(min, span - reserve) : Number.POSITIVE_INFINITY
    const origin = vertical ? event.clientX : event.clientY
    const base = size

    function move(moveEvent: PointerEvent) {
      const at = vertical ? moveEvent.clientX : moveEvent.clientY
      onResize(Math.min(limit, Math.max(min, base + (at - origin))))
    }
    function stop() {
      window.removeEventListener('pointermove', move)
      window.removeEventListener('pointerup', stop)
    }
    window.addEventListener('pointermove', move)
    window.addEventListener('pointerup', stop)
  }

  function nudge(event: React.KeyboardEvent) {
    const grow = vertical ? 'ArrowRight' : 'ArrowDown'
    const shrink = vertical ? 'ArrowLeft' : 'ArrowUp'
    if (event.key !== grow && event.key !== shrink) return

    event.preventDefault()
    onResize(Math.max(min, size + (event.key === grow ? NUDGE : -NUDGE)))
  }

  return (
    <div
      role="separator"
      aria-orientation={orientation}
      aria-label="Resize the panes"
      tabIndex={0}
      className={`pane-splitter is-${orientation}`}
      onPointerDown={startDrag}
      onKeyDown={nudge}
      onDoubleClick={() => onResize(defaultSize)}
    />
  )
}
```

Append to `src/frontend/src/styles/mail.css`:

```css
/* ── Pane splitter ───────────────────────────────────────── */

/* The bar carries the panes' separation — the old fixed border-right is gone with it. */
.pane-splitter {
  flex: none;
  background: var(--border);
  touch-action: none;
}

.pane-splitter.is-vertical { width: 5px; cursor: col-resize; }
.pane-splitter.is-horizontal { height: 5px; cursor: row-resize; }

.pane-splitter:hover,
.pane-splitter:focus-visible { background: var(--accent-unread); outline: none; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- src/modules/mail/split/PaneSplitter.test.tsx`
Expected: PASS. If `pointerMove` does not deliver `clientX` in this jsdom, switch the two drag tests to dispatch `new MouseEvent('pointermove', { clientX: … })` on `window` inside `act` — the handler only reads `clientX`/`clientY`.

- [ ] **Step 5: Commit**

```bash
git add src/modules/mail/split/PaneSplitter.tsx src/modules/mail/split/PaneSplitter.test.tsx src/styles/mail.css
git commit -m "Shared draggable splitter for the mail panes"
```

---

### Task 5: Wide single-line rows in `MessageList`

**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx`
- Modify: `src/frontend/src/styles/mail.css` (append wide-row rules)
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx`

**Interfaces:**
- Consumes: nothing new.
- Produces: `MessageList` accepts an optional `wide?: boolean` prop (default false → today's stacked rows, unchanged markup). Task 6 consumes it.

- [ ] **Step 1: Write the failing tests**

`MessageList.test.tsx` already renders the component with mocked messages — follow its existing render helper (it mocks `../../api.js` and preferences the same way `GeneralPage.test.tsx` does; reuse whatever helper the file defines rather than inventing a new harness). Add a describe block:

```tsx
describe('wide rows', () => {
  // One line per message: sender · subject — preview · date. The stacked markup must not
  // leak in — a .message-row-top inside a line row would stack the line again.
  it('renders a single-line row with the preview inline', async () => {
    renderList({ wide: true })  // extend the file's render helper to spread extra props

    const row = await screen.findByRole('button', { name: /alice/i })
    expect(row).toHaveClass('is-line')
    expect(row.querySelector('.message-row-line-preview')).toHaveTextContent('the preview text')
    expect(row.querySelector('.message-row-top')).toBeNull()
  })

  it('ends the line at the subject when previews are off', async () => {
    renderList({ wide: true }, { 'mail.showPreview': 'false' })

    const row = await screen.findByRole('button', { name: /alice/i })
    expect(row.querySelector('.message-row-line-preview')).toBeNull()
  })

  it('keeps the unread dot and classes in wide rows', async () => {
    renderList({ wide: true })

    const unreadRow = await screen.findByRole('button', { name: /unseen sender/i })
    expect(unreadRow).toHaveClass('is-unread')
    expect(unreadRow.querySelector('.message-row-unread-dot')).not.toBeNull()
  })

  it('keeps stacked rows when wide is off', async () => {
    renderList()

    const row = await screen.findByRole('button', { name: /alice/i })
    expect(row).not.toHaveClass('is-line')
    expect(row.querySelector('.message-row-top')).not.toBeNull()
  })
})
```

Adapt the sender names/preview text to the fixtures the file already uses (one seen message from "alice", one unseen — add fixtures if the file lacks them). Keep every existing test untouched.

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- src/modules/mail/list/MessageList.test.tsx`
Expected: the four new tests FAIL (`wide` prop unknown / `is-line` absent); every pre-existing test still PASSES.

- [ ] **Step 3: Implement the wide row**

In `MessageList.tsx`:

```tsx
interface Props {
  folderPath: string | null
  folderName?: string
  selectedUid: number | null
  onSelect: (uid: number) => void
  wide?: boolean
}
```

Destructure `wide = false` in the component signature. In the row map, push the class and branch the row body:

```tsx
            const classes = ['message-row']
            if (wide) classes.push('is-line')
            if (!message.seen) classes.push('is-unread')
            if (message.uid === selectedUid) classes.push('is-selected')
```

Replace the `<button>` content with:

```tsx
                <button type="button" className={classes.join(' ')} onClick={() => onSelect(message.uid)}>
                  {wide ? (
                    <>
                      {!message.seen && <span className="message-row-unread-dot" />}
                      <span className="message-row-from">{message.fromName || message.fromAddress}</span>
                      {message.hasAttachments && <PaperclipIcon size={13} title="Has attachments" />}
                      <span className="message-row-line">
                        {message.subject || '(no subject)'}
                        {showsPreview && message.preview && (
                          <span className="message-row-line-preview"> — {message.preview}</span>
                        )}
                      </span>
                      <span className="message-row-date">{formatListDate(message.date)}</span>
                    </>
                  ) : (
                    <>
                      <div className="message-row-top">
                        {!message.seen && <span className="message-row-unread-dot" />}
                        <span className="message-row-from">{message.fromName || message.fromAddress}</span>
                        {message.hasAttachments && <PaperclipIcon size={13} title="Has attachments" />}
                        <span className="message-row-date">{formatListDate(message.date)}</span>
                      </div>
                      <div className="message-row-subject">{message.subject || '(no subject)'}</div>
                      {/* Always rendered when previews are on, even empty: a message with no body
                          would otherwise make a shorter row than its neighbours and break the rhythm
                          of the column. The reserved height lives in CSS. */}
                      {showsPreview && <div className="message-row-preview">{message.preview}</div>}
                    </>
                  )}
                </button>
```

(The stacked branch is today's markup, moved — not rewritten.)

Append to `mail.css`, after the `.message-row-unread-dot` rule:

```css
/* ── Wide rows (bottom / no-split modes) ─────────────────── */

/* One line per message; the empty-preview reserve is pointless here — the subject fills the
   line whether or not a preview follows it. */
.message-row.is-line { display: flex; align-items: baseline; gap: 8px; }

.message-row.is-line .message-row-from { flex: none; width: 180px; }

.message-row.is-line .message-row-line {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.message-row.is-line .message-row-line-preview { color: var(--text-muted); font-weight: 400; }

.message-row.is-line.is-unread .message-row-line { font-weight: 700; }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- src/modules/mail/list/MessageList.test.tsx`
Expected: PASS, including all pre-existing tests.

- [ ] **Step 5: Commit**

```bash
git add src/modules/mail/list/MessageList.tsx src/modules/mail/list/MessageList.test.tsx src/styles/mail.css
git commit -m "Single-line message rows for wide lists"
```

---

### Task 6: Back button and Escape in `MessageReader`

**Files:**
- Create: `src/frontend/src/icons/ArrowLeftIcon.tsx`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx`
- Modify: `src/frontend/src/styles/mail.css` (append back-button rule)
- Test: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: nothing new.
- Produces: `MessageReader` accepts an optional `onBack?: () => void`. When present: a `←` button labelled "Back to the message list" renders before the subject, and Escape calls it. When absent (both split modes): nothing changes. Task 7 consumes it.

- [ ] **Step 1: Write the failing tests**

In `MessageReader.test.tsx`, follow the file's existing render helper (it mocks the api and renders with a loaded message). Add:

```tsx
describe('back navigation', () => {
  it('shows a back button only when a handler is given', async () => {
    const onBack = vi.fn()
    renderReader({ onBack })  // extend the file's helper to spread extra props

    fireEvent.click(await screen.findByRole('button', { name: 'Back to the message list' }))
    expect(onBack).toHaveBeenCalled()
  })

  it('shows no back button without a handler', async () => {
    renderReader()

    await screen.findByText(/subject of the fixture/i)
    expect(screen.queryByRole('button', { name: 'Back to the message list' })).toBeNull()
  })

  it('goes back on Escape', async () => {
    const onBack = vi.fn()
    renderReader({ onBack })

    await screen.findByText(/subject of the fixture/i)
    fireEvent.keyDown(window, { key: 'Escape' })

    expect(onBack).toHaveBeenCalled()
  })
})
```

Adapt `subject of the fixture` to the fixture subject the file already uses.

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- src/modules/mail/reader/MessageReader.test.tsx`
Expected: the three new tests FAIL; pre-existing tests PASS.

- [ ] **Step 3: Implement**

Create `src/frontend/src/icons/ArrowLeftIcon.tsx` (same shape as the other icon files):

```tsx
export default function ArrowLeftIcon({ size = 14 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M12.5 4.5L6.5 10l6 5.5M6.5 10h11" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
```

In `MessageReader.tsx`:

```tsx
interface Props {
  folderPath: string | null
  uid: number | null
  onBack?: () => void
}
```

Destructure `onBack` in the signature. Add `import { useEffect, useState } from 'react'` (already there) and `import ArrowLeftIcon from '../../../icons/ArrowLeftIcon'`. Add the effect after the per-message reset effect:

```tsx
  // Escape mirrors the ← button; both exist only in the no-split mode, where the reader has
  // replaced the list and needs a way back.
  useEffect(() => {
    if (!onBack) return

    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onBack() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onBack])
```

Change the subject line:

```tsx
          <h1 className="reader-subject">
            {onBack && (
              <button
                type="button"
                className="reader-back"
                aria-label="Back to the message list"
                onClick={onBack}
              >
                <ArrowLeftIcon size={16} />
              </button>
            )}
            {data.subject || '(no subject)'}
          </h1>
```

Append to `mail.css` (near the reader header rules):

```css
/* Sized against the subject line so the arrow reads as part of it, not as a toolbar. */
.reader-back {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  margin-right: 8px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text-muted);
  cursor: pointer;
  vertical-align: middle;
}

.reader-back:hover { background: var(--list-row-hover); color: var(--text); }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- src/modules/mail/reader/MessageReader.test.tsx`
Expected: PASS, including all pre-existing tests.

- [ ] **Step 5: Commit**

```bash
git add src/icons/ArrowLeftIcon.tsx src/modules/mail/reader/MessageReader.tsx src/modules/mail/reader/MessageReader.test.tsx src/styles/mail.css
git commit -m "Reader back button and Escape for the no-split mode"
```

---

### Task 7: `MailLayout` — the three arrangements

**Files:**
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx`
- Modify: `src/frontend/src/styles/mail.css` (rework the column rules)
- Test: `src/frontend/src/modules/mail/MailLayout.test.tsx`

**Interfaces:**
- Consumes: `readingPaneOf`, `ReadingPane` (Task 2); `usePaneSize` (Task 3); `PaneSplitter` (Task 4); `MessageList wide` (Task 5); `MessageReader onBack` (Task 6).
- Produces: `.mail-layout` carries `is-right` / `is-bottom` / `is-none`; bottom mode wraps list+splitter+reader in `.mail-stack`; none mode hides (never unmounts) the list behind `.is-hidden` while a message is open.

- [ ] **Step 1: Write the failing tests**

In `MailLayout.test.tsx`, add `getPreferences: vi.fn()` to the hoisted `mocks` object, and in `renderAt` add a default:

```tsx
  mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.readingPane': pane })
```

(`mail.pageSize` matters: without it `requestSizeOf` turns `undefined` into `NaN` inside the list query.) Add a `pane` option so tests can choose the mode — change `renderAt`'s signature to `renderAt(initial: string, tree: MailFolderNode[] = folders, pane = 'right')`. Then add:

```tsx
describe('reading pane arrangements', () => {
  it('renders the side-by-side split with a vertical splitter', async () => {
    const { container } = renderAt('/mail?folder=INBOX')

    await screen.findByRole('separator')
    expect(screen.getByRole('separator')).toHaveAttribute('aria-orientation', 'vertical')
    expect(container.querySelector('.mail-layout.is-right')).not.toBeNull()
  })

  it('renders the stacked split with a horizontal splitter', async () => {
    const { container } = renderAt('/mail?folder=INBOX', folders, 'bottom')

    await screen.findByRole('separator')
    expect(screen.getByRole('separator')).toHaveAttribute('aria-orientation', 'horizontal')
    expect(container.querySelector('.mail-stack')).not.toBeNull()
  })

  // No message open: the list has the space and there is nothing to split.
  it('renders no reader and no splitter in the no-split mode without a message', async () => {
    const { container } = renderAt('/mail?folder=INBOX', folders, 'none')

    await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalled())
    expect(screen.queryByRole('separator')).toBeNull()
    expect(screen.queryByText(/select a message/i)).toBeNull()
    expect(container.querySelector('.mail-list.is-hidden')).toBeNull()
  })

  // The list is hidden, never unmounted: unmounting would lose the scroll position and, in
  // streaming mode, the loaded blocks.
  it('hides the list behind the reader in the no-split mode', async () => {
    mocks.getMailMessage.mockResolvedValue({
      uid: 7, folderPath: 'INBOX', uidValidity: 1, subject: 'open me', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '<p>x</p>', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
    const { container } = renderAt('/mail?folder=INBOX&uid=7', folders, 'none')

    await screen.findByText('open me')
    expect(container.querySelector('.mail-list.is-hidden')).not.toBeNull()
  })

  it('drops the uid when the back button is used', async () => {
    mocks.getMailMessage.mockResolvedValue({
      uid: 7, folderPath: 'INBOX', uidValidity: 1, subject: 'open me', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '<p>x</p>', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
    renderAt('/mail?folder=INBOX&uid=7', folders, 'none')

    fireEvent.click(await screen.findByRole('button', { name: 'Back to the message list' }))

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
    expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX')
  })
})
```

Import `fireEvent` from `@testing-library/react` if the file does not already.

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- src/modules/mail/MailLayout.test.tsx`
Expected: the five new tests FAIL (no separator, no mode classes); pre-existing tests PASS (with `getPreferences` mocked they now resolve `right`, which renders today's layout).

- [ ] **Step 3: Implement the arrangements**

Rewrite `MailLayout.tsx`'s imports and body (folder logic, effects and helpers stay exactly as they are):

```tsx
import { readingPaneOf, usePreferences } from '../../hooks/usePreferences'
import PaneSplitter from './split/PaneSplitter'
import { usePaneSize } from './split/usePaneSize'
```

Inside the component, after the existing hooks:

```tsx
  const { data: preferences } = usePreferences()
  // Until the preferences answer, today's layout — the list already waits on the same query,
  // so nothing meaningful can flash in the wrong arrangement.
  const pane = preferences ? readingPaneOf(preferences) : 'right'
  const [listWidth, setListWidth] = usePaneSize('mail.split.right', 380, 240)
  const [listHeight, setListHeight] = usePaneSize('mail.split.bottom', 280, 120)

  function closeMessage() {
    if (folder) setParams({ folder })
  }
```

Replace the returned JSX from `<div className="mail-layout">` down (the folders column block is unchanged — keep it verbatim):

```tsx
  const list = (selected: number | null, wide: boolean) => (
    <MessageList
      folderPath={folder}
      folderName={folderName}
      selectedUid={selected}
      onSelect={selectMessage}
      wide={wide}
    />
  )

  return (
    <div className={`mail-layout is-${pane}`}>
      {/* Each column is a band stack: what scrolls is the middle band only, so the folder
          actions and the pager stay put instead of hiding below their own content. */}
      <div className="mail-folders">
        {/* ...unchanged folders column... */}
      </div>

      {pane === 'right' && (
        <>
          <div className="mail-list" style={{ width: listWidth }}>{list(uid, false)}</div>
          {preferences && (
            <PaneSplitter
              orientation="vertical" size={listWidth} defaultSize={380} min={240} reserve={320}
              onResize={setListWidth}
            />
          )}
          <div className="mail-reader"><MessageReader folderPath={folder} uid={uid} /></div>
        </>
      )}

      {pane === 'bottom' && (
        <div className="mail-stack">
          <div className="mail-list" style={{ height: listHeight }}>{list(uid, true)}</div>
          <PaneSplitter
            orientation="horizontal" size={listHeight} defaultSize={280} min={120} reserve={160}
            onResize={setListHeight}
          />
          <div className="mail-reader"><MessageReader folderPath={folder} uid={uid} /></div>
        </div>
      )}

      {pane === 'none' && (
        <>
          {/* Hidden, never unmounted: the scroll position and the streamed blocks live in this
              subtree. No selected row either — there is no message "open beside". */}
          <div className={`mail-list${uid !== null ? ' is-hidden' : ''}`}>{list(null, true)}</div>
          {uid !== null && (
            <div className="mail-reader">
              <MessageReader folderPath={folder} uid={uid} onBack={closeMessage} />
            </div>
          )}
        </>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
```

In `mail.css`, replace the `.mail-list` block (`width: 380px; flex: none; border-right: …`) with mode-aware rules (the splitter now carries the separation, so `border-right` goes):

```css
.mail-list {
  background: var(--surface);
}

/* The inline width is the splitter's; shrink-not-grow (flex: 0 1 auto) lets a size stored on
   a large screen give way on a small one instead of pushing the reader off the edge. */
.is-right .mail-list { flex: 0 1 auto; min-width: 240px; }
.is-right .mail-reader { min-width: 320px; }

.mail-stack {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.is-bottom .mail-list { flex: 0 1 auto; min-height: 120px; }
.is-bottom .mail-reader { min-height: 160px; }

.is-none .mail-list { flex: 1; min-width: 0; }

/* display:none would lose to .mail-list's own flex display without the class on the same
   element; !important is not needed once it is. */
.mail-list.is-hidden { display: none; }
```

`.mail-folders` keeps its `border-right` (no splitter there), and `.mail-reader` keeps `flex: 1; min-width: 0; background: var(--surface);` unchanged.

- [ ] **Step 4: Run tests and the whole frontend suite**

Run: `npm test -- src/modules/mail/MailLayout.test.tsx`
Expected: PASS — all new and pre-existing tests.

Run: `npm test`
Expected: all green (MessageList/MessageReader suites untouched by this task must stay green).

- [ ] **Step 5: Look at it**

Run: `npm run dev`, open the mail view, and check by hand: drag both splitters, double-click resets, switch the preference in devtools (`localStorage` untouched — change via the API or wait for Task 8) is not needed — instead temporarily hardcode `pane` to each of the three values, look, then revert. Verify dark mode and one non-default palette.

- [ ] **Step 6: Commit**

```bash
git add src/modules/mail/MailLayout.tsx src/modules/mail/MailLayout.test.tsx src/styles/mail.css
git commit -m "MailLayout: right/bottom/none reading pane arrangements"
```

---

### Task 8: The setting in `GeneralPage`

**Files:**
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx`
- Modify: `src/frontend/src/index.css` (append layout-card rules)
- Test: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`

**Interfaces:**
- Consumes: `PREFERENCE_KEYS.readingPane`, `readingPaneOf` (Task 2); the page's existing `save()` helper.
- Produces: the user-facing control. Radio cards named "Right", "Bottom", "Hidden" in a radiogroup labelled "Reading pane".

- [ ] **Step 1: Write the failing tests**

In `GeneralPage.test.tsx`, add:

```tsx
describe('reading pane', () => {
  it('checks the stored position', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.readingPane': 'bottom' })

    expect(await screen.findByLabelText('Bottom')).toBeChecked()
    expect(screen.getByLabelText('Right')).not.toBeChecked()
    expect(screen.getByRole('radiogroup', { name: 'Reading pane' })).toBeInTheDocument()
  })

  it('saves the chosen position', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.readingPane': 'right' })

    fireEvent.click(await screen.findByLabelText('Hidden'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.readingPane', 'none'))
    expect(await screen.findByText('Messages will open in place of the list')).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- src/modules/settings/general/GeneralPage.test.tsx`
Expected: the two new tests FAIL; pre-existing tests PASS.

- [ ] **Step 3: Implement the control**

In `GeneralPage.tsx`, import `readingPaneOf` and `type ReadingPane` from `usePreferences`, and add above the component:

```tsx
const READING_PANES: { value: ReadingPane; label: string; toast: string }[] = [
  { value: 'right', label: 'Right', toast: 'The reader sits beside the message list' },
  { value: 'bottom', label: 'Bottom', toast: 'The reader sits below the message list' },
  { value: 'none', label: 'Hidden', toast: 'Messages will open in place of the list' },
]

/** A miniature of the arrangement — the glyph is the description, like Appearance's
    palette thumbnails. */
function PaneGlyph({ variant }: { variant: ReadingPane }) {
  return (
    <span className={`pane-glyph is-${variant}`} aria-hidden="true">
      <span className="pane-glyph-lines" />
      {variant !== 'none' && <span className="pane-glyph-pane" />}
    </span>
  )
}
```

In the JSX, after the "Messages per page" field and before the preview `ToggleRow`:

```tsx
          <div className="field-h is-setting">
            <span id="reading-pane-label">Reading pane</span>
            <div className="layout-cards" role="radiogroup" aria-labelledby="reading-pane-label">
              {READING_PANES.map(({ value, label, toast }) => (
                <label key={value} className="layout-card">
                  <PaneGlyph variant={value} />
                  <span className="layout-card-name">
                    <input
                      type="radio"
                      name="reading-pane"
                      value={value}
                      checked={readingPaneOf(preferences) === value}
                      disabled={setPreference.isPending}
                      onChange={() => save(PREFERENCE_KEYS.readingPane, value, toast)}
                    />
                    {label}
                  </span>
                </label>
              ))}
            </div>
          </div>
```

Append to `src/frontend/src/index.css`, next to the palette-card rules:

```css
/* ── Reading-pane cards (General) ─────────────────────────── */

.layout-cards { display: flex; gap: 12px; }

.layout-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--bg);
  cursor: pointer;
  font-size: 13px;
}

.layout-card:has(input:checked) { border-color: var(--accent-unread); box-shadow: 0 0 0 1px var(--accent-unread); }

.layout-card-name { display: flex; align-items: center; gap: 6px; }

/* The glyph draws the arrangement it stands for: list lines, and a shaded reader pane. */
.pane-glyph {
  display: block;
  width: 56px;
  height: 38px;
  border: 1.5px solid var(--text-muted);
  border-radius: var(--radius-sm);
  position: relative;
  overflow: hidden;
}

.pane-glyph-lines {
  position: absolute;
  inset: 5px;
  background: repeating-linear-gradient(to bottom, var(--text-muted) 0 2px, transparent 2px 8px);
  opacity: 0.8;
}

.pane-glyph.is-right .pane-glyph-lines { right: 55%; }
.pane-glyph.is-bottom .pane-glyph-lines { bottom: 55%; }

.pane-glyph-pane {
  position: absolute;
  background: var(--text-muted);
  opacity: 0.3;
  border-radius: 2px;
}

.pane-glyph.is-right .pane-glyph-pane { left: 50%; top: 4px; right: 4px; bottom: 4px; }
.pane-glyph.is-bottom .pane-glyph-pane { left: 4px; right: 4px; top: 50%; bottom: 4px; }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- src/modules/settings/general/GeneralPage.test.tsx`
Expected: PASS, including all pre-existing tests.

- [ ] **Step 5: Look at it**

`npm run dev` → Settings → General. Check the three cards in light and dark, pick each one, and watch the mail view rearrange after each save (the `['mail']` invalidation plus the preferences refetch make it live without a reload). Verify with a second palette.

- [ ] **Step 6: Commit**

```bash
git add src/modules/settings/general/GeneralPage.tsx src/modules/settings/general/GeneralPage.test.tsx src/index.css
git commit -m "Reading pane setting as radio cards in General"
```

---

### Task 9: Full verification and docs

**Files:**
- Modify: `src/frontend/CLAUDE.md` (mail module notes)

- [ ] **Step 1: Full frontend gate**

Run (from `src/frontend`): `npm test && npm run lint && npm run typecheck`
Expected: all green. Fix anything that is not before proceeding.

- [ ] **Step 2: Full backend gate**

Run (from `src/snoopy.microservice`): `dotnet test`
Expected: all green.

- [ ] **Step 3: Document the feature**

In `src/frontend/CLAUDE.md`, add a short paragraph to the mail module section covering: the three `mail.readingPane` arrangements and their mode classes; the hidden-not-unmounted list in `none` (scroll + streamed blocks); `PaneSplitter`/`usePaneSize` and why splitter sizes are localStorage per device; the `wide` row skin and that it respects the preview setting. Match the file's existing voice; keep it to one paragraph plus the `split/` file list.

- [ ] **Step 4: Commit**

```bash
git add src/frontend/CLAUDE.md
git commit -m "Document the reading pane arrangements"
```
