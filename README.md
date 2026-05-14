# weesky.net-mail

[![CI/CD](https://github.com/darthmaul0181/weesky.net-mail/actions/workflows/deploy.yml/badge.svg)](https://github.com/darthmaul0181/weesky.net-mail/actions/workflows/deploy.yml)

Monorepo hosting the mail administration services for **weesky.net**: a .NET REST API on top of the Dovecot database and its React web interface.

## Repository layout

```
weesky.net-mail/
└── src/
    ├── snoopy.microservice/   # Backend REST API (ASP.NET Core .NET 10)
    └── frontend/              # React + Vite SPA
```

## Components

### `src/snoopy.microservice` — weesky.mail.snoopy

ASP.NET Core (.NET 10) REST API exposing admin operations on top of the `dovecot` database (MariaDB/MySQL).

Stack: ASP.NET Core, EF Core (Pomelo MySQL), JWT Bearer, `CSharpFunctionalExtensions` (`Result<T>` pattern), `CryptSharp` (Dovecot-compatible crypt hashing), Serilog, Swashbuckle.

See [`src/snoopy.microservice/DESIGN.md`](src/snoopy.microservice/DESIGN.md) for architecture details and [`src/snoopy.microservice/CLAUDE.md`](src/snoopy.microservice/CLAUDE.md) for commands.

### `src/frontend`

React SPA (Vite) for alias and account management. Talks to the API at `https://api.mail.weesky.net`.

See [`src/frontend/CLAUDE.md`](src/frontend/CLAUDE.md) for details.
