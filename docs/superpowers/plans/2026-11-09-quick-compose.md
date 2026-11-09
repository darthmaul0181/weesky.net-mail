# Quick compose Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An opt-in setting opens the composer in a floating window docked at the bottom right. The window lives in the app shell and survives navigation anywhere in the app.

**Architecture:**
- `ComposeForm` leaves `ComposeView.tsx` and becomes route-independent. It takes its seed, its exit and its frame (`page` | `window`) as props.
- `useLeaveGuard` gains a window mode: no router blocker; `beforeunload`, `confirmLeave` and the ✕ question stay.
- A `QuickComposeProvider` mounted in `AppShell` holds the window's state and renders `QuickComposeWindow`.
- One hook, `useComposer()`, decides page or window for every entry point.

**Tech Stack:** React 19, TypeScript, react-router v7, TanStack Query, Vitest + RTL. Backend: ASP.NET (preference registry only).

**Spec:** `docs/superpowers/specs/2026-11-09-quick-compose-design.md`

## Global Constraints

- **Preference:** `mail.composeWindow`, values `page` | `window`, default `page`. Any other value reads as `page`.
- **Phone tier:** the window never opens on the phone tier. `useViewport() === 'phone'` always routes to `/mail/compose`.
- **`mailto:`:** always opens the full page.
- **Default path untouched:** with the preference at `page`, routes, markup and the existing `ComposeView.test.tsx` assertions do not change.
- **One window:** never two windows. An entry point never opens a window while the page composer is mounted (`useMatch('/mail/compose')`).
- **Copy:**
  - Setting label: "Write messages in".
  - Choices: "Full page" and "Floating window".
  - Hint: "The floating window stays open while you move around the app. On a phone, messages always open full page."
  - FR: « Écrire les messages dans », « Pleine page », « Fenêtre flottante ». Hint: « La fenêtre flottante reste ouverte quand vous naviguez dans l'application. Sur téléphone, les messages s'ouvrent toujours en pleine page. » Follow the French no-break space and `’` rules enforced by `parity.test.ts`.
- **Version:** bump `src/frontend/VERSION` from 2.8.0 to 2.9.0.
- **Tests:** run them with `$env:NODE_OPTIONS='--no-experimental-webstorage'` (PowerShell) from `src/frontend`.
- **Commits:** two lines max, never starting or ending with `@`. Do not push.

## Review Focus

1. **A reply into a window that holds unsaved text.** The question comes before `prepare` stages anything, and Keep editing leaves the window untouched. Test: Task 6.
2. **An expanded draft.** The page must still guard it, so an expanded `action: 'draft'` seed opens dirty. Test: Task 4.
3. **A mailbox switch with a clean window.** The window closes anyway, so a later send cannot go out from the old mailbox. Test: Task 5.
4. **A `mailto:` page composer and a dirty window alive together.** A mailbox switch asks both, and either "keep" cancels the switch. Test: Task 2.
5. **Closing a clean window from Calendar.** No navigation happens and the URL stays on `/calendar`. Test: Task 5.

---

### Task 1: The setting

**Files:**
- Modify: `src/scotty.microservice/Models/UserPreferences.cs`
  - Add the constant beside `MailComposeFormat` (line 36).
  - Add the definition after line 93.
- Test: `src/scotty.microservice/scotty.microservice.Tests/Models/UserPreferencesTests.cs`. Add rows mirroring the `MailComposeFormat` ones at lines 20, 33 and 64–68.
- Modify: `src/frontend/src/hooks/usePreferences.ts`
  - Add `composeWindow: 'mail.composeWindow'` to `PREFERENCE_KEYS`.
  - Add the accessor beside `composeFormatOf`.
- Test: `src/frontend/src/hooks/usePreferences.test.tsx`
- Modify: `src/frontend/src/modules/settings/general/MailGeneralPage.tsx`. Add a new row directly under the Default editor row (after line ~390), in the same `.field-h.is-setting.is-stacked` + `.layout-cards` radiogroup idiom, wrapped in `<WhatsNewTarget id="mail:compose-window">`.
- Modify: `src/frontend/src/locales/{en,fr}/settings.json`. Add `general.composeWindow.{label,hint,page,window,pageToast,windowToast}`.
  - Toasts EN: "New messages will open full page" and "New messages will open in a floating window".
  - Toasts FR: « Les nouveaux messages s’ouvriront en pleine page » and « Les nouveaux messages s’ouvriront dans une fenêtre flottante ».
