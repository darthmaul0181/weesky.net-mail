# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                    # Build the project
dotnet build -c Release         # Release build
dotnet run                      # Run on localhost:5104 (opens Swagger UI)
dotnet clean                    # Clean build artifacts
```

There are no tests in this project.

## Architecture

**Milkyway** is an ASP.NET Core (.NET 10) REST API for weesky.net mail administration. It manages email accounts, aliases, and domains backed by a Dovecot/MariaDB database.

### Layers

**Controllers** (`Controllers/`) receive HTTP requests and return `ResultEnveloppe<T>` responses via helpers in `ApiBaseController`. The three main controllers are:
- `LoginController` — `POST /api/login` (issue JWT), `DELETE /api/login` (revoke cookie)
- `AccountController` — `GET /api/account` (info), `GET /api/account/quota` (Dovecot quota), `PATCH /api/account/changesecret` (password change)
- `AliasesController` — `GET/POST/DELETE /api/aliases` (alias CRUD, scoped to caller's owned domains)

**Repositories** (`Repositories/`) handle all database access via EF Core. `UsersRepository` validates credentials and updates passwords; `AliasesRepository` lists/creates/deletes aliases and enforces domain ownership via the `MailDomainOwnership` join table.

**Services** (`Services/`) wrap external integrations. `DovecotQuotaClient` (typed `HttpClient`) calls the remote doveadm HTTP API (`quotaGet`) to retrieve live mailbox quota.

**Authentication** (`Authentication/`) configures JWT bearer + HTTP-only cookie auth. `UserAuthenticator` validates credentials; `TokenManager` + `TokenBuilder` issue signed JWTs. Token constants (issuer, audience, expiry, signing key, cookie name) come from `appsettings.json` under the `"Token"` key.

**Data** (`Data/`) contains the EF Core `ApplicationDbContext` and entity classes (`MailUser`, `MailDomain`, `MailAlias`, `MailDomainOwnership`) that map directly to the Dovecot database schema.

### Key Patterns

- **Functional error handling:** Repository and service methods return `Result<T>` / `Result` from `CSharpFunctionalExtensions`. Controllers unwrap these and call `Ok(result)` / `Problem(result)` helpers from `ApiBaseController`.
- **JWT claims:** `ClaimTypes.Upn` = username, `ClaimTypes.Dns` = domain. Controllers extract these to scope queries to the authenticated user's domain.
- **Password hashing:** Uses `CryptSharp.Core` (crypt-style hashing matching Dovecot's format).
- **Database:** MySQL via Pomelo EF Core provider, targeting the `dovecot` database. Development overrides in `appsettings.Development.json` point to `10.0.0.2`.
