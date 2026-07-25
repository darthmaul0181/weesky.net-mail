# weesky.net — mail

Mail administration services for weesky.net: a REST API backed by the Dovecot database and its React web interface.

## Layout

```
src/
├── snoopy.microservice/   # Backend REST API (ASP.NET Core .NET 10)
└── frontend/              # React + Vite SPA
```

## snoopy.microservice — weesky.mail.snoopy

ASP.NET Core (.NET 10) REST API on top of the `dovecot` database (MariaDB/MySQL).

- **Login** (`/api/login`) — issues a signed JWT, also set as an `HttpOnly; Secure; SameSite=Strict` cookie. Rate limited (5 req/min/IP).
- **Account** (`/api/account`) — mailbox info, quota (via the remote `doveadm` HTTP API), IMAP folder list, and password change.
- **Aliases** (`/api/aliases`) — CRUD scoped to domains owned by the caller (via `MailDomainOwnership`).
- **Admin** (`/api/Admin`) — admin-only CRUD for users, domains, and alias domain ownerships.
- **Rules** (`/api/Rules`) — Sieve mail-filtering rules over the ManageSieve protocol, with a Weesky native provider and a Rainloop/Snappymail-interop provider (see [`/DESIGN-rules.md`](../DESIGN-rules.md)).

Stack: ASP.NET Core, EF Core (Pomelo MySQL), JWT Bearer, `CSharpFunctionalExtensions` (`Result<T>` pattern), Serilog, Swashbuckle. Passwords are stored plaintext — MariaDB triggers apply the Dovecot-compatible SHA-512 crypt encryption.

```bash
cd snoopy.microservice
dotnet run                    # http://localhost:5104 (Swagger UI)
dotnet build -c Release
```

See [`snoopy.microservice/DESIGN.md`](snoopy.microservice/DESIGN.md) for architecture details and [`snoopy.microservice/CLAUDE.md`](snoopy.microservice/CLAUDE.md) for commands.

## frontend

React SPA (Vite) webmail shell — Mail/Calendar/Contacts modules (Mail/Calendar/Contacts are placeholder pages today) plus a settings area (account, appearance, aliases, mail rules, admin). Talks to the API at `https://api.mail.weesky.net`.

- `react-router-dom` routing (`AppShell` + `SettingsLayout`), route guards for auth (`RequireAuth`) and admin (`RequireAdmin`).
- Cookie-based session (`HttpOnly` cookie set by the backend); `AuthContext` tracks login/account state, prepared for multi-account (sub-project 2).
- Token-based theming (`src/styles/`) with two palettes (night/classic) × two modes (light/dark), toggled from `/settings/appearance`.
- Centralized API client in `src/api.js` (`request()` helper, `setUnauthorizedHandler` to fall back to the login screen on 401).
- Sieve rules manager (`RulesPage`, at `/settings/rules`) with a step-by-step rule editor and an Extended-rules toggle (Weesky vs Rainloop/Snappymail interop).
- Tested with Vitest + Testing Library (`npm run test`); linted with ESLint (`npm run lint`); typechecked with `npm run typecheck`.

```bash
cd frontend
npm run dev       # dev server on port 5173
npm run build     # production build → dist/
npm run preview   # preview the production build
npm run ship      # build + SSH deployment
```

See [`frontend/CLAUDE.md`](frontend/CLAUDE.md) for details.

## Deployment

Both components ship to weesky.net's webserver:

- **Frontend** → via the Claude Code `ship-frontend` skill (`/ship-frontend`)
- **Microservice** → via the Claude Code `ship-microservice` skill (`/ship-microservice`).