- Modify: `src/frontend/src/modules/whatsnew/catalog.ts`. Add the entry `{ id: 'compose-window', version: '2.9.0', path: ['rail:settings', 'settings:mail'], target: 'mail:compose-window', href: '/settings/mail' }`.
- Modify: `src/frontend/src/locales/{en,fr}/whatsNew.json`. Add `items.compose-window.{title,body}`.
- Modify: `src/frontend/VERSION` → `2.9.0`.
- Test: `src/frontend/src/modules/settings/general/MailGeneralPage.test.tsx`

**Interfaces:**
- Produces:
  - `export type ComposeWindow = 'page' | 'window'`
  - `export function composeWindowOf(preferences: Preferences): ComposeWindow`
  - `PREFERENCE_KEYS.composeWindow`

- [ ] **Step 1: Write the failing backend test.** The test asserts that `mail.composeWindow` is registered with default `"page"` and allowed values `["page", "window"]`, and that `"floating"` is refused. Use the same theory/fact shape as the `MailComposeFormat` rows.
- [ ] **Step 2: Run it.** Run `dotnet test --filter UserPreferencesTests` in `src/scotty.microservice/scotty.microservice.Tests`. Expected: FAIL.
- [ ] **Step 3: Register the preference.** Add `public const string MailComposeWindow = "mail.composeWindow";` and `new(MailComposeWindow, "page", ["page", "window"])`.
- [ ] **Step 4: Run it again.** Expected: PASS. Then revert any drift in `ApiDocumentation.xml` (`git checkout -- '**/ApiDocumentation.xml'`).
- [ ] **Step 5: Write the failing frontend tests.**
  - `usePreferences.test.tsx`, test `composeWindowOf falls back to page`:
    - `{}` → `'page'`;
    - `'window'` → `'window'`;
    - `'floating'` → `'page'`.
  - `MailGeneralPage.test.tsx`, test `the compose window row saves the choice`:
    - the radiogroup named "Write messages in" holds "Full page" (checked by default) and "Floating window";
    - clicking "Floating window" calls `api.setPreference('mail.composeWindow', 'window')`;
    - the toast "New messages will open in a floating window" appears.
- [ ] **Step 6: Run them.** Run `npx vitest run src/hooks/usePreferences.test.tsx src/modules/settings/general/MailGeneralPage.test.tsx`. Expected: FAIL.
- [ ] **Step 7: Implement.** Add the accessor, the row, the locales, the catalogue entry, the What's new texts and the version bump.
- [ ] **Step 8: Run the checks.** Run `npx vitest run src/hooks src/modules/settings/general src/modules/whatsnew src/locales`. Expected: PASS, with `catalog.test.ts`, `parity.test.ts` and `keys.test.ts` included.
- [ ] **Step 9: Commit.** Message: `Add the mail.composeWindow preference and its settings row`.

### Task 2: `confirmLeave` asks every guard, and sign-out asks too

**Files:**
- Modify: `src/frontend/src/lib/leaveGuard.ts`
- Test: `src/frontend/src/lib/leaveGuard.test.ts`
- Modify: `src/frontend/src/modules/mail/compose/useLeaveGuard.ts`, lines 31–35. Use the returned unregister.
- Modify: `src/frontend/src/layouts/IdentityMenu.tsx`, `handleSignOut` (line 44).
- Test: `src/frontend/src/layouts/IdentityMenu.test.tsx`

**Interfaces:**
- Produces:
  - `export function registerLeaveGuard(fn: LeaveGuard): () => void` returns its own unregister. The `null` form is removed.
  - `export function confirmLeave(): Promise<boolean>` asks the registered guards one after another, in registration order, and stops at the first `false`.

- [ ] **Step 1: Write the failing tests** in `leaveGuard.test.ts`:
  - `asks every registered guard`: two guards both resolve `true` → `confirmLeave()` is `true`, and both were called.
  - `stops at the first refusal`: the first resolves `false` → the result is `false`, and the second was never called.
  - `unregister removes only its own guard`.
