# frontend

A React SPA for managing email aliases on the weesky.net mail service. It talks to the backend at `https://api.mail.weesky.net`.

## Stack

- React 18 + Vite
- Plain `fetch` through a thin API client — no data-fetching library
- No React Router; navigation is state-driven

## Commands

```bash
npm run dev        # start the Vite dev server on port 5173
npm run build      # production build → dist/
npm run preview    # preview the production build locally
npm run test       # run the Vitest suite once (--watch for watch mode)
npm run lint       # ESLint (eslint-plugin-react + react-hooks)
npm run ship       # build + deploy to production via SSH
```

Tests use Vitest + jsdom + `@testing-library/react` and live next to the code as `*.test.js`/`*.test.jsx`.

## Project layout

```
src/
  api.js          # backend client + auth token handling
  api.test.js     # api client tests
  App.jsx         # top-level LoginPage / AliasesPage switch
  main.jsx        # React entry point
  index.css
  pages/          # LoginPage, AliasesPage, RulesPage and their sub-components (+ *.test.jsx)
```

## Auth

Authentication state lives as module-level state in `src/api.js`, not in React context. The bearer token is kept in `localStorage` together with an expiry timestamp; on module load `api.js` restores it if still valid, or discards it otherwise.

`setUnauthorizedHandler(fn)` lets `App.jsx` register a callback that is invoked on any 401, sending the user back to the login screen without individual pages having to know about auth.