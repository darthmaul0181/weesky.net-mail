# Swipe gestures on the mail list — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a touch screen, a message row swipes right or left to run the action the account chose in Settings; Archive and Delete are held back five seconds behind an Undo.

**Architecture:** The preferences are two registry entries (backend) read through one accessor (frontend). A generic `useSwipe` hook moves the row by inline transform and reports only side/armed changes to React. Archive and Delete go through the existing `useMoveMessages` with an optional `hold` promise: the caches are patched at once, the request waits for the hold, and a cancelled hold is an Undo that rolls back silently. `useDeferredMove` owns the one pending hold and ties its release to the toast's own expiry, so pausing the toast also pauses the send.

**Tech Stack:** React 18 + TypeScript, TanStack Query v5, Vitest + Testing Library (jsdom), ASP.NET Core / xUnit.

**Spec:** `docs/history/specs/2026-09-27-mail-swipe-gestures-design.md`

## Deviations from the spec (both simplifications, both argued here)

1. **No split of `useMoveMessages`.** The spec split it into "patch + snapshot" and "send + rollback". A `hold?: Promise<void>` argument gets the same effect with no second code path: `onMutate` still patches at once, `mutationFn` awaits the hold before sending, and `onError` rolls back — silently when the error is `HoldCancelled`.
2. **No render filter for pending uids.** Because a held move is a *pending* mutation under `mailKeys.writes`, `useListRefresh` already skips the refresh while it waits (`useListRefresh.ts:70`) and re-evaluates on the next tick. What the existing guard did not cover is the folder tree: a poll answered during the hold would put the badge back for up to five seconds. Part 2B closes that: `useFolders` keeps its cached tree while a held move is pending.

## Global Constraints

- Preference keys `mail.swipeRight` (default `seen`) and `mail.swipeLeft` (default `delete`); values `none` | `seen` | `flag` | `archive` | `delete`.
- Threshold: **35 %** of the row's width. Direction travel: `GESTURE_TRAVEL_PX` (10 px) with |dx| > |dy|.
- Undo window: **5 000 ms**, with a countdown bar; one pending Undo at a time.
- Only `pointerType` `touch` or `pen` swipes; the mouse never does.
- The row keeps exactly **four** `gridcell`s; the band is `aria-hidden` and outside the `role="row"`.
- Colours are role tokens, never literals (`.claude/rules/frontend-theming.md`); white band text ≥ **4.5:1** in all 16 palette × mode combinations.
- i18n: every key written as a literal inside `t(…)` (no `t(variable)`); French uses `’` (U+2019) and U+00A0 before `: ; ? !` — write it as ` ` in the JSON.
- UI strings are English and French; code comments English, 3 lines max, only where the code is not obvious.
- Frontend commands run from `src/frontend`; backend from the repo root. `dotnet test` (never `--no-build`). Revert `src/scotty.microservice/ApiDocumentation.xml` drift before any commit.
- Commit messages: two lines max, never starting or ending with `@`; use `git commit -F -` with a heredoc. Do not push.

## Review Focus

- A scroll that starts vertical and drifts sideways must stay a scroll: the row never moves mid-scroll (Part 3A, "a scroll that drifts sideways stays a scroll").
- Two swipes within five seconds: the first is sent at once and its toast disappears, so its Undo can never be pressed on a move already sent (Part 2C, "a second start sends the first").
- Changing folder, page, search or account while an Undo is showing sends the move and removes the toast (Part 2C, "a new flush key sends the pending move").
- The folder badge must not jump back during the five seconds because a poll landed (Part 2B, "keeps the cached tree while a held move waits").
- Keyboard focus on Undo pauses the toast, and the move must not be sent while it is paused (Part 2A, "does not expire while paused").

## File Structure

| File | Responsibility |
|---|---|
| `src/scotty.microservice/Models/UserPreferences.cs` | registry: two swipe keys |
| `src/frontend/src/hooks/usePreferences.ts` | keys, `SwipeAction`, `swipeActionOf` |
| `src/frontend/src/styles/theme-*.css` (8 files) | four `--swipe-*` fill roles, light block only |
| `src/frontend/src/styles/swipeColours.test.ts` | **new**: white-on-fill contrast, 16 combinations |
| `src/frontend/src/hooks/useToasts.ts`, `components/Toasts.tsx`, `index.css` | `durationMs`, `countdown`, `onExpire`; `addToast` returns the id |
| `src/frontend/src/api.ts` | `keepalive` on writes |
| `src/frontend/src/modules/mail/hold.ts` | **new**: `createHold`, `HoldCancelled`, `holdsPendingMove` |
| `src/frontend/src/modules/mail/messages.ts`, `folders.ts` | held move; tree kept during a hold |
| `src/frontend/src/hooks/useSwipe.ts` | **new**: the gesture, knows nothing of mail |
| `src/frontend/src/modules/mail/list/useDeferredMove.ts` | **new**: the one pending hold, its toast, its flushes |
| `src/frontend/src/icons/BanIcon.tsx` | **new**: "none" / unavailable glyph |
| `src/frontend/src/modules/mail/SwipeActionIcon.tsx` | **new**: one icon per swipe action (row band + settings) |
| `src/frontend/src/modules/mail/list/MessageRow.tsx`, `MessageList.tsx`, `MailLayout.tsx`, `styles/mail.css` | band, wiring, dispatch |
| `src/frontend/src/modules/settings/general/GeneralPage.tsx` | "Swipe gestures" section |
| `src/frontend/src/locales/{en,fr}/{mail,settings}.json` | strings |
| `src/frontend/probes/mobile-layout.html`, `docs/architecture-mail.md`, `.claude/rules/frontend-theming.md` | probe case, docs |

---

### Task 1: The swipe preference, end to end

One reviewable unit: the two keys from the registry to the settings screen, with the colours and icons the setting shows. Parts run in order; each keeps its own red-green cycle.

**Interfaces produced for later tasks:** `PREFERENCE_KEYS.swipeRight/swipeLeft`, `type SwipeAction`, `SWIPE_ACTIONS`, `swipeActionOf(preferences, side)`; CSS roles `--swipe-seen/-flag/-archive/-delete`; `BanIcon`; `SwipeActionIcon({ action, size, unread })` in `src/frontend/src/modules/mail/SwipeActionIcon.tsx`.

#### Part 1A — Backend registry — two swipe preferences
**Files:**
- Modify: `src/scotty.microservice/Models/UserPreferences.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Produces: `UserPreferences.MailSwipeRight = "mail.swipeRight"`, `UserPreferences.MailSwipeLeft = "mail.swipeLeft"`; `GET /api/Preferences` answers both with their defaults.

- [ ] **Step 1A.1: Write the failing tests** — append to `UserPreferencesTests`:

```csharp
    [Theory]
    [InlineData(UserPreferences.MailSwipeRight, "seen")]
    [InlineData(UserPreferences.MailSwipeLeft, "delete")]
    public void Swipe_defaults_are_the_mail_app_convention(string key, string expected)
    {
        Assert.Equal(expected, UserPreferences.All.Single(p => p.Key == key).Default);
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("seen", true)]
    [InlineData("flag", true)]
    [InlineData("archive", true)]
    [InlineData("delete", true)]
    [InlineData("junk", false)]            // left out on purpose: too rare for a gesture
    [InlineData("Delete", false)]
    [InlineData("", false)]
    [InlineData("seen,delete", false)]     // one action per side, never a set
    public void Swipe_accepts_one_known_action(string value, bool valid)
    {
        Assert.Equal(valid, UserPreferences.IsValid(UserPreferences.MailSwipeRight, value));
        Assert.Equal(valid, UserPreferences.IsValid(UserPreferences.MailSwipeLeft, value));
    }
```

- [ ] **Step 1A.2: Run to see them fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~UserPreferencesTests"`
Expected: build error — `MailSwipeRight` is not defined.

- [ ] **Step 1A.3: Implement** — in `UserPreferences.cs`, beside the other constants:

```csharp
    public const string MailSwipeRight = "mail.swipeRight";
    public const string MailSwipeLeft = "mail.swipeLeft";
```

beside `Booleans`:

```csharp
    private static readonly string[] SwipeActions = ["none", "seen", "flag", "archive", "delete"];
```

and at the end of `All`, before `UiLanguage`:

```csharp
        // Right is the reversible action, left the one an Undo covers: what mail apps already do.
        new(MailSwipeRight, "seen", SwipeActions),
        new(MailSwipeLeft, "delete", SwipeActions),
```

- [ ] **Step 1A.4: Run to see them pass**

Run: the Step 2 command. Expected: PASS, whole class green.

#### Part 1B — Client preferences — `swipeActionOf`
**Files:**
- Modify: `src/frontend/src/hooks/usePreferences.ts`
- Test: `src/frontend/src/hooks/usePreferences.test.tsx`

**Interfaces:**
- Consumes: Part 1A's keys.
- Produces: `PREFERENCE_KEYS.swipeRight`, `PREFERENCE_KEYS.swipeLeft`; `export type SwipeAction = 'none' | 'seen' | 'flag' | 'archive' | 'delete'`; `export const SWIPE_ACTIONS: readonly SwipeAction[]`; `export function swipeActionOf(preferences: Preferences, side: 'left' | 'right'): SwipeAction`.

- [ ] **Step 1B.1: Write the failing test** — append to `usePreferences.test.tsx` (import `swipeActionOf` from `./usePreferences`):

