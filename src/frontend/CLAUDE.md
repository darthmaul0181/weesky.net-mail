# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`frontend` — a React SPA webmail for the weesky.net mail service (mail, calendar, contacts modules plus a settings area covering account, appearance, aliases, mail rules, and admin). It talks to a backend at `https://api.mail.weesky.net`. **Mail is built for reading**: folder tree with management, paginated message list, reading pane with attachments. Composing, flags and search are not in yet. Calendar and Contacts are still placeholder (`ComingSoon`) pages; the settings module is fully built out.

## Commands

```bash
npm run dev        # start Vite dev server on port 5173
npm run build      # tsc --noEmit && vite build → dist/
npm run typecheck  # tsc --noEmit only
npm run preview    # preview the production build locally
npm run ship       # build + deploy to production server via SSH
```

Tests use Vitest + jsdom + `@testing-library/react`:
```bash
npm run test            # run once
npm run test -- --watch # watch mode
npm run test:coverage   # run with v8 coverage report
```

ESLint is configured (`eslint.config.js`, flat config, `typescript-eslint` + `eslint-plugin-react` + `eslint-plugin-react-hooks`). Run with `npm run lint`.

The codebase is a JS/TS mix: new code (router, layouts, contexts, `AccountPage`, `AppearancePage`, `lib/accountIdentity.ts`) is TypeScript (`.tsx`/`.ts`); older ported pages (`AliasesPage.jsx`, `RulesPage.jsx`, the admin tabs, `api.js`) remain JavaScript. Both are typechecked/linted together; there is no plan to force-convert the JS files.

## Architecture

**Routing** — `react-router-dom` v6, `createBrowserRouter` defined in `src/routes.tsx`. Route table:

```
/login                              LoginRoute (redirects to "/" if already logged in)
/  (RequireAuth → AppShell)
  index                             → redirect to /mail
  /mail?folder=&uid=                MailLayout         (lazy-loaded)
  /calendar                         ComingSoon
  /contacts                         ComingSoon
  /settings  (SettingsLayout)
    index                           → redirect to /settings/account
    /settings/account               AccountPage
    /settings/general               GeneralPage
    /settings/accounts              ComingSoon ("Linked accounts" — sub-project 2)
    /settings/appearance            AppearancePage
    /settings/folders               FoldersPage
    /settings/system-folders        → redirect to /settings/folders
    /settings/aliases               AliasesPage        (lazy-loaded)
    /settings/rules                 RulesPage          (lazy-loaded)
    /settings/admin  (RequireAdmin) AdminPage          (lazy-loaded)
  *                                 → redirect to /mail
```

`MailLayout`, `AliasesPage`, `RulesPage`, and `AdminPage` are `lazy()`-imported in `routes.tsx` and wrapped in `<Suspense fallback={null}>` at the route level, so the shell/settings chrome never waits on their bundle.

**Mail selection lives in search params, not route segments.** A folder path may contain `/` — the hierarchy separator is whatever the IMAP server uses — which is the same reason folder paths travel in the query string or request body on the API side rather than in a route segment. `/mail?folder=INBOX&uid=42` keeps deep links and the back button working under any separator. Choosing a folder drops `uid`, because a message id means nothing in another folder. **`MailLayout` opens the inbox when the URL names no folder** — found by the resolution chain's `inbox` role, not by matching the name, and written with `replace` so Back leaves the mail view instead of bouncing off the redirect. It never selects a message: that stays the user's call.

**`RequireAuth`** (`src/layouts/RequireAuth.tsx`) — reads `isLoggedIn` from `AuthContext`; redirects to `/login` if false, otherwise renders `<Outlet/>`. Everything except `/login` sits behind it.

**`RequireAdmin`** (`src/layouts/RequireAdmin.tsx`) — reads `isAdmin`/`accountLoaded` from `AuthContext`; renders nothing while the account is still loading (avoids a flash-redirect), then redirects non-admins to `/settings/account`.

