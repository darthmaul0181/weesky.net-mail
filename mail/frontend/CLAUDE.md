# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`mailadmin-frontend` — a React SPA for managing email aliases on the weesky.net mail service. It talks to a backend at `https://api.mail.weesky.net`.

## Commands

```bash
npm run dev        # start Vite dev server on port 5173
npm run build      # production build → dist/
npm run preview    # preview the production build locally
npm run ship       # build + deploy to production server via SSH
```

There are no tests and no linter configured.

## Architecture

**Auth flow** — authentication state lives as module-level state in `src/api.js`, not React context. `App.jsx` conditionally renders `LoginPage` or `AliasesPage` based on a `loggedIn` boolean. There is no React Router; navigation is purely state-driven.

**Token persistence** — `src/api.js` stores the bearer token in `localStorage` alongside an expiry timestamp. On module load it checks expiry and restores or discards the saved token. `setToken(token, expiresIn, persist)` controls persistence; `clearToken()` wipes both memory and storage.

**Unauthorized handling** — `api.js` exposes `setUnauthorizedHandler(fn)`. `App.jsx` registers a callback that sets `loggedIn = false`, so any 401 response automatically drops the user back to the login screen without the page needing to know about auth internals.

**API client** — all backend calls go through the `request()` helper in `src/api.js` and are exported as named methods on `api`. The backend base URL is the hardcoded constant `BASE` at the top of that file.

**Pages** — `AliasesPage.jsx` is the main view and contains several self-contained sub-components (`AccountPanel`, `ChangePasswordModal`, `Toasts`) defined in the same file. No shared component library is used.

## Deployment

`npm run deploy` tarballs the project (excluding `node_modules`) and extracts it over SSH into `/var/www/admin/mail/account.frontend` on `root@curiosity.weesky.net`. Always run `npm run build` first (or just use `npm run ship`).
