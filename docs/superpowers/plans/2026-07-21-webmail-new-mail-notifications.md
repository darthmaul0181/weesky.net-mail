# New-mail notifications (sound and desktop) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** two independent settings, both off by default — play a sound, and raise a desktop
notification, when mail arrives in the inbox.

**Architecture:** the trigger already exists — the folder poll reports `uidNext` per folder every
minute. A pure decision function turns two folder snapshots plus the two settings into a
notification or nothing; a hook in `AppShell` acts on it, so notifications fire from anywhere in
the app. The desktop permission always outranks the stored preference.

**Tech Stack:** React 18 + TypeScript, TanStack Query v5, Vitest + jsdom + Testing Library;
Notification API, HTMLAudioElement. Backend: .NET 10, xUnit (registry only).

**Spec:** `docs/superpowers/specs/2026-07-21-webmail-new-mail-notifications-design.md`

## Global Constraints

- Both settings default to `"false"`. An app that makes noise nobody asked for is a defect.
- **The browser permission always outranks the preference.** Denied → the preference stays
  `false` and a message explains; revoked between sessions → the toggle renders off.
- `Notification.requestPermission()` is called **inside the click gesture**, never on mount.
- **Only `uidNext` triggers.** Not `total`, not `unread`, not `highestModSeq` — a deletion or a
  read-flip made elsewhere must not ring.
- The first observation of the inbox is the **baseline** and never notifies.
- The inbox is found through `specialUse === 'inbox'` (the role resolution chain), never by
  matching the name `INBOX`.
- **One notification per tick**, never one per message. One message → sender and subject;
  several → "N new messages".
- New messages are selected by **`uid >= the previous uidNext`**, never by taking the top rows:
  the server sorts by `Date` header, so a late-delivered message lands mid-list.
- `refetchIntervalInBackground` is **conditional on either setting being on**.
- Failures are silent: a rejected `play()`, a failed fetch, a blocked notification — never a toast.
- The sound is RainLoop's `new-mail.mp3`, MIT, and `THIRD-PARTY.md` carries the notice —
  without it the reuse is not licensed.
- Project rules (CLAUDE.md): comments only where the code is not self-evident, 3 lines max; no
  code duplication; think about performance; UI copy in English; commit messages two lines max.
- `dotnet test` (not `--no-build`) whenever a new test file is added.

---

### Task 1: Backend — two new preference keys

**Files:**
- Modify: `src/snoopy.microservice/Models/UserPreferences.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `UserPreferences.MailNotifySound` = `"mail.notifySound"` and
  `UserPreferences.MailNotifyDesktop` = `"mail.notifyDesktop"`, both defaulting to `"false"`,
  both accepting only `"true"`/`"false"`.

- [ ] **Step 1: Write the failing tests**

In `UserPreferencesTests.cs`, add to the `All_CarriesTheKeysTheClientOffers` fact:

```csharp
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailNotifySound);
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailNotifyDesktop);
```

Add two rows to the `Default_IsTheValueAnAccountWithNoRowsGets` theory:

```csharp
    [InlineData(UserPreferences.MailNotifySound, "false")]
    [InlineData(UserPreferences.MailNotifyDesktop, "false")]
```

And four rows to the `IsValid_AcceptsOnlyTheOfferedValues` theory:

```csharp
    [InlineData(UserPreferences.MailNotifySound, "true", true)]
    [InlineData(UserPreferences.MailNotifySound, "1", false)]
    [InlineData(UserPreferences.MailNotifyDesktop, "false", true)]
    [InlineData(UserPreferences.MailNotifyDesktop, "yes", false)]
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter UserPreferencesTests`

Expected: FAIL — `MailNotifySound` does not exist (a compile error is the failure here).

- [ ] **Step 3: Add the keys**

In `UserPreferences.cs`, beside the existing constants:

```csharp
    public const string MailNotifySound = "mail.notifySound";
    public const string MailNotifyDesktop = "mail.notifyDesktop";
```

And in the `All` collection, after the existing entries:

```csharp
        new(MailNotifySound, "false", Booleans),
        new(MailNotifyDesktop, "false", Booleans),
```

- [ ] **Step 4: Run to verify they pass, then the whole suite**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests`

Expected: PASS, no failures.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/UserPreferences.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs
git commit -m "Accept the two notification preferences

Both default to false: an app that rings unasked is a defect."
```

---

### Task 2: The decision, as a pure function

**Files:**
- Create: `src/frontend/src/modules/mail/notify/notifyDecision.ts`
- Test: `src/frontend/src/modules/mail/notify/notifyDecision.test.ts`

**Interfaces:**
- Consumes: `MailMessageSummary` from `../api/mailTypes`.
- Produces:

```ts
export interface NotifySettings { sound: boolean; desktop: boolean }
export interface NotifyDecision { count: number; sinceUid: number }
export function notifyDecision(
  previousUidNext: number | null, nextUidNext: number | null, settings: NotifySettings,
): NotifyDecision | null
export function notifyBody(messages: MailMessageSummary[], count: number): string
export function newSince(messages: MailMessageSummary[], sinceUid: number): MailMessageSummary[]
```

- [ ] **Step 1: Write the failing tests**

Create `src/frontend/src/modules/mail/notify/notifyDecision.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import type { MailMessageSummary } from '../api/mailTypes'
import { newSince, notifyBody, notifyDecision } from './notifyDecision'

const both = { sound: true, desktop: true }

function message(uid: number, fromName = '', subject = ''): MailMessageSummary {
  return {
    uid, subject, fromName, fromAddress: 'a@b.c', date: '2026-07-21T00:00:00Z',
    seen: false, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
  }
}