- [ ] **Step 2: Write the failing test** in `IdentityMenu.test.tsx`, `sign out asks the leave guard first`:
  - register a guard resolving `false`;
  - click Sign out;
  - `logout` was not called.
- [ ] **Step 3: Run them.** Expected: FAIL.
- [ ] **Step 4: Implement.**
  - `leaveGuard.ts`: replace the single slot with a `Set<LeaveGuard>`.
  - `useLeaveGuard`: the effect returns `registerLeaveGuard(...)`'s unregister.
  - `handleSignOut`: start with `if (!(await confirmLeave())) return`.
- [ ] **Step 5: Run the checks.** Run `npx vitest run src/lib/leaveGuard.test.ts src/layouts src/modules/mail/compose`. Expected: PASS.
- [ ] **Step 6: Commit.** Message: `Ask every open composer before leaving; guard sign-out too`.

### Task 3: Extract a route-independent `ComposeForm`

This is a pure refactor. The page behaves exactly as before.

**Files:**
- Create: `src/frontend/src/modules/mail/compose/ComposeForm.tsx`. It takes the `ComposeForm` function (ComposeView.tsx lines 90–379), `composeTitle` and `NO_FORMATS`.
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx`. It becomes the route wrapper:
  - reads `location.state` and the `mailto:` query;
  - computes `backTarget`;
  - renders `ComposeForm`.
- Modify: `src/frontend/src/modules/mail/compose/useLeaveGuard.ts`
- Test: the existing `ComposeView.test.tsx` must pass unchanged.

**Interfaces:**
- Produces, in `ComposeForm.tsx`:

```ts
export interface ComposeFormProps {
  preferences: Preferences
  /** Raw seed, before applyComposeFormat. null = new empty message. */
  seed: ComposeSeed | null
  frame: 'page' | 'window'
  onNotify: AddToast
  /** Where the form goes once it may leave: after a send, a discard, or a clean ✕. */
  onLeave: () => void
  /** Page only: the route the blocker lets through on leaveBehind. */
  backTarget?: string
  /** Expand hand-over: opens dirty and keeps seed.text/html as they are (no applyComposeFormat). */
  handedOver?: boolean
  /** Window only (Task 4). */
  onExpand?: (seed: ComposeSeed) => void
  onMinimise?: () => void
}
export default function ComposeForm(props: ComposeFormProps): JSX.Element
```

- In `useLeaveGuard.ts`:
  - New signature: `useLeaveGuard(opensChanged: boolean, hasContent: boolean, exit: LeaveExit)`, with `type LeaveExit = { mode: 'route'; path: string } | { mode: 'callback'; onLeave: () => void }`.
  - Add `requestClose(): void` to the returned object.
  - In route mode, `requestClose` navigates to `path` (the blocker asks).
  - In callback mode it opens the question when dirty and calls `onLeave` otherwise.
  - Window mode is filled in by Task 4. In this task, only `route` is used.

- [ ] **Step 1: Run the baseline.** Run `npx vitest run src/modules/mail/compose src/modules/mail/MailLayout.test.tsx src/modules/contacts`. Expected: PASS. Record the test count.
- [ ] **Step 2: Move the code.**
  - Move `ComposeForm` into `ComposeForm.tsx` with the props above.
  - `seed` replaces the `location.state`/`mailto` reads. `onLeave` and `backTarget` replace `backTargetOf` and the navigations at lines 82 and 320.
  - `ComposeView` passes `frame="page"`, `onLeave={() => navigate(backTarget)}` and `backTarget`.
  - The `close()` in the form calls `requestClose()`.
- [ ] **Step 3: Run the same command again.** Expected: PASS, with the same count and no test edited beyond import paths.
- [ ] **Step 4: Commit.** Message: `Extract a route-independent ComposeForm from ComposeView`.

### Task 4: Window frame and Expand hand-over in `ComposeForm`

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/useLeaveGuard.ts`. Add callback mode:
  - `useBlocker`'s predicate returns `false` when `exit.mode === 'callback'`;
  - `leave` and `leaveBehind` call `onLeave` instead of navigating;
  - `asking` also covers a local `closeAsk` state set by `requestClose`.