**Shell layout** — `AppShell` (`src/layouts/AppShell.tsx`) renders `TopBar` + a body split into `AppRail` (left icon rail: Mail/Calendar/Contacts + a spacer + Settings gear, `NavLink`-driven active state) and `<main className="app-content"><Outlet/></main>`. `TopBar` (`src/layouts/TopBar.tsx`) shows the logo/wordmark and `AvatarMenu`. `AvatarMenu` (`src/layouts/AvatarMenu.tsx`) is a click-toggled dropdown (outside-click closes it via a `mousedown` listener) showing identity, the linked-accounts list (length 1 today), a Settings link, and Sign out.

**Mail module** — `MailLayout` (`src/modules/mail/MailLayout.tsx`) builds its own three columns inside the shell's single outlet, the same way `SettingsLayout` does: `.mail-folders` (folder tree plus management), `.mail-list`, `.mail-reader`.

**Every column is a band stack, not a scrolling box.** Each is `display: flex; flex-direction: column; overflow: hidden`, with exactly one band carrying `flex: 1; min-height: 0; overflow-y: auto` and the rest `flex: none`. That is what keeps the folder actions, the list heading, the pager and the attachment row on screen — all four used to sit at the end of their column's content and had to be scrolled to. **`min-height: 0` is the load-bearing part**: without it a flex child refuses to shrink below its content height, the scroll lands on the column instead of the band, and the pinned bands drift away again.

Files under `src/modules/mail/`:
- `folders/FolderTree.tsx` — hides unsubscribed folders **except the inbox** (Dovecot reports `INBOX` unsubscribed; the flag is meaningless for a folder that is always available), splits the top level into two blocks with a rule between them (`splitByRole`), refuses to select a container-only folder, and suppresses the unread badge on trash and junk — an unread count there prompts reading nobody wants to do

**The role label (`roleLabel`, the i18n seam) replaces the folder name in the tree and the list header.** In the tree (`FolderTree.tsx`), the real name stays one hover away via `title` on the row; the list header (`MessageList.tsx`) has no such fallback — the role label is all that's shown there. A stale override is signalled in Settings next to the value that discovery now provides.
- `folders/FolderDialogs.tsx` — the column footer's **two icon buttons**. Folder work is rare next to reading, and labelled buttons at the top were taking a band off the tree permanently. New folder opens `CreateFolderModal` right there — a quick action, done while looking at the tree it changes. Manage folders is a `<Link>` to `/settings/folders`, because a row per folder with a switch and two actions is not something a 240px column can lay out legibly
- `folders/CreateFolderModal.tsx` — shared by the column footer and the folders settings page, so the two can never drift into two dialects of the same dialog
- `folders/FolderManager.tsx` — the flat indented list: visibility, rename, delete, plus their dialogs. Reuses `DeleteConfirmModal`
- `folders/folderNodes.ts` — `flatten` / `parentOf` / `indent` / `isSystemFolder` / `sortFolders`. `parentOf` strips the leaf name rather than splitting on a separator, which works under any separator because the backend rejects names containing one

**Two folder orders, deliberately different.** The mail column (`FolderTree.splitByRole`) puts the folders holding a role in a block at the top — inbox, drafts, sent, archive, junk, trash, the order a reader reaches for them — then a `<hr>`, then the user's own folders by name. The rule is load-bearing, not decoration: without it the two groups run together and a well-known folder is distinguishable from an ordinary one only by recognising its name, which fails exactly where it matters, on a mailbox holding both "Drafts" and "Brouillons". It is drawn only between two populated blocks, since a rule under nothing reads as a rendering fault. System rows carry `.is-system` (weight, no colour). Everywhere the user hunts for a folder *by name* — the folders list, the create dialog's parent picker, the role selects — `folderNodes.sortFolders` puts the inbox first and then everything by name, system folders **interleaved** rather than grouped, so "Deleted Items" sits between "Courrier indésirable" and "Developpement". It compares with `localeCompare` (`sensitivity: 'base'`): a codepoint sort files every accented name after "Z", and a case-sensitive one exiles "e-commerce" past every capitalised name

