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
    /settings/accounts              ComingSoon ("Linked accounts" — sub-project 2)
    /settings/appearance            AppearancePage
    /settings/aliases               AliasesPage        (lazy-loaded)
    /settings/rules                 RulesPage          (lazy-loaded)
    /settings/admin  (RequireAdmin) AdminPage          (lazy-loaded)
  *                                 → redirect to /mail
```

`MailLayout`, `AliasesPage`, `RulesPage`, and `AdminPage` are `lazy()`-imported in `routes.tsx` and wrapped in `<Suspense fallback={null}>` at the route level, so the shell/settings chrome never waits on their bundle.

**Mail selection lives in search params, not route segments.** A folder path may contain `/` — the hierarchy separator is whatever the IMAP server uses — which is the same reason folder paths travel in the query string or request body on the API side rather than in a route segment. `/mail?folder=INBOX&uid=42` keeps deep links and the back button working under any separator. Choosing a folder drops `uid`, because a message id means nothing in another folder.

**`RequireAuth`** (`src/layouts/RequireAuth.tsx`) — reads `isLoggedIn` from `AuthContext`; redirects to `/login` if false, otherwise renders `<Outlet/>`. Everything except `/login` sits behind it.

**`RequireAdmin`** (`src/layouts/RequireAdmin.tsx`) — reads `isAdmin`/`accountLoaded` from `AuthContext`; renders nothing while the account is still loading (avoids a flash-redirect), then redirects non-admins to `/settings/account`.

**Shell layout** — `AppShell` (`src/layouts/AppShell.tsx`) renders `TopBar` + a body split into `AppRail` (left icon rail: Mail/Calendar/Contacts + a spacer + Settings gear, `NavLink`-driven active state) and `<main className="app-content"><Outlet/></main>`. `TopBar` (`src/layouts/TopBar.tsx`) shows the logo/wordmark and `AvatarMenu`. `AvatarMenu` (`src/layouts/AvatarMenu.tsx`) is a click-toggled dropdown (outside-click closes it via a `mousedown` listener) showing identity, the linked-accounts list (length 1 today), a Settings link, and Sign out.

**Mail module** — `MailLayout` (`src/modules/mail/MailLayout.tsx`) builds its own three columns inside the shell's single outlet, the same way `SettingsLayout` does: `.mail-folders` (folder tree plus management), `.mail-list`, `.mail-reader`. Files under `src/modules/mail/`:
- `folders/FolderTree.tsx` — hides unsubscribed folders **except the inbox** (Dovecot reports `INBOX` unsubscribed; the flag is meaningless for a folder that is always available), orders well-known ones first (inbox, drafts, sent, archive, junk, trash), refuses to select a container-only folder, and suppresses the unread badge on trash and junk — an unread count there prompts reading nobody wants to do
- `folders/FolderDialogs.tsx` — create / rename / delete / visibility. Derives a parent path by stripping the leaf name, which works under any separator because the backend rejects names containing one. Reuses `DeleteConfirmModal`
- `list/MessageList.tsx` + `list/formatDate.ts` — paginated rows under a sticky folder heading; the page index resets when the folder changes. The preview element is always rendered even when empty, so a bodyless message does not make a shorter row than its neighbours
- `reader/MessageReader.tsx`, `reader/sanitizeBody.ts`, `reader/formatReaderDate.ts`, `reader/formatSize.ts`

**Two dates, two questions.** The list shows the message's *arrival* (`InternalDate`), because arrival is what orders the list — printing the `Date` header there would put a May date among the June rows and read as a sorting bug. The reader shows the `Date` header, spelled out and without seconds, because there the question is when the sender wrote it.
- `queries.ts` — TanStack Query hooks and keys; `api/mailTypes.ts` — response shapes

**Data layer** — TanStack Query, provided in `App.tsx`. This is the only module using it; the settings pages still hand-roll `useEffect`. Query keys are scoped by the active account (`['mail', accountId, …]`) from the outset, so linking a second account later isolates its cache instead of mixing two mailboxes. Folder mutations invalidate the folder tree, since each of them changes either the hierarchy or the counts it displays. A 401 is never retried — it will not succeed, and retrying only delays the redirect to `/login`.

**Rendering message HTML — three independent barriers.** The backend sanitises the body; `reader/sanitizeBody.ts` sanitises it again with DOMPurify; and it is rendered in an `<iframe sandbox="allow-popups allow-popups-to-escape-sandbox">` — never `allow-scripts`, never `allow-same-origin`. **Never render message HTML into the page itself.** The two popup permissions are load-bearing, not a loosening: a fully empty sandbox withholds navigation too, so the `target="_blank"` links the sanitiser produces silently did nothing on click, and without the escape clause the opened tab inherits the sandbox and the destination site loads broken. The two sanitising passes are not redundancy: the bug class that defeats a sanitiser is a parse divergence between it and the browser, and two passes in different engines mean a body must defeat both. Remote images arrive as `data-blocked-src` and are only restored on explicit user consent, per message — loading them tells the sender the message was opened.

**Settings module** — `SettingsLayout` (`src/modules/settings/SettingsLayout.tsx`) renders a `.context-pane` of `NavLink`s (Account / Linked accounts / Appearance / Aliases / Rules / Administration — the last conditional on `isAdmin`) beside a `.settings-content` `<Outlet/>`. Module directories under `src/modules/settings/`:
- `account/` — `AccountPage.tsx` (identity, other domains, quota via `QuotaBlock`, `ChangePasswordSection.tsx`)
- `appearance/` — `AppearancePage.tsx` (theme + palette radio groups, backed by `ThemeContext`)
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
- `src/modules/settings/account/AccountPage.test.tsx`, `src/modules/settings/appearance/AppearancePage.test.tsx`
- `src/modules/settings/aliases/AliasesPage.test.jsx` — alias CRUD, toasts, only the default export (see below).
- `src/modules/settings/rules/RulesPage.test.jsx` — `RuleCard`, `RuleEditorModal`, `ConvertConfirmModal`, the `isConditionValid`/`isActionValid` helpers, and the `RulesPage` default export.
- `src/modules/settings/admin/AdminPage.test.jsx` — `AdminPage`, `AccountsTab`, `DomainsTab`, `VirtualDomainsTab`, `AddEditUserModal`, `AddEditDomainModal`.
- `src/pages/LoginPage.test.jsx` — the login form. `src/pages/LoginRoute.tsx` (routing glue) has no dedicated test; it's exercised indirectly via navigation-flow tests.
- `src/components/*.test.jsx` for the extracted shared components (`Toasts`, `QuotaBlock`, `DeleteConfirmModal`).

**Named exports for tests — no longer a blanket rule.** The old convention ("every component under test carries a named `export` in addition to the default") is gone as a project-wide requirement. Shared, genuinely reusable pieces were extracted into their own files under `src/components/`/`src/hooks/`/`src/lib/` (Task 3) and are imported directly by whatever tests them — `AliasesPage.jsx` itself now has **only a default export** since its sub-components (`AccountPanel`, `ChangePasswordModal`, `QuotaBlock`, `Toasts`) were extracted or retired. Large page-local files that still bundle multiple sub-components in one module (`RulesPage.jsx`, the admin tab files) keep named exports alongside their default export purely so their tests can mount those sub-components in isolation — that's a per-file pragmatic choice now, not a house rule.

**No test lost without a replacement.** When moving/renaming a component (a `git mv`, an extraction, a retirement), the tests that covered its old behavior must keep covering it — either by moving with the file or by being folded into the destination's test file. A refactor that reduces the total assertion count on a behavior without an explicit reason is a regression, not a cleanup.

## Deployment

`npm run deploy` tarballs the project (excluding `node_modules`) and extracts it over SSH into `/var/www/admin/mail/account.frontend` on `root@curiosity.weesky.net`. Always run `npm run build` first (or just use `npm run ship`, or the `ship-frontend` skill / `/ship-frontend`).