- Modify: `src/frontend/src/modules/mail/compose/ComposeForm.tsx`
- Modify: `src/frontend/src/modules/mail/compose/useStagedAttachments.ts`, only if it releases staged files on unmount. In that case, add `handOver(): void`, which disarms that release.
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx`. `ComposeState` gains `handedOver?: boolean`, passed through to the form.
- Create: `src/frontend/src/modules/mail/compose/ComposeForm.test.tsx`
- Modify: `src/frontend/src/locales/{en,fr}/compose.json`. Add `window.minimise`, `window.expand`, `window.restore` (EN "Minimise", "Expand", "Restore"; FR « Réduire », « Agrandir », « Rouvrir »).

**Interfaces:**
- Consumes: `ComposeFormProps` and `LeaveExit` (Task 3).
- Produces: in `frame="window"`, the form renders:
  - a header with the title, then Minimise (`onMinimise`), Expand and ✕, in that order;
  - its body, with the toolbar folded (`is-extra` groups behind `⋯`, as on a phone; reuse the existing `narrow` fold flag for the toolbar only);
  - a footer with Save draft and Send on the right.

  Expand calls `onExpand(snapshotSeed())`. `snapshotSeed()` is local to the form and returns a `ComposeSeed` holding:
  - the current recipients, subject, From and priority;
  - `html` = `editor.getHTML()` when HTML, or `text` when plain;
  - `attachments` = the tray items and the inline ones (with their `contentId`) as `StagedAttachmentInfo[]`;
  - `draftRef`;
  - the original `action`, `inReplyTo`, `references` and `nameHints`.

  It does not discard staged files.

- [ ] **Step 1: Write the failing tests** in `ComposeForm.test.tsx`. Render `frame="window"` inside a `MemoryRouter` at `/calendar`:
  - `closing a clean window calls onLeave and does not navigate`: the location is still `/calendar`.
  - `closing a dirty window asks first`: type a subject, click ✕ → the leave dialog appears; Discard → `onLeave` is called once.
  - `navigating away from a dirty window is not blocked`: `navigate('/contacts')` lands.
  - `expand hands over everything`: set To, Cc and the subject, type a body, and stage one attachment. Click Expand → `onExpand` receives a seed with the same `to`, `cc`, `subject`, body, `attachments[0].id` and `draftRef`. The attachment delete API was **not** called.
  - `expand keeps plain text`: switch to plain text and type → the snapshot has `text !== null`.
- [ ] **Step 2: Write the failing test** in `ComposeView.test.tsx`, `a handed-over draft opens dirty`:
  - route state `{ seed: { ...draftSeed, action: 'draft' }, handedOver: true }`;
  - click a folder link;
  - the leave dialog appears.
  - Add also `a handed-over plain-text seed stays plain text when the account default is html`.
- [ ] **Step 3: Run them.** Run `npx vitest run src/modules/mail/compose`. Expected: FAIL.
- [ ] **Step 4: Implement.**
  - Callback mode in the guard.
  - The window frame markup. Reuse `.compose-*` classes; window-only classes are prefixed `quick-compose-` and styled in Task 5.
  - `snapshotSeed`.
  - `handedOver`: it sets `opensChanged` to `true` and skips `applyComposeFormat`.
- [ ] **Step 5: Run the checks.** Run `npx vitest run src/modules/mail/compose`. Expected: PASS.
- [ ] **Step 6: Commit.** Message: `Give ComposeForm a window frame, a callback leave mode and Expand`.

### Task 5: `QuickComposeProvider`, the window and its toasts

**Files:**
- Create: `src/frontend/src/modules/mail/compose/quick/QuickComposeContext.tsx`. It holds the provider, the context and `useQuickCompose()`.
- Create: `src/frontend/src/modules/mail/compose/quick/QuickComposeWindow.tsx`. It renders the open window or the minimised bar, plus its own `useToasts()` + `<Toasts>`.
- Create: `src/frontend/src/styles/quickCompose.css`, imported in `main.tsx` after `mail.css`.
- Create: `src/frontend/src/icons/MinimiseIcon.tsx`. Reuse `ExpandIcon` for Expand.
- Modify: `src/frontend/src/layouts/AppShell.tsx`. Wrap the body in `<QuickComposeProvider>`.
- Modify: `src/frontend/src/components/FloatingAction.tsx`. It renders nothing while `useQuickCompose().state !== null`, which covers the mail, contacts and calendar floating buttons in one place.
- Test: `src/frontend/src/modules/mail/compose/quick/QuickComposeContext.test.tsx`, `src/frontend/src/components/FloatingAction.test.tsx`
- Modify: `src/frontend/docs/architecture-shell.md`. Add a paragraph: the window lives in `AppShell`, it is the one surface that outlives a module change, `FloatingAction` yields to it, and `confirmLeave` asks every guard.
- Modify: `src/frontend/probes/mobile-layout.html`. Add cases `quick-compose-open-768`, `-1024`, `quick-compose-bar-768`, `-1024`, reading `escape` and `clipped`.

**Interfaces:**
- Consumes: `ComposeForm` with `frame="window"`, `onExpand` and `onMinimise` (Task 4); `confirmLeave` (Task 2).
- Produces:

```ts
export interface QuickComposeState {
  seed: ComposeSeed | null
  accountId: string
  origin: string          // '/mail?folder=…' or a contacts path
  minimised: boolean
  instance: number        // bumped on every open → key of ComposeForm
}
export interface QuickComposeApi {
  state: QuickComposeState | null
  open(seed: ComposeSeed | null, origin: string): void   // replaces; caller has already confirmed
  restore(): void                                        // un-minimise and focus
  close(): void
  showsDraft(ref: DraftRef): boolean
}
export function useQuickCompose(): QuickComposeApi
```

The window behaves as follows:
- **Region:** `role="region"`, with `aria-label` set to its title.
- **Minimise:** the minimised bar is one `<button>` (restore), plus Expand and ✕.
- **Expand:** calls `close()`, then `navigate('/mail/compose', { state: { seed, backTo: origin, handedOver: true } })`.
- **Account switch:** an effect on `useAccountId()` calls `close()` whenever the active account differs from `state.accountId`.
- **Focus on open:** to To for a null seed, otherwise to the body.
- **The composer chunk** stays `lazy()`.

- [ ] **Step 1: Write the failing tests** in `QuickComposeContext.test.tsx`. Mount `AppShell`'s provider with a `MemoryRouter`, and call the API through a probe component:
  - `open renders the window as a region named New message`.
  - `the window survives a route change`: `open` at `/mail`, navigate to `/calendar` → still there, with its typed subject.
  - `minimise then restore keeps the content`.
  - `a mailbox switch closes a clean window` (Review Focus 3): change the mocked active account id → the region is gone.
  - `closing a clean window from /calendar does not navigate` (Review Focus 5).
  - `expand navigates to the page with the hand-over`: the location is `/mail/compose`, `state.handedOver === true`, and `state.backTo` equals the origin.
  - `a send toast shows inside the window host`.
- [ ] **Step 2: Write the failing test** in `FloatingAction.test.tsx`, `hidden while a quick compose window exists`.
- [ ] **Step 3: Run them.** Run `npx vitest run src/modules/mail/compose/quick src/components/FloatingAction.test.tsx`. Expected: FAIL.
- [ ] **Step 4: Implement** the provider, the window, the CSS, the icon, the `AppShell` wrap and the `FloatingAction` gate.
  - **Window geometry:** about 540×560, docked at the bottom right of `.app-content`, `position: fixed`, under `.modal-overlay` in z-order.
  - **Minimised bar:** about 280px wide.
  - **Tablet:** clamp with `max-width`/`max-height` against the viewport.
  - **Toasts:** placed so they do not sit under the window.
  - Every colour is a role token.
- [ ] **Step 5: Run the checks.** Run `npx vitest run src/modules/mail/compose src/components src/layouts`. Expected: PASS.
- [ ] **Step 6: Measure.** Open `probes/mobile-layout.html` through `npm run dev` in Chrome at 768×1024 and 1024×768, with device-metric emulation. Expected: `escape` is 0 on all four edges, and `clipped` is 0 for the four new cases. Paste the numbers into the commit body.
- [ ] **Step 7: Commit.** Message: `Add the quick compose window to the app shell`.

### Task 6: `useComposer()` and the five entry points

**Files:**
- Create: `src/frontend/src/modules/mail/compose/quick/useComposer.ts`
- Test: `src/frontend/src/modules/mail/compose/quick/useComposer.test.tsx`
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx`
  - `openCompose` (line 155) becomes `composer.newMessage`.
  - `openDraftInComposer` (line 166): call `prepare(draftRef)` **before** `openDraft.mutateAsync`, and replace the navigate with `composer.open`.
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx`, `openCompose` (line 252): call `prepare()` before `prepare.mutateAsync`, and replace the navigate with `composer.open`.
- Modify: `src/frontend/src/modules/contacts/ContactsLayout.tsx`, `writeTo` (line 55): `composer.open({ seed, backTo: backToHere() })`.
- Tests: `MailLayout.test.tsx`, `MessageReader.test.tsx`, `ContactsLayout.test.tsx`.
- Modify: `src/frontend/CLAUDE.md`. The composer paragraph gets one sentence on the window mode and `mail.composeWindow`.
- Modify: `src/frontend/docs/architecture-mail.md`. The compose file list gains `ComposeForm.tsx`, `quick/` and `useComposer`.

**Interfaces:**
- Consumes: `useQuickCompose()` (Task 5), `confirmLeave` (Task 2), `composeWindowOf` (Task 1).
- Produces:

```ts
export interface ComposerRequest { seed: ComposeSeed | null; from?: string; backTo?: string }
export interface Composer {
  /** 'window' iff preference is window, viewport is not phone, and /mail/compose is not mounted. */
  target: 'page' | 'window'
  /** Call before staging anything. Page target: resolves true. Window target: false when the
      same draft is already in the window (restored + focused) or the user kept editing. */
  prepare(draft?: DraftRef): Promise<boolean>
  /** Page: navigate('/mail/compose', { state: { from, seed, backTo } }). Window: quick.open(seed, backTo ?? mail origin of from). */
  open(request: ComposerRequest): void
  /** Restores an existing window instead of replacing it; otherwise open({ seed: null, from }). */
  newMessage(from?: string): void
}
export function useComposer(): Composer
```

A preference that has not loaded yet counts as `page`.

- [ ] **Step 1: Write the failing tests** in `useComposer.test.tsx`:
  - `page by default`: preference absent → `target === 'page'`; `open` navigates to `/mail/compose` with the state.
  - `window when chosen on desktop`: preference `window`, viewport `desktop` → `open` calls `quick.open`.
  - `phone always page`.
  - `page while the page composer is mounted`: route `/mail/compose` → `target === 'page'`.
  - `newMessage restores an existing window rather than replacing it`.
  - `prepare on the same draft restores and returns false without asking`.
  - `prepare with a dirty window asks; keep editing returns false`.
- [ ] **Step 2: Write the failing entry-point tests.** Each test sets the preference to `window`:
  - `MailLayout.test.tsx`:
    - `New message opens the window, not the route`.
    - `a draft row opens in the window`.
    - `keeping the window's text cancels the draft open before anything is staged`: `openDraft` is not called (Review Focus 1, draft side).
  - `MessageReader.test.tsx`:
    - `Reply opens the window`.
    - `keeping the window's text cancels the reply before prepare` (Review Focus 1).
  - `ContactsLayout.test.tsx`:
    - `Write opens the window and stays on the card`: the location is still `/contacts?id=…`.
- [ ] **Step 3: Write the failing test** in `leaveGuard.test.ts` or `QuickComposeContext.test.tsx`, `a mailbox switch with a dirty window and a mailto page composer asks both` (Review Focus 4).
- [ ] **Step 4: Run them.** Expected: FAIL.
- [ ] **Step 5: Implement** `useComposer` and wire the five call sites.
- [ ] **Step 6: Run the full suite.** Run `npx vitest run`, then `npm run lint` and `npm run typecheck`. Expected: all PASS. The full count is the baseline plus the new tests.
- [ ] **Step 7: Commit.** Message: `Route every compose entry point through useComposer`.

### After the plan

Do a browser check on the dev deployment after the user approves the push:
- open the window from each of the five entry points;
- move to Calendar and Contacts;
- minimise, restore and expand;
- send;
- ✕ with unsaved text;
- switch mailbox;
- drop a file into the window.

Report the result before opening the PR.