describe('notifyDecision', () => {
  it('reports the arrivals and where they start', () => {
    expect(notifyDecision(10, 13, both)).toEqual({ count: 3, sinceUid: 10 })
  })

  // Opening the webmail would ring every time otherwise.
  it('stays silent on the baseline observation', () => {
    expect(notifyDecision(null, 13, both)).toBeNull()
  })

  it('stays silent when nothing arrived', () => {
    expect(notifyDecision(13, 13, both)).toBeNull()
  })

  // uidNext only ever rises; a fall means the folder was rebuilt, not that mail arrived.
  it('stays silent when uidNext went backwards', () => {
    expect(notifyDecision(20, 13, both)).toBeNull()
  })

  it('stays silent when the server stops reporting uidNext', () => {
    expect(notifyDecision(10, null, both)).toBeNull()
  })

  it.each([
    [{ sound: false, desktop: false }],
  ])('stays silent when both settings are off', settings => {
    expect(notifyDecision(10, 13, settings)).toBeNull()
  })

  it.each([
    [{ sound: true, desktop: false }],
    [{ sound: false, desktop: true }],
  ])('fires when either setting alone is on', settings => {
    expect(notifyDecision(10, 13, settings)).not.toBeNull()
  })
})

describe('newSince', () => {
  // The server sorts by Date header, so a late-delivered message lands mid-list: the new ones
  // are the ones whose uid the folder had not assigned yet, not the ones at the top.
  it('picks the messages the folder had not assigned yet', () => {
    const messages = [message(12), message(5), message(11), message(4)]

    expect(newSince(messages, 10).map(m => m.uid)).toEqual([12, 11])
  })

  it('answers empty when none qualify', () => {
    expect(newSince([message(5), message(4)], 10)).toEqual([])
  })
})

