# Quick compose: the composer in a floating window

## Intent

Let the user write a message without leaving what they are looking at, the way Gmail does: the composer opens in a window docked at the bottom right of the screen (mockup 1A, https://claude.ai/artifact/EQHVTzDFUpyndfJLV3C353), over whatever screen is showing. They can keep reading other mail, check the calendar or copy something from a contact while the message is open.

It is opt-in. The full-page composer at `/mail/compose` stays the default and is unchanged.

## Goals

- A setting chooses where the composer opens: **Full page** (default, today's behaviour) or **Floating window**.
- With **Floating window**, five entry points open the window instead of the page:
  - **New message**: the column button and the tablet's floating button.
  - **Reply, Reply all, Forward**: from the reader.
  - **A draft**: a click on a row in the drafts folder.
  - **Edit as new**: any other seed the reader hands to the composer today.
  - **Write**, on a contact card (`ContactsLayout.writeTo`): the window opens and the user stays on the card.
- The window survives navigation **anywhere in the app**: other folders, other messages, Calendar, Contacts, Settings. It keeps its content and its minimised/open state.
- The window can be **minimised** to a bar, **expanded** to the full-page composer without losing anything, and **closed** with the ✕.
- Nothing typed can be lost silently. The same save-or-discard question as today (Save draft / Discard / Keep editing) guards every way out.

## Non-goals

- The phone tier (below 640px). The setting is ignored there and the full-page composer opens as today.
- `mailto:` links. They arrive from the operating system with no screen behind them to keep, so they always open the full-page composer.
- Several windows at once. There is one window.
- Moving or resizing the window by dragging. It has a fixed place and size.
- Keeping the window across a page reload. Reloading with unsaved content triggers the browser's own warning, as the page composer does today.
- Autosaving drafts.
- Opening the composer in a separate browser window (pop-out).
- A quick-reply box inside the reader (that was option 2, not chosen).

## Anti-goals

- **The default must not move.** An account that never touches the setting sees exactly today's composer, routes, guard and tests.
- **No silent loss.** No click, navigation, account switch, sign-out or reply into an occupied window may throw away unsaved content without the question.
- **No second composer.** The window is the same form (same fields, editor, toolbar, attachments, send and save logic) in a different frame. Two copies of the composer would drift.
- **Expanding never re-uploads and never resets.** What was typed, the chosen editor format, the From address, staged attachments and inline images all reach the full page intact.

## The setting

- Key `mail.composeWindow`, values `page` | `window`, default `page`. Stored on the account like `mail.composeFormat`, so it follows the user from one device to another.
- It needs a backend registry entry next to `MailComposeFormat` in `src/scotty.microservice/Models/UserPreferences.cs`, with its allowed values and its default. Add an accessor `composeWindowOf` in `usePreferences.ts`. It falls back to `page` for any value it does not recognise.
- It lives in Settings › Mail › General, section **Composing**, directly under **Default editor**. Label: "Write messages in". Choices: "Full page" and "Floating window". Hint: "The floating window stays open while you move around the app. On a phone, messages always open full page."
- Changing it never affects a composer that is already open. It decides where the *next* one opens.
- The control is the builder's call, but it must be the same idiom as the Default editor row above it.

## The window (mockup 1A)

**Placement.** The window is docked at the bottom right of the content area, above the bottom edge, with a small gap from the right edge. It is drawn over the content, never beside it. Nothing reflows under it.

**Size.** Fixed: about 540px wide and about 560px tall on desktop. On a tablet, it is clamped to the viewport with a margin. The exact numbers are the builder's call, measured in `probes/mobile-layout.html` at 768×1024 and 1024×768.

**Header band**, from left to right:
- the title: the subject once one is typed, otherwise "New message", "Reply", "Forward" or "Draft", as `composeTitle` words it today;
- Minimise;
- Expand;
- ✕.

A click on the header's empty space toggles minimise, as in Gmail.

**Body.** The same fields as the full-page composer: From (only when several identities are usable), To with Cc and Bcc on demand, priority on demand, Subject, the formatting toolbar, the editor, and the attachment tray. Because the window is narrow, the toolbar uses the phone's existing fold: the `is-extra` groups go behind `⋯`. The folded field summary is phone-only and does not apply to the window.

**Footer.** Send sits at the bottom right, as in the mockup. Save draft is the builder's call: either beside Send or in a kebab, depending on what fits.

**Minimised.** The window shrinks to a bar about 280px wide, docked at the same bottom-right spot. The bar shows the title, Expand and ✕. A click on the bar restores the window.

**Drop target.** Dragging a file over the window stages it, exactly as dropping on the full-page surface does. The body still tells an inline image apart from an attachment. This is the existing `useComposeDropZone`, applied to the window's surface.

**Toasts.** There is no app-wide toast area today: each module draws its own. The window's toasts (sent, draft saved, failures) therefore need a host in the shell, beside the window *(my call)*, and it must not double the current module's toasts.

**Collisions.**
- The tablet's floating "New message" button is hidden while the window exists, because it occupies the same corner.
- Toasts must stay readable while the window is open; how they move is the builder's call, but they must not land underneath the window.

**Not a dialog.** The window does not trap focus and does not dim the page; the page behind it stays fully usable.
- For assistive tech it is a named region (`role="region"`, `aria-label` set to its title). It is not `role="dialog"`.
- Escape does nothing to the window itself *(my call: a stray Escape must never minimise or close a message)*. Menus and popovers inside it keep their own Escape through `useDismiss`, as they do now.
- Dialogs opened from it (the leave question, the plain-text confirmation) are ordinary `Modal`s.

**Focus.**
- On open, focus goes to To for a new message, to the body for a reply or forward, and to the body for a draft.
- Restoring a minimised window moves focus into it.
- Minimising leaves focus on the bar.

## Where the window lives

The window is mounted by `AppShell`, above the module outlet, so it outlives every module change. Its state lives in a provider (or context) in the shell, roughly:

```ts
{
  seed: ComposeSeed | null
  accountId: string
  origin: string
  minimised: boolean
  instance: number
}
```

- `seed` is the content the composer opens with.
- `accountId` is the mailbox the draft belongs to.
- `origin` is the screen the window was opened from: a mail folder, or the contact card for **Write**. Only Expand uses it: closing and sending never navigate *(my call: the window exists so the user stays where they are)*.
- `instance` remounts the form when the window is replaced.

The composer chunk (Squire) stays lazy. A session that never opens the window does not download it.

**One function decides page or window.** Every entry point calls a single opener (for example `openComposer({ seed, from })`) instead of `navigate('/mail/compose', …)` directly. Today those calls are in `MailLayout` (new message, draft), `MessageReader` (reply and forward) and `ContactsLayout.writeTo` (its `backTo` becomes the `origin`). The opener:

- opens the window when the preference is `window` and the viewport is not `phone`;
- otherwise navigates to `/mail/compose` with today's state, exactly as now.
- also navigates to the page while a page composer is mounted (after a `mailto:` or an Expand), so two composers are never open through an entry point *(my call)*.

## The composer becomes route-independent

`ComposeForm` reads its seed from `location.state` (or from the `mailto:` query) and its exit target from `backTargetOf(state)`. It navigates on send and on close. The refactor turns those into inputs:

- **The seed is a prop.** The route wrapper (`ComposeView`) keeps reading `location.state` and the `mailto:` query and passes the seed down. The window passes its own.
- **"Leave" is a callback.** On the page it navigates to the back target, as today. In the window it closes the window.
- **The guard has two modes** (see below).
- **A `frame` prop** (`'page' | 'window'`) selects the header layout and the window-specific controls. It does not fork the form logic.

The page composer's behaviour, markup and tests stay as they are. `ComposeView.test.tsx` must pass unchanged, apart from any mechanical update to how the seed is provided.

## The leave guard in window mode

Today `useLeaveGuard` has three parts:
- a router blocker on every navigation;
- `beforeunload`;
- the module-level `registerLeaveGuard` slot that the mailbox switch asks.

The page mode keeps all three.

The window mode must not block navigation, since moving around is the point. It keeps:

| Trigger | What happens |
|---|---|
| `beforeunload` | The browser's warning, as today. |
| Mailbox switch (identity menu) | The save-or-discard question when unsaved, through `confirmLeave`. The window closes on **every** switch, asked or not, since its draft is bound to the mailbox it was opened in *(my call)*. |
| Sign-out | The same question before signing out *(my call: today the page composer is not asked on sign-out either. If the existing sign-out path does not call `confirmLeave`, add the call; it then covers both modes)*. |
| ✕ with unsaved content | The question. With nothing unsaved, the window closes and its staged files are discarded, as the page's ✕ does. |
| Reply, Reply all, Forward or a draft while the window holds unsaved content | The question first. Then the window is replaced, or kept on Keep editing. With nothing unsaved, it is replaced directly. For a draft, the question (and the same-draft check below) comes **before** `openDraftInComposer` stages its attachments, so Keep editing leaves nothing staged. |
| Same draft clicked again while open in the window | Restore and focus the window. No question. |
| New message while a window exists | Restore and focus the existing window. It is not replaced *(my call: one click must not cost a question, and the user can close it first)*. |
| Send succeeds | The window closes where the user is, with the same toasts and draft cleanup as the page. |
| Save draft | The window stays open and becomes clean, as the page does. |

**`registerLeaveGuard` holds one slot today.** A `mailto:` can open the page composer while a window exists. That is the one case where two composers are alive at once, so both must be asked. `confirmLeave` therefore has to consult every registered guard. How it does that (a set, or the window refusing the slot while a page composer holds it) is the builder's call. The requirement is that neither composer's unsaved content can be skipped.

## Expand: window → full page

Expand hands the window's live content to the full-page composer and closes the window. Nothing is lost, discarded or re-uploaded:
- recipients;
- subject;
- body, in its current format: HTML stays HTML, and plain text stays plain text, overriding `applyComposeFormat` the way a resumed draft does;
- From;
- priority;
- staged attachment ids and inline image ids;
- the `draftRef` of a draft already saved;
- the dirty state, so content that was unsaved stays guarded. It travels as an explicit flag: `useLeaveGuard` derives 'unsaved at open' from `seed.action !== 'draft'`, so an expanded draft keeping `action: 'draft'` would otherwise open as saved.

Expanding always lands on the full-page composer in Mail, wherever the user is. Its back target is the window's `origin`: leaving that page returns to the mail folder or the contact card the window was opened from.

The editor's undo history may be lost across the handover; that is acceptable.

How the snapshot travels is the builder's call. It could be a seed built from the form state, or a shared store. One constraint: the window's unmount must **not** call `discardAll` on staged attachments that the page is about to adopt.

There is no reverse path (page → window). The page composer has no Minimise button.

## What's new

- Bump `src/frontend/VERSION` to the next minor (from 2.8.0 to 2.9.0, unless master has moved).
- Add an entry for the setting:
  - path `['rail:settings', 'settings:mail']`;
  - target the "Write messages in" row, wrapped in a `WhatsNewTarget`;
  - texts in EN and FR.
- `catalog.test.ts` must pass.

## Verification

**Unit tests:**
- the opener's routing (page vs window, phone override, `mailto:` always page);
- the window's five entry points;
- the guard table above, one test per row;
- that expanding keeps every listed field, keeps the attachments staged, and that the page's leave guard sees the expanded content as dirty;
- that the default (`page`) path is untouched;
- the setting row;
- the backend registry entry (allowed values, default).

**Geometry:**
- New cases in `probes/mobile-layout.html` for the open window and the minimised bar at 768×1024 and 1024×768, read for `escape` and `clipped`.
- The toast position next to the window.

**Browser check before the PR**, on the dev deployment (the local build cannot sign in):
- open from each entry point;
- move to Calendar and Contacts and back;
- minimise, restore and expand;
- send;
- the leave question on ✕ and on a mailbox switch;
- drag and drop a file into the window.

## Docs

- Update `src/frontend/CLAUDE.md`'s composer description and `docs/architecture-mail.md`'s compose files.
- Add a paragraph on the window to `docs/architecture-shell.md`: it lives in the shell, and it is the one surface that outlives a module change.

## Left to the builder

- The exact window and bar sizes, the toast arrangement, and where Save draft sits.
- The shape of the shell state (provider, context or store) and of the expand snapshot, within the constraints above.
- How `confirmLeave` consults two guards.
- File and component names.