```ts
describe('swipeActionOf', () => {
  it('reads each side', () => {
    const prefs = { 'mail.swipeRight': 'archive', 'mail.swipeLeft': 'none' }
    expect(swipeActionOf(prefs, 'right')).toBe('archive')
    expect(swipeActionOf(prefs, 'left')).toBe('none')
  })

  // An older backend sends neither key; a newer build may write one this build ignores.
  it('falls back to the defaults on an absent or unknown value', () => {
    expect(swipeActionOf({}, 'right')).toBe('seen')
    expect(swipeActionOf({}, 'left')).toBe('delete')
    expect(swipeActionOf({ 'mail.swipeLeft': 'junk' }, 'left')).toBe('delete')
  })
})
```

- [ ] **Step 1B.2: Run to see it fail**

Run: `npx vitest run src/hooks/usePreferences.test.tsx`
Expected: FAIL — `swipeActionOf` is not exported.

- [ ] **Step 1B.3: Implement** — add to `PREFERENCE_KEYS` (after `groupConversations`):

```ts
  swipeRight: 'mail.swipeRight',
  swipeLeft: 'mail.swipeLeft',
```

and after `groupConversationsOf`:

```ts
export type SwipeAction = 'none' | 'seen' | 'flag' | 'archive' | 'delete'

/** The order the settings list offers them in. */
export const SWIPE_ACTIONS: readonly SwipeAction[] = ['none', 'seen', 'flag', 'archive', 'delete']

/** The backend's own defaults, repeated for a backend that predates the keys. */
export function swipeActionOf(preferences: Preferences, side: 'left' | 'right'): SwipeAction {
  const stored = preferences[side === 'right' ? PREFERENCE_KEYS.swipeRight : PREFERENCE_KEYS.swipeLeft]
  const known = SWIPE_ACTIONS.find(action => action === stored)
  return known ?? (side === 'right' ? 'seen' : 'delete')
}
```

- [ ] **Step 1B.4: Run to see it pass** — same command. Expected: PASS.