**A folder holding a role carries its controls disabled, not withheld** — the switch, the rename and the delete are all on the row, greyed. Hiding one strands whatever gets filed into it; renaming or deleting one breaks the role for every client on the mailbox. Dropping the buttons entirely made those rows a different *shape* from every other one, which reads as a rendering fault; disabled beside the role badge — which sits against the name, not off in a column of its own — the rule explains itself. **The API refuses these three operations too** (`MailController.RefuseIfSystemFolderAsync`), deletion including the target's whole subtree — a guard living in one client is one new screen away from being forgotten

**A settings row is `.field-h.is-setting`, not a bare `.field-h`.** The base was drawn for the admin dialogs — a narrow box with one-word labels, so a 110px label column and the control on `flex: 1`. A settings page is the opposite shape: a wide column with sentence-length labels, and eventually twenty rows rather than two. The modifier widens the label column to 260px and lets a `select` size to its own widest option, so a page-size picker is as wide as "100" instead of as wide as the page.

**The mail dialogs are built from the admin module's parts**, not their own: `.field-h` rows, `.toggle-switch` for a boolean, one `.btn-primary` submit inside a `<form>` so Enter works, and the ✕ as the only way out — `AddEditUserModal` is the reference. A webmail with two dialects of dialog looks like two applications. Because `.field-h` puts the label *beside* its control rather than around it, every field needs an explicit `htmlFor`/`id` pair; without it the control has no accessible name and `getByLabelText` cannot reach it either.
- `list/MessageList.tsx` + `list/formatDate.ts` — rows between a fixed folder heading and a fixed footer; the page index resets when the folder changes. The footer holds a pager in paged mode and a loaded/total counter in streaming mode, never both — a footer that changes shape by mode is still one footer, not two. The preview element is always rendered even when empty, so a bodyless message does not make a shorter row than its neighbours
- `list/Pagination.tsx` + `list/pageList.ts` — numbered pages, one-based on screen and zero-based on the wire. `buildPageList` keeps the first, the last and a window around the current one, eliding the rest; it never spends a gap to hide a single page, since "…" is the same width as the number it replaces and costs a click
- `list/useMessageList.ts` — answers one shape (`messages`, `total`, `paging | null`, `streaming | null`) whether the reader is paged or scrolling "All", so `MessageList` never learns what "All" means; it renders a pager or a sentinel purely on which of the two came back non-null. `useMessages` and `useMessageStream` are both called on every render — hooks cannot be conditional — with whichever mode is inactive passed `enabled: false`, so it issues no request. A block that fails after others already loaded keeps those rows on screen and offers Retry; "Could not load messages" only appears when the failed block is the first one, because that is the only case with nothing to keep
- `list/messageStream.ts` — `dedupeByUid` exists because paging is a numeric offset: a message that arrives between two block fetches shifts every row after it by one, so the last row of an earlier block reappears as the first row of the next. `nextBlockIndex` stops the stream on a block shorter than the request size, never by comparing loaded count against `total` — `total` moves as mail arrives, so a stream chasing it would either never stop or stop one block short. `sentinelIndexOf` places the sentinel `PREFETCH_ROWS` rows before the last loaded row
- `list/LoadMoreSentinel.tsx` — a zero-height `<div>`; checked in a real browser rather than assumed, an `IntersectionObserver` fires on a zero-area target exactly as it does on a full row, at 20 rows before the end. `PREFETCH_ROWS = 20` is rows, never pixels — row height changes with the preview setting, so a pixel margin would fire at a different message count depending on a toggle that has nothing to do with scrolling. The margin has to stay well under `BLOCK_SIZE` (100): near 80, each arriving block would drop its own fresh sentinel back into view and queue the next block immediately, and the whole folder would load itself
- `list/folderDelta.ts` + `list/useListRefresh.ts` — the folder-move detector and the effect that acts on it; the *why* is in the periodic refresh note below
- `reader/MessageReader.tsx`, `reader/sanitizeBody.ts`, `reader/formatReaderDate.ts`, `reader/formatSize.ts`

