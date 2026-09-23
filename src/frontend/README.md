# frontend

Scotty webmail's React SPA: mail, calendar, contacts and settings for the weesky.net mail service. It talks to the backend named by `VITE_API_BASE`. `CLAUDE.md` and `docs/` hold the architecture.

## Stack

- React 18 + Vite, TypeScript throughout (`strict`, `noUncheckedIndexedAccess`, `allowJs: false`)
- React Router 7 for routing, TanStack Query for server state
- A thin `fetch` client in `src/api.ts`; the session rides on an `HttpOnly` cookie

## Commands

```bash
npm run dev        # start the Vite dev server on port 5173
npm run build      # typecheck, then production build → dist/
npm run preview    # preview the production build locally
npm run typecheck  # tsc --noEmit
npm test           # run the Vitest suite once (--watch for watch mode)
npm run lint       # ESLint, with typescript-eslint's type-checked rules
```

Tests use Vitest + jsdom + `@testing-library/react` and sit next to the code they test as `*.test.ts`/`*.test.tsx`. Building this for an installation: see `../../install/README.md` step 1.2, which sets `VITE_API_BASE` — `npm run build` fails without it.