#### Part 1C — Swipe fill colours — four role tokens with a contrast guard
White on `--success` (#16a34a) is 3.3:1 and on dark `--danger` (#f87171) 2.8:1, so the bands get their own roles. They are palette-independent semantic colours (like `--danger`), declared in each **light** block only: the dark block inherits them, the way it inherits `--icon-hover-*`.

**Files:**
- Modify: `src/frontend/src/styles/theme-{night,classic,forest,slate,plum,ink,azure,indigo}.css` (light block only)
- Modify: `src/frontend/src/styles/palettes.test.ts` (`toHaveLength(39)` → `43`)
- Modify: `.claude/rules/frontend-theming.md` ("39 role tokens" → "43 role tokens"; "37 in the dark block, which inherits the two `--icon-hover-*`" → "37 in the dark block, which inherits the two `--icon-hover-*` and the four `--swipe-*`")
- Create: `src/frontend/src/styles/swipeColours.test.ts`

**Interfaces:**
- Produces: `--swipe-seen`, `--swipe-flag`, `--swipe-archive`, `--swipe-delete`; text on them is `--action-primary-fg` (white in all 16 blocks by decision).

- [ ] **Step 1C.1: Write the failing test** — `src/frontend/src/styles/swipeColours.test.ts`:

```ts
import { describe, expect, it } from 'vitest'

const modules = import.meta.glob('./theme-*.css', { query: '?raw', import: 'default', eager: true }) as Record<string, string>
const files = Object.keys(modules)
const ROLES = ['--swipe-seen', '--swipe-flag', '--swipe-archive', '--swipe-delete']

function block(css: string, selector: string): string {
  const at = css.indexOf(`${selector} {`)
  return at < 0 ? '' : css.slice(at, css.indexOf('}', at))
}

function valueOf(body: string, role: string): string | undefined {
  return new RegExp(`${role}\\s*:\\s*(#[0-9a-fA-F]{6})`).exec(body)?.[1]
}

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map(i => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r! + 0.7152 * g! + 0.0722 * b!
}

const onWhite = (hex: string) => 1.05 / (luminance(hex) + 0.05)

describe('the swipe fills', () => {
  it('reads the stylesheets, not empty mocks', () => {
    expect(files).toHaveLength(8)
  })

  // The band's label is 14px: small text, 4.5:1. The dark block must not redeclare them, so one
  // check per palette covers both modes.
  it.each(files)('%s gives every swipe fill 4.5:1 under white text, in both modes', file => {
    const css = modules[file]!
    const id = file.slice('./theme-'.length, -'.css'.length)
    const light = block(css, `[data-palette='${id}']`)
    const dark = block(css, `[data-palette='${id}'][data-theme='dark']`)
    for (const role of ROLES) {
      const value = valueOf(light, role)
      expect(value, `${file} ${role}`).toBeDefined()
      expect(onWhite(value!), `${file} ${role}`).toBeGreaterThanOrEqual(4.5)
      expect(valueOf(dark, role), `${file} dark ${role}`).toBeUndefined()
    }
  })
})
```

- [ ] **Step 1C.2: Run to see it fail**

Run: `npx vitest run src/styles/swipeColours.test.ts`
Expected: FAIL — `--swipe-seen` undefined in `theme-azure.css`.

- [ ] **Step 1C.3: Implement** — in each of the 8 files, at the end of the **light** block (`[data-palette='<id>'] { … }`), add exactly:

```css
  /* Swipe bands: semantic like --danger, darkened so white text clears 4.5:1; dark inherits. */
  --swipe-seen: #2563eb;
  --swipe-flag: #b45309;
  --swipe-archive: #15803d;
  --swipe-delete: #dc2626;
```

(Measured: 5.17, 5.02, 5.01, 4.83 against white.) Then set `palettes.test.ts`'s `toHaveLength(39)` to `toHaveLength(43)` and edit the two phrases in `.claude/rules/frontend-theming.md` listed above.

- [ ] **Step 1C.4: Run to see it pass**

Run: `npx vitest run src/styles/`
Expected: PASS (both `swipeColours.test.ts` and `palettes.test.ts`).

#### Part 1D — Settings — the "Swipe gestures" section
**Files:**
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx`, `src/frontend/src/locales/{en,fr}/settings.json`, `src/frontend/src/styles/shell.css` (icon colour rule)
- Create: `src/frontend/src/icons/BanIcon.tsx`, `src/frontend/src/modules/mail/SwipeActionIcon.tsx`
- Test: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`

**Interfaces:**
- Consumes: `PREFERENCE_KEYS.swipeRight/Left`, `SWIPE_ACTIONS`, `swipeActionOf`, `SwipeAction` (Part 1B); `--swipe-*` (Part 1C). Creates `BanIcon` and `SwipeActionIcon` (Step 1D.1b), which Part 3B reuses.

- [ ] **Step 1D.1: Strings** — in `locales/en/settings.json`, inside `"general"`, after `"rowActions"`:

```json
    "swipe": {
      "heading": "Swipe gestures",
      "intro": "Swipe a message in the list to act on it without opening it. Touch screens only.",
      "right": "Swipe right",
      "left": "Swipe left",
      "offHint": "Choose None on both sides to turn swiping off.",
      "none": "None",
      "seen": "Read / unread",
      "flag": "Star / unstar",
      "archive": "Archive",
      "delete": "Delete",
      "rightToast": "Swipe right: {{action}}",
      "leftToast": "Swipe left: {{action}}"
    },
```

and in `locales/fr/settings.json`, same place (` ` is the required no-break space before `:`):

```json
    "swipe": {
      "heading": "Gestes de glissement",
      "intro": "Glissez un message dans la liste pour agir sans l’ouvrir. Écrans tactiles uniquement.",
      "right": "Glisser vers la droite",
      "left": "Glisser vers la gauche",
      "offHint": "Choisissez Aucune des deux côtés pour désactiver le glissement.",
      "none": "Aucune",
      "seen": "Lu / non lu",
      "flag": "Suivre / ne plus suivre",
      "archive": "Archiver",
      "delete": "Supprimer",
      "rightToast": "Glisser vers la droite : {{action}}",
      "leftToast": "Glisser vers la gauche : {{action}}"
    },
```

- [ ] **Step 1D.1b: Icons (the row reuses them in Part 3B)** — `src/frontend/src/icons/BanIcon.tsx` (Feather's `slash`, the house grid):

```tsx
import Icon from './Icon'
export default function BanIcon({ size = 15 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" xmlns="http://www.w3.org/2000/svg">
      <circle cx="12" cy="12" r="10" />
      <line x1="4.93" y1="4.93" x2="19.07" y2="19.07" />
    </Icon>
  )
}
```

(If `icons.test.tsx` enumerates the icon files, add `BanIcon` there.) `src/frontend/src/modules/mail/SwipeActionIcon.tsx`:

```tsx
import type { SwipeAction } from '../../hooks/usePreferences'
import ArchiveIcon from '../../icons/ArchiveIcon'
import BanIcon from '../../icons/BanIcon'
import MailIcon from '../../icons/MailIcon'
import MailOpenIcon from '../../icons/MailOpenIcon'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon'

/** One glyph per swipe action, shared by the row's band and the settings list. `unread: false`
    turns Read / Unread into the closed envelope, the action that swipe would take. */
export default function SwipeActionIcon({ action, size = 20, unread = true }:
  { action: SwipeAction; size?: number; unread?: boolean }) {
  switch (action) {
    case 'none': return <BanIcon size={size} />
    case 'seen': return unread ? <MailOpenIcon size={size} /> : <MailIcon size={size} />
    case 'flag': return <StarIcon size={size} />
    case 'archive': return <ArchiveIcon size={size} />
    case 'delete': return <TrashIcon size={size} />
  }
}
```


- [ ] **Step 1D.2: Write the failing tests** — append to `GeneralPage.test.tsx` (`renderPage`, `pickOption` are the file's helpers):

```tsx
describe('GeneralPage swipe gestures', () => {
  it('shows the defaults when nothing is stored', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.swipeRight': 'seen', 'mail.swipeLeft': 'delete' })
    expect(await screen.findByLabelText('Swipe right')).toHaveTextContent('Read / unread')
    expect(screen.getByLabelText('Swipe left')).toHaveTextContent('Delete')
  })

  it('saves each side', async () => {
    renderPage()
    await pickOption(await screen.findByLabelText('Swipe right'), 'Archive')
    await waitFor(() => expect(mocks.setPreference).toHaveBeenCalledWith('mail.swipeRight', 'archive'))
    await pickOption(screen.getByLabelText('Swipe left'), 'None')
    await waitFor(() => expect(mocks.setPreference).toHaveBeenCalledWith('mail.swipeLeft', 'none'))
  })
})
```

- [ ] **Step 1D.3: Run to see them fail**

Run: `npx vitest run src/modules/settings/general/GeneralPage.test.tsx -t "swipe"`
Expected: FAIL — no field labelled "Swipe right".

- [ ] **Step 1D.4: Implement** — in `GeneralPage.tsx`, import `SWIPE_ACTIONS`, `swipeActionOf`, `type SwipeAction` and `SwipeActionIcon` (`../../mail/SwipeActionIcon`). Above the component:

```tsx
/** Literal keys, one per action: the missing-key guard cannot read a key held in a variable. */
function swipeLabel(action: SwipeAction, t: TFunction<'settings'>): string {
  switch (action) {
    case 'none': return t('general.swipe.none')
    case 'seen': return t('general.swipe.seen')
    case 'flag': return t('general.swipe.flag')
    case 'archive': return t('general.swipe.archive')
    case 'delete': return t('general.swipe.delete')
  }
}
```

inside the component, before `return`:

```tsx
  const swipeOptions = SWIPE_ACTIONS.map(action => ({
    value: action,
    label: swipeLabel(action, t),
    icon: <span className={`swipe-option-icon is-${action}`}><SwipeActionIcon action={action} size={16} /></span>,
  }))
```

and a new section right after the Layout section's closing `</section>`:

```tsx
          <section className="account-section">
            <h2>{t('general.swipe.heading')}</h2>
            <p className="svc-account-section-intro">{t('general.swipe.intro')}</p>
            <div className="field-h is-setting">
              <span className="setting-label"><label htmlFor="swipe-right">{t('general.swipe.right')}</label></span>
              <MenuSelect
                id="swipe-right"
                value={swipeActionOf(preferences, 'right')}
                options={swipeOptions}
                disabled={setPreference.isPending}
                onChange={action => void save(PREFERENCE_KEYS.swipeRight, action,
                  t('general.swipe.rightToast', { action: swipeLabel(action, t) }))}
              />
            </div>
            <div className="field-h is-setting">
              <span className="setting-label">
                <label htmlFor="swipe-left">{t('general.swipe.left')}</label>
                <span className="setting-hint">{t('general.swipe.offHint')}</span>
              </span>
              <MenuSelect
                id="swipe-left"
                value={swipeActionOf(preferences, 'left')}
                options={swipeOptions}
                disabled={setPreference.isPending}
                onChange={action => void save(PREFERENCE_KEYS.swipeLeft, action,
                  t('general.swipe.leftToast', { action: swipeLabel(action, t) }))}
              />
            </div>
          </section>
```

In `shell.css`, after the `.menu-select` rules:

```css
/* The settings list shows each swipe action in the colour its band takes. */
.swipe-option-icon { display: inline-flex; }
.swipe-option-icon.is-seen { color: var(--swipe-seen); }
.swipe-option-icon.is-flag { color: var(--swipe-flag); }
.swipe-option-icon.is-archive { color: var(--swipe-archive); }
.swipe-option-icon.is-delete { color: var(--swipe-delete); }
```

(`.menu-select > svg { color: var(--text-muted) }` targets a direct `svg`; the icon is wrapped in a span so its own colour wins. `is-none` keeps the muted colour.)

- [ ] **Step 1D.5: Run to see them pass, then locales and keys**

Run: `npx vitest run src/modules/settings src/locales`
Expected: PASS — including `parity.test.ts` (same keys in both languages, French typography) and `keys.test.ts` (every literal key resolves).

- [ ] **Commit the task**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null || true
git add src/scotty.microservice src/frontend/src .claude/rules/frontend-theming.md
git commit -F - <<'EOF2'
Swipe preferences: registry, accessor, colours and settings section
EOF2
```

---

### Task 2: The held move and its Undo

One reviewable unit: the toast learns a duration, a countdown and an expiry; a move learns to wait for a hold; `useDeferredMove` ties the two. No UI calls it yet — Task 3 does.

**Interfaces produced for later tasks:** `AddToast` (returns the id, takes `ToastOptions`); `MoveMessagesArgs.hold`; `createHold`, `HoldCancelled`, `holdsPendingMove`; `useDeferredMove({ notify, dismiss, flushKey }).start(message, undoLabel)`; `UNDO_MS`.

#### Part 2A — Toasts — custom duration, countdown bar, expiry callback
**Files:**
- Modify: `src/frontend/src/hooks/useToasts.ts`, `src/frontend/src/components/Toasts.tsx`, `src/frontend/src/index.css` (after `.toast-action`)
- Test: `src/frontend/src/hooks/useToasts.test.tsx`, `src/frontend/src/components/Toasts.test.tsx` (create it if absent)

**Interfaces:**
- Produces:
  ```ts
  export interface ToastOptions { durationMs?: number; countdown?: boolean; onExpire?: () => void }
  export interface Toast { id: number; message: string; type: 'success' | 'error'; action?: ToastAction; durationMs?: number; countdown?: boolean }
  export type AddToast = (message: string, type?: Toast['type'], action?: ToastAction, options?: ToastOptions) => number
  ```
  `onExpire` runs only when the timer removes the toast — never on `removeToast`, never when the action is used. `addToast` returns the toast's id.

- [ ] **Step 2A.1: Write the failing tests** — append to `useToasts.test.tsx`:

```tsx
describe('useToasts options', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('returns the id and honours a custom duration', () => {
    const { result } = renderHook(() => useToasts())
    let id = 0
    act(() => { id = result.current.addToast('moved', 'success', undefined, { durationMs: 5000 }) })
    expect(id).toBeGreaterThan(0)
    act(() => { vi.advanceTimersByTime(4999) })
    expect(result.current.toasts).toHaveLength(1)
    act(() => { vi.advanceTimersByTime(1) })
    expect(result.current.toasts).toHaveLength(0)
  })

  it('calls onExpire on timeout only', () => {
    const expired = vi.fn()
    const { result } = renderHook(() => useToasts())
    let first = 0
    act(() => {
      first = result.current.addToast('a', 'success', undefined, { durationMs: 5000, onExpire: expired })
      result.current.addToast('b', 'success', undefined, { durationMs: 5000, onExpire: expired })
    })
    act(() => { result.current.removeToast(first) })
    act(() => { vi.advanceTimersByTime(5000) })
    expect(expired).toHaveBeenCalledTimes(1)
  })

  it('does not expire while paused', () => {
    const expired = vi.fn()
    const { result } = renderHook(() => useToasts())
    let id = 0
    act(() => { id = result.current.addToast('a', 'success', undefined, { durationMs: 5000, onExpire: expired }) })
    act(() => { vi.advanceTimersByTime(2000); result.current.pauseToast(id) })
    act(() => { vi.advanceTimersByTime(10_000) })
    expect(expired).not.toHaveBeenCalled()
    act(() => { result.current.resumeToast(id); vi.advanceTimersByTime(3000) })
    expect(expired).toHaveBeenCalledTimes(1)
  })
})
```

and in `src/frontend/src/components/Toasts.test.tsx` (create it with these imports if the file does not exist):

```tsx
import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import Toasts from './Toasts'

describe('Toasts countdown', () => {
  it('draws a bar over the toast’s own duration and pauses it with the toast', () => {
    render(<Toasts onRemove={vi.fn()} onPause={vi.fn()} onResume={vi.fn()} toasts={[
      { id: 1, message: 'Moved to Trash', type: 'success', durationMs: 5000, countdown: true,
        action: { label: 'Undo', onClick: vi.fn() } },
    ]} />)
    const toast = screen.getByText('Moved to Trash').closest('.toast') as HTMLElement
    const bar = toast.querySelector('.toast-countdown') as HTMLElement
    expect(bar.style.getPropertyValue('--toast-ms')).toBe('5000ms')

    fireEvent.focus(screen.getByRole('button', { name: 'Undo' }))
    expect(toast).toHaveClass('is-paused')
    fireEvent.blur(screen.getByRole('button', { name: 'Undo' }))
    expect(toast).not.toHaveClass('is-paused')
  })

  it('draws no bar on an ordinary toast', () => {
    render(<Toasts onRemove={vi.fn()} toasts={[{ id: 1, message: 'Saved', type: 'success' }]} />)
    expect(document.querySelector('.toast-countdown')).toBeNull()
  })
})
```

- [ ] **Step 2A.2: Run to see them fail**

Run: `npx vitest run src/hooks/useToasts.test.tsx src/components/Toasts.test.tsx`
Expected: FAIL — `addToast` returns `undefined`; no `.toast-countdown`.

- [ ] **Step 2A.3: Implement `useToasts.ts`**

Add the types:

```ts
export interface ToastOptions {
  /** Replaces the 3 s / 8 s default. */
  durationMs?: number
  /** A bar that empties over the duration, paused with the toast. */
  countdown?: boolean
  /** Runs when the toast times out — never when it is dismissed or its action is used. */
  onExpire?: () => void
}
```

extend `Toast` with `durationMs?: number` and `countdown?: boolean`, `TimerEntry` with `onExpire?: () => void`, and change `AddToast`'s return type to `number`. Then:

```ts
  const arm = useCallback((id: number, delay: number, onExpire?: () => void) => {
    const timeoutId = setTimeout(() => {
      timers.current.delete(id)
      setToasts(prev => prev.filter(t => t.id !== id))
      onExpire?.()
    }, delay)
    timers.current.set(id, { timeoutId, remaining: delay, startedAt: Date.now(), onExpire })
  }, [])

  const addToast: AddToast = useCallback((message, type = 'success', action, options) => {
    const id = ++nextToastId
    setToasts(prev => [...prev, {
      id, message, type, action, durationMs: options?.durationMs, countdown: options?.countdown,
    }])
    if (type !== 'error') {
      arm(id, options?.durationMs ?? (action ? DISMISS_WITH_ACTION_MS : DISMISS_MS), options?.onExpire)
    }
    return id
  }, [arm])
```

In `pauseToast`, carry `onExpire: entry.onExpire` into the paused entry; in `resumeToast`, call `arm(id, entry.remaining, entry.onExpire)`.

- [ ] **Step 2A.4: Implement `Toasts.tsx`** — import `useState` and `type CSSProperties`; in `Toast`:

```tsx
  const [paused, setPaused] = useState(false)

  function pauseFor(reason: PauseReason) {
    if (reasons.current.size === 0) { onPause?.(toast.id); setPaused(true) }
    reasons.current.add(reason)
  }

  function resumeFor(reason: PauseReason) {
    reasons.current.delete(reason)
    if (reasons.current.size === 0) { onResume?.(toast.id); setPaused(false) }
  }
```

Root class: ``className={`toast toast-${toast.type}${toast.countdown ? ' has-countdown' : ''}${paused ? ' is-paused' : ''}`}``, and as the root's last child:

```tsx
      {toast.countdown && (
        <span className="toast-countdown" aria-hidden="true"
          style={{ '--toast-ms': `${toast.durationMs ?? 0}ms` } as CSSProperties} />
      )}
```

- [ ] **Step 2A.5: Implement the CSS** — in `index.css`, after the `.toast-action` rule:

```css
/* Empties over the toast's own life and stops with its timer, so it shows what is really left. */
.toast.has-countdown { position: relative; overflow: hidden; }
.toast-countdown {
  position: absolute; left: 0; bottom: 0; width: 100%; height: 3px;
  background: var(--status-tone); transform-origin: left;
  animation: toast-countdown var(--toast-ms) linear forwards;
}
.toast.is-paused .toast-countdown { animation-play-state: paused; }
@keyframes toast-countdown { to { transform: scaleX(0); } }
@media (prefers-reduced-motion: reduce) { .toast-countdown { animation: none; } }
```

- [ ] **Step 2A.6: Run to see them pass, then the type check**

Run: `npx vitest run src/hooks/useToasts.test.tsx src/components/Toasts.test.tsx && npx tsc --noEmit -p .`
Expected: PASS, no type error (every `AddToast` consumer still compiles: a `number` return is assignable where `void` was expected). A hand-written `AddToast` in a test (`const notify: AddToast = () => {}`) now fails the check: make it return `0`.

#### Part 2B — Held moves — `hold`, `keepalive`, and a tree that waits
**Files:**
- Create: `src/frontend/src/modules/mail/hold.ts`
- Modify: `src/frontend/src/api.ts` (`RequestOptions`, `send`), `src/frontend/src/modules/mail/messages.ts` (`MoveMessagesArgs`, `useMoveMessages`), `src/frontend/src/modules/mail/folders.ts` (`useFolders`)
- Test: `src/frontend/src/api.test.ts`, `src/frontend/src/modules/mail/useMoveMessages.test.tsx`

**Interfaces:**
- Produces:
  ```ts
  // hold.ts
  export class HoldCancelled extends Error {}
  export interface Hold { promise: Promise<void>; release: () => void; cancel: () => void }
  export function createHold(): Hold
  export function holdsPendingMove(client: QueryClient, accountId: string): boolean
  // messages.ts
  export interface MoveMessagesArgs { folderPath: string; uids: number[]; targetFolderPath: string; copy: boolean; hold?: Promise<void> }
  // api.ts
  RequestOptions.keepalive?: boolean
  ```
  A held move patches the caches at `mutate`, sends `api.moveMessages(…, { accountId, keepalive: true })` once the hold resolves, and on `HoldCancelled` restores the snapshots **without** calling `onError`.

- [ ] **Step 2B.1: Write the failing tests**

In `api.test.ts` (follow the file's existing `fetch` stubbing; the assertion is what matters):

```ts
  it('passes keepalive through to a write', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }))
    await api.moveMessages('INBOX', [1], 'Trash', { keepalive: true })
    expect(fetchSpy).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({ keepalive: true }))
    fetchSpy.mockRestore()
  })
```

In `useMoveMessages.test.tsx`, add `getMailFolders: vi.fn()` and `getPreferences: vi.fn(() => Promise.resolve({}))` to `mocks` (`useFolders` reads preferences), import `createHold` from `./hold`, and add inside `describe('useMoveMessages')`:

```tsx
  it('patches at once, sends only when the hold releases, with keepalive', async () => {
    seed()
    mocks.moveMessages.mockResolvedValue(undefined)
    const hold = createHold()
    const { result } = renderHook(() => useMoveMessages(), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false, hold: hold.promise })
    })
    expect(uidsOf(sourcePage()!.messages)).toEqual([2, 3])
    expect(mocks.moveMessages).not.toHaveBeenCalled()

    await act(async () => { hold.release(); await settle() })

    expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [1], 'Archive', { accountId: 'primary', keepalive: true })
  })

  it('an Undo restores the caches in silence and sends nothing', async () => {
    seed()
    const onError = vi.fn()
    const hold = createHold()
    const { result } = renderHook(() => useMoveMessages(onError), { wrapper })
    await act(async () => {
      result.current.mutate({ folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false, hold: hold.promise })
    })

    await act(async () => { hold.cancel(); await settle() })

    expect(uidsOf(sourcePage()!.messages)).toEqual([1, 2, 3])
    expect(folder('INBOX').total).toBe(20)
    expect(mocks.moveMessages).not.toHaveBeenCalled()
    expect(onError).not.toHaveBeenCalled()
  })

  it('keeps the cached tree while a held move waits', async () => {
    seed()
    const hold = createHold()
    const moved = renderHook(() => useMoveMessages(), { wrapper })
    const tree = renderHook(() => useFolders(), { wrapper })
    await act(async () => {
      moved.result.current.mutate({ folderPath: 'INBOX', uids: [1], targetFolderPath: 'Archive', copy: false, hold: hold.promise })
    })
    const patched = folder('INBOX').total

    await act(async () => { await tree.result.current.refetch() })

    expect(mocks.getMailFolders).not.toHaveBeenCalled()
    expect(folder('INBOX').total).toBe(patched)
    hold.cancel()
  })
```

(`seed`, `sourcePage`, `uidsOf`, `folder`, `settle` are the file's existing helpers; adapt the expected uid lists to what `seed()` actually puts in the source page — `[1, 2, 3]` before, `[2, 3]` after removing uid 1. Import `useFolders` from `./queries`.)

- [ ] **Step 2B.2: Run to see them fail**

Run: `npx vitest run src/api.test.ts src/modules/mail/useMoveMessages.test.tsx`
Expected: FAIL — `./hold` missing, `keepalive` not forwarded.

- [ ] **Step 2B.3: Implement `hold.ts`**

```ts
import type { QueryClient } from '@tanstack/react-query'
import { mailKeys } from './mailKeys'

/** The rejection an Undo produces: a move that was never sent, rolled back in silence. */
export class HoldCancelled extends Error {
  constructor() { super('Undone'); this.name = 'HoldCancelled' }
}

export interface Hold { promise: Promise<void>; release: () => void; cancel: () => void }

export function createHold(): Hold {
  let release: () => void = () => {}
  let cancel: () => void = () => {}
  const promise = new Promise<void>((resolve, reject) => {
    release = () => resolve()
    cancel = () => reject(new HoldCancelled())
  })
  // An Undo before the mutation awaits it must not surface as an unhandled rejection.
  promise.catch(() => {})
  return { promise, release, cancel }
}

/** A held move has already patched the counts; a poll answered now would put them back. */
export function holdsPendingMove(client: QueryClient, accountId: string): boolean {
  return client.getMutationCache()
    .findAll({ mutationKey: mailKeys.writes(accountId), status: 'pending' })
    .some(mutation => (mutation.state.variables as { hold?: unknown } | undefined)?.hold !== undefined)
}
```

- [ ] **Step 2B.4: Implement `api.ts`** — add to `RequestOptions`:

```ts
  /** Lets a write outlive the page: a move sent while the tab closes. */
  keepalive?: boolean
```

and in `send`, destructure `keepalive` and change the non-GET branch to `return fetch(`${BASE}${path}`, { ...init, signal, keepalive })`.

- [ ] **Step 2B.5: Implement `messages.ts`** — import `HoldCancelled` from `./hold`; add to `MoveMessagesArgs`:

```ts
  /** The request waits for it; cancelled, it is an Undo and the caches go back in silence. */
  hold?: Promise<void>
```

replace `mutationFn` and `onError` in `useMoveMessages`:

```ts
    mutationFn: async ({ folderPath, uids, targetFolderPath, copy, hold }: MoveMessagesArgs) => {
      if (hold) await hold
      const options = hold ? { accountId, keepalive: true } : { accountId }
      return copy
        ? api.copyMessages(folderPath, uids, targetFolderPath, options)
        : api.moveMessages(folderPath, uids, targetFolderPath, options)
    },
```

```ts
    onError: (error, _args, context) => {
      restoreSnapshots(queryClient, context)
      if (error instanceof HoldCancelled) return
      onError?.(i18next.t(context?.copy ? 'mail:mutations.copyFailed' : 'mail:mutations.moveFailed'))
    },
```

- [ ] **Step 2B.6: Implement `folders.ts`** — in `useFolders`, `const client = useQueryClient()` and:

```ts
    queryFn: ({ signal }) => {
      const cached = client.getQueryData<MailFolderNode[]>(mailKeys.folders(accountId))
      return cached && holdsPendingMove(client, accountId)
        ? Promise.resolve(cached)
        : api.getMailFolders({ signal, accountId })
    },
```

- [ ] **Step 2B.7: Run to see them pass, then the whole mail module**

Run: `npx vitest run src/api.test.ts src/modules/mail`
Expected: PASS — including the existing `useMoveMessages` cases, whose un-held call still asserts `{ accountId: 'primary' }` exactly.

#### Part 2C — `useDeferredMove` — one pending Undo and its flushes
**Files:**
- Create: `src/frontend/src/modules/mail/list/useDeferredMove.ts`
- Test: `src/frontend/src/modules/mail/list/useDeferredMove.test.ts`

**Interfaces:**
- Consumes: `AddToast`, `ToastOptions` (Part 2A); `createHold`, `HoldCancelled` (Part 2B).
- Produces:
  ```ts
  export const UNDO_MS = 5000
  export function useDeferredMove(options: {
    notify?: AddToast
    dismiss?: (id: number) => void
    /** Any change sends the pending move: folder, page, search, account. */
    flushKey: string
  }): { start: (message: string, undoLabel: string) => Promise<void> }
  ```
  `start` sends any earlier pending move, raises the Undo toast and returns the hold's promise for the caller's `mutate({ …, hold })`. Without `notify`, the promise is already resolved.

- [ ] **Step 2C.1: Write the failing tests** — `useDeferredMove.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import type { AddToast, ToastAction, ToastOptions } from '../../../hooks/useToasts'
import { HoldCancelled } from '../hold'
import { UNDO_MS, useDeferredMove } from './useDeferredMove'

function state(promise: Promise<void>) {
  const seen = { value: 'pending' as 'pending' | 'released' | 'cancelled' }
  promise.then(() => { seen.value = 'released' },
    error => { seen.value = error instanceof HoldCancelled ? 'cancelled' : 'pending' })
  return seen
}

function setup(flushKey = 'INBOX') {
  const calls: { action?: ToastAction; options?: ToastOptions }[] = []
  let next = 0
  const notify = vi.fn(((_m, _t, action, options) => { calls.push({ action, options }); return ++next }) as AddToast)
  const dismiss = vi.fn()
  const hook = renderHook(({ key }) => useDeferredMove({ notify, dismiss, flushKey: key }),
    { initialProps: { key: flushKey } })
  return { hook, notify, dismiss, calls }
}

const flushPromises = () => act(async () => { await Promise.resolve() })

describe('useDeferredMove', () => {
  it('raises an Undo toast of five seconds with a countdown', () => {
    const { hook, notify } = setup()
    act(() => { void hook.result.current.start('Moved to Trash', 'Undo') })
    expect(notify).toHaveBeenCalledWith('Moved to Trash', 'success',
      expect.objectContaining({ label: 'Undo' }),
      expect.objectContaining({ durationMs: UNDO_MS, countdown: true }))
  })

  it('releases when the toast expires, cancels on Undo', async () => {
    const { hook, calls } = setup()
    let first!: Promise<void>, second!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    act(() => { calls[0]!.options!.onExpire!() })
    await flushPromises()
    expect(a.value).toBe('released')

    act(() => { second = hook.result.current.start('b', 'Undo') })
    const b = state(second)
    act(() => { calls[1]!.action!.onClick() })
    await flushPromises()
    expect(b.value).toBe('cancelled')
  })

  it('a second start sends the first and removes its toast', async () => {
    const { hook, dismiss } = setup()
    let first!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    act(() => { void hook.result.current.start('b', 'Undo') })
    await flushPromises()
    expect(a.value).toBe('released')
    expect(dismiss).toHaveBeenCalledWith(1)
  })

  it('a new flush key sends the pending move', async () => {
    const { hook, dismiss } = setup('INBOX')
    let first!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    hook.rerender({ key: 'Work' })
    await flushPromises()
    expect(a.value).toBe('released')
    expect(dismiss).toHaveBeenCalledWith(1)
  })

  it('sends on unmount and when the tab is hidden', async () => {
    const { hook } = setup()
    let first!: Promise<void>, second!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' })
    act(() => { document.dispatchEvent(new Event('visibilitychange')) })
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
    await flushPromises()
    expect(a.value).toBe('released')

    act(() => { second = hook.result.current.start('b', 'Undo') })
    const b = state(second)
    hook.unmount()
    await flushPromises()
    expect(b.value).toBe('released')
  })

  it('sends at once when nothing can show an Undo', async () => {
    const hook = renderHook(() => useDeferredMove({ flushKey: 'INBOX' }))
    let first!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    await flushPromises()
    expect(a.value).toBe('released')
  })
})
```

- [ ] **Step 2C.2: Run to see them fail**

Run: `npx vitest run src/modules/mail/list/useDeferredMove.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 2C.3: Implement** — `useDeferredMove.ts`:

```ts
import { useCallback, useEffect, useRef } from 'react'
import type { AddToast } from '../../../hooks/useToasts'
import { createHold, type Hold } from '../hold'

export const UNDO_MS = 5000

interface Pending { hold: Hold; toastId: number }

/** One Undo at a time. The send follows the toast's own expiry, so a toast paused under the
 * keyboard also holds the move back. */
export function useDeferredMove({ notify, dismiss, flushKey }: {
  notify?: AddToast
  dismiss?: (id: number) => void
  flushKey: string
}) {
  const pending = useRef<Pending | null>(null)

  const flush = useCallback(() => {
    const current = pending.current
    if (!current) return
    pending.current = null
    dismiss?.(current.toastId)
    current.hold.release()
  }, [dismiss])

  const start = useCallback((message: string, undoLabel: string) => {
    flush()
    const hold = createHold()
    if (!notify) { hold.release(); return hold.promise }
    const entry: Pending = { hold, toastId: 0 }
    const settle = (run: () => void) => () => {
      if (pending.current === entry) pending.current = null
      run()
    }
    pending.current = entry
    entry.toastId = notify(message, 'success', { label: undoLabel, onClick: settle(hold.cancel) },
      { durationMs: UNDO_MS, countdown: true, onExpire: settle(hold.release) })
    return hold.promise
  }, [flush, notify])

  // The key is only read to rerun the cleanup: a new folder, page, search or account sends.
  useEffect(() => flush, [flush, flushKey])

  useEffect(() => {
    const onHidden = () => { if (document.visibilityState === 'hidden') flush() }
    document.addEventListener('visibilitychange', onHidden)
    window.addEventListener('pagehide', flush)
    return () => {
      document.removeEventListener('visibilitychange', onHidden)
      window.removeEventListener('pagehide', flush)
    }
  }, [flush])

  return { start }
}
```

If `react-hooks/exhaustive-deps` flags `flushKey` as unnecessary, keep it and add `// eslint-disable-next-line react-hooks/exhaustive-deps` above the effect with the comment already there as the reason.

- [ ] **Step 2C.4: Run to see them pass** — Step 2's command, then `npm run lint -- src/modules/mail/list/useDeferredMove.ts`. Expected: PASS, lint clean.

- [ ] **Commit the task**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null || true
git add src/frontend/src
git commit -F - <<'EOF2'
Held moves behind a five-second Undo toast
EOF2
```

---

### Task 3: The gesture on the row

One reviewable unit: `useSwipe`, the row's band and dispatch, the probe, the docs, then the owner's device check.

**Consumes:** everything Tasks 1 and 2 produce.

#### Part 3A — `useSwipe` — the gesture
**Files:**
- Create: `src/frontend/src/hooks/useSwipe.ts`
- Test: `src/frontend/src/hooks/useSwipe.test.ts`

**Interfaces:**
- Consumes: `GESTURE_TRAVEL_PX` from `./gestureThresholds`.
- Produces:
  ```ts
  export type SwipeSide = 'left' | 'right'          // the way the finger travels
  export const SWIPE_THRESHOLD = 0.35
  export interface SwipeState { side: SwipeSide | null; armed: boolean }
  export type SwipeOutcome = 'leave' | 'return'
  export function useSwipe<E extends HTMLElement>(
    canSwipe: (side: SwipeSide) => boolean,
    onCommit: (side: SwipeSide) => SwipeOutcome,
  ): {
    ref: MutableRefObject<E | null>
    state: SwipeState
    handlers: { onPointerDown(e: PointerEvent): void; onPointerMove(e: PointerEvent): void; onPointerUp(): void; onPointerCancel(): void }
    swallowClick: () => boolean
  }
  ```
  `'leave'` slides the element out by its own width; `'return'` snaps it back. `swallowClick()` answers true once after a drag.

- [ ] **Step 3A.1: Write the failing tests** — `src/frontend/src/hooks/useSwipe.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useSwipe, type SwipeSide } from './useSwipe'

const at = (x: number, y = 0, over: Partial<React.PointerEvent> = {}) =>
  ({ clientX: x, clientY: y, pointerType: 'touch', isPrimary: true, button: 0, pointerId: 1, ...over }
  ) as React.PointerEvent

function setup(canSwipe: (side: SwipeSide) => boolean = () => true, outcome: 'leave' | 'return' = 'return') {
  const onCommit = vi.fn(() => outcome)
  const hook = renderHook(() => useSwipe<HTMLDivElement>(canSwipe, onCommit))
  const el = document.createElement('div')
  Object.defineProperty(el, 'clientWidth', { value: 300 })
  hook.result.current.ref.current = el
  const drag = (...points: [number, number?][]) => act(() => {
    hook.result.current.handlers.onPointerDown(at(100, 0))
    for (const [x, y] of points) hook.result.current.handlers.onPointerMove(at(x, y ?? 0))
  })
  const lift = () => act(() => { hook.result.current.handlers.onPointerUp() })
  return { hook, el, onCommit, drag, lift }
}

describe('useSwipe', () => {
  it('follows a horizontal finger without arming short of the threshold', () => {
    const { hook, el, drag } = setup()
    drag([140])
    expect(el.style.transform).toBe('translateX(40px)')
    expect(hook.result.current.state).toEqual({ side: 'right', armed: false })
  })

  it('arms past 35% of the width', () => {
    const { hook, drag } = setup()
    drag([206])
    expect(hook.result.current.state).toEqual({ side: 'right', armed: true })
  })

  it('commits an armed release and slides out on leave', () => {
    const { el, onCommit, drag, lift } = setup(() => true, 'leave')
    drag([50], [-20])
    lift()
    expect(onCommit).toHaveBeenCalledWith('left')
    expect(el.style.transform).toBe('translateX(-300px)')
  })

  it('snaps back on return', () => {
    const { hook, el, drag, lift } = setup(() => true, 'return')
    drag([210])
    lift()
    expect(el.style.transform).toBe('')
    expect(hook.result.current.state).toEqual({ side: null, armed: false })
  })

  it('does nothing on a release short of the threshold', () => {
    const { el, onCommit, drag, lift } = setup()
    drag([150])
    lift()
    expect(onCommit).not.toHaveBeenCalled()
    expect(el.style.transform).toBe('')
  })

  it('a scroll that drifts sideways stays a scroll', () => {
    const { hook, el, onCommit, drag, lift } = setup()
    drag([100, 30], [220, 40])
    lift()
    expect(el.style.transform).toBe('')
    expect(hook.result.current.state.side).toBeNull()
    expect(onCommit).not.toHaveBeenCalled()
  })

  it('never moves for a mouse', () => {
    const { hook, el } = setup()
    act(() => {
      hook.result.current.handlers.onPointerDown(at(100, 0, { pointerType: 'mouse' }))
      hook.result.current.handlers.onPointerMove(at(250, 0, { pointerType: 'mouse' }))
    })
    expect(el.style.transform).toBe('')
  })

  it('does not start toward a side that is off, and stops at rest crossing into one', () => {
    const { hook, el, drag } = setup(side => side === 'right')
    drag([40])
    expect(el.style.transform).toBe('')
    drag([150], [60])
    expect(el.style.transform).toBe('')
    expect(hook.result.current.state.side).toBeNull()
  })

  it('snaps back on pointercancel', () => {
    const { hook, el, onCommit, drag } = setup()
    drag([220])
    act(() => { hook.result.current.handlers.onPointerCancel() })
    expect(el.style.transform).toBe('')
    expect(onCommit).not.toHaveBeenCalled()
  })

  it('swallows the click that ends a drag, and only that one', () => {
    const { hook, drag, lift } = setup()
    drag([140])
    lift()
    expect(hook.result.current.swallowClick()).toBe(true)
    expect(hook.result.current.swallowClick()).toBe(false)
    act(() => { hook.result.current.handlers.onPointerDown(at(100)); hook.result.current.handlers.onPointerUp() })
    expect(hook.result.current.swallowClick()).toBe(false)
  })
})
```

- [ ] **Step 3A.2: Run to see them fail**

Run: `npx vitest run src/hooks/useSwipe.test.ts`
Expected: FAIL — module `./useSwipe` not found.

- [ ] **Step 3A.3: Implement** — `src/frontend/src/hooks/useSwipe.ts`:

```ts
import { useCallback, useRef, useState } from 'react'
import type { PointerEvent } from 'react'
import { GESTURE_TRAVEL_PX } from './gestureThresholds'

export type SwipeSide = 'left' | 'right'
export type SwipeOutcome = 'leave' | 'return'

export const SWIPE_THRESHOLD = 0.35
const SNAP_MS = 200

export interface SwipeState {
  side: SwipeSide | null
  armed: boolean
}

const IDLE: SwipeState = { side: null, armed: false }

interface Gesture { x: number; y: number; width: number; dragging: boolean }

const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false

/** A horizontal drag by touch or pen; the mouse never swipes. The element moves by an inline
 * transform, so a finger's travel costs no render: React hears of the side and the arming only. */
export function useSwipe<E extends HTMLElement>(
  canSwipe: (side: SwipeSide) => boolean,
  onCommit: (side: SwipeSide) => SwipeOutcome,
) {
  const ref = useRef<E | null>(null)
  const [state, setState] = useState<SwipeState>(IDLE)
  const shown = useRef<SwipeState>(IDLE)
  const gesture = useRef<Gesture | null>(null)
  const dragged = useRef(false)

  const show = useCallback((next: SwipeState) => {
    if (next.side === shown.current.side && next.armed === shown.current.armed) return
    shown.current = next
    setState(next)
  }, [])

  const place = useCallback((dx: number, animate: boolean) => {
    const el = ref.current
    if (!el) return
    el.style.transition = animate && !reducedMotion() ? `transform ${SNAP_MS}ms ease-out` : 'none'
    el.style.transform = dx === 0 ? '' : `translateX(${dx}px)`
  }, [])

  const settle = (dx: number) => {
    gesture.current = null
    place(dx, true)
    if (dx === 0) show(IDLE)
  }

  const handlers = {
    onPointerDown: (event: PointerEvent) => {
      dragged.current = false
      gesture.current = null
      if ((event.pointerType !== 'touch' && event.pointerType !== 'pen')
        || !event.isPrimary || event.button !== 0) return
      gesture.current = {
        x: event.clientX, y: event.clientY, width: ref.current?.clientWidth ?? 0, dragging: false,
      }
    },
    onPointerMove: (event: PointerEvent) => {
      const g = gesture.current
      if (!g) return
      const dx = event.clientX - g.x
      const dy = event.clientY - g.y
      if (!g.dragging) {
        if (Math.hypot(dx, dy) <= GESTURE_TRAVEL_PX) return
        // The first decisive move names the gesture for good: a scroll drifting sideways stays one.
        if (Math.abs(dx) <= Math.abs(dy) || !canSwipe(dx > 0 ? 'right' : 'left')) {
          gesture.current = null
          return
        }
        g.dragging = true
        dragged.current = true
        ref.current?.setPointerCapture?.(event.pointerId)
      }
      const side: SwipeSide = dx > 0 ? 'right' : 'left'
      const offset = canSwipe(side) ? dx : 0
      place(offset, false)
      show(offset === 0 ? IDLE
        : { side, armed: g.width > 0 && Math.abs(offset) >= g.width * SWIPE_THRESHOLD })
    },
    onPointerUp: () => {
      const g = gesture.current
      if (!g?.dragging) { gesture.current = null; return }
      const { side, armed } = shown.current
      if (!side || !armed) { settle(0); return }
      const leaves = onCommit(side) === 'leave'
      settle(leaves ? (side === 'right' ? g.width : -g.width) : 0)
    },
    onPointerCancel: () => {
      if (gesture.current?.dragging) settle(0)
      else gesture.current = null
    },
  }

  const swallowClick = () => {
    const was = dragged.current
    dragged.current = false
    return was
  }

  return { ref, state, handlers, swallowClick }
}
```

- [ ] **Step 3A.4: Run to see them pass** — Step 2's command. Expected: PASS. Then `npm run lint -- src/hooks/useSwipe.ts` (fix any finding).

#### Part 3B — The row — band, gesture and dispatch
**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageRow.tsx`, `src/frontend/src/modules/mail/list/MessageList.tsx`, `src/frontend/src/modules/mail/MailLayout.tsx`, `src/frontend/src/styles/mail.css`, `src/frontend/src/locales/{en,fr}/mail.json`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx`, `src/frontend/src/modules/mail/list/MessageRow.test.tsx` (props helper only)

**Interfaces:**
- Consumes: `BanIcon`, `SwipeActionIcon` (Step 1D.1b); `useSwipe`, `SwipeSide` (Part 3A); `useDeferredMove` (Part 2C); `swipeActionOf`, `SwipeAction` (Part 1B); `AddToast` (Part 2A); `--swipe-*` (Part 1C); `MoveMessagesArgs.hold` (Part 2B).
- Produces:
  - `RowCallbacks.swipe: (uids: number[], action: 'archive' | 'delete', label: string) => boolean` — true when the row leaves.
  - `MessageRowProps`: `selecting: boolean`, `swipeRight: SwipeAction`, `swipeLeft: SwipeAction`.
  - `MessageList` props: `onNotify?: AddToast`, `onDismissNotice?: (id: number) => void`.
  - `SwipeActionIcon({ action, size, unread }: { action: SwipeAction; size?: number; unread?: boolean })`.

- [ ] **Step 3B.1: Strings** — in `locales/en/mail.json`, inside `"list"`, add:

```json
    "swipe": {
      "archived": "Moved to Archive",
      "trashed": "Moved to Trash",
      "undo": "Undo"
    },
```

and in `locales/fr/mail.json`, same place:

```json
    "swipe": {
      "archived": "Déplacé dans l’archive",
      "trashed": "Déplacé dans la corbeille",
      "undo": "Annuler"
    },
```

(`’` is U+2019. The band itself reuses existing keys: `toolbar.markRead`, `toolbar.markUnread`, `list.star`, `list.unstar`, `toolbar.archive`, and the `deleteLabel` prop.)

- [ ] **Step 3B.2: Write the failing tests** — in `MessageList.test.tsx`, add near `FINGER`:

```tsx
/** A drag across the row by `dx`, the row given a 300px width jsdom does not lay out. */
function swipe(row: HTMLElement, dx: number) {
  Object.defineProperty(row, 'clientWidth', { configurable: true, value: 300 })
  fireEvent.pointerDown(row, { ...FINGER, clientX: 150, clientY: 10, pointerId: 1 })
  fireEvent.pointerMove(row, { ...FINGER, clientX: 150 + dx / 2, clientY: 10, pointerId: 1 })
  fireEvent.pointerMove(row, { ...FINGER, clientX: 150 + dx, clientY: 10, pointerId: 1 })
  fireEvent.pointerUp(row, { ...FINGER, pointerId: 1 })
}
```

and a render helper plus a suite. The swipe actions come from preferences, which resolve after the
first render under test (CLAUDE.md, Preferences), so the helper waits on a *visible* consequence of
the same answer: `mail.rowActions: 'seen'` removes the row's Archive button once preferences land.

```tsx
async function renderSwipe(preferences: Record<string, string>, props: Partial<ListProps> = {}) {
  const view = renderList(props, { 'mail.rowActions': 'seen', ...preferences })
  await waitFor(() => expect(
    within(rowOf(/alice martin/i)).queryByRole('button', { name: 'Archive' })).toBeNull())
  return view
}

describe('MessageList swipe', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.useMessageList.mockReturnValue(pagedState())
    mocks.folders = [
      folderNode({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
      folderNode({ path: 'Trash', name: 'Trash', specialUse: 'trash' }),
      folderNode({ path: 'Archive', name: 'Archive', specialUse: 'archive' }),
    ]
  })

  it('swiping right past the threshold marks an unread message read', async () => {
    await renderSwipe({ 'mail.swipeRight': 'seen' })
    swipe(rowOf(/alice martin/i), 200)
    expect(mocks.mutate).toHaveBeenCalledWith(
      expect.objectContaining({ folderPath: 'INBOX', uids: [2], flag: 'seen', value: true }))
  })

  it('swiping left deletes behind a five-second Undo', async () => {
    const onNotify = vi.fn(() => 1)
    await renderSwipe({ 'mail.swipeLeft': 'delete' }, { onNotify })
    swipe(rowOf(/alice martin/i), -200)
    expect(mocks.move).toHaveBeenCalledWith(expect.objectContaining({
      folderPath: 'INBOX', uids: [2], targetFolderPath: 'Trash', copy: false, hold: expect.any(Promise),
    }))
    expect(onNotify).toHaveBeenCalledWith('Moved to Trash', 'success',
      expect.objectContaining({ label: 'Undo' }),
      expect.objectContaining({ durationMs: 5000, countdown: true }))
  })

  it('inside the trash, swiping left asks before deleting for good', async () => {
    await renderSwipe({ 'mail.swipeLeft': 'delete' }, { folderPath: 'Trash', folderRole: 'trash' })
    swipe(rowOf(/alice martin/i), -200)
    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(mocks.move).not.toHaveBeenCalled()
  })

  it('an action no folder can take does nothing', async () => {
    mocks.folders = [folderNode({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' })]
    await renderSwipe({ 'mail.swipeLeft': 'archive' })
    swipe(rowOf(/alice martin/i), -200)
    expect(mocks.move).not.toHaveBeenCalled()
    expect(rowOf(/alice martin/i).style.transform).toBe('')
  })

  it('a short swipe does nothing, and the click it ends in opens nothing', async () => {
    const onSelect = vi.fn()
    await renderSwipe({ 'mail.swipeRight': 'seen' }, { onSelect })
    const row = rowOf(/alice martin/i)
    swipe(row, 60)
    fireEvent.click(row)
    expect(mocks.mutate).not.toHaveBeenCalled()
    expect(onSelect).not.toHaveBeenCalled()
  })

  it('does not swipe while selecting', async () => {
    await renderSwipe({ 'mail.swipeRight': 'seen' })
    vi.useFakeTimers()
    try {
      const row = rowOf(/alice martin/i)
      fireEvent.pointerDown(row, FINGER)
      act(() => { vi.advanceTimersByTime(500) })
      fireEvent.pointerUp(row)
      expect(screen.getByText('1 selected')).toBeInTheDocument()
      swipe(rowOf(/bob@x.be/i), 200)
      expect(mocks.mutate).not.toHaveBeenCalled()
    } finally { vi.useRealTimers() }
  })

  it('a side set to None does not move', async () => {
    await renderSwipe({ 'mail.swipeRight': 'none', 'mail.swipeLeft': 'none' })
    const row = rowOf(/alice martin/i)
    swipe(row, 200)
    swipe(row, -200)
    expect(row.style.transform).toBe('')
    expect(mocks.mutate).not.toHaveBeenCalled()
    expect(mocks.move).not.toHaveBeenCalled()
  })

  it('a mouse drag never swipes', async () => {
    await renderSwipe({ 'mail.swipeRight': 'seen' })
    const row = rowOf(/alice martin/i)
    Object.defineProperty(row, 'clientWidth', { configurable: true, value: 300 })
    const mouse = { pointerType: 'mouse', isPrimary: true, button: 0, pointerId: 1 }
    fireEvent.pointerDown(row, { ...mouse, clientX: 150, clientY: 10 })
    fireEvent.pointerMove(row, { ...mouse, clientX: 350, clientY: 10 })
    fireEvent.pointerUp(row, mouse)
    expect(mocks.mutate).not.toHaveBeenCalled()
  })
})
```

In `MessageRow.test.tsx`, add `swipe: vi.fn()` to `callbacks()` and `selecting: false, swipeRight: 'seen' as const, swipeLeft: 'delete' as const` to `props()`.

- [ ] **Step 3B.3: Run to see them fail**

Run: `npx vitest run src/modules/mail/list/MessageList.test.tsx -t "swipe"`
Expected: FAIL — nothing is called.

- [ ] **Step 3B.5: `MessageRow.tsx`** — add the props and callback to the interfaces (see Interfaces), import `useSwipe`, `type SwipeSide`, `type SwipeAction`, `SwipeActionIcon`, `BanIcon`. Replace `Row` so it takes the swipe:

```tsx
type SwipeBinding = ReturnType<typeof useSwipe<HTMLDivElement>>

function Row({ onLongPress, swipe, children, ...rest }:
  { onLongPress?: () => void; swipe: SwipeBinding; children: ReactNode } & HTMLAttributes<HTMLDivElement>) {
  const fired = useRef(false)
  const { onPointerDown, onPointerMove, onPointerUp, onPointerCancel } = useLongPress(() => {
    if (!onLongPress) return  // A cross-folder result: no selection to enter, so no click to eat.
    fired.current = true
    onLongPress()
  })
  const { handlers } = swipe
  return (
    <div
      {...rest}
      ref={swipe.ref}
      onPointerDown={event => { fired.current = false; onPointerDown(event); handlers.onPointerDown(event) }}
      onPointerMove={event => { onPointerMove(event); handlers.onPointerMove(event) }}
      onPointerUp={() => { onPointerUp(); handlers.onPointerUp() }}
      onPointerCancel={() => { onPointerCancel(); handlers.onPointerCancel() }}
      onClickCapture={event => {
        const swiped = swipe.swallowClick()
        if (!fired.current && !swiped) return
        fired.current = false
        // Both are load-bearing and neither replaces the other: stopPropagation keeps the click
        // from the checkbox's own listener, preventDefault is what cancels the input's native
        // activation — without it the box still toggles and undoes the selection just made.
        event.preventDefault()
        event.stopPropagation()
      }}
    >
      {children}
    </div>
  )
}
```

In `MessageRow`, after `rowUids`:

```tsx
  const actionOf = (side: SwipeSide) => (side === 'right' ? swipeRight : swipeLeft)
  const unavailable = (action: SwipeAction) =>
    (action === 'archive' && archiveOff) || (action === 'delete' && trashOff)
  const swipe = useSwipe<HTMLDivElement>(
    side => !crossFolder && !selecting && !leaving && actionOf(side) !== 'none',
    side => {
      const action = actionOf(side)
      if (unavailable(action)) return 'return'
      if (action === 'seen') { on.setFlag(rowUids, 'seen', unread); return 'return' }
      if (action === 'flag') { on.setFlag(rowUids, 'flagged', !flagged); return 'return' }
      if (action === 'none') return 'return'
      return on.swipe(rowUids, action, subject) ? 'leave' : 'return'
    })
  const band = swipe.state.side && actionOf(swipe.state.side)
  const bandOff = band ? unavailable(band) : false
  const bandLabel = band === 'seen' ? seenLabel
    : band === 'flag' ? t(flagged ? 'list.unstar' : 'list.star')
      : band === 'archive' ? t('toolbar.archive') : deleteLabel
```

and the spec's confirmation at the threshold (Android only; iOS has no vibration API):

```tsx
  const buzz = swipe.state.armed && !bandOff
  useEffect(() => { if (buzz && 'vibrate' in navigator) navigator.vibrate(10) }, [buzz])
```

Then push `'is-swiping'` into `classes` when `swipe.state.side`, pass `swipe={swipe}` to `<Row>`, and render the band as the slot's **first** child, before `<Row>`:

```tsx
      {band && swipe.state.side && (
        <div aria-hidden="true" className={`message-row-swipe is-${swipe.state.side} is-${band}`
          + `${swipe.state.armed && !bandOff ? ' is-armed' : ''}`}>
          {bandOff ? <BanIcon size={22} /> : <SwipeActionIcon action={band} size={22} unread={unread} />}
          {swipe.state.armed && !bandOff && <span>{bandLabel}</span>}
        </div>
      )}
```

- [ ] **Step 3B.6: `MessageList.tsx`** — change the prop to `onNotify?: AddToast` (import the type), add `onDismissNotice?: (id: number) => void` with a one-line doc, import `useAccountId`, `useDeferredMove`, `swipeActionOf`. After `rowActions`:

```tsx
  const swipeRight = preferences ? swipeActionOf(preferences, 'right') : 'none'
  const swipeLeft = preferences ? swipeActionOf(preferences, 'left') : 'none'
```

after `resetKey`:

```tsx
  const accountId = useAccountId()
  const deferred = useDeferredMove({
    notify: onNotify, dismiss: onDismissNotice, flushKey: `${accountId}::${resetKey}`,
  })
```

beside `removeRow`:

```tsx
  // True when the row leaves; inside the trash the confirm decides, so the row stays.
  function swipeRow(uids: number[], action: 'archive' | 'delete', label: string): boolean {
    if (action === 'delete' && inTrash) { setExpunging({ label, uids }); return false }
    const target = action === 'archive' ? roles.archive : roles.trash
    if (!folderPath || !target) return false
    const hold = deferred.start(
      t(action === 'archive' ? 'list.swipe.archived' : 'list.swipe.trashed'), t('list.swipe.undo'))
    rowExit.depart(uids, () =>
      moveMessages.mutate({ folderPath, uids, targetFolderPath: target, copy: false, hold }))
    reportDeparted(uids)
    return true
  }
```

add `swipe: swipeRow,` to `useForwarders({ … })`, and to `<MessageRow …>`: `selecting={count > 0}`, `swipeRight={swipeRight}`, `swipeLeft={swipeLeft}`.

- [ ] **Step 3B.7: `MailLayout.tsx`** — on `<MessageList …>` add `onDismissNotice={removeToast}`.

- [ ] **Step 3B.8: CSS** — in `mail.css`, after the `.message-row-slot` rule:

```css
/* The band shares the slot's one cell with the row and sits under it; it collapses with the slot. */
.message-row-slot > .message-row { grid-area: 1 / 1; touch-action: pan-y pinch-zoom; }
.message-row.is-swiping { box-shadow: 0 1px 6px rgb(0 0 0 / 0.18); }
.message-row-swipe {
  grid-area: 1 / 1; min-height: 0; overflow: hidden;
  display: flex; align-items: center; gap: 8px; padding: 0 22px;
  background: var(--surface-sunken); color: var(--text-muted);
  font-size: 14px; font-weight: 600;
}
/* The finger went left, so the band shows on the right, label before the glyph. */
.message-row-swipe.is-left { flex-direction: row-reverse; }
.message-row-swipe.is-armed { color: var(--action-primary-fg); }
.message-row-swipe.is-armed.is-seen { background: var(--swipe-seen); }
.message-row-swipe.is-armed.is-flag { background: var(--swipe-flag); }
.message-row-swipe.is-armed.is-archive { background: var(--swipe-archive); }
.message-row-swipe.is-armed.is-delete { background: var(--swipe-delete); }
```

(`.message-row-swipe.is-left` uses `row-reverse` so the icon, first in the markup, lands at the right edge.)

- [ ] **Step 3B.9: Run to see them pass, then the whole list and a type check**

Run: `npx vitest run src/modules/mail && npx tsc --noEmit -p . && npm run lint`
Expected: PASS, including every earlier `MessageList` and `MessageRow` test (long-press, click, memo contract).

#### Part 3C — Geometry probe, docs, and the real-device check
**Files:**
- Modify: `src/frontend/probes/mobile-layout.html`, `src/frontend/docs/architecture-mail.md`

- [ ] **Step 3C.1: Probe case** — in `probes/mobile-layout.html`, add a case named `swipe band` that follows the file's existing case format exactly (read two existing cases first). Restate the real markup: a `.message-row-slot` holding a `.message-row-swipe.is-left.is-delete.is-armed` band (TrashIcon's SVG + `<span>Delete</span>`) and a `.message-row.is-swiping` with `style="transform: translateX(-160px)"` and the narrow row's content. Measure `escape` on `.message-row-swipe` (0 on every edge) and `contrast` on `.message-row-swipe span`. Run it under **touch emulation** at 360×640 and 390×844, in both themes. Expected: escape 0, contrast ≥ 4.5 in both themes. Quote which run each number came from in the commit body if they differ.

- [ ] **Step 3C.2: Docs** — in `docs/architecture-mail.md`, after the paragraph that starts `**The message list is an ARIA grid, and the row is four cells.**`, add one paragraph:

```markdown
**A row swipes on touch, and the band is not a cell.** `useSwipe` (`src/hooks/useSwipe.ts`) moves the `role="row"` by inline transform — a finger's travel costs no render — and answers only to `touch`/`pen`; `touch-action: pan-y pinch-zoom` leaves the vertical scroll and pull-to-refresh to the browser. The band under it is the slot's first child, `aria-hidden`, so the row keeps its four cells. Each side runs `mail.swipeRight`/`mail.swipeLeft`: Read/unread and Star apply at once; Archive and Delete leave through `useDeferredMove`, which holds the move five seconds behind an Undo toast — the caches are patched at `mutate`, the request waits for the hold (`MoveMessagesArgs.hold`), and a cancelled hold rolls back in silence. The send follows the toast's own expiry, so a toast paused under the keyboard holds the move too; a second swipe, a folder, page, search or account change, a hidden tab or `pagehide` send it at once, with `keepalive`. While a held move waits, `useListRefresh` skips the list (the write guard) and `useFolders` keeps its cached tree (`holdsPendingMove`), or a poll would put the row or the badge back.
```

- [ ] **Step 3C.3: Full frontend suite and build**

Run: `npm run lint && npx tsc --noEmit -p . && npx vitest run && npm run build`
Expected: all green. One known intermittent failure is classed (a lazy chunk against a 1 s budget); rerun it alone before treating it as real.

- [ ] **Step 3C.4: Real-device check (owner, Android Chrome, deployed dev build)** — list for the owner, not automatable:
  1. Swipe a row left and right; the row follows, the band turns colour at about a third, a light vibration.
  2. A diagonal scroll never moves a row; pull-to-refresh still works at the top.
  3. **The native drag-and-drop does not start under the finger** (rows are `draggable`). If it does, set `draggable={!crossFolder && !swipe.state.side}` in `MessageRow` and recheck.
  4. Delete, then Undo within five seconds: the row comes back, the Inbox badge never flickers.
  5. Delete, then switch folder at once: the message is in the Trash.

- [ ] **Commit the task**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null || true
git add src/frontend
git commit -F - <<'EOF2'
Mail list: swipe a row to run the chosen action
EOF2
```
