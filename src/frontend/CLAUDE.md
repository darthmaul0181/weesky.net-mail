# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`frontend` — a React SPA for managing email aliases on the weesky.net mail service. It talks to a backend at `https://api.mail.weesky.net`.

## Commands

```bash
npm run dev        # start Vite dev server on port 5173
npm run build      # production build → dist/
npm run preview    # preview the production build locally
npm run ship       # build + deploy to production server via SSH
```

Tests use Vitest + jsdom + `@testing-library/react`. Run with:
```bash
npm run test          # run once
npm run test -- --watch   # watch mode
```
`src/api.test.js` covers token management, all api methods, and 401 handling. `src/pages/AliasesPage.main.test.jsx` covers `Toasts`, `QuotaBlock`, `ChangePasswordModal`, `AccountPanel`, and the `AliasesPage` default export (alias CRUD, toasts, modal open/close). `src/pages/AliasesPage.admin.test.jsx` covers `AccountPanel` admin visibility, `DeleteConfirmModal`, `AddEditUserModal`, `AddEditDomainModal`, `AccountsTab`, `DomainsTab`, `OwnershipTab`, and `AdminModal`. Components under test must carry a named `export` keyword in addition to the file's default export — the test files import them individually from `AliasesPage.jsx`.

ESLint is configured (`eslint.config.js`) with `eslint-plugin-react` and `eslint-plugin-react-hooks`. Run with `npm run lint`.

## Architecture

**Auth flow** — authentication state lives as module-level state in `src/api.js`, not React context. `App.jsx` conditionally renders `LoginPage` or `AliasesPage` based on a `loggedIn` boolean. There is no React Router; navigation is purely state-driven.

**Admin state** — `src/api.js` exports `setIsAdmin(bool)` and `getIsAdmin()`. `AliasesPage` calls `setIsAdmin(data.isAdmin)` after fetching account info. `clearToken()` resets `isAdmin` to `false`. `AccountPanel` receives `isAdmin` and `onAdmin` props; when `isAdmin === true` it renders an "Administration" link that opens `AdminModal`.

**Token persistence** — `src/api.js` stores the bearer token in `localStorage` alongside an expiry timestamp. On module load it checks expiry and restores or discards the saved token. `setToken(token, expiresIn, persist)` controls persistence; `clearToken()` wipes both memory and storage.

**Unauthorized handling** — `api.js` exposes `setUnauthorizedHandler(fn)`. `App.jsx` registers a callback that sets `loggedIn = false`, so any 401 response automatically drops the user back to the login screen without the page needing to know about auth internals.

**API client** — all backend calls go through the `request()` helper in `src/api.js` and are exported as named methods on `api`. The backend base URL is the hardcoded constant `BASE` at the top of that file.

**Pages** — `AliasesPage.jsx` is the main view and contains all self-contained sub-components defined in the same file. No shared component library is used. Key components:
- `AccountPanel` — slide-in settings panel (see below)
- `ChangePasswordModal` — password change dialog
- `AdminModal` — 800px admin panel with tab sidebar (Accounts / Domains / Ownerships). Only rendered when `adminOpen === true`.
- `AccountsTab` — lists all users (email, fullname, quota), add/edit/delete via `AddEditUserModal` and `DeleteConfirmModal`
- `DomainsTab` — lists all domains, add/edit/delete via `AddEditDomainModal` and `DeleteConfirmModal`
- `OwnershipTab` — lists all extra domains with their assigned owner (or "—" if unowned); inline pencil-click edit opens a live user-search input with dropdown; clicking a result sets the owner; a delete button appears when the domain already has an owner to unlink it

## Components

### AccountPanel
Slide-in panel (fixed, right side, full height) triggered by the avatar button in the header. Closes on outside click via `mousedown` listener. Internal structure top to bottom:
- User name + primary email
- Other domains list (hidden if none)
- Storage quota bar (`QuotaBlock`)
- **Options section** (`.panel-settings`) — bordered top, contains a toggle switch for alphabetical mode
- **Actions section** (`.panel-actions`) — bordered top, contains Change password + Sign out

Section labels (e.g. "Options", "Other domains", "Storage") use class `.panel-quota-label`: `11px`, `600`, `uppercase`, `letter-spacing 0.06em`, `var(--text-muted)`.

### Alias display — flat vs alphabetical mode

User preference is stored in `localStorage` key `alias_alpha_mode` (`"true"` / `"false"`), default `false`. It survives logout because it is never cleared by `clearToken()`. The state is initialized via `useState(() => localStorage.getItem('alias_alpha_mode') === 'true')`.

**Flat mode** (`alphaMode === false`) — original layout: a single `.alias-grid` flex-wrap div, no scroll container.

**Alphabetical mode** (`alphaMode === true`) — three-layer layout:
- `.alias-view-wrapper` — `display: flex`, `max-height: calc(100vh - 260px)`, `overflow: hidden`, no border, transparent background
- `.alias-scroll-area` — `flex: 1`, `overflow-y: auto`. Scrollbar: thumb `var(--primary)`, track `transparent` (keeps Dark Reader compat — avoids white background in dark mode)
- `.alpha-nav` — 28px wide column of letter buttons, no border, right of the scrollbar

**Group headers** — each letter group has an `.alias-group-header` with the letter (`13px`, bold, `var(--text-muted)`) and a flex-1 `<div>` acting as a horizontal rule.

**Scroll ↔ letter sync** — active letter is detected in the `onScroll` handler via `getBoundingClientRect()` relative to the container top (threshold: 8px). Letter refs are stored in `groupRefs` (`useRef({})`). `scrollToLetter` uses the same `getBoundingClientRect` delta to set `container.scrollTop`. `effectiveActiveLetter` falls back to the first available letter when `activeLetter` is stale after a filter change.

**Alpha-nav hover** — `background: var(--primary)`, `color: #fff` (mirrors the header banner). Active letter: `color: var(--primary)`, bold, no background.

## Deployment

`npm run deploy` tarballs the project (excluding `node_modules`) and extracts it over SSH into `/var/www/admin/mail/account.frontend` on `root@curiosity.weesky.net`. Always run `npm run build` first (or just use `npm run ship`).