**The iframe gets a document, not a bare fragment** (`renderBodyDocument`). A fragment inherits the browser's defaults: an 8px body margin that left message text visibly out of line with the header, and a serif face for any body carrying no styles. The wrapper sets padding matching `.reader-header`, contains the two overflows a body can inflict on the layout regardless of intent (`overflow-wrap: break-word` for the long unbroken URLs this mailbox is full of — **never `anywhere`**, which additionally feeds its break points into min-content sizing, collapsing a table column to one letter and rendering a GitHub mail's "Status" heading vertically, `img { max-width: 100% }` — **never `height: auto`**, which recomputes every height from the intrinsic ratio and turned a 1×1 spacer gif stretched to 154×10 by attributes into a 154px tower, wrecking newsletter buttons), and pins `color-scheme: light` with a white background in **every** theme, dark mode included — the inversion below needs a white canvas to turn black.

**Dark mode recolours the message, it does not invert it** (`reader/darkenColours.ts`). Each declared colour has its *lightness* inverted while its hue and saturation are left alone, the approach Dark Reader takes: a red button stays red, an Amazon yellow stays yellow, and a photograph is never touched. A `filter: invert(1) hue-rotate(180deg)` was tried first and is the cheap approximation — it inverts in RGB, so hues drag across the wheel, images need a second inversion to survive, and every white becomes pure black. White lands on the app's own dark surface rather than `#000`, because a black slab beside the app's greys reads as a hole. **Colours are written as hex, never `rgb()`**: `bgcolor` and friends are presentational attributes that do not understand `rgb()`, and the browser's legacy colour parsing salvages the hex digits out of `rgb(84, 22, 0)` and invents a colour from them — a brown background came out green, and a string assertion could not see it. It runs **before** sanitising, like `revealBlockedImages`, so what it writes faces the same pass as the rest. Since it is a rendering choice rather than a faithful one, `MessageReader` offers a per-message way back to the sender's colours, reset on every message like the image consent. The resolved theme comes from `useTheme().isDark` — the preference alone says nothing when it is `system`.

**Two dates, two questions.** The list shows the message's *arrival* (`InternalDate`), because arrival is what orders the list — printing the `Date` header there would put a May date among the June rows and read as a sorting bug. The reader shows the `Date` header, spelled out and without seconds, because there the question is when the sender wrote it.
- `queries.ts` — TanStack Query hooks and keys; `api/mailTypes.ts` — response shapes. `useMessageStream` sets `refetchOnWindowFocus: false`: TanStack's default refetches *every* loaded page on focus, so forty loaded blocks would mean forty IMAP connections and forty full folder sorts on switching back to the tab. `App.tsx` sets that default to `true` app-wide, so an omission here would silently reintroduce it. `useFolders` polls independently — see below

**The folder list polls so the open webmail catches up with other IMAP clients within a minute.** `useFolders` sets `refetchInterval: POLL_INTERVAL` (`queries.ts`, 60s — internal like `BLOCK_SIZE`, not a setting) rather than a bespoke timer, so one cheap LIST+STATUS answer refreshes the tree badges and drives the message-list watcher at once, for every folder, not only the open one. TanStack pauses the interval while the tab is unfocused; the app-wide focus-refetch default (`App.tsx`) is the catch-up tick on return, so no separate foreground-poll logic is needed. `MailLayout` wires the watcher in one line, `useListRefresh(folder)`, because the hook reads `useFolders()` itself rather than being handed folder data as a prop.

