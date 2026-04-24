![Logo](./assets/weesky_net.png)
# weesky.net

Monorepo hosting the mail administration services for **weesky.net**: a .NET REST API on top of the Dovecot database and its React web interface.

## Repository layout

```
weesky.net/
└── mail/
    ├── snoopy.microservice/   # Backend REST API (ASP.NET Core .NET 10)
    └── frontend/              # React + Vite SPA (mailadmin-frontend)
```

## Components

### `mail/snoopy.microservice` — weesky.mail.snoopy

ASP.NET Core (.NET 10) REST API exposing admin operations on top of the `dovecot` database (MariaDB/MySQL).

Stack: ASP.NET Core, EF Core (Pomelo MySQL), JWT Bearer, `CSharpFunctionalExtensions` (`Result<T>` pattern), `CryptSharp` (Dovecot-compatible crypt hashing), Serilog, Swashbuckle.

See [`mail/snoopy.microservice/DESIGN.md`](mail/snoopy.microservice/DESIGN.md) for architecture details and [`mail/snoopy.microservice/CLAUDE.md`](mail/snoopy.microservice/CLAUDE.md) for commands.

### `mail/frontend` — mailadmin-frontend

React SPA (Vite) for alias and account management. Talks to the API at `https://api.mail.weesky.net`.

See [`mail/frontend/CLAUDE.md`](mail/frontend/CLAUDE.md) for details.