describe('notifyBody', () => {
  it('names the sender and subject of a single message', () => {
    expect(notifyBody([message(11, 'Alice Dupont', 'Lunch?')], 1)).toBe('Alice Dupont — Lunch?')
  })

  it('falls back to the address when there is no display name', () => {
    expect(notifyBody([message(11, '', 'Lunch?')], 1)).toBe('a@b.c — Lunch?')
  })

  it('says so when a message carries no subject', () => {
    expect(notifyBody([message(11, 'Alice Dupont', '')], 1)).toBe('Alice Dupont — (no subject)')
  })

  it('counts instead of naming when several arrived', () => {
    expect(notifyBody([message(12), message(11)], 2)).toBe('2 new messages')
  })

  // The count comes from uidNext, the messages from a fetch that may not have found them —
  // a late-delivered message sorts out of the first block. Counting is honest; naming the
  // wrong message is not.
  it('counts when the fetch did not find the arrival', () => {
    expect(notifyBody([], 1)).toBe('1 new message')
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd src/frontend && npm run test -- notifyDecision`

Expected: FAIL — `Failed to resolve import "./notifyDecision"`.

- [ ] **Step 3: Write the implementation**

Create `src/frontend/src/modules/mail/notify/notifyDecision.ts`:

```ts
import type { MailMessageSummary } from '../api/mailTypes'

export interface NotifySettings {
  sound: boolean
  desktop: boolean
}

export interface NotifyDecision {
  count: number
  /** The uidNext before the arrivals: every new message has a uid at least this high. */
  sinceUid: number
}

/**
 * Whether this poll tick should notify, and about how many messages. uidNext alone decides:
 * a deletion or a read-flip made in another client moves the other counters, not this one.
 */
export function notifyDecision(
  previousUidNext: number | null,
  nextUidNext: number | null,
  settings: NotifySettings,
): NotifyDecision | null {
  if (!settings.sound && !settings.desktop) return null
  if (previousUidNext === null || nextUidNext === null) return null
  if (nextUidNext <= previousUidNext) return null

  return { count: nextUidNext - previousUidNext, sinceUid: previousUidNext }
}

/** The arrivals, by uid rather than by position: the list is sorted by Date header, so a
    late-delivered message sits mid-list and the top rows are not the new ones. */
export function newSince(messages: MailMessageSummary[], sinceUid: number): MailMessageSummary[] {
  return messages.filter(message => message.uid >= sinceUid)
}

export function notifyBody(messages: MailMessageSummary[], count: number): string {
  if (count === 1 && messages.length === 1) {
    const [message] = messages
    return `${message.fromName || message.fromAddress} — ${message.subject || '(no subject)'}`
  }

  return count === 1 ? '1 new message' : `${count} new messages`
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd src/frontend && npm run test -- notifyDecision`

Expected: PASS, 15 tests.

- [ ] **Step 5: Typecheck, lint, commit**

Run: `cd src/frontend && npm run typecheck && npm run lint`

```bash
git add src/frontend/src/modules/mail/notify/notifyDecision.ts \
        src/frontend/src/modules/mail/notify/notifyDecision.test.ts
git commit -m "Decide notifications from uidNext alone

New messages are picked by uid, not by position: the sort is by Date header."
```

---

### Task 3: The two channels — sound and desktop

**Files:**
- Create: `src/frontend/src/assets/new-mail.mp3` (copied, see step 1)
- Create: `src/frontend/THIRD-PARTY.md`
- Create: `src/frontend/src/modules/mail/notify/channels.ts`
- Test: `src/frontend/src/modules/mail/notify/channels.test.ts`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:

```ts
export function playNewMailSound(): void
export function desktopPermission(): NotificationPermission | 'unsupported'
export function requestDesktopPermission(): Promise<NotificationPermission | 'unsupported'>
export function showDesktopNotification(body: string, tag: string, onClick: () => void): void
export function claimNotification(uidNext: number): boolean
```

- [ ] **Step 1: Place the asset and its licence notice**

The verified file is at
`C:\Users\mick0\AppData\Local\Temp\claude\D--development-repos-weesky-net-mail\98006c02-bee8-4d71-a5cd-57a5310a310d\scratchpad\new-mail.mp3`
(MP3, ID3v2.4 header, 1.91 s, 15,288 bytes). If it is not there, fetch it again:

```bash
curl -sL -o src/frontend/src/assets/new-mail.mp3 \
  https://raw.githubusercontent.com/RainLoop/rainloop-webmail/master/assets/sounds/new-mail.mp3
```

Copy it to `src/frontend/src/assets/new-mail.mp3`. Do **not** take the `.ogg`: every current
browser decodes MP3, and the second file is 15 KB of history from when Firefox refused MP3 and
Safari refused Vorbis.

Create `src/frontend/THIRD-PARTY.md`:

```markdown
# Third-party assets

## `src/assets/new-mail.mp3`

The new-mail notification sound, taken from RainLoop Webmail
(<https://github.com/RainLoop/rainloop-webmail>, `assets/sounds/new-mail.mp3`).

RainLoop Webmail is distributed under the MIT License, which permits this reuse provided the
notice below travels with it. Without this notice the reuse is not licensed.

```
Copyright (c) 2022 RainLoopTeam

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
```

- [ ] **Step 2: Write the failing tests**

Create `src/frontend/src/modules/mail/notify/channels.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import {
  claimNotification, desktopPermission, playNewMailSound,
  requestDesktopPermission, showDesktopNotification,
} from './channels'

const play = vi.fn()
vi.stubGlobal('Audio', vi.fn(() => ({ play, currentTime: 0 })))

describe('playNewMailSound', () => {
  beforeEach(() => { play.mockReset(); play.mockResolvedValue(undefined) })

  it('plays the sound', () => {
    playNewMailSound()

    expect(play).toHaveBeenCalled()
  })

  // Browsers refuse audio from a page nobody has interacted with, which is exactly when a
  // notification plays. A refusal must not surface: a failed notification must not produce a
  // second interruption announcing the failure.
  it('swallows a blocked play', async () => {
    play.mockRejectedValue(new Error('NotAllowedError'))

    expect(() => playNewMailSound()).not.toThrow()
    await Promise.resolve()
  })
})

describe('desktopPermission', () => {
  it('reports unsupported where the API is absent', () => {
    vi.stubGlobal('Notification', undefined)

    expect(desktopPermission()).toBe('unsupported')
  })

  it('reports what the browser says', () => {
    vi.stubGlobal('Notification', { permission: 'granted' })

    expect(desktopPermission()).toBe('granted')
  })
})

describe('requestDesktopPermission', () => {
  it('asks the browser and returns its answer', async () => {
    const requestPermission = vi.fn().mockResolvedValue('granted')
    vi.stubGlobal('Notification', { permission: 'default', requestPermission })

    await expect(requestDesktopPermission()).resolves.toBe('granted')
    expect(requestPermission).toHaveBeenCalled()
  })

  it('reports unsupported instead of throwing where the API is absent', async () => {
    vi.stubGlobal('Notification', undefined)

    await expect(requestDesktopPermission()).resolves.toBe('unsupported')
  })
})

describe('showDesktopNotification', () => {
  it('raises a tagged notification and wires the click', () => {
    const instance: Record<string, unknown> = {}
    const ctor = vi.fn(() => instance)
    vi.stubGlobal('Notification', Object.assign(ctor, { permission: 'granted' }))
    const onClick = vi.fn()

    showDesktopNotification('Alice — Lunch?', 'weesky-mail-42', onClick)

    expect(ctor).toHaveBeenCalledWith('New mail', expect.objectContaining({
      body: 'Alice — Lunch?', tag: 'weesky-mail-42',
    }))
    ;(instance.onclick as () => void)()
    expect(onClick).toHaveBeenCalled()
  })

  it('does nothing without permission', () => {
    const ctor = vi.fn()
    vi.stubGlobal('Notification', Object.assign(ctor, { permission: 'denied' }))

    showDesktopNotification('body', 'tag', vi.fn())

    expect(ctor).not.toHaveBeenCalled()
  })
})

describe('claimNotification', () => {
  beforeEach(() => localStorage.clear())

  it('lets the first caller through', () => {
    expect(claimNotification(42)).toBe(true)
  })

  // Two tabs both poll and both decide to notify. The bubble dedupes itself through its tag;
  // the sound cannot, so the claim is what keeps a second tab quiet.
  it('turns away a second tab for the same arrival', () => {
    claimNotification(42)

    expect(claimNotification(42)).toBe(false)
  })

  it('lets a later arrival through', () => {
    claimNotification(42)

    expect(claimNotification(43)).toBe(true)
  })

  it('turns away an older arrival', () => {
    claimNotification(43)

    expect(claimNotification(42)).toBe(false)
  })
})
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd src/frontend && npm run test -- channels`

Expected: FAIL — `Failed to resolve import "./channels"`.

- [ ] **Step 4: Write the implementation**

Create `src/frontend/src/modules/mail/notify/channels.ts`:

```ts
import newMailSound from '../../../assets/new-mail.mp3'

const CLAIM_KEY = 'mail.lastNotifiedUidNext'

let audio: HTMLAudioElement | null = null

/** One element, kept and rewound. Constructing one per notification would re-download on
    some browsers and lose the autoplay engagement earned by the settings click. */
export function playNewMailSound(): void {
  audio ??= new Audio(newMailSound)
  audio.currentTime = 0
  // Browsers block audio from a page nobody has interacted with — which is precisely when a
  // notification plays. A refusal is swallowed: it must not raise a second interruption.
  void audio.play()?.catch(() => {})
}

export function desktopPermission(): NotificationPermission | 'unsupported' {
  return typeof Notification === 'undefined' ? 'unsupported' : Notification.permission
}

export async function requestDesktopPermission(): Promise<NotificationPermission | 'unsupported'> {
  if (typeof Notification === 'undefined') return 'unsupported'
  return Notification.requestPermission()
}

export function showDesktopNotification(body: string, tag: string, onClick: () => void): void {
  if (desktopPermission() !== 'granted') return

  // The tag is what makes two tabs raise one bubble: an identical tag replaces rather than
  // stacks, so the browser dedupes for us.
  const notification = new Notification('New mail', { body, tag })
  notification.onclick = onClick
}

/**
 * Cross-tab guard for the sound, which has no equivalent of the notification tag. Not an
 * atomic lock: two tabs poll on independent clocks and practically never land in the same
 * millisecond, and the worst case is one duplicate beep.
 */
export function claimNotification(uidNext: number): boolean {
  try {
    const claimed = Number(localStorage.getItem(CLAIM_KEY))
    if (Number.isFinite(claimed) && claimed >= uidNext) return false
    localStorage.setItem(CLAIM_KEY, String(uidNext))
    return true
  } catch {
    // Storage denied (private mode, blocked cookies): notify rather than stay silent.
    return true
  }
}
```

- [ ] **Step 5: Teach TypeScript about the audio import**

`import newMailSound from '…mp3'` needs a module declaration. Check whether
`src/frontend/src/vite-env.d.ts` exists and references `vite/client` (which declares `*.mp3`).
If it does, nothing to do. If it does not, create it:

```ts
/// <reference types="vite/client" />
```

- [ ] **Step 6: Run to verify it passes**

Run: `cd src/frontend && npm run test -- channels && npm run typecheck`

Expected: tests PASS, typecheck clean.

- [ ] **Step 7: Lint, build and commit**

Run: `cd src/frontend && npm run lint && npm run build`

Expected: lint 0 errors (3 pre-existing warnings), build succeeds and emits the mp3 as an asset.

```bash
git add src/frontend/src/assets/new-mail.mp3 \
        src/frontend/THIRD-PARTY.md \
        src/frontend/src/modules/mail/notify/channels.ts \
        src/frontend/src/modules/mail/notify/channels.test.ts
git add src/frontend/src/vite-env.d.ts 2>/dev/null || true
git commit -m "Add the sound and desktop notification channels

RainLoop's MIT sound, with its notice; a localStorage claim keeps tabs quiet."
```

---

### Task 4: The settings rows and the permission dance

**Files:**
- Modify: `src/frontend/src/hooks/usePreferences.ts`
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx`
- Test: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`
- Test: `src/frontend/src/hooks/usePreferences.test.tsx`

**Interfaces:**
- Consumes: `desktopPermission`, `requestDesktopPermission`, `playNewMailSound` (Task 3).
- Produces:
  - `PREFERENCE_KEYS.notifySound` = `'mail.notifySound'`,
    `PREFERENCE_KEYS.notifyDesktop` = `'mail.notifyDesktop'`
  - `notifySoundOf(preferences): boolean`, `notifyDesktopOf(preferences): boolean` — both
    `=== 'true'`, i.e. **off unless explicitly on**, the mirror of `showPreviewOf`.

- [ ] **Step 1: Write the failing accessor tests**

In `src/frontend/src/hooks/usePreferences.test.tsx`, add to the accessors describe block:

```tsx
  // The mirror of showPreviewOf: a key the backend has not sent yet must leave the app silent,
  // never guess that the user wanted noise.
  it.each([
    ['true', true],
    ['false', false],
    [undefined, false],
  ])('reads notifySound %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.notifySound]: stored }

    expect(notifySoundOf(preferences)).toBe(expected)
  })

  it.each([
    ['true', true],
    ['false', false],
    [undefined, false],
  ])('reads notifyDesktop %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.notifyDesktop]: stored }

    expect(notifyDesktopOf(preferences)).toBe(expected)
  })
```

Add `notifySoundOf, notifyDesktopOf` to the file's import from `./usePreferences`.

- [ ] **Step 2: Run to verify it fails**

Run: `cd src/frontend && npm run test -- usePreferences`

Expected: FAIL — `notifySoundOf is not a function`.

- [ ] **Step 3: Add the keys and accessors**

In `src/frontend/src/hooks/usePreferences.ts`, extend `PREFERENCE_KEYS`:

```ts
export const PREFERENCE_KEYS = {
  pageSize: 'mail.pageSize',
  showPreview: 'mail.showPreview',
  notifySound: 'mail.notifySound',
  notifyDesktop: 'mail.notifyDesktop',
} as const
```

And beside `showPreviewOf`:

```ts
/** Off unless explicitly on — the mirror of showPreviewOf, because silence is the safe default. */
export function notifySoundOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.notifySound] === 'true'
}

export function notifyDesktopOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.notifyDesktop] === 'true'
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd src/frontend && npm run test -- usePreferences`

Expected: PASS.

- [ ] **Step 5: Write the failing settings-page tests**

In `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`, add to the hoisted mocks
`playNewMailSound: vi.fn(), desktopPermission: vi.fn(), requestDesktopPermission: vi.fn()`, and:

```tsx
vi.mock('../../../modules/mail/notify/channels', () => ({
  playNewMailSound: mocks.playNewMailSound,
  desktopPermission: mocks.desktopPermission,
  requestDesktopPermission: mocks.requestDesktopPermission,
}))
```

Then a new describe block (adapt `renderPage` to the file's existing helper, which takes the
preferences map):

```tsx
describe('GeneralPage notifications', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.desktopPermission.mockReturnValue('default')
  })

  it('shows both toggles off by default', async () => {
    renderPage()

    expect(await screen.findByLabelText('Sound on new mail')).not.toBeChecked()
    expect(screen.getByLabelText('Desktop notification on new mail')).not.toBeChecked()
  })

  // Enabling is the interaction that unlocks autoplay, so the confirmation sound doubles as
  // proof it works and as the engagement the browser remembers.
  it('plays the sound when the sound toggle is switched on', async () => {
    renderPage()

    fireEvent.click(await screen.findByLabelText('Sound on new mail'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.notifySound', 'true'))
    expect(mocks.playNewMailSound).toHaveBeenCalled()
  })

  it('does not play when the sound toggle is switched off', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.notifySound': 'true' })

    fireEvent.click(await screen.findByLabelText('Sound on new mail'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.notifySound', 'false'))
    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
  })

  it('asks the browser when the desktop toggle is switched on', async () => {
    mocks.requestDesktopPermission.mockResolvedValue('granted')
    renderPage()

    fireEvent.click(await screen.findByLabelText('Desktop notification on new mail'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.notifyDesktop', 'true'))
  })

  // Storing a true that produces nothing is the lying switch: on, and silent.
  it('leaves the setting off and explains when the browser refuses', async () => {
    mocks.requestDesktopPermission.mockResolvedValue('denied')
    renderPage()

    fireEvent.click(await screen.findByLabelText('Desktop notification on new mail'))

    expect(await screen.findByText(/blocked/i)).toBeInTheDocument()
    expect(mocks.setPreference).not.toHaveBeenCalledWith('mail.notifyDesktop', 'true')
    expect(screen.getByLabelText('Desktop notification on new mail')).not.toBeChecked()
  })

  // Granted yesterday, revoked today in browser settings, preference still true.
  it('renders the desktop toggle off when the permission was revoked', async () => {
    mocks.desktopPermission.mockReturnValue('denied')
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.notifyDesktop': 'true' })

    expect(await screen.findByLabelText('Desktop notification on new mail')).not.toBeChecked()
    expect(screen.getByText(/blocked/i)).toBeInTheDocument()
  })

  it('says so where the browser has no notifications at all', async () => {
    mocks.desktopPermission.mockReturnValue('unsupported')
    renderPage()

    expect(await screen.findByText(/does not support/i)).toBeInTheDocument()
    expect(screen.getByLabelText('Desktop notification on new mail')).toBeDisabled()
  })

  it('switching the desktop toggle off needs no permission', async () => {
    mocks.desktopPermission.mockReturnValue('granted')
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.notifyDesktop': 'true' })

    fireEvent.click(await screen.findByLabelText('Desktop notification on new mail'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.notifyDesktop', 'false'))
    expect(mocks.requestDesktopPermission).not.toHaveBeenCalled()
  })
})
```

- [ ] **Step 6: Run to verify it fails**

Run: `cd src/frontend && npm run test -- GeneralPage`

Expected: FAIL — `Unable to find a label with the text of: Sound on new mail`.

- [ ] **Step 7: Add the two rows**

In `GeneralPage.tsx`, extend the imports:

```tsx
import { useState } from 'react'
import {
  ALL, PREFERENCE_KEYS, notifyDesktopOf, notifySoundOf, showPreviewOf,
  usePreferences, useSetPreference,
} from '../../../hooks/usePreferences'
import {
  desktopPermission, playNewMailSound, requestDesktopPermission,
} from '../../mail/notify/channels'
```

Inside the component, above `save`:

```tsx
  const [permission, setPermission] = useState(desktopPermission)
  const blocked = permission === 'denied'
  const unsupported = permission === 'unsupported'

  async function toggleSound(on: boolean) {
    await save(PREFERENCE_KEYS.notifySound, String(on),
      on ? 'New mail will play a sound' : 'New mail will be silent')
    // Played inside the click: it proves the sound works and earns the browser engagement
    // that lets a later, unattended notification play at all.
    if (on) playNewMailSound()
  }

  async function toggleDesktop(on: boolean) {
    if (!on) {
      await save(PREFERENCE_KEYS.notifyDesktop, 'false', 'Desktop notifications are off')
      return
    }

    // Asked inside the click gesture — Safari requires it, and a denied answer must not be
    // stored as an enabled setting that produces nothing.
    const answer = await requestDesktopPermission()
    setPermission(answer)
    if (answer === 'granted') {
      await save(PREFERENCE_KEYS.notifyDesktop, 'true', 'New mail will raise a notification')
    }
  }
```

And after the preview row, inside the same fragment:

```tsx
          <div className="field-h is-setting">
            <label htmlFor="notify-sound">Sound on new mail</label>
            <label className="toggle-switch">
              <input
                id="notify-sound"
                type="checkbox"
                checked={notifySoundOf(preferences)}
                disabled={setPreference.isPending}
                onChange={event => toggleSound(event.target.checked)}
              />
              <span className="toggle-track" />
            </label>
          </div>

          <div className="field-h is-setting">
            <label htmlFor="notify-desktop">Desktop notification on new mail</label>
            <label className="toggle-switch">
              <input
                id="notify-desktop"
                type="checkbox"
                checked={notifyDesktopOf(preferences) && permission === 'granted'}
                disabled={setPreference.isPending || unsupported}
                onChange={event => toggleDesktop(event.target.checked)}
              />
              <span className="toggle-track" />
            </label>
          </div>

          {blocked && (
            <p className="settings-note">
              Your browser is blocking notifications for this site. Allow them in its site
              settings, then switch this back on.
            </p>
          )}
          {unsupported && (
            <p className="settings-note">
              This browser does not support desktop notifications. On iPhone and iPad they work
              only once the site is added to the home screen.
            </p>
          )}
```

- [ ] **Step 8: Style the note**

Append to `src/frontend/src/styles/shell.css`:

```css
.settings-note {
  max-width: 520px;
  margin: -4px 0 12px;
  font-size: 12px;
  color: var(--text-muted);
}
```

Check `--text-muted` exists in `src/frontend/src/styles/tokens.css` or the palette files before
using it; never introduce a literal colour.

- [ ] **Step 9: Run to verify it passes**

Run: `cd src/frontend && npm run test -- GeneralPage`

Expected: PASS.

- [ ] **Step 10: Typecheck, lint, whole suite, commit**

Run: `cd src/frontend && npm run typecheck && npm run lint && npm run test`

```bash
git add src/frontend/src/hooks/usePreferences.ts \
        src/frontend/src/hooks/usePreferences.test.tsx \
        src/frontend/src/modules/settings/general/GeneralPage.tsx \
        src/frontend/src/modules/settings/general/GeneralPage.test.tsx \
        src/frontend/src/styles/shell.css
git commit -m "Add the two notification settings

The permission outranks the preference: a refusal never stores an enabled setting."
```

---

### Task 5: The watcher, and waking the poll

**Files:**
- Create: `src/frontend/src/modules/mail/notify/useMailNotifications.ts`
- Test: `src/frontend/src/modules/mail/notify/useMailNotifications.test.tsx`
- Modify: `src/frontend/src/modules/mail/queries.ts` (conditional background polling)
- Modify: `src/frontend/src/layouts/AppShell.tsx` (one hook call)
- Test: `src/frontend/src/modules/mail/queries.test.tsx`

**Interfaces:**
- Consumes: `notifyDecision`, `newSince`, `notifyBody` (Task 2); `playNewMailSound`,
  `showDesktopNotification`, `claimNotification` (Task 3); `notifySoundOf`, `notifyDesktopOf`
  (Task 4); `useFolders`, `useAccountId`, `mailKeys` and `mailKeys.messageStream`;
  `isStreaming` / `requestSizeOf` from `hooks/usePreferences`; `flatten` from
  `../folders/folderNodes`; `api.getMailMessages(folder, page, pageSize)`;
  `useNavigate` from `react-router-dom`.
- Produces: `useMailNotifications(): void` — side effects only.

- [ ] **Step 1: Write the failing background-polling test**

In `src/frontend/src/modules/mail/queries.test.tsx`, add to the `useFolders` describe block:

```tsx
  // A notification is useful only while the tab is elsewhere, so the poll has to survive the
  // loss of focus — but only for those who asked: an untouched tab must keep costing nothing.
  it.each([
    [{ 'mail.notifySound': 'false', 'mail.notifyDesktop': 'false' }, false],
    [{ 'mail.notifySound': 'true', 'mail.notifyDesktop': 'false' }, true],
    [{ 'mail.notifySound': 'false', 'mail.notifyDesktop': 'true' }, true],
  ])('polls in the background only when a notification is on', async (preferences, expected) => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', ...preferences })
    mocks.getMailFolders.mockResolvedValue([])
    const { wrapper, client } = createWrapper()

    renderHook(() => useFolders(), { wrapper })

    await waitFor(() =>
      expect(client.getQueryCache().find({ queryKey: mailKeys.folders('primary') })).toBeDefined())
    await waitFor(() => expect(
      client.getQueryCache().find({ queryKey: mailKeys.folders('primary') })!
        .options.refetchIntervalInBackground).toBe(expected))
  })
```

`createWrapper()` must expose its `client`; if it does not yet, return it alongside `wrapper` and
leave the existing call sites working (they destructure `{ wrapper }`). Add `getPreferences` to
the file's hoisted `api` mocks if it is absent.

- [ ] **Step 2: Run to verify it fails**

Run: `cd src/frontend && npm run test -- queries`

Expected: FAIL — `refetchIntervalInBackground` is `undefined`, not `false`/`true`.

- [ ] **Step 3: Make background polling conditional**

In `queries.ts`, import the accessors and read the preferences:

```ts
import { notifyDesktopOf, notifySoundOf, usePreferences } from '../../hooks/usePreferences'
```

```ts
export function useFolders() {
  const accountId = useAccountId()
  const { data: preferences } = usePreferences()
  // Background polling is the cost of a notification, so only those who asked for one pay it:
  // an untouched tab keeps costing nothing.
  const notifies = preferences
    ? notifySoundOf(preferences) || notifyDesktopOf(preferences)
    : false

  return useQuery<MailFolderNode[]>({
    queryKey: mailKeys.folders(accountId),
    queryFn: ({ signal }) => api.getMailFolders({ signal }),
    refetchInterval: POLL_INTERVAL,
    refetchIntervalInBackground: notifies,
  })
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd src/frontend && npm run test -- queries`

Expected: PASS.

- [ ] **Step 5: Write the failing watcher tests**

Create `src/frontend/src/modules/mail/notify/useMailNotifications.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import type { MailFolderNode } from '../api/mailTypes'
import { mailKeys } from '../queries'
import { useMailNotifications } from './useMailNotifications'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(), getMailMessages: vi.fn(), getPreferences: vi.fn(),
  playNewMailSound: vi.fn(), showDesktopNotification: vi.fn(), claimNotification: vi.fn(),
  navigate: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))
vi.mock('./channels', () => ({
  playNewMailSound: mocks.playNewMailSound,
  showDesktopNotification: mocks.showDesktopNotification,
  claimNotification: mocks.claimNotification,
}))
vi.mock('react-router-dom', () => ({ useNavigate: () => mocks.navigate }))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function inbox(overrides: Partial<MailFolderNode> = {}): MailFolderNode {
  return {
    path: 'INBOX', name: 'INBOX', specialUse: 'inbox', selectable: true, subscribed: true,
    total: 5, unread: 2, uidValidity: 100, uidNext: 10, highestModSeq: 40, children: [],
    ...overrides,
  }
}

function pageOf(uids: number[]) {
  return {
    folderPath: 'INBOX', uidValidity: 100, total: 5, page: 0, pageSize: uids.length,
    messages: uids.map(uid => ({
      uid, subject: `Subject ${uid}`, fromName: `Sender ${uid}`, fromAddress: 'a@b.c',
      date: '2026-07-21T00:00:00Z', seen: false, flagged: false, answered: false,
      hasAttachments: false, size: 0, preview: '',
    })),
  }
}

async function renderWithBaseline(preferences: Record<string, string>, first = inbox()) {
  mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', ...preferences })
  mocks.getMailFolders.mockResolvedValue([first])

  const rendered = renderHook(() => useMailNotifications(), { wrapper })
  await waitFor(() =>
    expect(client.getQueryData(mailKeys.folders('primary'))).toBeDefined())

  return {
    ...rendered,
    tick: (next: MailFolderNode) =>
      act(() => { client.setQueryData(mailKeys.folders('primary'), [next]) }),
  }
}

const soundOn = { 'mail.notifySound': 'true', 'mail.notifyDesktop': 'false' }
const bothOn = { 'mail.notifySound': 'true', 'mail.notifyDesktop': 'true' }
const bothOff = { 'mail.notifySound': 'false', 'mail.notifyDesktop': 'false' }

describe('useMailNotifications', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.claimNotification.mockReturnValue(true)
    mocks.getMailMessages.mockResolvedValue(pageOf([11, 9]))
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('says nothing on the baseline observation', async () => {
    await renderWithBaseline(bothOn)

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  it('plays the sound when mail arrives', async () => {
    const { tick } = await renderWithBaseline(soundOn)

    await tick(inbox({ uidNext: 11, total: 6, unread: 3 }))

    await waitFor(() => expect(mocks.playNewMailSound).toHaveBeenCalledTimes(1))
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  it('names the sender and subject of a single arrival', async () => {
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 11 }))

    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalledWith(
      'Sender 11 — Subject 11', expect.any(String), expect.any(Function)))
  })

  it('counts when several arrive at once', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([12, 11, 9]))
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 12 }))

    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalledWith(
      '2 new messages', expect.any(String), expect.any(Function)))
  })

  // A deletion moves total, a read-flip moves unread; neither is new mail.
  it.each([
    ['a deletion elsewhere', inbox({ total: 4 })],
    ['a read-flip elsewhere', inbox({ unread: 1 })],
  ])('stays silent on %s', async (_label, next) => {
    const { tick } = await renderWithBaseline(bothOn)

    await tick(next)

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  it('stays silent with both settings off, and issues no fetch', async () => {
    const { tick } = await renderWithBaseline(bothOff)

    await tick(inbox({ uidNext: 11 }))

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  // The second tab must not beep: the bubble dedupes through its tag, the sound cannot.
  it('stays silent when another tab claimed the arrival', async () => {
    mocks.claimNotification.mockReturnValue(false)
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 11 }))

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
    expect(mocks.showDesktopNotification).not.toHaveBeenCalled()
  })

  it('watches the inbox by role, not by name', async () => {
    const archive = inbox({ path: 'Archive', name: 'Archive', specialUse: null })
    const { tick } = await renderWithBaseline(bothOn, archive)

    await tick(inbox({ path: 'Archive', name: 'Archive', specialUse: null, uidNext: 11 }))

    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
  })

  // Clicking must land on the message, not merely raise the window — the notification named
  // that message, so it has to be what opens.
  it('opens the named message when its notification is clicked', async () => {
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 11 }))
    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalled())

    const onClick = mocks.showDesktopNotification.mock.calls[0][2] as () => void
    onClick()

    expect(mocks.navigate).toHaveBeenCalledWith('/mail?folder=INBOX&uid=11')
  })

  it('only raises the window when several arrived', async () => {
    mocks.getMailMessages.mockResolvedValue(pageOf([12, 11, 9]))
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 12 }))
    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalled())

    ;(mocks.showDesktopNotification.mock.calls[0][2] as () => void)()

    expect(mocks.navigate).not.toHaveBeenCalled()
  })

  it('still notifies when the fetch fails, counting instead of naming', async () => {
    mocks.getMailMessages.mockRejectedValue(new Error('refused'))
    const { tick } = await renderWithBaseline(bothOn)

    await tick(inbox({ uidNext: 11 }))

    await waitFor(() => expect(mocks.showDesktopNotification).toHaveBeenCalledWith(
      '1 new message', expect.any(String), expect.any(Function)))
  })
})
```

- [ ] **Step 6: Run to verify it fails**

Run: `cd src/frontend && npm run test -- useMailNotifications`

Expected: FAIL — `Failed to resolve import "./useMailNotifications"`.

- [ ] **Step 7: Write the hook**

Create `src/frontend/src/modules/mail/notify/useMailNotifications.ts`:

```ts
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQueryClient, type InfiniteData } from '@tanstack/react-query'
import { api } from '../../../api.js'
import {
  isStreaming, notifyDesktopOf, notifySoundOf, requestSizeOf, usePreferences,
} from '../../../hooks/usePreferences'
import type { MailFolderPage } from '../api/mailTypes'
import { flatten } from '../folders/folderNodes'
import { mailKeys, useAccountId, useFolders } from '../queries'
import { claimNotification, playNewMailSound, showDesktopNotification } from './channels'
import { newSince, notifyBody, notifyDecision } from './notifyDecision'

interface Described {
  body: string
  /** The message to open on click, when exactly one arrived and was found. */
  uid: number | null
}

/** Names the arrivals if it can. The count is already known; this only improves the wording,
    so any failure falls back to counting rather than surfacing. */
async function describeArrivals(
  fetchPage: () => Promise<MailFolderPage>, sinceUid: number, count: number,
): Promise<Described> {
  try {
    const page = await fetchPage()
    const arrivals = newSince(page.messages, sinceUid)
    return {
      body: notifyBody(arrivals, count),
      uid: count === 1 && arrivals.length === 1 ? arrivals[0].uid : null,
    }
  } catch {
    return { body: notifyBody([], count), uid: null }
  }
}

/**
 * Rings for mail arriving in the inbox, whatever the user is looking at. Lives in AppShell,
 * not the mail module, so it also fires from settings and the other sections.
 */
export function useMailNotifications(): void {
  const accountId = useAccountId()
  const client = useQueryClient()
  const navigate = useNavigate()
  const { data: folders } = useFolders()
  const { data: preferences } = usePreferences()
  const previousUidNext = useRef<number | null>(null)
  const seenInbox = useRef(false)

  useEffect(() => {
    if (!folders || !preferences) return

    const inbox = flatten(folders).find(entry => entry.node.specialUse === 'inbox')?.node
    if (!inbox) return

    const previous = seenInbox.current ? previousUidNext.current : null
    seenInbox.current = true
    previousUidNext.current = inbox.uidNext

    const decision = notifyDecision(previous, inbox.uidNext, {
      sound: notifySoundOf(preferences),
      desktop: notifyDesktopOf(preferences),
    })
    if (!decision) return

    // Derived rather than read off the node: after a decision this is exactly the new uidNext,
    // and it needs no non-null assertion.
    const arrivedAt = decision.sinceUid + decision.count

    // Both tabs decided to notify; only one may. The bubble would dedupe itself through its
    // tag, the sound would not.
    if (!claimNotification(arrivedAt)) return

    // The inbox's first page is already in hand when the user is looking at it — the list
    // refresh has just fetched it. The key differs by mode, hence the branch.
    const size = requestSizeOf(preferences)
    const cached = isStreaming(preferences)
      ? client.getQueryData<InfiniteData<MailFolderPage>>(
          mailKeys.messageStream(accountId, inbox.path, size))?.pages[0]
      : client.getQueryData<MailFolderPage>(mailKeys.messages(accountId, inbox.path, 0, size))

    void describeArrivals(
      () => cached ? Promise.resolve(cached) : api.getMailMessages(inbox.path, 0, size),
      decision.sinceUid,
      decision.count,
    ).then(({ body, uid }) => {
      if (notifySoundOf(preferences)) playNewMailSound()
      if (!notifyDesktopOf(preferences)) return

      showDesktopNotification(body, `weesky-mail-${arrivedAt}`, () => {
        window.focus()
        if (uid !== null) {
          navigate(`/mail?folder=${encodeURIComponent(inbox.path)}&uid=${uid}`)
        }
      })
    })
  }, [folders, preferences, accountId, client, navigate])
}
```

- [ ] **Step 8: Run to verify it passes**

Run: `cd src/frontend && npm run test -- useMailNotifications`

Expected: PASS, 13 tests.

- [ ] **Step 9: Wire it into the shell**

In `src/frontend/src/layouts/AppShell.tsx`:

```tsx
import { Outlet } from 'react-router-dom'
import { useMailNotifications } from '../modules/mail/notify/useMailNotifications'
import AppRail from './AppRail'
import TopBar from './TopBar'

export default function AppShell() {
  // Here rather than in MailLayout: new mail must ring from settings and calendar too.
  useMailNotifications()

  return (
```

(the rest of the component unchanged.)

- [ ] **Step 10: Run typecheck, lint and the whole suite**

Run: `cd src/frontend && npm run typecheck && npm run lint && npm run test`

Expected: typecheck clean, lint 0 errors (3 pre-existing warnings), all tests pass. `AppShell`
now mounts the folders query app-wide; if an existing test renders the shell without an `api`
mock for `getMailFolders`, give it one rather than changing the hook.

- [ ] **Step 11: Commit**

```bash
git add src/frontend/src/modules/mail/notify/useMailNotifications.ts \
        src/frontend/src/modules/mail/notify/useMailNotifications.test.tsx \
        src/frontend/src/modules/mail/queries.ts \
        src/frontend/src/modules/mail/queries.test.tsx \
        src/frontend/src/layouts/AppShell.tsx
git add -u src/frontend/src
git commit -m "Ring for mail arriving in the inbox

Background polling is conditional: only those who asked for a notification pay."
```

---

### Task 6: Document the slice

**Files:**
- Modify: `src/frontend/CLAUDE.md`

**Interfaces:**
- Consumes: the finished feature.
- Produces: nothing code-facing.

- [ ] **Step 1: Update the frontend guide**

Read the shipped code first — `notify/notifyDecision.ts`, `notify/channels.ts`,
`notify/useMailNotifications.ts`, `queries.ts`, `GeneralPage.tsx` — then extend the guide in its
established register: the *why* and the counter-example, not an inventory. Cover:

- **The permission outranks the preference.** A denial never stores an enabled setting: that is
  the lying switch, on and silent. A permission revoked between sessions renders the toggle off.
  Enabling always asks inside the click gesture — Safari requires it.
- **Enabling the sound plays it.** Browsers block audio from a page nobody has interacted with,
  which is exactly when a notification plays; the confirmation both proves it works and earns
  the origin engagement Chrome and Edge remember. A rejected `play()` is swallowed — a failed
  notification must not raise a second interruption announcing the failure.
- **Only `uidNext` triggers**, and the first observation is a baseline. A deletion moves `total`,
  a read-flip moves `unread`; neither is new mail.
- **Arrivals are picked by `uid >= previous uidNext`**, never by taking the top rows: the server
  sorts by `Date` header, so a late-delivered message lands mid-list.
- **Two tabs**: the bubble dedupes natively through an identical `tag`; the sound has no
  equivalent, hence the `localStorage` claim — not an atomic lock, worst case one extra beep.
- **Clicking a single-message notification opens that message**; a counted one only raises the
  window, since there is no single message to open.
- **`refetchIntervalInBackground` is conditional on the settings** — an untouched tab must keep
  costing nothing, and a notification is only useful while the tab is elsewhere.
- The sound is RainLoop's, MIT, and `THIRD-PARTY.md` carries the notice.
- iOS/iPadOS: notifications work only for a site added to the home screen; the UI says so
  rather than failing silently.

- [ ] **Step 2: Commit**

```bash
git add src/frontend/CLAUDE.md
git commit -m "Document the new-mail notifications

Records why the permission outranks the preference and why enabling plays."
```