**`folderChanged` (`list/folderDelta.ts`) trips on four counters because each is blind to what the others miss.** Mail arriving moves `uidNext` and `total`; mail deleted from another client moves `total` but leaves `uidNext` untouched; a flag flipped elsewhere (read/unread, a Sieve rule firing) moves none of the three, since they all describe *which* messages exist, never their state — which is what `highestModSeq` (`HIGHESTMODSEQ`, RFC 7162, CONDSTORE-gated) is for. A server without CONDSTORE answers `highestModSeq: null` forever, so `moved()` requires both sides non-null before comparing: null is tolerated indefinitely, and a null-to-value transition is the server just starting to report the counter (discovery), never treated as a change.

**A `uidValidity` break is answered differently from an ordinary change, and neither path invalidates the stream.** `uidValidity` moving means the server reassigned every UID in the folder — the cache is not stale, it is wrong — so `useListRefresh` calls `resetQueries` scoped to that folder's keys (messages, message and messageStream alike), refetching only what is actually mounted. An ordinary change in streaming mode instead fetches block 0 alone and swaps it in with `setQueryData` (`refreshFirstBlock`). **Neither path ever calls `invalidateQueries` on the stream key** — TanStack's invalidate replays every loaded block, so a reader forty blocks into a folder would cost forty IMAP connections and forty full folder sorts on a single poll tick. Paged mode has no such cost, one page is one request, so there `useListRefresh` invalidates freely. Poll and refresh failures are silent by design in both modes: at a tick a minute, a toast per blip would be unbearable, and the next tick is the retry.

**Scroll is deliberately not compensated.** Rainloop and Outlook Web were both tested by hand for this slice and neither compensates: when mail lands, the rows the user is looking at shift down instead of staying pinned. The reading pane is unaffected regardless — it is keyed by `uid` (`mailKeys.message`), a query of its own, not by list position — so do not reintroduce scroll compensation as a "fix" for something neither reference client fixes either.

**Preferences** (`src/hooks/usePreferences.ts`) — shared rather than owned by the settings module, because the message list reads them too. `GET /api/Preferences` answers **every** known key with its default already filled in, so **the client keeps no copy of the defaults**: a consumer waits for the answer instead of guessing, which is why `MessageList` shows "Loading messages…" until they arrive. The stored page-size value is `'10' | '20' | '30' | '50' | '100' | 'all'`, but nothing past `usePreferences` should see the string `'all'` — `requestSizeOf` always returns a number, `BLOCK_SIZE` (100) in streaming mode, and `isStreaming` is the only reader of the raw *stored* value, so a stray `Number('all')` NaN can't be born anywhere else. The literal itself is written once, as the exported `ALL`; `GeneralPage` compares against it to word its toast, but that is the write path, reading the `<select>`'s own value rather than the preferences map. The page size is part of `mailKeys.messages` — a page fetched at 30 per page is a different thing at 100 — and changing any preference invalidates the whole `['mail']` cache for the same reason.

**Data layer** — TanStack Query, provided in `App.tsx`. This is the only module using it; the settings pages still hand-roll `useEffect`. Query keys are scoped by the active account (`['mail', accountId, …]`) from the outset, so linking a second account later isolates its cache instead of mixing two mailboxes. Folder mutations invalidate the folder tree, since each of them changes either the hierarchy or the counts it displays; the tree also refreshes on a plain poll (see above), so a change made through another client shows up too, not only one made here. A 401 is never retried — it will not succeed, and retrying only delays the redirect to `/login`.

**Rendering message HTML — three independent barriers.** The backend sanitises the body; `reader/sanitizeBody.ts` sanitises it again with DOMPurify; and it is rendered in an `<iframe sandbox="allow-popups allow-popups-to-escape-sandbox">` — never `allow-scripts`, never `allow-same-origin`. **Never render message HTML into the page itself.** The two popup permissions are load-bearing, not a loosening: a fully empty sandbox withholds navigation too, so the `target="_blank"` links the sanitiser produces silently did nothing on click, and without the escape clause the opened tab inherits the sandbox and the destination site loads broken. The two sanitising passes are not redundancy: the bug class that defeats a sanitiser is a parse divergence between it and the browser, and two passes in different engines mean a body must defeat both. Remote images arrive as `data-blocked-src` and are only restored on explicit user consent, per message — loading them tells the sender the message was opened.

