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
- **Account** (`/api/account`) — mailbox info, quota (via the remote `doveadm` HTTP API) and password change.
- **Aliases** (`/api/aliases`) — CRUD scoped to domains owned by the caller (via `MailDomainOwnership`).

Stack: ASP.NET Core, EF Core (Pomelo MySQL), JWT Bearer, `CSharpFunctionalExtensions` (`Result<T>` pattern), `CryptSharp` (Dovecot-compatible crypt hashing), Serilog, Swashbuckle.

```bash
cd snoopy.microservice
dotnet run                    # http://localhost:5104 (Swagger UI)
dotnet build -c Release
```

See [`snoopy.microservice/DESIGN.md`](snoopy.microservice/DESIGN.md) for architecture details and [`snoopy.microservice/CLAUDE.md`](snoopy.microservice/CLAUDE.md) for commands.

## frontend

React SPA (Vite) for alias and account management. Talks to the API at `https://api.mail.weesky.net`.

- JWT authentication (bearer + cookie), state persisted in `localStorage` with expiry.
- No React Router: state-driven navigation (`LoginPage` / `AliasesPage`).
- Centralized API client in `src/api.js` (`request()` helper, `setUnauthorizedHandler` to fall back to the login screen on 401).

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

- **Frontend** → via the Claude code `ship-frontend` skipll (`/ship-frontend`)
- **Microservice** → via the Claude Code `ship-microservice` skill (`/ship-microservice`).