**Settings module** — `SettingsLayout` (`src/modules/settings/SettingsLayout.tsx`) renders a `.context-pane` of `NavLink`s (Account / General / Linked accounts / Appearance / Folders list / Aliases / Rules / Administration — the last conditional on `isAdmin`) beside a `.settings-content` `<Outlet/>`. Module directories under `src/modules/settings/`:
- `general/` — `GeneralPage.tsx` (messages per page, message-list preview)
- `account/` — `AccountPage.tsx` (identity, other domains, quota via `QuotaBlock`, `ChangePasswordSection.tsx`)
- `appearance/` — `AppearancePage.tsx` (theme + palette radio groups, backed by `ThemeContext`)
- `mail/` — `FoldersPage.tsx` (everything about folders in one place: the full list via `FolderManager`, plus the two dialogs that act across the whole set) and `SystemFoldersModal.tsx` (system role assignment; the `<select>`s exclude the inbox, non-selectable folders, and folders already overridden for another role)
- `aliases/` — `AliasesPage.jsx` (slimmed to alias CRUD only; the old `AccountPanel` slide-in was retired in favor of `AccountPage`)
- `rules/` — `RulesPage.jsx` (Sieve rules manager, unchanged wizard/provider logic — see below)
- `admin/` — `AdminPage.jsx` (tab bar: Accounts / Domains / Virtual domains) with `AccountsTab.jsx`, `DomainsTab.jsx`, `VirtualDomainsTab.jsx` (**not** `OwnershipTab` — renamed to match the `domains/virtuals` API and "virtual alias domain" terminology), `AddEditUserModal.jsx`, `AddEditDomainModal.jsx`

Shared building blocks live above the module tree: `src/components/` (`Toasts`, `QuotaBlock`/`QuotaMini`, `DeleteConfirmModal`, `HelpTooltip`, `ComingSoon`), `src/hooks/useToasts.js`, `src/icons/` (one file per icon), `src/lib/accountIdentity.ts` (pure `deriveIdentity()` — derives email/displayName/initials/subDomains from the raw account payload; shared by `AuthContext` and previously inlined in `AliasesPage`).

`RulesPage.jsx` key components: `RuleCard`, `RuleEditorModal` (step wizard: name → conditions → actions → options), `ConditionRow`/`ActionRow`, `ConvertConfirmModal`. The **Extended rules** toggle switches provider: ON = Weesky (native, full feature set), OFF = Rainloop (Snappymail-interop, restricted) — turning OFF runs `api.checkCompatibility` first and shows `ConvertConfirmModal` for rules that would be dropped. See the repo-root `DESIGN-rules.md`.

**API client** — `src/api.js`: all backend calls go through `request()` and are exported as named methods on `api`. `BASE` is `import.meta.env.VITE_API_BASE || 'https://api.mail.weesky.net'`.

Failures throw **`ApiError`**, which extends `Error` and carries `.status` and `.code`. The code is the backend's `ResultEnveloppe` message when it is a stable string — `credentials_unavailable`, `Message not found` — so callers branch on a symbol rather than on prose. `request()` accepts `{ signal }` for cancellation, which the message list relies on so that switching folders quickly cannot race a stale response into the UI. `requestBlob()` handles binary responses (attachments) and reads the file name from `Content-Disposition`. `mailAttachmentUrl()` builds the download URL so its encoding lives in one place. **Folder paths are always `encodeURIComponent`-encoded** — they may contain `/`, `&` or `#`.

## Auth

Session state is **cookie-based**, not a localStorage bearer token — `api.js` sends `credentials: 'include'` on every `fetch` and relies on the backend's `HttpOnly` cookie. `localStorage` only holds a non-secret `sessionActive` flag (`markLoggedIn()` / `clearSession()` / `hasSession()` in `api.js`) so the SPA can synchronously decide whether to attempt rendering an authenticated route before the first API round-trip; the flag carries no auth weight by itself; a stale flag with an expired/absent cookie just gets corrected by the first 401.

`AuthContext` (`src/contexts/AuthContext.tsx`) is the source of truth for the rest of the app:
- `isLoggedIn` — seeded from `hasSession()`, flipped by `syncFromSession()` and by the 401 handler.
- `syncFromSession()` — re-reads `hasSession()`; called by `LoginRoute` right after `LoginPage`'s `onLogin` fires (i.e. after `api.login()` succeeded and called `markLoggedIn()`).
- `logout()` — calls `api.logout()` (best-effort), then always `clearSession()` + `setIsLoggedIn(false)`.
- `account` / `accountLoaded` — populated by `refreshAccount()` (calls `api.getAccount()`), which runs automatically whenever `isLoggedIn` flips true.
- `identity` — `deriveIdentity(account)` or `null` before the account loads.
- `activeAccount` / `accounts` — **multi-account scaffolding for sub-project 2**: today `accounts` always has length ≤ 1 (the primary account) and `activeAccount` mirrors it; the shape (`{ id, email, displayName, isPrimary }`) exists so `AvatarMenu`'s account-switcher list and future account-scoping logic don't need to change when linked accounts ship.
- `isAdmin` — derived from `account?.isAdmin === true` (also mirrored into `api.js`'s module-level `isAdmin` flag via `setIsAdmin`; `api.js` exposes no public getter for it — the mirror is kept solely because `AuthContext` writes it, not because anything currently reads it back).

**401 handling** — `api.js`'s `request()` calls `clearSession()` and the registered `unauthorizedHandler` on any 401. `AuthContext` registers that handler on mount, setting `isLoggedIn = false` / clearing `account`. Since every authenticated route sits under `RequireAuth`, the next render redirects to `/login` — no page needs to know about auth internals.

## Theming

Token contract lives in `src/styles/`:
- `tokens.css` — role tokens only (`--font`, `--radius-sm`, `--radius-md`, ...). **A token names a role, never a color.** Components must never hard-code a color; add a role token instead if one doesn't exist.
- `theme-night.css` / `theme-classic.css` — the two **palettes**, each defining the actual color values for `[data-palette='night']` / `[data-palette='classic']`, further overridden by `[data-palette='X'][data-theme='dark']` for the dark variant. Four total combinations: night×light, night×dark, classic×light, classic×dark.
- `shell.css` — application shell layout (topbar, rail, settings pane, etc.), consumes the role tokens.
- `mail.css` — the mail module's three columns, folder tree, list rows and reader. New CSS goes here rather than into `index.css`, which is already ~2200 lines. Contains no literal color.

Mail added ten role tokens across all four palette-mode blocks, for states a settings pane does not have: `--list-row-hover`, `--list-row-selected-bg`/`-fg`, `--list-row-unread-bg`, `--list-separator`, `--badge-count-bg`/`-fg`, `--reader-header-border`, `--quote-text`, `--attachment-chip-bg`. `--accent-unread` was provisioned in the shell slice and has consumers only since the mail module.

Palette and theme are selected via `data-palette` / `data-theme` attributes on `<html>`, persisted in `localStorage` (`appearance_palette`, `appearance_theme`; theme is `'light' | 'dark' | 'system'`). A blocking inline `<script>` in `index.html` reads both keys and sets the attributes **before first paint** (avoids a flash of the wrong theme) — it duplicates the resolution logic that `ThemeContext` also runs, deliberately, since the context only mounts after React hydrates.

`ThemeContext` (`src/contexts/ThemeContext.tsx`) owns `theme`/`palette` state, re-applies the `data-theme`/`data-palette` attributes on change, and (when `theme === 'system'`) subscribes to `matchMedia('(prefers-color-scheme: dark)')` for live OS-theme changes. `AppearancePage` is the only UI that calls `setTheme`/`setPalette`.

**Night `--action-primary` hue-shift** — deliberate: in the night palette, `--action-primary` is navy in light mode but shifts to coral in dark mode (`theme-night.css`, see the comment above the dark-mode block) because navy would dissolve into the dark background. The *role* (`--action-primary`) is stable across modes; the *hue* backing it is not. Do not "fix" this into a single fixed color.

## Testing

Test files sit next to what they test (`Foo.tsx` → `Foo.test.tsx`, `Foo.jsx` → `Foo.test.jsx`), no separate `__tests__` tree. `src/test-setup.js` is the Vitest `setupFiles` entry (jest-dom matchers). Current suite:
- `src/api.test.js` — token/session management, all `api` methods, 401 handling.
- `src/App.test.tsx`, `src/contexts/AuthContext.test.tsx`, `src/contexts/ThemeContext.test.tsx`, `src/lib/accountIdentity.test.ts`
- `src/layouts/AvatarMenu.test.tsx`, `src/modules/settings/SettingsLayout.test.tsx`
- `src/modules/settings/account/AccountPage.test.tsx`, `src/modules/settings/appearance/AppearancePage.test.tsx`, `src/modules/settings/mail/FoldersPage.test.tsx`, `src/modules/settings/mail/SystemFoldersModal.test.tsx`
- `src/modules/settings/aliases/AliasesPage.test.jsx` — alias CRUD, toasts, only the default export (see below).
- `src/modules/settings/rules/RulesPage.test.jsx` — `RuleCard`, `RuleEditorModal`, `ConvertConfirmModal`, the `isConditionValid`/`isActionValid` helpers, and the `RulesPage` default export.
- `src/modules/settings/admin/AdminPage.test.jsx` — `AdminPage`, `AccountsTab`, `DomainsTab`, `VirtualDomainsTab`, `AddEditUserModal`, `AddEditDomainModal`.
- `src/pages/LoginPage.test.jsx` — the login form. `src/pages/LoginRoute.tsx` (routing glue) has no dedicated test; it's exercised indirectly via navigation-flow tests.
- `src/components/*.test.jsx` for the extracted shared components (`Toasts`, `QuotaBlock`, `DeleteConfirmModal`).

**Named exports for tests — no longer a blanket rule.** The old convention ("every component under test carries a named `export` in addition to the default") is gone as a project-wide requirement. Shared, genuinely reusable pieces were extracted into their own files under `src/components/`/`src/hooks/`/`src/lib/` (Task 3) and are imported directly by whatever tests them — `AliasesPage.jsx` itself now has **only a default export** since its sub-components (`AccountPanel`, `ChangePasswordModal`, `QuotaBlock`, `Toasts`) were extracted or retired. Large page-local files that still bundle multiple sub-components in one module (`RulesPage.jsx`, the admin tab files) keep named exports alongside their default export purely so their tests can mount those sub-components in isolation — that's a per-file pragmatic choice now, not a house rule.

**No test lost without a replacement.** When moving/renaming a component (a `git mv`, an extraction, a retirement), the tests that covered its old behavior must keep covering it — either by moving with the file or by being folded into the destination's test file. A refactor that reduces the total assertion count on a behavior without an explicit reason is a regression, not a cleanup.

## Deployment

`npm run deploy` tarballs the project (excluding `node_modules`) and extracts it over SSH into `/var/www/admin/mail/account.frontend` on `root@curiosity.weesky.net`. Always run `npm run build` first (or just use `npm run ship`, or the `ship-frontend` skill / `/ship-frontend`).
